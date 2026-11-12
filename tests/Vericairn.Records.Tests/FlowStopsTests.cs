// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// A run's stops (the estate's design/flow.md § 8; design/release-procedure.md §§ 6 and 8; design/cohesion-contract.md § 5): a reserved
/// step waits on the operator with its exact command computed only when it is next, and never for a later step; a request is delivered
/// only while it carries the command the records give now, withdrawn when they change and settled by the operator's own act; no agent's
/// record or close completes a reserved step; at most three stops reach one person at a time; and status shows the same stops.
/// </summary>
public class FlowStopsTests
{
    [Fact]
    public void AReservedStepWaitsOnTheOperatorWithItsCommandComputedOnlyWhenItIsNext()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        string accept = $"example-provider accept --recording {run.Work.Id} --goal {run.Goal.Id}";
        string publish = $"example-provider publish --recording {run.Work.Id} --work {run.Work.Id}";
        (_, _, RecordEnvelope master) = ToAcceptance(run, review: false);

        // Before acceptance is next no command is computed: listening, a person's role, is the only stop, due for its assignment.
        FlowPosition.Position before = run.Position();
        Assert.All(before.Steps, s => Assert.Null(s.Command));
        FlowStops.RunStop listen = Assert.Single(before.Stops);
        Assert.Equal(("person", "listen", run.Estate.OperatorId, "due"), (listen.Kind, listen.Name, listen.Principal, listen.Delivery));
        string shown = Shown(run, before);
        Assert.DoesNotContain(accept, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, shown, StringComparison.Ordinal);

        // Once acceptance is next its command is computed from the records; publication, later, still has none.
        run.Review(master);
        FlowPosition.Position next = run.Position();
        FlowPosition.StepInstance accepting = Assert.Single(next.Steps, s => s.Name == "accept");
        Assert.Equal(("stopped", "reserved", accept), (accepting.State, accepting.Stop, accepting.Command));
        Assert.Equal(("blocked", null), (run.Step("publish").State, run.Step("publish").Command));
        FlowStops.RunStop stop = Assert.Single(next.Stops);
        Assert.Equal(("reserved", "accept", run.Estate.OperatorId, "due", accept, "accept-release"), (stop.Kind, stop.Name, stop.Principal, stop.Delivery, stop.Command, stop.Act));
        shown = Shown(run, next);
        Assert.Contains(accept, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, shown, StringComparison.Ordinal);
        Assert.Contains("    command: " + accept, FlowPosition.RenderText(run.Estate.Load(), next), StringComparison.Ordinal);

