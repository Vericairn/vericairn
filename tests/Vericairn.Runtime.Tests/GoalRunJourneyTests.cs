// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Journey J49's cases with a step's run in flight (the estate's design/goals.md § 14), through the API with the stand-in harness: a
/// worker that disappears mid-run, a budget crossed mid-step, and a stop requested while a step runs. In each the goal's state stays
/// what its records say: a run nobody observes is neither stopped nor done, a crossed cap fences the goal until its sponsor raises it,
/// and a stopped goal takes no further step while its run is contained.
/// </summary>
[Collection("home")]
public class GoalRunJourneyTests
{
    [Fact]
    public void J49_AWorkerThatDisappearsMidRunHoldsItsStepAtItsCapUntilItsRunSettlesAndTheStepGoesOn()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;

        // The goal's first step is routed to the builder and its run dispatched; the worker supervising it then disappears without
        // observing its end.
        LostSupervisorSettlementTests.Lost lost = LostSupervisorSettlementTests.Abandon(run, "vanished", report =>
        {
            GoalStepResponse step = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("claude", report), WallMinutes: 10));
            Assert.True(step.Ok, step.Message);
            Assert.Equal(("run", "W-1", "agent"), (step.Outcome, step.Step, step.Route));
            return step.Run!;
        });

        // Nothing calls the run stopped or done: it is in flight, charged at its cap, and it holds its step and the goal's one run at a time.
        RunRow row = run.Row(lost.Run);
        Assert.Equal("dispatched", row.Status);
        Assert.Null(row.Usage);
        Assert.Equal(10, row.Charged.GetProperty("wallMinutes").GetInt64());
        JsonElement held = Route(run, "W-1");
        Assert.Equal("running", held.GetProperty("state").GetString());
        Assert.Contains("is in flight (dispatched); a stop is asked with vericairn worker stop", held.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("result"));
        JsonElement goal = run.Service.Goal(run.Goal)!.Json;
        Assert.Equal("active", goal.GetProperty("status").GetString());
        Assert.False(goal.GetProperty("ready").GetBoolean());
        GoalStepResponse waiting = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("claude", run.Report("second-step"))));
        Assert.False(waiting.Ok);
        Assert.Equal("W-2", waiting.Step);
        Assert.Contains("1 of the goal's runs is in flight, and its budget allows 1 at a time", waiting.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", waiting.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("second-step")));

        // Its worker last wrote a minute ago, within the run's stall limit: a stop asked of it stays requested, since it may still be observed.
        LostSupervisorSettlementTests.Write(lost, lost.Supervisor, lost.State.ProcessId!.Value, TimeSpan.FromHours(1) - TimeSpan.FromMinutes(1), tokens: null);
        RunStopResponse asked = run.Service.RunStop(lost.Run, new RunStopRequest(op, "Its worker is gone."));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal("requested", asked.State);
        Assert.Contains("stays in flight, charged at its cap", asked.Message, StringComparison.Ordinal);
        Assert.Equal("dispatched", run.Row(lost.Run).Status);

        // With its worker and its harness gone and nothing observed for longer than its stall limit, it settles as exited without
        // observation, charged what was observed up to its last heartbeat.
        LostSupervisorSettlementTests.Write(lost, lost.Supervisor, lost.State.ProcessId!.Value, TimeSpan.FromMinutes(3), tokens: null);
        RunSettleResponse settled = run.Service.RunSettle(lost.Run, new RunSettleRequest(op));
        Assert.True(settled.Ok, settled.Message);
        Assert.Equal("known-failure", settled.Status);
        Assert.Contains("exited without observation", settled.Message, StringComparison.Ordinal);
        Assert.Equal(3, run.Row(lost.Run).Charged.GetProperty("wallMinutes").GetInt64());

        // The step goes on: the builder's assignment carries it with another run, its second of three attempts, and the agent reports
        // done while the goal's predicate still fails.
        Assert.Equal("agent", Route(run, "W-1").GetProperty("state").GetString());
        GoalStepResponse again = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("claude", run.Report("again")), WallMinutes: 10));
        Assert.True(again.Ok, again.Message);
        Assert.Equal(("run", "W-1"), (again.Outcome, again.Step));
        Assert.Null(again.Assignment);
        Assert.Equal("observed-success", run.Settled(again.Run!.Run).Status);
        Assert.Equal(2, AgentRuns.All(run.Estate.Load()).Count(r => r.Assignment == run.Assignment));
        GoalStepTests.Done(run, run.Assignment);
        Assert.Equal("review", Route(run, "W-1").GetProperty("state").GetString());
        goal = run.Service.Goal(run.Goal)!.Json;
        Assert.Equal(("active", "not-met"), (goal.GetProperty("status").GetString(), goal.GetProperty("conditions")[0].GetProperty("verdict").GetString()));
        Assert.False(goal.GetProperty("ready").GetBoolean());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void J49_ABudgetCrossedMidStepFencesTheGoalUntilItsSponsorRaisesItByARevision()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        string op = run.Estate.OperatorId;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"OpenAI","product":"Codex"}}"""), Supersedes: run.Estate.BuilderId)));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, RunEstate.Json($$"""{"roles":[{"role":"builder","principals":["{{run.Estate.BuilderId}}"]},{"role":"decider","principals":["{{op}}"]}]}"""))));

        // The step's run reports more tokens than remain: the worker stops it mid-step and charges what it reported.
        GoalStepResponse step = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("codex-spender", run.Report("spender"))));
        Assert.True(step.Ok, step.Message);
        Assert.Equal(("run", "W-1", "agent"), (step.Outcome, step.Step, step.Route));
        Assert.Equal(1000, step.Run!.Cap!.Value.GetProperty("tokens").GetInt64());
        RunRow row = run.Settled(step.Run.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal("worker", row.Stop!.RequestedBy);
        Assert.Contains("below the reserved cap of 1000", row.Stop.Reason, StringComparison.Ordinal);
        Assert.Equal(1050, row.Charged.GetProperty("tokens").GetInt64());

        // Crossing the cap fences the goal: it is budget-limited, its sponsor's attention is asked, and no step is taken.
        // The goal-status record follows the run's settlement in a separate write.
        run.WaitUntil(() => run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString() == "budget-limited");
        LedgerRow ledger = Assert.Single(run.Service.Runs().Goals);
        Assert.Equal(("budget-limited", -50L), (ledger.Status, Assert.Single(ledger.Units, u => u.Unit == "tokens").Remaining));
        Assert.Contains(run.Service.AttentionGet(op).Items, a => a.SubjectType == "goal" && a.SubjectId == run.Goal);
        GoalStepResponse fenced = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("codex", run.Report("fenced"))));
        Assert.False(fenced.Ok);
        Assert.Contains($"Goal {run.Goal} is budget-limited: a step is taken only for an active goal", fenced.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("fenced")));

        // The sponsor raises the budget by a revision of the goal and activates it; the step's assignment goes on with another run,
        // capped at what remains of the raised budget, the first run's charge still counted.
        JsonObject body = JsonNode.Parse(run.Service.Goal(run.Goal)!.Json.GetProperty("goal").GetProperty("body").GetRawText())!.AsObject();
        body["budget"]!["tokens"] = 5000;
        string revised = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, RunEstate.Json(body.ToJsonString()), run.Goal))).Id;
        Assert.Equal("proposed", run.Service.Goal(revised)!.Json.GetProperty("status").GetString());
        RunEstate.Ok(run.Service.GoalActivate(revised, new GoalActionRequest(op)));
        GoalStepResponse again = run.Service.GoalStep(revised, new GoalStepRequest(op, run.Resource, Command("codex", run.Report("again"))));
        Assert.True(again.Ok, again.Message);
        Assert.Equal(("run", "W-1"), (again.Outcome, again.Step));
        Assert.Null(again.Assignment);
        Assert.Equal(3950, again.Run!.Cap!.Value.GetProperty("tokens").GetInt64());
        Assert.Equal("observed-success", run.Settled(again.Run.Run).Status);
        ledger = Assert.Single(run.Service.Runs().Goals);
        Assert.Equal(revised, ledger.Goal);
        Assert.Equal("active", ledger.Status);
        LedgerUnit tokens = Assert.Single(ledger.Units, u => u.Unit == "tokens");
        Assert.Equal(5000, tokens.Budget);
        Assert.True(tokens.Spent > 1050 && tokens.Remaining > 0, $"{tokens.Spent} of {tokens.Budget} tokens spent.");
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void J49_AStopRequestedWhileAStepRunsEndsTheGoalAndItsRunIsContainedWhenStopped()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        GoalStepResponse step = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("ack-stop", run.Report("running"))));
        Assert.True(step.Ok, step.Message);
        string running = step.Run!.Run;
        run.WaitUntil(() => run.Row(running).Observed && File.Exists(run.Report("running")), "the run and its stand-in to be ready");

        // The sponsor stops the goal while its step runs, and only the sponsor may: it ends at once as abandoned and takes no further step.
        Assert.Contains("Only the goal's sponsor", run.Service.GoalStop(run.Goal, new GoalActionRequest(run.Estate.BuilderId)).Problem!.Message, StringComparison.Ordinal);
        RunEstate.Ok(run.Service.GoalStop(run.Goal, new GoalActionRequest(op, "The widget was withdrawn.")));
        Assert.Equal("abandoned", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
        GoalStepResponse after = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command("claude", run.Report("after"))));
        Assert.False(after.Ok);
        Assert.Contains($"Goal {run.Goal} is abandoned: a step is taken only for an active goal", after.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("after")));

        // Stopping the goal leaves its run in flight (design/goals.md § 10, until the conductor stops runs): the run holds its step,
        // and the next view says how it is stopped.
        Assert.Equal("dispatched", run.Row(running).Status);
        JsonElement held = Route(run, "W-1");
        Assert.Equal("running", held.GetProperty("state").GetString());
        Assert.EndsWith("a stop is asked with vericairn worker stop", held.GetProperty("reason").GetString(), StringComparison.Ordinal);

        // The run is stopped through the worker: requested, transmitted, acknowledged and contained are each observed.
        RunStopResponse stop = run.Service.RunStop(running, new RunStopRequest(op, "The goal was stopped.", 5));
        Assert.True(stop.Ok, stop.Message);
        RunRow settled = run.Settled(running);
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal((op, "The goal was stopped."), (settled.Stop!.RequestedBy, settled.Stop.Reason));
        Assert.NotNull(settled.Stop.TransmittedAt);
        Assert.NotNull(settled.Stop.AcknowledgedAt);
        Assert.NotNull(settled.Stop.ContainedAt);
        Assert.Equal(0, Assert.Single(run.Service.Runs().Goals).InFlight);

        // The goal stays ended: it is not resumed, and no run of its work starts.
        Assert.Contains("ended as abandoned", run.Service.GoalResume(run.Goal, new GoalActionRequest(op)).Problem!.Message, StringComparison.Ordinal);
        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));
        Assert.False(refused.Ok);
        Assert.Contains("is abandoned", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("refused")));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static Dictionary<string, string> Command(string scenario, string report) =>
        new(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, report) };

    private static JsonElement Route(RunEstate run, string key) =>
        Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key).GetProperty("route");
}
