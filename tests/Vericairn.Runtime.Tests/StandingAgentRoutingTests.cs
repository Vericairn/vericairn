// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Standing agents carried one occurrence per goal step (from 0.9.4, GOAL-07; the estate's design/standing-agents.md §§ 5 to 7, 10 and
/// 12): goal due at an explicit time, each occurrence carried once as one assignment naming its key with its run, a key held by one
/// assignment whoever records it and whatever callers race, the period's caps deferring with one ask, three failed runs pausing the agent
/// with an ask, and a lapsed, paused or future-dated agent dispatching nothing. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class StandingAgentRoutingTests
{
    [Fact]
    public void AStandingAgentCarriesEachOccurrenceOnceAsOneAssignmentWithinItsPeriodCaps()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run, period: new JsonObject { ["unit"] = "months", ["runs"] = 1, ["wallMinutes"] = 120 });

        // goal due: the latest daily tick is due, the earlier ones coalesced into it.
        GoalDueResponse due = run.Service.GoalDue(goal)!;
        Assert.Equal(("vericairn.due/1", "active"), (due.Json.GetProperty("format").GetString(), due.Json.GetProperty("state").GetString()));
        JsonElement occurrence = Assert.Single(due.Json.GetProperty("occurrences").EnumerateArray());
        string key = occurrence.GetProperty("key").GetString()!;
        Assert.Equal(("due", "daily"), (occurrence.GetProperty("state").GetString(), occurrence.GetProperty("trigger").GetString()));
        Assert.EndsWith("/interval-2", key, StringComparison.Ordinal);
        Assert.Contains("Occurrences", due.Text, StringComparison.Ordinal);

        // goal step: one assignment naming the occurrence, its brief carrying the flow's never line, its run on the occurrence's branch.
        GoalStepResponse first = Step(run, goal, "first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal(("run", key, "agent", run.Estate.BuilderId), (first.Outcome, first.Step, first.Route, first.Principal));
        RecordEnvelope assignment = run.Estate.Load().ById[first.Assignment!.Id];
        Assert.Equal(key, assignment.Body.GetProperty("occurrence").GetProperty("key").GetString());
        Assert.Equal("daily", assignment.Body.GetProperty("occurrence").GetProperty("trigger").GetString());
        Assert.Contains(assignment.Body.GetProperty("brief").GetProperty("prohibitedEffects").EnumerateArray(), p => p.GetString() == "never publish anything outside the estate");
        Assert.Equal("observed-success", run.Settled(first.Run!.Run).Status);
        Assert.StartsWith("work/res-1-daily-", AgentRuns.Find(run.Estate.Load(), first.Run.Run)!.Target("branch"), StringComparison.Ordinal);

        // Whoever records it, a second assignment of the key is refused, and an assignment of the standing work naming none is too.
        JsonObject duplicate = JsonNode.Parse(assignment.Body.GetRawText())!.AsObject();
        duplicate.Remove("routedWith");
        WriteOutcome twice = run.Service.NewRecord(new NewRecordRequest("assignment", op, JsonSerializer.SerializeToElement(duplicate), $"work-order:{assignment.Subject!.Id}:{assignment.Subject.Hash}"));
        Assert.False(twice.Ok);
        Assert.Contains(twice.Problem!.Findings, f => f.Code == "standing.duplicate");
        duplicate.Remove("occurrence");
        WriteOutcome bare = run.Service.NewRecord(new NewRecordRequest("assignment", op, JsonSerializer.SerializeToElement(duplicate), $"work-order:{assignment.Subject.Id}:{assignment.Subject.Hash}"));
        Assert.Contains(bare.Problem!.Findings, f => f.Code == "assignment.occurrence");

        // Its result settles it: goal step has nothing to carry, and the period's one run is spent.
        FlowStepRoutingTests.Done(run, assignment.Id, ("findings", FlowFixture.Hash('a'), "text/markdown"));
        GoalDueResponse settled = run.Service.GoalDue(goal)!;
        Assert.Equal("settled", Assert.Single(settled.Json.GetProperty("occurrences").EnumerateArray()).GetProperty("state").GetString());
        Assert.Equal(1, settled.Json.GetProperty("period").GetProperty("spent").GetProperty("runs").GetInt32());
        GoalStepResponse nothing = Step(run, goal, "nothing");
        Assert.Equal("none", nothing.Outcome);
        Assert.Contains("no due occurrence", nothing.Message, StringComparison.Ordinal);

        // A request is an occurrence of its own; the period's cap defers it and tells the sponsor once in the period.
        GoalStepResponse requested = Step(run, goal, "request", occurrence: "request");
        Assert.True(requested.Ok, requested.Message);
        Assert.Equal(("ask", "deferred"), (requested.Outcome, requested.Route));
        Assert.Contains("Standing agent Research the widget has reached its caps for the period", requested.Message, StringComparison.Ordinal);
        RecordEnvelope ask = run.Estate.Load().ById[requested.Ask!.Id];
        Assert.StartsWith(StandingAgents.PeriodAsk, EstateModel.BodyString(ask, "question"), StringComparison.Ordinal);
        GoalStepResponse again = Step(run, goal, "request-again", occurrence: "request");
        Assert.Equal("none", again.Outcome);
        Assert.Contains($"ask {ask.Id} already told its sponsor", again.Message, StringComparison.Ordinal);

        // A person takes the occurrence over under its key.
        string person = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"human","name":"Test Researcher","roles":["contributor"]}""")))).Id;
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(assignment.Id, op, person));
        Assert.True(taken.Ok, taken.Message);
        Assert.Equal(key, run.Estate.Load().ById[taken.Assignment!.Id].Body.GetProperty("occurrence").GetProperty("key").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void CallersRacingForOneOccurrenceRecordOneAssignmentOfItsKey()
    {
        using var run = new RunEstate(recordFormat: 3);
        string goal = Standing(run, budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 3, ["concurrency"] = 2 });
        string key = Assert.Single(run.Service.GoalDue(goal)!.Json.GetProperty("occurrences").EnumerateArray()).GetProperty("key").GetString()!;
        using var start = new Barrier(2);
        GoalStepResponse[] responses = new GoalStepResponse[2];
        Thread[] callers = [.. Enumerable.Range(0, 2).Select(i => new Thread(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            responses[i] = Step(run, goal, "race-" + i, occurrence: key, branch: "work/race-" + i);
        }))];
        foreach (Thread caller in callers) caller.Start();
        foreach (Thread caller in callers) Assert.True(caller.Join(TestTiming.HangTimeoutMilliseconds), "A caller did not return.");

        // Exactly one assignment names the key, and exactly one caller started a run; the other was refused or found it held.
        EstateModel model = run.Estate.Load();
        RecordEnvelope held = Assert.Single(model.OfType("assignment"), a => StandingAgents.KeyOf(a) == key);
        Assert.Single(responses, r => r.Outcome == "run");
        GoalStepResponse other = Assert.Single(responses, r => r.Outcome != "run");
        Assert.True(other.Problem?.Findings.Any(f => f.Code == "standing.duplicate") == true || other.Message.Contains("Nothing to dispatch", StringComparison.Ordinal)
            || other.Message.Contains("standing.duplicate", StringComparison.Ordinal) || other.Message.Contains("is held by assignment", StringComparison.Ordinal), other.Message);
        foreach (GoalStepResponse started in responses.Where(r => r.Outcome == "run"))
        {
            run.Settled(started.Run!.Run!);
        }

        Assert.Equal(held.Id, Assert.Single(AgentRuns.All(model), r => r.Goal is not null && model.Chain(model.Latest(model.ById[goal])).Contains(r.Goal)).Assignment);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ThreeFailedRunsInARowPauseTheAgentWithAnAskAndTheSponsorsAnswerLetsItGoOn()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run, budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 });
        for (int i = 0; i < 3; i++)
        {
            GoalStepResponse failing = Step(run, goal, "fail-" + i, scenario: "claude-fails");
            Assert.True(failing.Ok, failing.Message);
            Assert.Equal("known-failure", run.Settled(failing.Run!.Run).Status);
        }

        GoalDueResponse held = run.Service.GoalDue(goal)!;
        Assert.Equal("held", held.Json.GetProperty("state").GetString());
        Assert.Equal(3, held.Json.GetProperty("failures").GetProperty("consecutive").GetInt32());

        // goal step asks the sponsor, once, and dispatches nothing while the ask waits; the run's own intent is refused too.
        GoalStepResponse asking = Step(run, goal, "ask");
        Assert.True(asking.Outcome == "ask", asking.Message);
        Assert.Equal(("ask", "attention"), (asking.Outcome, asking.Route));
        GoalStepResponse waiting = Step(run, goal, "wait");
        Assert.Equal("none", waiting.Outcome);
        Assert.Contains("paused by its failures", waiting.Message, StringComparison.Ordinal);

        // The agent that carried the failed runs records a decision answering the pause's ask in the sponsor's name: refused, and the pause holds.
        JsonObject answer = new()
        {
            ["title"] = "Go on", ["statement"] = "Let the researcher go on.", ["summary"] = "The failures were the stand-in's.", ["synthesis"] = "The operator answers the pause.",
            ["effects"] = new JsonArray("the agent goes on"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow),
            ["stake"] = "low", ["notAuthorized"] = new JsonArray("spending"), ["answers"] = new JsonArray(asking.Ask!.Id),
        };
        WriteOutcome own = run.Service.NewRecord(new NewRecordRequest("decision", run.Estate.BuilderId, JsonSerializer.SerializeToElement(answer)));
        Assert.False(own.Ok);
        Assert.Contains(own.Problem!.Findings, f => f.Code == "standing.own-output" && f.Severity == "error");
        Assert.Equal(("held", 3), (run.Service.GoalDue(goal)!.Json.GetProperty("state").GetString(), run.Service.GoalDue(goal)!.Json.GetProperty("failures").GetProperty("consecutive").GetInt32()));

        // The sponsor's answer lets it go on.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, JsonSerializer.SerializeToElement(answer))));
        Assert.Equal("active", run.Service.GoalDue(goal)!.Json.GetProperty("state").GetString());
        GoalStepResponse resumed = Step(run, goal, "resumed");
        Assert.Equal("run", resumed.Outcome);
        run.Settled(resumed.Run!.Run);
    }

    [Fact]
    public void ALapsedOrPausedAgentDispatchesNothingAndATimeAheadOfTheClockIsRefused()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        DateTimeOffset until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds());
        string goal = Standing(run, until: RecordParser.FormatTimestamp(until));

        GoalStepResponse ahead = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource) { Now = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(1)) });
        Assert.Equal("refused", ahead.Problem!.Code);
        Assert.Contains("later than this runtime's clock", ahead.Message, StringComparison.Ordinal);

        // Read before its first tick, nothing is due; an unreadable time and a goal that is not a standing agent say so.
        Assert.Empty(run.Service.GoalDue(goal, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(-5)))!.Json.GetProperty("occurrences").EnumerateArray());
        Assert.True(run.Service.GoalDue(goal, "soon")!.Json.TryGetProperty("problem", out _));
        Assert.False(run.Service.GoalDue(run.Goal)!.Json.GetProperty("standing").GetBoolean());
        Assert.Null(run.Service.GoalDue("01M7ASMJ43WMQ5VDASDYVZ7BGV"));

        // Paused, it dispatches nothing; resumed, it does.
        RunEstate.Ok(run.Service.GoalPause(goal, new GoalActionRequest(op)));
        Assert.Equal("paused", run.Service.GoalDue(goal)!.Json.GetProperty("state").GetString());
        Assert.Equal("refused", Step(run, goal, "paused").Problem!.Code);
        RunEstate.Ok(run.Service.GoalResume(goal, new GoalActionRequest(op)));

        // Past its until time, it returns to its sponsor: nothing is dispatched.
        run.Service.FlowClock = new FixedClock(until.AddMinutes(1));
        Assert.Equal("lapsed", run.Service.GoalDue(goal)!.Json.GetProperty("state").GetString());
        GoalStepResponse lapsed = Step(run, goal, "lapsed");
        Assert.Equal("refused", lapsed.Problem!.Code);
        Assert.Contains("is lapsed", lapsed.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void BlockedResultsOfRunsThatExitCleanlyPauseTheAgentAndADirectRunIsRefusedThenAsGoalStepIs()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run, budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 });
        string? assignment = null;
        for (int i = 0; i < 3; i++)
        {
            // Each run's harness exits 0 and its agent's result is blocked: every result is recorded, the earlier intents staying valid.
            GoalStepResponse step = Step(run, goal, "clean-" + i);
            Assert.True(step.Run is not null, step.Message);
            Assert.Equal("observed-success", run.Settled(step.Run!.Run).Status);
            assignment ??= step.Assignment!.Id;
            Blocked(run, assignment);
        }

        GoalDueResponse held = run.Service.GoalDue(goal)!;
        Assert.Equal(("held", 3), (held.Json.GetProperty("state").GetString(), held.Json.GetProperty("failures").GetProperty("consecutive").GetInt32()));
        Assert.Equal(0, run.Service.Check().Errors);

        // A run started outside goal step is refused at its intent while the pause holds.
        RunResponse direct = run.Service.RunStart(new RunStartRequest(assignment!, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 10, Settings: Command(run, "direct")));
        Assert.False(direct.Ok);
        Assert.Contains("failed in a row", direct.Message, StringComparison.Ordinal);
        Assert.Equal(("ask", "attention"), (Step(run, goal, "ask").Outcome, "attention"));
    }

    [Fact]
    public void ASettledOccurrenceIsNotRunAgainByADirectRunStart()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run);
        GoalStepResponse first = Step(run, goal, "first");
        Assert.True(first.Ok, first.Message);
        run.Settled(first.Run!.Run);
        FlowStepRoutingTests.Done(run, first.Assignment!.Id, ("findings", FlowFixture.Hash('a'), "text/markdown"));
        RunResponse again = run.Service.RunStart(new RunStartRequest(first.Assignment.Id, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 10, Settings: Command(run, "again")));
        Assert.False(again.Ok);
        Assert.Contains("is settled", again.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("again")));
    }

    [Fact]
    public void AReconciliationKeepingAStrandedAssignmentNeverReleasesASettledOccurrenceForASecondRun()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run);
        GoalStepResponse first = Step(run, goal, "first");
        Assert.True(first.Ok, first.Message);
        run.Settled(first.Run!.Run);
        FlowStepRoutingTests.Done(run, first.Assignment!.Id, ("findings", FlowFixture.Hash('a'), "text/markdown"));
        EstateModel model = run.Estate.Load();
        RecordEnvelope holder = model.ById[first.Assignment.Id];
        RecordEnvelope order = model.Current("work-order").Single(o => EstateModel.BodyString(o, "key") == "RES-1");

        // An assignment of the same key routed with a run and stored without it holds nothing, so the write boundary accepts it beside the holder.
        JsonObject body = JsonNode.Parse(holder.Body.GetRawText())!.AsObject();
        body["routedWith"] = "run";
        string stranded = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", op, JsonSerializer.SerializeToElement(body), $"work-order:{order.Id}:{order.ComputedHash}"))).Id;
        RecordEnvelope strandedRecord = run.Estate.Load().ById[stranded];

        // It is no duplicate, so the operator's reconciliation keeping it and withdrawing the holder is refused and nothing is recorded; one
        // keeping the holder is refused too, each assignment judged on its own revisions, not on the kept one's it would be withdrawn into.
        WriteOutcome released = run.Service.NewRecord(new NewRecordRequest("reconciliation", op, RunEstate.Json($$"""{"kept":"{{stranded}}","withdrawn":["{{holder.Id}}"],"note":"Keep the stranded one."}"""), $"assignment:{stranded}:{strandedRecord.ComputedHash}"));
        Assert.False(released.Ok);
        Assert.Contains(released.Problem!.Findings, f => f.Code == "reconciliation.occurrence");
        WriteOutcome kept = run.Service.NewRecord(new NewRecordRequest("reconciliation", op, RunEstate.Json($$"""{"kept":"{{holder.Id}}","withdrawn":["{{stranded}}"],"note":"Keep the holder."}"""), $"assignment:{stranded}:{strandedRecord.ComputedHash}"));
        Assert.False(kept.Ok);
        Assert.Contains(kept.Problem!.Findings, f => f.Code == "reconciliation.occurrence" && f.Message.Contains("is held by 1 of the assignments named", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Estate.Load().OfType("reconciliation"), r => r.Subject?.Id == stranded);

        // The occurrence stays settled: goal step dispatches no second run of it, and the estate has no errors.
        GoalStepResponse again = Step(run, goal, "again");
        Assert.Null(again.Run);
        Assert.False(File.Exists(run.Report("again")));
        Assert.Equal("settled", Assert.Single(run.Service.GoalDue(goal)!.Json.GetProperty("occurrences").EnumerateArray(), o => o.GetProperty("key").GetString() == StandingAgents.KeyOf(holder)).GetProperty("state").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ABackdatedNowNeverDispatchesALapsedAgentsOccurrenceToAPerson()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        DateTimeOffset until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds());
        string goal = Standing(run, until: RecordParser.FormatTimestamp(until), holder: op);
        run.Service.FlowClock = new FixedClock(until.AddMinutes(1));
        GoalStepResponse backdated = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource) { Now = RecordParser.FormatTimestamp(until.AddMinutes(-1)) });
        Assert.Equal("refused", backdated.Problem?.Code);
        Assert.Contains("whatever time --now names", backdated.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Estate.Load().OfType("assignment"), a => StandingAgents.KeyOf(a) is not null);
    }

    [Fact]
    public void PausingWithStopRunsAndStoppingAStandingAgentAskItsRunsToStopAndStoppingRetiresItsWork()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run);
        GoalStepResponse first = Step(run, goal, "running", scenario: "ack-stop");
        Assert.True(first.Ok, first.Message);
        run.WaitUntil(() => run.Row(first.Run!.Run).Observed && File.Exists(run.Report("running")), "the run and its stand-in to be ready");
        WriteOutcome paused = run.Service.GoalPause(goal, new GoalActionRequest(op) { StopRuns = true });
        Assert.True(paused.Ok, paused.Problem?.Message);
        Assert.Contains(paused.Notices!, n => n.Contains(first.Run!.Run, StringComparison.Ordinal));
        Assert.Equal("known-failure", run.Settled(first.Run!.Run).Status);
        RunEstate.Ok(run.Service.GoalResume(goal, new GoalActionRequest(op)));

        // The sponsor asked that run to stop: its known failure is not the agent's, so the failure pause counts nothing.
        Assert.Equal(0, run.Service.GoalDue(goal)!.Json.GetProperty("failures").GetProperty("consecutive").GetInt32());

        GoalStepResponse second = Step(run, goal, "running-again", scenario: "ack-stop");
        Assert.True(second.Ok, second.Message);
        run.WaitUntil(() => run.Row(second.Run!.Run).Observed && File.Exists(run.Report("running-again")), "the second run and its stand-in to be ready");
        WriteOutcome stopped = run.Service.GoalStop(goal, new GoalActionRequest(op, "The widget was withdrawn."));
        Assert.True(stopped.Ok, stopped.Problem?.Message);
        Assert.Contains(stopped.Notices!, n => n.StartsWith("Work order RES-1 is retired", StringComparison.Ordinal));
        Assert.Equal("known-failure", run.Settled(second.Run!.Run).Status);
        Assert.Equal("stopped", run.Service.GoalDue(goal)!.Json.GetProperty("state").GetString());
        Assert.Equal("retired", Assert.Single(run.Service.Status().Json.GetProperty("workOrders").EnumerateArray(), w => w.GetProperty("key").GetString() == "RES-1").GetProperty("status").GetString());
    }

    [Fact]
    public void TheRenewalAskComesOnceBeforeTheUntilTimeAndDispatchesNothing()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        // A term of six hours asks within its last three, half of it; the clock is controlled to an hour before its until time.
        DateTimeOffset until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds());
        string goal = Standing(run, until: RecordParser.FormatTimestamp(until));
        run.Service.FlowClock = new FixedClock(until.AddHours(-1));
        GoalStepResponse renewal = Step(run, goal, "renewal");
        Assert.True(renewal.Ok, renewal.Message);
        Assert.Equal(("ask", "attention"), (renewal.Outcome, renewal.Route));
        Assert.Null(renewal.Run);
        Assert.StartsWith(StandingAgents.RenewalAsk + " Research the widget before its until time", EstateModel.BodyString(run.Estate.Load().ById[renewal.Ask!.Id], "question"), StringComparison.Ordinal);
        GoalStepResponse next = Step(run, goal, "after-renewal");
        Assert.True(next.Outcome == "run", next.Message);
        run.Settled(next.Run!.Run);
    }

    [Fact]
    public void TheWriteBoundaryRefusesAConditionsOccurrenceThatDoesNotFailOverTheRecordsItNames()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Standing(run, triggers: new JsonArray(new JsonObject { ["key"] = "no-open-bugs", ["kind"] = "condition", ["record"] = new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["title"] = "Open bug" }, ["max"] = 0 } }));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json("""{"key":"BUG-1","kind":"ordinary","title":"Open bug","intent":"Fix it.","scope":["path:fix/"],"exitQuestion":"Fixed?","inputs":[],"deliverables":["fix"]}"""))));
        JsonElement due = Assert.Single(run.Service.GoalDue(goal)!.Json.GetProperty("occurrences").EnumerateArray());
        RecordEnvelope order = run.Estate.Load().Current("work-order").Single(o => EstateModel.BodyString(o, "key") == "RES-1");
        JsonObject Body(string failing)
        {
            JsonObject item = JsonNode.Parse(due.GetProperty("item").GetRawText())!.AsObject();
            item["failing"] = failing;
            string key = due.GetProperty("key").GetString()!;
            return new JsonObject
            {
                ["contributor"] = run.Estate.BuilderId, ["brief"] = JsonNode.Parse(RunEstate.Brief.Replace("path:widget/", "path:research/", StringComparison.Ordinal)),
                ["occurrence"] = new JsonObject { ["key"] = key[..(key.LastIndexOf('-') + 1)] + failing, ["trigger"] = "no-open-bugs", ["revision"] = due.GetProperty("revision").GetString(), ["item"] = item },
            };
        }

        JsonObject madeUp = Body(FlowFixture.Hash('9'));
        madeUp["brief"]!["repository"] = run.Resource;
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("assignment", op, JsonSerializer.SerializeToElement(madeUp), $"work-order:{order.Id}:{order.ComputedHash}"));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == StandingAgents.BoundaryCode);
        JsonObject real = Body(due.GetProperty("item").GetProperty("failing").GetString()!);
        real["brief"]!["repository"] = run.Resource;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", op, JsonSerializer.SerializeToElement(real), $"work-order:{order.Id}:{order.ComputedHash}")));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>The builder's attempt and blocked result on an assignment.</summary>
    private static void Blocked(RunEstate run, string assignment)
    {
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "research repository, a work branch", "Research."))).Id;
        var body = new JsonObject
        {
            ["outcome"] = "blocked", ["summary"] = "Blocked on a source.", ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Find another source.",
        };
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, JsonSerializer.SerializeToElement(body))));
    }

    private static Dictionary<string, string> Command(RunEstate run, string report) =>
        new(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report(report)) };

    /// <summary>A fixed clock, the runtime's controlled time.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Installs the researcher flow and a standing agent of it the operator sponsors and activates: a daily schedule whose first tick was
    /// two days ago, the request trigger, the builder (or the holders given) in the researcher role; returns the goal's id.
    /// </summary>
    internal static string Standing(RunEstate run, JsonObject? period = null, JsonObject? budget = null, string? until = null, string? holder = null, JsonArray? triggers = null, string[]? holders = null)
    {
        string op = run.Estate.OperatorId;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}"""), Supersedes: run.Estate.BuilderId)));
        TemplateDefinition flow = FlowFixture.Install(run.Estate, StandingFlows.Researcher());
        string order = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json("""{"key":"RES-1","kind":"ordinary","title":"Research the widget","intent":"Keep researching the widget.","scope":["path:research/"],"exitQuestion":"Is each occurrence's research recorded?","inputs":[],"deliverables":["cited findings"]}""")))).Id;
        DateTimeOffset first = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(-5).ToUnixTimeSeconds());
        var body = new JsonObject
        {
            ["title"] = "Research the widget", ["objective"] = "Keep researching the widget between releases.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = FlowFixture.Marker("RESEARCHED") }), ["constraints"] = new JsonArray("nothing outside research/"),
            ["kind"] = "stewardship", ["sponsor"] = op, ["budget"] = budget ?? new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 3 },
            ["roles"] = new JsonArray(FlowFixture.Role("researcher", holders ?? [holder ?? run.Estate.BuilderId])),
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order),
                ["standing"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(flow), ["subject"] = new JsonObject { ["topic"] = "the widget" },
                    ["triggers"] = triggers ?? new JsonArray(
                        new JsonObject { ["key"] = "daily", ["kind"] = "schedule", ["unit"] = "days", ["every"] = 1, ["firstAt"] = RecordParser.FormatTimestamp(first) },
                        new JsonObject { ["key"] = "asked", ["kind"] = "request" }),
                    ["period"] = period ?? new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 300 },
                    ["until"] = until ?? RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds())),
                },
            },
            ["inputs"] = new JsonArray(),
        };
        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(body)))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(op)));
        return goal;
    }

    /// <summary>One goal step of a standing agent taken by the principal named, its run capped at ten minutes, with the stand-in.</summary>
    internal static GoalStepResponse StepAs(RunEstate run, string goal, string report, string by) =>
        run.Service.GoalStep(goal, new GoalStepRequest(by, run.Resource, Command(run, report), WallMinutes: 10));

    /// <summary>One goal step of a standing agent, its run capped at ten minutes, with the stand-in in a scenario.</summary>
    internal static GoalStepResponse Step(RunEstate run, string goal, string report, string? occurrence = null, string scenario = "claude", string? branch = null) =>
        run.Service.GoalStep(goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, run.Report(report)) }, WallMinutes: 10, Branch: branch)
        {
            Occurrence = occurrence,
        });
}
