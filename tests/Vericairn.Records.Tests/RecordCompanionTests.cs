// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class RecordCompanionTests
{
    [Fact]
    public void EveryAdmittedTypeAndNestedCollectionHasInstalledItemAndListCoverage()
    {
        Assert.Equal(RecordTypeRegistry.All.Select(t => t.Name).Order(StringComparer.Ordinal), RecordCompanions.Types.Order(StringComparer.Ordinal));
        Assert.Empty(DocumentCoverage.Check(DocumentCoverage.RecordKinds, DocumentCoverage.All.Where(d => d.Kind.StartsWith("record/", StringComparison.Ordinal))));
        foreach (DocumentManagedKind kind in DocumentCoverage.RecordKinds)
        {
            DocumentCoverageDescriptor descriptor = Assert.Single(DocumentCoverage.All, d => d.Kind == kind.Id);
            Assert.NotNull(descriptor.Item);
            Assert.NotNull(descriptor.List);
            Assert.Contains(DocumentCoverage.Check(DocumentCoverage.RecordKinds, RecordCompanions.Coverage.Where(d => d.Kind != kind.Id)), p => p == "Missing companion coverage: " + kind.Id);
        }
        var addedType = new DocumentManagedKind("record/future-kind", "record/future-kind");
        var addedNested = new DocumentManagedKind("record/result#duties[].future", "record/result#duties[].future");
        Assert.Equal(2, DocumentCoverage.Check([.. DocumentCoverage.RecordKinds, addedType, addedNested], RecordCompanions.Coverage).Length);
    }

    [Fact]
    public void RetainedFixturesCannotGainAnUncoveredCollectionOrDropARecordedField()
    {
        string fixtureRoot = Path.Combine(FindRepository(), "tests", "fixtures");
        int records = 0;
        foreach (string file in Directory.EnumerateFiles(fixtureRoot, "*.json", SearchOption.AllDirectories))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("body", out JsonElement body)
                || !json.TryGetProperty("type", out JsonElement type)) continue;
            foreach (string field in Arrays(body, ""))
                Assert.Contains(DocumentCoverage.RecordKinds, k => k.Id == "record/" + type.GetString() + "#" + field);
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), Path.GetRelativePath(fixtureRoot, file).Replace('\\', '/'), findings);
            Assert.NotNull(record);
            DocumentRendition result = Render([DocumentSourceSnapshot.Record(record)]);
            Assert.Equal(DocumentGenerationStatus.Success, result.Status);
            string text = Encoding.UTF8.GetString(result.Bytes.AsSpan());
            foreach (JsonProperty field in body.EnumerateObject()) Assert.Contains("## " + RecordCompanionText.Label(field.Name) + "\n", text, StringComparison.Ordinal);
            records++;
        }
        Assert.True(records > 50);
    }

    [Fact]
    public void DutyClaimsAndReviewEvidenceKeepUnitsScopeAndExactReferences()
    {
        EstateModel model = EstateLoader.Load(Path.Combine(FindRepository(), "tests", "fixtures", "format-3-policy-estate"));
        RecordEnvelope result = model.OfType("result").First(r => r.Body.TryGetProperty("duties", out _));
        string text = System.Net.WebUtility.HtmlDecode(Text(Render([DocumentSourceSnapshot.Record(result)])));
        Assert.Contains("Contributor claim", text, StringComparison.Ordinal);
        Assert.Contains("Scope", text, StringComparison.Ordinal);
        Assert.Contains("Proofs", text, StringComparison.Ordinal);
        Assert.Contains(result.Body.GetProperty("duties")[0].GetProperty("policy").GetProperty("hash").GetString()!, text, StringComparison.Ordinal);
        RecordEnvelope review = model.OfType("review").First(r => r.Body.TryGetProperty("read", out _));
        string reviewed = System.Net.WebUtility.HtmlDecode(Text(Render([DocumentSourceSnapshot.Record(review)])));
        Assert.Contains("Attributed review", reviewed, StringComparison.Ordinal);
        Assert.Contains(review.Body.GetProperty("read")[0].GetProperty("hash").GetString()!, reviewed, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedListsRetainPositionAndCanSelectAnExactMember()
    {
        using var f = new PolicyFixture();
        RecordEnvelope result = f.Result();
        DocumentSourceSnapshot source = DocumentSourceSnapshot.Record(result);
        string list = System.Net.WebUtility.HtmlDecode(Text(Render([source], "list", "checks")));
        Assert.Contains("Checks[1] / Name: inventory", list, StringComparison.Ordinal);
        string item = Text(Render([source], "item", "checks", "1"));
        Assert.Contains("inventory", item, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render([source], "item", "checks", "2").Status);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render([source], "list", "unregistered").Status);
        Assert.Contains("None (complete selected collection)", Text(Render([source], "list", "outputs")), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenContributionMapsKeepNestedValuesWithoutClaimingToEvaluateThem()
    {
        DocumentSourceSnapshot source = Source("observation", new JsonObject { ["facts"] = new JsonObject { ["checks"] = new JsonArray("first", "second") } });
        string text = Text(Render([source], "list", "facts"));
        Assert.Contains("owning evaluator", text, StringComparison.Ordinal);
        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCapsAreNotZeroAndNoTypedPlacementIsInferredFromProse()
    {
        DocumentSourceSnapshot operation = Source("operation", new JsonObject
        {
            ["cap"] = new JsonObject { ["wallMinutes"] = 3, ["currency"] = new JsonObject { ["amount"] = 50, ["unit"] = "USD" } },
            ["usage"] = new JsonObject { ["unreported"] = new JsonArray("tokens") },
        });
        string text = Text(Render([operation]));
        Assert.Contains("absent unit is not unlimited", text, StringComparison.Ordinal);
        Assert.Contains("minor units", text, StringComparison.Ordinal);
        Assert.Contains("never counted as zero", text, StringComparison.Ordinal);
        DocumentSourceSnapshot work = Source("work-order", new JsonObject { ["intent"] = "Release 1.2.3 might carry this work." });
        Assert.Contains("No typed placement recorded", Text(Render([work])), StringComparison.Ordinal);
    }

    [Fact]
    public void HostileSourceWordingRemainsLiteralAndLongValuesAreFullyPaged()
    {
        string hostile = "<script>|[link](bad)` & 東京";
        DocumentSourceSnapshot source = Source("decision", new JsonObject { ["title"] = "Readable", ["statement"] = hostile + new string('z', 1100000) + "ENDMARK" });
        var complete = new StringBuilder();
        int pages = 0;
        for (int page = 1; page < 20; page++)
        {
            DocumentRendition rendered = Render([source], page: page);
            if (rendered.Status == DocumentGenerationStatus.Unavailable) break;
            Assert.Equal(DocumentGenerationStatus.Success, rendered.Status);
            Assert.InRange(rendered.Bytes.Length, 1, 1048576);
            string text = Text(rendered);
            Assert.Contains("Partial page", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", text, StringComparison.Ordinal);
            complete.Append(text); pages++;
        }
        Assert.True(pages > 1);
        Assert.Contains("ENDMARK", complete.ToString(), StringComparison.Ordinal);
        Assert.Equal(1100000, System.Text.RegularExpressions.Regex.Matches(complete.ToString(), "z{100,}").Sum(m => m.Length));
    }

    internal static DocumentSourceSnapshot Source(string type, JsonObject body) => DocumentSourceSnapshot.Record(RecordAuthor.Create(new RecordDraft(
        type, "01ARZ3NDEKTSV4RRFFQ69G5FAV", JsonSerializer.SerializeToElement(body), At: new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
        Id: "01ARZ3NDEKTSV4RRFFQ69G5FAW", Format: 3)).Envelope);

    internal static DocumentRendition Render(ImmutableArray<DocumentSourceSnapshot> sources, string selection = "item", string field = "body", string index = "all", int page = 1)
    {
        IDocumentGenerator generator = DocumentGenerators.Find("vericairn.records/items")!;
        var parameters = new Dictionary<string, string>
        {
            ["format"] = "1", ["audience"] = "Internal", ["selection"] = selection, ["field"] = field, ["index"] = index, ["page"] = page.ToString(CultureInfo.InvariantCulture),
        }.ToImmutableDictionary(StringComparer.Ordinal);
        return DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, selection, null, sources));
    }

    internal static string Text(DocumentRendition result)
    {
        Assert.Equal(DocumentGenerationStatus.Success, result.Status);
        return Encoding.UTF8.GetString(result.Bytes.AsSpan());
    }

    internal static string FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Vericairn.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The test repository is unavailable.");
    }

    private static IEnumerable<string> Arrays(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            yield return path;
            foreach (JsonElement item in value.EnumerateArray()) foreach (string nested in Arrays(item, path + "[]")) yield return nested;
        }
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in value.EnumerateObject()) foreach (string nested in Arrays(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name)) yield return nested;
    }
}
