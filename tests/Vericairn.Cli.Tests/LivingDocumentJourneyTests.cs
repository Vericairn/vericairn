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
}
