// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// GOAL-11's exit question (design/events-and-presence.md §§ 9 and 10): routing passes over an away person and a lapsed session, and a
/// person can hand a lapsed step to another principal of its role. Lease time is a controlled clock moved by hand; nothing sleeps.
/// </summary>
[Collection("home")]
public sealed class AvailabilityRoutingTests
{
    [Fact]
    public void AvailabilityAndQuietHoursLiveOnAPersonsOwnScopeFromRecordFormatThree()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string pat = Person(run, "Pat");
        string until = Later(run, TimeSpan.FromDays(2));
        var away = new CommunicationValues { Availability = new CommunicationAvailability("away", until) };
        var quiet = new QuietHoursSetting("Europe/Berlin", [new QuietHoursWindow(["mon", "tue", "wed", "thu", "fri"], "19:00", "08:00")]);

        RunEstate.Ok(run.Service.CommunicationSet(new CommunicationSetRequest(pat, "person", away with { QuietHours = quiet }, pat)));
        CommunicationProfile profile = run.Service.Communication(pat);
        Assert.Null(profile.Unavailable);
        Assert.Equal(("away", until), (profile.Availability!.State, profile.Availability.Until));
        Assert.Equal("Europe/Berlin", profile.QuietHours!.Zone);
        Assert.True(profile.AvailabilityNow!.Away);
        Assert.Contains($"availability: away until {until}; away now", CommunicationPresentation.Describe(profile), StringComparison.Ordinal);
        Assert.Equal(until, Routing.AwayUntil(run.Estate.Load(), pat, DateTimeOffset.UtcNow));

        // Neither belongs to estate defaults, away needs its until, and a window's start and end differ.
        Assert.False(run.Service.CommunicationSet(new CommunicationSetRequest(run.Estate.OperatorId, "estate", away)).Ok);
        Assert.False(run.Service.CommunicationSet(new CommunicationSetRequest(pat, "person", new CommunicationValues { Availability = new CommunicationAvailability("away") }, pat, Expected: Hash(profile))).Ok);
        Assert.False(run.Service.CommunicationSet(new CommunicationSetRequest(pat, "person", new CommunicationValues { QuietHours = quiet with { Windows = [new QuietHoursWindow(["mon"], "09:00", "09:00")] } }, pat, Expected: Hash(profile))).Ok);

        // A record carrying them directly is judged the same way: an assignment override is refused.
        WriteOutcome overriding = run.Service.NewRecord(new NewRecordRequest("communication-setting", pat, RunEstate.Json($$$$"""{"scope":"assignment","recipient":"{{{{pat}}}}","assignment":"{{{{run.Assignment}}}}","values":{"availability":{"state":"available"}}}""")));
        Assert.Contains(overriding.Problem!.Findings, f => f.Code == "communication.availability");

        // Back again: an available person routes as before.
        RunEstate.Ok(run.Service.CommunicationSet(new CommunicationSetRequest(pat, "person", new CommunicationValues { Availability = new CommunicationAvailability("available") }, pat, Expected: Hash(profile))));
        Assert.Null(Routing.AwayUntil(run.Estate.Load(), pat, DateTimeOffset.UtcNow));
        Assert.Equal(0, run.Service.Check().Errors);

