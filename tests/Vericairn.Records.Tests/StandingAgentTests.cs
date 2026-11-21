// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Standing agents at the record (from 0.9.4, GOAL-07; the estate's design/standing-agents.md §§ 5, 6, 10 and 12): golden trigger
/// evaluations at explicit times, keys stable across revisions, occurrence states from their assignments, the gates over what is due,
/// the plan's validation, and the duplicate refusal every caller meets because it is the record's own.
/// </summary>
public class StandingAgentTests
{
    private static DateTimeOffset At(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>What validating a record not yet written finds, against the estate as it stands, with the record among it.</summary>
    private static FindingList Validate(StandingFixture fixture, string type, JsonObject body, RecordReference? subject = null, string? supersedes = null, bool builder = false)
    {
        fixture.Estate.SetClock(fixture.Estate.Clock.AddSeconds(1));
        string by = builder ? fixture.Estate.BuilderId : fixture.Estate.OperatorId;
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, Element(body), subject, supersedes, fixture.Estate.Clock, null, 3));
        var findings = new FindingList();
        RecordTypeRegistry.Find(type)!.Validate(new ValidationContext(fixture.Estate.Load(), record.Envelope, findings));
        return findings;
    }

    [Fact]
    public void ScheduleTicksFollowTheCalendarFromTheirAnchorAndMissedTicksCoalesce()
    {
        JsonElement monthEnd = Element(StandingFixture.Monthly(firstAt: "2027-01-31T09:30:00Z"));
        Assert.Equal(At("2027-02-28T09:30:00Z"), StandingAgents.Tick(monthEnd, 1));
        Assert.Equal(At("2027-03-31T09:30:00Z"), StandingAgents.Tick(monthEnd, 2));
        Assert.Equal(At("2028-02-29T09:30:00Z"), StandingAgents.Tick(monthEnd, 13));
        JsonElement weekly = Element(new JsonObject { ["key"] = "weekly", ["kind"] = "schedule", ["unit"] = "days", ["every"] = 7, ["firstAt"] = "2026-10-01T00:00:00Z" });
        Assert.Equal(At("2026-10-15T00:00:00Z"), StandingAgents.Tick(weekly, 2));
        Assert.Equal(-1, StandingAgents.LatestTick(weekly, At("2026-09-30T23:59:59Z")));
        Assert.Equal(0, StandingAgents.LatestTick(weekly, At("2026-10-01T00:00:00Z")));
        Assert.Equal(2, StandingAgents.LatestTick(weekly, At("2026-10-21T23:59:59Z")));
        Assert.Equal(3, StandingAgents.LatestTick(weekly, At("2026-10-22T00:00:00Z")));

        // Periods are UTC-anchored: calendar months, or days counted from 1970-01-01.
        Assert.Equal((At("2026-12-01T00:00:00Z"), At("2027-01-01T00:00:00Z")), StandingAgents.PeriodAt("months", 1, At("2026-12-15T10:00:00Z")));
        Assert.Equal((At("2027-01-01T00:00:00Z"), At("2027-04-01T00:00:00Z")), StandingAgents.PeriodAt("months", 3, At("2027-02-28T23:00:00Z")));
        Assert.Equal((At("2026-12-10T00:00:00Z"), At("2026-12-17T00:00:00Z")), StandingAgents.PeriodAt("days", 7, At("2026-12-15T10:00:00Z")));

        // Three ticks passed unclaimed: they coalesce into the latest, the only one due, and the next is shown.
        using var fixture = new StandingFixture();
        StandingAgents.Agent agent = fixture.Agent(At("2026-12-15T00:00:00Z"));
        StandingAgents.OccurrenceState due = Assert.Single(agent.Occurrences);
        Assert.Equal(("due", "schedule", "monthly"), (due.State, due.Occurrence.Kind, due.Occurrence.Trigger));
        Assert.Equal($"{fixture.Goal.Id}/monthly/{fixture.Goal.Id}/interval-2", due.Occurrence.Key);
        Assert.Equal(At("2026-12-01T00:00:00Z"), due.Occurrence.DueAt);
        Assert.Equal(At("2027-01-01T00:00:00Z"), agent.Next["monthly"]);
        Assert.Equal("active", agent.State);
        Assert.Contains("interval 2 of schedule monthly", StandingView.RenderText(fixture.Estate.Load(), agent), StringComparison.Ordinal);
        Assert.Equal(StandingView.DueFormat, StandingView.Json(fixture.Estate.Load(), agent)["format"]!.GetValue<string>());
    }

    [Fact]
    public void AnOccurrenceIsCarriedOnceAndItsKeyHoldsOneAssignmentWhoeverRecordsIt()
    {
        using var fixture = new StandingFixture();
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        Assert.Equal("due", Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).State);

        // A second assignment of the key is refused whoever records it, the operator included.
        FindingList duplicate = Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference);
        Assert.Contains(duplicate, f => f.Code == "standing.duplicate" && f.Message.Contains(assignment.Id, StringComparison.Ordinal));

        // The occurrence settles with its result, and runs again only when a review returns it, on the assignment that holds it.
        RecordEnvelope result = fixture.Result(assignment);
        StandingAgents.OccurrenceState settled = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Assert.Equal(("settled", assignment.Id, result.Id), (settled.State, settled.Assignment!.Id, settled.Result!.Id));
        fixture.Review(result, "findings");
        StandingAgents.OccurrenceState returned = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Assert.Equal("due", returned.State);
        Assert.Contains($"assignment {assignment.Id} goes on with another attempt", returned.Reason, StringComparison.Ordinal);
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "standing.duplicate");
        RecordEnvelope again = fixture.Result(assignment);
        fixture.Review(again, "pass");
        Assert.Equal("disposed", Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).State);

        // A person takes the occurrence over under the same key, which is no duplicate.
        Assert.DoesNotContain(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.OperatorId), fixture.Work.Reference), f => f.Severity == Severity.Error);
    }

    [Fact]
    public void AnOccurrenceNamesTheKeyItsGoalTriggerRevisionAndItemMakeAndIsNeverCarriedBeforeItIsDue()
    {
        using var fixture = new StandingFixture();
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId, key: occurrence.Key + "x"), fixture.Work.Reference),
            f => f.Code == "assignment.occurrence" && f.Message.Contains("is not the one its goal, trigger, revision and item make", StringComparison.Ordinal));

        // Interval 3 falls due in January 2027: in October 2026 it is not carried.
        StandingAgents.Trigger trigger = Assert.Single(StandingAgents.Triggers(fixture.Estate.Load(), fixture.Goal));
        JsonElement later = Element(new JsonObject { ["interval"] = 3, ["dueAt"] = "2027-01-01T00:00:00Z" });
        var early = new StandingAgents.Occurrence(StandingAgents.Key(fixture.Goal.Id, trigger, "interval-3"), "monthly", "schedule", trigger.Revision, later, At("2027-01-01T00:00:00Z"), [], "early");
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(early, fixture.Estate.BuilderId), fixture.Work.Reference),
            f => f.Code == "assignment.occurrence" && f.Message.Contains("an occurrence is never carried before it is due", StringComparison.Ordinal));

        // Every assignment of the standing work order names an occurrence, and only those do.
        JsonObject bare = StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId);
        bare.Remove("occurrence");
        Assert.Contains(Validate(fixture, "assignment", bare, fixture.Work.Reference), f => f.Code == "assignment.occurrence" && f.Message.Contains("names the occurrence it carries", StringComparison.Ordinal));
        RecordEnvelope other = fixture.Add("work-order", new JsonObject
        {
            ["key"] = "OTHER-1", ["kind"] = "ordinary", ["title"] = "Other", ["intent"] = "Other work.", ["scope"] = new JsonArray("path:other/"), ["exitQuestion"] = "Done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("other"),
        });
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), other.Reference), f => f.Code == "assignment.occurrence" && f.Message.Contains("is the work of none", StringComparison.Ordinal));

        // A trigger the goal does not declare is refused.
        var unknown = new StandingAgents.Occurrence(occurrence.Key.Replace("/monthly/", "/weekly/", StringComparison.Ordinal), "weekly", "schedule", occurrence.Revision, occurrence.Item, occurrence.DueAt, [], "unknown");
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(unknown, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "assignment.occurrence" && f.Message.Contains("declares no trigger 'weekly'", StringComparison.Ordinal));
    }

    [Fact]
    public void EventTriggersCountOnlyRecordsAfterActivationOfTheirFiltersAndNeverTheAgentsOwn()
    {
        JsonObject bug = new() { ["key"] = "bugs", ["kind"] = "event", ["recordType"] = "work-order", ["fields"] = new JsonObject { ["title"] = "A bug" } };
        using var fixture = new StandingFixture(triggers: new JsonArray(bug), activate: false);
        RecordEnvelope Order(string key, string title, bool builder = false) => fixture.Add("work-order", new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = title, ["intent"] = "Fix it.", ["scope"] = new JsonArray("path:fix/"), ["exitQuestion"] = "Fixed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("fix"),
        }, builder: builder);
        Order("BUG-0", "A bug");
        fixture.Activate(fixture.Goal);
        RecordEnvelope counted = Order("BUG-1", "A bug");
        Order("FEAT-1", "A feature");
        Order("BUG-2", "A bug", builder: true);
        StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
        StandingAgents.OccurrenceState due = Assert.Single(agent.Occurrences);
        Assert.Equal($"{fixture.Goal.Id}/bugs/{fixture.Goal.Id}/record-{counted.Id}@{counted.ComputedHash}", due.Occurrence.Key);
        Assert.Equal(counted.Id, Assert.Single(due.Occurrence.Records).Id);
        Assert.Null(agent.Next["bugs"]);

        // The event's record must be one of the trigger's: an assignment naming another record is refused.
        RecordEnvelope feature = fixture.Estate.Load().Current("work-order").Single(o => EstateModel.BodyString(o, "key") == "FEAT-1");
        JsonElement item = Element(new JsonObject { ["record"] = new JsonObject { ["type"] = "work-order", ["id"] = feature.Id, ["hash"] = feature.ComputedHash } });
        StandingAgents.Trigger trigger = Assert.Single(agent.Triggers);
        var wrong = new StandingAgents.Occurrence(StandingAgents.Key(fixture.Goal.Id, trigger, StandingAgents.ItemKey("event", item)!), "bugs", "event", trigger.Revision, item, feature.AtTime, [], "wrong");
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(wrong, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "assignment.occurrence" && f.Message.Contains("trigger bugs's filters", StringComparison.Ordinal));
        fixture.Assign(due.Occurrence);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }

    [Fact]
    public void AConditionMakesOneOccurrenceForEachNewSetOfFailingRecords()
    {
        JsonObject open = new() { ["key"] = "no-open-bugs", ["kind"] = "condition", ["record"] = new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["title"] = "Open bug" }, ["max"] = 0 } };
        using var fixture = new StandingFixture(triggers: new JsonArray(open));
        Assert.Empty(fixture.Agent(fixture.Estate.Clock).Occurrences);
        RecordEnvelope Bug(string key) => fixture.Add("work-order", new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = "Open bug", ["intent"] = "Fix it.", ["scope"] = new JsonArray("path:fix/"), ["exitQuestion"] = "Fixed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("fix"),
        });
        Bug("BUG-1");
        StandingAgents.OccurrenceState first = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Assert.StartsWith($"{fixture.Goal.Id}/no-open-bugs/{fixture.Goal.Id}/failing-sha256:", first.Occurrence.Key, StringComparison.Ordinal);
        fixture.Assign(first.Occurrence);
        // The same failing set makes no new occurrence; a new one does.
        Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Bug("BUG-2");
        StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(2, agent.Occurrences.Count);
        Assert.Contains(agent.Occurrences, o => o.Occurrence.Key != first.Occurrence.Key && o.State == "due");
        Assert.Contains(agent.Occurrences, o => o.Occurrence.Key == first.Occurrence.Key && o.Assignment is not null);
    }

    [Fact]
    public void ADutyTriggerCarriesTheDutyOccurrencesOwnIdentitySoTwoAgentsNeverBothCarryIt()
    {
        using var fixture = new StandingFixture(activate: false);
        RecordEnvelope policy = fixture.Policies.Policy();
        JsonObject duty = new() { ["key"] = "maintenance", ["kind"] = "duty", ["policy"] = policy.Id, ["rule"] = "dependencies" };
        RecordEnvelope goal = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(duty)), supersedes: fixture.Goal.Id);
        fixture.Activate(goal);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        var owed = new StandingAgents.DutyDue($"{policy.Id}:{fixture.Estate.Load().FirstEstate!.Id}:{{\"kind\":\"interval\",\"number\":0}}", policy.Id, "dependencies",
            At("2026-10-13T13:00:00Z"), At("2026-10-20T13:00:00Z"), policy.Reference, Element(new JsonObject { ["kind"] = "interval", ["number"] = 0 }));
        StandingAgents.Agent agent = StandingAgents.Derive(fixture.Estate.Load(), goal, At("2026-10-14T00:00:00Z"), [owed]);
        StandingAgents.OccurrenceState due = Assert.Single(agent.Occurrences);
        Assert.Equal("duty/" + owed.Identity, due.Occurrence.Key);
        Assert.Equal("due", due.State);
        Assert.Empty(StandingAgents.Derive(fixture.Estate.Load(), goal, At("2026-10-13T12:59:59Z"), [owed]).Occurrences);
    }

    [Fact]
    public void TheAgentsGatesDeferWhatIsDueWhenItIsPausedOrLapsedOrItsFailuresPauseIt()
    {
        using var fixture = new StandingFixture(until: "2026-12-01T00:00:00Z");
        StandingAgents.Agent lapsed = fixture.Agent(At("2026-12-01T00:00:00Z"));
        Assert.Equal("lapsed", lapsed.State);
        Assert.Equal("deferred", Assert.Single(lapsed.Occurrences).State);
        Assert.Contains("its until time 2026-12-01T00:00:00Z has passed", Assert.Single(lapsed.Occurrences).Reason, StringComparison.Ordinal);
        RecordEnvelope later = RecordAuthor.Create(new RecordDraft("goal-status", fixture.Estate.OperatorId, Element(new JsonObject { ["status"] = "paused", ["note"] = "A time after until." }), fixture.Goal.Reference, null, At("2026-12-02T00:00:00Z"), null, 3)).Envelope;
        Assert.Contains(StandingAgents.RunRefusals(fixture.Estate.Load(), fixture.Goal, null, later), r => r.Message.Contains("until time", StringComparison.Ordinal));

        // The failure pause itself runs in a run's real order: see AFailurePauseCountsEachRunsResultInTheOrderRunsHappen.
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        fixture.Assign(occurrence);

        // Paused, nothing is dispatched and what is due waits.
        fixture.Add("goal-status", new JsonObject { ["status"] = "paused", ["note"] = "Paused for the test." }, subject: fixture.Goal.Reference);
        StandingAgents.Agent paused = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal("paused", paused.State);
        Assert.All(paused.Occurrences.Where(o => o.Assignment is null || o.State is "due" or "deferred"), o => Assert.Equal("deferred", o.State));
    }

    [Fact]
    public void AStandingPlanIsAStewardshipGoalsWithOneWorkOrderAFlowThatDeclaresItsTriggersAndCapsOverEveryCharge()
    {
        using var fixture = new StandingFixture(activate: false);
        FindingList Plan(Action<JsonObject> change)
        {
            JsonObject body = fixture.GoalBody();
            change(body);
            return Validate(fixture, "goal", body);
        }

        Assert.DoesNotContain(Plan(_ => { }), f => f.Severity == Severity.Error);
        Assert.Contains(Plan(b => b["kind"] = "finite"), f => f.Code == "standing.kind");
        Assert.Contains(Plan(b => b["plan"]!["flow"] = new JsonObject { ["pin"] = FlowFixture.Pin(fixture.Flow), ["subject"] = new JsonObject() }), f => f.Code == "standing.flow");
        Assert.Contains(Plan(b => b["plan"]!["workOrders"] = new JsonArray()), f => f.Code == "standing.work");
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["triggers"] = new JsonArray(StandingFixture.Monthly(), StandingFixture.Monthly())), f => f.Code == "standing.trigger" && f.Message.Contains("its own key", StringComparison.Ordinal));
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["triggers"] = new JsonArray(new JsonObject { ["key"] = "x", ["kind"] = "schedule", ["unit"] = "weeks", ["every"] = 1, ["firstAt"] = "2026-10-01T00:00:00Z" })), f => f.Location.EndsWith(".unit", StringComparison.Ordinal));
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["period"] = new JsonObject { ["unit"] = "months", ["runs"] = 4 }), f => f.Location.EndsWith("period.wallMinutes", StringComparison.Ordinal));
        Assert.Contains(Plan(b => b["budget"]!["tokens"] = 1000), f => f.Code == "standing.period" && f.Message.Contains("tokens is in the budget and not the period", StringComparison.Ordinal));
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["until"] = "2026-01-01T00:00:00Z"), f => f.Code == "standing.until");
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["subject"] = new JsonObject { ["topic"] = "x", ["extra"] = "y" }), f => f.Code == "standing.subject");

        // A flow that does not declare the trigger kind, and one with two routed steps, are refused.
        JsonObject quiet = StandingFixture.Researcher();
        quiet["id"] = "example/quiet";
        quiet["triggers"] = new JsonArray(new JsonObject { ["kind"] = "schedule", ["summary"] = "On a calendar." });
        Templates.TemplateDefinition installed = FlowFixture.Install(fixture.Estate, quiet);
        Assert.Contains(Plan(b =>
        {
            b["plan"]!["standing"]!["pin"] = FlowFixture.Pin(installed);
            b["plan"]!["standing"]!["triggers"] = new JsonArray(new JsonObject { ["key"] = "asked", ["kind"] = "request" });
        }), f => f.Code == "standing.trigger" && f.Message.Contains("declares no request trigger", StringComparison.Ordinal));
        JsonObject two = StandingFixture.Researcher();
        two["id"] = "example/two";
        two["steps"]!.AsArray().Add(FlowFixture.Step("summarize", "work", "researcher"));
        Templates.TemplateDefinition twoSteps = FlowFixture.Install(fixture.Estate, two);
        Assert.Contains(Plan(b => b["plan"]!["standing"]!["pin"] = FlowFixture.Pin(twoSteps)), f => f.Code == "standing.flow" && f.Message.Contains("exactly one routed step", StringComparison.Ordinal));

        // A flow's trigger kinds are the standing agents' own.
        JsonObject odd = StandingFixture.Researcher();
        odd["id"] = "example/odd";
        odd["triggers"] = new JsonArray(new JsonObject { ["kind"] = "tide", ["summary"] = "At high tide." });
        Assert.Contains(Templates.TemplateDefinition.Validate(Element(odd)), f => f.Severity == Severity.Error && f.Location.Contains("triggers", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivationRefusesASecondAgentOfTheRoleOverTheSubjectAndAnAgentPastItsUntilTime()
    {
        using var fixture = new StandingFixture();
        RecordEnvelope second = fixture.Add("goal", fixture.GoalBody());
        Assert.Contains(Validate(fixture, "goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, second.Reference), f => f.Code == "goal-status.standing" && f.Message.Contains(fixture.Goal.Id, StringComparison.Ordinal));
        RecordEnvelope elsewhere = fixture.Add("goal", fixture.GoalBody(subject: "the gadget"));
        Assert.DoesNotContain(Validate(fixture, "goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, elsewhere.Reference), f => f.Severity == Severity.Error);
        RecordEnvelope brief = fixture.Add("goal", fixture.GoalBody(subject: "the sprocket", until: "2026-10-13T12:01:00Z"));
        fixture.Estate.SetClock(At("2026-10-13T12:05:00Z"));
        Assert.Contains(Validate(fixture, "goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, brief.Reference), f => f.Code == "goal-status.standing" && f.Message.Contains("has passed", StringComparison.Ordinal));
    }

    [Fact]
    public void ARenewalKeepsAnUnchangedTriggersKeysAndAChangedTriggerStartsItsOwn()
    {
        using var fixture = new StandingFixture();
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        fixture.Assign(occurrence);
        RecordEnvelope renewed = fixture.Add("goal", fixture.GoalBody(until: "2027-09-01T00:00:00Z"), supersedes: fixture.Goal.Id);
        fixture.Activate(renewed);
        Assert.Equal(fixture.Goal.Id, Assert.Single(StandingAgents.Triggers(fixture.Estate.Load(), renewed)).Revision);
        StandingAgents.OccurrenceState kept = Assert.Single(StandingAgents.Derive(fixture.Estate.Load(), renewed, fixture.Estate.Clock).Occurrences);
        Assert.Equal((occurrence.Key, "due"), (kept.Occurrence.Key, kept.State));
        Assert.NotNull(kept.Assignment);
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "standing.duplicate");

        RecordEnvelope changed = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(StandingFixture.Monthly(firstAt: "2026-10-02T00:00:00Z")), until: "2027-09-01T00:00:00Z"), supersedes: renewed.Id);
        fixture.Activate(changed);
        StandingAgents.Trigger trigger = Assert.Single(StandingAgents.Triggers(fixture.Estate.Load(), changed));
        Assert.Equal(changed.Id, trigger.Revision);
        Assert.Contains(StandingAgents.Derive(fixture.Estate.Load(), changed, fixture.Estate.Clock).Occurrences, o => o.Occurrence.Key == $"{fixture.Goal.Id}/monthly/{changed.Id}/interval-0" && o.State == "due");
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }

    private static readonly JsonObject Roomy = new() { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 };

    /// <summary>The sponsor's answer to the failure pause's ask about a record.</summary>
    private static void AnswerPause(StandingFixture fixture, RecordEnvelope about)
    {
        RecordEnvelope ask = fixture.Add("ask", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = "Required attention in goal Research the widget: its runs failed. How does the run go on?", ["recommendation"] = "Let it go on.",
            ["consequence"] = "Nothing is dispatched.", ["goal"] = fixture.Goal.Id, ["state"] = "open",
        }, subject: about.Reference);
        JsonObject answer = fixture.Policies.DecisionBody();
        answer["answers"] = new JsonArray(ask.Id);
        fixture.Add("decision", answer);
    }

    [Fact]
    public void AFailurePauseCountsEachRunsResultInTheOrderRunsHappenAndNeverInvalidatesAnEarlierIntent()
    {
        using var fixture = new StandingFixture(budget: Roomy.DeepClone().AsObject(), period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 });
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);

        // Each run records a blocked or abandoned result and its harness then exits 0: the run settles as observed-success after its result,
        // and still counts as a failure. A known failure whose result is also blocked counts once.
        RecordEnvelope? last = null;
        foreach ((string outcome, string settled) in new[] { ("blocked", "observed-success"), ("abandoned", "observed-success"), ("blocked", "known-failure") })
        {
            RecordEnvelope intent = fixture.RunIntent(assignment, "run-" + outcome + "-" + settled);
            last = fixture.Result(assignment, outcome);
            fixture.Settle(intent, settled);
        }

        // The third blocked result leaves every earlier intent valid: each is judged by what was recorded before it.
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        StandingAgents.Agent held = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(("held", 3, last!.Id), (held.State, held.Failures, held.FailureTrigger!.Id));
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));

        // The sponsor's answer lets it go on: the next intent is accepted; a success resets the count.
        AnswerPause(fixture, last);
        Assert.Equal(("active", 0), (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures));
        Assert.DoesNotContain(Validate(fixture, "operation", fixture.RunBody(assignment, "run-after", "not-started")), f => f.Severity == Severity.Error);
    }

    [Fact]
    public void AnIntentIsJudgedOnlyByTheResultsRecordedBeforeItSoALaterRunsResultNeverCountsForAnEarlierRun()
    {
        using var fixture = new StandingFixture(budget: Roomy.DeepClone().AsObject(), period: new JsonObject { ["unit"] = "months", ["runs"] = 10, ["wallMinutes"] = 200 });
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);

        // Two failures, then a run its sponsor asked to stop, which settles with neither a result nor a failure of its own and counts for
        // nothing (from FIX-155 a run that settles as an observed success with no result is a success; see the failure pause's tests).
        foreach (string outcome in new[] { "blocked", "abandoned" })
        {
            RecordEnvelope intent = fixture.RunIntent(assignment, "run-" + outcome);
            fixture.Result(assignment, outcome);
            fixture.Settle(intent, "observed-success");
        }

        fixture.Settle(fixture.RunIntent(assignment, "run-silent"), "known-failure", fixture.Estate.OperatorId);
        Assert.Equal(("active", 2), (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures));

        // The fourth run's intent is accepted at two failures; its own blocked result, recorded after it, is the third failure. Judged again
        // as the records stood before it, the intent stays valid: that later result is the fourth run's, never the stopped run's before it.
        RecordEnvelope fourth = fixture.RunIntent(assignment, "run-fourth");
        RecordEnvelope blocked = fixture.Result(assignment, "blocked");
        fixture.Settle(fourth, "observed-success");
        EstateModel model = fixture.Estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        StandingAgents.Agent held = StandingAgents.Derive(model, fixture.Goal, fixture.Estate.Clock);
        Assert.Equal(("held", 3, blocked.Id), (held.State, held.Failures, held.FailureTrigger!.Id));
    }

    [Fact]
    public void ARunItsSponsorOrAnOperatorAskedToStopIsNoFailureWhileAStopByAnyoneElseOrTheWorkerIs()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 80, ["attemptsPerStep"] = 12 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 300 });
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);

        // Three runs stopped at the sponsor's request (goal pause --stop-runs, goal stop) settle known-failure, and none counts.
        for (int i = 1; i <= 3; i++)
        {
            fixture.Settle(fixture.RunIntent(assignment, "run-asked-" + i.ToString(CultureInfo.InvariantCulture)), "known-failure", fixture.Estate.OperatorId);
        }

        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
        Assert.Equal(("active", 0), (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures));
        Assert.DoesNotContain(Validate(fixture, "operation", fixture.RunBody(assignment, "run-goes-on", "not-started")), f => f.Severity == Severity.Error);

        // Three stopped by a principal that neither sponsors the goal nor holds the operator role, as a dispatcher stopping its own failing
        // runs would, are the agent's failures: the pause holds. The sponsor's answer lets it go on.
        RecordEnvelope? last = null;
        for (int i = 1; i <= 3; i++)
        {
            last = fixture.Settle(fixture.RunIntent(assignment, "run-dispatcher-" + i.ToString(CultureInfo.InvariantCulture)), "known-failure", fixture.Estate.BuilderId);
        }

        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
        StandingAgents.Agent held = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(("held", 3, last!.Id), (held.State, held.Failures, held.FailureTrigger!.Id));
        AnswerPause(fixture, last);
        Assert.Equal(("active", 0), (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures));

        // Three the worker stopped at a cap the run reached are the agent's failures too.
        for (int i = 1; i <= 3; i++)
        {
            last = fixture.Settle(fixture.RunIntent(assignment, "run-capped-" + i.ToString(CultureInfo.InvariantCulture)), "known-failure", "worker");
        }

        held = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(("held", 3, last!.Id), (held.State, held.Failures, held.FailureTrigger!.Id));
    }

    [Fact]
    public void NoRecordOfTheCarrierEndsItsFailurePauseWhateverItsDateAndOnlyThePersonsAnswerDoes()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
        string builder = fixture.Estate.BuilderId;
        (string, int, string?) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).FailureTrigger?.Id);

        // The builder, which the roster names for the researcher role, carries the occurrence and holds local-engineering, so it may record
        // runs as well. Three runs the operator records fail, the third after five minutes in flight: the pause holds.
        fixture.Add("authority", new JsonObject { ["grantee"] = builder, ["scope"] = new JsonArray("local-engineering"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = fixture.Policies.Decision.Id });
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
        fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
        RecordEnvelope third = fixture.RunIntent(assignment, "run-3");
        fixture.Estate.SetClock(fixture.Estate.Clock.AddMinutes(5));
        RecordEnvelope last = fixture.Settle(third, "known-failure");
        Assert.Equal(("held", 3, last.Id), Pause());

        // The carrier records its own attempt and a completed result on the assignment, now: a claim another principal reviews, which ends
        // nothing, so the pause holds and the failures stay three.
        RecordEnvelope claim = fixture.Result(assignment, "completed");
        Assert.Equal(("held", 3, last.Id), Pause());

        // Nor does a run of its own, written by hand and dated while the third was in flight, that settles as an observed success with a
        // completed result in it, which also lies within the third run: a run a tied principal records never succeeds.
        DateTimeOffset now = fixture.Estate.Clock;
        fixture.Estate.SetClock(third.AtTime.AddSeconds(30));
        RecordEnvelope own = fixture.Add("operation", fixture.RunBody(assignment, "run-own", "not-started"), builder: true);
        fixture.Result(assignment, "completed");
        fixture.Settle(own, "observed-success", builder: true);
        fixture.Estate.SetClock(now);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        Assert.Equal(("held", 3, last.Id), Pause());
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));

        // The person's answer to the pause's ask ends it; the operator's review returns the carrier's result, so the occurrence runs again.
        AnswerPause(fixture, last);
        Assert.Equal(("active", 0, null), Pause());
        fixture.Review(claim, "findings");

        // Two more failures, then a run the carrier records whose known-failure settlement says the operator asked it to stop: a stop a
        // principal tied to the goal records is no stop of who decides for the goal, so the failures are three and the pause holds again.
        fixture.Settle(fixture.RunIntent(assignment, "run-4"), "known-failure");
        fixture.Settle(fixture.RunIntent(assignment, "run-5"), "known-failure");
        RecordEnvelope claimed = fixture.Settle(fixture.Add("operation", fixture.RunBody(assignment, "run-claimed", "not-started"), builder: true), "known-failure", fixture.Estate.OperatorId, builder: true);
        Assert.Equal(("held", 3, claimed.Id), Pause());
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }

    [Fact]
    public void ACleanExitWithNoResultEndsNoFailuresARunNamingItsResultEndsThemBelowTheLimitAndAtTheLimitOnlyTheAnswerDoes()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 40, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 40, ["wallMinutes"] = 800 });
        (string, int, string?) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).FailureTrigger?.Id);
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);

        // Four rounds of two failed runs and a run that exits cleanly with no result, as the Reviewer probed: a clean exit leaves nothing to
        // review and ends no failures, so it never lowers the count, and every third failure since the person last answered pauses the agent
        // until the person's answer.
        int expected = 0;
        RecordEnvelope? clean = null;
        for (int round = 1; round <= 4; round++)
        {
            foreach (string name in new[] { "first", "second" })
            {
                RecordEnvelope failed = fixture.Settle(fixture.RunIntent(assignment, $"run-{round}-{name}"), "known-failure");
                if (++expected == 3)
                {
                    Assert.Equal(("held", 3, failed.Id), Pause());
                    Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, $"run-{round}-{name}-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
                    AnswerPause(fixture, failed);
                    expected = 0;
                }

                Assert.Equal(("active", expected), (Pause().Item1, Pause().Item2));
            }

            clean = fixture.RunIntent(assignment, $"run-{round}-clean");
            fixture.Estate.SetClock(fixture.Estate.Clock.AddMinutes(1));
            fixture.Settle(clean, "observed-success");
            Assert.Equal(("active", expected), (Pause().Item1, Pause().Item2));
        }

        Assert.Equal(2, expected);

        // The carrier writes by hand a completed result dated within the last clean run: that run's settlement names no result, so it still
        // counts for nothing and the count stays two. The operator's review returns the result, so the occurrence runs again.
        DateTimeOffset now = fixture.Estate.Clock;
        fixture.Estate.SetClock(clean!.AtTime.AddSeconds(10));
        RecordEnvelope backdated = fixture.Result(assignment, "completed");
        fixture.Estate.SetClock(now);
        Assert.Equal(("active", 2), (Pause().Item1, Pause().Item2));
        fixture.Review(backdated, "findings");

        // A run that settles as an observed success naming the completed result its agent recorded during it ends the failures below the limit.
        RecordEnvelope succeeding = fixture.RunIntent(assignment, "run-succeeds");
        RecordEnvelope done = fixture.Result(assignment, "completed");
        fixture.Settle(succeeding, "observed-success", result: done);
        Assert.Equal(("active", 0, null), Pause());
        fixture.Review(done, "findings");

        // A completed result in a run that settles as a known failure is the carrier's claim, and the run failed; so did one whose blocked
        // result the carrier took back by a completed correction after the run settled naming no result. The operator's review returns each
        // claim, so the occurrence runs again.
        RecordEnvelope claimed = fixture.RunIntent(assignment, "run-claimed");
        RecordEnvelope claim = fixture.Result(assignment, "completed");
        RecordEnvelope failedClaim = fixture.Settle(claimed, "known-failure", result: claim);
        Assert.Equal(("active", 1, failedClaim.Id), Pause());
        fixture.Review(claim, "findings");
        RecordEnvelope corrected = fixture.RunIntent(assignment, "run-corrected");
        RecordEnvelope blocked = fixture.Result(assignment, "blocked");
        JsonObject correction = PolicyFixture.Body(blocked);
        correction["outcome"] = "completed";
        fixture.Settle(corrected, "observed-success");
        RecordEnvelope taken = fixture.Add("result", correction, builder: true, subject: blocked.Subject, supersedes: blocked.Id);
        Assert.Equal(("active", 2, blocked.Id), Pause());
        fixture.Review(taken, "findings");

        // A run starts and another beside it; the second fails, so three fail in a row and the pause holds. The first then settles as an
        // observed success naming its result: once the failures reach the limit a success ends nothing, and a run intent is refused until the
        // person's answer, which ends the pause.
        RecordEnvelope beside = fixture.RunIntent(assignment, "run-beside");
        RecordEnvelope third = fixture.Settle(fixture.RunIntent(assignment, "run-third"), "known-failure");
        Assert.Equal(("held", 3, third.Id), Pause());
        RecordEnvelope besideResult = fixture.Result(assignment, "completed");
        fixture.Settle(beside, "observed-success", result: besideResult);
        Assert.Equal(("held", 3, third.Id), Pause());
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
        AnswerPause(fixture, third);
        Assert.Equal(("active", 0, null), Pause());
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }

    [Fact]
    public void TheCarriersRevisionOfASettledFailureTakesBackNoFailureAndTheOperatorsCorrectionCountsAsWritten()
    {
        foreach (string status in new[] { "canceled", "uncertain", "observed-success" })
        {
            using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
            (string, int, string?) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures, fixture.Agent(fixture.Estate.Clock).FailureTrigger?.Id);
            StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
            RecordEnvelope assignment = fixture.Assign(occurrence);
            JsonObject Revised(RecordEnvelope settled, string to, RecordEnvelope? result = null)
            {
                JsonObject body = PolicyFixture.Body(settled);
                body["status"] = to;
                if (result is not null) body["observed"]!["reference"] = result.Id;
                return body;
            }

            // Below the limit: two runs the operator records fail, and the carrier, which the roster names and which carried them, writes by
            // hand a revision of the second settlement: it takes back no failure.
            fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
            RecordEnvelope second = fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
            RecordEnvelope below = fixture.Add("operation", Revised(second, status), builder: true, supersedes: second.Id);
            Assert.Equal(("active", 2, second.Id), Pause());

            // At the limit: the third fails and the pause holds; the carrier's revision of that settlement, a success naming a completed result
            // it records afterwards included, ends nothing: the pause holds at three and a run intent is refused.
            RecordEnvelope third = fixture.Settle(fixture.RunIntent(assignment, "run-3"), "known-failure");
            Assert.Equal(("held", 3, third.Id), Pause());
            RecordEnvelope? late = status == "observed-success" ? fixture.Result(assignment, "completed") : null;
            RecordEnvelope atLimit = fixture.Add("operation", Revised(third, status, late), builder: true, supersedes: third.Id);
            Assert.Equal(("held", 3, third.Id), Pause());
            Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
            List<Finding> errors = [.. fixture.Estate.Load().Findings.Where(f => f.Severity == Severity.Error)];
            Assert.Contains(errors, f => f.Location.Contains(below.Id, StringComparison.Ordinal));
            Assert.Contains(errors, f => f.Location.Contains(atLimit.Id, StringComparison.Ordinal));

            // The operator's own correction of the third settlement to canceled counts as written, as it did before: that run counts for nothing,
            // and two failures remain.
            fixture.Add("operation", Revised(atLimit, "canceled"), supersedes: atLimit.Id);
            Assert.Equal(("active", 2), (Pause().Item1, Pause().Item2));
        }
    }

    /// <summary>
    /// An identifier below another, so that a record carrying it sorts before that one among a record's successors: its time part a little
    /// earlier and its random part the same, so it meets none of the identifiers generated beside that one.
    /// </summary>
    public static string Below(string id)
    {
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        char[] chars = id.ToCharArray();
        int at = Array.FindLastIndex(chars, 9, c => c != '0');
        chars[at] = Alphabet[Alphabet.IndexOf(chars[at], StringComparison.Ordinal) - 1];
        return new string(chars);
    }

    /// <summary>A record the builder writes by hand superseding a run's record beside its real successor, dated as that record and sorting before its successor.</summary>
    private static RecordEnvelope Fork(StandingFixture fixture, RecordEnvelope superseded, RecordEnvelope successor, string status)
    {
        JsonObject body = PolicyFixture.Body(successor);
        body["status"] = status;
        DateTimeOffset now = fixture.Estate.Clock;
        fixture.Estate.SetClock(superseded.AtTime.AddSeconds(-1));
        RecordEnvelope fork = fixture.Estate.Add("operation", fixture.Estate.BuilderId, body.ToJsonString(), supersedes: superseded.Id, id: Below(successor.Id));
        fixture.Estate.SetClock(now);
        return fork;
    }

    [Fact]
    public void ADivergentRevisionOfAFailedRunHidesNoFailureWhateverItsIdentifierOrReconciliation()
    {
        foreach (string status in new[] { "canceled", "uncertain", "observed-success" })
        {
            using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 3 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
            (string, int) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures);
            StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
            RecordEnvelope assignment = fixture.Assign(occurrence);
            EstateModel Model() => fixture.Estate.Load();

            // Below the limit: the carrier writes by hand a record superseding the second run's dispatched revision beside its settlement,
            // sorting before it, so the run as read takes the carrier's branch: the failure stays counted.
            fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
            RecordEnvelope second = fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
            RecordEnvelope dispatched = Model().ById[second.Supersedes!];
            Fork(fixture, dispatched, second, status);
            Assert.NotEqual(second.Id, Model().SupersededBy[dispatched.Id]);
            Assert.Equal(("active", 2), Pause());

            // At the limit: a record superseding the third run's intent beside its dispatched revision, and then one superseding its
            // dispatched revision beside its settlement that the carrier's own reconciliation keeps, each leave the pause held at three.
            RecordEnvelope intent = fixture.RunIntent(assignment, "run-3");
            RecordEnvelope third = fixture.Settle(intent, "known-failure");
            Assert.Equal(("held", 3), Pause());
            RecordEnvelope running = Model().ById[third.Supersedes!];
            Fork(fixture, intent, running, status);
            Assert.Equal(("held", 3), Pause());
            RecordEnvelope kept = Fork(fixture, running, third, status);
            fixture.Add("reconciliation", new JsonObject { ["kept"] = kept.Id, ["withdrawn"] = new JsonArray(third.Id), ["note"] = "Keep the carrier's revision." }, builder: true, subject: running.Reference);
            Assert.Equal(("held", 3), Pause());
            Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheOperatorsCorrectionOfASelfSteppingAgentsFailureCountsAndACarriersFailureRecordLeavesAnUntiedSuccessCounted()
    {
        using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
        string builder = fixture.Estate.BuilderId;
        (string, int) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures);
        fixture.Add("authority", new JsonObject { ["grantee"] = builder, ["scope"] = new JsonArray("local-engineering"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = fixture.Policies.Decision.Id });
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);

        // The builder, which the roster names for the role and which carries the occurrence, records its own runs under local-engineering, as
        // an agent stepping its own goal does: their known failures count, and three hold the pause.
        RecordEnvelope? third = null;
        for (int i = 1; i <= 3; i++)
        {
            third = fixture.Settle(fixture.Add("operation", fixture.RunBody(assignment, "run-own-" + i, "not-started"), builder: true), "known-failure", builder: true);
        }

        Assert.Equal(("held", 3), Pause());

        // The operator's correction of the third settlement to canceled counts as written: that run counts for nothing, and two remain.
        JsonObject canceled = PolicyFixture.Body(third!);
        canceled["status"] = "canceled";
        fixture.Add("operation", canceled, supersedes: third!.Id);
        Assert.Equal(("active", 2), Pause());

        // A run the operator records and settles as a success naming the completed result its agent recorded in it ends the failures; the
        // carrier's known-failure record beside that settlement, sorting before it, and its revision of it, leave the success counted.
        RecordEnvelope intent = fixture.RunIntent(assignment, "run-succeeds");
        RecordEnvelope done = fixture.Result(assignment, "completed");
        RecordEnvelope success = fixture.Settle(intent, "observed-success", result: done);
        Assert.Equal(("active", 0), Pause());
        Fork(fixture, fixture.Estate.Load().ById[success.Supersedes!], success, "known-failure");
        Assert.Equal(("active", 0), Pause());
        JsonObject failed = PolicyFixture.Body(success);
        failed["status"] = "known-failure";
        fixture.Add("operation", failed, builder: true, supersedes: success.Id);
        Assert.Equal(("active", 0), Pause());
    }

    /// <summary>An earlier month than the fixture's records, so that a record dated in it sorts before them by path, and a later one.</summary>
    public static readonly DateTimeOffset EarlierMonth = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A later month than the fixture's records, so that a record dated in it sorts after them by path.</summary>
    public static readonly DateTimeOffset LaterMonth = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The builder's hand-written record carrying another record's identifier, with its own body, dated in the month given: its path sorts
    /// before that record's for an earlier month and after it for a later one.
    /// </summary>
    internal static RecordEnvelope Reusing(StandingFixture fixture, RecordEnvelope original, JsonObject body, DateTimeOffset at, string? supersedes)
    {
        DateTimeOffset now = fixture.Estate.Clock;
        fixture.Estate.SetClock(at);
        RecordEnvelope copy = fixture.Estate.Add(original.Type, fixture.Estate.BuilderId, body.ToJsonString(), subject: original.Subject, supersedes: supersedes, id: original.Id);
        fixture.Estate.SetClock(now);
        Assert.NotEqual(original.ExpectedPath, copy.ExpectedPath);
        return copy;
    }

    /// <summary>Removes a record's file, as the operator removes a copy.</summary>
    internal static void Remove(StandingFixture fixture, RecordEnvelope record) => File.Delete(Path.Combine(fixture.Estate.Root, record.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void ARecordReusingASettlementsIdentifierTakesBackNoFailureWhateverItsMonth()
    {
        // The Reviewer's probe of FIX-155: the carrier writes by hand a record carrying the identifier of a failed run's settlement, canceled
        // or an observed success naming a completed result, dated in an earlier month so that its path sorts first, or in a later one.
        // Neither copy is read, nor any record of the run: the run counts as failed, and the agent is held until the copy is removed.
        foreach (string status in new[] { "canceled", "observed-success" })
        {
            foreach (DateTimeOffset month in new[] { EarlierMonth, LaterMonth })
            {
                using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
                (string, int) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures);
                StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
                RecordEnvelope assignment = fixture.Assign(occurrence);
                RecordEnvelope? done = null;
                JsonObject Revised(RecordEnvelope settled)
                {
                    JsonObject body = PolicyFixture.Body(settled);
                    body["status"] = status;
                    if (done is not null) body["observed"]!["reference"] = done.Id;
                    return body;
                }

                // Below the limit: two runs the operator records fail; the copy of the second settlement leaves both counted, and holds the agent.
                fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
                RecordEnvelope second = fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
                done = status == "observed-success" ? fixture.Result(assignment, "completed") : null;
                RecordEnvelope below = Reusing(fixture, second, Revised(second), month, second.Supersedes);
                StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
                Assert.Equal(("held", 2), (agent.State, agent.Failures));
                Assert.Contains("record.duplicate-id", agent.Reason, StringComparison.Ordinal);
                Assert.All(agent.Occurrences, o => Assert.NotEqual("due", o.State));
                Remove(fixture, below);
                Assert.Equal(("active", 2), Pause());

                // At the limit: the third fails and the pause holds; the copy of its settlement leaves it held at three and a run intent refused.
                RecordEnvelope third = fixture.Settle(fixture.RunIntent(assignment, "run-3"), "known-failure");
                Assert.Equal(("held", 3), Pause());
                RecordEnvelope copy = Reusing(fixture, third, Revised(third), month, third.Supersedes);
                Assert.Equal(("held", 3), Pause());
                Assert.Contains("record.duplicate-id", fixture.Agent(fixture.Estate.Clock).Reason, StringComparison.Ordinal);
                Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
                EstateModel model = fixture.Estate.Load();
                Assert.Equal(new[] { copy.ExpectedPath, third.ExpectedPath }.Order(StringComparer.Ordinal), model.Findings.Where(f => f.Code == "record.duplicate-id").Select(f => f.Location).Order(StringComparer.Ordinal));
                Assert.False(model.ById.ContainsKey(third.Id));

                // Removing the copy reads the run again: three failures hold the pause.
                Remove(fixture, copy);
                StandingAgents.Agent restored = fixture.Agent(fixture.Estate.Clock);
                Assert.Equal(("held", 3), (restored.State, restored.Failures));
                Assert.Contains("failed in a row", restored.Reason, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ARecordReusingARunIntentsIdentifierTakesNoRunOutOfTheCount()
    {
        // A copy of a failed run's intent naming no goal, dated in an earlier month so that it is read first, or in a later one: the run's line
        // is not read, and it counts as one failed run of the goal its other records name.
        foreach (DateTimeOffset month in new[] { EarlierMonth, LaterMonth })
        {
            using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
            (string, int) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures);
            StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
            RecordEnvelope assignment = fixture.Assign(occurrence);
            fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
            fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
            RecordEnvelope intent = fixture.RunIntent(assignment, "run-3");
            fixture.Settle(intent, "known-failure");
            Assert.Equal(("held", 3), Pause());

            JsonObject body = PolicyFixture.Body(intent);
            body["target"]!.AsObject().Remove("goal");
            body["status"] = "canceled";
            RecordEnvelope copy = Reusing(fixture, intent, body, month, null);
            StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
            Assert.Equal(("held", 3), (agent.State, agent.Failures));
            Assert.Contains("record.duplicate-id", agent.Reason, StringComparison.Ordinal);
            Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-held", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("failed in a row", StringComparison.Ordinal));
            Remove(fixture, copy);
            Assert.Equal(("held", 3), Pause());
        }
    }

    /// <summary>A run's revision as goal step's worker writes it, by the operator or by the builder: its status, and once settled what it observed.</summary>
    private static JsonObject Revision(StandingFixture fixture, RecordEnvelope assignment, RecordEnvelope intent, string status, RecordEnvelope? result = null)
    {
        JsonObject body = fixture.RunBody(assignment, intent.Body.GetProperty("identity").GetString()!, status);
        body["dispatchedAt"] = intent.At;
        if (AgentRuns.Settled.Contains(status))
        {
            body["observed"] = new JsonObject { ["summary"] = "The run ended.", ["at"] = RecordParser.FormatTimestamp(fixture.Estate.Clock) };
            if (result is not null) body["observed"]!["reference"] = result.Id;
            body["usage"] = new JsonObject { ["wallMinutes"] = 5, ["unreported"] = new JsonArray() };
        }

        return body;
    }

    [Fact]
    public void AnUntiedRevisionOnABranchATiedPrincipalPaddedNeitherOutranksAnUntiedSettlementNorIsOutrankedWrongly()
    {
        // The depth rule of FIX-155's last review: the carrier pads a run's dispatched revision with ten revisions of its own, and the operator
        // writes by hand on the branches it made. Tied revisions place nothing; on one line the untied revision the others lead to decides;
        // across branches the untied heads' least favorable reading decides, a failure over nothing and nothing over a success.
        foreach (string shape in new[] { "unsettled-on-the-padding", "success-on-the-padding", "correction-over-the-padding", "correction-on-a-fork" })
        {
            using var fixture = new StandingFixture(budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 12, ["concurrency"] = 2 }, period: new JsonObject { ["unit"] = "months", ["runs"] = 20, ["wallMinutes"] = 400 });
            (string, int) Pause() => (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures);
            StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
            RecordEnvelope assignment = fixture.Assign(occurrence);
            fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
            fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
            Assert.Equal(("active", 2), Pause());

            RecordEnvelope intent = fixture.RunIntent(assignment, "run-3");
            RecordEnvelope? done = shape == "success-on-the-padding" ? fixture.Result(assignment, "completed") : null;
            RecordEnvelope dispatched = fixture.Add("operation", Revision(fixture, assignment, intent, "dispatched"), supersedes: intent.Id);
            RecordEnvelope? firstPad = null;
            RecordEnvelope Pad(RecordEnvelope from)
            {
                RecordEnvelope last = from;
                for (int i = 0; i < 10; i++)
                {
                    last = fixture.Add("operation", Revision(fixture, assignment, intent, "dispatched"), builder: true, supersedes: last.Id);
                    firstPad ??= last;
                }

                return last;
            }

            // The carrier's fork of the dispatched revision, sorting before its padding, so the run as read ends on it.
            RecordEnvelope Fork() => fixture.Estate.Add("operation", fixture.Estate.BuilderId, Revision(fixture, assignment, intent, "dispatched").ToJsonString(), supersedes: dispatched.Id, id: Below(firstPad!.Id));

            (string, int) expected;
            switch (shape)
            {
                case "unsettled-on-the-padding":
                    // An untied revision leaving the run dispatched at the padded end (depth 12), then the operator's known failure on the
                    // carrier's fork (depth 3): the settlement is not outranked by the deeper unsettled one, and three failures hold.
                    fixture.Add("operation", Revision(fixture, assignment, intent, "dispatched"), supersedes: Pad(dispatched).Id);
                    fixture.Add("operation", Revision(fixture, assignment, intent, "known-failure"), supersedes: Fork().Id);
                    expected = ("held", 3);
                    break;
                case "success-on-the-padding":
                    // The operator's success naming the completed result at the padded end, and its revision leaving the run dispatched on
                    // a shallower fork: the heads disagree, so the run counts for nothing and ends no failure.
                    fixture.Add("operation", Revision(fixture, assignment, intent, "observed-success", done), supersedes: Pad(dispatched).Id);
                    fixture.Add("operation", Revision(fixture, assignment, intent, "dispatched"), supersedes: dispatched.Id);
                    expected = ("active", 2);
                    break;
                case "correction-over-the-padding":
                    // The operator's known failure, padded by the carrier, and the operator's correction to canceled written over the padding:
                    // the correction follows the settlement on its own chain and counts as written.
                    RecordEnvelope settled = fixture.Add("operation", Revision(fixture, assignment, intent, "known-failure"), supersedes: dispatched.Id);
                    fixture.Add("operation", Revision(fixture, assignment, intent, "canceled"), supersedes: Pad(settled).Id);
                    expected = ("active", 2);
                    break;
                default:
                    // The operator's known failure at the padded end and its correction to canceled on a shallower fork, which does not follow
                    // it: the failure is not outranked, as it was not by depth before.
                    fixture.Add("operation", Revision(fixture, assignment, intent, "known-failure"), supersedes: Pad(dispatched).Id);
                    fixture.Add("operation", Revision(fixture, assignment, intent, "canceled"), supersedes: dispatched.Id);
                    expected = ("held", 3);
                    break;
            }

            Assert.True(expected == Pause(), $"{shape}: {Pause()}");
        }
    }

    [Fact]
    public void ARunIntentIsRefusedPastUntilDuringThePausePastThePeriodsRunsAndOnASettledOccurrenceWhoeverRecordsIt()
    {
        using var fixture = new StandingFixture(budget: Roomy.DeepClone().AsObject(), period: new JsonObject { ["unit"] = "months", ["runs"] = 2, ["wallMinutes"] = 200 }, until: "2026-10-13T13:00:00Z");
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        Assert.DoesNotContain(Validate(fixture, "operation", fixture.RunBody(assignment, "run-ok", "not-started")), f => f.Severity == Severity.Error);

        // Two runs spend the period's runs: a third intent is refused.
        fixture.Settle(fixture.RunIntent(assignment, "run-1"), "known-failure");
        fixture.Settle(fixture.RunIntent(assignment, "run-2"), "known-failure");
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-3", "not-started")), f => f.Code == "operation.budget" && f.Message.Contains("runs in the period", StringComparison.Ordinal));

        // Past its until time, an intent is refused at the record whatever route records it.
        fixture.Estate.SetClock(At("2026-10-13T13:00:00Z"));
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-late", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("until time", StringComparison.Ordinal));
        StandingAgents.Trigger monthly = Assert.Single(StandingAgents.Triggers(fixture.Estate.Load(), fixture.Goal));
        var late = new StandingAgents.Occurrence(occurrence.Key, "monthly", "schedule", monthly.Revision, occurrence.Item, occurrence.DueAt, [], "late");
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(late, fixture.Estate.BuilderId), fixture.Work.Reference),
            f => f.Code == "assignment.occurrence" && f.Message.Contains("has passed when this assignment is recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void ASettledOccurrenceRunsAgainOnlyWhenAReviewReturnsItsResultAndResultsAwaitingReviewHoldDispatchAtTheirCap()
    {
        using var fixture = new StandingFixture(budget: Roomy.DeepClone().AsObject(), triggers: new JsonArray(StandingFixture.Monthly(), new JsonObject { ["key"] = "asked", ["kind"] = "request" }));
        JsonObject capped = fixture.GoalBody(budget: Roomy.DeepClone().AsObject(), triggers: new JsonArray(StandingFixture.Monthly(), new JsonObject { ["key"] = "asked", ["kind"] = "request" }));
        capped["plan"]!["standing"]!["awaitingReview"] = 1;
        RecordEnvelope goal = fixture.Add("goal", capped, supersedes: fixture.Goal.Id);
        fixture.Activate(goal);
        StandingAgents.Occurrence occurrence = Assert.Single(StandingAgents.Derive(fixture.Estate.Load(), goal, fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        RecordEnvelope result = fixture.Result(assignment);

        // Its result awaits review: a run of it is refused at the record, and the cap of one result awaiting review holds every other run.
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(assignment, "run-again", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("is settled", StringComparison.Ordinal));
        StandingAgents.Trigger asked = StandingAgents.Triggers(fixture.Estate.Load(), goal).Single(t => t.Key == "asked");
        StandingAgents.Occurrence request = StandingAgents.Request(goal.Id == fixture.Goal.Id ? goal.Id : fixture.Estate.Load().Chain(goal)[0], asked, fixture.Estate.OperatorId, fixture.Estate.Clock);
        RecordEnvelope other = fixture.Assign(request, fixture.Estate.BuilderId);
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(other, "run-other", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("await review, its cap of 1", StringComparison.Ordinal));
        StandingAgents.Agent agent = StandingAgents.Derive(fixture.Estate.Load(), goal, fixture.Estate.Clock);
        Assert.Contains(agent.Occurrences, o => o.Occurrence.Key == request.Key && o.State == "deferred" && o.Reason.Contains("await review, its cap of 1", StringComparison.Ordinal));

        // A review returns the result: the occurrence runs again on its assignment, and nothing awaits review.
        fixture.Review(result, "findings");
        Assert.DoesNotContain(Validate(fixture, "operation", fixture.RunBody(assignment, "run-returned", "not-started")), f => f.Severity == Severity.Error);
        Assert.DoesNotContain(Validate(fixture, "operation", fixture.RunBody(other, "run-free", "not-started")), f => f.Severity == Severity.Error);
    }

    [Fact]
    public void ARevisionKeepsItsOccurrenceSoItsKeyStaysHeldAndAMergedDuplicateIsAReconcileTaskSettledByAReconciliation()
    {
        using var fixture = new StandingFixture(triggers: new JsonArray(StandingFixture.Monthly(), new JsonObject { ["key"] = "asked", ["kind"] = "request" }));
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        fixture.Result(assignment);

        // A revision naming another occurrence is refused; one keeping it is accepted, and the key stays held.
        StandingAgents.Trigger asked = StandingAgents.Triggers(fixture.Estate.Load(), fixture.Goal).Single(t => t.Key == "asked");
        StandingAgents.Occurrence request = StandingAgents.Request(fixture.Goal.Id, asked, fixture.Estate.OperatorId, fixture.Estate.Clock);
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(request, fixture.Estate.BuilderId), fixture.Work.Reference, supersedes: assignment.Id),
            f => f.Code == "assignment.occurrence" && f.Message.Contains("a revision keeps its occurrence exactly", StringComparison.Ordinal));
        JsonObject kept = StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId);
        kept["note"] = "Narrowed.";
        RecordEnvelope revised = fixture.Add("assignment", kept, subject: fixture.Work.Reference, supersedes: assignment.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "standing.duplicate" && f.Message.Contains(revised.Id, StringComparison.Ordinal));

        // Two clones' merge leaves a second assignment of the key: both are flagged, status lists them as reconcile tasks, and a
        // reconciliation keeping one settles it.
        RecordEnvelope merged = fixture.Add("assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), subject: fixture.Work.Reference);
        EstateModel model = fixture.Estate.Load();
        Assert.Equal(2, model.Findings.Count(f => f.Code == "standing.duplicate"));
        Assert.Equal(2, EstateStatus.ReconcileTasks(model.Findings).Count(f => f.Code == "standing.duplicate"));
        Assert.Contains(Validate(fixture, "reconciliation", new JsonObject { ["kept"] = revised.Id, ["withdrawn"] = new JsonArray(merged.Id, fixture.Policies.Decision.Id), ["note"] = "Keep the first." }, merged.Reference),
            f => f.Code == "reconciliation.occurrence");

        // An assignment stored without the run it was routed with holds nothing: kept in place of both holders, it would free the key.
        JsonObject strandedBody = StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId);
        strandedBody["routedWith"] = "run";
        RecordEnvelope stranded = fixture.Add("assignment", strandedBody, subject: fixture.Work.Reference);
        Assert.Contains(Validate(fixture, "reconciliation", new JsonObject { ["kept"] = stranded.Id, ["withdrawn"] = new JsonArray(revised.Id, merged.Id), ["note"] = "Keep the stranded one." }, merged.Reference),
            f => f.Code == "reconciliation.occurrence" && f.Message.Contains($"{stranded.Id} does not hold occurrence", StringComparison.Ordinal));
        fixture.Add("reconciliation", new JsonObject { ["kept"] = revised.Id, ["withdrawn"] = new JsonArray(merged.Id), ["note"] = "Keep the first clone's assignment of the occurrence." }, subject: merged.Reference);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }

    [Fact]
    public void AReconciliationSettlesOnlyARealDuplicateAndASettledOccurrenceStaysSettledWhateverIsWithdrawn()
    {
        JsonArray Triggers() => new(StandingFixture.Monthly(), new JsonObject { ["key"] = "asked", ["kind"] = "request" });
        using var fixture = new StandingFixture(budget: Roomy.DeepClone().AsObject(), triggers: Triggers());
        JsonObject capped = fixture.GoalBody(budget: Roomy.DeepClone().AsObject(), triggers: Triggers());
        capped["plan"]!["standing"]!["awaitingReview"] = 1;
        RecordEnvelope goal = fixture.Add("goal", capped, supersedes: fixture.Goal.Id);
        fixture.Activate(goal);

        // The occurrence is carried, its run settles and its result awaits review.
        StandingAgents.Occurrence occurrence = Assert.Single(StandingAgents.Derive(fixture.Estate.Load(), goal, fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope holder = fixture.Assign(occurrence);
        RecordEnvelope intent = fixture.RunIntent(holder, "run-carried");
        RecordEnvelope result = fixture.Result(holder);
        fixture.Settle(intent, "observed-success");
        Assert.Equal("settled", Assert.Single(StandingAgents.Derive(fixture.Estate.Load(), goal, fixture.Estate.Clock).Occurrences).State);

        // An assignment of the same key routed with a run and stored without its intent holds nothing, so it stands beside the holder.
        JsonObject strandedBody = StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId);
        strandedBody["routedWith"] = "run";
        RecordEnvelope stranded = fixture.Add("assignment", strandedBody, subject: fixture.Work.Reference);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));

        // It is no duplicate: one holder holds the key, so the operator's reconciliation keeping it and withdrawing the holder is refused,
        // and so is one keeping the holder.
        JsonObject release = new() { ["kept"] = stranded.Id, ["withdrawn"] = new JsonArray(holder.Id), ["note"] = "Keep the stranded one." };
        Assert.Contains(Validate(fixture, "reconciliation", release, stranded.Reference), f => f.Code == "reconciliation.occurrence" && f.Message.Contains("is held by 1 of the assignments named", StringComparison.Ordinal));
        Assert.Contains(Validate(fixture, "reconciliation", new JsonObject { ["kept"] = holder.Id, ["withdrawn"] = new JsonArray(stranded.Id), ["note"] = "Keep the holder." }, stranded.Reference),
            f => f.Code == "reconciliation.occurrence" && f.Message.Contains("is held by 1 of the assignments named", StringComparison.Ordinal));

        // Recorded anyway, as a merge could bring it, it is an error, and the occurrence stays settled: the withdrawn holder's result settles
        // it, a run of the kept assignment is refused at its intent, and the result still holds the awaiting-review cap of one.
        fixture.Add("reconciliation", release, subject: stranded.Reference);
        EstateModel model = fixture.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "reconciliation.occurrence" && f.Severity == Severity.Error);
        StandingAgents.Agent agent = StandingAgents.Derive(model, goal, fixture.Estate.Clock);
        StandingAgents.OccurrenceState state = Assert.Single(agent.Occurrences, o => o.Occurrence.Key == occurrence.Key);
        Assert.Equal(("settled", result.Id, 1), (state.State, state.Result?.Id, agent.Awaiting));
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(stranded, "run-again", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("is settled", StringComparison.Ordinal));
        StandingAgents.Trigger asked = StandingAgents.Triggers(model, goal).Single(t => t.Key == "asked");
        RecordEnvelope other = fixture.Assign(StandingAgents.Request(fixture.Goal.Id, asked, fixture.Estate.OperatorId, fixture.Estate.Clock));
        Assert.Contains(Validate(fixture, "operation", fixture.RunBody(other, "run-other", "not-started")), f => f.Code == "operation.standing" && f.Message.Contains("await review, its cap of 1", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOccurrenceIsCarriedOnlyAsItsTriggerMakesItWhileItsAgentIsActive()
    {
        RecordEnvelope? policy = null;
        JsonObject duty = new() { ["key"] = "maintenance", ["kind"] = "duty", ["policy"] = "01M7ASMJ43WMQ5VDASDYVZ7BGV", ["rule"] = "monthly" };
        using var fixture = new StandingFixture(activate: false);
        policy = fixture.Policies.Policy();
        duty["policy"] = policy.Id;
        JsonObject condition = new() { ["key"] = "no-open-bugs", ["kind"] = "condition", ["record"] = new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["title"] = "Open bug" }, ["max"] = 0 } };
        JsonObject events = new() { ["key"] = "bugs", ["kind"] = "event", ["recordType"] = "work-order", ["fields"] = new JsonObject { ["title"] = "Open bug" } };
        RecordEnvelope goal = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(StandingFixture.Monthly(firstAt: "2026-07-01T00:00:00Z"), duty, condition, events)), supersedes: fixture.Goal.Id);
        fixture.Activate(goal);
        fixture.Estate.SetClock(At("2026-11-14T00:00:00Z"));
        StandingAgents.Trigger Trigger(string key) => StandingAgents.Triggers(fixture.Estate.Load(), goal).Single(t => t.Key == key);
        FindingList Carry(StandingAgents.Trigger trigger, JsonNode item) =>
            Validate(fixture, "assignment", StandingFixture.AssignmentBody(new StandingAgents.Occurrence(StandingAgents.Key(fixture.Goal.Id, trigger, StandingAgents.ItemKey(trigger.Kind, Element(item)) ?? "x"), trigger.Key, trigger.Kind, trigger.Revision, Element(item), fixture.Estate.Clock, [], "probe"), fixture.Estate.BuilderId), fixture.Work.Reference);

        // Missed ticks coalesce: only the latest interval is carried.
        StandingAgents.Trigger monthly = Trigger("monthly");
        Assert.Contains(Carry(monthly, new JsonObject { ["interval"] = 2, ["dueAt"] = "2026-09-01T00:00:00Z" }), f => f.Code == "assignment.occurrence" && f.Message.Contains("coalesced into interval 4", StringComparison.Ordinal));
        Assert.DoesNotContain(Carry(monthly, new JsonObject { ["interval"] = 4, ["dueAt"] = "2026-11-01T00:00:00Z" }), f => f.Severity == Severity.Error);
        Assert.Contains(Carry(monthly, new JsonObject { ["interval"] = "4", ["dueAt"] = "2026-11-01T00:00:00Z" }), f => f.Code == "assignment.occurrence");

        // A duty occurrence is the duty's exact identity: a near miss or a made-up one is refused.
        StandingAgents.Trigger owed = Trigger("maintenance");
        JsonElement rule = Assert.Single(policy.Body.GetProperty("rules").EnumerateArray(), r => r.GetProperty("key").GetString() == "monthly");
        string subject = fixture.Estate.Load().Chain(fixture.Estate.Load().ById[rule.GetProperty("parameters").GetProperty("subject").GetProperty("id").GetString()!])[0];
        string identity = $"{policy.Id}:{subject}:{{\"kind\":\"interval\",\"number\":0}}";
        JsonObject Duty(string occurrenceIdentity) => new()
        {
            ["occurrence"] = occurrenceIdentity, ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "monthly", ["dueAt"] = "2026-10-13T13:00:00Z", ["deadline"] = "2026-10-16T13:00:00Z",
            ["schedule"] = new JsonObject { ["kind"] = "interval", ["number"] = 0 },
        };
        Assert.DoesNotContain(Carry(owed, Duty(identity)), f => f.Severity == Severity.Error);
        Assert.Contains(Carry(owed, Duty(identity + " ")), f => f.Code == "assignment.occurrence" && f.Message.Contains("its identity is not the duty occurrence's", StringComparison.Ordinal));
        Assert.Contains(Carry(owed, Duty("anything-at-all")), f => f.Code == "assignment.occurrence");

        // The schedule item is the duty's own, built from the policy's schedule, exactly: with the identity built from it, an extra key or a
        // changed value is another item and refused, so one deadline never has a second key. A reordered object is the same canonical item
        // (records are stored in canonical form), so it makes the same identity and the same key.
        JsonObject Varied(JsonObject schedule)
        {
            JsonObject duty = Duty($"{policy.Id}:{subject}:{CanonicalJson.Serialize(Element(schedule))}");
            duty["schedule"] = schedule;
            return duty;
        }

        Assert.Contains(Carry(owed, Varied(new JsonObject { ["kind"] = "interval", ["number"] = 0, ["note"] = "again" })),
            f => f.Code == "assignment.occurrence" && f.Message.Contains("its schedule item is not the duty's own, {\"kind\":\"interval\",\"number\":0}, exactly", StringComparison.Ordinal));
        Assert.Contains(Carry(owed, Varied(new JsonObject { ["kind"] = "date", ["number"] = 0 })), f => f.Code == "assignment.occurrence" && f.Message.Contains("is not the duty's own", StringComparison.Ordinal));
        Assert.Contains(Carry(owed, Varied(new JsonObject { ["kind"] = "interval", ["number"] = "0" })), f => f.Code == "assignment.occurrence" && f.Message.Contains("is not one of the duty's schedule", StringComparison.Ordinal));
        JsonObject reordered = Varied(new JsonObject { ["number"] = 0, ["kind"] = "interval" });
        Assert.DoesNotContain(Carry(owed, reordered), f => f.Severity == Severity.Error);
        Assert.Equal(StandingAgents.ItemKey("duty", Element(Duty(identity))), StandingAgents.ItemKey("duty", Element(reordered)));

        // A condition's failing set, and an event's record, are the trigger's as the records stand: the write boundary refuses others.
        fixture.Add("work-order", new JsonObject { ["key"] = "BUG-1", ["kind"] = "ordinary", ["title"] = "Open bug", ["intent"] = "Fix it.", ["scope"] = new JsonArray("path:fix/"), ["exitQuestion"] = "Fixed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("fix") });
        Assert.Contains(Carry(Trigger("no-open-bugs"), new JsonObject { ["failing"] = FlowFixture.Hash('9'), ["records"] = 1 }), f => f.Code == StandingAgents.BoundaryCode);
        RecordEnvelope own = fixture.Add("work-order", new JsonObject { ["key"] = "BUG-2", ["kind"] = "ordinary", ["title"] = "Open bug", ["intent"] = "Fix it.", ["scope"] = new JsonArray("path:fix/"), ["exitQuestion"] = "Fixed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("fix") }, builder: true);
        Assert.Contains(Carry(Trigger("bugs"), new JsonObject { ["record"] = PolicyFixture.Ref(own) }), f => f.Code == StandingAgents.BoundaryCode && f.Message.Contains("the agent's own", StringComparison.Ordinal));

        // Paused, no new occurrence is carried, whatever time its triggers were evaluated at.
        fixture.Add("goal-status", new JsonObject { ["status"] = "paused", ["note"] = "Paused for the test." }, subject: goal.Reference);
        Assert.Contains(Carry(monthly, new JsonObject { ["interval"] = 4, ["dueAt"] = "2026-11-01T00:00:00Z" }), f => f.Code == "assignment.occurrence" && f.Message.Contains("is paused when this assignment is recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void AReleaseDutyOccurrenceIsOnlyTheFirstPublicationOfTheSchedulesProductWithinItsWindow()
    {
        using var fixture = new StandingFixture(activate: false);
        RecordEnvelope Product(string slug) => fixture.Add("product", new JsonObject
        {
            ["name"] = "Fixture product", ["slug"] = slug, ["summary"] = "Synthetic publication history.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture surface." }),
        });
        RecordEnvelope Publish(RecordEnvelope product, string version)
        {
            RecordEnvelope candidate = fixture.Add("release", new JsonObject
            {
                ["version"] = version, ["repository"] = "Example/example", ["commit"] = new string('1', 40), ["status"] = "candidate",
                ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture", ["hash"] = FlowFixture.Hash('b'), ["bytes"] = 0, ["platform"] = "any" }),
                ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "Fixture only", ["limitations"] = new JsonArray(),
            }, subject: new RecordReference("product", product.Id, null));
            fixture.Add("acceptance", new JsonObject { ["statement"] = "Accept the fixture publication.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
            JsonObject body = PolicyFixture.Body(candidate);
            body["status"] = "published";
            body["publishedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
            body["url"] = "https://example.invalid/releases/fixture";
            return fixture.Add("release", body, subject: candidate.Subject, supersedes: candidate.Id);
        }

        // A release-scheduled duty from 13:00; one release of its product is published before that.
        RecordEnvelope product = Product("example");
        RecordEnvelope other = Product("other");
        RecordEnvelope early = Publish(product, "0.9.0");
        JsonObject definition = fixture.Policies.PolicyBody();
        definition["rules"]![0]!["parameters"]!["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = product.Id };
        RecordEnvelope policy = fixture.Policies.Policy(definition);
        JsonObject duty = new() { ["key"] = "releases", ["kind"] = "duty", ["policy"] = policy.Id, ["rule"] = "monthly" };
        RecordEnvelope goal = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(duty)), supersedes: fixture.Goal.Id);
        fixture.Activate(goal);

        // Within its window: the product's first publication, another product's, and a later published revision of the first.
        fixture.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope first = Publish(product, "1.0.0");
        RecordEnvelope elsewhere = Publish(other, "1.0.0");
        fixture.Add("acceptance", new JsonObject { ["statement"] = "Accept the publication again.", ["basis"] = new JsonArray(), ["self"] = true }, subject: first.Reference);
        JsonObject again = PolicyFixture.Body(first);
        again["url"] = "https://example.invalid/releases/fixture-again";
        again["publishedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1));
        RecordEnvelope republished = fixture.Add("release", again, subject: first.Subject, supersedes: first.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings.Where(f => f.Severity == Severity.Error)));
        fixture.Estate.SetClock(At("2026-11-14T00:00:00Z"));

        StandingAgents.Trigger trigger = StandingAgents.Triggers(fixture.Estate.Load(), goal).Single(t => t.Key == "releases");
        string subject = fixture.Estate.Load().Chain(fixture.Estate.Estate)[0];
        FindingList Carry(RecordEnvelope release)
        {
            JsonObject schedule = new() { ["kind"] = "release", ["publication"] = PolicyFixture.Ref(release) };
            JsonObject item = new()
            {
                ["occurrence"] = $"{policy.Id}:{subject}:{CanonicalJson.Serialize(Element(schedule))}", ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "monthly",
                ["dueAt"] = release.At, ["deadline"] = RecordParser.FormatTimestamp(release.AtTime.AddDays(3)), ["schedule"] = schedule,
            };
            var occurrence = new StandingAgents.Occurrence(StandingAgents.Key(fixture.Goal.Id, trigger, StandingAgents.ItemKey("duty", Element(item))!), trigger.Key, trigger.Kind, trigger.Revision, Element(item), fixture.Estate.Clock, [], "probe");
            return Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference);
        }

        Assert.DoesNotContain(Carry(first), f => f.Severity == Severity.Error);
        Assert.Contains(Carry(elsewhere), f => f.Code == "assignment.occurrence" && f.Message.Contains("is not one of the schedule's product", StringComparison.Ordinal));
        Assert.Contains(Carry(early), f => f.Code == "assignment.occurrence" && f.Message.Contains("published before the schedule's activation", StringComparison.Ordinal));
        Assert.Contains(Carry(republished), f => f.Code == "assignment.occurrence" && f.Message.Contains("is not the first published revision", StringComparison.Ordinal));
    }
}
