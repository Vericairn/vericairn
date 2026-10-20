// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
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

            // In a format-2 estate the verification says how its evidence was obtained: the command ran here and was seen to finish.
            Assert.Contains("\"evidenceClass\": \"observation\"", verification, StringComparison.Ordinal);
            Assert.Contains("\"observer\": \"automation\"", verification, StringComparison.Ordinal);

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
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            string op = Init(root);
            string builder = Principal(root, op, scratch, "Builder", "builder");
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
            string resource = CliHarness.Ok("--estate", root, "--json", "records", "new", "resource", "--by", op, "--body", CliHarness.WriteJson(scratch, "resource.json", """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"public"}""")).Id;

            // Submit: the intent is a record before the dispatch; the run is recorded as observed.
            CliHarness.Outcome submitted = ReleaseJourney.Ok(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal("dispatched", submitted.Json.GetProperty("status").GetString());
            string run = submitted.Json.GetProperty("run").GetString()!;
            Assert.Single(provider.Dispatches);
            Assert.Equal("0.2.0", provider.Dispatches[0].Inputs["version"]);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.Single(submitted.Json.GetProperty("basisEvidence").EnumerateArray());
            Assert.Contains("github-releases:Example/widget:v0.2.0  dispatched", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // A delayed decision leaves it pending: nothing new is recorded, and a second submit is refused until reconciled.
            CliHarness.Outcome pending = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder);
            Assert.Contains("in progress", pending.Json.GetProperty("pending").GetString(), StringComparison.Ordinal);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.Equal(2, ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Code);

            // The run succeeds and the draft appears: the operation is observed-success and the candidate is recorded with every distributable by hash.
            provider.CompleteRun("Example/widget", run, true, "0.2.0");
            CliHarness.Outcome built = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder, "--qualified", "linux-x64", "--limitation", "Nothing signed.");
            Assert.Equal("observed-success", built.Json.GetProperty("status").GetString());
            Assert.Contains("--draft=false", built.Json.GetProperty("publish").GetString(), StringComparison.Ordinal);
            string candidateId = built.Json.GetProperty("candidate").GetProperty("id").GetString()!;
            string candidateHash = built.Json.GetProperty("candidate").GetProperty("hash").GetString()!;
            Assert.Contains("0.2.0  candidate", CliHarness.Ok("--estate", root, "release", "list").Out, StringComparison.Ordinal);
            Assert.Contains("not published", CliHarness.Run("--estate", root, "estate", "upgrade", "--to", "0.2.0", "--check").Err, StringComparison.Ordinal);
            Assert.Equal(2, ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Code);

            // Automation's verification of the run is recorded from the host's own report.
            CliHarness.Outcome ci = CliHarness.Ok("--estate", root, "--json", "check", "observe", "--resource", resource, "--repository", "Example/widget", "--run", run, "--workflow", "release.yml", "--by", builder);
            Assert.Equal("success", ci.Json.GetProperty("conclusion").GetString());
            string reported = File.ReadAllText(Path.Combine(root, ci.Json.GetProperty("verification").GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("\"evidenceClass\": \"report\"", reported, StringComparison.Ordinal);
            Assert.Contains("\"observer\": \"provider\"", reported, StringComparison.Ordinal);

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

            // The release notes since 0.2.0 list a result as accepted only when its exact revision is accepted.
            // Explicit record times put the notes' work after the publication without depending on wall-clock delays.
            DateTimeOffset notesTime = EstateLoader.Load(root).Current("release").Single(r => EstateModel.BodyString(r, "version") == "0.2.0").AtTime.AddSeconds(1);
            string brief = CliHarness.WriteJson(scratch, "notes-brief.json", """{"outcome":"Notes.","sources":[],"permittedChanges":["notes"],"prohibitedEffects":["none"],"workspace":["here"],"limits":["none"],"expectedOutputs":["notes"],"reviewCriteria":["read"],"recovery":"Read the brief."}""");
            string done = CliHarness.WriteJson(scratch, "notes-result.json", """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""");
            var noted = new List<string>();
            var earlierResults = new List<string>();
            foreach (string key in new[] { "W-8", "W-9" })
            {
                CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Work {{key}}","intent":"Exercise the notes.","scope":["notes"],"exitQuestion":"Done?","inputs":[],"deliverables":["notes"]}"""));
                string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", key, "--contributor", builder, "--by", builder, "--brief", brief).Id;
                for (int i = 0; i < (key == "W-8" ? 3 : 1); i++)
                {
                    CliHarness.Outcome attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "here", "--note", "Do it.", "--at", RecordParser.FormatTimestamp(notesTime));
                    CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "records", "new", "result", "--by", builder, "--subject", $"attempt:{attempt.Id}:{attempt.Hash}", "--body", done, "--at", RecordParser.FormatTimestamp(notesTime));
                    notesTime = notesTime.AddSeconds(1);
                    if (key == "W-8" && i < 2) earlierResults.Add(result.Id);
                    else noted.Add(result.Id);
                }
            }

            string acceptedResult = noted[0];
            string acceptedHash = Directory.EnumerateFiles(Path.Combine(root, "records", "result"), acceptedResult + ".json", SearchOption.AllDirectories).Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("hash").GetString()!).Single();
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{acceptedResult}:{acceptedHash}", "--body", CliHarness.WriteJson(scratch, "accept-w8.json", """{"statement":"Accepted.","basis":[],"self":false}"""));

            string notesDirectory = Path.Combine(scratch, "notes");
            Assert.Equal(0, Docs.Notes(root, "0.2.0", "0.3.0", notesDirectory, null, json: false, at: notesTime.AddSeconds(1)));
            string notes = File.ReadAllText(Path.Combine(notesDirectory, "release-notes.md"));
            int acceptedSection = notes.IndexOf("## Work accepted", StringComparison.Ordinal);
            int openSection = notes.IndexOf("## Work recorded, not yet accepted", StringComparison.Ordinal);
            Assert.True(acceptedSection >= 0 && openSection > acceptedSection, notes);
            Assert.InRange(notes.IndexOf("Result " + noted[0], StringComparison.Ordinal), acceptedSection, openSection);
            Assert.True(notes.IndexOf("Result " + noted[1], StringComparison.Ordinal) > openSection, notes);
            Assert.Single(notes.Split('\n'), line => line.StartsWith("### W-8:", StringComparison.Ordinal));
            Assert.Contains("`vericairn work observe W-8`", notes, StringComparison.Ordinal);
            string history = CliHarness.Ok("--estate", root, "work", "observe", "W-8").Out;
            foreach (string earlier in earlierResults)
            {
                Assert.DoesNotContain("Result " + earlier, notes, StringComparison.Ordinal);
                Assert.Contains(earlier, history, StringComparison.Ordinal);
            }

            // A missing account blocks that destination, not local work; the refusal is a known failure and a retry names its reconciliation.
            provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.RefuseAuthentication;
            CliHarness.Outcome refused = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(1, refused.Code);
            Assert.Equal("known-failure", refused.Json.GetProperty("status").GetString());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.Equal(2, ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder).Code);
            CliHarness.Outcome retried = ReleaseJourney.Ok(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder, "--reconciliation", "Logged in; the refused attempt had no effect.");
            Assert.Equal("dispatched", retried.Json.GetProperty("status").GetString());

            // An uncertain dispatch stays uncertain until reconciled; cancellation settles it once no effect is found.
            provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.Uncertain;
            CliHarness.Outcome uncertain = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.4.0", "--repository", "Example/widget", "--by", builder);
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

    [Fact]
    public void J04_ASecondProductReleasesThroughTheProviderItDeclaresWithItsOwnArtifacts()
    {
        string root = CliHarness.TempDirectory("j04-second");
        string scratch = CliHarness.TempDirectory("j04-second-scratch");
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            string op = Init(root);
            string builder = Principal(root, op, scratch, "Builder", "builder");
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));

            // A product with nothing declared and a request naming no provider: refused before anything is recorded.
            string bare = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "bare.json", Product)).Id;
            CliHarness.Outcome unselected = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--product", bare, "--version", "1.0.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(2, unselected.Code);
            Assert.Contains("No release provider is selected", unselected.Err, StringComparison.Ordinal);
            CliHarness.Outcome unknown = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--provider", "gitlab-releases", "--product", bare, "--version", "1.0.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(2, unknown.Code);
            Assert.Contains("'gitlab-releases' has no implementation", unknown.Err, StringComparison.Ordinal);
            CliHarness.Outcome incomplete = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "release", "submit", "--provider", "github-releases", "--product", bare, "--version", "1.0.0", "--by", builder);
            Assert.Equal(2, incomplete.Code);
            Assert.Contains("needs the settings repository", incomplete.Err, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "records", "operation")));
            Assert.Empty(provider.Dispatches);

            // A second product declares its destination and its own artifacts on its record.
            string gadgetBody = """
                {"name":"Gadget","slug":"gadget","summary":"A gadget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],
                 "release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/gadget","workflow":"ship.yml"}},"checksums":"CHECKSUMS.txt",
                  "artifacts":[{"name":"gadget-{version}-{platform}.zip","role":"distributable","platforms":["linux-x64","win-x64"]},{"name":"gadget-{version}.spdx.json","role":"sbom"},{"name":"LICENSES-{version}.txt","role":"notices"}]}}
                """;
            string gadget = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "gadget.json", gadgetBody)).Id;
            CliHarness.Outcome submitted = ReleaseJourney.Ok(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--product", gadget, "--version", "1.0.0", "--by", builder);
            Assert.Equal("github-releases:Example/gadget:v1.0.0", submitted.Json.GetProperty("identity").GetString());
            Assert.Single(submitted.Json.GetProperty("basisEvidence").EnumerateArray());
            DispatchRequest dispatch = Assert.Single(provider.Dispatches);
            Assert.Equal("Example/gadget", dispatch.Destination);
            Assert.Equal("ship.yml", dispatch.Settings["workflow"]);
            Assert.Equal("main", dispatch.Settings["ref"]);
            Assert.Equal("CHECKSUMS.txt", dispatch.Settings["checksums"]);
            string run = submitted.Json.GetProperty("run").GetString()!;

            // A draft that lacks a declared artifact is not settled; the complete one becomes the candidate with the declared platforms and roles.
            provider.CompleteRun("Example/gadget", run, true, "1.0.0", ["gadget-1.0.0-linux-x64.zip", "gadget-1.0.0.spdx.json", "LICENSES-1.0.0.txt"], "CHECKSUMS.txt");
            CliHarness.Outcome waiting = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--product", gadget, "--version", "1.0.0", "--by", builder);
            Assert.Contains("gadget-1.0.0-win-x64.zip", waiting.Json.GetProperty("pending").GetString(), StringComparison.Ordinal);
            provider.CompleteRun("Example/gadget", run, true, "1.0.0", ["gadget-1.0.0-linux-x64.zip", "gadget-1.0.0-win-x64.zip", "gadget-1.0.0.spdx.json", "LICENSES-1.0.0.txt"], "CHECKSUMS.txt");
            CliHarness.Outcome built = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--product", gadget, "--version", "1.0.0", "--by", builder);
            Assert.Equal("observed-success", built.Json.GetProperty("status").GetString());
            string candidateId = built.Json.GetProperty("candidate").GetProperty("id").GetString()!;
            JsonElement candidate = JsonDocument.Parse(File.ReadAllBytes(Directory.EnumerateFiles(Path.Combine(root, "records", "release"), candidateId + ".json", SearchOption.AllDirectories).Single())).RootElement.GetProperty("body");
            Assert.Equal("Example/gadget", candidate.GetProperty("repository").GetString());
            Dictionary<string, string> platforms = candidate.GetProperty("artifacts").EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("platform").GetString()!);
            Assert.Equal(4, platforms.Count);
            Assert.Equal("win-x64", platforms["gadget-1.0.0-win-x64.zip"]);
            Assert.Equal("any", platforms["LICENSES-1.0.0.txt"]);
            string sbomHash = candidate.GetProperty("artifacts").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "gadget-1.0.0.spdx.json").GetProperty("hash").GetString()!;
            Assert.Equal(sbomHash, candidate.GetProperty("sbom").GetString());
            Assert.True(candidate.TryGetProperty("notices", out _));
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

            // The providers the runtime can reach, with what each declares.
            JsonElement providers = CliHarness.Ok("--estate", root, "--json", "release", "providers").Json;
            JsonElement github = providers.EnumerateArray().Single(p => p.GetProperty("provider").GetString() == "github-releases");
            Assert.Equal("repository", github.GetProperty("destination").GetString());
            Assert.Contains(github.GetProperty("settings").EnumerateArray(), s => s.GetProperty("name").GetString() == "workflow" && s.GetProperty("default").GetString() == "release.yml");
            Assert.Contains(providers.EnumerateArray(), p => p.GetProperty("kind").GetString() == "source" && p.GetProperty("provider").GetString() == "git");
            Assert.Contains("repository   destination, required", CliHarness.Ok("--estate", root, "release", "providers").Out, StringComparison.Ordinal);
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
