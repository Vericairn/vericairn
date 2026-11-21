// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// The dispatcher and the dispatch scope (ID-07; design/standing-agents.md §§ 4 and 7, design/flow.md §§ 8 and 12, the lapse bound of
/// design/events-and-presence.md § 10): unattended dispatch is signed by a service principal that holds no other authority, under a
/// signed grant of dispatch alone that names the goals and standing agents it carries, caps its assignments a day and expires within 30
/// days; an assignment outside the grant's goals, cap or expiry, in a reserved role, or reassigning a held step outside the lapse bound
/// is refused.
/// </summary>
public sealed class DispatchAuthorityTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class Estate : IDisposable
    {
        public Estate()
        {
            F = new HistoryFixture();
            string op = F.Estate.OperatorId;
            Dispatcher = F.Estate.Add("principal", op, """{"kind":"service","name":"Dispatcher","roles":["service"]}""", signer: F.OperatorSigner);
            (DispatcherDevice, _) = F.Admitted(Dispatcher.Id, "The dispatcher's device");
            Signer = F.Signer(DispatcherDevice);
            Spare = F.Estate.Add("principal", op, """{"kind":"agent","name":"Spare Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""", signer: F.OperatorSigner);
            Person = F.Estate.Add("principal", op, """{"kind":"human","name":"Pat","roles":["contributor"]}""", signer: F.OperatorSigner);
            F.Estate.Add("roster", op, $$"""{"roles":[{"role":"builder","principals":["{{Spare.Id}}","{{F.Estate.BuilderId}}"]},{"role":"reviewer","principals":["{{Person.Id}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}""", signer: F.OperatorSigner);
            Goal = AddGoal("Build the widget");
            Other = AddGoal("Another goal");
            Build = Order("DSP-1", Goal, string.Empty);
            Review = Order("DSP-2", Goal, ""","role":"reviewer" """);
            Publish = Order("DSP-3", Goal, ""","role":"publisher" """);
            Elsewhere = Order("DSP-4", Other, string.Empty);
            Basis = F.Decision();
        }

        public HistoryFixture F { get; }

        public RecordEnvelope Dispatcher { get; }

        public RecordEnvelope DispatcherDevice { get; }

        public StoreSigner Signer { get; }

        public RecordEnvelope Spare { get; }

        public RecordEnvelope Person { get; }

        public RecordEnvelope Goal { get; }

        public RecordEnvelope Other { get; }

        public RecordEnvelope Build { get; }

        public RecordEnvelope Review { get; }

        public RecordEnvelope Publish { get; }

        public RecordEnvelope Elsewhere { get; }

        public RecordEnvelope Basis { get; }

        private RecordEnvelope AddGoal(string title)
        {
            string op = F.Estate.OperatorId;
            RecordEnvelope goal = F.Estate.Add("goal", op, $$"""{"title":"{{title}}","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{op}}","budget":{"wallMinutes":60,"attemptsPerStep":2},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""", signer: F.OperatorSigner);
            F.Estate.Add("goal-status", op, """{"status":"active","note":"Activated."}""", goal.Reference, signer: F.OperatorSigner);
            return goal;
        }

        private RecordEnvelope Order(string key, RecordEnvelope goal, string extra) =>
            F.Estate.Add("work-order", F.Estate.OperatorId, $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{goal.Id}}"}],"deliverables":["work"]{{extra}}}""", signer: F.OperatorSigner);

        public static JsonObject Bound(IEnumerable<string> goals, int cap = 2, bool? reassign = true)
        {
            var bound = new JsonObject { ["scope"] = "dispatch", ["goals"] = new JsonArray([.. goals.Select(g => (JsonNode)g)]), ["maxAssignmentsPerDay"] = cap };
            if (reassign is { } value) bound["reassign"] = value;
            return bound;
        }

        public JsonObject GrantBody(string? grantee = null, string[]? scopes = null, TimeSpan? term = null, JsonArray? bounds = null, string[]? conditions = null) =>
            new()
            {
                ["grantee"] = grantee ?? Dispatcher.Id, ["scope"] = new JsonArray([.. (scopes ?? ["dispatch"]).Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(),
                ["conditions"] = new JsonArray([.. (conditions ?? []).Select(c => (JsonNode)c)]), ["basis"] = Basis.Id,
                ["expires"] = RecordParser.FormatTimestamp(F.Estate.Clock.AddSeconds(1) + (term ?? TimeSpan.FromDays(20))),
                ["bounds"] = bounds ?? new JsonArray(Bound([Goal.Id])),
            };

        public RecordEnvelope Grant(JsonObject? body = null, bool sign = true) =>
            F.Estate.Add("authority", F.Estate.OperatorId, (body ?? GrantBody()).ToJsonString(), signer: sign ? F.OperatorSigner : null);

        public RecordEnvelope Assign(RecordEnvelope order, string contributor, IRecordSigner? signer = null, string? by = null) =>
            F.Estate.Add("assignment", by ?? Dispatcher.Id, Assignment(contributor), new RecordReference("work-order", order.Id, order.ComputedHash), signer: signer ?? Signer);

        public void Dispose() => F.Dispose();
    }

    private static JsonObject WithoutBounds(JsonObject body)
    {
        body.Remove("bounds");
        return body;
    }

    private const string Attempt = """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget, branch work/w-1","note":"Build it."}""";

    private const string Result = """{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""";

    private static string Assignment(string contributor) =>
        $$$"""{"contributor":"{{{contributor}}}","brief":{"outcome":"The work.","sources":[],"permittedChanges":["work"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["work"],"reviewCriteria":["done"],"recovery":"Record a result."}}""";

    private static Finding[] Errors(EstateModel model, RecordEnvelope record) =>
        [.. model.Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal))];

    private static Finding Refused(EstateModel model, RecordEnvelope record, string code = "assignment.dispatch") =>
        Assert.Single(Errors(model, record), f => f.Code == code);

    [Fact]
    public void ADispatcherAssignsAGoalsStepWithinItsGrantAndTakesItsSteps()
    {
        using var e = new Estate();
        RecordEnvelope grant = e.Grant();
        RecordEnvelope assigned = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, grant));
        Assert.Empty(Errors(model, assigned));
        Assert.True(model.Counts(assigned));
        Assert.True(Routing.MayTakeStep(model, model.ById[e.Goal.Id], e.Dispatcher.Id, e.F.Estate.Clock));
        Assert.False(Routing.MayTakeStep(model, model.ById[e.Other.Id], e.Dispatcher.Id, e.F.Estate.Clock));
        Assert.Contains(Scopes.Dispatch, Scopes.Known);
    }

    [Fact]
    public void AnAssignmentOutsideTheGrantsGoalsCapOrExpiryOrInAReservedRoleIsRefused()
    {
        using var e = new Estate();
        RecordEnvelope grant = e.Grant();
        RecordEnvelope elsewhere = e.Assign(e.Elsewhere, e.F.Estate.BuilderId);
        RecordEnvelope publish = e.Assign(e.Publish, e.F.Estate.OperatorId);
        RecordEnvelope first = e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope second = e.Assign(e.Review, e.Person.Id);
        RecordEnvelope third = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("a step of no goal", Refused(model, elsewhere).Message, StringComparison.Ordinal);
        Assert.Contains("reserved action", Refused(model, publish).Message, StringComparison.Ordinal);
        // The cap counts every assignment the dispatcher dated that UTC day, refused or not: the third and later are refused.
        Assert.Contains("caps dispatch at 2", Refused(model, first).Message, StringComparison.Ordinal);
        Assert.Contains("caps dispatch at 2", Refused(model, second).Message, StringComparison.Ordinal);
        Assert.Contains("caps dispatch at 2", Refused(model, third).Message, StringComparison.Ordinal);

        // The next day the cap starts again; after the grant expires, and before it was made, nothing is in force.
        e.F.Estate.SetClock(Start.AddDays(1).Date.AddHours(1));
        RecordEnvelope nextDay = e.Assign(e.Build, e.F.Estate.BuilderId);
        e.F.Estate.SetClock(e.F.Estate.Clock.AddDays(25));
        RecordEnvelope expired = e.Assign(e.Build, e.F.Estate.BuilderId);
        e.F.Estate.SetClock(grant.AtTime.AddSeconds(-30));
        RecordEnvelope early = e.Assign(e.Build, e.F.Estate.BuilderId);
        model = e.F.Load();
        Assert.Empty(Errors(model, nextDay));
        Assert.Contains("no dispatch grant in force", Refused(model, expired).Message, StringComparison.Ordinal);
        // A dispatcher with no history before its grant is one at every date: an assignment it dates before the grant is outside any
        // dispatch grant in force then, and counts for nothing.
        Assert.Contains("no dispatch grant in force", Refused(model, early).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(model.ById[early.Id]));
        Assert.False(Routing.MayTakeStep(model, model.ById[e.Goal.Id], e.Dispatcher.Id, Start.AddDays(30)));
    }

    [Fact]
    public void WithoutADispatchGrantAServicePrincipalAssignsNothing()
    {
        using var e = new Estate();
        RecordEnvelope assigned = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Refused(model, assigned, "assignment.author");
        Assert.False(Routing.MayTakeStep(model, model.ById[e.Goal.Id], e.Dispatcher.Id, e.F.Estate.Clock));
    }

    [Fact]
    public void AStepAnotherAssignmentHoldsIsReassignedOnlyWithinTheLapseBound()
    {
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope again = e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope toItself = e.Assign(e.Build, e.Dispatcher.Id);
        RecordEnvelope toSpare = e.Assign(e.Build, e.Spare.Id);
        RecordEnvelope toPerson = e.Assign(e.Build, e.Person.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("not the holder it replaces", Refused(model, again).Message, StringComparison.Ordinal);
        Assert.Contains("never assigns a step to itself", Refused(model, toItself).Message, StringComparison.Ordinal);
        // A person takes a step over by a hand-over they record, never by dispatch.
        Refused(model, toPerson, "assignment.handover");
        // The roster's next principal for the role takes it.
        Assert.Empty(Errors(model, toSpare));

        // A step whose role the roster gives a person is never reassigned by dispatch; a step whose holder settled it is not held.
        e.Assign(e.Review, e.Spare.Id);
        RecordEnvelope review = e.Assign(e.Review, e.F.Estate.BuilderId);
        model = e.F.Load();
        Assert.Contains("gives role reviewer to a person", Refused(model, review).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStepAPersonHoldsIsNeverReassignedByDispatch()
    {
        // An operator assigns a step to a person, though the roster names only agents for its role: dispatch never takes it over.
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        e.Assign(e.Build, e.Person.Id, e.F.OperatorSigner, e.F.Estate.OperatorId);
        RecordEnvelope toSpare = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("a step a person holds is never reassigned", Refused(model, toSpare).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADispatcherNeverAssignsAStepToItselfAndWhatItWorksCountsForNothing()
    {
        // A step no one holds, assigned by the dispatcher to itself: refused, and its own attempt and result on it count for nothing.
        using var e = new Estate();
        e.Grant();
        RecordEnvelope assigned = e.Assign(e.Build, e.Dispatcher.Id);
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, assigned.Reference, signer: e.Signer);
        RecordEnvelope result = e.F.Estate.Add("result", e.Dispatcher.Id, Result, attempt.Reference, signer: e.Signer);
        EstateModel model = e.F.Load();
        Assert.Contains("never assigns a step to itself", Refused(model, assigned).Message, StringComparison.Ordinal);
        Refused(model, attempt, "dispatch.work");
        Refused(model, result, "dispatch.work");
        Assert.False(model.Counts(assigned));
        Assert.False(model.Counts(attempt));
        Assert.False(model.Counts(result));
        Assert.Empty(WorkView.AttemptsOf(model, assigned));
    }

    [Fact]
    public void TheRosterIsReadAsItStoodAtTheAssignmentsTime()
    {
        // A reassignment to the roster's next agent stands; a roster revised later to give the role to a person changes nothing for it.
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope toSpare = e.Assign(e.Build, e.Spare.Id);
        string op = e.F.Estate.OperatorId;
        RecordEnvelope roster = e.F.Load().Current("roster").Single();
        e.F.Estate.Add("roster", op, $$"""{"roles":[{"role":"builder","principals":["{{e.Spare.Id}}","{{e.F.Estate.BuilderId}}","{{e.Person.Id}}"]},{"role":"reviewer","principals":["{{e.Person.Id}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}""", supersedes: roster.Id, signer: e.F.OperatorSigner);
        RecordEnvelope later = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, toSpare));
        Assert.Contains("gives role builder to a person", Refused(model, later).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFreshStepGoesOnlyToAPrincipalTheRosterNamesForItsRole()
    {
        // A step no one holds follows the roster as goal step routes it: never an agent the roster does not name for the role, never a
        // person, and the roster's agent takes it.
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        RecordEnvelope outsider = e.F.Estate.Add("principal", e.F.Estate.OperatorId, """{"kind":"agent","name":"Outside Agent","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""", signer: e.F.OperatorSigner);
        RecordEnvelope toOutsider = e.Assign(e.Build, outsider.Id);
        RecordEnvelope toPerson = e.Assign(e.Review, e.Person.Id);
        RecordEnvelope toRoster = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("only to a principal the goal's roster names for role builder", Refused(model, toOutsider).Message, StringComparison.Ordinal);
        Assert.Contains("never gives a step to a person", Refused(model, toPerson).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(toOutsider));
        Assert.False(model.Counts(toPerson));
        Assert.Empty(Errors(model, toRoster));
    }

    [Fact]
    public void AServicePrincipalsEarlierAssignmentKeepsCountingAndItsLaterDispatchGrantIsRefused()
    {
        // The service principal assigns a step under local-engineering, which then expires. A dispatch grant afterwards finds that
        // history and is refused, so it withdraws nothing the principal wrote before it; from its date on it confines the principal,
        // giving no authority to assign.
        using var e = new Estate();
        var engineering = new JsonObject
        {
            ["grantee"] = e.Dispatcher.Id, ["scope"] = new JsonArray(Scopes.LocalEngineering), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(),
            ["basis"] = e.Basis.Id, ["expires"] = RecordParser.FormatTimestamp(e.F.Estate.Clock.AddHours(1)),
        };
        RecordEnvelope engineeringGrant = e.Grant(engineering);
        RecordEnvelope assigned = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, assigned));

        e.F.Estate.SetClock(e.F.Estate.Clock.AddHours(2));
        RecordEnvelope dispatch = e.Grant();
        RecordEnvelope after = e.Assign(e.Review, e.Spare.Id);
        model = e.F.Load();
        Finding refused = Refused(model, dispatch, "authority.dispatch");
        Assert.Contains("has a history before this grant", refused.Message, StringComparison.Ordinal);
        Assert.Contains(engineeringGrant.Id, refused.Message, StringComparison.Ordinal);
        Assert.Empty(Errors(model, assigned));
        Assert.True(model.Counts(model.ById[assigned.Id]));
        Assert.Contains("no dispatch grant in force", Refused(model, after).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(after));
        Assert.False(Routing.MayTakeStep(model, model.ById[e.Goal.Id], e.Dispatcher.Id, e.F.Estate.Clock));
    }

    [Fact]
    public void AStepTheOperatorAssignedBeforeTheGrantRefusesTheGrantAndOnlyEarlierWorkCounts()
    {
        // The Reviewer's probe (a): the operator assigns the service principal a step, then grants it dispatch. The grant finds that
        // history and is refused. The principal's attempt and result dated before the grant count, as work on a step the operator gave it;
        // its attempt dated after the grant counts for nothing, since the refused grant still confines it from its date on.
        using var e = new Estate();
        RecordEnvelope assigned = e.Assign(e.Build, e.Dispatcher.Id, e.F.OperatorSigner, e.F.Estate.OperatorId);
        DateTimeOffset before = e.F.Estate.Clock;
        e.F.Estate.SetClock(before.AddHours(1));
        RecordEnvelope dispatch = e.Grant();
        RecordEnvelope later = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, assigned.Reference, signer: e.Signer);
        e.F.Estate.SetClock(before.AddMinutes(1));
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, assigned.Reference, signer: e.Signer);
        RecordEnvelope result = e.F.Estate.Add("result", e.Dispatcher.Id, Result, attempt.Reference, signer: e.Signer);
        EstateModel model = e.F.Load();
        Assert.Contains("has a history before this grant", Refused(model, dispatch, "authority.dispatch").Message, StringComparison.Ordinal);
        Assert.True(model.Counts(model.ById[attempt.Id]));
        Assert.True(model.Counts(model.ById[result.Id]));
        Assert.DoesNotContain(model.Findings, f => f.Code == "dispatch.work" && f.Location.StartsWith(attempt.ExpectedPath, StringComparison.Ordinal));
        Refused(model, later, "dispatch.work");
        Assert.False(model.Counts(later));
    }

    [Fact]
    public void AnAssignmentDatedIntoAnEarlierGrantsTermIsNoDispatchersWorkSinceTheGrantIsRefused()
    {
        // The Reviewer's probe (b): the service principal held local-engineering, which expired, and was then granted dispatch. The grant
        // is refused for that history, so the principal never became a dispatcher: an assignment it dates back into its local-engineering
        // term is judged by that grant, as any principal's record dated into an expired grant's term is; one it dates after the refused
        // grant counts for nothing.
        using var e = new Estate();
        DateTimeOffset start = e.F.Estate.Clock;
        var engineering = new JsonObject
        {
            ["grantee"] = e.Dispatcher.Id, ["scope"] = new JsonArray(Scopes.LocalEngineering), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(),
            ["basis"] = e.Basis.Id, ["expires"] = RecordParser.FormatTimestamp(start.AddHours(1)),
        };
        e.Grant(engineering);
        e.F.Estate.SetClock(start.AddHours(2));
        RecordEnvelope dispatch = e.Grant();
        RecordEnvelope after = e.Assign(e.Build, e.F.Estate.BuilderId);
        e.F.Estate.SetClock(start.AddMinutes(10));
        RecordEnvelope backdated = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("has a history before this grant", Refused(model, dispatch, "authority.dispatch").Message, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Findings, f => f.Code is "assignment.dispatch" or "assignment.author" && f.Location.StartsWith(backdated.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains("no dispatch grant in force", Refused(model, after).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(after));
    }

    [Fact]
    public void ADispatchersRecordsDatedBeforeItsGrantAreConfinedAndVoidNothing()
    {
        // A dispatcher with no history before its grant writes, after the grant, an assignment and work dated before it. Its own records
        // never count as history, so the grant stays valid, and it is a dispatcher for every record at any date: the backdated assignment
        // is outside any dispatch grant in force at its date, and the backdated attempt and result are a dispatcher's work.
        using var e = new Estate();
        DateTimeOffset start = e.F.Estate.Clock;
        e.F.Estate.SetClock(start.AddHours(1));
        RecordEnvelope grant = e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        e.F.Estate.SetClock(start.AddMinutes(10));
        RecordEnvelope backdated = e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope itself = e.Assign(e.Review, e.Dispatcher.Id);
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, itself.Reference, signer: e.Signer);
        RecordEnvelope result = e.F.Estate.Add("result", e.Dispatcher.Id, Result, attempt.Reference, signer: e.Signer);
        e.F.Estate.SetClock(start.AddHours(2));
        RecordEnvelope legitimate = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, grant));
        Assert.Contains("no dispatch grant in force", Refused(model, backdated).Message, StringComparison.Ordinal);
        Refused(model, attempt, "dispatch.work");
        Refused(model, result, "dispatch.work");
        foreach (RecordEnvelope record in new[] { backdated, itself, attempt, result }) Assert.False(model.Counts(model.ById[record.Id]), record.Type);
        Assert.Empty(Errors(model, legitimate));
        Assert.True(model.Counts(model.ById[legitimate.Id]));
    }

    [Fact]
    public void AGrantVoidedByAnotherPrincipalsBackdatedRecordStillConfinesTheDispatcher()
    {
        // After a valid grant, the operator writes an assignment to the dispatcher dated before it. The grant is now refused for that
        // history, and still confines the dispatcher from its date on: its later attempt counts for nothing, and it assigns nothing.
        using var e = new Estate();
        DateTimeOffset start = e.F.Estate.Clock;
        e.F.Estate.SetClock(start.AddHours(1));
        RecordEnvelope grant = e.Grant();
        RecordEnvelope assigned = e.Assign(e.Build, e.Spare.Id);
        Assert.Empty(Errors(e.F.Load(), assigned));
        e.F.Estate.SetClock(start.AddMinutes(10));
        RecordEnvelope backdated = e.Assign(e.Review, e.Dispatcher.Id, e.F.OperatorSigner, e.F.Estate.OperatorId);
        e.F.Estate.SetClock(start.AddHours(3));
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, backdated.Reference, signer: e.Signer);
        RecordEnvelope later = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Assert.Contains("has a history before this grant", Refused(model, grant, "authority.dispatch").Message, StringComparison.Ordinal);
        Refused(model, attempt, "dispatch.work");
        Assert.False(model.Counts(attempt));
        Assert.Contains("no dispatch grant in force", Refused(model, later).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(later));
    }

    [Theory]
    [InlineData("assignment")]
    [InlineData("grant")]
    public void AnUnauthorizedRecordDatedBeforeTheGrantNeitherVoidsItNorFreesTheDispatcher(string variant)
    {
        // The Reviewer's probe: a fresh dispatcher holds a valid grant and its assignment to a roster agent counts. Afterwards the builder,
        // an agent with no authority to assign or to grant, writes an assignment of a step to the dispatcher, or a repository-write grant
        // to it, dated before the grant. That record is itself refused, so it is no history: the grant stays valid, the dispatcher's
        // assignment keeps counting, and an attempt the dispatcher dates before the grant is still a dispatcher's work.
        using var e = new Estate();
        DateTimeOffset start = e.F.Estate.Clock;
        e.F.Estate.SetClock(start.AddHours(1));
        RecordEnvelope grant = e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20))));
        RecordEnvelope assigned = e.Assign(e.Build, e.Spare.Id);
        e.F.Estate.SetClock(start.AddMinutes(10));
        RecordEnvelope backdated = variant == "assignment"
            ? e.Assign(e.Review, e.Dispatcher.Id, e.F.BuilderSigner, e.F.Estate.BuilderId)
            : e.F.Estate.Add("authority", e.F.Estate.BuilderId, WithoutBounds(e.GrantBody(scopes: [Scopes.RepositoryWrite])).ToJsonString(), signer: e.F.BuilderSigner);
        RecordEnvelope ownAttempt = e.F.Estate.Add("attempt", e.Dispatcher.Id, Attempt, (variant == "assignment" ? backdated : assigned).Reference, signer: e.Signer);
        EstateModel model = e.F.Load();
        Refused(model, backdated, variant == "assignment" ? "assignment.author" : "authority.author");
        Assert.Empty(Errors(model, grant));
        Assert.Empty(Errors(model, assigned));
        Assert.True(model.Counts(model.ById[assigned.Id]));
        Refused(model, ownAttempt, "dispatch.work");
        Assert.False(model.Counts(ownAttempt));
    }

    [Fact]
    public void AFormerDispatcherIsGrantedNothingElseAfterExpiryAndMayBeGrantedDispatchAgain()
    {
        // The dispatch grant expires; an administer or a local-engineering grant afterwards is refused, and a renewed dispatch grant is
        // valid, its assignments counting.
        using var e = new Estate();
        RecordEnvelope first = e.Grant(e.GrantBody(term: TimeSpan.FromDays(2)));
        e.F.Estate.SetClock(e.F.Estate.Clock.AddDays(3));
        RecordEnvelope administer = e.F.Grant(e.Dispatcher.Id, e.Basis, [Scopes.Administer]);
        RecordEnvelope engineering = e.F.Grant(e.Dispatcher.Id, e.Basis, [Scopes.LocalEngineering]);
        EstateModel model = e.F.Load();
        Assert.Contains(first.Id, Refused(model, administer, "authority.dispatch").Message, StringComparison.Ordinal);
        Assert.Contains("during or after its term", Refused(model, engineering, "authority.dispatch").Message, StringComparison.Ordinal);

        using var g = new Estate();
        g.Grant(g.GrantBody(term: TimeSpan.FromDays(2)));
        g.F.Estate.SetClock(g.F.Estate.Clock.AddDays(3));
        RecordEnvelope renewed = g.Grant();
        RecordEnvelope assigned = g.Assign(g.Build, g.F.Estate.BuilderId);
        model = g.F.Load();
        Assert.Empty(Errors(model, renewed));
        Assert.Empty(Errors(model, assigned));
        Assert.True(model.Counts(model.ById[assigned.Id]));
    }

    [Fact]
    public void AnInvalidDispatchGrantToTheBuilderLeavesItsWorkCounting()
    {
        // A dispatch grant to an agent is itself refused (authority.dispatch), and makes no dispatcher of the builder: its attempt and
        // result on a step the operator assigned it, before the grant and after it, keep counting.
        using var e = new Estate();
        RecordEnvelope assigned = e.Assign(e.Build, e.F.Estate.BuilderId, e.F.OperatorSigner, e.F.Estate.OperatorId);
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.F.Estate.BuilderId, Attempt, assigned.Reference, signer: e.F.BuilderSigner);
        RecordEnvelope result = e.F.Estate.Add("result", e.F.Estate.BuilderId, Result, attempt.Reference, signer: e.F.BuilderSigner);
        RecordEnvelope grant = e.Grant(e.GrantBody(grantee: e.F.Estate.BuilderId));
        RecordEnvelope review = e.Assign(e.Review, e.F.Estate.BuilderId, e.F.OperatorSigner, e.F.Estate.OperatorId);
        RecordEnvelope later = e.F.Estate.Add("attempt", e.F.Estate.BuilderId, Attempt, review.Reference, signer: e.F.BuilderSigner);
        EstateModel model = e.F.Load();
        Assert.Contains(Errors(model, grant), f => f.Code == "authority.dispatch");
        foreach (RecordEnvelope record in new[] { attempt, result, later })
        {
            Assert.DoesNotContain(model.Findings, f => f.Code == "dispatch.work" && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal));
            Assert.True(model.Counts(model.ById[record.Id]), record.Type);
        }
    }

    [Fact]
    public void AStepWhoseHolderRecordedAResultIsNotHeld()
    {
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20, reassign: null))));
        RecordEnvelope first = e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope attempt = e.F.Estate.Add("attempt", e.F.Estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget, branch work/w-1","note":"Build it."}""", first.Reference, signer: e.F.BuilderSigner);
        e.F.Estate.Add("result", e.F.Estate.BuilderId, """{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""", attempt.Reference, signer: e.F.BuilderSigner);
        RecordEnvelope next = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, next));
    }

    [Fact]
    public void AGrantWithoutTheReassignmentBoundReassignsNothing()
    {
        using var e = new Estate();
        e.Grant(e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Goal.Id], cap: 20, reassign: null))));
        e.Assign(e.Build, e.F.Estate.BuilderId);
        RecordEnvelope toSpare = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("does not cover reassignment after a lapse", Refused(model, toSpare).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("another-scope", "authority.dispatch")]
    [InlineData("agent-grantee", "authority.dispatch")]
    [InlineData("service-with-another-role", "authority.dispatch")]
    [InlineData("no-expiry", "body.missing")]
    [InlineData("expiry-past-30-days", "authority.dispatch")]
    [InlineData("no-bound", "authority.bounds")]
    [InlineData("bound-names-no-goal", "authority.bounds")]
    [InlineData("conditions", "authority.dispatch")]
    [InlineData("unsigned", "signature.required")]
    public void ADispatchGrantOutsideItsShapeIsRefused(string defect, string code)
    {
        using var e = new Estate();
        string op = e.F.Estate.OperatorId;
        JsonObject body = defect switch
        {
            "another-scope" => e.GrantBody(scopes: ["dispatch", Scopes.LocalEngineering]),
            "agent-grantee" => e.GrantBody(grantee: e.Spare.Id),
            "service-with-another-role" => e.GrantBody(grantee: e.F.Estate.Add("principal", op, """{"kind":"service","name":"Busy service","roles":["service","builder"]}""", signer: e.F.OperatorSigner).Id),
            "expiry-past-30-days" => e.GrantBody(term: TimeSpan.FromDays(31)),
            "no-bound" => e.GrantBody(bounds: []),
            "bound-names-no-goal" => e.GrantBody(bounds: new JsonArray(Estate.Bound([e.Build.Id]))),
            "conditions" => e.GrantBody(conditions: ["Only at night."]),
            _ => e.GrantBody(),
        };
        if (defect == "no-expiry") body.Remove("expires");
        RecordEnvelope grant = e.Grant(body, sign: defect != "unsigned");
        EstateModel model = e.F.Load();
        Assert.Contains(Errors(model, grant), f => f.Code == code);
    }

    [Fact]
    public void ADispatcherHoldsNoOtherAuthority()
    {
        // Another grant to a dispatcher is refused, and so is a dispatch grant to a principal holding another; an assignment by a
        // dispatcher that holds another grant all the same is outside its dispatch authority.
        using var e = new Estate();
        RecordEnvelope grant = e.Grant();
        RecordEnvelope engineering = e.F.Grant(e.Dispatcher.Id, e.Basis, [Scopes.LocalEngineering]);
        RecordEnvelope assigned = e.Assign(e.Build, e.F.Estate.BuilderId);
        EstateModel model = e.F.Load();
        Assert.Empty(Errors(model, grant));
        Assert.Contains("holds no other authority", Refused(model, engineering, "authority.dispatch").Message, StringComparison.Ordinal);
        Assert.Contains("holds no other authority", Refused(model, assigned).Message, StringComparison.Ordinal);

        using var g = new Estate();
        RecordEnvelope held = g.F.Grant(g.Dispatcher.Id, g.Basis, [Scopes.AcceptWork]);
        RecordEnvelope dispatch = g.Grant();
        model = g.F.Load();
        Assert.Contains(Errors(model, dispatch), f => f.Code == "authority.dispatch" && f.Message.Contains(held.Id, StringComparison.Ordinal));
    }
}
