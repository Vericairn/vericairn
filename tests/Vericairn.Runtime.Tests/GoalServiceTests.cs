// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The goal operations of the runtime (the estate's design/goals.md §§ 4, 5 and 12): propose, activate, pause, resume and stop by
/// the sponsor; evaluate, where the runtime decides every record predicate and the evaluator every evidence statement; accept,
/// once the current evaluation finds every condition met and every predicate holds now. Every refusal comes before any record.
/// </summary>
[Collection("home")]
public class GoalServiceTests
{
    [Fact]
    public void TheSponsorActivatesPausesResumesAndStopsAGoalInItsOrder()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        string goal = Ok(service.GoalPropose(new GoalProposeRequest(estate.BuilderId, Json(Goal(estate.OperatorId))))).Id;
        Assert.Equal("proposed", Status(service, goal));

        Refused(service.GoalActivate(goal, new GoalActionRequest(estate.BuilderId)), "Only the goal's sponsor records it as active.");
        Refused(service.GoalResume(goal, new GoalActionRequest(estate.OperatorId)), "was never activated; activate it");
        Refused(service.GoalPause(goal, new GoalActionRequest(estate.OperatorId)), "is proposed, not active");
        Ok(service.GoalActivate(goal, new GoalActionRequest(estate.OperatorId)));
        Assert.Equal("active", Status(service, goal));
        Refused(service.GoalActivate(goal, new GoalActionRequest(estate.OperatorId)), "is already active");
        Refused(service.GoalPause(goal, new GoalActionRequest(estate.BuilderId)), "Only the goal's sponsor");
        Ok(service.GoalPause(goal, new GoalActionRequest(estate.OperatorId, "Waiting on the reviewer.")));
        Refused(service.GoalActivate(goal, new GoalActionRequest(estate.OperatorId)), "resume it instead");
        Refused(service.GoalResume(goal, new GoalActionRequest(estate.BuilderId)), "Only the goal's sponsor records it as active.");
        Ok(service.GoalResume(goal, new GoalActionRequest(estate.OperatorId)));
        Refused(service.GoalStop(goal, new GoalActionRequest(estate.BuilderId)), "Only the goal's sponsor");
        Ok(service.GoalStop(goal, new GoalActionRequest(estate.OperatorId)));
        Assert.Equal("abandoned", Status(service, goal));
        foreach (WriteOutcome after in new[] { service.GoalActivate(goal, new GoalActionRequest(estate.OperatorId)), service.GoalResume(goal, new GoalActionRequest(estate.OperatorId)), service.GoalPause(goal, new GoalActionRequest(estate.OperatorId)), service.GoalStop(goal, new GoalActionRequest(estate.OperatorId)) })
        {
            Refused(after, "ended as abandoned");
        }

