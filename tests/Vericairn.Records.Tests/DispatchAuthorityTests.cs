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
        Assert.Contains("no dispatch grant in force", Refused(model, early).Message, StringComparison.Ordinal);
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
        RecordEnvelope toPerson = e.Assign(e.Build, e.Person.Id);
        RecordEnvelope toSpare = e.Assign(e.Build, e.Spare.Id);
        EstateModel model = e.F.Load();
        Assert.Contains("not the holder it replaces", Refused(model, again).Message, StringComparison.Ordinal);
        Assert.Contains("never goes to the dispatcher itself", Refused(model, toItself).Message, StringComparison.Ordinal);
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
