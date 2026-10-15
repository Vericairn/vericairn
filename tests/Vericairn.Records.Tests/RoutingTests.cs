// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// Routing by roster (the estate's design/goals.md § 6) in the next-eligible view: each eligible step of a goal reaches the first
/// available principal its role's roster names, agents where available and people where needed; a reserved action reaches only a
/// person who holds it or an agent a grant in force covers; a step a principal holds stays with it; and when no principal is
/// available the step is an ask, never a contributor the roster does not name.
/// </summary>
public sealed class RoutingTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-routing-keys-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public RoutingTests()
    {
        store = new FileKeyStore(home);
    }

    [Fact]
    public void EachEligibleStepReachesTheFirstAvailablePrincipalOfItsRolesRosterAndNeverAnother()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Fixture fixture = Seed(estate);
        RecordEnvelope build = Order(estate, fixture, "W-1", string.Empty);
        Order(estate, fixture, "W-2", ""","role":"reviewer" """);
        Order(estate, fixture, "W-3", ""","role":"publisher" """);
        Order(estate, fixture, "W-4", ""","role":"planner" """);
        RecordEnvelope later = Order(estate, fixture, "W-5", $$""","after":["{{build.Id}}"]""");
        EstateModel model = Loaded(estate);

        NextEligible.GoalRoutes routes = NextEligible.Routes(model, model.ById[fixture.Goal], Machine(fixture.BuilderDevice));
        Assert.Equal("active", routes.Status);
        Assert.Equal(["W-1", "W-2", "W-3", "W-4", "W-5"], routes.Steps.Select(s => EstateModel.BodyString(s.Order, "key")));
        NextEligible.Route builder = Route(routes, "W-1");
        Assert.Equal(("agent", estate.BuilderId, "stand-in"), (builder.State, builder.Principal, builder.Launcher));
        Assert.Equal([(fixture.Spare, false), (estate.BuilderId, true)], builder.Candidates.Select(c => (c.Principal, c.Available)));
        Assert.Contains("it has no admitted device", Assert.Single(builder.Candidates[0].Reasons), StringComparison.Ordinal);
        Assert.True(builder.BudgetAllows);
        Assert.Empty(builder.Custody);
        Assert.Equal(fixture.Roster, builder.Source.Roster!.Id);
        Assert.Equal(("person", fixture.Person), (Route(routes, "W-2").State, Route(routes, "W-2").Principal));
        NextEligible.Route publication = Route(routes, "W-3");
        Assert.Equal(("reserved", estate.OperatorId, "publication"), (publication.State, publication.Principal, publication.Reserved));
        NextEligible.Route nobody = Route(routes, "W-4");
        Assert.Equal(("unavailable", estate.OperatorId), (nobody.State, nobody.Principal));
        Assert.Contains("names no principal for the planner role", nobody.Reason, StringComparison.Ordinal);
        Assert.Null(routes.Steps.Single(s => s.Order.Id == later.Id).Route);
        Assert.False(routes.Steps.Single(s => s.Order.Id == later.Id).Planned!.Eligible);
        Assert.Equal(build.Id, routes.Next!.Order.Id);

        // Without the builder's key here no agent of the role is available: the sponsor is asked, and no person outside the role is given it.
        NextEligible.Route keyless = Route(NextEligible.Routes(model, model.ById[fixture.Goal], Machine()), "W-1");
        Assert.Equal(("unavailable", estate.OperatorId), (keyless.State, keyless.Principal));
        Assert.Contains("no key of its admitted devices is held on this machine", keyless.Candidates[1].Reasons[0], StringComparison.Ordinal);
        Assert.Contains("never falls back to a contributor the roster does not name", keyless.Reason, StringComparison.Ordinal);

        // Without a launcher for its harness here, neither.
        NextEligible.Route launcherless = Route(NextEligible.Routes(model, model.ById[fixture.Goal], Machine(fixture.BuilderDevice) with { Launchers = [("claude-code", "Claude Code")] }), "W-1");
        Assert.Equal("unavailable", launcherless.State);
        Assert.Contains("no launcher for its harness, Example Agent, is available on this machine (claude-code for Claude Code)", launcherless.Candidates[1].Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AReservedActionReachesAnAgentOnlyUnderAGrantInForce()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Fixture fixture = Seed(estate, publishers: $$"""["{{estate.BuilderId}}","{{estate.OperatorId}}"]""");
        Order(estate, fixture, "W-1", ""","role":"publisher" """);
        Order(estate, fixture, "W-2", ""","role":"decider" """);
        Assert.Equal(("reserved", estate.OperatorId), Reached(estate, fixture, "W-1"));
        NextEligible.Route ungranted = Route(NextEligible.Routes(Loaded(estate), Loaded(estate).ById[fixture.Goal], Machine(fixture.BuilderDevice)), "W-1");
        Assert.Contains("the step is a reserved action, publication, and no grant of publication in force covers it", ungranted.Candidates[0].Reasons[0], StringComparison.Ordinal);

        // A grant of publication in force lets the builder carry it; a decision stays the decider's, an operator.
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Publication","statement":"The builder publishes the guide.","summary":"A grant.","synthesis":"A time-boxed grant.","effects":["publication"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[]}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["publication"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}","expires":"2026-10-14T12:00:00Z"}""");
        Assert.Equal(("agent", estate.BuilderId), Reached(estate, fixture, "W-1"));
        Assert.Equal(("reserved", estate.OperatorId), Reached(estate, fixture, "W-2"));

        // Expired, it covers nothing.
        Assert.Equal(("reserved", estate.OperatorId), Reached(estate, fixture, "W-1", at: new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AStepAPrincipalHoldsStaysWithItAndAnAskAboutItHoldsIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Fixture fixture = Seed(estate);
        RecordEnvelope build = Order(estate, fixture, "W-1", string.Empty);
        RecordEnvelope review = Order(estate, fixture, "W-2", ""","role":"reviewer" """);
        RecordEnvelope plan = Order(estate, fixture, "W-4", ""","role":"planner" """);

        // The builder's assignment holds W-1: it goes on with another run while it can, and stays with it when it cannot.
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), build.Reference);
        NextEligible.Route held = Route(Routes(estate, fixture, Machine(fixture.BuilderDevice)), "W-1");
        Assert.Equal(("agent", estate.BuilderId, assignment.Id), (held.State, held.Principal, held.Assignment!.Id));
        NextEligible.Route stuck = Route(Routes(estate, fixture, Machine()), "W-1");
        Assert.Equal(("unavailable", estate.OperatorId, assignment.Id), (stuck.State, stuck.Principal, stuck.Assignment!.Id));
        Assert.Equal([estate.BuilderId], stuck.Candidates.Select(c => c.Principal));
        Assert.Contains("it is never given to another contributor without the sponsor", stuck.Reason, StringComparison.Ordinal);

        // A run in flight holds it; once it settles, the result goes to review, and findings send the step back to the builder.
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, $$$"""{"provider":"stand-in","action":"agent-run","identity":"agent-run:one","intent":"Run the builder.","target":{"assignment":"{{{assignment.Id}}}","principal":"{{{estate.BuilderId}}}","workspace":"widget, branch work/w-1","goal":"{{{fixture.Goal}}}"},"status":"not-started","evidence":[],"cap":{"wallMinutes":10,"attemptsPerStep":2}}""");
        Assert.Equal("running", Route(Routes(estate, fixture, Machine(fixture.BuilderDevice)), "W-1").State);
        estate.Add("operation", estate.OperatorId, $$$"""{"provider":"stand-in","action":"agent-run","identity":"agent-run:one","intent":"Run the builder.","target":{"assignment":"{{{assignment.Id}}}","principal":"{{{estate.BuilderId}}}","workspace":"widget, branch work/w-1","goal":"{{{fixture.Goal}}}"},"status":"canceled","evidence":[],"cap":{"wallMinutes":10,"attemptsPerStep":2}}""", supersedes: intent.Id);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget, branch work/w-1","note":"Build it."}""", assignment.Reference);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""", attempt.Reference);
        NextEligible.Route awaiting = Route(Routes(estate, fixture, Machine(fixture.BuilderDevice)), "W-1");
        Assert.Equal(("review", estate.BuilderId), (awaiting.State, awaiting.Principal));
        Assert.Contains($"the result {result.Id}", awaiting.Reason, StringComparison.Ordinal);
        RecordEnvelope findings = estate.Add("review", estate.OperatorId, """{"independence":"operator","outcome":"findings","summary":"The tests do not pass.","findings":[{"severity":"error","summary":"A test fails."}]}""", result.Reference);
        NextEligible.Route again = Route(Routes(estate, fixture, Machine(fixture.BuilderDevice)), "W-1");
        Assert.Equal("agent", again.State);
        Assert.Contains($"review {findings.Id} found what to change", again.Reason, StringComparison.Ordinal);

        // A person's assignment holds W-2.
        estate.Add("assignment", estate.OperatorId, Assignment(fixture.Person), review.Reference);
        Assert.Equal(("assigned", fixture.Person), Reached(estate, fixture, "W-2"));

        // An ask about W-4 holds it while it waits; answered, its principal is not asked again while the answer holds.
        RecordEnvelope ask = estate.Add("ask", estate.OperatorId, $$"""{"to":"{{estate.OperatorId}}","question":"Name a planner for W-4?","recommendation":"Name one in the roster.","consequence":"W-4 waits.","goal":"{{fixture.Goal}}","state":"open"}""", new RecordReference("work-order", plan.Id, null));
        Assert.Equal(("asked", estate.OperatorId), Reached(estate, fixture, "W-4"));
        Assert.Equal(ask.Id, Route(Routes(estate, fixture, Machine()), "W-4").Ask!.Id);
        estate.Add("decision", estate.BuilderId, $$"""{"title":"No planner","statement":"Not now.","summary":"No planner yet.","synthesis":"The sponsor answers.","effects":["W-4 waits"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[],"answers":["{{ask.Id}}"]}""");
        NextEligible.Route answered = Route(Routes(estate, fixture, Machine()), "W-4");
        Assert.Equal(("answered", estate.OperatorId), (answered.State, answered.Principal));
        Assert.Contains($"answered ask {ask.Id}", answered.Reason, StringComparison.Ordinal);
        Assert.False(answered.Dispatches);

        // The goal's asks and every step, by key, are in the view's text and its JSON.
        NextEligible.GoalRoutes routes = Routes(estate, fixture, Machine(fixture.BuilderDevice));
        string text = NextEligible.RenderText(Loaded(estate), routes);
        Assert.Contains($"  W-1  Order W-1 [work-order {build.Id}]\n    builder: agent, Test Builder [principal {estate.BuilderId}] through stand-in\n", text, StringComparison.Ordinal);
        Assert.Contains($"    answered, to Test Operator: Name a planner for W-4? [ask {ask.Id}]\n", text, StringComparison.Ordinal);
        Assert.Contains($"  Next: W-1, agent; vericairn goal step {fixture.Goal} takes it\n", text, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(NextEligible.RenderJson(Loaded(estate), routes));
        Assert.Equal("vericairn.next/1", json.RootElement.GetProperty("format").GetString());
        Assert.Equal("W-1", json.RootElement.GetProperty("next").GetString());
        Assert.Equal(["agent", "assigned", "answered"], json.RootElement.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("route").GetProperty("state").GetString()));
        Assert.Equal("answered", Assert.Single(json.RootElement.GetProperty("asks").EnumerateArray()).GetProperty("state").GetString());
    }

    [Fact]
    public void AWorkOrderNamesItsRoleFromRecordFormatTwo()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope wrong = estate.Add("work-order", estate.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Publish","intent":"Publish.","scope":["guide"],"exitQuestion":"Published?","inputs":[],"deliverables":["guide"],"role":"Publisher"}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.pattern" && f.Location == wrong.ExpectedPath + "#body.role");

        using var early = new TestEstate();
        RecordEnvelope formatOne = early.Add("work-order", early.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Publish","intent":"Publish.","scope":["guide"],"exitQuestion":"Published?","inputs":[],"deliverables":["guide"],"role":"publisher"}""");
        Assert.Contains(early.Load().Findings, f => f.Code == "body.format" && f.Location == formatOne.ExpectedPath + "#body.role");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(home, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    private static (string State, string? Principal) Reached(TestEstate estate, Fixture fixture, string key, DateTimeOffset? at = null)
    {
        NextEligible.Route route = Route(Routes(estate, fixture, Machine(fixture.BuilderDevice) with { Now = at ?? new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero) }), key);
        return (route.State, route.Principal);
    }

    private static NextEligible.GoalRoutes Routes(TestEstate estate, Fixture fixture, Routing.Machine machine)
    {
        EstateModel model = Loaded(estate);
        return NextEligible.Routes(model, model.ById[fixture.Goal], machine);
    }

    private static NextEligible.Route Route(NextEligible.GoalRoutes routes, string key) =>
        Assert.Single(routes.Steps, s => EstateModel.BodyString(s.Order, "key") == key).Route ?? throw new InvalidOperationException(key + " has no route.");

    private static EstateModel Loaded(TestEstate estate)
    {
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings.Where(f => f.Severity == Severity.Error)));
        return model;
    }

    /// <summary>A machine with the stand-in launcher for the builder's harness and the keys of the devices named, at a fixed time.</summary>
    private static Routing.Machine Machine(params string[] held) =>
        new([("claude-code", "Claude Code"), ("stand-in", "Example Agent")], id => held.Contains(id, StringComparer.Ordinal), (_, _) => [], new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// An estate whose operator's device admitted itself and the builder's; a spare agent with no device; a person; the roster (builder:
    /// the spare, then the builder; reviewer: the person; publisher and decider: the operator, unless named); and a goal the operator
    /// sponsors and activated.
    /// </summary>
    private Fixture Seed(TestEstate estate, string? publishers = null)
    {
        RecordEnvelope operatorDevice = Device(estate, estate.OperatorId, "Operator's laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", operatorDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
        RecordEnvelope builderDevice = Device(estate, estate.BuilderId, "Builder's device");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"The builder's."}""", builderDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
        string spare = estate.Add("principal", estate.OperatorId, """{"kind":"agent","name":"Spare Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""").Id;
        string person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Pat","roles":["contributor"]}""").Id;
        string roster = estate.Add("roster", estate.OperatorId, $$"""{"roles":[{"role":"builder","principals":["{{spare}}","{{estate.BuilderId}}"]},{"role":"reviewer","principals":["{{person}}"]},{"role":"publisher","principals":{{publishers ?? $"[\"{estate.OperatorId}\"]"}}},{"role":"decider","principals":["{{estate.OperatorId}}"]}]}""").Id;
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, $$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":["no change outside the widget"],"kind":"finite","sponsor":"{{estate.OperatorId}}","budget":{"wallMinutes":60,"attemptsPerStep":2},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""");
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", goal.Reference);
        return new Fixture(goal.Id, roster, spare, person, builderDevice.Id);
    }

    private static RecordEnvelope Order(TestEstate estate, Fixture fixture, string key, string extra) =>
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{fixture.Goal}}"}],"deliverables":["work"]{{extra}}}""");

    private static string Assignment(string contributor) =>
        $$$"""{"contributor":"{{{contributor}}}","brief":{"outcome":"The work.","sources":[],"permittedChanges":["work"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["work"],"reviewCriteria":["done"],"recovery":"Record a result."}}""";

    private RecordEnvelope Device(TestEstate estate, string principal, string name)
    {
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"signing"}""", id: id, signer: new StoreSigner(store, id));
    }

    private sealed record Fixture(string Goal, string Roster, string Spare, string Person, string BuilderDevice);
}
