// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Cli.Tests;

/// <summary>A published release makes the product's living documents due, and a due document at level refuse holds the next release.</summary>
[Collection("cli")]
public class LivingDocumentJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";

    [Fact]
    public void ADocumentDueAtRefuseHoldsTheNextReleaseUntilItIsReviewed()
    {
        string root = CliHarness.TempDirectory("living");
        string scratch = CliHarness.TempDirectory("living-scratch");
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Living documents.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;

            // The operator declares the overview living: each publication of the widget makes it due, at refuse.
            string overview = Path.Combine(root, "docs", "overview.md");
            Directory.CreateDirectory(Path.GetDirectoryName(overview)!);
            File.WriteAllText(overview, "# Widget\n\nSee the releases page for the current release.\n");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "document-revision", "--by", op, "--body", CliHarness.WriteJson(scratch, "revision.json", $$"""{"path":"docs/overview.md","title":"Overview","hash":"{{ContentHash.Sha256File(overview)}}","bytes":{{new FileInfo(overview).Length}},"mediaType":"text/markdown","revision":1,"summary":"First."}"""));
            CliHarness.Outcome declared = CliHarness.Ok("--estate", root, "--json", "records", "new", "living-document", "--by", op, "--body", CliHarness.WriteJson(scratch, "declaration.json", $$"""{"path":"docs/overview.md","title":"Overview","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{product}}"}],"summary":"Says what the widget is and where its releases are."}"""));
            Assert.Contains("Living documents: 0 due of 1 declared", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // Release 0.2.0 runs through intent, effect, acceptance and publication.
            string run = CliHarness.Ok("--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Json.GetProperty("run").GetString()!;
            provider.CompleteRun("Example/widget", run, true, "0.2.0");
            JsonElement candidate = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder, "--qualified", "linux-x64", "--limitation", "Nothing signed.").Json.GetProperty("candidate");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.GetProperty("id").GetString()}:{candidate.GetProperty("hash").GetString()}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted 0.2.0.","basis":[],"self":false}"""));
            provider.Publish("Example/widget", "v0.2.0");
            Assert.Equal("published", CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", "0.2.0", "--repository", "Example/widget", "--by", builder).Json.GetProperty("status").GetString());

            // The publication makes the overview due: status says why, the estate check warns, and 0.3.0 is refused before any intent.
            string status = CliHarness.Ok("--estate", root, "status").Out;
            Assert.True(status.Contains("Living documents: 1 due of 1 declared", StringComparison.Ordinal), status);
            Assert.Contains("docs/overview.md: due at refuse: release 0.2.0 was published", status, StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.Equal(0, check.Code);
            Assert.Contains("document.due", check.Out, StringComparison.Ordinal);
            int operations = Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count();
            CliHarness.Outcome held = CliHarness.Run("--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder);
            Assert.Equal(2, held.Code);
            Assert.Contains("docs/overview.md", held.Out + held.Err, StringComparison.Ordinal);
            Assert.Equal(operations, Directory.EnumerateFiles(Path.Combine(root, "records", "operation"), "*.json", SearchOption.AllDirectories).Count());
            Assert.Single(provider.Dispatches);

            // The operator reads it against 0.2.0 and records that it holds; the gate opens.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "document-review", "--by", op, "--subject", $"living-document:{declared.Id}:{declared.Hash}", "--body", CliHarness.WriteJson(scratch, "review.json", $$"""{"outcome":"current","content":{"hash":"{{ContentHash.Sha256File(overview)}}","bytes":{{new FileInfo(overview).Length}}},"answers":[{"on":"release"}],"independence":"self","summary":"It names no release and points to the releases page.","findings":[],"checks":[]}"""));
            Assert.Contains("Living documents: 0 due of 1 declared", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Equal("dispatched", CliHarness.Ok("--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.3.0", "--repository", "Example/widget", "--by", builder).Json.GetProperty("status").GetString());
        }
        finally
        {
            Commands.ReleaseProvider = previous;
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void TheDocsCommandsDeclareRegenerateAndReviewAndRefuseWhatTheChecksAndClearedByForbid()
    {
        string root = CliHarness.TempDirectory("living-docs");
        string scratch = CliHarness.TempDirectory("living-docs-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Living documents.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string guide = Path.Combine(root, "docs", "guide.md");
            Directory.CreateDirectory(Path.GetDirectoryName(guide)!);
            File.WriteAllText(Path.Combine(root, "docs", "overview.md"), "# Overview\n");
            File.WriteAllText(guide, "# Guide\n\nSee [the overview](overview.md).\n<!-- vericairn:generated releases -->\n<!-- vericairn:end releases -->\n");

            // Declared with a generated region, two checks, and clearance by someone other than whoever revised it.
            CliHarness.Ok("--estate", root, "--json", "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, "declaration.json", """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"regions":[{"id":"releases","generator":"vericairn.records/releases"}],"checks":["links-resolve","matches-generator"],"clearedBy":"independent","summary":"Restates the releases in a generated region."}"""));

            // The region does not hold what its generator makes until it is regenerated; then the estate check passes.
            CliHarness.Outcome stale = CliHarness.Run("--estate", root, "estate", "check");
            Assert.Equal(1, stale.Code);
            Assert.Contains("document.generated-edited", stale.Out, StringComparison.Ordinal);
            Assert.Contains("rewrote releases", CliHarness.Ok("--estate", root, "docs", "regenerate", "docs/guide.md").Out, StringComparison.Ordinal);
            Assert.Contains("See [the overview](overview.md).", File.ReadAllText(guide), StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.Contains("docs/guide.md  current", CliHarness.Ok("--estate", root, "docs", "living").Out, StringComparison.Ordinal);

            // A decision that names it makes it due, at warn.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Rename","stake":"low","notAuthorized":[],"statement":"Rename the overview.","summary":"Rename.","synthesis":"The operator renames the overview.","effects":["rename"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","documents":["docs/guide.md"]}"""));
            string living = CliHarness.Ok("--estate", root, "docs", "living").Out;
            Assert.Contains("docs/guide.md  due at warn", living, StringComparison.Ordinal);
            Assert.Contains("decision (warn)", living, StringComparison.Ordinal);
            Assert.Contains("document.due", CliHarness.Run("--estate", root, "estate", "check").Out, StringComparison.Ordinal);

            // A hand edit in the region and a broken link each fail a check, which refuses the clearance.
            File.WriteAllText(guide, File.ReadAllText(guide).Replace("| Version |", "| Edited |", StringComparison.Ordinal));
            CliHarness.Outcome edited = CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", op, "--outcome", "current", "--summary", "Read it.");
            Assert.Equal(1, edited.Code);
            Assert.Contains("document.check-failed", edited.Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "docs", "regenerate", "docs/guide.md");
            File.WriteAllText(guide, File.ReadAllText(guide).Replace("overview.md", "renamed.md", StringComparison.Ordinal));
            Assert.Contains("links-resolve", CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", op, "--outcome", "current", "--summary", "Read it.").Out, StringComparison.Ordinal);

            // The builder fixes it and records its revision; it cannot also clear what it revised here.
            File.Move(Path.Combine(root, "docs", "overview.md"), Path.Combine(root, "docs", "renamed.md"));
            CliHarness.Outcome own = CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", builder, "--outcome", "revised", "--summary", "Pointed the link at the renamed overview.");
            Assert.Equal(1, own.Code);
            Assert.Contains("document.cleared-by", own.Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", builder, "--outcome", "needs-change", "--summary", "Changed for the rename; asking for the operator's review.", "--finding", "info:The link now points at renamed.md.");
            Assert.Contains("docs/guide.md  due at warn", CliHarness.Ok("--estate", root, "docs", "living").Out, StringComparison.Ordinal);

            // The operator's review clears it, with what it answered, the checks that ran and his independence recorded by the runtime.
            CliHarness.Outcome cleared = CliHarness.Ok("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", op, "--outcome", "current", "--summary", "The guide holds after the rename.");
            using (JsonDocument review = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, cleared.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)))))
            {
                JsonElement body = review.RootElement.GetProperty("body");
                Assert.Equal("operator", body.GetProperty("independence").GetString());
                Assert.Equal("decision", body.GetProperty("answers")[0].GetProperty("on").GetString());
                Assert.All(body.GetProperty("checks").EnumerateArray(), c => Assert.Equal("passed", c.GetProperty("outcome").GetString()));
                Assert.Equal(ContentHash.Sha256File(guide), body.GetProperty("content").GetProperty("hash").GetString());
            }

            Assert.Contains("docs/guide.md  current", CliHarness.Ok("--estate", root, "docs", "living").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }
}
