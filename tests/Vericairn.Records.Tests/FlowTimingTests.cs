// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A step's timing contract (the estate's design/flow.md § 7, decision 01M75X387N0J4FRWQV80M406CY; PROC-18): instant by default, after a
/// delay, at a time, on a schedule, within a window or on an event, each due time computed from the records and a controlled clock and
/// shown before the step runs; deadlines and their missed-time rules, a run catching up after downtime once, local times across
/// daylight-saving changes, a reserved act armed ahead of its time and checked again when it runs, a venture's configuration with the
/// narrowest winning and a fixed timing never changed, and what the contract refuses.
/// </summary>
public class FlowTimingTests
{
    /// <summary>A Friday afternoon, after the run's activation, from which the tests count.</summary>
    private static readonly DateTimeOffset Friday = new(2026, 10, 16, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AnInstantStepStartsAsSoonAsWhatItWaitsOnIsDone()
    {
        foreach (JsonObject? when in new JsonObject?[] { null, new JsonObject { ["instant"] = true } })
        {
            using var run = new FlowFixture(Timed(when));
            AssertNoErrors(run);
            Assert.Equal("blocked", Timed(run, run.Estate.Clock).State);
            RecordEnvelope prepared = Prepare(run, Friday);
            FlowPosition.StepInstance timed = Timed(run, prepared.AtTime);
            Assert.Equal("next", timed.State);
            Assert.Null(timed.Due);
        }
    }

    [Fact]
    public void ADelayCountsFromTheCloseOfWhatItWaitsOnTheRunsActivationOrAnItemsTime()
    {
        // Two hours after the close of the step it waits on: blocked, its due time and why shown, until the clock reaches it.
        using (var run = new FlowFixture(Timed(Delay("PT2H"))))
        {
            RecordEnvelope prepared = Prepare(run, Friday);
            FlowPosition.StepInstance waiting = Timed(run, Friday.AddHours(1));
            Assert.Equal(("blocked", Friday.AddHours(2)), (waiting.State, waiting.Due!.At));
            Assert.Equal($"it is due at 2026-10-16T18:30:00Z: PT2H after prepare's close at 2026-10-16T16:30:00Z", waiting.Reasons[0]);
            Assert.Equal(("delay", "PT2H after the close of the steps it waits on", "UTC"), ("delay", waiting.Due.Timing, waiting.Due.Zone));
            Assert.Equal("next", Timed(run, Friday.AddHours(2)).State);
            Assert.Equal(prepared.AtTime, Friday);
        }

        // Five days after the run's activation, whenever the step before it was done.
        using (var run = new FlowFixture(Timed(Delay("P5D", "activation"))))
        {
            RecordEnvelope activation = GoalRules.Statuses(run.Estate.Load(), run.Goal).Single();
            Prepare(run, Friday);
            Assert.Equal(("blocked", activation.AtTime.AddDays(5)), (Timed(run, Friday).State, Timed(run, Friday).Due!.At));
            Assert.Equal("next", Timed(run, activation.AtTime.AddDays(5)).State);
        }

        // Half an hour after the time of an earlier step's item, the record that carries it.
        using (var run = new FlowFixture(Timed(Delay("PT30M", "prepare.marker"), prepareOutputs: new JsonArray(Marker()))))
        {
            AssertNoErrors(run);
            RecordEnvelope prepared = Prepare(run, Friday);
            FlowPosition.StepInstance waiting = Timed(run, Friday);
            Assert.Equal(Friday.AddMinutes(30), waiting.Due!.At);
            Assert.Contains($"PT30M after the time of item prepare.marker at 2026-10-16T16:30:00Z", waiting.Due.Reason, StringComparison.Ordinal);
            Assert.Equal(prepared.Id, waiting.Due.From!.Id);
        }
    }

    [Theory]
    [InlineData("2026-10-20T09:00:00Z", null, "2026-10-20T09:00:00Z")]
    [InlineData("2026-10-20T11:00:00+02:00", null, "2026-10-20T09:00:00Z")]
    [InlineData("2026-10-20T09:00", "Europe/London", "2026-10-20T08:00:00Z")]
    [InlineData("2026-03-08T02:30", "America/New_York", "2026-03-08T07:30:00Z")]
    [InlineData("2026-11-01T01:30", "America/New_York", "2026-11-01T05:30:00Z")]
    [InlineData("2026-03-29T01:30", "Europe/London", "2026-03-29T01:30:00Z")]
    [InlineData("2026-10-25T01:30", "Europe/London", "2026-10-25T00:30:00Z")]
    public void AnAtTimingIsAnInstantOrALocalTimeInItsZoneWithDaylightSavingDefined(string time, string? zone, string expected)
    {
        // A local time the clocks skip moves forward by the change, 02:30 becoming 03:30; one they repeat is its first occurrence.
        var when = new JsonObject { ["at"] = new JsonObject { ["time"] = time } };
        if (zone is not null)
        {
            when["zone"] = zone;
        }

        using var run = new FlowFixture(Timed(when));
        AssertNoErrors(run);
        Prepare(run, Friday);
        FlowPosition.StepInstance timed = Timed(run, Friday);
        Assert.Equal(Instant(expected), timed.Due!.At);
        Assert.Equal(zone ?? FlowTiming.Utc, timed.Due.Zone);
        Assert.Equal(Instant(expected) > Friday ? "blocked" : "next", timed.State);
    }

    [Fact]
    public void AnAtTimingReadsItsTimeFromAnEarlierItemsRecord()
    {
        JsonArray outputs = new(new JsonObject { ["name"] = "go", ["kind"] = "decision", ["required"] = true });
        JsonArray done = new(new JsonObject { ["type"] = "decision", ["fields"] = new JsonObject { ["title"] = "Go ahead" } });
        using var run = new FlowFixture(Timed(new JsonObject { ["at"] = new JsonObject { ["output"] = "prepare.go", ["field"] = "decidedAt" } }, prepareOutputs: outputs, prepareDone: done));
        AssertNoErrors(run);
        FlowPosition.StepInstance unready = Timed(run, Friday);
        Assert.Equal(("blocked", (DateTimeOffset?)null), (unready.State, unready.Due?.At));
        run.Estate.SetClock(Friday.AddSeconds(-1));
        JsonObject decision = run.Policies.DecisionBody();
        decision["title"] = "Go ahead";
        decision["decidedAt"] = "2026-10-16T15:00:00Z";
        RecordEnvelope go = run.Add("decision", decision);
        FlowPosition.StepInstance timed = Timed(run, Friday);
        Assert.Equal((Instant("2026-10-16T15:00:00Z"), go.Id), (timed.Due!.At, timed.Due.From!.Id));
        Assert.Equal("next", timed.State);
    }

    [Theory]
    [InlineData("2026-10-16T10:00:00Z", "2026-10-19T08:00:00Z")]
    [InlineData("2026-10-23T10:00:00Z", "2026-10-26T09:00:00Z")]
    public void AScheduleGivesItsFirstTimeOnceTheStepIsReadyInTheVenturesZoneAcrossADaylightSavingChange(string ready, string expected)
    {
        // Mondays at 09:00 in London: in summer time before the clocks go back on October 25, 2026, and in winter time after.
        using var run = new FlowFixture(Timed(new JsonObject { ["schedule"] = new JsonObject { ["days"] = new JsonArray("mon"), ["times"] = new JsonArray("09:00") } }),
            options: new JsonObject { ["zone"] = "Europe/London" });
        AssertNoErrors(run);
        Prepare(run, Instant(ready));
        FlowPosition.StepInstance timed = Timed(run, Instant(ready));
        Assert.Equal(("blocked", Instant(expected), "Europe/London"), (timed.State, timed.Due!.At, timed.Due.Zone));
        Assert.Equal("next", Timed(run, Instant(expected)).State);

        // The spring change the same way: the Monday after March 29, 2026 is in summer time.
        FlowTiming mondays = FlowTiming.Read(FlowFixture.Element(new JsonObject { ["schedule"] = new JsonObject { ["days"] = new JsonArray("mon"), ["times"] = new JsonArray("09:00") } }));
        Assert.Equal(Instant("2026-03-30T08:00:00Z"), mondays.NextScheduled(Instant("2026-03-27T10:00:00Z"), FlowTiming.ZoneOf("Europe/London")));
        Assert.Equal(Instant("2026-03-23T09:00:00Z"), mondays.NextScheduled(Instant("2026-03-20T10:00:00Z"), FlowTiming.ZoneOf("Europe/London")));
    }

    [Fact]
    public void AWindowHoldsAStepOutsideItsHoursAndQuietHoursAndADelayLandsInTheNextWindow()
    {
        var when = Delay("PT2H");
        when["window"] = new JsonObject
        {
            ["open"] = new JsonArray(new JsonObject { ["days"] = new JsonArray("mon", "tue", "wed", "thu", "fri"), ["from"] = "09:00", ["to"] = "17:00" }),
            ["quiet"] = new JsonArray(new JsonObject { ["from"] = "12:00", ["to"] = "13:00" }),
        };
        using var run = new FlowFixture(Timed(when));
        AssertNoErrors(run);
        Prepare(run, Friday);

        // Two hours after a close at 16:30 on Friday is 18:30, outside the window: it is due when the window next opens, on Monday.
        DateTimeOffset monday = Instant("2026-10-19T09:00:00Z");
        FlowPosition.StepInstance saturday = Timed(run, Instant("2026-10-17T10:00:00Z"));
        Assert.Equal(("blocked", monday), (saturday.State, saturday.Due!.At));
        Assert.EndsWith(", in its window from 2026-10-19T09:00:00Z", saturday.Due.Reason, StringComparison.Ordinal);
        Assert.Equal("next", Timed(run, monday).State);

        // Due, but in the quiet hours: it waits for the window to open again.
        FlowPosition.StepInstance quiet = Timed(run, Instant("2026-10-19T12:30:00Z"));
        Assert.Equal("blocked", quiet.State);
        Assert.EndsWith("in UTC): it opens next at 2026-10-19T13:00:00Z", quiet.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("next", Timed(run, Instant("2026-10-19T13:00:00Z")).State);
    }

    [Fact]
    public void AnEventStartsAStepAtTheFirstRecordItsPredicateCountsAfterTheStepIsReady()
    {
        JsonObject predicate = FlowFixture.Marker("SIGNAL");
        using var run = new FlowFixture(Timed(new JsonObject { ["event"] = new JsonObject { ["kind"] = "record", ["predicate"] = predicate } }));
        AssertNoErrors(run);
        RecordEnvelope early = run.Marked("SIGNAL");
        Prepare(run, Friday);

        // A record from before the step was ready is no event for it: it waits, the records read again rather than any event trusted.
        FlowPosition.StepInstance waiting = Timed(run, Friday.AddMinutes(5));
        Assert.Equal(("blocked", (DateTimeOffset?)null), (waiting.State, waiting.Due!.At));
        Assert.StartsWith("it waits for a record event whose predicate holds (work-order records with key SIGNAL", waiting.Reasons[0], StringComparison.Ordinal);

        run.Estate.SetClock(Friday.AddMinutes(10));
        JsonObject again = PolicyFixture.Body(early);
        again["title"] = "Signal again";
        RecordEnvelope signal = run.Add("work-order", again, supersedes: early.Id);
        FlowPosition.StepInstance started = Timed(run, signal.AtTime);
        Assert.Equal(("next", (DateTimeOffset?)signal.AtTime, signal.Id), (started.State, started.Due!.At, started.Due.From!.Id));
        Assert.Contains($"work-order {signal.Id}", started.Due.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("late")]
    [InlineData("skip")]
    [InlineData("attention")]
    public void ADeadlinePassedBeforeTheStepWasTakenTakesItsMissedTimeRule(string rule)
    {
        JsonObject when = Delay("PT1H");
        when["deadline"] = "PT1H";
        when["missed"] = rule;
        using var run = new FlowFixture(Timed(when, optional: rule == "skip"));
        AssertNoErrors(run);
        Prepare(run, Friday);
        DateTimeOffset deadline = Friday.AddHours(2);
        FlowPosition.StepInstance inTime = Timed(run, Friday.AddHours(1).AddMinutes(30));
        Assert.Equal(("next", deadline, (string?)null), (inTime.State, inTime.Due!.Deadline, inTime.Due.Missed));

        DateTimeOffset after = Friday.AddHours(3);
        FlowPosition.StepInstance missed = Timed(run, after);
        switch (rule)
        {
            case "late":
                Assert.Equal(("next", "late"), (missed.State, missed.Due!.Missed));
                Assert.Equal("it missed its deadline at 2026-10-16T18:30:00Z: its timing runs it once, late", missed.Reasons[^1]);
                break;
            case "skip":
                Assert.Equal(("off", (DateTimeOffset?)deadline), (missed.State, missed.SkippedAt));
                Assert.StartsWith("skipped: it missed its deadline at 2026-10-16T18:30:00Z", missed.Reasons[0], StringComparison.Ordinal);
                break;
            default:
                // The sponsor is asked about it; once the ask is answered, the step runs once, late.
                Assert.Equal(("stopped", "attention", "missed"), (missed.State, missed.Stop, missed.Attention!.Kind));
                FlowStops.RunStop stop = Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, after).Stops, s => s.Step?.Key == "timed");
                Assert.Equal(("attention", "due", run.Estate.OperatorId), (stop.Kind, stop.Delivery, stop.Principal));
                Answer(run, missed.Attention.Trigger, after);
                FlowPosition.StepInstance late = Timed(run, after.AddMinutes(10));
                Assert.Equal(("next", (FlowPosition.Attention?)null), (late.State, late.Attention));
                Assert.Equal("it missed its deadline at 2026-10-16T18:30:00Z, and the sponsor answered the ask about it: it runs once, late", late.Reasons[^1]);
                break;
        }
    }

    [Fact]
    public void AfterDowntimeAMissedScheduledStepRunsOnceAndRaisesAttention()
    {
        using var run = new FlowFixture(Timed(new JsonObject { ["schedule"] = new JsonObject { ["times"] = new JsonArray("09:00") }, ["deadline"] = "PT1H" }));
        AssertNoErrors(run);
        Prepare(run, Instant("2026-10-16T08:00:00Z"));

        // Three days of downtime pass three times its schedule gives: it is asked about once, for the first, and runs once.
        DateTimeOffset back = Instant("2026-10-19T15:00:00Z");
        FlowPosition.Position position = FlowPosition.Derive(run.Estate.Load(), run.Goal, back);
        FlowPosition.StepInstance missed = Assert.Single(position.Steps, s => s.Step.Key == "timed");
        Assert.Equal(("stopped", Instant("2026-10-16T09:00:00Z"), Instant("2026-10-16T10:00:00Z"), "attention"), (missed.State, missed.Due!.At, missed.Due.Deadline, missed.Due.Missed));
        Assert.Single(position.Stops, s => s.Step?.Key == "timed");
        Answer(run, missed.Attention!.Trigger, back);
        Assert.Equal("next", Timed(run, back.AddMinutes(5)).State);
        run.Estate.SetClock(back.AddMinutes(10));
        run.Marked("TIMED");
        Assert.Equal(("done", (FlowPosition.StepDue?)null), (Timed(run, back.AddHours(1)).State, Timed(run, back.AddHours(1)).Due));
        Assert.DoesNotContain(FlowPosition.Derive(run.Estate.Load(), run.Goal, back.AddDays(3)).Stops, s => s.Step?.Key == "timed");
    }

    [Fact]
    public void AReservedActAtATimeIsArmedAheadAndCountsOnlyOnceItsTimeHasCome()
    {
        DateTimeOffset embargo = Instant("2026-10-20T09:00:00Z");
        using var run = new FlowFixture(Timed(new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-10-20T09:00:00Z" }, ["fixed"] = true }, reserved: true));
        AssertNoErrors(run);
        Prepare(run, Friday);

        // Armed: its request reaches the operator ahead of its time, with its exact command.
        FlowPosition.StepInstance armed = Timed(run, Friday);
        Assert.Equal(("stopped", "reserved", embargo), (armed.State, armed.Stop, armed.Due!.At));
        Assert.Equal($"example-provider publish --recording {run.Work.Id} --work {run.Work.Id}", armed.Command);
        Assert.Contains("armed: its act is due at 2026-10-20T09:00:00Z (at 2026-10-20T09:00:00Z); the operator may arm the platform's scheduler for that time, which is the act itself, or carry the act then, and the act counts from then while its author still holds it", armed.Reasons);

        // The operator arms the platform's scheduler, the act itself: it counts only once the time has come.
        run.Estate.SetClock(Instant("2026-10-17T09:59:59Z"));
        run.Marked("TIMED");
        FlowPosition.StepInstance waiting = Timed(run, Instant("2026-10-18T10:00:00Z"));
        Assert.Equal("blocked", waiting.State);
        Assert.StartsWith("armed: the operator's act, recorded at 2026-10-17T10:00:00Z, counts from 2026-10-20T09:00:00Z", waiting.Reasons[0], StringComparison.Ordinal);
        FlowPosition.StepInstance done = Timed(run, embargo);
        Assert.Equal(("done", (DateTimeOffset?)embargo), (done.State, done.DoneAt));
        Assert.Contains("its act counts from 2026-10-20T09:00:00Z, when it was due (at 2026-10-20T09:00:00Z)", done.Reasons);
    }

    [Fact]
    public void AReservedActArmedAheadIsCheckedAgainWhenItsTimeComes()
    {
        // In an estate with signed history, an act signed history places before its author's operator role was revoked still counts as
        // that author's; a timed act also needs its author to hold the act when its time comes.
        using var run = new FlowFixture(Timed(new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-10-20T09:00:00Z" }, ["fixed"] = true }, reserved: true));
        RecordEnvelope person = run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("operator") });
        Signing.StoreSigner signer = run.Policies.AdmitSigner(person.Id);
        Checkpoint(run, "adoption");
        Prepare(run, Friday);
        run.Estate.SetClock(Instant("2026-10-17T09:59:59Z"));
        RecordEnvelope act = run.Estate.Add("work-order", person.Id, MarkerBody("TIMED").ToJsonString(), signer: signer);
        Checkpoint(run, "manual");
        run.Estate.SetClock(Instant("2026-10-18T09:59:59Z"));
        run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("contributor") }, supersedes: person.Id);
        Assert.True(FlowPosition.IsReservedAct(run.Estate.Load(), act));

        FlowPosition.StepInstance lapsed = Timed(run, Instant("2026-10-20T09:00:00Z"));
        Assert.Equal(("stopped", "reserved"), (lapsed.State, lapsed.Stop));
        Assert.Contains($"the operator's act work-order {act.Id} no longer counts: Second Person does not hold the act at 2026-10-20T09:00:00Z, when the step runs", lapsed.Reasons);
    }

    [Fact]
    public void AVentureConfiguresTimingAtTheEstateOrTheGoalTheNarrowestWinning()
    {
        using var run = new FlowFixture(Timed(Delay("PT1H")), run: false);
        RecordEnvelope defaults = Defaults(run, new JsonObject { ["when.timed"] = """{"delay":{"after":"PT2H"}}""", ["zone"] = "Europe/London" });
        RecordEnvelope goal = run.Add("goal", run.GoalBody(new JsonObject { ["when.timed"] = """{"delay":{"after":"PT3H"}}""" }));
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);
        RecordEnvelope plain = run.Add("goal", run.GoalBody());
        EstateModel model = run.Estate.Load();
        Assert.Empty(model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        FlowDefinition flow = FlowRuns.Resolve(model, goal).Flow!;
        FlowStep timed = flow.Step("timed")!;

        // The goal's own timing wins over the estate's, and the estate's zone applies; a run without its own takes the estate's timing.
        FlowRuns.Configuration configured = FlowRuns.Configure(model, goal, flow);
        Assert.Equal((TimeSpan.FromHours(3), "the goal's plan"), (configured.Timing(timed).After!.Value, configured.Values["when.timed"].Source));
        Assert.Equal(("Europe/London", $"the estate defaults (template-defaults {defaults.Id})"), (configured.Zone, configured.Values["zone"].Source));
        Assert.Equal(TimeSpan.FromHours(2), FlowRuns.Configure(model, plain, flow).Timing(timed).After);
        Assert.Equal(TimeSpan.FromHours(1), FlowTiming.Read(timed.When).After);

        run.Estate.SetClock(Friday.AddSeconds(-1));
        run.Marked("PREPARED");
        FlowPosition.StepInstance due = Assert.Single(FlowPosition.Derive(run.Estate.Load(), goal, Friday).Steps, s => s.Step.Key == "timed");
        Assert.Equal((Friday.AddHours(3), "Europe/London"), (due.Due!.At, due.Due.Zone));
        Assert.Contains("when.timed {\"delay\":{\"after\":\"PT3H\"}}, from the goal's plan", FlowPosition.RenderText(run.Estate.Load(), FlowPosition.Derive(run.Estate.Load(), goal, Friday)), StringComparison.Ordinal);
    }

    [Fact]
    public void AFixedTimingIsNeverConfiguredAndAConfiguredTimingIsCheckedAsTheFlowsOwn()
    {
        using var run = new FlowFixture(Timed(new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-10-20T09:00:00Z" }, ["fixed"] = true }), run: false);
        RecordEnvelope embargoed = Defaults(run, new JsonObject { ["when.timed"] = """{"delay":{"after":"PT1H"}}""" });
        RecordEnvelope fixedGoal = run.Add("goal", run.GoalBody(new JsonObject { ["when.timed"] = """{"instant":true}""" }));
        RecordEnvelope badZone = run.Add("goal", run.GoalBody(new JsonObject { ["zone"] = "Mars/Olympus_Mons" }));
        RecordEnvelope badTiming = run.Add("goal", run.GoalBody(new JsonObject { ["when.prepare"] = """{"delay":{"after":"soon"}}""" }));
        RecordEnvelope skipped = run.Add("goal", run.GoalBody(new JsonObject { ["when.prepare"] = """{"delay":{"after":"PT1H"},"deadline":"PT1H","missed":"skip"}""" }));
        RecordEnvelope selfFixed = run.Add("goal", run.GoalBody(new JsonObject { ["when.prepare"] = """{"delay":{"after":"PT1H"},"fixed":true}""" }));
        RecordEnvelope unknown = run.Add("goal", run.GoalBody(new JsonObject { ["when.mixdown"] = """{"instant":true}""" }));
        RecordEnvelope fine = run.Add("goal", run.GoalBody(new JsonObject { ["when.prepare"] = """{"delay":{"after":"PT1H","from":"activation"}}""", ["zone"] = "Asia/Kolkata" }));
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, embargoed), f => f.Code == "flow.timing-fixed");
        Assert.Contains(Errors(model, fixedGoal), f => f.Code == "flow.timing-fixed");
        Assert.Contains(Errors(model, badZone), f => f.Code == "flow.zone");
        Assert.Contains(Errors(model, badTiming), f => f.Code == "flow.when" && f.Message.Contains("A duration is ISO 8601", StringComparison.Ordinal));
        Assert.Contains(Errors(model, skipped), f => f.Code == "flow.when" && f.Message.Contains("Only an optional step is skipped", StringComparison.Ordinal));
        Assert.Contains(Errors(model, selfFixed), f => f.Code == "flow.when" && f.Message.Contains("Only the flow fixes a step's timing", StringComparison.Ordinal));
        Assert.Contains(Errors(model, unknown), f => f.Code == "flow.option");
        Assert.Empty(Errors(model, fine));

        // A configuration the record refuses never applies: the run reads the flow's fixed timing.
        FlowDefinition flow = FlowRuns.Resolve(model, fixedGoal).Flow!;
        Assert.Equal("2026-10-20T09:00:00Z", FlowRuns.Configure(model, fixedGoal, flow).Timing(flow.Step("timed")!).Time);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "two-starts", "flow.when" },
        { "skip-on-a-required-step", "flow.when" },
        { "unknown-zone", "flow.zone" },
        { "not-a-time", "flow.when" },
        { "delay-from-a-later-step", "flow.when" },
        { "item-not-declared", "flow.when" },
        { "missed-without-a-deadline", "flow.when" },
        { "empty-window", "flow.when" },
        { "span-ending-where-it-starts", "flow.when" },
        { "not-a-clock-time", "body.pattern" },
        { "unknown-day", "flow.when" },
        { "unknown-event-kind", "body.enum" },
        { "event-predicate-of-an-unknown-type", "goal.predicate" },
        { "fixed-false", "flow.when" },
        { "output-without-its-field", "flow.when" },
        { "deadline-on-a-watch", "flow.when" },
        { "instant-false", "flow.when" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void TheContractRefusesATimingAStepCannotTake(string change, string code)
    {
        JsonObject when = change switch
        {
            "two-starts" => new JsonObject { ["delay"] = new JsonObject { ["after"] = "PT1H" }, ["at"] = new JsonObject { ["time"] = "2026-10-20T09:00:00Z" } },
            "skip-on-a-required-step" => new JsonObject { ["delay"] = new JsonObject { ["after"] = "PT1H" }, ["deadline"] = "PT1H", ["missed"] = "skip" },
            "unknown-zone" => new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-10-20T09:00" }, ["zone"] = "Mars/Olympus_Mons" },
            "not-a-time" => new JsonObject { ["at"] = new JsonObject { ["time"] = "next Tuesday" } },
            "delay-from-a-later-step" => new JsonObject { ["delay"] = new JsonObject { ["after"] = "PT1H", ["from"] = "publish" } },
            "item-not-declared" => new JsonObject { ["delay"] = new JsonObject { ["after"] = "PT1H", ["from"] = "prepare.minutes" } },
            "missed-without-a-deadline" => new JsonObject { ["missed"] = "late" },
            "empty-window" => new JsonObject { ["window"] = new JsonObject() },
            "span-ending-where-it-starts" => new JsonObject { ["window"] = new JsonObject { ["open"] = new JsonArray(new JsonObject { ["from"] = "09:00", ["to"] = "09:00" }) } },
            "not-a-clock-time" => new JsonObject { ["schedule"] = new JsonObject { ["times"] = new JsonArray("9am") } },
            "unknown-day" => new JsonObject { ["schedule"] = new JsonObject { ["days"] = new JsonArray("someday"), ["times"] = new JsonArray("09:00") } },
            "unknown-event-kind" => new JsonObject { ["event"] = new JsonObject { ["kind"] = "email", ["predicate"] = FlowFixture.Marker("SIGNAL") } },
            "event-predicate-of-an-unknown-type" => new JsonObject { ["event"] = new JsonObject { ["kind"] = "record", ["predicate"] = new JsonObject { ["type"] = "telegram" } } },
            "fixed-false" => new JsonObject { ["fixed"] = false },
            "output-without-its-field" => new JsonObject { ["at"] = new JsonObject { ["output"] = "prepare.marker" } },
            "deadline-on-a-watch" => new JsonObject { ["watch"] = new JsonObject { ["every"] = "PT1H", ["limit"] = "P1D" }, ["deadline"] = "PT1H" },
            "instant-false" => new JsonObject { ["instant"] = false },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        JsonObject flow = Timed(when, prepareOutputs: new JsonArray(Marker()));
        flow["steps"]!.AsArray().Add(FlowFixture.Step("publish", "operation", "decider", reserved: "publish", done: new JsonArray(FlowFixture.Marker("PUBLISHED")), action: FlowFixture.PublishCommand));
        IReadOnlyList<Finding> findings = TemplateDefinition.Validate(FlowFixture.Element(flow));
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Code == code);
    }

    [Fact]
    public void EveryDueTimeIsShownBeforeTheStepRunsInGoalShowAndStatus()
    {
        using var run = new FlowFixture(Timed(Delay("PT2H")));
        Prepare(run, Friday);
        EstateModel model = run.Estate.Load();
        FlowPosition.Position position = FlowPosition.Derive(model, run.Goal, Friday);
        Assert.Contains("       due: 2026-10-16T18:30:00Z (PT2H after prepare's close at 2026-10-16T16:30:00Z); timing: PT2H after the close of the steps it waits on, in UTC\n",
            FlowPosition.RenderText(model, position), StringComparison.Ordinal);
        JsonObject step = FlowPosition.RenderJson(model, position)["steps"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "timed")!.AsObject();
        Assert.Equal(("2026-10-16T18:30:00Z", "UTC", null), (step["due"]!["at"]!.GetValue<string>(), step["due"]!["zone"]!.GetValue<string>(), step["due"]!["deadline"]?.GetValue<string>()));
        Assert.Null(FlowPosition.RenderJson(model, position)["steps"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "prepare")!["due"]);

        // Status gives each run's due times, as of the records' own time, before each step runs, and none once it is done.
        IReadOnlyList<FlowPosition.Position> runs = FlowStops.Runs(model, Friday);
        Assert.Contains("    Due: timed 2026-10-16T18:30:00Z (PT2H after prepare's close", FlowStops.RunsText(model, runs), StringComparison.Ordinal);
        JsonObject shown = FlowStops.RunsJson(model, runs)[0]!.AsObject();
        Assert.Equal(("timed", "2026-10-16T18:30:00Z"), (shown["due"]![0]!["name"]!.GetValue<string>(), shown["due"]![0]!["at"]!.GetValue<string>()));
        run.Estate.SetClock(Friday.AddHours(3));
        run.Marked("TIMED");
        Assert.Null(FlowStops.RunsJson(run.Estate.Load(), FlowStops.Runs(run.Estate.Load(), Friday.AddHours(4)))[0]!["due"]);
    }

    private static JsonObject Timed(JsonObject? when, bool optional = false, bool reserved = false, JsonArray? prepareOutputs = null, JsonArray? prepareDone = null) =>
        FlowFixture.Timed(when, optional, reserved, prepareOutputs, prepareDone);

    /// <summary>A delay after a point, the close of what the step waits on when none is named.</summary>
    public static JsonObject Delay(string after, string? from = null)
    {
        var delay = new JsonObject { ["after"] = after };
        if (from is not null)
        {
            delay["from"] = from;
        }

        return new JsonObject { ["delay"] = delay };
    }

    /// <summary>The prepare step's record item: the work order its marker is.</summary>
    private static JsonObject Marker() => new() { ["name"] = "marker", ["kind"] = "record", ["recordType"] = "work-order", ["required"] = true };

    /// <summary>The prepare step done at a time: its marker recorded then.</summary>
    private static RecordEnvelope Prepare(FlowFixture run, DateTimeOffset at)
    {
        run.Estate.SetClock(at.AddSeconds(-1));
        return run.Marked("PREPARED");
    }

    private static FlowPosition.StepInstance Timed(FlowFixture run, DateTimeOffset at) =>
        Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, at).Steps, s => s.Step.Key == "timed");

