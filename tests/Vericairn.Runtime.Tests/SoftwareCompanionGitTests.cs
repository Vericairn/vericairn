// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Packs.Software;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

public sealed class SoftwareCompanionGitTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "software-companion-git-" + Guid.NewGuid().ToString("N"));
    private readonly string repository;
    private readonly string home;

    public SoftwareCompanionGitTests()
    {
        repository = Path.Combine(scratch, "repository");
        home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "gitconfig"), "");
        Git("init", "--initial-branch=main");
        SoftwareCompanions.Register();
    }

    [Fact]
    public void CandidateCommitUsesItsOwnCompleteInputsAndInventoryEvenWhenCloneHeadDiffers()
    {
        using var fixture = new PolicyFixture();
        var source = new NamedSource(new GitFileSource(repository, SoftwareCompanionSources.Select));
        Assert.True(DocumentSources.Register(source));
        RecordEnvelope resource = fixture.Estate.Add("resource", fixture.Estate.OperatorId,
            """{"kind":"repository","name":"Fixture","role":"source","location":{"github":"Example/example"},"visibility":"private"}""");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = "1", ["audience"] = "Internal", ["selection"] = "list", ["build-commit"] = SoftwareCompanionTests.Commit, ["source"] = "SHA256SUMS", ["artifacts"] = "none",
        };
        RecordEnvelope declaration = fixture.Add("living-document", new JsonObject
        {
            ["path"] = "docs/set/checksums.md", ["title"] = "Retained artifact digests", ["kind"] = "generated", ["repository"] = resource.Id,
            ["source"] = source.Id, ["triggers"] = new JsonArray(), ["summary"] = "The retained build's exact checksum claims.",
            ["generator"] = new JsonObject { ["id"] = "vericairn.software/checksums", ["inputs"] = new JsonArray(parameters.Select(p => (JsonNode)(p.Key + "=" + p.Value)).ToArray()) },
        });
        string firstList = new string('b', 64) + "  fixture.zip\n";
        DocumentRendition first = Set(firstList, parameters);
        Write("docs/set/checksums.md", first.Bytes.ToArray());
        string candidate = Commit("Retain the first build artifact");

        DocumentRendition later = Set(new string('c', 64) + "  changed.zip\n", parameters);
        string newer = Commit("Change the retained checksum input");
        Assert.NotEqual(candidate, newer);
        EstateModel model = fixture.Estate.Load();
        Assert.Equal(newer, source.Head(model, declaration));
        GeneratedDocumentPlan selected = GeneratedDocumentVerification.Plan(model, declaration, candidate);
        Assert.True(selected.Current, string.Join('\n', selected.Problems));
        Assert.Equal(candidate, selected.Commit);
        Assert.Equal(first.Bytes, Assert.Single(selected.Expected).Bytes);
        Assert.Equal(first.Bytes, Assert.Single(selected.Actual).Bytes);
        GeneratedDocumentPlan stale = GeneratedDocumentVerification.Plan(model, declaration, newer);
        Assert.False(stale.Current);
        Assert.Equal(DocumentGenerationStatus.Success, stale.Status);
        Assert.Equal(later.Bytes, Assert.Single(stale.Expected).Bytes);
        Assert.Equal(first.Bytes, Assert.Single(stale.Actual).Bytes);
        Assert.Equal(DocumentGenerationStatus.Unavailable, GeneratedDocumentVerification.Plan(model, declaration, new string('f', 40)).Status);
        Assert.Equal(DocumentGenerationStatus.Unavailable, GeneratedDocumentVerification.Plan(model, declaration).Status);

        // An inventory digest cannot excuse stale source bytes, even when the candidate commit exists.
        Write("docs/set/SHA256SUMS", Encoding.UTF8.GetBytes(firstList));
        string inconsistent = Commit("Retain an inconsistent source inventory");
        Assert.Equal(DocumentGenerationStatus.Unavailable, GeneratedDocumentVerification.Plan(model, declaration, inconsistent).Status);
        Set(firstList, parameters);
        JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, "docs/set/manifest.json")))!;
        manifest["companions"]!["inputs"]!.AsArray().Add(new JsonObject { ["path"] = "unused.txt", ["hash"] = "sha256:" + new string('d', 64), ["bytes"] = 1 });
        Write("docs/set/manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        string unowned = Commit("Retain an input outside the declared output selection");
        Assert.Equal(DocumentGenerationStatus.Unavailable, GeneratedDocumentVerification.Plan(model, declaration, unowned).Status);
        Assert.True(GeneratedDocumentVerification.Plan(model, declaration, candidate).Current);
        Assert.Equal(unowned, Git("rev-parse", "HEAD"));
        Assert.Empty(Git("status", "--porcelain"));
    }

    private DocumentRendition Set(string checksums, Dictionary<string, string> parameters)
    {
        DocumentSourceSnapshot input = SoftwareCompanionTests.File("SHA256SUMS", checksums);
        DocumentRendition rendition = SoftwareCompanionTests.Render("checksums", "SHA256SUMS", input);
        Assert.Equal(DocumentGenerationStatus.Success, rendition.Status);
        var generator = SoftwareCompanions.Generators.Single(g => g.Id == "vericairn.software/checksums");
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            commit = SoftwareCompanionTests.Commit,
            companions = new
            {
                format = "vericairn.software-companions/1", scope = "artifacts", repository = "fixture-repository",
                implementation = generator.Descriptor!.Implementation, implementationHash = generator.Descriptor.ImplementationHash,
                inputs = new[] { new { path = input.Path, hash = input.Hash, bytes = input.Bytes.Length } },
                outputs = new[] { new { path = "checksums.md", generator = generator.Id, contract = 1, source = "SHA256SUMS", inputDigest = rendition.InputDigest,
                    inputs = parameters.Where(p => p.Key == "source").Select(p => p.Value), parameters } },
            },
        });
        Write("docs/set/manifest.json", manifest);
        Write("docs/set/SHA256SUMS", input.Bytes.ToArray());
        return rendition;
    }

    private string Commit(string message) { Git("add", "docs"); Git("commit", "-m", message); return Git("rev-parse", "HEAD"); }
    private void Write(string path, byte[] bytes)
    {
        string full = Path.Combine(repository, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }
    private string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string option in new[] { "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgSign=false", "-c", "core.hooksPath=" + home }) start.ArgumentList.Add(option);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "gitconfig");
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new InvalidOperationException("The fixture git operation timed out."); }
        Assert.True(process.ExitCode == 0, error.Result);
        return output.Result.Trim();
    }
    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private sealed class NamedSource(GitFileSource source) : IDocumentSource
    {
        public string Id { get; } = "example.companions/source-" + Guid.NewGuid().ToString("N");
        public int SnapshotVersion => source.SnapshotVersion;
        public string? Head(EstateModel model, RecordEnvelope declaration) => source.Head(model, declaration);
        public string? Resolve(EstateModel model, RecordEnvelope declaration, string reference) => source.Resolve(model, declaration, reference);
        public byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit) => throw new InvalidOperationException("A complete snapshot is required.");
        public string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit) => null;
        public bool Exists(EstateModel model, RecordEnvelope declaration, string commit, string path) => false;
        public DocumentSourceSelection ReadSelection(EstateModel model, RecordEnvelope declaration, string commit, DocumentGeneratorDescriptor generator) => source.ReadSelection(model, declaration, commit, generator);
        public DocumentFileInventory ReadInventory(EstateModel model, RecordEnvelope declaration, string commit) => source.ReadInventory(model, declaration, commit);
    }
}
