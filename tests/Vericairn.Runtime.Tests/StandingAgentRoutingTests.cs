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
        Assert.Contains(twice.Problem!.Findings, f => f.Code == "assignment.duplicate");
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
        Assert.True(other.Problem?.Findings.Any(f => f.Code == "assignment.duplicate") == true || other.Message.Contains("Nothing to dispatch", StringComparison.Ordinal)
            || other.Message.Contains("assignment.duplicate", StringComparison.Ordinal) || other.Message.Contains("is held by assignment", StringComparison.Ordinal), other.Message);
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

        // The sponsor's answer lets it go on.
        JsonObject answer = new()
        {
            ["title"] = "Go on", ["statement"] = "Let the researcher go on.", ["summary"] = "The failures were the stand-in's.", ["synthesis"] = "The operator answers the pause.",
            ["effects"] = new JsonArray("the agent goes on"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow),
            ["stake"] = "low", ["notAuthorized"] = new JsonArray("spending"), ["answers"] = new JsonArray(asking.Ask!.Id),
        };
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

    /// <summary>A fixed clock, the runtime's controlled time.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Installs the researcher flow and a standing agent of it the operator sponsors and activates: a daily schedule whose first tick was
    /// two days ago, the request trigger, the builder in the researcher role; returns the goal's id.
    /// </summary>
    internal static string Standing(RunEstate run, JsonObject? period = null, JsonObject? budget = null, string? until = null)
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
            ["roles"] = new JsonArray(FlowFixture.Role("researcher", run.Estate.BuilderId)),
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order),
                ["standing"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(flow), ["subject"] = new JsonObject { ["topic"] = "the widget" },
                    ["triggers"] = new JsonArray(
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

    /// <summary>One goal step of a standing agent, its run capped at ten minutes, with the stand-in in a scenario.</summary>
    internal static GoalStepResponse Step(RunEstate run, string goal, string report, string? occurrence = null, string scenario = "claude", string? branch = null) =>
        run.Service.GoalStep(goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, run.Report(report)) }, WallMinutes: 10, Branch: branch)
        {
            Occurrence = occurrence,
        });
}
