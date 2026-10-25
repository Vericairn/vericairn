// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Two local publications and declared front documents; all storage belongs to the test fixtures.</summary>
public sealed class DocumentPassFixture : IDisposable
{
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    private static readonly string[] DecisionEffects = ["Revise the guide."];
    private static readonly string[] DecisionDocuments = ["docs/guide.md"];
    public TestEstate Estate { get; } = new(recordFormat: 2);
    public TestEstate Repository { get; } = new(recordFormat: 2);
    public EstateService Service { get; }
    public RecordEnvelope Product { get; }
    public RecordEnvelope Resource { get; }
    public RecordEnvelope First { get; }
    public RecordEnvelope Second { get; }
    public RecordEnvelope Readme { get; }
    public RecordEnvelope Guide { get; }
    public RecordEnvelope Cleared { get; }
    public RecordEnvelope Unrelated { get; }
    public RecordEnvelope Decision { get; }
    public string FirstCommit { get; }
    public string SecondCommit { get; }

    public DocumentPassFixture(string newline = "\n", bool finalNewline = true)
    {
        Git("init", "--initial-branch=main");
        Git("config", "core.autocrlf", "false");
        Repository.WriteFile("README.md", "# Widget" + newline + newline + "The current published release is 0.1.0, from" + newline + "October 13, 2026." + (finalNewline ? newline : ""));
        Repository.WriteFile("ROADMAP.md", "# Roadmap" + newline + newline + "## 0.1.0 — current published release" + newline + newline + "Earlier work." + newline + newline + "## 0.2.0 — planned" + newline + newline + "New work." + newline);
        Repository.WriteFile("docs/releases/README.md", "# Releases" + newline + newline + "The current published release is **0.1.0**, published on October 13, 2026." + newline + "Historical details stay here." + newline);
        Repository.WriteFile("docs/releases/0.1.0/README.md", "Frozen first set.\n");
        Repository.WriteFile("docs/releases/frozen-sha256.json", "{\"fixture\":\"unchanged\"}\n");
        Repository.WriteFile("src/changed.txt", "before\n");
        Repository.WriteFile("src/deleted.txt", "deleted later\n");
        Repository.WriteFile("docs/cleared.md", "Already reviewed.\n");
        Repository.WriteFile("docs/unrelated.md", "Content only.\n");
        Git("add", "--all");
        Git("commit", "-m", "First fixture publication");
        FirstCommit = Git("rev-parse", "HEAD").Trim();
        Product = Estate.Add("product", Estate.OperatorId, """{"name":"Widget","slug":"widget","summary":"Fixture product.","editions":[{"name":"Community","slug":"community","summary":"Fixture."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Fixture."}]}""");
        Resource = Estate.Add("resource", Estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Fixture sources.","location":{"github":"Example/widget"},"visibility":"private"}""");
        RepositoryClones.Register(Estate.Estate.Id, Resource.Id, Repository.Root);
        DocumentSources.Register(new GitFileSource());
        Readme = Declare("README.md", covers: true);
        Declare("ROADMAP.md");
        Declare("docs/releases/README.md");
        Cleared = Declare("docs/cleared.md");
        Unrelated = Declare("docs/unrelated.md", releaseTrigger: false);
        Estate.WriteFile("docs/guide.md", "# Guide\n");
        Guide = Declare("docs/guide.md", estateFile: true);
        First = Publish("0.1.0", FirstCommit);
        foreach (RecordEnvelope declaration in Estate.Load().Current("living-document")) Clear(declaration, FirstCommit);
        Decision = Estate.Add("decision", Estate.OperatorId, JsonSerializer.Serialize(new
        {
            title = "Explain the new behavior", statement = "Revise the guide.", summary = "The guide explains the added command.", synthesis = "Explain the added command.",
            stake = "low", notAuthorized = Array.Empty<string>(),
            effects = DecisionEffects, affects = Array.Empty<object>(), decidedBy = Estate.OperatorId,
            decidedAt = Estate.Clock.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), documents = DecisionDocuments,
        }));
        Repository.WriteFile("src/changed.txt", "after\n");
        Repository.WriteFile("src/added.txt", "added\n");
        File.Delete(Path.Combine(Repository.Root, "src", "deleted.txt"));
        Repository.WriteFile("elsewhere.txt", "outside covered paths\n");
        Repository.WriteFile("docs/unrelated.md", "Changed but no release trigger.\n");
        Git("add", "--all");
        Git("commit", "-m", "Second fixture publication");
        SecondCommit = Git("rev-parse", "HEAD").Trim();
        Second = Publish("0.2.0", SecondCommit);
        Clear(Cleared, SecondCommit);
        EstateModel model = Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Service = EstateService.Open(Estate.Root);
    }

    public RecordEnvelope Declare(string path, bool covers = false, bool estateFile = false, bool releaseTrigger = true, string? product = null, string? check = null)
    {
        var body = new JsonObject
        {
            ["path"] = path, ["title"] = path, ["kind"] = "authored", ["source"] = estateFile ? "estate-file" : GitFileSource.Name,
            ["summary"] = "Fixture document.", ["clearedBy"] = "independent", ["triggers"] = new JsonArray(),
        };
        if (!estateFile) body["repository"] = Resource.Id;
        if (releaseTrigger) ((JsonArray)body["triggers"]!).Add(new JsonObject { ["on"] = "release", ["level"] = "refuse", ["product"] = product ?? Product.Id });
        if (covers) ((JsonArray)body["triggers"]!).Add(new JsonObject { ["on"] = "covers", ["level"] = "warn", ["paths"] = new JsonArray("src") });
        if (!releaseTrigger) ((JsonArray)body["triggers"]!).Add(new JsonObject { ["on"] = "decision", ["level"] = "warn" });
        if (check is not null) body["checks"] = new JsonArray(check);
        return Estate.Add("living-document", Estate.OperatorId, body.ToJsonString());
    }

    public RecordEnvelope Publish(string version, string commit, RecordEnvelope? product = null)
    {
        var body = new JsonObject
        {
            ["version"] = version, ["repository"] = "Example/widget", ["commit"] = commit, ["status"] = "candidate",
            ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture.zip", ["hash"] = "sha256:" + new string('a', 64), ["bytes"] = 1, ["platform"] = "any" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2), ["writes"] = 2 },
            ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "Fixture.", ["limitations"] = new JsonArray(),
        };
        RecordReference subject = new("product", (product ?? Product).Id, null);
        RecordEnvelope candidate = Estate.Add("release", Estate.OperatorId, body.ToJsonString(), subject);
        Estate.Add("acceptance", Estate.OperatorId, """{"statement":"Accepted fixture.","basis":[],"self":true}""", candidate.Reference);
        body["status"] = "published";
        body["publishedAt"] = Estate.Clock.AddSeconds(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        body["url"] = "https://example.invalid/releases/" + version;
        return Estate.Add("release", Estate.OperatorId, body.ToJsonString(), subject, candidate.Id);
    }

    public void Clear(RecordEnvelope declaration, string commit)
    {
        bool estateFile = EstateModel.BodyString(declaration, "repository") is null;
        string path = EstateModel.BodyString(declaration, "path")!;
        byte[] bytes = estateFile ? File.ReadAllBytes(Path.Combine(Estate.Root, path)) : new GitFileSource().Read(Estate.Load(), declaration, commit)!;
        var content = new JsonObject { ["hash"] = ContentHash.Sha256(bytes), ["bytes"] = bytes.Length };
        if (!estateFile) content["commit"] = commit;
        var body = new JsonObject
        {
            ["outcome"] = "current", ["content"] = content, ["answers"] = new JsonArray(), ["independence"] = "operator",
            ["summary"] = "Checked fixture.", ["findings"] = new JsonArray(), ["checks"] = new JsonArray(),
        };
        if (LivingDocuments.Triggers(declaration).Any(t => t.On == "covers"))
            body["covered"] = new GitFileSource().Covered(Estate.Load(), declaration, ["src"], commit);
        Estate.Add("document-review", Estate.OperatorId, body.ToJsonString(), declaration.Reference);
    }

    public string Git(params string[] arguments) => GitInput(null, arguments);

    public string GitInput(string? input, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Repository.Root, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, UseShellExecute = false };
        foreach (string option in GitOptions.Concat(arguments)) start.ArgumentList.Add(option);
        using Process process = Process.Start(start)!;
        if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000), "Fixture git command exceeded its deadline.");
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Service.Dispose();
        Repository.Dispose();
        Estate.Dispose();
    }
}
