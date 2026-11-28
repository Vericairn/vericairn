// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

public sealed class CompanionReadTests
{
    [Fact]
    [Trait("Journey", "D19-A")]
    public void ExactRevisionNeverFollowsItsSuccessorAndReadingWritesNoRecords()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest("vericairn.records/items", "item", fixture.Decision.Id);
        CompanionResponse original = service.CompanionRender(request);
        Assert.Equal("success", original.Status);
        Assert.Equal("current", original.Currentness);
        Assert.Contains(fixture.Decision.Body.GetProperty("statement").GetString()!, WebUtility.HtmlDecode(original.Text), StringComparison.Ordinal);
        JsonObject body = fixture.DecisionBody();
        body["summary"] = "A different successor explanation.";
        fixture.Add("decision", body, supersedes: fixture.Decision.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        int records = fixture.Estate.Load().Records.Count;
        CompanionResponse historical = service.CompanionRender(request);
        Assert.Equal(original.Text, historical.Text);
        Assert.Equal(original.InputDigest, historical.InputDigest);
        Assert.Equal("historical", historical.Currentness);
        Assert.False(Assert.Single(historical.Sources).Current);
        Assert.Equal(fixture.Decision.ComputedHash, Assert.Single(historical.Sources).Hash);
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
        Assert.Empty(fixture.Estate.Load().OfType("document-revision"));
    }

