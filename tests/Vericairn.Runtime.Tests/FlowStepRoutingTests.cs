// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A run moved one step per command (the estate's design/flow.md §§ 6, 8 and 10): each work and review step routed by its role through
/// the goal's roles, an agent's step as an assignment naming the step with its run, a person's as an assignment and an ask, each brief
/// pinning its inputs by hash; a stale step rerun from a fresh assignment; a person taking a step over; reserved steps never dispatched;
/// and a run whose flow does not resolve here shown and never run. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class FlowStepRoutingTests
{
    [Fact]
    public void ARunMovesOneStepPerCommandRoutedByEachStepsRole()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string goal = Run(run);

        // The run's position and routes: draft is next, routed by the writer role to the builder's agent through its launcher.
        NextResponse next = run.Service.GoalNext(goal)!;
        Assert.Equal("draft", next.Json.GetProperty("next").GetString());
        Assert.Equal("example/song", next.Json.GetProperty("flow").GetProperty("pin").GetProperty("id").GetString());
        Assert.Equal("agent", Route(next, "draft").GetProperty("state").GetString());
        Assert.Contains("Next steps of goal Release the song", next.Text, StringComparison.Ordinal);

        // goal step: draft's assignment names its step, carries the flow's never lines, and its run starts.
        GoalStepResponse first = Step(run, goal, "draft");
        Assert.True(first.Ok, first.Message);
        Assert.Equal(("run", "draft", "agent", builder), (first.Outcome, first.Step, first.Route, first.Principal));
        RecordEnvelope drafting = run.Estate.Load().ById[first.Assignment!.Id];
        Assert.Equal("draft", drafting.Body.GetProperty("step").GetProperty("key").GetString());
        Assert.False(drafting.Body.GetProperty("step").TryGetProperty("inputs", out _));
        Assert.Contains(drafting.Body.GetProperty("brief").GetProperty("prohibitedEffects").EnumerateArray(), p => p.GetString() == "never publish without the operator");
        Assert.StartsWith("draft Step draft: the work step of flow example/song 1.0.0", drafting.Body.GetProperty("brief").GetProperty("outcome").GetString(), StringComparison.Ordinal);
        Assert.Equal("observed-success", run.Settled(first.Run!.Run).Status);
        Assert.Equal("work/song-1-draft", AgentRuns.Find(run.Estate.Load(), first.Run.Run)!.Target("branch"));

        // The agent holds draft: its position says so, and the step goes on with it until its result.
        Assert.StartsWith($"Test Builder holds it by assignment {drafting.Id}", Step(run.Service.Goal(goal)!, "draft").GetProperty("reasons")[0].GetString(), StringComparison.Ordinal);
        RecordWritten lyrics = Done(run, drafting.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));

        // review-lyrics is the reviewer's, a person: assigned to the operator and asked of them, pinning the lyrics by their result.
        GoalStepResponse second = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(second.Ok, second.Message);
        Assert.Equal(("ask", "review-lyrics", "person", op), (second.Outcome, second.Step, second.Route, second.Principal));
        RecordEnvelope reviewing = run.Estate.Load().ById[second.Assignment!.Id];
        JsonElement pin = Assert.Single(reviewing.Body.GetProperty("step").GetProperty("inputs").EnumerateArray());
        Assert.Equal(("lyrics", "result", lyrics.Id, lyrics.Hash), (pin.GetProperty("name").GetString(), pin.GetProperty("type").GetString(), pin.GetProperty("id").GetString(), pin.GetProperty("hash").GetString()));
        Assert.Contains(reviewing.Body.GetProperty("brief").GetProperty("sources").EnumerateArray(), s => s.GetProperty("id").GetString() == lyrics.Id);
        Assert.StartsWith($"Review result {lyrics.Id} at {lyrics.Hash}, of step draft", reviewing.Body.GetProperty("brief").GetProperty("reviewCriteria")[0].GetString(), StringComparison.Ordinal);
        RecordEnvelope ask = run.Estate.Load().ById[second.Ask!.Id];
        Assert.Equal(reviewing.Id, EstateModel.BodyString(ask, "assignment"));
        Assert.Contains($"; assignment {reviewing.Id}'s brief, which pins the step's inputs, with vericairn work brief SONG-1 is everything it needs", EstateModel.BodyString(ask, "recommendation"), StringComparison.Ordinal);
        Review(run, lyrics);

        // master: the builder again, its brief pinning the lyrics.
        GoalStepResponse third = Step(run, goal, "master");
        Assert.True(third.Ok, third.Message);
        Assert.Equal(("run", "master"), (third.Outcome, third.Step));
        RecordEnvelope mastering = run.Estate.Load().ById[third.Assignment!.Id];
        Assert.Equal(lyrics.Id, Assert.Single(mastering.Body.GetProperty("step").GetProperty("inputs").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("observed-success", run.Settled(third.Run!.Run).Status);
        Done(run, mastering.Id, ("master", FlowFixture.Hash('b'), null));

        // The lyrics change after the master used them: draft's new result needs its review again, and the master is stale, waiting on it.
        RecordWritten changed = Done(run, drafting.Id, ("lyrics", FlowFixture.Hash('c'), "text/markdown"));
        JsonElement flow = run.Service.Goal(goal)!.Json.GetProperty("flow");
        JsonElement master = Assert.Single(flow.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "master");
        Assert.Equal("stale", master.GetProperty("state").GetString());
        Assert.Contains(master.GetProperty("reasons").EnumerateArray(), r => r.GetString() == "it waits on review-lyrics, which is stale");
        Assert.StartsWith("its input lyrics changed after use", master.GetProperty("inputs")[0].GetProperty("change").GetString(), StringComparison.Ordinal);
        Assert.Contains("master  stale", run.Service.Goal(goal)!.Text, StringComparison.Ordinal);

        // The review step reruns first: a fresh assignment to the reviewer, pinning the new lyrics; its review of them clears it.
        GoalStepResponse again = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "review-lyrics"), (again.Outcome, again.Step));
        Assert.NotEqual(reviewing.Id, again.Assignment!.Id);
        Assert.Equal(changed.Id, Assert.Single(run.Estate.Load().ById[again.Assignment.Id].Body.GetProperty("step").GetProperty("inputs").EnumerateArray()).GetProperty("id").GetString());
        Review(run, changed);

        // Rerun from a fresh assignment whose brief pins the lyrics as they are now.
        GoalStepResponse rerun = Step(run, goal, "rerun");
        Assert.True(rerun.Ok, rerun.Message);
        Assert.Equal(("run", "master"), (rerun.Outcome, rerun.Step));
        Assert.NotEqual(mastering.Id, rerun.Assignment!.Id);
        Assert.Equal(changed.Id, Assert.Single(run.Estate.Load().ById[rerun.Assignment.Id].Body.GetProperty("step").GetProperty("inputs").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("observed-success", run.Settled(rerun.Run!.Run).Status);

        // A person takes the master over: the hand-over carries the same step and pins, and routing shows the step with them.
        string person = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"human","name":"Test Engineer","roles":["contributor"]}""")))).Id;
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(rerun.Assignment.Id, op, person));
        Assert.True(taken.Ok, taken.Message);
        RecordEnvelope handover = run.Estate.Load().ById[taken.Assignment!.Id];
        Assert.Equal(run.Estate.Load().ById[rerun.Assignment.Id].Body.GetProperty("step").GetRawText(), handover.Body.GetProperty("step").GetRawText());
        Assert.Equal(("assigned", person), (Route(run.Service.GoalNext(goal)!, "master").GetProperty("state").GetString(), Route(run.Service.GoalNext(goal)!, "master").GetProperty("principal").GetString()));
        Assert.Contains($"no longer holds step master of work order SONG-1", run.Service.WorkTakeover(new TakeoverRequest(rerun.Assignment.Id, op)).Message, StringComparison.Ordinal);
        RecordWritten remastered = Done(run, handover.Id, ("master", FlowFixture.Hash('d'), null), by: person);

        // Each track's clip is its own step instance: two commands, two assignments naming the item.
        GoalStepResponse intro = Step(run, goal, "intro");
        GoalStepResponse outro = Step(run, goal, "outro");
        Assert.True(outro.Ok, outro.Message);
        Assert.Equal(("shorts[intro]", "shorts[outro]"), (intro.Step, outro.Step));
        Assert.Equal("outro", run.Estate.Load().ById[outro.Assignment!.Id].Body.GetProperty("step").GetProperty("instance").GetString());
        run.Settled(intro.Run!.Run);
        run.Settled(outro.Run!.Run);
        Done(run, intro.Assignment!.Id, ("clip", FlowFixture.Hash('1'), null));
        Done(run, outro.Assignment.Id, ("clip", FlowFixture.Hash('2'), null));

        // Listening is a person's role: routed to the operator, never to the agent, even though the roster could name one.
        GoalStepResponse listen = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "listen", "person", op), (listen.Outcome, listen.Step, listen.Route, listen.Principal));
        Review(run, remastered);

        // Acceptance and publication are reserved acts: goal step dispatches neither, and says why.
        GoalStepResponse reserved = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource, Command(run, "reserved")));
        Assert.Equal("none", reserved.Outcome);
        Assert.Contains("accept is stopped: a reserved act, accept-release", reserved.Message, StringComparison.Ordinal);
        Assert.Null(reserved.Assignment);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnItemsToolIsTheLauncherOfTheRunThatProducedIt()
    {
        foreach ((string tool, string state) in new[] { ("claude-code", "satisfied"), ("codex", "unproven") })
        {
            using var run = new RunEstate(recordFormat: 3);
            JsonObject song = FlowFixture.Song();
            song["steps"]![0]!["outputs"]![0]!["provenance"] = new JsonObject { ["holder"] = "agent", ["tool"] = tool };
            string goal = Run(run, song);
            GoalStepResponse first = Step(run, goal, "draft-" + tool);
            Assert.True(first.Ok, first.Message);
            run.Settled(first.Run!.Run);
            Done(run, first.Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
            JsonElement item = Step(run.Service.Goal(goal)!, "draft").GetProperty("items")[0];
            Assert.Equal(state, item.GetProperty("state").GetString());
            if (state != "satisfied")
            {
                Assert.Equal("it is to be produced with tool codex, and the record that produced it shows claude-code", item.GetProperty("reasons")[0].GetString());
            }
        }
    }

    [Fact]
    public void AnAgentInAPersonsRoleIsRefusedAndAnUnresolvableRunIsNeverStepped()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        (string Order, TemplateDefinition Flow) prepared = Setup(run, null);
        WriteOutcome named = run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(Body(run, prepared, run.Estate.BuilderId))));
        Assert.False(named.Ok);
        Assert.Contains(named.Problem!.Findings, f => f.Code == "flow.holder");

        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(Body(run, prepared, op))))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(op)));
        File.Delete(Directory.GetFiles(Path.Combine(run.Estate.Root, "docs", "templates")).Single());
        GoalStepResponse refused = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource, Command(run, "never")));
        Assert.False(refused.Ok);
        Assert.Equal("refused", refused.Problem!.Code);
        Assert.Contains("does not resolve here", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        Assert.Contains("not run here", run.Service.Goal(goal)!.Text, StringComparison.Ordinal);
        Assert.False(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("available").GetBoolean());
        Assert.DoesNotContain(run.Service.Records("assignment"), a => run.Estate.Load().ById[a.Id].Body.TryGetProperty("step", out _));
    }

    [Fact]
    public void AFlowStepsPersonRoleRefusesTheAgentCandidate()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        JsonObject song = FlowFixture.Song();
        JsonObject draft = song["steps"]!.AsArray()[0]!.AsObject();
        draft["role"] = "listener";
        (string Order, TemplateDefinition Flow) prepared = Setup(run, song);
        JsonObject body = Body(run, prepared, op);
        body["roles"]!.AsArray().RemoveAt(2);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, JsonSerializer.SerializeToElement(new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("listener", run.Estate.BuilderId, op)) }))));
        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(body)))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(op)));
        JsonElement route = Route(run.Service.GoalNext(goal)!, "draft");
        Assert.Equal(("person", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
        Assert.Contains(route.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("kind").GetString() == "agent"
            && c.GetProperty("reasons").EnumerateArray().Any(r => r.GetString() == "the flow marks the listener role a person's, and an agent never holds it"));
    }

    [Fact]
    public void AnAgentsRunOnAReservedStepsAssignmentIsRefusedBeforeItsIntent()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        JsonObject song = FlowFixture.Song();
        song["steps"]![0]!["reserved"] = "security-review";
        string goal = Run(run, song);
        EstateModel model = run.Estate.Load();
        RecordEnvelope order = model.ById[model.ById[goal].Body.GetProperty("plan").GetProperty("workOrders")[0].GetString()!];

        // The operator, a person, holds the reserved step by its assignment; a run of an agent on that assignment is refused, whoever asks.
        var body = new JsonObject
        {
            ["contributor"] = op, ["step"] = new JsonObject { ["key"] = "draft" },
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Carry step draft.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("song repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("the lyrics"),
                ["reviewCriteria"] = new JsonArray("the lyrics exist"), ["recovery"] = "Carry it again.",
            },
        };
        string assignment = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", op, RunEstate.Json(body.ToJsonString()), $"work-order:{order.Id}:{order.ComputedHash}"))).Id;
        int runs = AgentRuns.All(run.Estate.Load()).Count;
        RunResponse refused = run.Service.RunStart(new RunStartRequest(assignment, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 30, Settings: Command(run, "reserved-run")));
        Assert.False(refused.Ok);
        Assert.Contains("Step draft of work order SONG-1 is a reserved act, security-review: no agent's run carries it, and the operator acts on it.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(runs, AgentRuns.All(run.Estate.Load()).Count);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ARevisionThatSwapsTheFlowStartsFromActivationAndLeavesTheOldStepsAssignmentUnroutable()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = Run(run);
        GoalStepResponse first = Step(run, goal, "swap-draft");
        Assert.True(first.Ok, first.Message);
        run.Settled(first.Run!.Run);
        string drafting = first.Assignment!.Id;

        // Version 2.0.0 of the flow names its first step write, not draft; the run's revision pins it.
        JsonObject second = FlowFixture.Song();
        second["version"] = "2.0.0";
        foreach (JsonObject step in second["steps"]!.AsArray().Select(s => s!.AsObject()))
        {
            if (step["key"]!.GetValue<string>() == "draft")
            {
                step["key"] = "write";
            }

            foreach (JsonObject input in (step["inputs"] as JsonArray ?? []).Select(i => i!.AsObject()).Where(i => i["step"]?.GetValue<string>() == "draft"))
            {
                input["step"] = "write";
            }
        }

        TemplateDefinition installed = FlowFixture.Install(run.Estate, second);
        JsonObject body = JsonNode.Parse(run.Estate.Load().ById[goal].Body.GetRawText())!.AsObject();
        body["plan"]!["flow"]!["pin"] = FlowFixture.Pin(installed);
        string revised = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, RunEstate.Json(body.ToJsonString()), goal))).Id;

        // The revision starts again from its activation: no step of it is taken before then.
        GoalStepResponse early = run.Service.GoalStep(revised, new GoalStepRequest(op, run.Resource, Command(run, "swap-early"), WallMinutes: 30));
        Assert.False(early.Ok);
        Assert.Contains("a step is taken only for an active goal", early.Message, StringComparison.Ordinal);
        RunEstate.Ok(run.Service.GoalActivate(revised, new GoalActionRequest(op)));

        // The old assignment names a step the run's flow no longer declares: it holds nothing, routing reaches write afresh, and no run
        // carries the old assignment.
        EstateModel model = run.Estate.Load();
        Assert.Null(Routing.RoleOf(model, model.ById[drafting]));
        JsonElement write = Route(run.Service.GoalNext(revised)!, "write");
        Assert.Equal("agent", write.GetProperty("state").GetString());
        Assert.Null(Step(run.Service.Goal(revised)!, "write").GetProperty("assignment").GetString());
        RunResponse refused = run.Service.RunStart(new RunStartRequest(drafting, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 30, Settings: Command(run, "swap-old")));
        Assert.False(refused.Ok);
        Assert.Contains($"Assignment {drafting} names step draft, which the run's flow example/song 2.0.0 at the goal's current revision does not declare", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>Installs the song flow, a work order and a run of it the operator sponsors and activates; returns the goal's id.</summary>
    internal static string Run(RunEstate run, JsonObject? flow = null, Action<JsonObject>? change = null)
    {
        string op = run.Estate.OperatorId;
        (string Order, TemplateDefinition Flow) prepared = Setup(run, flow);
        JsonObject body = Body(run, prepared, op);
        change?.Invoke(body);
        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(body)))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(op)));
        return goal;
    }

    /// <summary>The builder as a Claude Code agent, the flow installed, and the run's work order.</summary>
    private static (string Order, TemplateDefinition Flow) Setup(RunEstate run, JsonObject? flow)
    {
        string op = run.Estate.OperatorId;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}"""), Supersedes: run.Estate.BuilderId)));
        TemplateDefinition installed = FlowFixture.Install(run.Estate, flow ?? FlowFixture.Song());
        string order = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json("""{"key":"SONG-1","kind":"ordinary","title":"Produce the song","intent":"Produce a song and release it.","scope":["path:song/"],"exitQuestion":"Is the song released?","inputs":[],"deliverables":["the released song"]}""")))).Id;
        return (order, installed);
    }

    /// <summary>The run's goal: its roles, its budget, and its plan naming the flow, the subject and the run's work order.</summary>
    private static JsonObject Body(RunEstate run, (string Order, TemplateDefinition Flow) prepared, string listener)
    {
        string op = run.Estate.OperatorId;
        return new JsonObject
        {
            ["title"] = "Release the song", ["objective"] = "Produce a song and release it.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = FlowFixture.Marker("PUBLISHED") }), ["constraints"] = new JsonArray("nothing outside the song"),
            ["kind"] = "finite", ["sponsor"] = op, ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 5, ["concurrency"] = 2 },
            ["roles"] = new JsonArray(FlowFixture.Role("writer", run.Estate.BuilderId), FlowFixture.Role("reviewer", op), FlowFixture.Role("listener", listener), FlowFixture.Role("decider", op)),
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(prepared.Order),
                ["flow"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(prepared.Flow),
                    ["subject"] = new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = prepared.Order }, ["tracks"] = new JsonArray("intro", "outro") },
                },
            },
            ["inputs"] = new JsonArray(),
        };
    }

    /// <summary>One step, its run capped at thirty minutes so that two runs of the goal's budget may be in flight together.</summary>
    internal static GoalStepResponse Step(RunEstate run, string goal, string report) =>
        run.Service.GoalStep(goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, Command(run, report), WallMinutes: 30));

    internal static Dictionary<string, string> Command(RunEstate run, string report) =>
        new(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report(report)) };

    private static JsonElement Step(GoalResponse goal, string key) =>
        Assert.Single(goal.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null);

    private static JsonElement Route(NextResponse next, string key) =>
        Assert.Single(next.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null).GetProperty("route");

    /// <summary>An attempt and a completed result on an assignment, by its contributor, naming outputs by name and hash.</summary>
    internal static RecordWritten Done(RunEstate run, string assignment, (string Name, string Hash, string? MediaType) output, string? by = null)
    {
        by ??= run.Estate.BuilderId;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, by, "song repository, a work branch", "Carry the step."))).Id;
        var named = new JsonObject { ["name"] = output.Name, ["hash"] = output.Hash };
        if (output.MediaType is not null)
        {
            named["mediaType"] = output.MediaType;
        }

        var body = new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = new JsonArray(named), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Hand it on.",
        };
        return RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, by, JsonSerializer.SerializeToElement(body))));
    }

    /// <summary>The operator's review of a result, with outcome pass.</summary>
    internal static void Review(RunEstate run, RecordWritten result) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", run.Estate.OperatorId,
            RunEstate.Json("""{"independence":"operator","outcome":"pass","summary":"Read the exact result.","findings":[]}"""), $"result:{result.Id}:{result.Hash}")));
}
