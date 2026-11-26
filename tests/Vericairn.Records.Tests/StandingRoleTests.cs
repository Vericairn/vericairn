// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// The first standing roles as flows (from 0.9.4, PROC-08; the estate's design/standing-agents.md § 4 and § 14 item 5): the duty carrier,
/// the researcher and the bug hunter ship as built-in flows with their triggers, briefs, outputs and never lines, and each fills a
/// standing agent whose occurrences GOAL-07 carries and whose outputs GOAL-08 records as reviewable possibilities within its caps.
/// </summary>
public class StandingRoleTests
{
    private static DateTimeOffset At(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    private static TemplateDefinition Flow(string id) => StandingRoleFlows.Definitions.Single(d => d.Id == id);

    private static FindingList Validate(StandingFixture fixture, string type, JsonObject body, RecordReference? subject = null, string? supersedes = null, bool builder = false)
    {
        fixture.Estate.SetClock(fixture.Estate.Clock.AddSeconds(1));
        string by = builder ? fixture.Estate.BuilderId : fixture.Estate.OperatorId;
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, Element(body), subject, supersedes, fixture.Estate.Clock, null, 3));
        var findings = new FindingList();
        RecordTypeRegistry.Find(type)!.Validate(new ValidationContext(fixture.Estate.Load(), record.Envelope, findings));
        return findings;
    }

    private static void Valid(StandingFixture fixture) => Assert.False(fixture.Estate.Load().Findings.HasErrors, string.Join('\n', fixture.Estate.Load().Findings.Where(f => f.Severity == Severity.Error)));

    private static JsonObject Raised(StandingFixture fixture, RecordEnvelope assignment, RecordEnvelope run) =>
        new() { ["goal"] = fixture.Goal.Id, ["occurrence"] = StandingAgents.KeyOf(assignment), ["assignment"] = assignment.Id, ["run"] = run.Id };

    private static JsonObject Possibility(JsonObject raised, string fingerprint, string source, bool draft = false)
    {
        var body = new JsonObject
        {
            ["title"] = "Finding " + fingerprint, ["idea"] = "What the occurrence found about " + fingerprint + ".", ["origin"] = "A standing role's occurrence.", ["disposition"] = "open",
            ["raisedBy"] = raised.DeepClone(), ["fingerprint"] = fingerprint, ["sources"] = new JsonArray(new JsonObject { ["address"] = source, ["hash"] = FlowFixture.Hash('d') }),
            ["evidence"] = new JsonArray(new JsonObject { ["stance"] = "for", ["statement"] = "The source shows it.", ["sources"] = new JsonArray(0) }),
        };
        if (draft)
        {
            body["draft"] = new JsonObject { ["title"] = "Fix " + fingerprint, ["intent"] = "Fix what was found.", ["scope"] = new JsonArray("path:src/"), ["deliverables"] = new JsonArray("the fix with its test"), ["exitQuestion"] = "Does the reproduction pass?" };
        }

        return body;
    }

    private static JsonObject Disposed(RecordEnvelope possibility, string disposition, string reason, RecordEnvelope? adoptedAs = null)
    {
        JsonObject body = JsonNode.Parse(possibility.Body.GetRawText())!.AsObject();
        body["disposition"] = disposition;
        body["reason"] = reason;
        if (adoptedAs is not null) body["adoptedAs"] = new JsonObject { ["type"] = adoptedAs.Type, ["id"] = adoptedAs.Id, ["hash"] = adoptedAs.ComputedHash };
        return body;
    }

    private static RecordEnvelope WorkOrder(StandingFixture fixture, string key, RecordEnvelope possibility) => fixture.Add("work-order", new JsonObject
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Fix it", ["intent"] = "Fix what was found.", ["scope"] = new JsonArray("path:src/"), ["exitQuestion"] = "Is it fixed?",
        ["inputs"] = new JsonArray(new JsonObject { ["type"] = "possibility", ["id"] = possibility.Id }), ["deliverables"] = new JsonArray("the fix"),
    });

    [Fact]
    public void TheThreeRolesShipAsFlowsWithTriggersBriefsOutputsAndNeverLines()
    {
        Assert.Equal([StandingRoleFlows.DutyCarrier, StandingRoleFlows.Researcher, StandingRoleFlows.BugHunter], StandingRoleFlows.Definitions.Select(d => d.Id));
        using var fixture = new StandingFixture();
        EstateModel model = fixture.Estate.Load();
        foreach (TemplateDefinition definition in StandingRoleFlows.Definitions)
        {
            FlowDefinition flow = FlowDefinition.Resolve(model, new TemplatePin(definition.Id, definition.Version, definition.Hash));
            FlowStep step = Assert.IsType<FlowStep>(StandingAgents.WorkStep(flow));
            Assert.Contains(TemplateCatalog.All(model), d => d.Id == definition.Id && d.Hash == definition.Hash);
            Assert.NotEmpty(flow.Never);
            Assert.Contains(flow.Never, n => n.Contains("your own", StringComparison.Ordinal));
            Assert.Contains("authority each effect needs", flow.Role(step.Role)!.Brief, StringComparison.Ordinal);
            Assert.Contains(flow.Triggers, t => TJson.Text(t, "kind") == "request");
        }

        Assert.Equal(["duty", "request"], Kinds(StandingRoleFlows.DutyCarrier));
        Assert.Equal(["schedule", "event", "request"], Kinds(StandingRoleFlows.Researcher));
        Assert.Equal(["schedule", "event", "request"], Kinds(StandingRoleFlows.BugHunter));

        IEnumerable<string> Kinds(string id) => FlowDefinition.Resolve(model, new TemplatePin(id, StandingRoleFlows.Version, Flow(id).Hash)).Triggers.Select(t => TJson.Text(t, "kind")!);
    }

    // Monthly research, cited possibilities, independent adoption and the declined-output rule.
    [Trait("Journey", "D07-A")]
    [Fact]
    public void AResearcherRecordsCitedPossibilitiesOneAdoptedAndOneDeclinedAndNotRaisedAgain()
    {
        using var fixture = new StandingFixture(flow: Flow(StandingRoleFlows.Researcher), role: "researcher");
        Valid(fixture);
        StandingAgents.Agent agent = fixture.Agent(fixture.Estate.Clock);
        Assert.Equal(("active", "researcher"), (agent.State, agent.Step!.Role));
        StandingAgents.Occurrence monthly = Assert.Single(agent.Occurrences).Occurrence;
        RecordEnvelope assignment = fixture.Assign(monthly);
        RecordEnvelope run = fixture.RunIntent(assignment, "research-run");
        JsonObject raised = Raised(fixture, assignment, run);
        RecordEnvelope adoptable = fixture.Add("possibility", Possibility(raised, "index/cache-reads", "https://example.org/widget/index", draft: true), builder: true);
        RecordEnvelope declinable = fixture.Add("possibility", Possibility(raised, "legacy/drop-index", "https://example.org/widget/legacy"), builder: true);
        RecordEnvelope ask = fixture.Add("ask", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = "Is the legacy index still read by older clients?", ["recommendation"] = "Keep it until you know.", ["consequence"] = "The research goes on.",
            ["goal"] = fixture.Goal.Id, ["state"] = "open", ["raisedBy"] = raised.DeepClone(),
        }, builder: true);
        Valid(fixture);
        Assert.Equal(3, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // The agent adopts nothing of its own; the operator adopts one as a work order and declines the other.
        Assert.Contains(Validate(fixture, "possibility", Disposed(adoptable, "adopted", "Worth it.", fixture.Work), supersedes: adoptable.Id, builder: true), f => f.Code == "standing.own-output");
        RecordEnvelope order = WorkOrder(fixture, "IDX-1", adoptable);
        fixture.Add("possibility", Disposed(adoptable, "adopted", "Worth the second it saves.", order), supersedes: adoptable.Id);
        fixture.Add("possibility", Disposed(declinable, "declined", "Older clients still read it."), supersedes: declinable.Id);
        Valid(fixture);
        Assert.Equal(1, fixture.Agent(fixture.Estate.Clock).OpenOutputs);

        // The declined one is not raised again without naming it; the result settles the occurrence for review.
        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "legacy/drop-index", "https://example.org/widget/legacy"), builder: true), f => f.Code == "standing.raises-again");
        fixture.Settle(run, "observed-success");
        RecordEnvelope result = fixture.Result(assignment);
        Assert.Equal("settled", Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).State);
        fixture.Review(result, "pass");
        Assert.Equal("disposed", Assert.Single(fixture.Agent(fixture.Estate.Clock).Occurrences).State);
        Assert.Contains(StandingOutputs.OpenAndDeclined(fixture.Estate.Load(), fixture.Goal), p => fixture.Estate.Load().Chain(p)[0] == declinable.Id);
        Assert.Equal("open", EstateModel.BodyString(fixture.Estate.Load().Latest(ask), "state"));
        Valid(fixture);
    }

    [Fact]
    public void ABugHunterRecordsReproducedDefectsWithinItsCapsAndAHeldOneWaitsForAPersonOperator()
    {
        JsonObject asked = new() { ["key"] = "asked", ["kind"] = "request" };
        JsonObject weekly = new() { ["key"] = "weekly", ["kind"] = "schedule", ["unit"] = "days", ["every"] = 7, ["firstAt"] = "2026-10-01T00:00:00Z" };
        using var fixture = new StandingFixture(triggers: new JsonArray(weekly, asked), flow: Flow(StandingRoleFlows.BugHunter), role: "bug-hunter");
        Valid(fixture);
        StandingAgents.Trigger request = StandingAgents.Triggers(fixture.Estate.Load(), fixture.Goal).Single(t => t.Key == "asked");
        fixture.Estate.SetClock(fixture.Estate.Clock.AddSeconds(1));
        StandingAgents.Occurrence occurrence = StandingAgents.Request(fixture.Goal.Id, request, fixture.Estate.OperatorId, fixture.Estate.Clock);
        RecordEnvelope assignment = fixture.Assign(occurrence);
        RecordEnvelope run = fixture.RunIntent(assignment, "hunt-run");
        JsonObject raised = Raised(fixture, assignment, run);

        // Each defect carries its reproduction and a draft fix; three drafts, then the rest without, then one summary.
        foreach (string test in new[] { "tests/index/stale-read", "tests/index/double-write", "tests/index/lost-update" })
        {
            fixture.Add("possibility", Possibility(raised, test, "tests/index at commit " + new string('a', 40), draft: true), builder: true);
        }

        Assert.Contains(Validate(fixture, "possibility", Possibility(raised, "tests/index/fourth", "tests/index", draft: true), builder: true), f => f.Code == "standing.output-cap");
        JsonObject held = Possibility(raised, "tests/auth/bypass", "tests/auth at commit " + new string('b', 40));
        held["disclosure"] = "held";
        RecordEnvelope finding = fixture.Add("possibility", held, builder: true);
        Valid(fixture);

        // A held defect is adopted only by an operator who is a person: never by the hunter.
        RecordEnvelope order = WorkOrder(fixture, "SEC-1", finding);
        Assert.Contains(Validate(fixture, "possibility", Disposed(finding, "adopted", "Reviewed.", order), supersedes: finding.Id, builder: true), f => f.Code == "standing.own-output");
        Assert.DoesNotContain(Validate(fixture, "possibility", Disposed(finding, "adopted", "The Reviewer reviewed it.", order), supersedes: finding.Id), f => f.Severity == Severity.Error);
        Assert.Equal(4, fixture.Agent(fixture.Estate.Clock).OpenOutputs);
    }

    // Duty identity, refusal of a second carrier, and the reserved publication request.
    [Trait("Journey", "D07-B")]
    [Fact]
    public void ADutyCarrierCarriesTheDutyOccurrencesOwnKeyAndLeavesReservedActionsToTheOperator()
    {
        using var fixture = new StandingFixture(triggers: new JsonArray(new JsonObject { ["key"] = "asked", ["kind"] = "request" }), activate: false, flow: Flow(StandingRoleFlows.DutyCarrier), role: "duty-carrier", subjectField: "duties");
        RecordEnvelope policy = fixture.Policies.Policy();
        JsonObject duty = new() { ["key"] = "maintenance", ["kind"] = "duty", ["policy"] = policy.Id, ["rule"] = "monthly" };
        RecordEnvelope goal = fixture.Add("goal", fixture.GoalBody(triggers: new JsonArray(duty)), supersedes: fixture.Goal.Id);
        fixture.Activate(goal);
        Valid(fixture);
        fixture.Estate.SetClock(At("2026-11-14T00:00:00Z"));
        StandingAgents.Trigger trigger = StandingAgents.Triggers(fixture.Estate.Load(), goal).Single();
        JsonElement rule = Assert.Single(policy.Body.GetProperty("rules").EnumerateArray(), r => r.GetProperty("key").GetString() == "monthly");
        string subject = fixture.Estate.Load().Chain(fixture.Estate.Load().ById[rule.GetProperty("parameters").GetProperty("subject").GetProperty("id").GetString()!])[0];
        string identity = $"{policy.Id}:{subject}:{{\"kind\":\"interval\",\"number\":0}}";
        JsonObject item = new()
        {
            ["occurrence"] = identity, ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "monthly", ["dueAt"] = "2026-10-13T13:00:00Z", ["deadline"] = "2026-10-16T13:00:00Z",
            ["schedule"] = new JsonObject { ["kind"] = "interval", ["number"] = 0 },
        };
        var occurrence = new StandingAgents.Occurrence(StandingAgents.Key(fixture.Goal.Id, trigger, StandingAgents.ItemKey("duty", Element(item))!), trigger.Key, trigger.Kind, trigger.Revision, Element(item), fixture.Estate.Clock, [], "the monthly duty");
        Assert.Equal("duty/" + identity, occurrence.Key);
        RecordEnvelope assignment = fixture.Assign(occurrence);
        Valid(fixture);
        RecordEnvelope run = fixture.RunIntent(assignment, "duty-run");

        // A reserved action the duty needs is a publication request the operator approves, pinning the carrier's work order.
        const string text = "Dependencies were reviewed this month.";
        fixture.Add("operator-request", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["askedBy"] = fixture.Estate.BuilderId, ["action"] = "Publish an agent's draft", ["why"] = "The duty's report is public.", ["recommendation"] = "Post it.",
            ["consequence"] = "Nothing is posted.", ["kind"] = "publication", ["destination"] = "the project's notes",
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "text", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), ["text"] = text }), ["raisedBy"] = Raised(fixture, assignment, run),
        }, builder: true, subject: new RecordReference("work-order", fixture.Work.Id, fixture.Work.ComputedHash));
        Valid(fixture);
        Assert.Equal(1, StandingOutputs.OpenCount(fixture.Estate.Load(), goal));

        // A second carrier of the same duty occurrence is refused by its key, whoever records it.
        Assert.Contains(Validate(fixture, "assignment", StandingFixture.AssignmentBody(occurrence, fixture.Estate.BuilderId), fixture.Work.Reference), f => f.Code == "standing.duplicate");
    }
}