        Assert.Equal("not-found", service.GoalActivate("01M4TRNE1GYEK4P8MXQAB3C8Q4", new GoalActionRequest(estate.OperatorId)).Problem!.Code);
        Assert.Null(service.Goal("01M4TRNE1GYEK4P8MXQAB3C8Q4"));
        Assert.Equal(0, service.Check().Errors);
    }

    [Fact]
    public void AnAgentSponsoredGoalStaysProposedAndARevisionStartsAgain()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        string agents = Ok(service.GoalPropose(new GoalProposeRequest(estate.BuilderId, Json(Goal(estate.BuilderId))))).Id;
        Refused(service.GoalActivate(agents, new GoalActionRequest(estate.BuilderId)), "an agent may propose a goal, never activate or fund one");
        Refused(service.GoalActivate(agents, new GoalActionRequest(estate.OperatorId)), "Only the goal's sponsor records it as active.");
        Assert.Contains("accepted by an operator, since its sponsor is an agent", service.Goal(agents)!.Text, StringComparison.Ordinal);

        // A person sponsors it by a revision, which starts from proposed and is found by the id of any revision.
        string revised = Ok(service.GoalPropose(new GoalProposeRequest(estate.OperatorId, Json(Goal(estate.OperatorId)), agents))).Id;
        Assert.Equal(revised, service.Goal(agents)!.Json.GetProperty("goal").GetProperty("id").GetString());
        Assert.Equal(2, service.Goal(agents)!.Json.GetProperty("goal").GetProperty("revision").GetInt32());
        Ok(service.GoalActivate(agents, new GoalActionRequest(estate.OperatorId)));
        Assert.Equal("active", Status(service, revised));
        Refused(service.GoalPropose(new GoalProposeRequest(estate.OperatorId, Json(Goal(estate.OperatorId)), agents)), $"was revised by {revised}");
        Assert.Equal("not-found", service.GoalPropose(new GoalProposeRequest(estate.OperatorId, Json(Goal(estate.OperatorId)), "01M4TRNE1GYEK4P8MXQAB3C8Q4")).Problem!.Code);

        string again = Ok(service.GoalPropose(new GoalProposeRequest(estate.OperatorId, Json(Goal(estate.OperatorId).Replace("Ship the widget.", "Ship the widget sooner.", StringComparison.Ordinal)), revised))).Id;
        Assert.Equal("proposed", Status(service, again));
        Assert.Contains("Budget its activation grants: wallMinutes 600, attemptsPerStep 3; not granted: tokens, concurrency, currency", service.Goal(again)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeDecidesEveryRecordPredicateAndTheEvaluatorEveryEvidenceStatement()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        (string goal, string reviewer, string result) = Work(estate, service);
        string read = $"{result}";

        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(estate.BuilderId, "Mine.", [new GoalVerdict(2, "met", "I built it.", [read])])), "other than any step's author");
        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read.", [new GoalVerdict(1, "met", "Looks done.")])), "Condition 1 is a record predicate, which the runtime finds not-met");
        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read.", [])), "Condition 2 is an evidence statement; the evaluator gives its verdict");
        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read.", [new GoalVerdict(2, "met", "Believed.")])), "name the records read");
        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read.", [new GoalVerdict(2, "met", "Read.", ["01M4TRNE1GYEK4P8MXQAB3C8Q4"])])), "no record has that id");
        Refused(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read.", [new GoalVerdict(3, "met", "x"), new GoalVerdict(2, "done", "x")])), "conditions 1 to 2, not 3");

        // The predicate gets the runtime's verdict with the records it read; the evaluator may judge an unmet one impossible.
        string partly = Ok(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read the result.", [new GoalVerdict(2, "met", "The guide covers the widget.", [read])]))).Id;
        JsonElement conditions = Body(service, partly).GetProperty("conditions");
        Assert.Equal("not-yet", conditions[0].GetProperty("verdict").GetString());
        Assert.StartsWith("work-order records with key W-1 that are accepted: no current record matches", conditions[0].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("met", conditions[1].GetProperty("verdict").GetString());
        Assert.Equal(result, conditions[1].GetProperty("read")[0].GetProperty("id").GetString());
        Assert.Equal("findings", Body(service, partly).GetProperty("outcome").GetString());

        string impossible = Ok(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "The work order was retired.", [new GoalVerdict(1, "impossible", "W-1 will not be accepted."), new GoalVerdict(2, "met", "The guide covers the widget.", [read])]))).Id;
        Assert.Equal("fail", Body(service, impossible).GetProperty("outcome").GetString());
        Assert.Contains("condition 1 impossible", service.Goal(goal)!.Text, StringComparison.Ordinal);
        Assert.Equal(0, service.Check().Errors);
    }

    [Fact]
    public void AcceptanceWaitsForTheCurrentEvaluationAndForEveryPredicateToHoldNow()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        (string goal, string reviewer, string result) = Work(estate, service);
        Refused(service.GoalAccept(goal, new GoalAcceptRequest(estate.OperatorId, "Done.")), "No evaluation of the goal's exact revision is recorded");

        // The work order is accepted, so the predicate holds, and an evaluator who did no step finds every condition met.
        JsonElement order = service.Brief("W-1")!.Json.GetProperty("workOrder");
        Ok(service.NewRecord(new NewRecordRequest("acceptance", estate.OperatorId, Json("""{"statement":"Accepted.","basis":[],"self":true}"""), $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}")));
        string evaluation = Ok(service.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read the result and the guide.", [new GoalVerdict(2, "met", "The guide covers the widget.", [result])]))).Id;
        Assert.True(service.Goal(goal)!.Json.GetProperty("ready").GetBoolean());
        Refused(service.GoalAccept(goal, new GoalAcceptRequest(reviewer, "Done.")), "never accepted by its evaluator");
        Refused(service.GoalAccept(goal, new GoalAcceptRequest(estate.BuilderId, "Done.")), "never accepted by the author of one of its steps");

        // A predicate that stops holding after the evaluation refuses the acceptance until it holds again: the worker's report is no proof.
        string revisedOrder = Ok(service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, Json(Order("W-1", goal).Replace("Build the widget.", "Build the widget again.", StringComparison.Ordinal)), Supersedes: order.GetProperty("id").GetString()))).Id;
        Refused(service.GoalAccept(goal, new GoalAcceptRequest(estate.OperatorId, "Done.")), "Condition 1 is not-met now");
        Assert.False(service.Goal(goal)!.Json.GetProperty("ready").GetBoolean());
        JsonElement current = service.Brief("W-1")!.Json.GetProperty("workOrder");
        Ok(service.NewRecord(new NewRecordRequest("acceptance", estate.OperatorId, Json("""{"statement":"Accepted again.","basis":[],"self":true}"""), $"work-order:{revisedOrder}:{current.GetProperty("hash").GetString()}")));

        string acceptance = Ok(service.GoalAccept(goal, new GoalAcceptRequest(estate.OperatorId, "Accepted on the evaluation."))).Id;
        JsonElement accepted = Body(service, acceptance);
        Assert.Equal(evaluation, accepted.GetProperty("basis")[0].GetString());
        Assert.False(accepted.GetProperty("self").GetBoolean());
        Assert.Equal("achieved", Status(service, goal));
        Refused(service.GoalAccept(goal, new GoalAcceptRequest(estate.OperatorId, "Again.")), "was achieved by acceptance");
        Refused(service.GoalPause(goal, new GoalActionRequest(estate.OperatorId)), "was achieved");
        Assert.Equal(0, service.Check().Errors);
    }

    /// <summary>A goal the builder proposes for the operator, activated; the work order that names it; the builder's result; and a reviewer.</summary>
    private static (string Goal, string Reviewer, string Result) Work(TestEstate estate, EstateService service)
    {
        string reviewer = Ok(service.NewRecord(new NewRecordRequest("principal", estate.OperatorId, Json("""{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Reviewer"}}""")))).Id;
        string goal = Ok(service.GoalPropose(new GoalProposeRequest(estate.BuilderId, Json(Goal(estate.OperatorId))))).Id;
        Ok(service.GoalActivate(goal, new GoalActionRequest(estate.OperatorId)));
        Ok(service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, Json(Order("W-1", goal)))));
        string assignment = Ok(service.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, Json("""{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests pass"],"recovery":"Record a result."}""")))).Id;
        string attempt = Ok(service.Attempt(new AttemptRequest(assignment, estate.BuilderId, "example repository, work branch", "Build it."))).Id;
        string result = Ok(service.Result(new ResultRequest(attempt, estate.BuilderId, Json("""{"outcome":"completed","summary":"Built the widget and its guide.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")))).Id;
        return (goal, reviewer, result);
    }

    private static string Goal(string sponsor) =>
        """{"title":"Build the widget","objective":"Ship the widget.","completion":[{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}},{"evidence":"The guide describes the widget.","check":"An evaluator reads the guide."}],"constraints":["no publication"],"kind":"finite","sponsor":"@SPONSOR@","budget":{"wallMinutes":600,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}"""
            .Replace("@SPONSOR@", sponsor, StringComparison.Ordinal);

    private static string Order(string key, string goal) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Build the widget","intent":"Build the widget.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}""";

    private static string Status(EstateService service, string goal) => service.Goal(goal)!.Json.GetProperty("status").GetString()!;

    private static JsonElement Body(EstateService service, string id) => service.Record(id)!.Envelope.GetProperty("body");

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem is null ? "no problem reported" : outcome.Problem.Message + "\n" + string.Join('\n', outcome.Problem.Findings.Select(f => $"{f.Severity} {f.Code} {f.Location}: {f.Message}")));
        return outcome.Written!;
    }

    private static void Refused(WriteOutcome outcome, string because)
    {
        Assert.False(outcome.Ok, "written, though it should have been refused: " + because);
        Assert.Equal("refused", outcome.Problem!.Code);
        Assert.Contains(because, outcome.Problem.Message, StringComparison.Ordinal);
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
