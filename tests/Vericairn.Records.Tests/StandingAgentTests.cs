// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
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
        Assert.Contains(duplicate, f => f.Code == "assignment.duplicate" && f.Message.Contains(assignment.Id, StringComparison.Ordinal));

        // The occurrence settles with its result, and runs again only when a review returns it, on the assignment that holds it.
        RecordEnvelope result = fixture.Result(assignment);
        StandingAgents.OccurrenceState settled = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Assert.Equal(("settled", assignment.Id, result.Id), (settled.State, settled.Assignment!.Id, settled.Result!.Id));
        fixture.Review(result, "findings");
        StandingAgents.OccurrenceState returned = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences);
        Assert.Equal("due", returned.State);
        Assert.Contains($"assignment {assignment.Id} goes on with another attempt", returned.Reason, StringComparison.Ordinal);
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "assignment.duplicate");
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

        // Three blocked results in a row pause it with an ask; the sponsor's answer lets it go on.
        StandingAgents.Occurrence occurrence = Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(occurrence);
        fixture.Result(assignment, "blocked");
        fixture.Result(assignment, "abandoned");
        RecordEnvelope third = fixture.Result(assignment, "blocked");
        StandingAgents.Agent held = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(("held", 3, third.Id), (held.State, held.Failures, held.FailureTrigger!.Id));
        Assert.Contains(StandingAgents.RunRefusals(fixture.Estate.Load(), fixture.Goal, assignment.Id, null), r => r.Message.Contains("failed in a row", StringComparison.Ordinal));
        RecordEnvelope ask = fixture.Add("ask", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = "Required attention in goal Research the widget: its runs failed. How does the run go on?", ["recommendation"] = "Let it go on.",
            ["consequence"] = "Nothing is dispatched.", ["goal"] = fixture.Goal.Id, ["state"] = "open",
        }, subject: third.Reference);
        Assert.Equal("held", fixture.Agent(fixture.Estate.Clock).State);
        JsonObject answer = fixture.Policies.DecisionBody();
        answer["answers"] = new JsonArray(ask.Id);
        fixture.Add("decision", answer);
        Assert.Equal(("active", 0), (fixture.Agent(fixture.Estate.Clock).State, fixture.Agent(fixture.Estate.Clock).Failures));

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
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "assignment.duplicate");

        RecordEnvelope changed = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(StandingFixture.Monthly(firstAt: "2026-10-02T00:00:00Z")), until: "2027-09-01T00:00:00Z"), supersedes: renewed.Id);
        fixture.Activate(changed);
        StandingAgents.Trigger trigger = Assert.Single(StandingAgents.Triggers(fixture.Estate.Load(), changed));
        Assert.Equal(changed.Id, trigger.Revision);
        Assert.Contains(StandingAgents.Derive(fixture.Estate.Load(), changed, fixture.Estate.Clock).Occurrences, o => o.Occurrence.Key == $"{fixture.Goal.Id}/monthly/{changed.Id}/interval-0" && o.State == "due");
        Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings));
    }
}
