// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Decision steps under goal step and goal decide (the estate's design/flow.md § 5; PROC-19): goal step gives a decision step to the
/// principal its role reaches, a person asked or an agent routed, with a brief naming its paths; the person decides with goal decide,
/// which records the result of the assignment with its paths, reasons and evidence by hash, refusing every problem before anything is
/// written; goal step then takes the path chosen; and a decision the flow requires of a person is never routed to, or decided by, an agent.
/// </summary>
[Collection("home")]
public class FlowDecisionRoutingTests
{
    [Fact]
    public void GoalStepAsksThePersonInTheRoleAndGoalDecideRecordsTheirDecisionBeforeGoalStepTakesThePath()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, Gate(), body => Roles(body, op, op));
        GoalStepResponse drafting = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(drafting.Ok, drafting.Message);
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, drafting.Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), null), op);

        // Nothing holds the decision until goal step gives it to the approver; every refusal comes before anything is written.
        int before = run.Estate.Load().Records.Count;
        WriteOutcome early = run.Service.GoalDecide(goal, new GoalDecideRequest(op, "gate", ["ship"], "Ready."));
        Assert.False(early.Ok);
        Assert.Contains("No assignment holds step gate for this round", early.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, run.Estate.Load().Records.Count);

        GoalStepResponse asked = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "gate", "person", op), (asked.Outcome, asked.Step, asked.Route, asked.Principal));
        RecordEnvelope question = run.Estate.Load().ById[asked.Ask!.Id];
        Assert.StartsWith("Will you decide gate Step gate, a step of goal Release the song, among its paths (ship, to step release; drop, to the end of the run)?", EstateModel.BodyString(question, "question"), StringComparison.Ordinal);
        Assert.StartsWith("Decide it from assignment", EstateModel.BodyString(question, "recommendation"), StringComparison.Ordinal);
        RecordEnvelope assignment = run.Estate.Load().ById[asked.Assignment!.Id];
        string criteria = assignment.Body.GetProperty("brief").GetProperty("reviewCriteria").EnumerateArray().First().GetString()!;
        Assert.Contains("Decide among its paths (ship, to step release; drop, to the end of the run), one of them", criteria, StringComparison.Ordinal);
        Assert.Contains($"vericairn goal decide {goal} --step gate --path <path>", criteria, StringComparison.Ordinal);

        WriteOutcome refused = run.Service.GoalDecide(goal, new GoalDecideRequest(op, "gate", ["ship", "drop"], " ", ["work-order:" + lyrics.Id]));
        Assert.False(refused.Ok);
        Assert.Contains("takes one path", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("A decision records its reasons", refused.Problem.Message, StringComparison.Ordinal);
        Assert.Contains($"Evidence work-order:{lyrics.Id} is not a record of this estate of type work-order", refused.Problem.Message, StringComparison.Ordinal);

        WriteOutcome decided = run.Service.GoalDecide(goal, new GoalDecideRequest(op, "gate", ["ship"], "The lyrics are ready.", [lyrics.Id]));
        Assert.True(decided.Ok, decided.Problem?.Message);
        RecordEnvelope result = run.Estate.Load().ById[decided.Written!.Id];
        Assert.Equal(("result", op), (result.Type, result.By));
        Assert.Equal(lyrics.Hash, result.Body.GetProperty("decision").GetProperty("evidence")[0].GetProperty("hash").GetString());
        JsonElement gate = Step(run, goal, "gate");
        Assert.Equal(("done", "taken", "person"), (gate.GetProperty("state").GetString(), gate.GetProperty("decision").GetProperty("state").GetString(), gate.GetProperty("decision").GetProperty("decidedBy").GetString()));

        // goal step takes the path chosen; the path not chosen ends nothing, and a second decision is refused.
        GoalStepResponse next = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(next.Ok, next.Message);
        Assert.Equal(("ask", "release"), (next.Outcome, next.Step));
        WriteOutcome again = run.Service.GoalDecide(goal, new GoalDecideRequest(op, "gate", ["drop"], "Changed my mind."));
        Assert.Contains("is not waiting on its decider", again.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAgentInTheRoleIsRoutedToDecideAndADecisionTheFlowRequiresOfAPersonNeverIs()
    {
        using (var run = new RunEstate(recordFormat: 3))
        {
            string op = run.Estate.OperatorId;
            string goal = FlowStepRoutingTests.Run(run, Gate(), body => Roles(body, op, run.Estate.BuilderId));
            FlowStepRoutingTests.Done(run, run.Service.GoalStep(goal, new GoalStepRequest(op)).Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), null), op);
            JsonElement route = Route(run, goal, "gate");
            Assert.Equal(("agent", run.Estate.BuilderId), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
        }

        using (var run = new RunEstate(recordFormat: 3))
        {
            string op = run.Estate.OperatorId;
            JsonObject flow = Gate();
            flow["steps"]![1]!["transition"]!["person"] = true;
            string goal = FlowStepRoutingTests.Run(run, flow, body => Roles(body, op, run.Estate.BuilderId, op));
            FlowStepRoutingTests.Done(run, run.Service.GoalStep(goal, new GoalStepRequest(op)).Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), null), op);
            JsonElement route = Route(run, goal, "gate");
            Assert.Equal(("person", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
            Assert.Contains(route.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("principal").GetString() == run.Estate.BuilderId && !c.GetProperty("available").GetBoolean()
                && c.GetProperty("reasons").EnumerateArray().Any(r => r.GetString()!.Contains("decision is a person's", StringComparison.Ordinal)));
            WriteOutcome agent = run.Service.GoalDecide(goal, new GoalDecideRequest(run.Estate.BuilderId, "gate", ["ship"], "The agent's choice."));
            Assert.Contains("Step gate's decision is a person's, as the flow requires", agent.Problem!.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>The gate flow, its release a work step goal step dispatches once the gate's decision takes its path.</summary>
    private static JsonObject Gate()
    {
        JsonObject flow = FlowFixture.Gate();
        JsonObject release = flow["steps"]![2]!.AsObject();
        release["kind"] = "work";
        release.Remove("done");
        release["outputs"] = new JsonArray(FlowFixture.FileItem("notes"));
        return flow;
    }

    /// <summary>The run's subject is its recording only; the writer and the approvers are the principals given.</summary>
    private static void Roles(JsonObject body, string writer, params string[] approvers)
    {
        body["plan"]!["flow"]!["subject"]!.AsObject().Remove("tracks");
        body["roles"] = new JsonArray(FlowFixture.Role("writer", writer), FlowFixture.Role("approver", approvers));
    }

    private static JsonElement Step(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);

    private static JsonElement Route(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.GoalNext(goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key).GetProperty("route");
}