    private static DateTimeOffset Instant(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>The sponsor's answer to an ask about the record a missed step's due time counts from.</summary>
    private static void Answer(FlowFixture run, RecordEnvelope trigger, DateTimeOffset at)
    {
        run.Estate.SetClock(at);
        RecordEnvelope ask = run.Add("ask", new JsonObject
        {
            ["to"] = run.Estate.OperatorId, ["question"] = "The step missed its deadline. How does the run go on?", ["recommendation"] = "Let it run once, late.",
            ["consequence"] = "It waits.", ["goal"] = run.Goal.Id, ["state"] = "open",
        }, builder: true, subject: trigger.Reference);
        JsonObject answer = run.Policies.DecisionBody();
        answer["answers"] = new JsonArray(ask.Id);
        run.Add("decision", answer);
    }

    private static RecordEnvelope Defaults(FlowFixture run, JsonObject options) => run.Add("template-defaults", new JsonObject
    {
        ["template"] = FlowFixture.Pin(run.Flow), ["scope"] = new JsonObject { ["kind"] = "estate" }, ["options"] = options, ["reason"] = "The venture's timing.",
    });

    private static JsonObject MarkerBody(string key) => new()
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Marker " + key, ["intent"] = "Stand in for an act's record.", ["scope"] = new JsonArray("marker"),
        ["exitQuestion"] = "Recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("marker"),
    };

    private static void AssertNoErrors(FlowFixture run) =>
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));

    private static List<Finding> Errors(EstateModel model, RecordEnvelope record) =>
        model.Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToList();

    private static void Checkpoint(FlowFixture run, string trigger)
    {
        EstateModel model = run.Estate.Load();
        History.CheckpointDraft draft = History.SignedHistory.Prepare(model, trigger, trigger == "adoption" ? model.Records.Where(r => r.Signatures.Count == 0) : null);
        foreach (byte[] bytes in draft.Objects)
        {
            string path = Path.Combine(run.Estate.Root, EstateLayout.ObjectsDirectory, Hashing.ContentHash.Sha256(bytes)["sha256:".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        run.Estate.Add("history-checkpoint", run.Estate.OperatorId, draft.Body.GetRawText(), signer: run.Policies.OperatorSigner);
    }
}
