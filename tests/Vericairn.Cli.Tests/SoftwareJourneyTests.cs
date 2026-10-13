// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Cli.Tests;

/// <summary>The software journeys through the CLI: J02 adoption by observation, J01 complete with checks as evidence, J04 the direct-download route as intent then observed effect.</summary>
[Collection("cli")]
public class SoftwareJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private const string Brief = """{"outcome":"A widget that builds and passes its tests.","sources":[],"permittedChanges":["the widget sources and tests"],"prohibitedEffects":["no publication"],"workspace":["the widget repository, main branch"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["tests pass"],"recovery":"Record a result before stopping."}""";

    [Fact]
    public void J02_AdoptAnExistingRepositoryWithoutDisruptingCurrentWork()
    {
        string root = CliHarness.TempDirectory("j02");
        string repo = CliHarness.TempDirectory("j02-repo");
        try
        {
            string op = Init(root);
            Git(repo, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repo, "README.md"), "widget\n");
            Git(repo, "add", "README.md");
            Git(repo, "commit", "-q", "-m", "First commit");
            File.AppendAllText(Path.Combine(repo, "README.md"), "unpublished change\n");
            File.WriteAllText(Path.Combine(repo, "scratch.txt"), "untracked\n");

            // Read-only inspection first: the working tree is reported and untouched, and no remote account is needed.
            CliHarness.Outcome inspect = CliHarness.Ok("sources", "inspect", repo);
            Assert.Contains("1 modified, 1 untracked", inspect.Out, StringComparison.Ordinal);
            Assert.Contains("remote      none", inspect.Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "sources", "inspect", Path.Combine(repo, "..")).Code == 0 ? 1 : 0);

            // Adoption: a resource and an observation, nothing in the repository changes, no path enters the estate.
            CliHarness.Outcome adopt = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", repo, "--by", op, "--name", "Widget repository", "--role", "source of the widget");
            string resource = adopt.Json.GetProperty("resource").GetProperty("id").GetString()!;
            Assert.False(adopt.Json.GetProperty("clean").GetBoolean());
            Assert.Equal("widget\nunpublished change\n", File.ReadAllText(Path.Combine(repo, "README.md")));
            Assert.True(File.Exists(Path.Combine(repo, "scratch.txt")));
            string resourceFile = File.ReadAllText(Path.Combine(root, adopt.Json.GetProperty("resource").GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("\"rootCommit\"", resourceFile, StringComparison.Ordinal);
            Assert.DoesNotContain(repo, resourceFile, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.Contains("Resources: 1", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // Work continues; a fresh observation records the new commit against the same resource.
            File.WriteAllText(Path.Combine(repo, "widget.txt"), "widget\n");
            Git(repo, "add", "widget.txt");
            Git(repo, "commit", "-q", "-m", "Add the widget");
            CliHarness.Outcome observed = CliHarness.Ok("--estate", root, "--json", "sources", "observe", repo, "--resource", resource, "--by", op);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, "records", "observation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.NotNull(observed.Id);
            Assert.Equal(2, CliHarness.Run("--estate", root, "--json", "sources", "observe", repo, "--resource", "01NOPE", "--by", op).Code);
        }
        finally
        {
            CliHarness.Delete(root, repo);
        }
    }

    [Fact]
    public void J01_Complete_ChecksAreEvidenceAndTheExactResultIsExported()
    {
        string root = CliHarness.TempDirectory("j01c");
        string repo = CliHarness.TempDirectory("j01c-repo");
        string scratch = CliHarness.TempDirectory("j01c-scratch");
        try
        {
            string op = Init(root);
            string builder = Principal(root, op, scratch, "Builder", "builder");
            Git(repo, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repo, "widget.txt"), "widget\n");
            Git(repo, "add", "widget.txt");
            Git(repo, "commit", "-q", "-m", "The widget");
            string commit = GitOutput(repo, "rev-parse", "HEAD");

            CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product));
            string resource = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", repo, "--by", op, "--name", "Widget repository", "--role", "source of the widget").Json.GetProperty("resource").GetProperty("id").GetString()!;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Deliver.","scope":["widget"],"exitQuestion":"Does it pass?","inputs":[],"deliverables":["widget.txt"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", Brief)).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "the widget repository, main branch", "--note", "Build and check.").Id;

            // A check that passes and one that fails are both evidence of the exact commit; the failure is recorded, not hidden.
            CliHarness.Outcome passed = CliHarness.Ok("--estate", root, "--json", "check", "run", "--resource", resource, "--by", builder, "--in", repo, "--summary", "The widget is present.", "--", "git", "status", "--short");
            Assert.Equal("passed", passed.Json.GetProperty("outcome").GetString());
            Assert.Equal(commit, passed.Json.GetProperty("commit").GetString());
            CliHarness.Outcome failed = CliHarness.Ok("--estate", root, "--json", "check", "run", "--resource", resource, "--by", builder, "--in", repo, "--", "git", "rev-parse", "--verify", "no-such-ref");
            Assert.Equal("failed", failed.Json.GetProperty("outcome").GetString());
            Assert.NotEqual(0, failed.Json.GetProperty("exitCode").GetInt32());
            Assert.Contains("Verifications: 2", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            string verification = File.ReadAllText(Path.Combine(root, passed.Json.GetProperty("verification").GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain(repo, verification, StringComparison.Ordinal);
            Assert.Contains("\"method\": \"command\"", verification, StringComparison.Ordinal);

            // The exact widget is stored as an object and named by the result; the acceptance is separate.
            string widget = Path.Combine(repo, "widget.txt");
            string hash = ContentHash.Sha256File(widget);
            CliHarness.Outcome stored = CliHarness.Ok("--estate", root, "--json", "objects", "put", widget);
            Assert.Equal(hash, stored.Json.GetProperty("hash").GetString());
            CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, "result.json", $$"""{"outcome":"completed","summary":"Built and checked the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7}],"evidence":[{"summary":"Verification {{passed.Json.GetProperty("verification").GetProperty("id").GetString()}} passed at {{commit[..12]}}."}],"checks":[{"name":"git status","outcome":"passed","summary":"Clean."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{result.Id}:{result.Hash}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted the exact result.","basis":[],"self":false}"""));
            string export = Path.Combine(scratch, "widget-estate.zip");
            CliHarness.Outcome exported = CliHarness.Ok("--estate", root, "--json", "estate", "export", "--out", export, "--by", op);
            Assert.Equal(2, exported.Json.GetProperty("Objects").GetInt32());
            Assert.Contains(hash, CliHarness.Ok("--estate", root, "objects", "list").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.Contains("completed, accepted", CliHarness.Ok("--estate", root, "work", "observe", "W-1").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, repo, scratch);
        }
    }

    [Fact]
    public void J04_DirectDownloadRoute_IntentBeforeEffectAndManualPublication()
    {
        string root = CliHarness.TempDirectory("j04");
        string scratch = CliHarness.TempDirectory("j04-scratch");
        IReleaseProvider previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            string op = Init(root);
            string builder = Principal(root, op, scratch, "Builder", "builder");
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
            string resource = CliHarness.Ok("--estate", root, "--json", "records", "new", "resource", "--by", op, "--body", CliHarness.WriteJson(scratch, "resource.json", """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"public"}""")).Id;

            // Submit: the intent is a record before the dispatch; the run is recorded as observed.
            CliHarness.Outcome submitted = CliHarness.Ok("--estate", root, "--json", "release", "submit", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal("dispatched", submitted.Json.GetProperty("status").GetString());
            string run = submitted.Json.GetProperty("run").GetString()!;
            Assert.Single(provider.Dispatches);
            Assert.Equal("0.2.0", provider.Dispatches[0].Inputs["version"]);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.Contains("github-releases:Example/widget:v0.2.0  dispatched", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // A delayed decision leaves it pending: nothing new is recorded, and a second submit is refused until reconciled.
            CliHarness.Outcome pending = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Contains("in progress", pending.Json.GetProperty("pending").GetString(), StringComparison.Ordinal);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.Equal(2, CliHarness.Run("--estate", root, "release", "submit", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Code);

            // The run succeeds and the draft appears: the operation is observed-success and the candidate is recorded with every distributable by hash.
            provider.CompleteRun("Example/widget", run, true, "0.2.0");
            CliHarness.Outcome built = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder, "--qualified", "linux-x64", "--limitation", "Nothing signed.");
            Assert.Equal("observed-success", built.Json.GetProperty("status").GetString());
            Assert.Contains("--draft=false", built.Json.GetProperty("publish").GetString(), StringComparison.Ordinal);
            string candidateId = built.Json.GetProperty("candidate").GetProperty("id").GetString()!;
            string candidateHash = built.Json.GetProperty("candidate").GetProperty("hash").GetString()!;
            Assert.Contains("0.2.0  candidate", CliHarness.Ok("--estate", root, "release", "list").Out, StringComparison.Ordinal);
            Assert.Contains("not published", CliHarness.Run("--estate", root, "estate", "upgrade", "--to", "0.2.0", "--check").Err, StringComparison.Ordinal);
            Assert.Equal(2, CliHarness.Run("--estate", root, "release", "submit", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Code);

            // Automation's verification of the run is recorded from the host's own report.
            CliHarness.Outcome ci = CliHarness.Ok("--estate", root, "--json", "check", "observe", "--resource", resource, "--repository", "Example/widget", "--run", run, "--workflow", "release.yml", "--by", builder);
            Assert.Equal("success", ci.Json.GetProperty("conclusion").GetString());

            // Still a draft: observing records nothing; publication is the operator's manual step after accepting the candidate.
            Assert.Contains("a draft", CliHarness.Ok("--estate", root, "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Out, StringComparison.Ordinal);
            provider.Publish("Example/widget", "v0.2.0");
            CliHarness.Outcome unaccepted = CliHarness.Run("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(1, unaccepted.Code);
            Assert.Contains("release.unaccepted", unaccepted.Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidateId}:{candidateHash}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted 0.2.0 from the recorded evidence.","basis":[],"self":false}"""));
            CliHarness.Outcome published = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal("published", published.Json.GetProperty("status").GetString());
            Assert.Contains("0.2.0  published", CliHarness.Ok("--estate", root, "release", "list").Out, StringComparison.Ordinal);
            Assert.Contains("would upgrade", CliHarness.Ok("--estate", root, "estate", "upgrade", "--to", "0.2.0", "--check").Out, StringComparison.Ordinal);
            Assert.Contains("is published", CliHarness.Ok("--estate", root, "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Out, StringComparison.Ordinal);

            // A missing account blocks that destination, not local work; the refusal is a known failure and a retry names its reconciliation.
            provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.RefuseAuthentication;
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "release", "submit", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(1, refused.Code);
            Assert.Equal("known-failure", refused.Json.GetProperty("status").GetString());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.Equal(2, CliHarness.Run("--estate", root, "release", "submit", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder).Code);
            CliHarness.Outcome retried = CliHarness.Ok("--estate", root, "--json", "release", "submit", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder, "--reconciliation", "Logged in; the refused attempt had no effect.");
            Assert.Equal("dispatched", retried.Json.GetProperty("status").GetString());

            // An uncertain dispatch stays uncertain until reconciled; cancellation settles it once no effect is found.
            provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.Uncertain;
            CliHarness.Outcome uncertain = CliHarness.Run("--estate", root, "--json", "release", "submit", "--product", product, "--version", "0.4.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(1, uncertain.Code);
            Assert.Equal("uncertain", uncertain.Json.GetProperty("status").GetString());
            Assert.Contains("no run is identified", CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.4.0", "--repository", "Example/widget", "--by", builder).Json.GetProperty("pending").GetString(), StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.4.0", "--repository", "Example/widget", "--by", builder, "--cancel", "GitHub shows no run and no release for v0.4.0.");
            Assert.Contains("github-releases:Example/widget:v0.4.0  canceled", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            Commands.ReleaseProvider = previous;
            CliHarness.Delete(root, scratch);
        }
    }

    private static string Init(string root) =>
        CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Software journeys.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;

    private static string Principal(string root, string op, string scratch, string name, string role) =>
        CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, $"principal-{name}.json", $$$"""{"kind":"agent","name":"{{{name}}}","roles":["{{{role}}}"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;

    private static void Git(string repo, params string[] arguments)
    {
        Assert.Equal(0, RunGit(repo, out _, arguments));
    }

    private static string GitOutput(string repo, params string[] arguments)
    {
        Assert.Equal(0, RunGit(repo, out string output, arguments));
        return output.Trim();
    }

    private static int RunGit(string repo, out string output, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = repo };
        foreach (string argument in GitIdentity.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
