// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// ID-11's exit question (design/events-and-presence.md §§ 8 and 10): a silent session holding work is noticed and asked about, and
/// after a hand-over only the current holder's work counts. Every wait is a controlled clock moved by hand and a sweep run in the test;
/// nothing sleeps.
/// </summary>
[Collection("home")]
public sealed class PresenceLeaseTests
{
    private const string ResultBody = """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""";

    [Fact]
    public void ALeaseIsRenewedByItsHolderAndAPersonIsNeverTracked()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        SteppedClock clock = Present(run);
        string builder = run.Estate.BuilderId;

        PresenceLeaseResponse registered = run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session", ["builder"]));
        Assert.True(registered.Ok, registered.Message);
        PresenceLease lease = registered.Lease!;
        Assert.Equal((builder, PresenceVocabulary.DefaultLeaseSeconds, 30, 1), (lease.Principal, lease.LeaseSeconds, lease.HeartbeatSeconds, lease.Capacity));
        Assert.Equal(At(clock, 90), lease.ExpiresAt);

        // Its transport renews it: the lease runs a full lease from each heartbeat.
        clock.Advance(TimeSpan.FromSeconds(60));
        PresenceLeaseResponse renewed = run.Service.PresenceHeartbeat(lease.Lease);
        Assert.True(renewed.Ok, renewed.Message);
        Assert.Equal(At(clock, 90), renewed.Lease!.ExpiresAt);
        clock.Advance(TimeSpan.FromSeconds(60));
        run.Service.SweepPresence();
        PresenceEntry entry = Assert.Single(run.Service.Presence().Sessions);
        Assert.Equal((lease.Session, "live", "a builder session"), (entry.Session, entry.State, entry.Label));

        // No listing names the lease identity, and the live store keeps only its digest.
        Assert.DoesNotContain(lease.Lease, JsonSerializer.Serialize(run.Service.Presence(), ProtocolJson.Options), StringComparison.Ordinal);
        Assert.DoesNotContain(lease.Lease, run.Service.Session.Live.Meta("presence:leases")!, StringComparison.Ordinal);

