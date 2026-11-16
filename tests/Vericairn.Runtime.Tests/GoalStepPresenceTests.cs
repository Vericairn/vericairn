// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// ID-16's follow-up (0.9.4): goal step's presence session, route by route, with the stand-in helper (design/presence-sessions.md §§ 5
/// and 10). The operator's device needs presence and a session may open on the embedded profile; a step routed to a person authors its
/// assignment and its ask completely in advance and lists both by exact hash, so one check covers them; every other route keeps one check
/// per signature: an agent's assignment, listed alone, and its run's intent, which the worker authors after its preflight; a rule's
/// flow-decision observation, outside § 10's allow-list; and the single writes of a sponsor's ask, an attention ask and an operator
/// request. goal decide is outside the action vocabulary, and its records keep one check each.
/// </summary>
[Collection("home")]
public class GoalStepPresenceTests
{
    [Fact]
    public void APersonsAssignmentAndAskAreListedByExactHashAndShareOneCheckWhileTheSponsorsAskKeepsItsOwn()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        Order(run, "W-0", "publisher");
        (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(run);
        long start = run.Service.PresencePrompts(new()).Cursor;

        GoalStepResponse asked = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));

        Assert.True(asked.Ok, asked.Message);
        Assert.Equal("W-0", asked.Step);
        Assert.True(asked.Route is "person" or "reserved", asked.Route);
        PresenceHelperStandIn helper = Assert.Single(helpers);
        Assert.True(helper.Closed);
        Assert.Empty(Asked(custody));
        PresenceSessionRules terms = PresenceSessionRules.ReadTerms(Assert.Single(helper.Terms));
        Assert.Equal("goal step", terms.Action);
        Assert.Equal(["assignment", "ask"], terms.Entries.Select(e => e.Type));
        Assert.All(terms.Entries, e => Assert.True(e.Exact));
        // Each stored record is the one listed, by exact hash, signed by the operator's device under the session's one check; the ask
        // names the assignment it gives.
        EstateModel model = run.Estate.Load();
        RecordEnvelope assignment = model.ById[asked.Assignment!.Id], ask = model.ById[asked.Ask!.Id];
        Assert.Equal([assignment.ComputedHash, ask.ComputedHash], terms.Entries.Select(e => e.Hash));
        Assert.Equal(2, helper.SignedHashes.Count);
        Assert.All(new[] { assignment, ask }, r => Assert.Equal(run.OperatorDevice, Assert.Single(r.Signatures).Device));
        Assert.Equal(assignment.Id, EstateModel.BodyString(ask, "assignment"));
        IReadOnlyList<PresenceNotice> notices = run.Service.PresencePrompts(new(start)).Notices;
        Assert.StartsWith("goal step " + run.Goal + " asks for your presence once, for one presence session of 2 signatures", notices[0].Text, StringComparison.Ordinal);
        Assert.Equal("Signed 2 of 2 with one presence check.", notices[^1].Text);

        // A step no principal is available for is the sponsor's ask alone: a single write, which keeps its own check.
        Order(run, "W-00", "planner");
        int before = Asked(custody).Count;
        GoalStepResponse sponsor = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(sponsor.Ok, sponsor.Message);
        Assert.Equal(("ask", "unavailable"), (sponsor.Outcome, sponsor.Route));
        Assert.Null(sponsor.Assignment);
        Assert.Single(helpers);
        Assert.Equal(before + 1, Asked(custody).Count);
        Assert.StartsWith("sign Vericairn signature 1 of 1: ask W-00", Asked(custody)[^1], StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAgentsAssignmentListedAloneAndItsRunsIntentEachKeepTheirOwnCheck()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        // The operator's key signs here, so the sponsor's custody exception lets the agent's run launch.
        string hash = run.Service.Goal(run.Goal)!.Json.GetProperty("goal").GetProperty("hash").GetString()!;
        string expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("custody-exception", op, RunEstate.Json($$"""{"reason":"The test's agent runs while the operator's key is held here.","expires":"{{expires}}"}"""), $"goal:{run.Goal}:{hash}")));
        GoalStepResponse first = GoalStepTests.Step(run, "first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal("W-1", first.Step);
        run.Settled(first.Run!.Run);
        GoalStepTests.Done(run, run.Assignment);
        (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(run);

        // W-2: routing's assignment, authored completely in advance, and the run's intent, authored by the worker after its preflight.
        GoalStepResponse second = GoalStepTests.Step(run, "second");

        Assert.True(second.Ok, second.Message);
        Assert.Equal(("run", "W-2", "agent"), (second.Outcome, second.Step, second.Route));
        run.Settled(second.Run!.Run);
        Assert.Empty(helpers);
        List<string> asked = Asked(custody);
        Assert.True(asked.Count >= 2, string.Join(" | ", asked));
        Assert.Contains(": assignment ", asked[0], StringComparison.Ordinal);
        Assert.Contains(": operation ", asked[1], StringComparison.Ordinal);
        EstateModel model = run.Estate.Load();
        Assert.Equal(run.OperatorDevice, Assert.Single(model.ById[second.Assignment!.Id].Signatures).Device);
    }

    [Fact]
    public void ARulesObservationKeepsItsOwnCheckThePersonsDecisionAskSharesOneAndGoalDecideKeepsOneEach()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, FlowDecisionRoutingTests.RuleThenRole(), body => FlowDecisionRoutingTests.Configured(body, op));
        FlowStepRoutingTests.Done(run, run.Service.GoalStep(goal, new GoalStepRequest(op)).Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), null), op);
        (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(run);

        // The rule route: goal step records that no rule held, a flow-decision observation outside § 10's allow-list, a single write.
        GoalStepResponse kind = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(kind.Ok, kind.Message);
        Assert.Equal(("decided", "gate"), (kind.Outcome, kind.Step));
        Assert.Empty(helpers);
        Assert.Contains(": observation ", Assert.Single(Asked(custody)), StringComparison.Ordinal);
        Assert.Equal(run.OperatorDevice, Assert.Single(run.Estate.Load().ById[kind.Decision!.Id].Signatures).Device);

        // The person route of the decision step (PROC-19): its assignment and its ask, listed by exact hash, share one check.
        GoalStepResponse asked = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "gate", "person"), (asked.Outcome, asked.Step, asked.Route));
        PresenceHelperStandIn helper = Assert.Single(helpers);
        PresenceSessionRules terms = PresenceSessionRules.ReadTerms(Assert.Single(helper.Terms));
        EstateModel model = run.Estate.Load();
        Assert.Equal("goal step", terms.Action);
        Assert.Equal([model.ById[asked.Assignment!.Id].ComputedHash, model.ById[asked.Ask!.Id].ComputedHash], terms.Entries.Select(e => e.Hash));
        Assert.Single(Asked(custody));

        // goal decide is outside the action vocabulary: the attempt and the decision it stores together each keep their own check.
        WriteOutcome decided = run.Service.GoalDecide(goal, new GoalDecideRequest(op, "gate", ["ship"], "No rule settled it; ship."));
        Assert.True(decided.Ok, decided.Problem?.Message);
        Assert.Single(helpers);
        List<string> all = Asked(custody);
        Assert.Equal(3, all.Count);
        Assert.Contains(": attempt ", all[1], StringComparison.Ordinal);
        Assert.Contains(": result ", all[2], StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAttentionAskAndAnOperatorRequestAreSingleWritesThatKeepTheirOwnCheck()
    {
        using (var run = new RunEstate(recordFormat: 3, keepOperatorKey: true))
        {
            string op = run.Estate.OperatorId;
            string goal = FlowStepRoutingTests.Run(run, FlowTimingRoutingTests.Song(new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-01-01T09:00:00Z" }, ["deadline"] = "PT1H" }));
            (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(run);

            GoalStepResponse attention = run.Service.GoalStep(goal, new GoalStepRequest(op));

            Assert.True(attention.Ok, attention.Message);
            Assert.Equal(("ask", "attention"), (attention.Outcome, attention.Route));
            Assert.Empty(helpers);
            Assert.Contains(": ask ", Assert.Single(Asked(custody)), StringComparison.Ordinal);
        }

        using (var run = new RunEstate(recordFormat: 3, keepOperatorKey: true))
        {
            string op = run.Estate.OperatorId;
            string goal = FlowStepRoutingTests.Run(run, change: FlowStopRoutingTests.Options);
            (_, _, RecordWritten master) = FlowStopRoutingTests.ToAcceptance(run, goal);
            FlowStepRoutingTests.Review(run, master);
            (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(run);

            GoalStepResponse request = run.Service.GoalStep(goal, new GoalStepRequest(op));

            Assert.True(request.Ok, request.Message);
            Assert.Equal(("request", "request"), (request.Outcome, request.Route));
            Assert.Empty(helpers);
            Assert.Contains(": operator-request ", Assert.Single(Asked(custody)), StringComparison.Ordinal);
        }
    }

    // The operator's device needs presence, held by custody that records each check it asks; sessions open on the embedded profile, each
    // helper a stand-in that signs with the device's file key.
    private static (List<PresenceHelperStandIn> Helpers, RecordingCustody Custody) Presence(RunEstate run)
    {
        var custody = new RecordingCustody(VericairnHome.Path());
        var helpers = new List<PresenceHelperStandIn>();
        run.Service.KeyStores = [custody];
        run.Service.PresenceRequired = device => device == run.OperatorDevice;
        run.Service.HostsEmbedded = true;
        run.Service.PresenceHost = StandInSessions.Host(helpers);
        return (helpers, custody);
    }

    // The checks custody asked with a reason: each signature that needs presence outside a session.
    private static List<string> Asked(RecordingCustody custody) => [.. custody.Reasons.OfType<string>()];

    private static void Order(RunEstate run, string key, string role) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["work"],"role":"{{role}}"}"""))));
}