        // Status shows the run with exactly the stops its position shows, the command among them.
        EstateModel model = run.Estate.Load();
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        JsonElement shownRun = Assert.Single(status.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal(run.Goal.Id, shownRun.GetProperty("goal").GetProperty("id").GetString());
        Assert.True(JsonNode.DeepEquals(FlowStops.RenderJson(model, next.Stops, commands: false), JsonNode.Parse(shownRun.GetProperty("stops").GetRawText())));
        Assert.DoesNotContain(accept, status.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("    command: " + accept, EstateStatus.Render(model, new FindingList(), run.Estate.Clock), StringComparison.Ordinal);

        // The operator's act completes acceptance; only then is publication next, with its own command.
        run.Marked("ACCEPTED");
        Assert.Equal(("done", null), (run.Step("accept").State, run.Step("accept").Command));
        Assert.Equal(("stopped", publish), (run.Step("publish").State, run.Step("publish").Command));
        RecordEnvelope published = run.Marked("PUBLISHED");
        Assert.All(run.Position().Steps, s => Assert.Null(s.Command));

        // Complete, the run stops once more, for its acceptance, only once an evaluator who carried none of it finds it met.
        Assert.True(run.Position().Complete);
        Assert.Empty(run.Position().Stops);
        RecordEnvelope reviewer = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Reviewer", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Example Reviewer" } });
        run.Estate.Add("review", reviewer.Id, GoalEvaluationTests.Evaluation("pass", GoalEvaluationTests.Met(published)), run.Goal.Reference);
        FlowStops.RunStop accepting2 = Assert.Single(run.Position().Stops);
        Assert.Equal(("accept", "the goal", run.Estate.OperatorId, "outside", null), (accepting2.Kind, accepting2.Name, accepting2.Principal, accepting2.Delivery, accepting2.Command));
        Assert.Contains($"vericairn goal accept {run.Goal.Id}", accepting2.Reason, StringComparison.Ordinal);
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
    }

    [Fact]
    public void ARequestIsDeliveredOnlyWhileItCarriesTheCommandTheRecordsGiveNow()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject step = flow["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "accept");
        step["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master"));
        step["action"] = "example-provider accept --master {input.master.hash} --result {input.master.id} --goal {goal}";
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        (RecordEnvelope lyrics, RecordEnvelope mastering, RecordEnvelope master) = ToAcceptance(run);

        // The lyrics feed the master, which feeds acceptance, so they take an independent review before they are handed on.
        Assert.Equal(("person", "draft review", "due"), (run.Position().Stops[0].Kind, run.Position().Stops[0].Name, run.Position().Stops[0].Delivery));
        run.Review(lyrics);
        string first = $"example-provider accept --master {FlowFixture.Hash('b')} --result {master.Id} --goal {run.Goal.Id}";
        Assert.Equal(first, run.Step("accept").Command);

        // A request carrying the records' command is delivered; one carrying any other command is withdrawn and never delivered.
        RecordEnvelope request = run.Add("operator-request", RequestBody(run, first), subject: run.Work.Reference);
        RecordEnvelope forged = run.Add("operator-request", RequestBody(run, "example-provider accept --everything"), subject: run.Work.Reference);
        EstateModel model = run.Estate.Load();
        Assert.Empty(model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        Assert.Equal(["due", "withdrawn"], FlowStops.Classify(model, run.Position(), new FlowRuns.StepRef("accept", null)).Select(r => r.State));
        FlowStops.RunStop stop = Assert.Single(run.Position().Stops);
        Assert.Equal(("delivered", request.Id), (stop.Delivery, stop.Record!.Id));
        OperatorRequests.Waiting waiting = OperatorRequests.WaitingOf(model, null, run.Estate.Clock);
        Assert.Equal([request.Id], waiting.Open.Select(r => r.Id));
        Assert.Equal((forged.Id, "withdrawn"), (Assert.Single(waiting.Withdrawn).Request.Id, waiting.Withdrawn[0].State));
        Assert.StartsWith("its command is not the one the records give now", waiting.Withdrawn[0].Reason, StringComparison.Ordinal);
        JsonObject queue = OperatorRequests.Derive(model, null, run.Estate.Clock);
        Assert.Equal(request.Id, queue["delivered"]![0]!["revision"]!.GetValue<string>());
        Assert.Equal(forged.Id, queue["withdrawn"]![0]!["revision"]!.GetValue<string>());
        Assert.Contains("A run's reserved step; its command, checked against the records: " + first + "\n  The requester's words, not checked:\n", OperatorRequests.RenderText(model, null, run.Estate.Clock), StringComparison.Ordinal);

        // The master changes before the operator acts: acceptance no longer waits, and its request is withdrawn from delivery.
        RecordEnvelope remastered = run.Result(mastering, "completed", ("master", FlowFixture.Hash('c'), null));
        Assert.NotEqual("stopped", run.Step("accept").State);
        Assert.Null(run.Step("accept").Command);
        model = run.Estate.Load();
        Assert.Empty(OperatorRequests.WaitingOf(model, null, run.Estate.Clock).Open);
        Assert.StartsWith("step accept is ", OperatorRequests.WaitingOf(model, null, run.Estate.Clock).Withdrawn.Single(h => h.Request.Id == request.Id).Reason, StringComparison.Ordinal);

        // Reviewed again, acceptance is next with the command the records give now; the old request stays withdrawn until revised.
        run.Review(remastered);
        string second = $"example-provider accept --master {FlowFixture.Hash('c')} --result {remastered.Id} --goal {run.Goal.Id}";
        Assert.Equal(second, run.Step("accept").Command);
        Assert.Equal(("due", null), (Assert.Single(run.Position().Stops).Delivery, Assert.Single(run.Position().Stops).Record));
        RecordEnvelope revised = run.Add("operator-request", RequestBody(run, second), subject: run.Work.Reference, supersedes: request.Id);
        stop = Assert.Single(run.Position().Stops);
        Assert.Equal(("delivered", revised.Id, second), (stop.Delivery, stop.Record!.Id, stop.Command));

        // The operator's own act completes the step from the records, and the request is settled: open until closed, never delivered.
        run.Marked("ACCEPTED");
        model = run.Estate.Load();
        Assert.Equal("done", run.Step("accept").State);
        waiting = OperatorRequests.WaitingOf(model, null, run.Estate.Clock);
        Assert.Empty(waiting.Open);
        Assert.Equal("settled", waiting.Withdrawn.Single(h => h.Request.Id == revised.Id).State);
        Assert.Empty(OperatorRequests.Closures(model, revised));
        Assert.Empty(model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
    }

    [Fact]
    public void NeitherAnAgentsCloseNorItsRecordOfTheActCompletesAReservedStep()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        ToAcceptance(run);
        string accept = run.Step("accept").Command!;
        RecordEnvelope request = run.Add("operator-request", RequestBody(run, accept), subject: run.Work.Reference);

        // An agent closes the request citing the operator's words: the request is closed, acceptance still waits on the operator's act,
        // and since the close is not the addressed operator's own, the step's request is due again.
        run.Add("operator-request-close", new JsonObject
        {
            ["outcome"] = "done",
            ["operatorWords"] = new JsonObject { ["by"] = run.Estate.OperatorId, ["statement"] = "I have done it.", ["source"] = "Operator conversation, message 4", ["at"] = request.At },
        }, builder: true, subject: request.Reference);
        Assert.Equal("stopped", run.Step("accept").State);
        FlowStops.RunStop stop = Assert.Single(run.Position().Stops);
        Assert.Equal(("due", null), (stop.Delivery, stop.Record));
        Assert.Empty(OperatorRequests.WaitingOf(run.Estate.Load(), null, run.Estate.Clock).Open);

        // An agent's record of the act does not count; the operator's own revision of it does.
        RecordEnvelope claimed = run.Marked("ACCEPTED", builder: true);
        FlowPosition.StepInstance accepting = run.Step("accept");
        Assert.Equal("stopped", accepting.State);
        Assert.Contains(accepting.Reasons, r => r.Contains($"recorded by a principal who does not hold the act, which does not count ({claimed.Id})", StringComparison.Ordinal));
        Assert.False(FlowPosition.HoldsReservedAct(run.Estate.Load(), run.Estate.BuilderId));
        run.Add("work-order", new JsonObject
        {
            ["key"] = "ACCEPTED", ["kind"] = "ordinary", ["title"] = "Marker ACCEPTED", ["intent"] = "The operator's act.", ["scope"] = new JsonArray("marker"),
            ["exitQuestion"] = "Recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("marker"),
        }, supersedes: claimed.Id);
        Assert.Equal("done", run.Step("accept").State);
    }

    [Fact]
    public void AStepsRequestNamesAReservedStepOfTheRunPinsItsWorkAndCarriesOneCommand()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        ToAcceptance(run);
        string accept = run.Step("accept").Command!;
        RecordEnvelope other = run.Marked("OTHER");
        JsonObject twoItems = RequestBody(run, accept);
        twoItems["content"]!.AsArray().Add(new JsonObject { ["kind"] = "text", ["text"] = "and more", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes("and more")) });
        RecordEnvelope[] refused =
        [
            run.Add("operator-request", RequestBody(run, accept, key: "draft"), subject: run.Work.Reference),
            run.Add("operator-request", RequestBody(run, accept, key: "mixdown"), subject: run.Work.Reference),
            run.Add("operator-request", RequestBody(run, accept), subject: other.Reference),
            run.Add("operator-request", twoItems, subject: run.Work.Reference),
            run.Add("operator-request", RequestBody(run, accept, instance: "intro"), subject: run.Work.Reference),
        ];
        EstateModel model = run.Estate.Load();
        string[] expected =
        [
            "Step draft is not a reserved act; only a reserved step waits on the operator as a request.",
            "Flow example/song has no step 'mixdown'.",
            "A step's request pins the run's work order as its subject.",
            "A step's request carries one content item, of kind command: the step's exact command.",
            "Step accept runs once; its request names no instance.",
        ];
        for (int index = 0; index < refused.Length; index++)
        {
            Assert.Contains(model.Findings, f => f.Severity == Severity.Error && f.Code == "operator-request.flow-step" && f.Location.StartsWith(refused[index].ExpectedPath, StringComparison.Ordinal) && f.Message == expected[index]);
        }
    }

    [Fact]
    public void AtMostThreeStopsOfARunReachOnePersonAtATime()
    {
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
        using var run = new FlowFixture(flow, run: false);
        RecordEnvelope goal = run.Add("goal", run.GoalBody(tracks: new JsonArray("intro", "verse", "chorus", "bridge", "outro"), roles: new JsonArray(FlowFixture.Role("editor", run.Estate.OperatorId))));
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);

        // Five clips stop for the operator at once: three are due, and two wait queued in the flow's order.
        Assert.Equal(["due", "due", "due", "queued", "queued"], Stops(run, goal).Select(s => s.Delivery));
        Assert.Equal(["clip[intro]", "clip[verse]", "clip[chorus]", "clip[bridge]", "clip[outro]"], Stops(run, goal).Select(s => s.Name));
        Assert.Contains("at most 3 reach one person at a time, and 2 more wait queued", FlowPosition.RenderText(run.Estate.Load(), FlowPosition.Derive(run.Estate.Load(), goal, run.Estate.Clock)), StringComparison.Ordinal);

        // Delivered to the operator, three hold the person's attention, and the rest stay queued until one of them settles.
        var held = new List<RecordEnvelope>();
        foreach (string track in new[] { "intro", "verse", "chorus" })
        {
            held.Add(Assign(run, goal, track));
        }

        Assert.Equal(["delivered", "delivered", "delivered", "queued", "queued"], Stops(run, goal).Select(s => s.Delivery));
        run.Result(held[0], "completed", ("clip", FlowFixture.Hash('1'), null));
        Assert.Equal(["clip[verse] delivered", "clip[chorus] delivered", "clip[bridge] due", "clip[outro] queued"], Stops(run, goal).Select(s => $"{s.Name} {s.Delivery}"));
    }

    [Fact]
    public void OnlyAnAuthenticActOfAPrincipalWhoWasAnOperatorThenCompletesAReservedStep()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        ToAcceptance(run);

        // An unsigned record claiming the operator as its author proves nothing: acceptance still waits.
        RecordEnvelope forged = run.Policies.Add("work-order", Marker("ACCEPTED"), sign: false);
        Assert.False(FlowPosition.IsReservedAct(run.Estate.Load(), forged));
        Assert.Equal("stopped", run.Step("accept").State);

        // A person's signed act before they hold the operator role does not count, even once they hold it.
        RecordEnvelope person = run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("contributor") });
        Signing.StoreSigner signer = run.Policies.AdmitSigner(person.Id);
        RecordEnvelope early = run.Estate.Add("work-order", person.Id, Marker("ACCEPTED").ToJsonString(), supersedes: forged.Id, signer: signer);
        RecordEnvelope promoted = run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("operator") }, supersedes: person.Id);
        Assert.False(FlowPosition.IsReservedAct(run.Estate.Load(), early));
        Assert.Equal("stopped", run.Step("accept").State);

        // Their act as an operator counts while the role stands. Once it is revoked, an estate without signed history cannot tell that act
        // from one dated back into the time the role stood, so it no longer counts.
        RecordEnvelope act = run.Estate.Add("work-order", person.Id, Marker("ACCEPTED").ToJsonString(), supersedes: early.Id, signer: signer);
        Assert.Equal("done", run.Step("accept").State);
        run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("contributor") }, supersedes: promoted.Id);
        Assert.False(FlowPosition.IsReservedAct(run.Estate.Load(), act));
        Assert.Equal("stopped", run.Step("accept").State);
    }

    [Fact]
    public void AnActSignedHistoryPlacesBeforeTheRoleWasRevokedStillCompletesAReservedStep()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        ToAcceptance(run);
        RecordEnvelope person = run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("operator") });
        Signing.StoreSigner signer = run.Policies.AdmitSigner(person.Id);
        Checkpoint(run, "adoption");
        RecordEnvelope act = run.Estate.Add("work-order", person.Id, Marker("ACCEPTED").ToJsonString(), signer: signer);
        Checkpoint(run, "manual");
        run.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second Person", ["roles"] = new JsonArray("contributor") }, supersedes: person.Id);
        Assert.True(FlowPosition.IsReservedAct(run.Estate.Load(), act));
        Assert.Equal("done", run.Step("accept").State);

        // One written after the revocation and dated back into the time the role stood does not.
        DateTimeOffset resume = run.Estate.Clock;
        run.Estate.SetClock(act.AtTime);
        RecordEnvelope dated = run.Estate.Add("work-order", person.Id, Marker("DATED").ToJsonString(), signer: signer);
        run.Estate.SetClock(resume);
        Assert.False(FlowPosition.IsReservedAct(run.Estate.Load(), dated));
    }

    private static void Checkpoint(FlowFixture run, string trigger)
    {
        EstateModel model = run.Estate.Load();
        History.CheckpointDraft draft = History.SignedHistory.Prepare(model, trigger, trigger == "adoption" ? model.Records.Where(r => r.Signatures.Count == 0) : null);
        foreach (byte[] bytes in draft.Objects)
        {
            string path = Path.Combine(run.Estate.Root, EstateLayout.ObjectsDirectory, Hashing.ContentHash.Sha256(bytes)["sha256:".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        run.Estate.Add("history-checkpoint", run.Estate.OperatorId, draft.Body.GetRawText(), signer: run.Policies.OperatorSigner);
    }

    [Fact]
    public void AReservedActsPredicateNeverHoldsByADerivedState()
    {
        using var run = new FlowFixture();
        JsonElement accepted = FlowFixture.Element(new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = "SONG-1" }, ["state"] = "planned" });
        EstateModel model = run.Estate.Load();
        Assert.Equal("met", GoalPredicates.EvaluatePredicate(model, accepted).Value);
        GoalPredicates.Verdict refused = GoalPredicates.EvaluatePredicate(model, accepted, record => FlowPosition.IsReservedAct(model, record));
        Assert.Equal("not-met", refused.Value);
        Assert.Contains("a state rests on other records than those counted", refused.Reason, StringComparison.Ordinal);
        Assert.Empty(GoalPredicates.Matching(model, accepted, record => true));
    }

    [Theory]
    [InlineData("1.0.0\n")]
    [InlineData("-rf")]
    [InlineData("1.0 beta")]
    [InlineData("1.0.0\t")]
    [InlineData("1.0.0\u00e9")]
    [InlineData("1;rm")]
    [InlineData("$(id)")]
    [InlineData("`id`")]
    [InlineData("a|b")]
    [InlineData("a&b")]
    [InlineData("'a'")]
    [InlineData("a\\b")]
    [InlineData("a*")]
    [InlineData("a>b")]
    public void ABoundValueACommandWouldNotTakeUnquotedBlocksTheStepAndIsNeverPutIn(string value)
    {
        JsonObject song = FlowFixture.Song();
        song["subject"]!["fields"]!.AsArray().Add(new JsonObject { ["name"] = "version", ["type"] = "text" });
        song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "accept")["action"] = "example-provider accept --version {subject.version}";
        using var run = new FlowFixture(song, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" },
            subject: f => new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = f.Work.Id }, ["tracks"] = new JsonArray("intro"), ["version"] = value });
        ToAcceptance(run);
        FlowPosition.StepInstance accepting = run.Step("accept");
        Assert.Equal(("blocked", null), (accepting.State, accepting.Command));
        Assert.Equal("its command cannot be computed from the records now: {subject.version} is a value with characters a command never takes unquoted, so it is not put in", Assert.Single(accepting.Reasons));
        Assert.Empty(run.Position().Stops);
    }

    [Fact]
    public void ASafeValueIsPutInAndASlugEndsAtItsLastCharacter()
    {
        Assert.Matches(Types.Patterns.Slug, "intro");
        Assert.DoesNotMatch(Types.Patterns.Slug, "intro\n");
        JsonObject song = FlowFixture.Song();
        song["subject"]!["fields"]!.AsArray().Add(new JsonObject { ["name"] = "version", ["type"] = "text" });
        song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "accept")["action"] = "example-provider accept --version {subject.version}";
        using var run = new FlowFixture(song, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" },
            subject: f => new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = f.Work.Id }, ["tracks"] = new JsonArray("intro\n"), ["version"] = "1.0.0-rc.1+build=7,a/b@c" });
        Assert.Contains(run.Estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(run.Goal.ExpectedPath, StringComparison.Ordinal) && f.Message.Contains("an instance name", StringComparison.Ordinal));
        ToAcceptance(run);
        Assert.Equal("example-provider accept --version 1.0.0-rc.1+build=7,a/b@c", run.Step("accept").Command);
    }

    [Fact]
    public void NoAgentThatDoesNotTakeTheRunsStepsSilencesAReservedStepsRequest()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        ToAcceptance(run);
        string accept = run.Step("accept").Command!;
        JsonObject Words(RecordEnvelope request) => new()
        {
            ["outcome"] = "done",
            ["operatorWords"] = new JsonObject { ["by"] = run.Estate.OperatorId, ["statement"] = "Done.", ["source"] = "A conversation", ["at"] = request.At },
        };

        // The builder, who takes no steps of the run, records the step's request with its exact command and closes it citing the operator.
        JsonObject body = RequestBody(run, accept);
        body["askedBy"] = run.Estate.BuilderId;
        RecordEnvelope own = run.Add("operator-request", body, builder: true, subject: run.Work.Reference);
        Assert.StartsWith("Test Builder recorded it, and does not take the run's steps", Assert.Single(FlowStops.Classify(run.Estate.Load(), run.Position(), new FlowRuns.StepRef("accept", null))).Reason, StringComparison.Ordinal);
        run.Add("operator-request-close", Words(own), builder: true, subject: own.Reference);

        // It closes the operator's own request the same way: neither close silences the step, whose request stays due.
        RecordEnvelope request = run.Add("operator-request", RequestBody(run, accept), subject: run.Work.Reference);
        run.Add("operator-request-close", Words(request), builder: true, subject: request.Reference);
        FlowStops.RunStop stop = Assert.Single(run.Position().Stops);
        Assert.Equal(("due", null), (stop.Delivery, stop.Record));

        // Only the addressed operator's own close stops the request being made again while its command holds.
        RecordEnvelope again = run.Add("operator-request", RequestBody(run, accept), subject: run.Work.Reference);
        run.Add("operator-request-close", new JsonObject { ["outcome"] = "dismissed", ["reason"] = "Not now." }, subject: again.Reference);
        stop = Assert.Single(run.Position().Stops);
        Assert.Equal(("closed", again.Id), (stop.Delivery, stop.Record!.Id));
    }

    [Fact]
    public void AtMostThreeStopsReachOnePersonAcrossEveryRun()
    {
        JsonObject flow = Clips();
        using var run = new FlowFixture(flow, run: false);
        RecordEnvelope first = Activate(run, run.Work, "a", "b");
        RecordEnvelope other = run.Add("work-order", new JsonObject
        {
            ["key"] = "SONG-2", ["kind"] = "ordinary", ["title"] = "The second song", ["intent"] = "Cut its clips.", ["scope"] = new JsonArray("path:song/"),
            ["exitQuestion"] = "Are its clips cut?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("clips"),
        });
        RecordEnvelope second = Activate(run, other, "c", "d");

        // Four clips wait on the operator across two runs: the earliest of each flow first, then by run, three at a time.
        Assert.Equal(["clip[a] due", "clip[b] due"], Stops(run, first).Select(s => $"{s.Name} {s.Delivery}"));
        Assert.Equal(["clip[c] due", "clip[d] queued"], Stops(run, second).Select(s => $"{s.Name} {s.Delivery}"));
        Assert.Equal(3, FlowPosition.Derive(run.Estate.Load(), first, run.Estate.Clock).Reaching[run.Estate.OperatorId]);
    }

    private static JsonObject Clips() => new()
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

    private static RecordEnvelope Activate(FlowFixture run, RecordEnvelope work, params string[] tracks)
    {
        JsonObject body = run.GoalBody(tracks: new JsonArray(tracks.Select(t => (JsonNode)t).ToArray()), roles: new JsonArray(FlowFixture.Role("editor", run.Estate.OperatorId)));
        body["plan"]!["workOrders"] = new JsonArray(work.Id);
        body["plan"]!["flow"]!["subject"]!["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = work.Id };
        RecordEnvelope goal = run.Add("goal", body);
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);
        return goal;
    }

    private static JsonObject Marker(string key) => new()
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Marker " + key, ["intent"] = "Stand in for an act's record.", ["scope"] = new JsonArray("marker"),
        ["exitQuestion"] = "Recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("marker"),
    };

    private static List<FlowStops.RunStop> Stops(FlowFixture run, RecordEnvelope goal) => FlowPosition.Derive(run.Estate.Load(), goal, run.Estate.Clock).Stops.ToList();

    private static RecordEnvelope Assign(FlowFixture run, RecordEnvelope goal, string track)
    {
        RecordEnvelope order = run.Estate.Load().ById[goal.Body.GetProperty("plan").GetProperty("workOrders")[0].GetString()!];
        return run.Add("assignment", new JsonObject
        {
            ["contributor"] = run.Estate.OperatorId, ["step"] = new JsonObject { ["key"] = "clip", ["instance"] = track },
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Cut the clip.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("song repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("the clip"),
                ["reviewCriteria"] = new JsonArray("the clip exists"), ["recovery"] = "Cut it again.",
            },
        }, subject: order.Reference);
    }

    /// <summary>The song taken to its acceptance: the builder drafts and masters, and the operator's review of the master is the listening review.</summary>
    private static (RecordEnvelope Lyrics, RecordEnvelope Mastering, RecordEnvelope Master) ToAcceptance(FlowFixture run, bool review = true)
    {
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics")));
        RecordEnvelope master = run.Result(mastering, "completed", ("master", FlowFixture.Hash('b'), null));
        if (review)
        {
            run.Review(master);
        }

        return (lyrics, mastering, master);
    }

    /// <summary>The operator's request for a step of the run, carrying a command.</summary>
    private static JsonObject RequestBody(FlowFixture run, string command, string key = "accept", string? instance = null)
    {
        var step = new JsonObject { ["goal"] = run.Goal.Id, ["key"] = key };
        if (instance is not null)
        {
            step["instance"] = instance;
        }

        return new JsonObject
        {
            ["to"] = run.Estate.OperatorId, ["askedBy"] = run.Estate.OperatorId, ["action"] = "Carry the reserved act.", ["why"] = "The step is next.",
            ["recommendation"] = "Run the command yourself.", ["consequence"] = "The step waits.", ["kind"] = "action", ["destination"] = "the reserved act of the step",
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "command", ["text"] = command, ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(command)) }),
            ["flowStep"] = step,
        };
    }

    /// <summary>Everything a run's position shows of itself, as text and as JSON.</summary>
    private static string Shown(FlowFixture run, FlowPosition.Position position)
    {
        EstateModel model = run.Estate.Load();
        return FlowPosition.RenderText(model, position) + FlowPosition.RenderJson(model, position).ToJsonString();
    }
}
