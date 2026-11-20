// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

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
