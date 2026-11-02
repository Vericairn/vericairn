// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Runtime.Providers;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A document revision clears a living document of the estate only as its declaration allows: its author meets clearedBy, judged as
/// that author's own review of the pinned bytes would be, and the declared checks pass on those bytes. Explicitly covered paths are
/// cleared only by a review. Any other revision is recorded, and docs living, status, the estate check and the release gate keep the
/// document due with a reason that says who may clear it or which check failed.
/// </summary>
[Collection("cli")]
public sealed class RevisionClearanceJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";

    [Fact]
    public void AnAuthorsOwnRevisionLeavesAnIndependentDocumentDueAndItsReleaseHeldUntilAnotherPrincipalReviewsIt()
    {
        string root = CliHarness.TempDirectory("revision-clearance-independent");
        string scratch = CliHarness.TempDirectory("revision-clearance-independent-scratch");
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            (string op, string author, string reviewer, string product) = Initialize(root, scratch);
            Write(root, "docs/overview.md", "# Overview\n");
            Write(root, "docs/source/a.txt", "a\n");
            Write(root, "docs/guide.md", "# Guide\n\nSee [the overview](overview.md).\n");
            Declare(root, scratch, op, "docs/guide.md", product, "independent", covers: true);

            // The author's first revision is recorded, and it does not clear a document that another principal clears.
            string first = Pin(root, "docs/guide.md", author, "First.");
            JsonElement row = Row(root, "docs/guide.md");
            Assert.Equal("due", row.GetProperty("state").GetString());
            Assert.Equal("refuse", row.GetProperty("level").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("clearance").ValueKind);
            string uncleared = OwnContent(row);
            Assert.Contains($"its revision {first} by {author} does not clear it", uncleared, StringComparison.Ordinal);
            Assert.Contains("clearedBy is independent", uncleared, StringComparison.Ordinal);

            // Another principal's review clears it, its covered paths included.
            string review = Review(root, "docs/guide.md", reviewer);
            row = Row(root, "docs/guide.md");
            Assert.Equal("current", row.GetProperty("state").GetString());
            Assert.Equal(review, row.GetProperty("clearance").GetString());

            // The author adds a link to a file that does not exist and records the revision: it is recorded, and the document stays
            // due at refuse in docs living, status and the estate check, and holds the product's release, naming who may clear it.
            Write(root, "docs/guide.md", "# Guide\n\nSee [the overview](overview.md) and [the plan](plan.md).\n");
            string second = Pin(root, "docs/guide.md", author, "Second.");
            row = Row(root, "docs/guide.md");
            Assert.Equal("due", row.GetProperty("state").GetString());
            Assert.Equal("refuse", row.GetProperty("level").GetString());
            Assert.Equal(review, row.GetProperty("clearance").GetString());
            string reason = OwnContent(row);
            Assert.Contains($"docs/guide.md changed after its review {review}", reason, StringComparison.Ordinal);
            Assert.Contains($"its revision {second} by {author} does not clear it", reason, StringComparison.Ordinal);
            Assert.Contains("a document review by a principal other than the one that wrote or revised its content clears it", reason, StringComparison.Ordinal);
            string living = CliHarness.Ok("--estate", root, "docs", "living").Out;
            Assert.Contains($"docs/guide.md  due at refuse  Guide  declaration", living, StringComparison.Ordinal);
            Assert.Contains($"cleared by {review}", living, StringComparison.Ordinal);
            Assert.Contains(reason, CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.Equal(0, check.Code);
            Assert.Contains("document.due", check.Out, StringComparison.Ordinal);
            Assert.Contains(reason, check.Out, StringComparison.Ordinal);
            CliHarness.Outcome held = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", op);
            Assert.Equal(2, held.Code);
            Assert.Contains(reason, held.Json.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Empty(provider.Dispatches);

            // The author's own review of the same bytes is refused for who it is, and the reviewer's for the failing check.
            CliHarness.Outcome own = CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", author, "--outcome", "current", "--summary", "My own text.");
            Assert.Equal(1, own.Code);
            Assert.Contains("document.cleared-by", own.Out, StringComparison.Ordinal);
            CliHarness.Outcome failing = CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", "current", "--summary", "Read it.");
            Assert.Equal(1, failing.Code);
            Assert.Contains("document.check-failed", failing.Out, StringComparison.Ordinal);
            Assert.Contains("links-resolve", failing.Out, StringComparison.Ordinal);
            Assert.Equal(review, Row(root, "docs/guide.md").GetProperty("clearance").GetString());

            // Once the linked file exists, the reviewer's review clears it everywhere and the release proceeds.
            Write(root, "docs/plan.md", "# Plan\n");
            Assert.Equal("due", Row(root, "docs/guide.md").GetProperty("state").GetString());
            string cleared = Review(root, "docs/guide.md", reviewer);
            row = Row(root, "docs/guide.md");
            Assert.Equal("current", row.GetProperty("state").GetString());
            Assert.Equal(cleared, row.GetProperty("clearance").GetString());
            Assert.Contains("Living documents: 0 due of 1 declared", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.DoesNotContain("document.due", CliHarness.Ok("--estate", root, "estate", "check").Out, StringComparison.Ordinal);
            Assert.Equal("dispatched", ReleaseJourney.Ok(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", op).Json.GetProperty("status").GetString());
        }
        finally
        {
            Commands.ReleaseProvider = previous;
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void CoveredPathsAreClearedOnlyByAReviewAndAFailingCheckKeepsARevisionFromClearing()
    {
        string root = CliHarness.TempDirectory("revision-clearance-checks");
        string scratch = CliHarness.TempDirectory("revision-clearance-checks-scratch");
        try
        {
            (string op, string author, string reviewer, string product) = Initialize(root, scratch);
            Write(root, "docs/overview.md", "# Overview\n");
            Write(root, "docs/source/a.txt", "a\n");
            Write(root, "docs/guide.md", "# Guide\n\nSee [the overview](overview.md).\n");
            Declare(root, scratch, op, "docs/guide.md", product, "any", covers: true);

            // Any principal may clear it, so the author's revision clears its own content, but not the covered paths it declares.
            string first = Pin(root, "docs/guide.md", author, "First.");
            JsonElement row = Row(root, "docs/guide.md");
            Assert.Equal(first, row.GetProperty("clearance").GetString());
            JsonElement reason = Assert.Single(row.GetProperty("reasons").EnumerateArray());
            Assert.Equal("covers", reason.GetProperty("on").GetString());
            Assert.Equal("warn", reason.GetProperty("level").GetString());
            Assert.Contains("no review has recorded its covered paths (docs/source)", reason.GetProperty("summary").GetString(), StringComparison.Ordinal);
            string review = Review(root, "docs/guide.md", reviewer);
            Assert.Equal("current", Row(root, "docs/guide.md").GetProperty("state").GetString());

            // A covered path changes; a new revision of the document clears its own bytes, and the covered paths stay due.
            Write(root, "docs/source/a.txt", "changed\n");
            Write(root, "docs/guide.md", "# Guide\n\nSee [the overview](overview.md) for the source.\n");
            string second = Pin(root, "docs/guide.md", author, "Second.");
            row = Row(root, "docs/guide.md");
            Assert.Equal(second, row.GetProperty("clearance").GetString());
            reason = Assert.Single(row.GetProperty("reasons").EnumerateArray());
            Assert.Contains($"its covered paths (docs/source) changed after the review {review}", reason.GetProperty("summary").GetString(), StringComparison.Ordinal);
            string reviewed = Review(root, "docs/guide.md", reviewer);
            Assert.Equal("current", Row(root, "docs/guide.md").GetProperty("state").GetString());

            // A revision whose bytes fail a declared check is recorded and clears nothing; the reason names the failing check.
            Write(root, "docs/guide.md", "# Guide\n\nSee [the overview](overview.md) and [the plan](plan.md).\n");
            string third = Pin(root, "docs/guide.md", author, "Third.");
            row = Row(root, "docs/guide.md");
            Assert.Equal("due", row.GetProperty("state").GetString());
            Assert.Equal("refuse", row.GetProperty("level").GetString());
            Assert.Equal(reviewed, row.GetProperty("clearance").GetString());
            string failed = OwnContent(row);
            Assert.Contains($"docs/guide.md changed after its review {reviewed}", failed, StringComparison.Ordinal);
            Assert.Contains($"its revision {third} by {author} does not clear it: its declared check failed on the bytes it pinned (links-resolve:", failed, StringComparison.Ordinal);
            Assert.Contains("plan.md", failed, StringComparison.Ordinal);
            Assert.Contains(failed, CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Contains(failed, CliHarness.Ok("--estate", root, "estate", "check").Out, StringComparison.Ordinal);

            // When the check passes on the same pinned bytes, the revision clears it.
            Write(root, "docs/plan.md", "# Plan\n");
            row = Row(root, "docs/guide.md");
            Assert.Equal("current", row.GetProperty("state").GetString());
            Assert.Equal(third, row.GetProperty("clearance").GetString());
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void OperatorAndAnyDeclarationsStillClearByARevisionWhoseAuthorMeetsThem()
    {
        string root = CliHarness.TempDirectory("revision-clearance-operator");
        string scratch = CliHarness.TempDirectory("revision-clearance-operator-scratch");
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            (string op, string author, _, string product) = Initialize(root, scratch);
            Write(root, "docs/guide.md", "# Guide\n");
            Write(root, "docs/notes.md", "# Notes\n");
            Declare(root, scratch, op, "docs/guide.md", product, "operator", covers: false);
            Declare(root, scratch, op, "docs/notes.md", product, "any", covers: false);

            // Any principal's revision clears a document any principal clears.
            string notes = Pin(root, "docs/notes.md", author, "Notes.");
            JsonElement row = Row(root, "docs/notes.md");
            Assert.Equal("current", row.GetProperty("state").GetString());
            Assert.Equal(notes, row.GetProperty("clearance").GetString());

            // The author's revision does not clear what the operator clears, and the release is held for it alone.
            string authored = Pin(root, "docs/guide.md", author, "Authored.");
            row = Row(root, "docs/guide.md");
            Assert.Equal("refuse", row.GetProperty("level").GetString());
            string reason = OwnContent(row);
            Assert.Contains($"its revision {authored} by {author} does not clear it: its clearedBy is operator", reason, StringComparison.Ordinal);
            CliHarness.Outcome held = ReleaseJourney.Run(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", op);
            Assert.Equal(2, held.Code);
            Assert.Contains("docs/guide.md", held.Json.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("docs/notes.md", held.Json.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Empty(provider.Dispatches);

            // The operator's own revision, whose checks pass, clears it, and the release proceeds.
            Write(root, "docs/guide.md", "# Guide\n\nRead by the operator.\n");
            string operated = Pin(root, "docs/guide.md", op, "Operator.");
            row = Row(root, "docs/guide.md");
            Assert.Equal("current", row.GetProperty("state").GetString());
            Assert.Equal(operated, row.GetProperty("clearance").GetString());
            Assert.Contains("Living documents: 0 due of 2 declared", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Equal("dispatched", ReleaseJourney.Ok(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", "0.2.0", "--repository", "Example/widget", "--by", op).Json.GetProperty("status").GetString());
        }
        finally
        {
            Commands.ReleaseProvider = previous;
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void GeneratedRevisionHistoriesClearByRevisionOnlyAsTheirDeclarationAllows()
    {
        string root = CliHarness.TempDirectory("revision-clearance-history");
        string scratch = CliHarness.TempDirectory("revision-clearance-history-scratch");
        try
        {
            (string op, string author, string reviewer, string product) = Initialize(root, scratch);
            Write(root, "design/any.md", "# Any\n\nBody.\n");
            Write(root, "design/independent.md", "# Independent\n\nBody.\n");
            Declare(root, scratch, op, "design/any.md", product, "any", covers: false);
            Declare(root, scratch, op, "design/independent.md", product, "independent", covers: false);

            foreach (string path in new[] { "design/any.md", "design/independent.md" })
            {
                bool independent = path == "design/independent.md";
                string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

                // Conversion and a later authored change each regenerate the history; the author's pins clear only where any principal may.
                string converted = Pin(root, path, author, "Start the history.", "--convert-revisions");
                Assert.Equal(independent ? "due" : "current", Row(root, path).GetProperty("state").GetString());
                if (independent) Review(root, path, reviewer);
                Write(root, path, File.ReadAllText(file).Replace("Body.", "Changed body.", StringComparison.Ordinal));
                string changed = Pin(root, path, author, "Change the body.");
                JsonElement row = Row(root, path);
                Assert.Equal(independent ? "due" : "current", row.GetProperty("state").GetString());
                if (independent)
                {
                    Assert.Contains($"its revision {changed} by {author} does not clear it", OwnContent(row), StringComparison.Ordinal);
                    Review(root, path, reviewer);
                }
                else Assert.Equal(changed, row.GetProperty("clearance").GetString());
                Assert.Equal("current", Row(root, path).GetProperty("state").GetString());
                Assert.NotEqual(converted, changed);

                // A hand edit of a generated line is due against its records, and no review clears it.
                Write(root, path, File.ReadAllText(file).Replace("Change the body.", "A hand-edited line.", StringComparison.Ordinal));
                Assert.Contains("the generated revision region differs from its records", OwnContent(Row(root, path)), StringComparison.Ordinal);
                CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "docs", "review", path, "--by", reviewer, "--outcome", "current", "--summary", "Read it.");
                Assert.Equal(1, refused.Code);
                Assert.Contains("revision-history", refused.Out, StringComparison.Ordinal);

                // Refreshing the history restores it from its records; the refresh is a revision, cleared as the declaration allows.
                string refreshed = Pin(root, path, author, "Refresh the history.", "--render-template");
                Assert.DoesNotContain("A hand-edited line.", File.ReadAllText(file), StringComparison.Ordinal);
                row = Row(root, path);
                Assert.Equal(independent ? "due" : "current", row.GetProperty("state").GetString());
                if (independent) Review(root, path, reviewer);
                else Assert.Equal(refreshed, row.GetProperty("clearance").GetString());
                Assert.Equal("current", Row(root, path).GetProperty("state").GetString());
            }

            Assert.DoesNotContain("document.revisions", CliHarness.Ok("--estate", root, "estate", "check").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static (string Operator, string Author, string Reviewer, string Product) Initialize(string root, string scratch)
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Revision clearance.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        string author = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "author.json", """{"kind":"agent","name":"Author A","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
        string reviewer = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "reviewer.json", """{"kind":"agent","name":"Reviewer B","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
        string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
        return (op, author, reviewer, product);
    }

    private static void Declare(string root, string scratch, string op, string path, string product, string clearedBy, bool covers)
    {
        string triggers = (covers ? """{"on":"covers","level":"warn","paths":["docs/source"]},""" : string.Empty)
            + $$"""{"on":"release","level":"refuse","product":"{{product}}"}""";
        string title = path.Contains("guide", StringComparison.Ordinal) ? "Guide" : Path.GetFileNameWithoutExtension(path);
        CliHarness.Ok("--estate", root, "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, Path.GetFileNameWithoutExtension(path) + "-declaration.json",
            $$"""{"path":"{{path}}","title":"{{title}}","kind":"authored","source":"estate-file","triggers":[{{triggers}}],"checks":["links-resolve"],"clearedBy":"{{clearedBy}}","summary":"Describes the widget."}"""));
    }

    private static void Write(string root, string path, string text)
    {
        string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private static string Pin(string root, string path, string by, string summary, params string[] options) =>
        CliHarness.Ok(["--estate", root, "--json", "docs", "pin", path, "--by", by, "--summary", summary, .. options]).Id;

    private static string Review(string root, string path, string by) =>
        CliHarness.Ok("--estate", root, "--json", "docs", "review", path, "--by", by, "--outcome", "current", "--summary", "Read against the widget.").Id;

    private static JsonElement Row(string root, string path) =>
        CliHarness.Ok("--estate", root, "--json", "docs", "living").Json.EnumerateArray().Single(r => r.GetProperty("path").GetString() == path);

    // The document's own-content reason: a covers reason without a record that names the document itself.
    private static string OwnContent(JsonElement row) => row.GetProperty("reasons").EnumerateArray()
        .Select(r => r.GetProperty("summary").GetString()!)
        .Single(s => s.StartsWith(row.GetProperty("path").GetString()!, StringComparison.Ordinal));
}