    [Fact]
    public void ARecordWhoseStaleSubjectIsSettledReadsAsHistoricalWithTheSettlingRecord()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId,
            """{"key":"W-1","kind":"ordinary","title":"Widget","intent":"A widget.","scope":["path:widget/**"],"exitQuestion":"Done?","inputs":[],"deliverables":["widget"]}""");
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId,
            $$"""{"to":"{{estate.OperatorId}}","question":"Inspect?","recommendation":"Inspect.","consequence":"Work waits.","state":"open"}""", order.Reference);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId,
            $$"""{"title":"Inspection","statement":"Reviewed.","summary":"The work was reviewed.","synthesis":"Proceed.","effects":["Continue."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"{{ask.At}}","stake":"low","notAuthorized":[],"answers":["{{ask.Id}}"]}""");
        JsonObject revised = HistoryFixture.Body(order);
        revised["title"] = "Revised widget";
        estate.Add("work-order", estate.OperatorId, revised.ToJsonString(), supersedes: order.Id);
        Assert.Contains(estate.Load().Findings, f => f.Location == ask.ExpectedPath && f.Code == "record.subject-settled");

        using EstateService service = EstateService.Open(estate.Root);
        CompanionResponse read = service.CompanionRender(new CompanionRequest("vericairn.records/items", "item", ask.Id));
        Assert.Equal("success", read.Status);
        Assert.Equal("historical", read.Currentness);
        Assert.Contains(decision.Id, read.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Journey", "D19-A")]
    [Trait("Journey", "D19-D")]
    public void CollectionsRequireTheExactSnapshotAndKeepEmptyDistinctFromUnavailable()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        CompanionsResponse coverage = service.Companions();
        Assert.Equal("success", coverage.Status);
        Assert.Empty(coverage.Destinations);
        Assert.Contains(coverage.Generators, g => g.Id == "vericairn.records/decisions" && g.Pattern is not null);
        var request = new CompanionRequest("vericairn.records/releases", "current", Snapshot: coverage.Snapshot);
        CompanionResponse empty = service.CompanionRender(request);
        Assert.Equal("success", empty.Status);
        Assert.Contains("None (complete authorized selection)", empty.Text, StringComparison.Ordinal);
        Assert.Equal("error", service.CompanionRender(request with { Snapshot = null }).Status);
        fixture.Add("decision", fixture.DecisionBody());
        CompanionResponse stale = service.CompanionRender(request);
        Assert.Equal("unavailable", stale.Currentness);
        Assert.Null(stale.Text);
        Assert.Null(stale.InputDigest);
        Assert.Empty(stale.Sources);
    }

    [Theory]
    [InlineData("vericairn.records/items")]
    [InlineData("vericairn.records/decisions")]
    public void CollectionNavigationNamesOnlyTheExactRevisionsRenderedOnEachPage(string generator)
    {
        using var fixture = new PolicyFixture();
        foreach (int number in Enumerable.Range(0, 230))
        {
            JsonObject body = fixture.DecisionBody();
            body["title"] = number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('&', 196);
            fixture.Add("decision", body);
        }
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest(generator, "current", Snapshot: service.Companions().Snapshot);
        CompanionResponse first = service.CompanionRender(request);
        CompanionResponse second = service.CompanionRender(request with { Page = 2 });
        CompanionPage firstPage = Assert.IsType<CompanionPage>(first.Page);
        CompanionPage secondPage = Assert.IsType<CompanionPage>(second.Page);
        Assert.Equal((1, 2), (firstPage.Number, firstPage.Total));
        Assert.Equal((2, 2), (secondPage.Number, secondPage.Total));
        Assert.NotEmpty(firstPage.Items);
        Assert.NotEmpty(secondPage.Items);
        Assert.Empty(firstPage.Items.Intersect(secondPage.Items));
        Assert.Equal(first.Sources.Select(s => s.Identity).Order(StringComparer.Ordinal),
            firstPage.Items.Concat(secondPage.Items).Select(item => item.Revision).Order(StringComparer.Ordinal));
        foreach ((CompanionPage page, CompanionResponse response) in new[] { (firstPage, first), (secondPage, second) })
        {
            string rendered = WebUtility.HtmlDecode(response.Text)!;
            foreach (CompanionPageItem item in page.Items)
            {
                Assert.Contains(item.Revision, rendered, StringComparison.Ordinal);
                Assert.Contains(item.Hash, rendered, StringComparison.Ordinal);
                Assert.Contains(item.Title, rendered, StringComparison.Ordinal);
                Assert.Contains(response.Sources, s => s.Identity == item.Revision && s.Hash == item.Hash && s.RecordType == item.RecordType);
            }
        }
        Assert.Null(service.CompanionSources(request).Page);
        Assert.Null(service.CompanionRender(request with { Audience = "Public" }).Page);
    }

    [Fact]
    [Trait("Journey", "D19-A")]
    [Trait("Journey", "D19-F")]
    public void ManifestAndNestedSourceViewsRetainTheirExactBasis()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest("vericairn.estate/manifest", "item", Snapshot: service.Companions().ManifestSnapshot);
        CompanionResponse manifest = service.CompanionRender(request);
        Assert.Equal("success", manifest.Status);
        Assert.Contains("Manifest record format: 3", manifest.Text, StringComparison.Ordinal);
        Assert.Contains(manifest.Sources, s => s.Kind == "estate-manifest" && s.Hash == ContentHash.Sha256File(Path.Combine(fixture.Estate.Root, EstateLayout.ManifestPath)));
        var nested = new CompanionRequest("vericairn.records/items", "list", fixture.Decision.Id, Field: "effects");
        CompanionResponse sources = service.CompanionSources(nested);
        CompanionResponse rendered = service.CompanionRender(nested);
        Assert.Null(sources.Text);
        Assert.Equal(sources.Sources, rendered.Sources);
        Assert.Equal(sources.InputDigest, rendered.InputDigest);
        Assert.Contains("Apply the exact approved definition.", WebUtility.HtmlDecode(rendered.Text), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Journey", "D19-C")]
    [Trait("Journey", "D19-D")]
    public void CompareFindsMissingChangedAndUnexpectedMembersWithoutRepairOrPins()
    {
        using var fixture = new PolicyFixture();
        var declaration = fixture.Add("living-document", Declaration());
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        int records = fixture.Estate.Load().Records.Count;
        CompanionComparison missing = service.CompanionCompare(new(declaration.Id));
        Assert.Equal("missing", Assert.Single(missing.Differences).Change);
        Assert.Equal(missing.Differences, service.CompanionCompare(new("docs/registers/decisions")).Differences);
        Assert.False(Directory.Exists(Path.Combine(fixture.Estate.Root, "docs")));
        DocumentChangePlan repaired = DocumentRepairWriter.Repair(service.Session, declaration.Id);
        Assert.Empty(repaired.Problems);
        Assert.Equal("current", service.CompanionCompare(new(declaration.Id)).Currentness);
        string path = Assert.Single(repaired.Changes).Path;
        fixture.Estate.WriteFile(path, "Hand edit.\n");
        fixture.Estate.WriteFile("docs/registers/decisions/unexpected.md", "Unowned.\n");
        CompanionComparison different = service.CompanionCompare(new(declaration.Id));
        Assert.Equal("different", different.Currentness);
        Assert.Contains(different.Differences, d => d.Path == path && d.Change == "changed");
        Assert.Contains(different.Differences, d => d.Change == "unexpected");
        Assert.Equal("Hand edit.\n", File.ReadAllText(Path.Combine(fixture.Estate.Root, path)));
        Assert.Equal(records, fixture.Estate.Load().Records.Count);
        Assert.Empty(fixture.Estate.Load().OfType("document-revision"));
        Assert.Empty(BatchJournal.All(service.Session).Journals);
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("Members only")]
    [Trait("Journey", "D19-E")]
    public void UnsupportedAudienceRevealsNoTitlesHashesCountsOrChangedHiddenBasis(string audience)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest("vericairn.records/items", "item", fixture.Decision.Id, Audience: audience);
        CompanionResponse first = service.CompanionRender(request);
        fixture.Add("decision", fixture.DecisionBody());
        CompanionResponse changed = service.CompanionRender(request);
        Assert.Equal(first, changed);
        Assert.Equal("unavailable", first.Status);
        Assert.Null(first.Snapshot);
        Assert.Null(first.Generator);
        Assert.Empty(first.Sources);
        Assert.Null(first.Text);
    }

    [Fact]
    [Trait("Journey", "D19-E")]
    [Trait("Journey", "D19-H")]
    public void DeviceAccessAndLocalOnlyScopeAreRecheckedForEveryRead()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        string local = service.Companions().Generators.First(g => g.LocalOnly).Id;
        CompanionResponse refused = service.CompanionRender(new(local, "list", Snapshot: service.Companions().Snapshot));
        Assert.Equal("unavailable", refused.Status);
        Assert.Empty(refused.Sources);
        using var scope = service.ForDeviceRequest(fixture.BuilderDevice.Id, "unrecognized-key");
        Assert.Empty(service.Companions().Coverage);
        Assert.Null(service.CompanionRender(new("vericairn.records/items", "item", fixture.Decision.Id)).Text);
        Assert.Empty(service.CompanionSources(new("vericairn.records/items", "item", fixture.Decision.Id)).Sources);
        Assert.Equal("unavailable", service.CompanionCompare(new("unavailable")).Status);
    }

    internal static JsonObject Declaration() => new()
    {
        ["pathPattern"] = "docs/registers/decisions/{month}/{date}-{slug}-{id}.md", ["title"] = "Recorded decisions", ["kind"] = "generated",
        ["generator"] = new JsonObject { ["id"] = "vericairn.records/decisions", ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
        ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator", "links-resolve"), ["summary"] = "One file per recorded revision.",
    };
}
