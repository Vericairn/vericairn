// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.ProgressReportTests;

namespace Vericairn.Records.Tests;

public class OperatorActionsTests
{
    [Fact]
    public void EmptyQueueIsDeterministicAndReadingWritesNothing()
    {
        using var estate = new TestEstate(recordFormat: 2);
        EstateModel model = estate.Load();
        string before = EstateStatus.RenderJson(model, new FindingList());
        JsonObject report = OperatorActions.Derive(model);
        Assert.Empty(Items(report));
        Assert.Equal(OperatorActions.Format, report["format"]!.GetValue<string>());
        Assert.Equal(OperatorActions.RenderJson(report), OperatorActions.RenderJson(OperatorActions.Derive(estate.Load())));
        Assert.Contains("0 items", OperatorActions.RenderText(report), StringComparison.Ordinal);
        Assert.Equal(before, EstateStatus.RenderJson(estate.Load(), new FindingList()));
    }

    [Fact]
    public void AllOperatorAsksAppearIncludingTheQueueBeyondThreeAndOnlyTheirAddresseeAnswers()
    {
        using var estate = new TestEstate(recordFormat: 2);
        List<RecordEnvelope> asks = Enumerable.Range(1, 5).Select(i => estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Question " + i))).ToList();
        RecordEnvelope notified = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Question 1", "notified"), supersedes: asks[0].Id);
        RecordEnvelope answered = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Answered"));
        RecordEnvelope answeredRevision = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Answered", "notified"), supersedes: answered.Id);
        estate.Add("decision", estate.BuilderId, Decision(estate, $",\"answers\":[\"{answered.Id}\"]"));
        RecordEnvelope withdrawn = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Withdrawn"));
        estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Withdrawn", "withdrawn"), supersedes: withdrawn.Id);
        estate.Add("ask", estate.OperatorId, Ask(estate.BuilderId, "Agent's question"));
        JsonObject report = Valid(estate);
        Assert.Equal(new[] { notified.Id }.Concat(asks.Skip(1).Select(a => a.Id)), Items(report).Select(Id));
        Assert.DoesNotContain(Items(report), i => Id(i) == answeredRevision.Id);
        foreach (JsonObject item in Items(report))
        {
            JsonObject command = Assert.Single(Commands(item));
            Assert.Equal(estate.OperatorId, command["by"]!.GetValue<string>());
            Assert.Equal("vericairn records new decision --by " + estate.OperatorId + " --body -", command["command"]!.GetValue<string>());
            Assert.Equal(Id(item), command["body"]!["answers"]![0]!.GetValue<string>());
            Assert.Null(command["body"]!["statement"]);
            Assert.Contains(Id(item), OperatorActions.RenderText(report), StringComparison.Ordinal);
        }
        Assert.Equal(5, OperatorActions.RenderText(report).Split("If unanswered:", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void EveryOperatorIsIncludedAndPrincipalRevisionsKeepTheOriginalActingIdentity()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Other operator","roles":["operator"]}""");
        estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Renamed operator","roles":["operator"]}""", supersedes: other.Id);
        estate.Add("ask", estate.BuilderId, Ask(other.Id, "Other operator's answer"));
        RecordEnvelope order = Order(estate, "M-1", "milestone");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", order.Reference);
        List<JsonObject> items = Items(Valid(estate));
        Assert.Equal(other.Id, Assert.Single(Commands(items[0]))["by"]!.GetValue<string>());
        Assert.Equal(new[] { estate.OperatorId, other.Id }.Order(StringComparer.Ordinal), Commands(items[1]).Select(c => c["by"]!.GetValue<string>()));
        Assert.True(Commands(items[1]).Single(c => c["by"]!.GetValue<string>() == estate.OperatorId)["body"]!["self"]!.GetValue<bool>());
        Assert.False(Commands(items[1]).Single(c => c["by"]!.GetValue<string>() == other.Id)["body"]!["self"]!.GetValue<bool>());
    }

    [Fact]
    public void ReleaseAcceptancePinsTheCurrentHashAndAnEarlierAcceptanceDoesNotClearIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope first = estate.Add("release", estate.OperatorId, ReleaseBody("1.0.0", Commit('a'), "candidate"), product.Reference);
        JsonObject original = Assert.Single(Items(Valid(estate)));
        AssertPin(original, first);
        Accept(estate, first);
        JsonObject publication = Assert.Single(Items(Valid(estate)));
        Assert.Equal("reserved-action", Kind(publication));
        JsonObject publicationCommand = Assert.Single(Commands(publication));
        Assert.Contains("--supersedes " + first.Id, publicationCommand["command"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(publicationCommand["body"]!["publishedAt"]);
        Assert.Equal(Commit('a'), publicationCommand["body"]!["commit"]!.GetValue<string>());
        RecordEnvelope next = estate.Add("release", estate.OperatorId, ReleaseBody("1.0.0", Commit('b'), "candidate"), product.Reference, supersedes: first.Id);
        AssertPin(Assert.Single(Items(Valid(estate))), next);
        estate.Add("release", estate.OperatorId, ReleaseBody("1.0.0", Commit('b'), "withdrawn"), product.Reference, supersedes: next.Id);
        Assert.Empty(Items(Valid(estate)));
        Publish(estate, product, "2.0.0", Commit('c'));
        Assert.Empty(Items(Valid(estate)));
    }

    [Fact]
    public void MilestoneOrdersAndTheirLatestResultsNeedSeparatePinnedAcceptances()
    {
        using var estate = new TestEstate(recordFormat: 2);
        (RecordEnvelope order, RecordEnvelope result) = Worked(estate, "M-1", "milestone");
        Worked(estate, "W-1");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", order.Reference);
        List<JsonObject> items = Items(Valid(estate));
        Assert.Equal(["milestone-acceptance", "milestone-result-acceptance"], items.Select(Kind));
        AssertPin(items[0], order);
        AssertPin(items[1], result);
        Assert.False(Assert.Single(Commands(items[1]))["body"]!["self"]!.GetValue<bool>());
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Result accepted.","basis":[],"self":false}""", result.Reference);
        Assert.Single(Items(Valid(estate)));
        RecordEnvelope revised = estate.Add("result", estate.BuilderId, result.Body.GetRawText(), result.Subject, supersedes: result.Id);
        AssertPin(Items(Valid(estate)).Single(i => Kind(i) == "milestone-result-acceptance"), revised);
        Accept(estate, order);
        Assert.Empty(Items(Valid(estate)));
        RecordEnvelope orderRevision = estate.Add("work-order", estate.OperatorId, order.Body.GetRawText(), supersedes: order.Id);
        Assert.Contains(Items(Valid(estate)), i => Id(i) == orderRevision.Id && i["state"]!.GetValue<string>() == "accepted (earlier revision)");
    }

    [Fact]
    public void GoalAcceptanceRequiresTheCurrentEvaluationAndNamesItsExactBasis()
    {
        using var estate = new TestEstate(recordFormat: 2);
        GoalEvaluationTests.Work work = GoalEvaluationTests.Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Start."}""", work.Goal.Reference);
        JsonObject waiting = Assert.Single(Items(Valid(estate)));
        Assert.Empty(Commands(waiting));
        Assert.Contains("no evaluation", waiting["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, GoalEvaluationTests.Evaluation("pass", GoalEvaluationTests.Met(work.Order), GoalEvaluationTests.Met(work.Result)), work.Goal.Reference);
        JsonObject ready = Assert.Single(Items(Valid(estate)));
        AssertPin(ready, work.Goal);
        Assert.Equal(evaluation.Id, Assert.Single(Commands(ready))["body"]!["basis"]![0]!.GetValue<string>());
        estate.Add("review", work.Reviewer.Id, GoalEvaluationTests.Evaluation("findings", GoalEvaluationTests.Met(work.Order), GoalEvaluationTests.NotYet()), work.Goal.Reference);
        Assert.Empty(Commands(Assert.Single(Items(Valid(estate)))));
    }

    [Fact]
    public void AGoalEvaluatorIsNeverOfferedTheirOwnAcceptanceAndOtherOperatorsRemainEligible()
    {
        using var estate = new TestEstate(recordFormat: 2);
        GoalEvaluationTests.Work work = GoalEvaluationTests.Work.Write(estate, sponsor: estate.BuilderId);
        RecordEnvelope other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Independent operator","roles":["operator"]}""");
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Start."}""", work.Goal.Reference);
        estate.Add("review", estate.OperatorId, GoalEvaluationTests.Evaluation("pass", GoalEvaluationTests.Met(work.Order), GoalEvaluationTests.Met(work.Result), "self"), work.Goal.Reference);
        JsonObject item = Assert.Single(Items(Valid(estate)));
        Assert.Equal(other.Id, Assert.Single(Commands(item))["by"]!.GetValue<string>());
        Assert.Contains("never accepted by its evaluator", item["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AGoalWhoseOperatorAuthoredAStepKeepsItsIndependenceHoldVisible()
    {
        using var estate = new TestEstate(recordFormat: 2);
        GoalEvaluationTests.Work work = GoalEvaluationTests.Work.Write(estate);
        estate.Add("assignment", estate.OperatorId, GoalEvaluationTests.Assignment(estate.OperatorId), work.Order.Reference);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Start."}""", work.Goal.Reference);
        estate.Add("review", work.Reviewer.Id, GoalEvaluationTests.Evaluation("pass", GoalEvaluationTests.Met(work.Order), GoalEvaluationTests.Met(work.Result)), work.Goal.Reference);
        JsonObject item = Assert.Single(Items(Valid(estate)));
        Assert.Empty(Commands(item));
        Assert.Contains("author of one of its steps", item["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenOperatorObligationsStayUntilDischargedIncludingAcrossDecisionRevisions()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string extra = $$""", "obligations":[{"key":"publish","summary":"Publish after approval","owner":"{{estate.OperatorId}}","resolution":"action","discharge":"Observed publication evidence.","gates":"next release"},{"key":"build","summary":"Build","owner":"{{estate.BuilderId}}","resolution":"action","discharge":"Test result."}]""";
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Decision(estate, extra));
        RecordEnvelope revision = estate.Add("decision", estate.OperatorId, Decision(estate, extra), supersedes: decision.Id);
        JsonObject item = Assert.Single(Items(Valid(estate)));
        Assert.Equal("reserved-action", Kind(item));
        AssertPin(item, revision);
        Assert.Equal("publish", Assert.Single(Commands(item))["body"]!["obligation"]!.GetValue<string>());
        Assert.Contains("Observed publication evidence.", OperatorActions.RenderText(Valid(estate)), StringComparison.Ordinal);
        estate.Add("discharge", estate.OperatorId, """{"obligation":"publish","summary":"Published.","evidence":[{"summary":"Observed the publication."}]}""", decision.Reference);
        Assert.Empty(Items(Valid(estate)));
    }

    [Fact]
    public void ReservedAssignmentsShowTheirHoldsAndUseOnlyTheirContributorsAttemptAndResult()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope dependency = Order(estate, "W-1");
        JsonObject body = JsonNode.Parse(OrderBody("P-1", "ordinary", string.Empty, after: dependency.Id))!.AsObject();
        body["role"] = "publisher";
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, body.ToJsonString());
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, GoalEvaluationTests.Assignment(estate.OperatorId), order.Reference);
        JsonObject assigned = Assert.Single(Items(Valid(estate)));
        Assert.Contains(dependency.Id, assigned["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
        JsonObject command = Assert.Single(Commands(assigned));
        Assert.Contains("records new attempt", command["command"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(command["body"]!.AsObject().ContainsKey("startedAt"));
        RecordEnvelope attempt = estate.Add("attempt", estate.OperatorId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository","note":"Publish."}""", assignment.Reference);
        estate.Add("work-status", estate.OperatorId, """{"status":"active","note":"Started."}""", order.Reference);
        JsonObject running = Assert.Single(Items(Valid(estate)));
        Assert.Contains(dependency.Id, running["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("records new result", Assert.Single(Commands(running))["command"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(attempt.ComputedHash, Assert.Single(Commands(running))["command"]!.GetValue<string>(), StringComparison.Ordinal);
        estate.Add("result", estate.OperatorId, """{"outcome":"completed","summary":"Published.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review."}""", attempt.Reference);
        Assert.Empty(Items(Valid(estate)));
    }

    [Fact]
    public void AStrandedRoutedAssignmentDoesNotClaimToWaitOnAnOperator()
    {
        using var estate = new TestEstate(recordFormat: 2);
        JsonObject orderBody = JsonNode.Parse(OrderBody("P-1", "ordinary", string.Empty))!.AsObject();
        orderBody["role"] = "publisher";
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, orderBody.ToJsonString());
        JsonObject assignmentBody = JsonNode.Parse(GoalEvaluationTests.Assignment(estate.OperatorId))!.AsObject();
        assignmentBody["routedWith"] = "ask";
        estate.Add("assignment", estate.OperatorId, assignmentBody.ToJsonString(), order.Reference);
        Assert.Empty(Items(Valid(estate)));
    }

    [Fact]
    public void PublicationCommandKeepsAcceptedContentsAndRequiresAnObservedTimeAndLocation()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, ReleaseBody("1.0.0", Commit('a'), "candidate"), product.Reference);
        Accept(estate, candidate);
        JsonObject command = Assert.Single(Commands(Assert.Single(Items(Valid(estate)))));
        JsonObject body = command["body"]!.DeepClone().AsObject();
        body["publishedAt"] = "2026-10-13T13:00:00Z";
        body["url"] = "https://example.invalid/releases/1.0.0";
        estate.Add("release", estate.OperatorId, body.ToJsonString(), product.Reference, supersedes: candidate.Id);
        Assert.Empty(Items(Valid(estate)));
    }

    [Fact]
    public void RecordErrorsKeepTheQueueVisibleWithoutOfferingAuthoringCommands()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, Ask(estate.OperatorId, "Still waiting"));
        Order(estate, "W-1");
        Order(estate, "W-1");
        JsonObject report = OperatorActions.Derive(estate.Load());
        Assert.True(report["hasErrors"]!.GetValue<bool>());
        JsonObject item = Assert.Single(Items(report));
        Assert.Equal(ask.Id, Id(item));
        Assert.Empty(Commands(item));
        Assert.Equal("vericairn records show " + ask.Id, item["inspect"]!.GetValue<string>());
        Assert.Contains("record errors", item["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelegatedGoalsAreReservedOnlyWhenTheNonOperatorDelegateLacksAuthority(bool granted)
    {
        using var estate = new TestEstate(2);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Delegate","roles":["contributor"]}""");
        GoalEvaluationTests.Work work = GoalEvaluationTests.Work.Write(estate, delegated: person.Id);
        if (granted)
        {
            RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Decision(estate, string.Empty));
            estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{person.Id}}","scope":["accept-work"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""");
        }
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Start."}""", work.Goal.Reference);
        estate.Add("review", work.Reviewer.Id, GoalEvaluationTests.Evaluation("pass", GoalEvaluationTests.Met(work.Order), GoalEvaluationTests.Met(work.Result)), work.Goal.Reference);
        List<JsonObject> items = Items(Valid(estate));
        Assert.Equal(granted ? 0 : 1, items.Count);
        if (!granted) Assert.Equal(estate.OperatorId, Assert.Single(Commands(Assert.Single(items)))["by"]!.GetValue<string>());
    }

    [Fact]
    public void APartialReservedAttemptKeepsItsUnresolvedEffectsAndNextStepInTheQueue()
    {
        using var estate = new TestEstate(2);
        JsonObject body = JsonNode.Parse(OrderBody("P-1", "ordinary", string.Empty))!.AsObject();
        body["role"] = "publisher";
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, body.ToJsonString());
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, GoalEvaluationTests.Assignment(estate.OperatorId), order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.OperatorId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository","note":"Publish."}""", assignment.Reference);
        estate.Add("result", estate.OperatorId, """{"outcome":"partial","summary":"Publication may have started.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":["The publication outcome is unknown."],"nextStep":"Observe the original operation."}""", attempt.Reference);
        JsonObject item = Assert.Single(Items(Valid(estate)));
        Assert.Equal("another attempt needed", item["state"]!.GetValue<string>());
        Assert.Contains("Reconcile before retry", item["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("Observe the original operation.", item["waitsOn"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("records new attempt", Assert.Single(Commands(item))["command"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("milestone", "ordinary", 1)]
    [InlineData("ordinary", "milestone", 0)]
    public void AResultsAcceptancePolicyUsesTheOrderRevisionItsAssignmentPinned(string originalKind, string revisedKind, int expected)
    {
        using var estate = new TestEstate(2);
        (RecordEnvelope order, RecordEnvelope result) = Worked(estate, "W-1", originalKind);
        JsonObject body = JsonNode.Parse(order.Body.GetRawText())!.AsObject();
        body["kind"] = revisedKind;
        estate.Add("work-order", estate.OperatorId, body.ToJsonString(), supersedes: order.Id);
        List<JsonObject> items = Items(Valid(estate));
        Assert.Equal(expected, items.Count);
        if (expected > 0) AssertPin(Assert.Single(items), result);
    }

    internal static JsonObject Valid(TestEstate estate)
    {
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        return OperatorActions.Derive(model);
    }

    private static List<JsonObject> Items(JsonObject report) => report["items"]!.AsArray().OfType<JsonObject>().ToList();
    private static List<JsonObject> Commands(JsonObject item) => item["commands"]!.AsArray().OfType<JsonObject>().ToList();
    private static string Id(JsonObject item) => item["source"]!["id"]!.GetValue<string>();
    private static string Kind(JsonObject item) => item["kind"]!.GetValue<string>();
    private static void AssertPin(JsonObject item, RecordEnvelope subject) => Assert.All(Commands(item), command =>
        Assert.Contains($"--subject {subject.Type}:{subject.Id}:{subject.ComputedHash}", command["command"]!.GetValue<string>(), StringComparison.Ordinal));
}
