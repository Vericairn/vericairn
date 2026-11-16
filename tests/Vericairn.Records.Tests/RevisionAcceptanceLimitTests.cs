// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The limits of reading an acceptance of a work order's current revision as accepting its current result (FIX-141): only a current
/// acceptance counts, only one its author could have given the result directly, and only one recorded before acceptances named a
/// result's exact hash, which the records date at an operator's first acceptance of a result.
/// </summary>
public sealed class RevisionAcceptanceLimitTests
{
    [Fact]
    public void AnAcceptanceSupersededByOneOfAnotherWorkOrderAcceptsNeitherTheWorkOrderNorItsResult()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope first = estate.Add("work-order", estate.OperatorId, Order("WORK-1"));
        RecordEnvelope second = estate.Add("work-order", estate.OperatorId, Order("WORK-2"));
        RecordEnvelope result = Result(estate, first, estate.BuilderId);
        RecordEnvelope accepted = estate.Add("acceptance", estate.OperatorId, Acceptance(self: true), first.Reference);
        estate.Add("acceptance", estate.OperatorId, Acceptance(self: true), second.Reference, supersedes: accepted.Id);

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.False(model.IsCurrent(model.ById[accepted.Id]));
        Assert.Null(EstateStatus.RevisionAcceptance(model, result));
        Assert.Equal("awaiting review", WorkView.ResultState(model, result));
        // Status counted the superseded acceptance as well: from 0.9.4 the work order it no longer names is not accepted.
        Assert.Equal("planned", Status(model, first));
        Assert.Equal("accepted", Status(model, second));
    }

    [Fact]
    public void AWorkOrdersSelfAcceptanceAcceptsOnlyAResultItsAuthorCouldHaveAcceptedItself()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = AcceptanceBasisOutcomeTests.OrdinaryWorkWithReview(estate, "pass");
        RecordEnvelope theirs = Result(estate, order, estate.OperatorId);
        RecordEnvelope accepted = estate.Add("acceptance", estate.BuilderId, AcceptanceBasisOutcomeTests.Body(review.Id, self: true), order.Reference);

        // The builder may accept its own work order under 'self-accept-ordinary-work', but accepting the operator's result needs
        // 'accept-work', so its acceptance of the work order does not accept that result.
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(("accepted", accepted.Id), (Status(model, order), EstateStatus.StatusRecord(model, order)!.Id));
        Assert.Null(EstateStatus.RevisionAcceptance(model, theirs));
        Assert.Equal("awaiting review", WorkView.ResultState(model, theirs));

        // A result of its own, which the same scope lets it accept, is accepted by the same acceptance.
        using var own = new TestEstate(3);
        (RecordEnvelope ownOrder, RecordEnvelope ownReview) = AcceptanceBasisOutcomeTests.OrdinaryWorkWithReview(own, "pass");
        RecordEnvelope mine = Result(own, ownOrder, own.BuilderId);
        RecordEnvelope ownAcceptance = own.Add("acceptance", own.BuilderId, AcceptanceBasisOutcomeTests.Body(ownReview.Id, self: true), ownOrder.Reference);
        model = own.Load();
        Assert.Equal(ownAcceptance.Id, EstateStatus.RevisionAcceptance(model, mine)?.Id);
        Assert.Equal("accepted", WorkView.ResultState(model, mine));
    }

    [Fact]
    public void AResultDatedBeforeAWorkOrdersAcceptanceGivenAfterResultAcceptancesBeganStillWaits()
    {
        using var estate = new TestEstate(3);
        // The operator accepts a result at its exact hash: from here acceptances name results.
        RecordEnvelope earlier = estate.Add("work-order", estate.OperatorId, Order("WORK-0"));
        RecordEnvelope began = estate.Add("acceptance", estate.OperatorId, Acceptance(self: false), Result(estate, earlier, estate.BuilderId).Reference);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, Order("WORK-1"));
        RecordEnvelope attempt = Attempt(estate, order, estate.BuilderId);
        DateTimeOffset started = estate.Clock;
        estate.SetClock(started.AddMinutes(10));
        RecordEnvelope accepted = estate.Add("acceptance", estate.OperatorId, Acceptance(self: true), order.Reference);
        // A worker whose clock is behind records its result after the acceptance, dated before it.
        estate.SetClock(started.AddMinutes(5));
        RecordEnvelope late = estate.Add("result", estate.BuilderId, ResultBody(), attempt.Reference);

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.True(GoalRules.Before(late, accepted) && GoalRules.Before(began, accepted));
        Assert.Equal(began.Id, EstateStatus.ResultAcceptancesBegan(model)?.Id);
        Assert.Null(EstateStatus.RevisionAcceptance(model, late));
        Assert.Equal("awaiting review", WorkView.ResultState(model, late));
    }

    private static string Status(EstateModel model, RecordEnvelope order) => EstateStatus.WorkItems(model).Single(w => w.Id == model.Latest(order).Id).Status;

    private static string Order(string key) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""";

    private static string Acceptance(bool self) => $$"""{"statement":"I accept it.","basis":[],"self":{{(self ? "true" : "false")}}}""";

    private static RecordEnvelope Attempt(TestEstate estate, RecordEnvelope order, string contributor)
    {
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{contributor}}}","brief":{"outcome":"Finish the step.","sources":[],"permittedChanges":["work"],"prohibitedEffects":[],"workspace":["test repository"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["It is finished."],"recovery":"Read the result."}}""", order.Reference);
        return estate.Add("attempt", contributor, $$"""{"startedAt":"{{RecordParser.FormatTimestamp(estate.Clock)}}","workspace":"test repository","note":"Finish the step."}""", assignment.Reference);
    }

    private static RecordEnvelope Result(TestEstate estate, RecordEnvelope order, string contributor) =>
        estate.Add("result", contributor, ResultBody(), Attempt(estate, order, contributor).Reference);

    private static string ResultBody() =>
        """{"outcome":"completed","summary":"Finished work for inspection.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review and accept the exact result."}""";
}
