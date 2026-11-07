// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class AcceptanceBasisOutcomeTests
{
    public const string Code = "acceptance.basis-outcome";

    [Theory]
    [InlineData("findings")]
    [InlineData("fail")]
    public void AnEarlierNonOperatorAcceptanceOnAReviewThatDidNotPassKeepsItsReadingAsInformation(string outcome)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = OrdinaryWorkWithReview(estate, outcome);
        RecordEnvelope acceptance = estate.Add("acceptance", estate.BuilderId, Body(review.Id, self: true), order.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(acceptance.ExpectedPath + "#body.basis", finding.Location);
        Assert.Contains($"{review.Id} ({outcome})", finding.Message, StringComparison.Ordinal);
        // Its earlier reading is kept: the work order still reads as accepted.
        Assert.Equal("accepted", Assert.Single(EstateStatus.WorkItems(model)).Status);
    }

    [Theory]
    [InlineData("pass", true)]
    [InlineData("findings", false)]
    [InlineData("fail", false)]
    public void TheOperatorsAcceptanceAndOneOnPassingReviewsCarryNoFinding(string outcome, bool contributor)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = OrdinaryWorkWithReview(estate, outcome);
        estate.Add("acceptance", contributor ? estate.BuilderId : estate.OperatorId, Body(review.Id, self: contributor), order.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
        Assert.Equal("accepted", Assert.Single(EstateStatus.WorkItems(model)).Status);
    }

    public static (RecordEnvelope Order, RecordEnvelope Review) OrdinaryWorkWithReview(TestEstate estate, string outcome)
    {
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Build and accept ordinary work yourself.","summary":"Delegation.","synthesis":"Standing delegation for ordinary work.","effects":["builder engineers and self-accepts ordinary work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Ordinary work","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        string findings = outcome == "pass" ? "[]" : """[{"severity":"warning","summary":"Something to fix.","location":"docs"}]""";
        RecordEnvelope review = estate.Add("review", estate.OperatorId, $$"""{"independence":"operator","outcome":"{{outcome}}","summary":"Reviewed the exact order.","findings":{{findings}}}""", order.Reference);
        return (order, review);
    }

    public static string Body(string review, bool self) => $$"""{"statement":"I accept W-1 on the recorded review.","basis":["{{review}}"],"self":{{(self ? "true" : "false")}}}""";
}
