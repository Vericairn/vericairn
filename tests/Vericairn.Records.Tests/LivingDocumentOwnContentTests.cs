// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class LivingDocumentOwnContentTests
{
    [Theory]
    [InlineData("remind")]
    [InlineData("warn")]
    [InlineData("refuse")]
    public void OwnBytesAreCoveredWithoutADeclaredCoversTriggerAtTheHighestDeclaredLevel(string level)
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# Guide\n");
        RecordEnvelope product = Product(estate);
        RecordEnvelope declaration = Declare(estate, product, level);
        Assert.Equal(["docs/guide.md"], LivingDocuments.CoveredPaths(declaration));
        RecordEnvelope review = Review(estate, declaration, "current");
        Assert.False(State(estate).Due);

        // The only changed bytes are a line ending, with no release or decision after the review.
        estate.WriteFile("docs/guide.md", "# Guide\r\n");
        LivingDocuments.State state = State(estate);
        LivingDocuments.Reason reason = Assert.Single(state.Reasons);
        Assert.Equal(level, state.Level);
        Assert.Equal("covers", reason.On);
        Assert.Null(reason.Record);
        Assert.Contains("docs/guide.md changed after its review " + review.Id, reason.Summary, StringComparison.Ordinal);
        Assert.Contains(ContentHash.Sha256("# Guide\n"u8), reason.Summary, StringComparison.Ordinal);
        Assert.Contains(ContentHash.Sha256("# Guide\r\n"u8), reason.Summary, StringComparison.Ordinal);
        EstateModel model = estate.Load();
        FindingList checks = EstateChecks.Run(model);
        Assert.Equal(level == "refuse", LivingDocuments.Gate(model, product.Id).Count == 1);
        Assert.Contains(reason.Summary, EstateStatus.Render(model, checks), StringComparison.Ordinal);
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, checks));
        Assert.Equal("due", status.RootElement.GetProperty("documents")[0].GetProperty("state").GetString());
        Assert.Equal(reason.Summary, status.RootElement.GetProperty("documents")[0].GetProperty("reasons")[0].GetProperty("summary").GetString());
        if (level != "remind")
        {
            Assert.Contains(checks, f => f.Code == "document.due" && f.Message.Contains(reason.Summary, StringComparison.Ordinal));
            Assert.Contains(reason.Summary, ProgressReport.Render(model, checks), StringComparison.Ordinal);
            using JsonDocument progress = JsonDocument.Parse(ProgressReport.RenderJson(model, checks));
            Assert.Contains(progress.RootElement.GetProperty("standing").GetProperty("check").GetProperty("findings").EnumerateArray(),
                f => f.GetProperty("code").GetString() == "document.due" && f.GetProperty("message").GetString()!.Contains(reason.Summary, StringComparison.Ordinal));
        }

        Review(estate, declaration, "needs-change");
        Assert.Equal(review.Id, State(estate).Clearance!.Id);
        Assert.True(State(estate).Due);
        RecordEnvelope cleared = Review(estate, declaration, "current");
        Assert.False(State(estate).Due);
        Assert.Equal(cleared.Id, State(estate).Clearance!.Id);
        Assert.Empty(LivingDocuments.Gate(estate.Load(), product.Id));
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code == "document.due");
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void OwnContentReasonsPreserveTheOrderOfDeclaredTriggers()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# Guide\n");
        RecordEnvelope declaration = Declare(estate, Product(estate), "refuse");
        Review(estate, declaration, "current");
        estate.WriteFile("docs/guide.md", "# Changed guide\n");
        estate.Add("decision", estate.OperatorId,
            $$"""{"title":"Change the guide","statement":"Revise the guide.","summary":"Update the guide.","synthesis":"The operator changes the guide.","effects":["Update the guide."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[],"documents":["docs/guide.md"]}""");

        Assert.Equal(["decision", "covers"], State(estate).Reasons.Select(r => r.On));
        Assert.Equal("refuse", State(estate).Level);
    }

    [Fact]
    public void ADeletedDocumentIsDueAndOnlyTheLatestClearedBytesCanRestoreIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# First\n");
        RecordEnvelope declaration = Declare(estate, Product(estate), "refuse");
        Review(estate, declaration, "current");
        estate.WriteFile("docs/guide.md", "# Second\n");
        Review(estate, declaration, "current");
        File.Delete(Path.Combine(estate.Root, "docs", "guide.md"));
        Assert.Contains("is not in the estate's working tree", Assert.Single(State(estate).Reasons).Summary, StringComparison.Ordinal);
        estate.WriteFile("docs/guide.md", "# First\n");
        Assert.True(State(estate).Closes);
        estate.WriteFile("docs/guide.md", "# Second\n");
        Assert.False(State(estate).Due);
    }

    [Fact]
    public void AnEstateRevisionClearsItsOwnBytesButALaterEditIsDueAgain()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# Guide\n");
        RecordEnvelope declaration = Declare(estate, Product(estate), "refuse");
        Review(estate, declaration, "current");
        estate.WriteFile("docs/guide.md", "# New guide\n");
        Assert.True(State(estate).Due);
        string hash = ContentHash.Sha256("# New guide\n"u8);
        RecordEnvelope revision = estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","hash":"{{hash}}","bytes":12,"mediaType":"text/markdown","revision":1,"summary":"Updated guide."}""");
        Assert.False(State(estate).Due);
        estate.WriteFile("docs/guide.md", "# Another guide\n");
        Assert.Contains("changed after its revision " + revision.Id, Assert.Single(State(estate).Reasons).Summary, StringComparison.Ordinal);
    }

    private static LivingDocuments.State State(TestEstate estate) => Assert.Single(LivingDocuments.Derive(estate.Load()));

    private static RecordEnvelope Product(TestEstate estate) => estate.Add("product", estate.OperatorId,
        """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");

    private static RecordEnvelope Declare(TestEstate estate, RecordEnvelope product, string level) => estate.Add("living-document", estate.OperatorId,
        $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"{{level}}","product":"{{product.Id}}"},{"on":"decision","level":"remind"}],"summary":"Follows the releases."}""");

    private static RecordEnvelope Review(TestEstate estate, RecordEnvelope declaration, string outcome)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(estate.Root, "docs", "guide.md"));
        return estate.Add("document-review", estate.OperatorId,
            $$"""{"outcome":"{{outcome}}","content":{"hash":"{{ContentHash.Sha256(bytes)}}","bytes":{{bytes.Length}}},"answers":[{"on":"covers"}],"independence":"operator","summary":"Read the guide.","findings":[],"checks":[]}""", declaration.Reference);
    }
}
