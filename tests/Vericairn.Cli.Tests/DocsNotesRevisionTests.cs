// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>Release notes show current wording at the window end and retain the revisions they intentionally enumerate.</summary>
[Collection("cli")]
public class DocsNotesRevisionTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void ADecisionAppearsOnceNamingEveryReplacedRevision(int revisionCount, int beforeWindow)
    {
        using var notes = new NotesFixture();
        var revisions = new List<RecordEnvelope>();
        for (int i = 0; i < revisionCount; i++)
            revisions.Add(notes.Decision($"Wording {i + 1}", revisions.LastOrDefault()?.Id));

        RecordEnvelope current = revisions[^1];
        string rendered = notes.Render(current.At, beforeWindow == 0 ? null : revisions[beforeWindow - 1].At);
        string entry = Assert.Single(Items(rendered, "Decisions"));

        Assert.Contains($"**Wording {revisionCount}**", entry, StringComparison.Ordinal);
        Assert.Contains($"decision {current.Id}", entry, StringComparison.Ordinal);
        Assert.Equal(1, entry.Split(current.Id, StringSplitOptions.None).Length - 1);
        if (revisionCount == 1)
            Assert.DoesNotContain("revised", entry, StringComparison.Ordinal);
        else
            Assert.Contains($"(revised; replaces {string.Join(", ", revisions.SkipLast(1).Select(r => r.Id))})", entry, StringComparison.Ordinal);
        foreach (RecordEnvelope earlier in revisions.SkipLast(1))
        {
            Assert.DoesNotContain(EstateModel.BodyString(earlier, "title")!, rendered, StringComparison.Ordinal);
            Assert.Equal(1, entry.Split(earlier.Id, StringSplitOptions.None).Length - 1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARevisionAfterTheWindowLeavesTheWordingCurrentAtItsEnd(bool published)
    {
        using var notes = new NotesFixture();
        RecordEnvelope first = notes.Decision("Original wording");
        RecordEnvelope second = notes.Decision("Wording at the end", first.Id);
        string end = published ? notes.Publish("0.2.0").At : second.At;
        RecordEnvelope later = notes.Decision("Later wording", second.Id);

        string rendered = notes.Render(end);
        string entry = Assert.Single(Items(rendered, "Decisions"));

        Assert.Contains($"decision {second.Id}", entry, StringComparison.Ordinal);
        Assert.Contains("**Wording at the end**", entry, StringComparison.Ordinal);
        Assert.Contains($"(revised; replaces {first.Id})", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Original wording", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Later wording", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(later.Id, rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("work-order", "Work orders opened or revised")]
    [InlineData("principal", "Identity and policy")]
    [InlineData("acceptance", "Milestone and release acceptances")]
    [InlineData("migration", "Upgrading")]
    public void OtherRecordSectionsShowOneCurrentEntryAndItsReplacedId(string type, string section)
    {
        using var notes = new NotesFixture();
        TestEstate estate = notes.Estate;
        RecordReference? subject = type == "acceptance" ? estate.Add("work-order", estate.BuilderId, OrderBody("Notes")).Reference : null;
        string body = type switch
        {
            "work-order" => OrderBody("Original wording"),
            "principal" => """{"kind":"human","name":"Original wording","roles":["reviewer"]}""",
            "acceptance" => """{"statement":"Original wording","basis":[],"self":false}""",
            _ => """{"fromPin":"release 0.1.0","toPin":"release 0.2.0","fromFormat":2,"toFormat":2,"migrations":[],"changes":["Original wording"]}""",
        };
        RecordEnvelope original = estate.Add(type, estate.OperatorId, body, subject);
        RecordEnvelope current = estate.Add(type, estate.OperatorId, body.Replace("Original wording", "Corrected wording", StringComparison.Ordinal), subject, supersedes: original.Id);
        estate.Add(type, estate.OperatorId, body.Replace("Original wording", "Later wording", StringComparison.Ordinal), subject, supersedes: current.Id);

        string entry = Assert.Single(Items(notes.Render(current.At), section));

        Assert.Contains("Corrected wording", entry, StringComparison.Ordinal);
        Assert.Contains(current.Id, entry, StringComparison.Ordinal);
        Assert.Contains($"(revised; replaces {original.Id})", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Original wording", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Later wording", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentResultsKeepTheirExistingSelectionAndAcceptanceRules(bool accepted)
    {
        using var notes = new NotesFixture();
        TestEstate estate = notes.Estate;
        RecordEnvelope order = notes.Order();
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"Render notes.","sources":[],"permittedChanges":["notes"],"prohibitedEffects":[],"workspace":["test estate"],"limits":[],"expectedOutputs":["notes"],"reviewCriteria":["Revisions named."],"recovery":"Read the notes."}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"test estate","note":"Render notes."}""", assignment.Reference);
        const string Body = """{"outcome":"completed","summary":"Original wording","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review."}""";
        RecordEnvelope original = estate.Add("result", estate.BuilderId, Body, attempt.Reference);
        RecordEnvelope current = estate.Add("result", estate.BuilderId, Body.Replace("Original wording", "Corrected wording", StringComparison.Ordinal), attempt.Reference, supersedes: original.Id);
        if (accepted)
            estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", current.Reference);

        string rendered = notes.Render(RecordParser.FormatTimestamp(estate.Clock));
        string result = Assert.Single(rendered.Split('\n'), line => line.StartsWith("Result ", StringComparison.Ordinal));

        Assert.Contains($"Result {current.Id}", result, StringComparison.Ordinal);
        Assert.Contains("Corrected wording", result, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Id, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Original wording", rendered, StringComparison.Ordinal);
        Assert.Contains(accepted ? "## Work accepted\n\n### W-1:" : "## Work recorded, not yet accepted", rendered, StringComparison.Ordinal);
        if (accepted)
            Assert.DoesNotContain("## Work recorded, not yet accepted", rendered, StringComparison.Ordinal);
        else
            Assert.Contains("## Work accepted\n\nNone in this window.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentAndReleaseHistorySectionsStillListEachRevision()
    {
        using var notes = new NotesFixture();
        TestEstate estate = notes.Estate;
        RecordEnvelope first = estate.Add("document-revision", estate.OperatorId, DocumentBody(1, "Original document wording"));
        estate.Add("document-revision", estate.OperatorId, DocumentBody(2, "Corrected document wording"), supersedes: first.Id);
        RecordEnvelope published = notes.Publish("0.2.0");

        string rendered = notes.Render(published.At, notes.Product.At);

        Assert.Equal([
            "- Guide revision 1 (`docs/guide.md`): Original document wording",
            "- Guide revision 2 (`docs/guide.md`): Corrected document wording",
        ], Items(rendered, "Design documents revised"));
        foreach ((string version, string section) in new[] { ("0.1.0", "Other releases recorded in this window"), ("0.2.0", "The release itself") })
        {
            string[] entries = Items(rendered, section);
            Assert.Equal(2, entries.Length);
            Assert.StartsWith($"- {version} candidate (", entries[0], StringComparison.Ordinal);
            Assert.StartsWith($"- {version} published (", entries[1], StringComparison.Ordinal);
            Assert.All(entries, entry => Assert.DoesNotContain("replaces", entry, StringComparison.Ordinal));
        }
    }

    private static string OrderBody(string title) =>
        $$"""{"key":"W-1","kind":"ordinary","title":"{{title}}","intent":"Exercise the notes.","scope":["notes"],"exitQuestion":"Done?","inputs":[],"deliverables":["notes"]}""";

    private static string DocumentBody(int revision, string summary) =>
        $$"""{"path":"docs/guide.md","title":"Guide","hash":"sha256:{{new string('1', 64)}}","bytes":10,"mediaType":"text/markdown","revision":{{revision}},"summary":"{{summary}}"}""";

    private static string[] Items(string notes, string section) =>
        notes.Split("## " + section + "\n", StringSplitOptions.None)[1].Split("\n## ", StringSplitOptions.None)[0]
            .Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToArray();

    private sealed class NotesFixture : IDisposable
    {
        private readonly string directory = CliHarness.TempDirectory("notes-revisions");
        private readonly RecordEnvelope previous;
        public TestEstate Estate { get; } = new(recordFormat: 2);
        public RecordEnvelope Product { get; }

        public NotesFixture()
        {
            Product = Estate.Add("product", Estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
            previous = Publish("0.1.0");
        }

        public RecordEnvelope Decision(string title, string? supersedes = null) =>
            Estate.Add("decision", Estate.OperatorId, $$"""{"title":"{{title}}","stake":"low","notAuthorized":[],"statement":"Use the recorded wording.","summary":"{{title}}.","synthesis":"Release note correction.","effects":["Use the wording in the notes."],"affects":[],"decidedBy":"{{Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""", supersedes: supersedes);

        public RecordEnvelope Order() => Estate.Add("work-order", Estate.OperatorId, OrderBody("Notes"));

        public RecordEnvelope Publish(string version)
        {
            string body = $$"""{"version":"{{version}}","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[{"name":"example-{{version}}-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
            RecordEnvelope candidate = Estate.Add("release", Estate.OperatorId, body, Product.Reference);
            Estate.Add("acceptance", Estate.OperatorId, """{"statement":"Accepted release.","basis":[],"self":true}""", candidate.Reference);
            string published = body.Replace("\"status\":\"candidate\"", $"\"status\":\"published\",\"publishedAt\":\"{RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1))}\",\"url\":\"https://example.invalid/releases/{version}\"", StringComparison.Ordinal);
            return Estate.Add("release", Estate.OperatorId, published, Product.Reference, supersedes: candidate.Id);
        }

        public string Render(string end, string? after = null)
        {
            EstateModel model = Estate.Load();
            Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
            string start = after ?? previous.At;
            string earlier = Path.Combine(directory, "0.1.0");
            Directory.CreateDirectory(earlier);
            File.WriteAllText(Path.Combine(earlier, "manifest.json"), JsonSerializer.Serialize(new { format = Docs.ManifestFormat, version = "0.1.0", notes = new { from = Estate.Estate.At, to = start } }));
            string output = Path.Combine(directory, "0.2.0");
            using var response = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            int code = Docs.Notes(new CommandContext(response, error), Estate.Root, "0.1.0", "0.2.0", output, null, json: true,
                at: DateTimeOffset.Parse(end, CultureInfo.InvariantCulture).AddSeconds(1));
            Assert.True(code == 0, error.ToString());
            using JsonDocument written = JsonDocument.Parse(response.ToString());
            Assert.Equal(ReleaseContents.Window(model, start, end).Count, written.RootElement.GetProperty("records").GetInt32());
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert.Equal(start, manifest.RootElement.GetProperty("notes").GetProperty("from").GetString());
            Assert.Equal(end, manifest.RootElement.GetProperty("notes").GetProperty("to").GetString());
            return File.ReadAllText(Path.Combine(output, "release-notes.md"));
        }

        public void Dispose()
        {
            Estate.Dispose();
            CliHarness.Delete(directory);
        }
    }
}
