// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Standing outputs through the runtime (from 0.9.4, GOAL-08; the estate's design/standing-agents.md §§ 8, 9 and 12, journey D07-A): a
/// researcher's occurrence carried by goal step records cited possibilities, an ask and a publication request over MCP and the API, each
/// naming what raised it; the operator adopts one by work new --from-possibility and declines another, which is not raised again without
/// naming it; the agent adopts nothing of its own, and its next occurrence's brief names what it declined. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class StandingOutputJourneyTests
{
    [Fact]
    public void AResearchersOutputsAreDisposedOfByAnotherPrincipalAndNeverByTheAgent()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string goal = StandingAgentRoutingTests.Standing(run);
        GoalStepResponse first = StandingAgentRoutingTests.Step(run, goal, "first");
        Assert.True(first.Ok, first.Message);
        run.Settled(first.Run!.Run);
        string assignment = first.Assignment!.Id;
        RecordEnvelope carried = run.Estate.Load().ById[assignment];
        Assert.Contains("records another principal disposes of", carried.Body.GetProperty("brief").GetProperty("outcome").GetString(), StringComparison.Ordinal);

        // A second run of the occurrence is in flight, and the builder records its outputs in it.
        AgentRuns.Run original = AgentRuns.Find(run.Estate.Load(), first.Run.Run)!;
        JsonObject intent = JsonNode.Parse(original.Intent.Body.GetRawText())!.AsObject();
        intent["identity"] = "agent-run:outputs";
        RecordReference? subject = original.Intent.Subject;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("operation", op, RunEstate.Json(intent.ToJsonString()), Subject: subject is null ? null : $"{subject.Type}:{subject.Id}")));

        // Over MCP: the three write tools, open to the occurrence's contributor; a possibility with a draft work order.
        var registry = new ToolRegistry(run.Service, builder);
        Assert.Contains(registry.Visible(), t => t.Name == "standing_possibility");
        ToolCallResult raised = registry.Call("standing_possibility", new JsonObject { ["assignment"] = assignment, ["body"] = Possibility("cache/index", draft: true) });
        Assert.False(raised.IsError, raised.Text);
        string adoptable = JsonDocument.Parse(raised.Text).RootElement.GetProperty("written").GetProperty("id").GetString()!;
        RecordEnvelope recorded = run.Estate.Load().ById[adoptable];
        Assert.Equal(("open", goal, StandingAgents.KeyOf(carried)), (EstateModel.BodyString(recorded, "disposition"), recorded.Body.GetProperty("raisedBy").GetProperty("goal").GetString(), recorded.Body.GetProperty("raisedBy").GetProperty("occurrence").GetString()));
        Assert.True(recorded.Body.GetProperty("raisedBy").TryGetProperty("run", out _));

        // What raised it, and its disposition, are the runtime's to name; the operator is not the occurrence's contributor.
        JsonObject forged = Possibility("forged");
        forged["disposition"] = "adopted";
        Assert.True(registry.Call("standing_possibility", new JsonObject { ["assignment"] = assignment, ["body"] = forged }).IsError);
        WriteOutcome notTheirs = run.Service.StandingOutput(new StandingOutputRequest(op, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("other"))));
        Assert.False(notTheirs.Ok);
        Assert.Contains("Only the contributor", notTheirs.Problem!.Message, StringComparison.Ordinal);

        // A second open possibility with the same fingerprint is refused; another is recorded through the API.
        WriteOutcome twice = run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("cache/index"))));
        Assert.Contains(twice.Problem!.Findings, f => f.Code == "standing.duplicate-output");
        string declinable = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("drop/legacy"))))).Id;

        // An ask to who decides for the goal, and words outside the estate as a publication request.
        string ask = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "ask", RunEstate.Json("""{"question":"Is the legacy index still read?","recommendation":"Drop it if not.","consequence":"The research goes on."}""")))).Id;
        Assert.Equal(op, EstateModel.BodyString(run.Estate.Load().ById[ask], "to"));
        ToolCallResult published = registry.Call("standing_publication", new JsonObject
        {
            ["assignment"] = assignment, ["body"] = new JsonObject { ["destination"] = "the project's discussion board", ["text"] = "The widget's index is cached from the next release.", ["why"] = "Users asked.", ["recommendation"] = "Post it.", ["consequence"] = "Nothing is posted." },
        });
        Assert.False(published.IsError, published.Text);
        RecordEnvelope request = run.Estate.Load().ById[JsonDocument.Parse(published.Text).RootElement.GetProperty("written").GetProperty("id").GetString()!];
        Assert.Equal(("publication", carried.Subject!.Id), (EstateModel.BodyString(request, "kind"), request.Subject!.Id));
        Assert.Equal(4, JsonDocument.Parse(run.Service.GoalDue(goal)!.Json.GetRawText()).RootElement.GetProperty("outputs").GetProperty("open").GetInt32());

        // The agent does not adopt its own possibility; the operator does, by work new --from-possibility, as one batch.
        WriteOutcome own = run.Service.WorkNew(new WorkNewRequest(builder) { FromPossibility = adoptable });
        Assert.False(own.Ok);
        Assert.Contains(own.Problem!.Findings, f => f.Code == "standing.own-output");
        RecordWritten order = RunEstate.Ok(run.Service.WorkNew(new WorkNewRequest(op, Key: "IDX-1") { FromPossibility = adoptable }));
        EstateModel model = run.Estate.Load();
        RecordEnvelope adopted = model.Latest(model.ById[adoptable]);
        Assert.Equal(("adopted", order.Id), (EstateModel.BodyString(adopted, "disposition"), adopted.Body.GetProperty("adoptedAs").GetProperty("id").GetString()));
        RecordEnvelope created = model.ById[order.Id];
        Assert.Equal(("IDX-1", "Cache the index"), (EstateModel.BodyString(created, "key"), EstateModel.BodyString(created, "title")));
        Assert.Contains(created.Body.GetProperty("inputs").EnumerateArray(), i => i.GetProperty("type").GetString() == "possibility" && i.GetProperty("id").GetString() == adoptable);
        Assert.False(run.Service.WorkNew(new WorkNewRequest(op, Key: "IDX-2") { FromPossibility = adoptable }).Ok);

        // The operator declines the other; raising it again is refused unless it names the declined one with new evidence.
        RecordEnvelope open = model.ById[declinable];
        JsonObject declined = JsonNode.Parse(open.Body.GetRawText())!.AsObject();
        declined["disposition"] = "declined";
        declined["reason"] = "The legacy index is still read by older clients.";
        string decline = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", op, JsonSerializer.SerializeToElement(declined), Supersedes: declinable))).Id;
        WriteOutcome again = run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("drop/legacy"))));
        Assert.Contains(again.Problem!.Findings, f => f.Code == "standing.raises-again");

        // The next occurrence's brief names the declined possibility among its sources.
        Assert.Contains(StandingOutputs.OpenAndDeclined(run.Estate.Load(), run.Estate.Load().ById[goal]), p => p.Id == decline);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void NoAuthorAdoptsItsOwnPossibilityAndAHeldFindingAdoptedStaysHeld()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        (string _, string assignment) = Carried(run);

        // A plain possibility the builder wrote outside any occurrence, with a draft: its author does not adopt it by work new
        // --from-possibility, as the option says; the operator does.
        JsonObject plain = Possibility("plain/idea", draft: true);
        plain["origin"] = "The builder's own note.";
        plain["disposition"] = "open";
        string idea = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", builder, JsonSerializer.SerializeToElement(plain)))).Id;
        WriteOutcome own = run.Service.WorkNew(new WorkNewRequest(builder, Key: "IDEA-0") { FromPossibility = idea });
        Assert.False(own.Ok);
        Assert.Contains(own.Problem!.Findings, f => f.Code == "standing.own-output");
        Assert.Equal("open", EstateModel.BodyString(run.Estate.Load().Latest(run.Estate.Load().ById[idea]), "disposition"));
        RunEstate.Ok(run.Service.WorkNew(new WorkNewRequest(op, Key: "IDEA-1") { FromPossibility = idea }));

        // A held finding the operator adopts becomes a held work order, which the disclosure hold keeps out of public pushes.
        JsonObject finding = Possibility("held/finding", draft: true);
        finding["disclosure"] = "held";
        string held = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(finding)))).Id;
        RecordWritten order = RunEstate.Ok(run.Service.WorkNew(new WorkNewRequest(op, Key: "SEC-1") { FromPossibility = held }));
        Assert.Equal("held", EstateModel.BodyString(run.Estate.Load().ById[order.Id], "disclosure"));
        Assert.Contains(DisclosureHolds.Active(run.Estate.Load()), h => h.Key == "SEC-1");
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAgentsAskInTheRenewalAsksWordsHoldsBackNoRenewalAsk()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        // A term of six hours asks within its last three; the clock is controlled to an hour before its until time.
        DateTimeOffset until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds());
        string goal = StandingAgentRoutingTests.Standing(run, until: RecordParser.FormatTimestamp(until));
        run.Service.FlowClock = new FixedClock(until.AddHours(-1));

        // The builder writes a plain ask about the goal's revision in the renewal ask's words; goal step still asks the sponsor itself.
        RecordEnvelope current = run.Estate.Load().ById[goal];
        string forged = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("ask", run.Estate.BuilderId, RunEstate.Json($$"""
            {"to":"{{op}}","question":"{{StandingAgents.RenewalAsk}} Research the widget before its until time?","recommendation":"Let it lapse.","consequence":"Nothing.","goal":"{{goal}}","state":"open"}
            """), Subject: $"goal:{goal}:{current.ComputedHash}"))).Id;
        GoalStepResponse renewal = StandingAgentRoutingTests.Step(run, goal, "renewal");
        Assert.True(renewal.Ok, renewal.Message);
        Assert.Equal(("ask", "attention"), (renewal.Outcome, renewal.Route));
        Assert.NotEqual(forged, renewal.Ask!.Id);
        Assert.Equal(op, run.Estate.Load().ById[renewal.Ask.Id].By);
    }

    [Fact]
    public void TheWriteBoundaryRefusesADispositionByAnAgentTheStandingAgentsRoleNames()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Helper","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        (string goal, string assignment) = Carried(run, [builder, helper]);
        string possibility = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("cache/index"))))).Id;

        // The helper, which the goal's role names and which carried none of the occurrences, does not decline it (standing.own-output).
        JsonObject declined = JsonNode.Parse(run.Estate.Load().ById[possibility].Body.GetRawText())!.AsObject();
        declined["disposition"] = "declined";
        declined["reason"] = "Not worth it.";
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("possibility", helper, JsonSerializer.SerializeToElement(declined), Supersedes: possibility));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "standing.own-output" && f.Severity == "error" && f.Message.Contains(helper, StringComparison.Ordinal));
        Assert.Equal(1, JsonDocument.Parse(run.Service.GoalDue(goal)!.Json.GetRawText()).RootElement.GetProperty("outputs").GetProperty("open").GetInt32());

        // The operator, a person, declines it.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", op, JsonSerializer.SerializeToElement(declined), Supersedes: possibility)));
        Assert.Equal(0, JsonDocument.Parse(run.Service.GoalDue(goal)!.Json.GetRawText()).RootElement.GetProperty("outputs").GetProperty("open").GetInt32());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAgentTiedLaterDisposesOfNothingByABackdatedDeclineAndTheOperatorStillAdoptsThePossibility()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string helper = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Helper","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        (string goal, string assignment) = Carried(run);
        string possibility = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("cache/index", draft: true))))).Id;
        int Open() => JsonDocument.Parse(run.Service.GoalDue(goal)!.Json.GetRawText()).RootElement.GetProperty("outputs").GetProperty("open").GetInt32();
        Assert.Equal(1, Open());

        // Written straight to records/: the helper's decline, a minute after the possibility, then the operator's revision of the goal naming
        // the helper beside the builder for the researcher role. The decline is reported (standing.output-now), and it disposes of nothing.
        EstateModel model = run.Estate.Load();
        RecordEnvelope raised = model.ById[possibility];
        RecordEnvelope current = model.Latest(model.ById[goal]);
        JsonObject declined = JsonNode.Parse(raised.Body.GetRawText())!.AsObject();
        declined["disposition"] = "declined";
        declined["reason"] = "Not worth it.";
        JsonObject named = JsonNode.Parse(current.Body.GetRawText())!.AsObject();
        named["roles"] = new JsonArray(Vericairn.Records.Tests.FlowFixture.Role("researcher", builder, helper));
        run.Estate.SetClock(raised.AtTime.AddMinutes(1));
        run.Estate.Add("possibility", helper, declined.ToJsonString(), supersedes: possibility);
        run.Estate.Add("goal", op, named.ToJsonString(), supersedes: current.Id);
        model = run.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == StandingOutputs.BoundaryCode && f.Severity == Severity.Info && f.Message.Contains(helper, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Code is "standing.own-output" or StandingOutputs.BoundaryCode && f.Severity == Severity.Error);
        Assert.Equal(1, Open());

        // records new refuses such a disposition by the helper: the write boundary refuses one the records as they stand report.
        JsonObject parked = declined.DeepClone().AsObject();
        parked["disposition"] = "parked";
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("possibility", helper, JsonSerializer.SerializeToElement(parked), Supersedes: model.Latest(raised).Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code is "standing.own-output" or StandingOutputs.BoundaryCode && f.Severity == "error" && f.Message.Contains(helper, StringComparison.Ordinal));

        // The possibility reads open, so the operator adopts it by work new --from-possibility, and the open outputs fall.
        RecordWritten order = RunEstate.Ok(run.Service.WorkNew(new WorkNewRequest(op, Key: "IDX-1") { FromPossibility = possibility }));
        model = run.Estate.Load();
        RecordEnvelope adopted = model.Latest(model.ById[possibility]);
        Assert.Equal(("adopted", order.Id), (EstateModel.BodyString(adopted, "disposition"), adopted.Body.GetProperty("adoptedAs").GetProperty("id").GetString()));
        Assert.Equal(0, Open());
    }

    [Fact]
    public void AStandingAgentRecordsNoAnswerToItsOwnAskAndNoAuthorAdoptsItsOwnPossibility()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        (string goal, string assignment) = Carried(run);
        string ask = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "ask", RunEstate.Json("""{"question":"Is the legacy index still read?","recommendation":"Drop it if not.","consequence":"The research goes on."}""")))).Id;
        int Open() => JsonDocument.Parse(run.Service.GoalDue(goal)!.Json.GetRawText()).RootElement.GetProperty("outputs").GetProperty("open").GetInt32();
        Assert.Equal(1, Open());
        JsonElement Answer() => JsonSerializer.SerializeToElement(new JsonObject
        {
            ["title"] = "Drop it", ["statement"] = "Drop the legacy index.", ["summary"] = "The legacy index goes.", ["synthesis"] = "The operator answers the agent's ask.",
            ["effects"] = new JsonArray("the legacy index is dropped"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(-1)),
            ["stake"] = "low", ["notAuthorized"] = new JsonArray("spending"), ["answers"] = new JsonArray(ask),
        });

        // The agent that raised the ask records a decision naming the operator: the record write refuses it, and the ask stays open.
        WriteOutcome own = run.Service.NewRecord(new NewRecordRequest("decision", builder, Answer()));
        Assert.False(own.Ok);
        Assert.Contains(own.Problem!.Findings, f => f.Code == "standing.own-output" && f.Severity == "error");
        Assert.Equal(1, Open());

        // The operator's own decision answers it.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, Answer())));
        Assert.Equal(0, Open());

        // A plain possibility the builder wrote, with only the fields earlier estates wrote: its adoption by its author is refused at the
        // record write (standing.output-now), and the operator adopts it.
        string order = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json("""{"key":"IDEA-1","kind":"ordinary","title":"A books pack","intent":"Ship a books pack.","scope":["path:books/"],"exitQuestion":"Is the books pack shipped?","inputs":[],"deliverables":["a books pack"]}""")))).Id;
        string idea = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", builder, RunEstate.Json("""{"title":"A books pack","idea":"Ship a books pack.","origin":"The builder's own note.","disposition":"open"}""")))).Id;
        JsonElement adopting = RunEstate.Json($$$"""{"title":"A books pack","idea":"Ship a books pack.","origin":"The builder's own note.","disposition":"adopted","reason":"Planned.","adoptedAs":{"type":"work-order","id":"{{{order}}}"}}""");
        WriteOutcome adopted = run.Service.NewRecord(new NewRecordRequest("possibility", builder, adopting, Supersedes: idea));
        Assert.False(adopted.Ok);
        Assert.Contains(adopted.Problem!.Findings, f => f.Code == StandingOutputs.BoundaryCode && f.Severity == "error");
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", op, adopting, Supersedes: idea)));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheBuilderThatStepsTheGoalRecordsTheOperatorsDecisionOnTheAsksItWrote()
    {
        using var fixture = new SteppedAsksFixture();
        RunEstate run = fixture.Run;
        string op = run.Estate.OperatorId;
        EstateModel model = run.Estate.Load();
        Assert.Equal((fixture.Builder, op), (model.ById[fixture.Pause].By, EstateModel.BodyString(model.ById[fixture.Pause], "to")));
        Assert.Equal((fixture.Builder, op), (model.ById[fixture.Renewal].By, EstateModel.BodyString(model.ById[fixture.Renewal], "to")));
        (string, int) Due() => (run.Service.GoalDue(fixture.Goal)!.Json.GetProperty("state").GetString()!, run.Service.GoalDue(fixture.Goal)!.Json.GetProperty("failures").GetProperty("consecutive").GetInt32());
        Assert.Equal(("held", 3), Due());
        JsonElement Answer(string ask) => JsonSerializer.SerializeToElement(new JsonObject
        {
            ["title"] = "Go on", ["statement"] = "Let the researcher go on.", ["summary"] = "The operator answers.", ["synthesis"] = "The operator answers the runtime's ask.",
            ["effects"] = new JsonArray("the agent goes on"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(-1)),
            ["stake"] = "low", ["notAuthorized"] = new JsonArray("spending"), ["answers"] = new JsonArray(ask),
        });

        // The helper, which carried the failed runs, answers neither ask, whatever decidedBy names.
        foreach (string ask in new[] { fixture.Pause, fixture.Renewal })
        {
            WriteOutcome own = run.Service.NewRecord(new NewRecordRequest("decision", fixture.Helper, Answer(ask)));
            Assert.False(own.Ok);
            Assert.Contains(own.Problem!.Findings, f => f.Code == "standing.own-output" && f.Severity == "error");
        }

        Assert.Equal(("held", 3), Due());

        // The builder, which wrote both as goal step's caller and is not tied to the agent, records the operator's decision on each: the pause
        // lifts and the renewal ask is answered.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", fixture.Builder, Answer(fixture.Pause))));
        Assert.Equal(("active", 0), Due());
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", fixture.Builder, Answer(fixture.Renewal))));
        model = run.Estate.Load();
        Assert.Equal("answered", Asks.State(model, model.ById[fixture.Renewal]).State);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ARecordReusingADispositionsIdentifierLeavesTheOutputOpenAndNothingRouted()
    {
        // From 0.9.4 (FIX-156), through the worker: the builder records a possibility in its run, the operator parks it and opens it again, and
        // the builder writes by hand a record carrying the identifier of the reopening, parking it again, dated in the month before so that
        // its path sorts first. goal due reads the output open and the agent held, goal step starts no run and nobody adopts the possibility.
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        (string goal, string assignment) = Carried(run);
        string raised = RunEstate.Ok(run.Service.StandingOutput(new StandingOutputRequest(builder, assignment, "possibility", JsonSerializer.SerializeToElement(Possibility("cache/index", draft: true))))).Id;
        JsonObject Disposed(string id, string disposition, string reason)
        {
            JsonObject body = JsonNode.Parse(run.Estate.Load().ById[id].Body.GetRawText())!.AsObject();
            body["disposition"] = disposition;
            body["reason"] = reason;
            return body;
        }

        string parked = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", op, JsonSerializer.SerializeToElement(Disposed(raised, "parked", "Not before the index format settles.")), Supersedes: raised))).Id;
        string reopened = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("possibility", op, JsonSerializer.SerializeToElement(Disposed(parked, "open", "The index format settled.")), Supersedes: parked))).Id;
        JsonElement Due() => run.Service.GoalDue(goal)!.Json;
        Assert.Equal(1, Due().GetProperty("outputs").GetProperty("open").GetInt32());
        Assert.Equal(0, run.Service.Check().Errors);

        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.BuilderDevice);
        RecordEnvelope original = run.Estate.Load().ById[reopened];
        run.Estate.SetClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeSeconds()));
        RecordEnvelope copy = run.Estate.Add("possibility", builder, Disposed(parked, "parked", "Parked again by hand.").ToJsonString(), supersedes: parked, id: reopened, signer: signer);
        Assert.NotEqual(original.ExpectedPath, copy.ExpectedPath);
        Assert.Equal(("held", 1), (Due().GetProperty("state").GetString(), Due().GetProperty("outputs").GetProperty("open").GetInt32()));
        Assert.Null(StandingAgentRoutingTests.Step(run, goal, "after-copy").Run);
        Assert.False(run.Service.WorkNew(new WorkNewRequest(op, Key: "IDX-1") { FromPossibility = raised }).Ok);
    }

    /// <summary>A standing agent whose occurrence goal step routed to the builder, with a second run of it in flight; its goal and assignment.</summary>
    private static (string Goal, string Assignment) Carried(RunEstate run, string[]? holders = null)
    {
        string goal = StandingAgentRoutingTests.Standing(run, holders: holders);
        GoalStepResponse first = StandingAgentRoutingTests.Step(run, goal, "first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal(run.Estate.BuilderId, first.Principal);
        run.Settled(first.Run!.Run);
        AgentRuns.Run original = AgentRuns.Find(run.Estate.Load(), first.Run.Run)!;
        JsonObject intent = JsonNode.Parse(original.Intent.Body.GetRawText())!.AsObject();
        intent["identity"] = "agent-run:outputs";
        RecordReference? subject = original.Intent.Subject;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("operation", run.Estate.OperatorId, RunEstate.Json(intent.ToJsonString()), Subject: subject is null ? null : $"{subject.Type}:{subject.Id}")));
        return (goal, first.Assignment!.Id);
    }

    /// <summary>A fixed clock, the runtime's controlled time.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JsonObject Possibility(string fingerprint, bool draft = false)
    {
        var body = new JsonObject
        {
            ["title"] = "Cache the widget's index (" + fingerprint + ")", ["idea"] = "Reading the index on every call costs a second.", ["fingerprint"] = fingerprint,
            ["sources"] = new JsonArray(new JsonObject { ["address"] = "https://example.org/widget/index", ["retrievedAt"] = "2026-10-01T00:00:00Z" }),
            ["evidence"] = new JsonArray(new JsonObject { ["stance"] = "for", ["statement"] = "The profile shows the read.", ["sources"] = new JsonArray(0) }),
        };
        if (draft)
        {
            body["draft"] = new JsonObject
            {
                ["title"] = "Cache the index", ["intent"] = "Stop reading the index on every call.", ["scope"] = new JsonArray("path:src/index/"), ["deliverables"] = new JsonArray("a cached index"),
                ["exitQuestion"] = "Is the index read once per change?",
            };
        }

        return body;
    }
}

/// <summary>
/// A standing agent the builder steps and a helper agent carries (from 0.9.4, GOAL-08): the builder holds local-engineering, and the goal's
/// role names only the helper, which has a device held here and the builder's harness. An hour before the agent's until time the builder's
/// goal step writes the renewal ask; then three of the helper's runs end with blocked results, and the builder's goal step writes the failure
/// pause's ask. Both are put to the operator; every harness is the stand-in.
/// </summary>
public sealed class SteppedAsksFixture : IDisposable
{
    private readonly RunEstate run = new(keepOperatorKey: true, recordFormat: 3);

    public SteppedAsksFixture()
    {
        string op = run.Estate.OperatorId;
        Builder = run.Estate.BuilderId;
        Helper = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Helper","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        DateTimeOffset until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds());
        Goal = StandingAgentRoutingTests.Standing(run, budget: new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 6 }, until: RecordParser.FormatTimestamp(until), holders: [Helper]);

        // The helper runs under the builder's harness and signs with a device of its own held here, which the operator admits before its
        // key leaves this machine, as the estate's other keys do.
        JsonObject helper = JsonNode.Parse(run.Estate.Load().CurrentPrincipal(Builder)!.Body.GetRawText())!.AsObject();
        helper["name"] = "Helper";
        helper["roles"] = new JsonArray("contributor");
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, JsonSerializer.SerializeToElement(helper), Supersedes: Helper)));
        string device = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(Helper, "Helper's device", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(device, op, "The helper signs its attempts and results.")));
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);

        // The builder takes the goal's steps under local-engineering.
        string basis = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json($$"""{"title":"Local engineering","statement":"The builder dispatches under local-engineering.","summary":"A grant.","synthesis":"A grant of local-engineering.","effects":["local-engineering"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T12:00:00Z","stake":"medium","notAuthorized":[]}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("authority", op, RunEstate.Json($$"""{"grantee":"{{Builder}}","scope":["local-engineering"],"reserved":[],"conditions":[],"basis":"{{basis}}"}"""))));

        // The runtime's clock an hour before the until time: the builder's goal step asks for the renewal and dispatches nothing.
        run.Service.FlowClock = new FixedClock(until.AddHours(-1));
        GoalStepResponse renewal = StandingAgentRoutingTests.StepAs(run, Goal, "renewal", Builder);
        Assert.True(renewal.Outcome == "ask" && renewal.Run is null, renewal.Message);
        Renewal = renewal.Ask!.Id;
        run.Service.FlowClock = TimeProvider.System;

        // Three of the helper's runs exit cleanly with blocked results, which pause the agent; the builder's goal step then asks the operator.
        string? assignment = null;
        for (int i = 0; i < 3; i++)
        {
            GoalStepResponse step = StandingAgentRoutingTests.StepAs(run, Goal, "blocked-" + i, Builder);
            Assert.True(step.Run is not null && step.Principal == Helper, step.Message);
            Assert.Equal("observed-success", run.Settled(step.Run!.Run).Status);
            assignment ??= step.Assignment!.Id;
            string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, Helper, "research repository, a work branch", "Research."))).Id;
            RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, Helper, JsonSerializer.SerializeToElement(new JsonObject
            {
                ["outcome"] = "blocked", ["summary"] = "Blocked on a source.", ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
                ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Find another source.",
            }))));
        }

        GoalStepResponse pause = StandingAgentRoutingTests.StepAs(run, Goal, "pause", Builder);
        Assert.True(pause.Outcome == "ask", pause.Message);
        Pause = pause.Ask!.Id;
        Assignment = assignment!;
    }

    /// <summary>The estate's root.</summary>
    public string Root => run.Estate.Root;

    /// <summary>The runtime serving it.</summary>
    public EstateService Service => run.Service;

    /// <summary>The operator, whom both asks are put to.</summary>
    public string Operator => run.Estate.OperatorId;

    /// <summary>The builder, which took the goal's steps and wrote both asks.</summary>
    public string Builder { get; }

    /// <summary>The helper agent, which carried the occurrences and the failed runs.</summary>
    public string Helper { get; }

    /// <summary>The standing agent's goal.</summary>
    public string Goal { get; }

    /// <summary>The renewal ask the builder's goal step wrote.</summary>
    public string Renewal { get; }

    /// <summary>The failure pause's ask the builder's goal step wrote.</summary>
    public string Pause { get; }

    /// <summary>The assignment that carries the occurrence the helper's failed runs carried.</summary>
    public string Assignment { get; }

    internal RunEstate Run => run;

    public void Dispose() => run.Dispose();

    /// <summary>A fixed clock, the runtime's controlled time.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