        // A person is never tracked by a connection, a label names no path, and an unknown lease identity renews nothing.
        Assert.Equal("refused", run.Service.PresenceRegister(new PresenceRegisterRequest(run.Estate.OperatorId, "the operator")).Problem!.Code);
        Assert.Equal("refused", run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "/home/someone/clone")).Problem!.Code);
        Assert.Equal("refused", run.Service.PresenceRegister(new PresenceRegisterRequest(builder, LeaseSeconds: 10)).Problem!.Code);
        Assert.Equal("not-found", run.Service.PresenceHeartbeat(PresenceLeaseState.NewLease()).Problem!.Code);

        // A release ends the session, and is no lapse.
        Assert.True(run.Service.PresenceRelease(lease.Lease).Ok);
        Assert.Empty(run.Service.Presence().Sessions);
        Assert.Equal("not-found", run.Service.PresenceHeartbeat(lease.Lease).Problem!.Code);
    }

    [Fact]
    public void ASilentSessionHoldingWorkIsNoticedThenAskedAboutAndAHeartbeatWithdrawsTheAsk()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        SteppedClock clock = Present(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        (EventSubscription presence, IDisposable listening) = Follow(run, "presence");
        using IDisposable _ = listening;
        PresenceLease lease = run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session")).Lease!;

        // The session records an attempt on its assignment through the runtime, so it holds the assignment.
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget.")));
        run.Service.SweepPresence();
        Assert.Equal([run.Assignment], Assert.Single(run.Service.Presence().Sessions).Holds);
        Assert.Null(Lapse(run, op));

        // Silence past its lease: a presence event and attention, labeled a local observation, and no ask yet.
        clock.Advance(TimeSpan.FromSeconds(91));
        run.Service.SweepPresence();
        PresenceEntry lapsed = Assert.Single(run.Service.Presence().Sessions);
        Assert.Equal(("lapsed", At(clock, -1)), (lapsed.State, lapsed.ExpiresAt));
        AttentionNotice noticed = Lapse(run, op)!;
        Assert.Equal(("incident", "lapsed"), (noticed.Kind, noticed.State));
        Assert.Contains("A local observation of this runtime", noticed.Why, StringComparison.Ordinal);
        EventEnvelope raised = Assert.Single(Read(run, presence));
        Assert.Equal(("presence", "raised", "session", op, "incident"), (raised.Kind, raised.Change, raised.Subject.Type, raised.To, raised.Category));
        Assert.Contains(raised.Sources, s => s.Id == run.Assignment);
        Assert.Empty(run.Service.Records("ask"));

        // Still lapsed one second short of reassignAfterSeconds, nothing is asked; at it, the goal's accountable principal is asked.
        clock.Advance(TimeSpan.FromSeconds(PresenceVocabulary.DefaultReassignAfterSeconds - 2));
        run.Service.SweepPresence();
        Assert.Empty(run.Service.Records("ask"));
        clock.Advance(TimeSpan.FromSeconds(1));
        run.Service.SweepPresence();
        RecordEnvelope ask = run.Estate.Load().ById[Assert.Single(run.Service.Records("ask")).Id];
        Assert.Equal((op, run.Goal, "open"), (EstateModel.BodyString(ask, "to"), EstateModel.BodyString(ask, "goal"), EstateModel.BodyString(ask, "state")));
        Assert.Equal(("assignment", run.Assignment), (ask.Subject!.Type, ask.Subject.Id));
        Assert.Contains("Hand the step to another principal of its role?", EstateModel.BodyString(ask, "question"), StringComparison.Ordinal);
        Assert.Equal("asked", Lapse(run, op)!.State);
        Assert.Equal([ask.Id], Assert.Single(run.Service.Presence().Sessions).Asks);
        Assert.Equal("changed", Assert.Single(Read(run, presence)).Change);

        // A heartbeat first restores the lease, and the next sweep withdraws the ask by its asker; the lapse's event clears.
        PresenceLeaseResponse restored = run.Service.PresenceHeartbeat(lease.Lease);
        Assert.True(restored.Ok, restored.Message);
        Assert.Contains("live again", restored.Message, StringComparison.Ordinal);
        run.Service.SweepPresence();
        EstateModel model = run.Estate.Load();
        Assert.Equal("withdrawn", Asks.State(model, model.ById[ask.Id]).State);
        Assert.Null(Lapse(run, op));
        Assert.Equal("cleared", Assert.Single(Read(run, presence)).Change);
        Assert.Equal("live", Assert.Single(run.Service.Presence().Sessions).State);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheRosterSetsHowLongALapseStandsBeforeItIsAskedAboutAndOnlyFromRecordFormatThree()
    {
        static JsonElement Roster(string builder, int seconds) =>
            RunEstate.Json($$$$"""{"roles":[{"role":"builder","principals":["{{{{builder}}}}"]}],"limits":{"watch":{"reassignAfterSeconds":{{{{seconds}}}}}}}""");
        using (var older = new RunEstate())
        {
            WriteOutcome refused = older.Service.NewRecord(new NewRecordRequest("roster", older.Estate.OperatorId, Roster(older.Estate.BuilderId, 120)));
            Assert.Contains(refused.Problem!.Findings, f => f.Code == "body.format" && f.Message.Contains("'reassignAfterSeconds' belongs to record format 3", StringComparison.Ordinal));
        }

        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Assert.Contains(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId, Roster(run.Estate.BuilderId, 59))).Problem!.Findings,
            f => f.Location.EndsWith("reassignAfterSeconds", StringComparison.Ordinal));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId, Roster(run.Estate.BuilderId, 120))));
        Assert.Equal(120, Routing.ReassignAfterSeconds(run.Estate.Load(), run.Estate.Load().ById[run.Goal]));

        SteppedClock clock = Present(run);
        run.Service.PresenceRegister(new PresenceRegisterRequest(run.Estate.BuilderId, "a builder session"));
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget, a work branch", "Build the widget.")));
        clock.Advance(TimeSpan.FromSeconds(90 + 119));
        run.Service.SweepPresence();
        Assert.Empty(run.Service.Records("ask"));
        clock.Advance(TimeSpan.FromSeconds(1));
        run.Service.SweepPresence();
        Assert.Single(run.Service.Records("ask"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AfterAHandOverTheDisplacedHoldersWritesAreRefusedWhateverTheirDateAndOnlyTheCurrentHoldersCount()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        SteppedClock clock = Present(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        PresenceLease lease = run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "a builder session")).Lease!;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;
        Assert.True(run.Service.WorkProgress(new WorkProgressRequest(attempt, builder, "build", "Building.")).Ok);
        clock.Advance(TimeSpan.FromSeconds(91 + PresenceVocabulary.DefaultReassignAfterSeconds));
        run.Service.SweepPresence();
        string ask = Assert.Single(run.Service.Records("ask")).Id;

        // The operator takes the step over: the hand-over displaces the builder's assignment.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);
        string handover = taken.Assignment!.Id;
        EstateModel model = run.Estate.Load();
        Assert.True(Routing.Displaced(model, model.ById[run.Assignment]));
        Assert.Equal(handover, Routing.HolderOf(model, model.ById[run.Assignment])!.Id);

        // The woken session's late attempt, progress claim and result are refused and nothing is written, whatever time they state.
        int records = run.Estate.Load().Records.Count;
        string backdated = RecordParser.FormatTimestamp(run.Estate.Load().ById[run.Assignment].AtTime);
        WriteOutcome lateAttempt = run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Carry on."));
        WriteOutcome backdatedAttempt = run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Carry on.", backdated));
        WorkProgressResponse lateProgress = run.Service.WorkProgress(new WorkProgressRequest(attempt, builder, "build", "Still building."));
        WriteOutcome lateResult = run.Service.Result(new ResultRequest(attempt, builder, RunEstate.Json(ResultBody)));
        WriteOutcome backdatedResult = run.Service.NewRecord(new NewRecordRequest("result", builder, RunEstate.Json(ResultBody), $"attempt:{attempt}:{model.ById[attempt].ComputedHash}", At: backdated));
        foreach (Problem? refused in new[] { lateAttempt.Problem, backdatedAttempt.Problem, lateProgress.Problem, lateResult.Problem, backdatedResult.Problem })
        {
            Assert.NotNull(refused);
            Assert.Contains($"Assignment {run.Assignment} no longer holds its step of W-1: assignment {handover} of Test Operator holds it now.", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(records, run.Estate.Load().Records.Count);

        // The current holder's attempt and result count; its result awaits review as the step's.
        string current = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(handover, op, "widget, a work branch", "Finish it."))).Id;
        string result = RunEstate.Ok(run.Service.Result(new ResultRequest(current, op, RunEstate.Json(ResultBody)))).Id;
        WorkView.AssignmentItem item = Assert.Single(WorkView.AssignmentItems(run.Estate.Load()), i => i.AssignmentId == handover);
        Assert.Equal((result, "awaiting review"), (item.LatestResultId, item.State));

        // The step was handed over, so the lapse ask is withdrawn and the lapsed session holds nothing.
        run.Service.SweepPresence();
        model = run.Estate.Load();
        Assert.Equal("withdrawn", Asks.State(model, model.ById[ask]).State);
        Assert.Empty(Assert.Single(run.Service.Presence().Sessions).Holds);
        Assert.Equal("not-found", run.Service.PresenceHeartbeat(PresenceLeaseState.NewLease()).Problem!.Code);
        Assert.True(run.Service.PresenceHeartbeat(lease.Lease).Ok);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AResultAnotherRuntimeRecordedBeforeTheHandOverReachedItIsLabeledDisplacedAndReviewRoutingSkipsIt()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);

        // The builder's own runtime recorded its result before the hand-over reached it; a pull brings it here, dated earlier.
        RecordEnvelope recorded = run.Estate.Load().ById[attempt];
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.BuilderDevice);
        run.Estate.SetClock(recorded.AtTime.AddSeconds(-1));
        RecordEnvelope pulled = run.Estate.Add("result", builder, ResultBody, recorded.Reference, signer: signer);
        Assert.DoesNotContain(run.Estate.Load().Findings, f => f.Location.Contains(pulled.Id, StringComparison.Ordinal) && f.Severity == Severity.Error);
        Assert.True(pulled.AtTime <= run.Estate.Load().ById[taken.Assignment!.Id].AtTime);

        // It is labeled displaced, never awaiting review: no review is asked of the operator, and the step stays with its holder.
        EstateModel model = run.Estate.Load();
        WorkView.AssignmentItem item = Assert.Single(WorkView.AssignmentItems(model), i => i.AssignmentId == run.Assignment);
        Assert.Equal((pulled.Id, WorkView.Displaced), (item.LatestResultId, item.State));
        Assert.DoesNotContain(run.Service.AttentionGet(op).Items, i => i.SubjectId == pulled.Id);
        Assert.Contains($"displaced: assignment {taken.Assignment!.Id} holds the step, so review routing skips it", WorkView.RenderObserve(model, WorkView.OrderOfAssignment(model, model.ById[run.Assignment])!), StringComparison.Ordinal);
        JsonElement route = Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "W-1").GetProperty("route");
        Assert.Equal(("assigned", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
    }

    [Fact]
    public void ADisplacedAgentNeverTakesItsStepBackByAssigningItToItselfAndAPersonsAssignmentDisplaces()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        Grant(run, builder, "local-engineering");
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget.")));
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);
        string handover = taken.Assignment!.Id;

        // The displaced agent, under local-engineering, cannot assign the step to itself or to another agent through the runtime.
        WriteOutcome itself = run.Service.Assign(new AssignRequest("W-1", builder, builder, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource });
        Assert.Contains("only a person hands a held step to another principal", itself.Problem!.Message, StringComparison.Ordinal);

        // Written by hand, its assignment takes nothing: the person's hand-over still holds the step, the person's attempt counts and the
        // agent's attempts on either assignment are refused.
        RecordEnvelope order = run.Estate.Load().ById[run.Estate.Load().ById[run.Assignment].Subject!.Id];
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.BuilderDevice);
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        var body = new System.Text.Json.Nodes.JsonObject { ["contributor"] = builder, ["brief"] = System.Text.Json.Nodes.JsonNode.Parse(RunEstate.Brief), ["note"] = "Taken back." };
        RecordEnvelope back = run.Estate.Add("assignment", builder, body.ToJsonString(), order.Reference, signer: signer);
        EstateModel model = run.Estate.Load();
        Assert.Equal(handover, Routing.HolderOf(model, model.ById[back.Id])!.Id);
        Assert.Equal(handover, Routing.HolderOf(model, model.ById[run.Assignment])!.Id);
        Assert.DoesNotContain(Routing.Assignments(model, order), a => a.Id == back.Id);
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(handover, op, "widget, a work branch", "Finish it.")));
        Assert.Contains($"Assignment {back.Id} holds nothing of its step of W-1", run.Service.Attempt(new AttemptRequest(back.Id, builder, "widget, a work branch", "Mine again.")).Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("no longer holds its step", run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Mine again.")).Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(WorkView.Displaced, Assert.Single(WorkView.AssignmentItems(model), i => i.AssignmentId == back.Id).State);
        JsonElement route = Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "W-1").GetProperty("route");
        Assert.Equal(("assigned", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));

        // A person's plain assignment of the step to another principal displaces without a basis: the operator, holding it, gives it away.
        string helper = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Helper","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        string given = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-1", helper, op, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        model = run.Estate.Load();
        Assert.Equal(given, Routing.HolderOf(model, model.ById[handover])!.Id);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ABackdatedAssignmentIsRefusedAndAPersonWhoGaveTheStepAwayTakesItOverAgain()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        Grant(run, builder, "local-engineering");
        RecordEnvelope first = run.Estate.Load().ById[run.Assignment];
        RecordEnvelope order = run.Estate.Load().ById[first.Subject!.Id];
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);

        // The displaced agent's assignment of the step to the person holding it, stating a time before the first assignment, is refused.
        var body = new System.Text.Json.Nodes.JsonObject { ["contributor"] = op, ["brief"] = System.Text.Json.Nodes.JsonNode.Parse(RunEstate.Brief) };
        WriteOutcome backdated = run.Service.NewRecord(new NewRecordRequest("assignment", builder, RunEstate.Json(body.ToJsonString()), $"work-order:{order.Id}:{order.ComputedHash}",
            At: RecordParser.FormatTimestamp(first.AtTime.AddSeconds(-1))));
        Assert.Contains("before the step's latest assignment", backdated.Problem!.Message, StringComparison.Ordinal);
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(taken.Assignment!.Id, op, "widget, a work branch", "Carry it.")));

        // The person gives the step to another agent of its role and takes it over again: the person's latest assignment holds it.
        string helper = Agent(run);
        string given = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-1", helper, op, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        TakeoverResponse again = run.Service.WorkTakeover(new TakeoverRequest(given, op));
        Assert.True(again.Ok, again.Message);
        EstateModel model = run.Estate.Load();
        Assert.Equal(again.Assignment!.Id, Routing.HolderOf(model, model.ById[given])!.Id);
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(again.Assignment.Id, op, "widget, a work branch", "Carry it again.")));
        Assert.Contains("no longer holds its step", run.Service.Attempt(new AttemptRequest(given, helper, "widget, a work branch", "Mine.")).Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ALateAttemptWrittenByHandLeavesTheDisplacedAssignmentDisplacedAndUnresolvedEffectsKeepTheirNotice()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, builder, "widget, a work branch", "Build the widget."))).Id;
        string result = RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, builder, RunEstate.Json("""{"outcome":"partial","summary":"Half.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":["A post may have been published; reconcile it under its original identity."],"nextStep":"Reconcile it."}""")))).Id;
        Assert.True(run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op)).Ok);

        // The earlier result reads displaced and asks no review, but its unresolved effect keeps its own notice for the operator.
        Assert.Equal(WorkView.Displaced, Assert.Single(WorkView.AssignmentItems(run.Estate.Load()), i => i.AssignmentId == run.Assignment).State);
        Assert.DoesNotContain(run.Service.AttentionGet(op).Items, i => i.SubjectId == result && i.Kind == "decision needed");
        Assert.Contains(run.Service.AttentionGet(op).Items, i => i.SubjectId == result && i.Kind == "uncertain-effect");

        // A late attempt the displaced holder's device signed and someone wrote by hand leaves the assignment displaced, not in progress.
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.BuilderDevice);
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        RecordEnvelope assignment = run.Estate.Load().ById[run.Assignment];
        run.Estate.Add("attempt", builder, """{"workspace":"widget, a work branch","note":"Carry on.","startedAt":"2026-11-21T12:00:00Z"}""", assignment.Reference, signer: signer);
        Assert.Equal(WorkView.Displaced, Assert.Single(WorkView.AssignmentItems(run.Estate.Load()), i => i.AssignmentId == run.Assignment).State);
    }

    [Fact]
    public void AHeartbeatRestoresALapsedLeaseOnlyWithinItsPrincipalsLimitAndNoPrincipalFillsTheRuntime()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        SteppedClock clock = Present(run);
        string builder = run.Estate.BuilderId;
        List<PresenceLease> first = [.. Enumerable.Range(0, 8).Select(i => run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "first " + i, LeaseSeconds: 30)).Lease!)];
        clock.Advance(TimeSpan.FromSeconds(31));
        run.Service.SweepPresence();
        List<PresenceLease> second = [.. Enumerable.Range(0, 8).Select(i => run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "second " + i, LeaseSeconds: 30)).Lease!)];
        Assert.All(second, Assert.NotNull);

        // Eight live already: a lapsed one of the first eight stays lapsed.
        PresenceLeaseResponse refused = run.Service.PresenceHeartbeat(first[0].Lease);
        Assert.Equal("unavailable", refused.Problem!.Code);
        Assert.Equal("lapsed", Assert.Single(run.Service.Presence().Sessions, s => s.Session == first[0].Session).State);
        Assert.Equal(8, run.Service.Presence(builder).Sessions.Count(s => s.State == "live"));

        // Lapsed leases holding nothing make room for the same principal's next ones, which never exceed twice its live limit in all.
        clock.Advance(TimeSpan.FromSeconds(31));
        run.Service.SweepPresence();
        for (int i = 0; i < 8; i++)
        {
            Assert.True(run.Service.PresenceRegister(new PresenceRegisterRequest(builder, "third " + i, LeaseSeconds: 30)).Ok);
        }

        Assert.Equal(16, run.Service.Presence(builder).Sessions.Count);
        Assert.True(run.Service.PresenceRegister(new PresenceRegisterRequest(Agent(run), "another agent's session")).Ok);
    }

    [Fact]
    public void AnAskTheAssignmentsAuthorCannotSignHereIsTriedAgainOnlyAfterABackOff()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        SteppedClock clock = Present(run);
        // Every ask write is refused here, as when the assignment's author signs nothing on this machine.
        run.Service.BeforeValidatingRecords = records =>
        {
            if (records.Any(r => r.Envelope.Type == "ask")) throw new InvalidOperationException("Refused for the test.");
        };
        string op = run.Estate.OperatorId;
        run.Service.PresenceRegister(new PresenceRegisterRequest(run.Estate.BuilderId, "a builder session"));
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget, a work branch", "Build the widget.")));
        clock.Advance(TimeSpan.FromSeconds(90 + PresenceVocabulary.DefaultReassignAfterSeconds));

        // Only the operator, who recorded the assignment, signs its lapse ask, and the write is refused: none is recorded, the attention
        // says so, and the next try waits a minute.
        run.Service.SweepPresence();
        Assert.Empty(run.Service.Records("ask"));
        string retry = RecordParser.FormatTimestamp(clock.GetUtcNow().AddSeconds(60));
        Assert.Contains($"No ask could be recorded here: only Test Operator, who recorded the assignment, signs its lapse ask, and the write was refused; it is tried again from {retry}.", Lapse(run, op)!.Why, StringComparison.Ordinal);
        clock.Advance(TimeSpan.FromSeconds(59));
        run.Service.SweepPresence();
        Assert.Contains($"tried again from {retry}.", Lapse(run, op)!.Why, StringComparison.Ordinal);

        // Past it, the sweep tries once more and then waits twice as long.
        clock.Advance(TimeSpan.FromSeconds(1));
        run.Service.SweepPresence();
        Assert.Contains($"tried again from {RecordParser.FormatTimestamp(clock.GetUtcNow().AddSeconds(120))}.", Lapse(run, op)!.Why, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("ask"));

        // Once the write is accepted, the next try records the ask, signed by the assignment's author.
        run.Service.BeforeValidatingRecords = null;
        clock.Advance(TimeSpan.FromSeconds(120));
        run.Service.SweepPresence();
        RecordEnvelope ask = run.Estate.Load().ById[Assert.Single(run.Service.Records("ask")).Id];
        Assert.Equal(run.Estate.Load().ById[run.Assignment].By, ask.By);
    }

    [Fact]
    public async Task TheTransportRenewsTheLeaseOnItsIntervalRegistersAgainWhenItIsGoneAndReleasesItAtTheEnd()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        using var clock = new TimerClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);
        var answers = new System.Collections.Concurrent.BlockingCollection<PresenceLeaseResponse>();
        PresenceLease first;
        await using (var keeper = new PresenceKeeper(run.Service, new PresenceRegisterRequest(run.Estate.BuilderId, "a kept session"), clock))
        {
            keeper.Answered += answers.Add;
            PresenceLeaseResponse registered = await keeper.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(registered.Ok, registered.Message);
            first = Take(answers).Lease!;

            // The keeper waits one heartbeat interval, a third of the lease, on the controlled clock; then it renews.
            await clock.WhenWaiting(TimeSpan.FromSeconds(first.HeartbeatSeconds));
            clock.Advance(TimeSpan.FromSeconds(first.HeartbeatSeconds));
            PresenceLeaseResponse renewed = Take(answers);
            Assert.Equal((first.Session, RecordParser.FormatTimestamp(clock.GetUtcNow().AddSeconds(first.LeaseSeconds))), (renewed.Lease!.Session, renewed.Lease.ExpiresAt));

            // A lease the runtime no longer keeps is registered again at the next interval.
            Assert.True(run.Service.PresenceRelease(first.Lease).Ok);
            await clock.WhenWaiting(TimeSpan.FromSeconds(first.HeartbeatSeconds));
            clock.Advance(TimeSpan.FromSeconds(first.HeartbeatSeconds));
            PresenceLeaseResponse again = Take(answers);
            Assert.True(again.Ok, again.Message);
            Assert.NotEqual(first.Session, again.Lease!.Session);
            Assert.Equal(again.Lease.Session, Assert.Single(run.Service.Presence().Sessions).Session);
        }

        // Disposal releases the lease: the session's end is no lapse.
        Assert.Empty(run.Service.Presence().Sessions);
    }

    /// <summary>The operator's grant of a scope to a principal, on a decision of the operator's.</summary>
    private static void Grant(RunEstate run, string grantee, string scope)
    {
        string op = run.Estate.OperatorId;
        string decision = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json($$"""{"title":"A grant","statement":"Grant {{scope}}.","summary":"A grant.","synthesis":"A grant of {{scope}}.","effects":["{{scope}}"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T12:00:00Z","stake":"medium","notAuthorized":[]}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("authority", op, RunEstate.Json($$"""{"grantee":"{{grantee}}","scope":["{{scope}}"],"reserved":[],"conditions":[],"basis":"{{decision}}"}"""))));
    }

    private static string Agent(RunEstate run) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", run.Estate.OperatorId, RunEstate.Json("""{"kind":"agent","name":"Another Agent","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;

    /// <summary>Keeps presence on the service as the daemon does, beside an event journal, judged by a clock the test moves.</summary>
    private static SteppedClock Present(RunEstate run)
    {
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        run.Service.EnableEvents(Path.Combine(run.Scratch, "live"), clock);
        return clock;
    }

    private static string At(TimeProvider clock, int seconds) => RecordParser.FormatTimestamp(clock.GetUtcNow().AddSeconds(seconds));

    private static AttentionNotice? Lapse(RunEstate run, string principal) =>
        run.Service.AttentionGet(principal).Items.SingleOrDefault(i => i.Kind == "incident" && i.SubjectType == "assignment" && i.SubjectId == run.Assignment);

    /// <summary>A subscription that listens, so each derivation pass covers its kind.</summary>
    private static (EventSubscription Subscription, IDisposable Listening) Follow(RunEstate run, string kind)
    {
        (EventSubscription? subscription, Problem? problem) = run.Service.OpenEvents(null, new EventFilter(Kinds: [kind]), run.Estate.OperatorId);
        Assert.Null(problem);
        return (subscription!, run.Service.ListenEvents(subscription!));
    }

    private static IReadOnlyList<EventEnvelope> Read(RunEstate run, EventSubscription subscription)
    {
        run.Service.DeriveEvents();
        return subscription.Read().Events;
    }

    private static PresenceLeaseResponse Take(System.Collections.Concurrent.BlockingCollection<PresenceLeaseResponse> answers)
    {
        Assert.True(answers.TryTake(out PresenceLeaseResponse? answer, TestTiming.HangTimeoutMilliseconds, TestContext.Current.CancellationToken), "the keeper did not answer");
        return answer!;
    }

    /// <summary>A clock whose timers fire only when the test moves it, and which says when a timer of a given length waits.</summary>
    private sealed class TimerClock(DateTimeOffset start) : TimeProvider, IDisposable
    {
        private readonly object sync = new();
        private readonly List<Timer> timers = [];
        private readonly SemaphoreSlim created = new(0);
        private long ticks;

        public override DateTimeOffset GetUtcNow() => start + TimeSpan.FromTicks(Interlocked.Read(ref ticks));

        public override long GetTimestamp() => Interlocked.Read(ref ticks);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            created.Release();
            return timer;
        }

        /// <summary>Waits until a timer of that length is set to fire.</summary>
        public async Task WhenWaiting(TimeSpan length)
        {
            while (true)
            {
                lock (sync)
                {
                    if (timers.Any(t => t.Due == GetTimestamp() + length.Ticks))
                    {
                        return;
                    }
                }

                Assert.True(await created.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken), "no timer was set");
            }
        }

        public void Dispose() => created.Dispose();

        public void Advance(TimeSpan by)
        {
            Interlocked.Add(ref ticks, by.Ticks);
            Timer[] due;
            lock (sync)
            {
                due = [.. timers.Where(t => t.Due <= GetTimestamp())];
                foreach (Timer timer in due)
                {
                    timer.Due = null;
                    timers.Remove(timer);
                }
            }

            foreach (Timer timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class Timer(TimerClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long? Due { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock.sync)
                {
                    clock.timers.Remove(this);
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetTimestamp() + dueTime.Ticks;
                    if (Due is not null)
                    {
                        clock.timers.Add(this);
                    }
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (clock.sync)
                {
                    clock.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
