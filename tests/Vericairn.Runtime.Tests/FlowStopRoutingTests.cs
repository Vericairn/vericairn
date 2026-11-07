// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A run's stops taken one step per command (the estate's design/flow.md § 8; design/release-procedure.md §§ 6 and 8;
/// design/cohesion-contract.md § 5): goal step puts a reserved step to the operator as an operator request carrying its exact command,
/// computed only when the step is next and never for a later one; the request is withdrawn when the records change and revised by the
/// next goal step; the operator's own act completes the step while neither an agent's close nor its record of the act does; at most three
/// stops reach one person at a time; and status, goal show and goal next show the same stops.
/// </summary>
[Collection("home")]
public class FlowStopRoutingTests
{
    [Fact]
    public void GoalStepPutsAReservedStepToTheOperatorWithItsExactCommandOnlyWhenItIsNext()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, change: Options);
        string work = WorkOf(run, goal);
        string accept = $"example-provider accept --recording {work} --goal {goal}";
        string publish = $"example-provider publish --recording {work} --work {work}";
        (_, _, RecordWritten master) = ToAcceptance(run, goal);

        // While listening waits on the operator, no reserved command is computed anywhere the run is shown.
        string shown = Shown(run, goal);
        Assert.DoesNotContain(accept, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, shown, StringComparison.Ordinal);
        FlowStepRoutingTests.Review(run, master);

        // Acceptance is next: goal next routes it to the operator as a request with its command; publication, later, shows none.
        NextResponse next = run.Service.GoalNext(goal)!;
        Assert.Equal("accept", next.Json.GetProperty("next").GetString());
        JsonElement route = Route(next, "accept");
        Assert.Equal(("request", op, "accept-release"), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString(), route.GetProperty("reserved").GetString()));
        Assert.Empty(route.GetProperty("candidates").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, Route(next, "publish").ValueKind);
        shown = Shown(run, goal);
        Assert.Contains(accept, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, shown, StringComparison.Ordinal);
        AssertTheSameStops(run, goal);

        // goal step records the operator request and nothing else: no assignment, ask or run, and its one item is the exact command.
        int assignments = run.Service.Records("assignment").Count;
        GoalStepResponse taken = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(taken.Ok, taken.Message);
        Assert.Equal(("request", "accept", "request", op), (taken.Outcome, taken.Step, taken.Route, taken.Principal));
        Assert.Null(taken.Assignment);
        Assert.Null(taken.Ask);
        Assert.Null(taken.Run);
        Assert.Equal(assignments, run.Service.Records("assignment").Count);
        Assert.Contains(accept, taken.Message, StringComparison.Ordinal);
        EstateModel model = run.Estate.Load();
        RecordEnvelope request = model.ById[taken.Request!.Id];
        Assert.Equal("operator-request", request.Type);
        JsonElement content = Assert.Single(request.Body.GetProperty("content").EnumerateArray());
        Assert.Equal(("command", accept), (content.GetProperty("kind").GetString(), content.GetProperty("text").GetString()));
        JsonElement step = request.Body.GetProperty("flowStep");
        Assert.Equal((goal, "accept", false), (step.GetProperty("goal").GetString(), step.GetProperty("key").GetString(), step.TryGetProperty("instance", out _)));
        Assert.Equal(("work-order", work), (request.Subject!.Type, request.Subject.Id));
        Assert.Equal((op, op, "action"), (EstateModel.BodyString(request, "to"), EstateModel.BodyString(request, "askedBy"), EstateModel.BodyString(request, "kind")));
        Assert.StartsWith("Step accept, Step accept, of the run of flow example/song 1.0.0 is next", EstateModel.BodyString(request, "why"), StringComparison.Ordinal);
        Assert.DoesNotContain(publish, request.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);

        // The request is delivered on the operator's queue, in status and in required attention; goal step records nothing more for it.
        OperatorRequestsResponse queue = run.Service.OperatorRequests();
        Assert.Equal(request.Id, Assert.Single(queue.Json.GetProperty("delivered").EnumerateArray()).GetProperty("revision").GetString());
        Assert.Contains("A run's reserved step; its command, checked against the records: " + accept + "\n  The requester's words, not checked:\n", queue.Text, StringComparison.Ordinal);

        // operator list shows the checked command first and labels the requester's prose, read with the runtime's clock.
        OperatorActionsResponse actions = run.Service.OperatorActions();
        Assert.Contains("A run's reserved step; its command, checked against the records: " + accept, actions.Text, StringComparison.Ordinal);
        StatusResponse status = run.Service.Status();
        JsonElement stop = Assert.Single(Assert.Single(status.Json.GetProperty("runs").EnumerateArray()).GetProperty("stops").EnumerateArray());
        Assert.Equal(("reserved", "delivered", request.Id, JsonValueKind.Null), (stop.GetProperty("kind").GetString(), stop.GetProperty("delivery").GetString(), stop.GetProperty("record").GetProperty("id").GetString(), stop.GetProperty("command").ValueKind));
        Assert.Contains("    command: " + accept, status.Text, StringComparison.Ordinal);
        JsonElement noticed = Assert.Single(status.Json.GetProperty("attention").EnumerateArray(), a => a.TryGetProperty("record", out JsonElement id) && id.GetString() == request.Id);
        Assert.StartsWith("A run's reserved step; its command, checked against the records: " + accept + "\n  The requester's words, not checked:\n", noticed.GetProperty("text").GetString(), StringComparison.Ordinal);

        // A client that cannot read the runtime's queue cannot check a step's request against the records, so it never shows one open.
        EstateService api = run.Service;
        var fallback = new AttentionDerivation(api.Records, api.Record, () => api.Progress(), api.Asks, api.Goals, () => api.Runs());
        Assert.DoesNotContain(fallback.AskItems(null), a => a.Record == request.Id);
        AssertTheSameStops(run, goal);
        GoalStepResponse again = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("none", again.Outcome);
        Assert.Contains($"accept is stopped, requested: a reserved act, accept-release: operator request {request.Id} waits on", again.Message, StringComparison.Ordinal);

        // The operator's own act completes acceptance: the request is settled, open and no longer delivered, and publication is next.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, Marker("ACCEPTED"))));
        queue = run.Service.OperatorRequests();
        Assert.Empty(queue.Json.GetProperty("delivered").EnumerateArray());
        JsonElement settled = Assert.Single(queue.Json.GetProperty("withdrawn").EnumerateArray());
        Assert.Equal((request.Id, "settled"), (settled.GetProperty("revision").GetString(), settled.GetProperty("state").GetString()));
        Assert.DoesNotContain(run.Service.Status().Json.GetProperty("attention").EnumerateArray(), a => a.TryGetProperty("record", out JsonElement id) && id.GetString() == request.Id);
        GoalStepResponse publishing = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("request", "publish"), (publishing.Outcome, publishing.Step));
        RecordEnvelope publication = run.Estate.Load().ById[publishing.Request!.Id];
        Assert.Equal(("publication", publish), (EstateModel.BodyString(publication, "kind"), publication.Body.GetProperty("content")[0].GetProperty("text").GetString()));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AReservedStepsRequestIsWithdrawnWhenTheRecordsChangeAndRevisedByTheNextStep()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        JsonObject song = FlowFixture.Song();
        JsonObject acceptStep = song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "accept");
        acceptStep["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master"));
        acceptStep["action"] = "example-provider accept --master {input.master.hash} --result {input.master.id}";
        string goal = FlowStepRoutingTests.Run(run, song, Options);
        (RecordWritten lyrics, RecordWritten mastering, RecordWritten master) = ToAcceptance(run, goal, listen: false);

        // The lyrics feed the master, which feeds acceptance: their independent review is routed first, to the operator.
        Assert.Equal("person", Assert.Single(run.Service.GoalNext(goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("review").ValueKind == JsonValueKind.Object && s.GetProperty("key").GetString() == "draft").GetProperty("route").GetProperty("state").GetString());
        FlowStepRoutingTests.Review(run, lyrics);
        FlowStepRoutingTests.Review(run, master);
        GoalStepResponse first = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("request", first.Outcome);
        Assert.Contains($"--master {FlowFixture.Hash('b')} --result {master.Id}", first.Message, StringComparison.Ordinal);

        // A new master before the operator acts: acceptance no longer waits, and the request leaves every delivery, open as it was.
        RecordWritten remastered = FlowStepRoutingTests.Done(run, mastering.Id, ("master", FlowFixture.Hash('c'), null));
        OperatorRequestsResponse queue = run.Service.OperatorRequests();
        Assert.Empty(queue.Json.GetProperty("delivered").EnumerateArray());
        JsonElement withdrawn = Assert.Single(queue.Json.GetProperty("withdrawn").EnumerateArray());
        Assert.Equal((first.Request!.Id, "withdrawn"), (withdrawn.GetProperty("revision").GetString(), withdrawn.GetProperty("state").GetString()));
        Assert.StartsWith("step accept is ", withdrawn.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Stops(run.Service.Status().Json), s => s.GetProperty("kind").GetString() == "reserved");
        Assert.DoesNotContain(FlowFixture.Hash('b'), Route(run.Service.GoalNext(goal)!, "accept").GetRawText(), StringComparison.Ordinal);

        // Reviewed again, acceptance is next with the command the records give now, and goal step revises the request to carry it.
        FlowStepRoutingTests.Review(run, remastered);
        string second = $"example-provider accept --master {FlowFixture.Hash('c')} --result {remastered.Id}";
        Assert.Equal("request", Route(run.Service.GoalNext(goal)!, "accept").GetProperty("state").GetString());
        GoalStepResponse revised = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("request", revised.Outcome);
        RecordEnvelope revision = run.Estate.Load().ById[revised.Request!.Id];
        Assert.Equal(first.Request.Id, revision.Supersedes);
        Assert.Equal(second, revision.Body.GetProperty("content")[0].GetProperty("text").GetString());
        queue = run.Service.OperatorRequests();
        Assert.Equal(revision.Id, Assert.Single(queue.Json.GetProperty("delivered").EnumerateArray()).GetProperty("revision").GetString());
        Assert.False(queue.Json.TryGetProperty("withdrawn", out _));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void NoAgentCompletesAReservedStepOrStandsInForTheOperatorsAct()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string goal = FlowStepRoutingTests.Run(run, change: Options);
        (_, _, RecordWritten master) = ToAcceptance(run, goal);
        FlowStepRoutingTests.Review(run, master);

        // An agent without the step-taking authority records no request; whoever takes the run's steps does, and nothing is dispatched.
        GoalStepResponse refused = run.Service.GoalStep(goal, new GoalStepRequest(builder));
        Assert.Equal("refused", refused.Problem!.Code);
        GoalStepResponse taken = run.Service.GoalStep(goal, new GoalStepRequest(op));
        RecordEnvelope request = run.Estate.Load().ById[taken.Request!.Id];

        // An agent closes the request citing the operator's words: the request is closed, acceptance still waits on the act, and since
        // the close is not the addressed operator's own, the step's request is due again.
        WriteOutcome closed = run.Service.OperatorRequestDone(request.Id, new OperatorRequestCloseRequest(builder, request.ComputedHash,
            OperatorWords: new OperatorRequestWords(op, "I have done it.", "Operator conversation, message 4", request.At)));
        Assert.True(closed.Ok, closed.Problem?.Message);
        JsonElement accept = Step(run.Service.Goal(goal)!, "accept");
        Assert.Equal(("stopped", "reserved"), (accept.GetProperty("state").GetString(), accept.GetProperty("stop").GetString()));
        JsonElement stop = Assert.Single(Stops(run.Service.Status().Json));
        Assert.Equal(("due", JsonValueKind.Null), (stop.GetProperty("delivery").GetString(), stop.GetProperty("record").ValueKind));
        Assert.Equal("request", Route(run.Service.GoalNext(goal)!, "accept").GetProperty("state").GetString());

        // The addressed operator's own dismissal is not asked again while the command holds.
        GoalStepResponse again = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("request", again.Outcome);
        RecordEnvelope renewed = run.Estate.Load().ById[again.Request!.Id];
        Assert.True(run.Service.OperatorRequestDismiss(renewed.Id, new OperatorRequestCloseRequest(op, renewed.ComputedHash, Reason: "Not now.")).Ok);
        Assert.Equal("closed", Route(run.Service.GoalNext(goal)!, "accept").GetProperty("state").GetString());
        Assert.Equal("none", run.Service.GoalStep(goal, new GoalStepRequest(op)).Outcome);

        // An agent's record of the act does not count: the step still waits, and says why.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", builder, Marker("ACCEPTED"))));
        accept = Step(run.Service.Goal(goal)!, "accept");
        Assert.Equal("stopped", accept.GetProperty("state").GetString());
        Assert.Contains(accept.GetProperty("reasons").EnumerateArray(), r => r.GetString()!.Contains("recorded by a principal who does not hold the act, which does not count", StringComparison.Ordinal));
        Assert.Equal("not-met", accept.GetProperty("done")[0].GetProperty("verdict").GetString());
    }

    [Fact]
    public void AtMostThreeStopsOfARunReachOnePersonAtATime()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        JsonObject flow = new()
        {
            ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = "example/clips", ["version"] = "1.0.0", ["purpose"] = "Cut a clip of each track.",
            ["subject"] = new JsonObject
            {
                ["summary"] = "a recording",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "recording", ["type"] = "record", ["recordType"] = "work-order" }, new JsonObject { ["name"] = "tracks", ["type"] = "list" }),
            },
            ["roles"] = new JsonArray(new JsonObject { ["id"] = "editor", ["holder"] = "person", ["brief"] = "Cut the clip by hand." }),
            ["options"] = new JsonArray(new JsonObject { ["id"] = "review", ["values"] = new JsonArray("independent", "none"), ["default"] = "none" }),
            ["steps"] = new JsonArray(FlowFixture.Step("clip", "work", "editor", each: "tracks", outputs: new JsonArray(FlowFixture.FileItem("clip")))),
        };
        string goal = FlowStepRoutingTests.Run(run, flow, body =>
        {
            body["roles"] = new JsonArray(FlowFixture.Role("editor", op));
            body["plan"]!["flow"]!["subject"]!["tracks"] = new JsonArray("intro", "verse", "chorus", "bridge", "outro");
        });

        // Five clips wait on the operator at once: three are routed to them, and two wait queued in the flow's order.
        Assert.Equal(["person", "person", "person", "queued", "queued"], Routes(run, goal));
        Assert.Equal(["due", "due", "due", "queued", "queued"], Stops(run.Service.Status().Json).Select(s => s.GetProperty("delivery").GetString()));

        // goal step delivers three; the fourth stays queued, and goal step dispatches nothing for it.
        var held = new List<GoalStepResponse>();
        for (int index = 0; index < 3; index++)
        {
            held.Add(run.Service.GoalStep(goal, new GoalStepRequest(op)));
            Assert.Equal("ask", held[^1].Outcome);
        }

        Assert.Equal(["clip[intro]", "clip[verse]", "clip[chorus]"], held.Select(h => h.Step));
        Assert.Equal(["assigned", "assigned", "assigned", "queued", "queued"], Routes(run, goal));
        GoalStepResponse queued = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("none", queued.Outcome);
        Assert.Contains("clip[bridge] is stopped, queued", queued.Message, StringComparison.Ordinal);

        // One settles: the next clip in order is delivered, and the last still waits.
        FlowStepRoutingTests.Done(run, held[0].Assignment!.Id, ("clip", FlowFixture.Hash('1'), null), by: op);
        GoalStepResponse bridge = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "clip[bridge]"), (bridge.Outcome, bridge.Step));
        Assert.Equal(["assigned", "assigned", "assigned", "queued"], Routes(run, goal).Skip(1));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void CanonicalStatusOfARunIsByteIdenticalWheneverItIsReadWhileLiveReadsFollowTheClock()
    {
        // Publication falls due a day after acceptance, so a live read before then and one after it differ.
        JsonObject song = FlowFixture.Song();
        song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "publish")["when"] =
            new JsonObject { ["due"] = new JsonObject { ["after"] = "P1D", ["from"] = "accept" } };
        using var run = new FlowFixture(song, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        run.Review(run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null)));
        RecordEnvelope accepted = run.Marked("ACCEPTED");
        string publish = $"example-provider publish --recording {run.Work.Id} --work {run.Work.Id}";
        using EstateService service = EstateService.Open(run.Estate.Root);

        service.FlowClock = new Fixed(accepted.AtTime.AddHours(1));
        StatusResponse early = service.Status();
        JsonElement earlyRoute = Route(service.GoalNext(run.Goal.Id)!, "publish");
        service.FlowClock = new Fixed(accepted.AtTime.AddDays(2));
        StatusResponse late = service.Status();

        // The canonical document is the same at both readings; the live reads follow the clock.
        Assert.Equal(early.Json.GetRawText(), late.Json.GetRawText());
        Assert.Equal(JsonValueKind.Null, earlyRoute.ValueKind);
        Assert.Equal("request", Route(service.GoalNext(run.Goal.Id)!, "publish").GetProperty("state").GetString());
        Assert.DoesNotContain(publish, early.Text, StringComparison.Ordinal);
        Assert.Contains("    command: " + publish, late.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, late.Json.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(publish, service.Goal(run.Goal.Id)!.Json.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class Fixed(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private static void Options(JsonObject body) =>
        body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" };

    /// <summary>The song taken to its listening review: the builder drafts and masters by assignments the operator makes, and goal step gives listening to the operator.</summary>
    private static (RecordWritten Lyrics, RecordWritten Mastering, RecordWritten Master) ToAcceptance(RunEstate run, string goal, bool listen = true)
    {
        string builder = run.Estate.BuilderId;
        RecordWritten drafting = Assign(run, goal, "draft", builder);
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, drafting.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordWritten mastering = Assign(run, goal, "master", builder, new JsonArray(new JsonObject { ["name"] = "lyrics", ["type"] = "result", ["id"] = lyrics.Id, ["hash"] = lyrics.Hash }));
        RecordWritten master = FlowStepRoutingTests.Done(run, mastering.Id, ("master", FlowFixture.Hash('b'), null));
        if (listen)
        {
            GoalStepResponse listening = run.Service.GoalStep(goal, new GoalStepRequest(run.Estate.OperatorId));
            Assert.Equal(("ask", "listen"), (listening.Outcome, listening.Step));
        }

        return (lyrics, mastering, master);
    }

    private static RecordWritten Assign(RunEstate run, string goal, string step, string contributor, JsonArray? inputs = null)
    {
        string work = WorkOf(run, goal);
        RecordEnvelope order = run.Estate.Load().ById[work];
        var named = new JsonObject { ["key"] = step };
        if (inputs is not null)
        {
            named["inputs"] = inputs;
        }

        var body = new JsonObject
        {
            ["contributor"] = contributor, ["step"] = named,
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Carry step " + step + ".", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("song repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("the step's outputs"),
                ["reviewCriteria"] = new JsonArray("the outputs exist"), ["recovery"] = "Carry it again.",
            },
        };
        return RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), $"work-order:{order.Id}:{order.ComputedHash}")));
    }

    private static string WorkOf(RunEstate run, string goal) => run.Estate.Load().ById[goal].Body.GetProperty("plan").GetProperty("workOrders")[0].GetString()!;

    private static JsonElement Marker(string key) =>
        RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Marker {{key}}","intent":"Stand in for an act's record.","scope":["marker"],"exitQuestion":"Recorded?","inputs":[],"deliverables":["marker"]}""");

    /// <summary>Everything goal show, goal next and status show of the run, as text and JSON.</summary>
    private static string Shown(RunEstate run, string goal) =>
        string.Join("\n", run.Service.Goal(goal)!.Text, run.Service.Goal(goal)!.Json.GetRawText(), run.Service.GoalNext(goal)!.Text, run.Service.GoalNext(goal)!.Json.GetRawText(),
            run.Service.Status().Text, run.Service.Status().Json.GetRawText(), run.Service.OperatorRequests().Text);

    /// <summary>
    /// Status, goal show and goal next show one run's stops identically, but that canonical status, which trusts each record's claimed
    /// time, leaves a reserved step's command to the live reads.
    /// </summary>
    private static void AssertTheSameStops(RunEstate run, string goal)
    {
        JsonNode? status = JsonNode.Parse(Assert.Single(run.Service.Status().Json.GetProperty("runs").EnumerateArray()).GetProperty("stops").GetRawText());
        JsonNode? shown = JsonNode.Parse(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("stops").GetRawText());
        JsonNode? routed = JsonNode.Parse(run.Service.GoalNext(goal)!.Json.GetProperty("flow").GetProperty("stops").GetRawText());
        Assert.True(JsonNode.DeepEquals(shown, routed), $"{shown}\n{routed}");
        Assert.All(status!.AsArray(), s => Assert.Null(s!["command"]));
        foreach (JsonNode? live in shown!.AsArray())
        {
            live!["command"] = null;
        }

        Assert.True(JsonNode.DeepEquals(status, shown), $"{status}\n{shown}");
    }

    private static List<JsonElement> Stops(JsonElement status) =>
        status.GetProperty("runs").EnumerateArray().SelectMany(r => r.GetProperty("stops").EnumerateArray()).ToList();

    private static List<string?> Routes(RunEstate run, string goal) =>
        run.Service.GoalNext(goal)!.Json.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("route").ValueKind == JsonValueKind.Object ? s.GetProperty("route").GetProperty("state").GetString() : null).ToList();

    private static JsonElement Step(GoalResponse goal, string key) =>
        Assert.Single(goal.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null);

    private static JsonElement Route(NextResponse next, string key) =>
        Assert.Single(next.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null).GetProperty("route");
}
