// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Due living documents derived from records and covered paths (the estate's design/living-documents.md §§ 5 and 6).</summary>
public class LivingDocumentDerivationTests
{
    private const string Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    [Fact]
    public void EachTriggerMakesItsDocumentDueUntilAClearanceNewerThanIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope declared = estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{product.Id}}"},{"on":"upgrade","level":"remind"},{"on":"decision","level":"warn"},{"on":"accepted","level":"remind","keys":["W-1"]}],"summary":"Follows the releases."}""");
        Assert.False(Single(estate).Due);

        // A publication of the product: due at refuse, and the product's release gate closes.
        RecordEnvelope release = estate.Add("release", estate.BuilderId, Release("published"), new RecordReference("product", product.Id, null));
        LivingDocuments.State state = Single(estate);
        Assert.Equal("refuse", state.Level);
        Assert.Equal(release.Id, Assert.Single(state.Reasons).Record);
        Assert.Single(LivingDocuments.Gate(estate.Load(), product.Id));
        Assert.Empty(LivingDocuments.Gate(estate.Load(), estate.Estate.Id));
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.due" && f.Location == "docs/guide.md");

        // A new revision of the estate file clears it; an upgrade after that makes it due again, at remind, which no check reports.
        estate.Add("document-revision", estate.BuilderId, $$"""{"path":"docs/guide.md","title":"Guide","hash":"{{Hash}}","bytes":10,"mediaType":"text/markdown","revision":1,"summary":"For the release."}""");
        Assert.False(Single(estate).Due);
        estate.Add("migration", estate.BuilderId, """{"fromPin":"release 0.1.0","toPin":"release 0.2.0","fromFormat":2,"toFormat":2,"migrations":[],"changes":["manifest pinned to release 0.2.0"]}""");
        Assert.Equal("remind", Single(estate).Level);
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code == "document.due");

        // A review that finds it stale does not clear it; one that finds it current does.
        RecordEnvelope latest = estate.Load().Latest(declared);
        estate.Add("document-review", estate.OperatorId, Review("needs-change", "operator"), latest.Reference);
        Assert.True(Single(estate).Due);
        estate.Add("document-review", estate.OperatorId, Review("current", "operator"), latest.Reference);
        Assert.False(Single(estate).Due);

        // A decision naming it, by path or by declaration, and an acceptance of a named work order.
        estate.Add("decision", estate.BuilderId, Decision(estate, """ ,"documents":["docs/guide.md"]"""));
        Assert.Equal("warn", Single(estate).Level);
        estate.Add("document-review", estate.OperatorId, Review("current", "operator"), latest.Reference);
        estate.Add("decision", estate.BuilderId, Decision(estate, $$""" ,"documents":["{{declared.Id}}"]"""));
        Assert.Equal("warn", Single(estate).Level);
        estate.Add("document-review", estate.OperatorId, Review("current", "operator"), latest.Reference);
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"x","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""");
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", order.Reference);
        Assert.Equal("accepted", Assert.Single(Single(estate).Reasons).On);

        // Status says so, in text and in its JSON.
        EstateModel model = estate.Load();
        Assert.Contains("Living documents: 1 due of 1 declared", EstateStatus.Render(model, EstateChecks.Run(model)), StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(EstateStatus.RenderJson(model, EstateChecks.Run(model)));
        Assert.Equal("due", json.RootElement.GetProperty("documents")[0].GetProperty("state").GetString());
    }

    [Fact]
    public void CoveredPathsAreComparedWithTheLatestReviewIncludingUncommittedChanges()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# Guide\n");
        estate.WriteFile("docs/source/a.txt", "a\n");
        RecordEnvelope declared = estate.Add("living-document", estate.OperatorId, """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"covers","level":"warn","paths":["docs/source","docs/absent"]}],"summary":"Describes the source."}""");
        Assert.Contains("no review", Assert.Single(Single(estate).Reasons).Summary, StringComparison.Ordinal);

        string identity = LivingDocuments.CoveredIdentity(estate.Root, ["docs/source", "docs/absent"]);
        estate.Add("document-review", estate.OperatorId, Review("current", "operator", $$""","covered":"{{identity}}" """), declared.Reference);
        Assert.False(Single(estate).Due);

        // An edit not yet committed makes it due; a new revision of the document does not clear covered paths, only a review does.
        estate.WriteFile("docs/source/a.txt", "changed\n");
        Assert.Contains("changed after the review", Assert.Single(Single(estate).Reasons).Summary, StringComparison.Ordinal);
        estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","hash":"{{Hash}}","bytes":10,"mediaType":"text/markdown","revision":1,"summary":"x"}""");
        Assert.True(Single(estate).Due);

        // The identity is the same whatever order the files were written in, and it changes when a covered path appears.
        Assert.NotEqual(identity, LivingDocuments.CoveredIdentity(estate.Root, ["docs/source", "docs/absent"]));
        estate.WriteFile("docs/source/a.txt", "a\n");
        Assert.Equal(identity, LivingDocuments.CoveredIdentity(estate.Root, ["docs/absent", "docs/source"]));
        estate.WriteFile("docs/absent", "now present\n");
        Assert.NotEqual(identity, LivingDocuments.CoveredIdentity(estate.Root, ["docs/source", "docs/absent"]));
    }

    [Fact]
    public void WithinOneSecondRecordsAreOrderedByIdentifierAndARevisedDeclarationKeepsItsClearances()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope declared = estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"warn","product":"{{product.Id}}"}],"summary":"x"}""");
        DateTimeOffset second = estate.Clock.AddSeconds(5);
        string[] ids = [Ulid.NewUlid(second).Value, Ulid.NewUlid(second).Value];
        Array.Sort(ids, StringComparer.Ordinal);
        Write(estate, "release", estate.BuilderId, Release("published"), new RecordReference("product", product.Id, null), second, ids[0]);
        Write(estate, "document-review", estate.OperatorId, Review("current", "operator"), declared.Reference, second, ids[1]);
        Assert.False(Single(estate).Due);

        // A revision of the declaration is still the same document: its clearances count.
        estate.Add("living-document", estate.OperatorId, declared.Body.GetRawText().Replace("\"warn\"", "\"refuse\"", StringComparison.Ordinal), supersedes: declared.Id);
        Assert.False(Single(estate).Due);
    }

    [Fact]
    public void AContributionNoInstalledPackProvidesIsShownAndClosesTheGateAtRefuse()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/page.md","title":"Page","kind":"authored","source":"vericairn.example/never-installed","triggers":[{"on":"release","level":"refuse","product":"{{product.Id}}"},{"on":"vericairn.example/page-changed","level":"warn"}],"summary":"x"}""");
        LivingDocuments.State state = Single(estate);
        Assert.False(state.Due);
        Assert.Equal(["vericairn.example/never-installed", "vericairn.example/page-changed"], state.Unavailable);
        Assert.True(state.Closes);
        Assert.Single(LivingDocuments.Gate(estate.Load(), product.Id));
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.unavailable" && f.Message.Contains("closes its product's release gate", StringComparison.Ordinal));

        // A pack that registers a contribution makes it available.
        LivingDocuments.Register("vericairn.example/registered-here");
        Assert.True(LivingDocuments.IsAvailable("vericairn.example/registered-here"));
        Assert.True(LivingDocuments.IsAvailable("vericairn.mcp/orientation"));
        Assert.False(LivingDocuments.IsAvailable("vericairn.example/never-installed"));
    }

    private static LivingDocuments.State Single(TestEstate estate) => Assert.Single(LivingDocuments.Derive(estate.Load()));

    private static void Write(TestEstate estate, string type, string by, string bodyJson, RecordReference subject, DateTimeOffset at, string id)
    {
        using JsonDocument body = JsonDocument.Parse(bodyJson);
        RecordAuthor.Write(estate.Root, RecordAuthor.Create(new RecordDraft(type, by, body.RootElement, subject, null, at, id, 2)));
    }

    private static RecordEnvelope Product(TestEstate estate) =>
        estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");

    private static string Release(string status) =>
        $$"""{"version":"0.2.0","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"{{status}}","publishedAt":"2026-10-13T20:00:00Z","url":"https://example.invalid/releases/0.2.0","artifacts":[],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";

    private static string Review(string outcome, string independence, string extra = "") =>
        $$"""{"outcome":"{{outcome}}","content":{"hash":"{{Hash}}","bytes":10},"answers":[],"independence":"{{independence}}","summary":"Read it.","findings":[],"checks":[]{{extra}}}""";

    private static string Decision(TestEstate estate, string extra) =>
        $$"""{"title":"Change","statement":"Change it.","summary":"Change.","synthesis":"The operator changes it.","effects":["change"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]{{extra}}}""";
}