        // A format-2 estate refuses both, since older readers refuse the fields.
        using var older = new RunEstate(keepOperatorKey: true);
        WriteOutcome refused = older.Service.CommunicationSet(new CommunicationSetRequest(older.Estate.OperatorId, "person", away, older.Estate.OperatorId));
        Assert.Contains("record format 3", refused.Problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutingPassesOverAnAwayPersonAndAsksThemOnlyWhenNoOtherPrincipalOfTheRoleIsAvailable()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string pat = Person(run, "Pat");
        string quinn = Person(run, "Quinn");
        Roster(run, ("reviewer", [pat, quinn]), ("checker", [pat]));
        Order(run, "W-3", "reviewer");
        Order(run, "W-4", "checker");
        string until = Later(run, TimeSpan.FromDays(3));
        RunEstate.Ok(run.Service.CommunicationSet(new CommunicationSetRequest(pat, "person", new CommunicationValues { Availability = new CommunicationAvailability("away", until) }, pat)));

        // Pat is away: the reviewer step passes over Pat to Quinn, and says why Pat was passed over.
        JsonElement review = Route(run, "W-3");
        Assert.Equal(("person", quinn), (review.GetProperty("state").GetString(), review.GetProperty("principal").GetString()));
        JsonElement passed = Assert.Single(review.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("principal").GetString() == pat);
        Assert.Equal((false, until), (passed.GetProperty("available").GetBoolean(), passed.GetProperty("awayUntil").GetString()));
        Assert.Contains($"away until {until}, as their communication setting declares", passed.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        // Only Pat holds the checker role: the step still goes to Pat and is asked of them, shown as away until their return.
        JsonElement check = Route(run, "W-4");
        Assert.Equal(("person", pat), (check.GetProperty("state").GetString(), check.GetProperty("principal").GetString()));
        Assert.EndsWith($"Pat is away until {until}: the step is still assigned to them and asked, shown as away until their return", check.GetProperty("reason").GetString(), StringComparison.Ordinal);
        RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", quinn, op, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource }));
        Assert.True(run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op)).Ok);
        GoalStepResponse asked = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "W-4", pat), (asked.Outcome, asked.Step, asked.Principal));
        Assert.EndsWith($"Pat is away until {until}", Route(run, "W-4").GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ALiveSessionIsGivenAStepThatReadsNothingAndNoAttemptWithinFiveMinutesCountsAsALapse()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);
        Roster(run, ("builder", [builder]));
        Order(run, "W-2", "builder");
        // A record a service principal wrote is outside-derived as an agent's is: a step naming it reads.
        string service = Service(run, "Test Service");
        string written = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("ask", service, RunEstate.Json($$"""{"to":"{{op}}","question":"Is the widget's schema current?","recommendation":"Read it.","consequence":"Nothing waits.","state":"open"}""")))).Id;
        Order(run, "W-3", "builder", ("ask", written));

        // No launcher here starts the builder's harness, so without a session no agent carries a step.
        Assert.Equal("unavailable", Route(run, "W-2").GetProperty("state").GetString());

        // A live session offering the builder role: the builder's held step goes on with it, and W-2 is given to it. The attempt recorded
        // before the session registered is not the session's.
        PresenceLease lease = run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session", ["builder"], Capacity: 3)).Lease!;
        Assert.Empty(Assert.Single(run.Service.Presence().Sessions).Holds);
        JsonElement offered = Route(run, "W-2");
        Assert.Equal(("agent", builder, lease.Session), (offered.GetProperty("state").GetString(), offered.GetProperty("principal").GetString(), offered.GetProperty("session").GetString()));
        GoalStepResponse kept = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource));
        Assert.True(kept.Ok, kept.Message);
        Assert.Equal(("session", "W-1", null), (kept.Outcome, kept.Step, kept.Assignment));
        Assert.Equal([run.Assignment], Assert.Single(run.Service.Presence().Sessions).Holds);
        Assert.Equal("assigned", Route(run, "W-1").GetProperty("state").GetString());
        GoalStepResponse routed = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource));
        Assert.True(routed.Ok, routed.Message);
        Assert.Equal(("session", "W-2"), (routed.Outcome, routed.Step));
        RecordEnvelope assignment = run.Estate.Load().ById[routed.Assignment!.Id];
        Assert.Equal(("session", builder), (EstateModel.BodyString(assignment, "routedWith"), EstateModel.BodyString(assignment, "contributor")));
        Assert.Equal([run.Assignment, routed.Assignment.Id], Assert.Single(run.Service.Presence().Sessions).Holds);
        Assert.Equal("assigned", Route(run, "W-2").GetProperty("state").GetString());

        // A step whose exposure reads (an input a service principal wrote) is never routed to the session.
        JsonElement reading = Route(run, "W-3");
        Assert.Equal("unavailable", reading.GetProperty("state").GetString());
        Assert.Contains(Assert.Single(reading.GetProperty("candidates").EnumerateArray()).GetProperty("reasons").EnumerateArray(),
            r => r.GetString()!.Contains("takes no step whose exposure reads", StringComparison.Ordinal) && r.GetString()!.Contains($"(ask {written}) was written by Test Service, which is no person", StringComparison.Ordinal));

        // No attempt within five minutes counts as a lapse of the live session: attention, a local observation.
        clock.Advance(TimeSpan.FromSeconds(PresenceVocabulary.SessionAttemptSeconds - 1));
        Assert.True(run.Service.PresenceHeartbeat(lease.Lease).Ok);
        run.Service.SweepPresence();
        Assert.DoesNotContain(run.Service.AttentionGet(op).Items, i => i.SubjectId == routed.Assignment.Id);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(run.Service.PresenceHeartbeat(lease.Lease).Ok);
        run.Service.SweepPresence();
        AttentionNotice lapse = Assert.Single(run.Service.AttentionGet(op).Items, i => i.SubjectId == routed.Assignment.Id);
        Assert.Contains("no attempt followed within 300 seconds", lapse.Why, StringComparison.Ordinal);

        // Its attempt ends the lapse.
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(routed.Assignment.Id, builder, "widget, a work branch", "Start.")));
        run.Service.SweepPresence();
        Assert.DoesNotContain(run.Service.AttentionGet(op).Items, i => i.SubjectId == routed.Assignment.Id);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void APersonHandsALapsedStepToAnotherPrincipalOfItsRoleCitingTheLapseAsk()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = Agent(run, "Test Helper");
        string stranger = Agent(run, "Test Stranger");
        Roster(run, ("builder", [builder, helper]));
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);
        run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session"));
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;
        clock.Advance(TimeSpan.FromSeconds(90 + PresenceVocabulary.DefaultReassignAfterSeconds));
        run.Service.SweepPresence();
        string ask = Assert.Single(run.Service.Records("ask")).Id;
        int assignments = run.Service.Records("assignment").Count;

        // To anyone but a person the step goes only citing the lapse ask, only to a principal of its role, and never by an agent.
        Assert.Contains("is not a person", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper)).Message, StringComparison.Ordinal);
        Assert.Contains("not a principal the roster names for the step's role, builder", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, stranger) { Basis = ask }).Message, StringComparison.Ordinal);
        Assert.Contains("does not record this hand-over", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, builder, helper) { Basis = ask }).Message, StringComparison.Ordinal);
        Assert.Contains("is already Test Builder's", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, builder) { Basis = ask }).Message, StringComparison.Ordinal);
        Assert.Contains("is no standing lapse ask about assignment", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper) { Basis = run.Goal }).Message, StringComparison.Ordinal);
        Assert.Equal(assignments, run.Service.Records("assignment").Count);

        // The operator hands it to the helper, citing the ask: the hand-over records its basis, and the builder's work no longer counts.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper) { Basis = ask });
        Assert.True(taken.Ok, taken.Message);
        RecordEnvelope handover = run.Estate.Load().ById[taken.Assignment!.Id];
        Assert.Equal((helper, ask), (EstateModel.BodyString(handover, "contributor"), EstateModel.BodyString(handover, "basis")));
        Assert.EndsWith($"citing the lapse ask {ask}.", EstateModel.BodyString(handover, "note"), StringComparison.Ordinal);
        WriteOutcome late = run.Service.Result(new ResultRequest(attempt, builder, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""")));
        Assert.Contains("no longer holds its step", late.Problem!.Message, StringComparison.Ordinal);

        // A hand-over is a person's act: an agent's assignment of the held step, citing a basis or not, takes nothing and is refused.
        RecordEnvelope order = run.Estate.Load().ById[handover.Subject!.Id];
        var agents = new System.Text.Json.Nodes.JsonObject { ["contributor"] = stranger, ["brief"] = System.Text.Json.Nodes.JsonNode.Parse(RunEstate.Brief), ["basis"] = ask };
        WriteOutcome byAgent = run.Service.NewRecord(new NewRecordRequest("assignment", helper, RunEstate.Json(agents.ToJsonString()), $"work-order:{order.Id}:{order.ComputedHash}"));
        Assert.Contains("only a person hands a held step to another principal", byAgent.Problem!.Message, StringComparison.Ordinal);
        EstateModel model = run.Estate.Load();
        Assert.True(Routing.Displaced(model, model.ById[run.Assignment]));
        run.Service.SweepPresence();
        model = run.Estate.Load();
        Assert.Equal("withdrawn", Asks.State(model, model.ById[ask]).State);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ABasisIsAStandingLapseAskWhoseSessionIsStillLapsedAndTheLiveHoldersWorkStillCounts()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = Agent(run, "Test Helper");
        Roster(run, ("builder", [builder, helper]));
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);
        PresenceLease lease = run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session")).Lease!;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;
        clock.Advance(TimeSpan.FromSeconds(90 + PresenceVocabulary.DefaultReassignAfterSeconds));
        run.Service.SweepPresence();
        string ask = Assert.Single(run.Service.Records("ask")).Id;

        // The session's heartbeat restores the lease: before the sweep withdraws the ask it no longer stands, and after it is withdrawn.
        Assert.True(run.Service.PresenceHeartbeat(lease.Lease).Ok);
        Assert.Contains("no longer stands for a lapse", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper) { Basis = ask }).Message, StringComparison.Ordinal);
        run.Service.SweepPresence();
        Assert.Equal("live", Assert.Single(run.Service.Presence().Sessions).State);
        Assert.Contains("is no standing lapse ask about assignment", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper) { Basis = ask }).Message, StringComparison.Ordinal);

        // Written by hand, a hand-over citing the withdrawn ask is invalid.
        RecordEnvelope order = run.Estate.Load().ById[run.Estate.Load().ById[run.Assignment].Subject!.Id];
        var operatorSigner = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        RecordEnvelope forged = run.Estate.Add("assignment", op, Handover(helper, ask), order.Reference, signer: operatorSigner);
        Assert.Contains(run.Estate.Load().Findings, f => f.Code == "assignment.basis" && f.Location.Contains(forged.Id, StringComparison.Ordinal));
        File.Delete(Path.Combine(run.Estate.Root, forged.ExpectedPath));

        // The live holder's result still counts.
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, builder, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))));
        Assert.Equal("awaiting review", Assert.Single(WorkView.AssignmentItems(run.Estate.Load()), i => i.AssignmentId == run.Assignment).State);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepAnAgentDispatchedIsHandedOverCitingItsLapseAskAndTheDisplacedAgentStaysAnAuthor()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = Agent(run, "Test Helper");
        string third = Agent(run, "Test Third");
        Roster(run, ("builder", [helper, third]));
        Order(run, "W-2", "builder");
        GrantLocalEngineering(run, builder);
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);

        // The builder, an agent under local-engineering, records the step's first assignment, to the helper, whose session lapses.
        string dispatched = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-2", helper, builder, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        run.Service.PresenceRegister(new PresenceRegisterRequest(helper, "a helper session"));
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(dispatched, helper, "widget, a work branch", "Build it."))).Id;
        clock.Advance(TimeSpan.FromSeconds(90 + PresenceVocabulary.DefaultReassignAfterSeconds));
        run.Service.SweepPresence();
        RecordEnvelope ask = run.Estate.Load().ById[Assert.Single(run.Service.Records("ask")).Id];
        Assert.Equal(builder, ask.By);

        // The operator hands the step to the third agent of its role, citing the sweep's standing ask: recorded, and valid.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(dispatched, op, third) { Basis = ask.Id });
        Assert.True(taken.Ok, taken.Message);
        EstateModel model = run.Estate.Load();
        RecordEnvelope order = model.ById[model.ById[dispatched].Subject!.Id];
        Assert.Equal(taken.Assignment!.Id, Routing.HolderOf(model, model.ById[dispatched])!.Id);
        Assert.Contains("no longer holds its step", run.Service.Result(new ResultRequest(attempt, helper, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))).Problem!.Message, StringComparison.Ordinal);

        // The step's history keeps the displaced assignment, so the helper, which recorded an attempt, is still an author of its work.
        Assert.Equal([dispatched, taken.Assignment.Id], Routing.Assignments(model, order).Select(a => a.Id));
        Assert.Contains(helper, FlowRuns.Authors(model, Routing.Assignments(model, order)));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAskTheReceivingAgentWroteItselfIsNoBasisWithNoPresenceKept()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = Agent(run, "Test Helper");
        Roster(run, ("builder", [builder, helper]));
        RecordEnvelope assignment = run.Estate.Load().ById[run.Assignment];
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;

        // The helper writes an ask about the builder's assignment, even in the sweep's words; no presence is kept here.
        string own = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("ask", helper, RunEstate.Json($$"""{"to":"{{op}}","question":"Session 0123456789abcdef0123456789abcdef of Test Builder, which holds assignment {{run.Assignment}} of W-1, lapsed at 2026-11-21T12:00:00Z: its transport renewed its lease no more. Hand the step to another principal of its role?","recommendation":"Hand it to me.","consequence":"It waits.","state":"open"}"""), $"assignment:{assignment.Id}:{assignment.ComputedHash}"))).Id;
        TakeoverResponse refused = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op, helper) { Basis = own });
        Assert.False(refused.Ok);
        Assert.Contains("is no standing lapse ask about assignment", refused.Message, StringComparison.Ordinal);

        // Written by hand, a hand-over citing it is invalid; the builder's work still counts.
        RecordEnvelope order = run.Estate.Load().ById[assignment.Subject!.Id];
        var operatorSigner = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        RecordEnvelope forged = run.Estate.Add("assignment", op, Handover(helper, own), order.Reference, signer: operatorSigner);
        Assert.Contains(run.Estate.Load().Findings, f => f.Code == "assignment.basis" && f.Location.Contains(forged.Id, StringComparison.Ordinal));
        File.Delete(Path.Combine(run.Estate.Root, forged.ExpectedPath));
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, builder, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string Handover(string contributor, string basis) => new System.Text.Json.Nodes.JsonObject
    {
        ["contributor"] = contributor,
        ["brief"] = System.Text.Json.Nodes.JsonNode.Parse(RunEstate.Brief),
        ["basis"] = basis,
    }.ToJsonString();

    [Fact]
    public void QuietHoursHoldNotificationsUntilTheWindowEndsAndRemindersArriveAtItsStart()
    {
        var quiet = new QuietHoursSetting("Europe/Berlin",
            [new QuietHoursWindow(["mon", "tue", "wed", "thu", "fri"], "19:00", "08:00"), new QuietHoursWindow(["sat", "sun"], "00:00", "24:00")]);

        // Tuesday 24 November 2026, 21:00 in Berlin (UTC+1): inside the weeknight window, which ends at 08:00 on Wednesday.
        DateTimeOffset evening = DateTimeOffset.Parse("2026-11-24T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse("2026-11-25T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture), AvailabilityRules.DeliverAt(quiet, "information", evening));
        Assert.Equal(evening, AvailabilityRules.DeliverAt(quiet, "incident", evening));
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T18:00:00Z", System.Globalization.CultureInfo.InvariantCulture), AvailabilityRules.ReminderAt(quiet, evening));

        // Wednesday at noon nothing is held; Saturday the whole day is.
        DateTimeOffset noon = DateTimeOffset.Parse("2026-11-25T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(noon, AvailabilityRules.DeliverAt(quiet, "decision-needed", noon));
        Assert.Null(AvailabilityRules.QuietUntil(quiet, noon));
        DateTimeOffset saturday = DateTimeOffset.Parse("2026-11-28T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse("2026-11-28T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture), AvailabilityRules.QuietUntil(quiet, saturday));

        // A setting's own breakthrough replaces the default; a zone this machine does not know holds nothing.
        Assert.Equal(evening, AvailabilityRules.DeliverAt(quiet with { Breakthrough = ["decision-needed"] }, "decision-needed", evening));
        Assert.Equal(DateTimeOffset.Parse("2026-11-25T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture), AvailabilityRules.DeliverAt(quiet with { Breakthrough = ["decision-needed"] }, "incident", evening));
        Assert.Equal(evening, AvailabilityRules.DeliverAt(quiet with { Zone = "Nowhere/Unknown" }, "information", evening));

        // Away from its from until its until, and not after.
        var away = new CommunicationAvailability("away", "2026-12-04T09:00:00Z", "2026-12-01T00:00:00Z");
        Assert.False(AvailabilityRules.AwayAt(away, evening));
        Assert.True(AvailabilityRules.AwayAt(away, DateTimeOffset.Parse("2026-12-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(AvailabilityRules.AwayAt(away, DateTimeOffset.Parse("2026-12-04T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.NotNull(AvailabilityRules.Refusal(new CommunicationAvailability("away", "2026-12-04T09:00:00Z", "2026-12-05T00:00:00Z")));
        Assert.NotNull(AvailabilityRules.Refusal(quiet with { Breakthrough = ["urgent"] }));
    }

    private static string Later(RunEstate run, TimeSpan by) => RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.Add(by));

    private static string? Hash(CommunicationProfile profile) => profile.Revisions.Single(r => r.Current && r.Scope == "person").Hash;

    private static JsonElement Route(RunEstate run, string key) =>
        Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key).GetProperty("route");

    /// <summary>A person with an admitted device whose key this machine holds, so their own records are signed here.</summary>
    private static string Person(RunEstate run, string name)
    {
        string person = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", run.Estate.OperatorId, RunEstate.Json($$"""{"kind":"human","name":"{{name}}","roles":["contributor"]}""")))).Id;
        string device = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(person, name + "'s laptop", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(device, run.Estate.OperatorId, "Signs " + name + "'s own records.")));
        return person;
    }

    /// <summary>An agent principal of a product no launcher here starts, with an admitted device whose key this machine holds.</summary>
    private static string Agent(RunEstate run, string name)
    {
        string agent = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", run.Estate.OperatorId, RunEstate.Json($$$"""{"kind":"agent","name":"{{{name}}}","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        string device = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(agent, name + "'s device", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(device, run.Estate.OperatorId, "Signs " + name + "'s records.")));
        return agent;
    }

    private static void Roster(RunEstate run, params (string Role, string[] Principals)[] roles) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId, RunEstate.Json(JsonSerializer.Serialize(new
        {
            roles = roles.Select(r => new { role = r.Role, principals = r.Principals }),
        })))));

    /// <summary>The operator's grant of local-engineering to a principal, on a decision of the operator's.</summary>
    private static void GrantLocalEngineering(RunEstate run, string grantee)
    {
        string op = run.Estate.OperatorId;
        string decision = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json($$"""{"title":"A grant","statement":"Grant local-engineering.","summary":"A grant.","synthesis":"A grant of local-engineering.","effects":["local-engineering"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T12:00:00Z","stake":"medium","notAuthorized":[]}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("authority", op, RunEstate.Json($$"""{"grantee":"{{grantee}}","scope":["local-engineering"],"reserved":[],"conditions":[],"basis":"{{decision}}"}"""))));
    }

    /// <summary>A service principal with an admitted device whose key this machine holds.</summary>
    private static string Service(RunEstate run, string name)
    {
        string service = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", run.Estate.OperatorId, RunEstate.Json($$"""{"kind":"service","name":"{{name}}","roles":["contributor"]}""")))).Id;
        string device = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(service, name + "'s device", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(device, run.Estate.OperatorId, "Signs " + name + "'s records.")));
        return service;
    }

    private static void Order(RunEstate run, string key, string role, (string Type, string Id)? input = null)
    {
        string inputs = $$"""{"type":"goal","id":"{{run.Goal}}"}""" + (input is not { } named ? string.Empty : $$""",{"type":"{{named.Type}}","id":"{{named.Id}}"}""");
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{{inputs}}],"deliverables":["work"],"role":"{{role}}"}"""))));
    }
}
