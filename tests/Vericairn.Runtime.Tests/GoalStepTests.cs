// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// One routed step at a time, the smallest form of Community's conductor (the estate's design/goals.md §§ 6, 8 and 11): a goal's next
/// eligible step goes to the principal its roster names, an agent's through the worker within the custody precondition, a person's as
/// an assignment and an ask, and a step no principal is available for as an ask to the sponsor; and a person takes over a step an
/// agent holds, the hand-over being a record. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class GoalStepTests
{
    [Fact]
    public void OneStepAtATimeRunsAnAgentAndAsksAPersonOrTheSponsor()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        Order(run, "W-3", ""","role":"publisher" """);
        Order(run, "W-4", ""","role":"planner" """);

        // W-1 is held by the builder's assignment: its run starts through Claude Code's launcher, and nothing new is assigned.
        GoalStepResponse first = Step(run, "first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal(("run", "W-1", "agent", builder), (first.Outcome, first.Step, first.Route, first.Principal));
        Assert.StartsWith($"W-1 is routed to the agent Test Builder [principal {builder}] through claude-code: ", first.Message, StringComparison.Ordinal);
        Assert.Null(first.Assignment);
        Assert.Equal("observed-success", run.Settled(first.Run!.Run).Status);
        Assert.Equal(run.Assignment, AgentRuns.Find(run.Estate.Load(), first.Run.Run)!.Assignment);
        Done(run, run.Assignment);

        // W-2, with its result awaiting review behind it: routing assigns W-2 to the builder, from the work order and the goal, and its run starts.
        GoalStepResponse second = Step(run, "second");
        Assert.True(second.Ok, second.Message);
        Assert.Equal(("run", "W-2"), (second.Outcome, second.Step));
        RecordEnvelope assigned = run.Estate.Load().ById[second.Assignment!.Id];
        Assert.Equal(builder, EstateModel.BodyString(assigned, "contributor"));
        Assert.StartsWith("Routed by roster (builder, from the estate's roster): the first available of the builder role's principals", EstateModel.BodyString(assigned, "note"), StringComparison.Ordinal);
        JsonElement brief = assigned.Body.GetProperty("brief");
        Assert.Equal(["work-order", "goal"], brief.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("type").GetString()));
        Assert.Equal(["widget, a work branch"], brief.GetProperty("workspace").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal("The exit question is answered yes: Does the guide describe the widget?", brief.GetProperty("reviewCriteria")[0].GetString());
        Assert.Contains(brief.GetProperty("prohibitedEffects").EnumerateArray(), p => p.GetString() == "no change outside the widget");
        Assert.Equal("observed-success", run.Settled(second.Run!.Run).Status);
        Done(run, second.Assignment.Id);

        // W-3 is a reserved action: it is assigned to the operator, who holds publication, and asked of them, the recommendation first.
        GoalStepResponse third = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(third.Ok, third.Message);
        Assert.Equal(("ask", "W-3", "reserved", op), (third.Outcome, third.Step, third.Route, third.Principal));
        RecordEnvelope ask = run.Estate.Load().ById[third.Ask!.Id];
        Assert.Equal((op, run.Goal, "open"), (EstateModel.BodyString(ask, "to"), EstateModel.BodyString(ask, "goal"), EstateModel.BodyString(ask, "state")));
        Assert.Equal("work-order", ask.Subject!.Type);
        Assert.StartsWith("Carry it yourself, from vericairn work brief W-3: no agent carries a reserved action without a grant in force", EstateModel.BodyString(ask, "recommendation"), StringComparison.Ordinal);
        Assert.Equal("W-3 waits until you act, and nothing reserved is done for you; the goal's other steps go on.", EstateModel.BodyString(ask, "consequence"));
        Assert.Equal(op, EstateModel.BodyString(run.Estate.Load().ById[third.Assignment!.Id], "contributor"));

        // W-4's role has no principal: the sponsor is asked to assign someone or change the roster, and no one is assigned it.
        GoalStepResponse fourth = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "W-4", "unavailable", op), (fourth.Outcome, fourth.Step, fourth.Route, fourth.Principal));
        Assert.Null(fourth.Assignment);
        RecordEnvelope sponsorAsk = run.Estate.Load().ById[fourth.Ask!.Id];
        Assert.Equal(["assign someone", "change the roster"], sponsorAsk.Body.GetProperty("options").EnumerateArray().Select(o => o.GetString()));
        Assert.Contains("the estate's roster names no principal for the planner role", EstateModel.BodyString(sponsorAsk, "question"), StringComparison.Ordinal);
        Assert.StartsWith("Name a principal for the planner role in the roster", EstateModel.BodyString(sponsorAsk, "recommendation"), StringComparison.Ordinal);

        // Every eligible step is held now; nothing is dispatched, and both asks wait on the operator.
        GoalStepResponse fifth = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(fifth.Ok);
        Assert.Equal("none", fifth.Outcome);
        Assert.Contains("W-1 is review", fifth.Message, StringComparison.Ordinal);
        Assert.Contains("W-3 is assigned", fifth.Message, StringComparison.Ordinal);
        Assert.Contains("W-4 is asked", fifth.Message, StringComparison.Ordinal);
        Assert.Equal(2, Assert.Single(run.Service.Asks(op).Json.GetProperty("principals").EnumerateArray()).GetProperty("waiting").GetInt32());
        Assert.Empty(run.Service.Records("acceptance"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepIsRefusedBeforeAnythingIsRecordedWhenItsRunCouldNotStart()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        int assignments = run.Service.Records("assignment").Count;

        GoalStepResponse placeless = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
        Assert.False(placeless.Ok);
        Assert.Contains("An agent's run is made in a workspace of a repository", placeless.Message, StringComparison.Ordinal);

        // While the operator's key signs here without presence and no custody exception is in force, the agent's step is refused.
        GoalStepResponse guarded = Step(run, "guarded");
        Assert.False(guarded.Ok);
        Assert.Equal("refused", guarded.Problem!.Code);
        Assert.Equal(("W-1", "agent"), (guarded.Step, guarded.Route));
        Assert.Contains($"Device {run.OperatorDevice} (Operator's laptop, admitted) of Test Operator", guarded.Message, StringComparison.Ordinal);
        Assert.Contains($"vericairn goal custody-exception {run.Goal} --by <sponsor>", guarded.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", guarded.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("guarded")));

        RunEstate.Ok(run.Service.GoalPause(run.Goal, new GoalActionRequest(run.Estate.OperatorId)));
        Assert.Contains("is paused: a step is taken only for an active goal", run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource)).Message, StringComparison.Ordinal);
        Assert.Equal("not-found", run.Service.GoalStep("01M4TRNE1GYEK4P8MXQAB3C8Q4", new GoalStepRequest(run.Estate.OperatorId)).Problem!.Code);
        Assert.Empty(run.Service.Records("operation"));
        Assert.Empty(run.Service.Records("ask"));
        Assert.Equal(assignments, run.Service.Records("assignment").Count);
    }

    [Fact]
    public void APersonTakesOverAStepAnAgentHoldsAndItsRunInFlightIsAskedToStop()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        GoalStepResponse step = Step(run, "lingering", "ack-stop");
        Assert.True(step.Ok, step.Message);
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!run.Row(step.Run!.Run).Observed)
        {
            Assert.True(DateTimeOffset.UtcNow < until, "The run was not observed in time.");
            Thread.Sleep(50);
        }

        // The operator takes W-1 over: a new assignment carries the builder's brief and names its assignment, and the run is stopped.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);
        RunStopResponse stop = Assert.Single(taken.Stops!);
        Assert.Equal("contained", stop.State);
        Assert.Equal(op, stop.Stop!.RequestedBy);
        Assert.Equal("the step was taken over by Test Operator", stop.Stop.Reason);
        RecordEnvelope handover = run.Estate.Load().ById[taken.Assignment!.Id];
        Assert.Equal(op, EstateModel.BodyString(handover, "contributor"));
        Assert.Contains(handover.Body.GetProperty("brief").GetProperty("sources").EnumerateArray(), s => s.GetProperty("type").GetString() == "assignment" && s.GetProperty("id").GetString() == run.Assignment);
        Assert.Equal($"Takes the step over from Test Builder's assignment {run.Assignment}: the hand-over to Test Operator.", EstateModel.BodyString(handover, "note"));
        Assert.Equal("known-failure", run.Settled(step.Run.Run).Status);
        JsonElement route = Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "W-1").GetProperty("route");
        Assert.Equal(("assigned", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
        Assert.EndsWith($"which takes it over from Test Builder's assignment {run.Assignment}", route.GetProperty("reason").GetString(), StringComparison.Ordinal);

        // The agent's assignment holds the step no longer, a person's is not taken over, and a step goes over to a person only.
        Assert.Contains("no longer holds work order W-1", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op)).Message, StringComparison.Ordinal);
        Assert.Contains("not an agent's", run.Service.WorkTakeover(new TakeoverRequest(taken.Assignment.Id, op)).Message, StringComparison.Ordinal);
        string second = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-2", run.Estate.BuilderId, op, RunEstate.Json(RunEstate.Brief)))).Id;
        Assert.Contains("is not a person", run.Service.WorkTakeover(new TakeoverRequest(second, op, run.Estate.BuilderId)).Message, StringComparison.Ordinal);
        Assert.Equal("not-found", run.Service.WorkTakeover(new TakeoverRequest("01M4TRNE1GYEK4P8MXQAB3C8Q4", op)).Problem!.Code);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepNoAssignmentHeldIsAssignedOnlyWithItsRunsIntent()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        Done(run, run.Assignment);
        int assignments = run.Service.Records("assignment").Count;
        string? unverified = null;
        HookedLauncher.Install(run, () => unverified);

        // W-1's result awaits review, so the next step is W-2, which no assignment holds: each refusal of the worker's preflight leaves
        // it so, and records nothing.
        Dictionary<string, string> command = Command(run, "never");
        var unknown = new Dictionary<string, string>(command, StringComparer.Ordinal) { ["model"] = "x" };
        (GoalStepRequest Request, string Because, string? Unverified)[] cases =
        [
            (new GoalStepRequest(op), "An agent's run is made in a workspace of a repository", null),
            (new GoalStepRequest(op, "01M4TRNE1GYEK4P8MXQAB3C8Q4", command), "No repository resource has id 01M4TRNE1GYEK4P8MXQAB3C8Q4.", null),
            (new GoalStepRequest(op, run.Resource, command, WallMinutes: 0), "A run's cap is at least one wall minute", null),
            (new GoalStepRequest(op, run.Resource, unknown), "it takes no model", null),
            (new GoalStepRequest(op, run.Resource, command), "The claude-code launcher cannot run here: Claude Code is not signed in.", "Claude Code is not signed in."),
            (new GoalStepRequest(op, run.Resource, command, Branch: "bad..branch"), "The workspace could not be prepared: 'bad..branch' is not a branch name git accepts.", null),
        ];
        foreach ((GoalStepRequest request, string because, string? refusal) in cases)
        {
            unverified = refusal;
            GoalStepResponse refused = run.Service.GoalStep(run.Goal, request);
            Assert.False(refused.Ok, because);
            Assert.Equal(("none", "W-2", "agent"), (refused.Outcome, refused.Step, refused.Route));
            Assert.Null(refused.Assignment);
            Assert.Contains(because, refused.Message, StringComparison.Ordinal);
            Assert.EndsWith("nothing was recorded.", refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Routing assigned", refused.Message, StringComparison.Ordinal);
            Assert.Equal(assignments, run.Service.Records("assignment").Count);
            Assert.Empty(run.Service.Records("operation"));
        }

        unverified = null;
        JsonElement unassigned = Next(run, "W-2");
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("assignment").ValueKind);
        Assert.Equal("agent", unassigned.GetProperty("route").GetProperty("state").GetString());
        Assert.False(File.Exists(run.Report("never")));

        // Taken: routing's assignment of W-2 and the run's intent are recorded together.
        GoalStepResponse taken = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "taken")));
        Assert.True(taken.Ok, taken.Message);
        Assert.StartsWith($"Routing assigned W-2 to its agent (assignment {taken.Assignment!.Id}), recorded with the run's intent.", taken.Message, StringComparison.Ordinal);
        AgentRuns.Run carried = AgentRuns.Find(run.Estate.Load(), taken.Run!.Run)!;
        Assert.Equal(taken.Assignment.Id, carried.Assignment);
        Assert.Equal("observed-success", run.Settled(taken.Run.Run).Status);
        Done(run, taken.Assignment.Id);

        // A harness that cannot start once the intent is recorded: W-3's assignment and its run's intent were recorded together, and the
        // run is recorded as the known failure it is, which spent nothing.
        Order(run, "W-3", string.Empty);
        GoalStepResponse failed = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = Path.Combine(run.Scratch, "no-such-harness") }));
        Assert.False(failed.Ok);
        Assert.Equal(("none", "W-3"), (failed.Outcome, failed.Step));
        Assert.StartsWith($"Routing assigned W-3 to its agent (assignment {failed.Assignment!.Id}), recorded with the run's intent.", failed.Message, StringComparison.Ordinal);
        Assert.Contains("The run is recorded as a known failure; nothing ran and nothing is charged.", failed.Message, StringComparison.Ordinal);
        Assert.Equal("known-failure", run.Row(failed.Run!.Run).Status);
        Assert.Equal(failed.Assignment.Id, AgentRuns.Find(run.Estate.Load(), failed.Run.Run)!.Assignment);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static Dictionary<string, string> Command(RunEstate run, string report) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report(report)) };

    private static JsonElement Next(RunEstate run, string key) =>
        Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);

    private static GoalStepResponse Step(RunEstate run, string report, string scenario = "claude") =>
        run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, run.Report(report)) }));

    private static void Order(RunEstate run, string key, string extra) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["work"]{{extra}}}"""))));

    /// <summary>The builder's attempt and result on an assignment, as its MCP tools would record them: its result then awaits review.</summary>
    private static void Done(RunEstate run, string assignment)
    {
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "widget, a work branch", "Do the work."))).Id;
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))));
    }
}
