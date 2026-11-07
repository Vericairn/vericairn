// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A run's hand-offs moved one step per command (the estate's design/flow.md §§ 5 to 8): a review before hand-off routed to its reviewer
/// role and never to the work's author, a brief that names its inputs exactly as handed on and the estate's living copy of its role
/// brief, a failed run backing off, a spent retry budget asked of the sponsor while the steps that do not depend on it go on, and the
/// run's own required attention holding every dispatch until the sponsor answers. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class FlowHandOffRoutingTests
{
    [Fact]
    public void AReviewBeforeHandOffIsRoutedToItsReviewerAndNeverToTheWorksAuthor()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string goal = FlowStepRoutingTests.Run(run, change: body =>
        {
            body["plan"]!["flow"]!["options"] = new JsonObject { ["review"] = "independent", ["step.review-lyrics"] = "off" };
            body["roles"]![1] = FlowFixture.Role("reviewer", builder, op);
        });
        GoalStepResponse drafted = FlowStepRoutingTests.Step(run, goal, "review-draft");
        Assert.True(drafted.Ok, drafted.Message);
        run.Settled(drafted.Run!.Run);
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, drafted.Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));

        // The lyrics await an independent review: routed to the reviewer role, whose first principal, the builder, wrote them and is
        // refused; the operator is assigned the review and asked.
        NextResponse next = run.Service.GoalNext(goal)!;
        JsonElement review = Assert.Single(next.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "draft" && s.GetProperty("review").ValueKind == JsonValueKind.Object);
        Assert.Equal(("person", op), (review.GetProperty("route").GetProperty("state").GetString(), review.GetProperty("route").GetProperty("principal").GetString()));
        Assert.Contains(review.GetProperty("route").GetProperty("candidates").EnumerateArray(), c => c.GetProperty("principal").GetString() == builder && !c.GetProperty("available").GetBoolean()
            && c.GetProperty("reasons").EnumerateArray().Any(r => r.GetString()!.StartsWith("it authored the step's work", StringComparison.Ordinal)));
        Assert.Equal("draft review", next.Json.GetProperty("next").GetString());

        GoalStepResponse asked = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "draft review", "person", op), (asked.Outcome, asked.Step, asked.Route, asked.Principal));
        RecordEnvelope assignment = run.Estate.Load().ById[asked.Assignment!.Id];
        JsonElement named = assignment.Body.GetProperty("step");
        Assert.Equal("draft", named.GetProperty("key").GetString());
        Assert.Equal((lyrics.Id, lyrics.Hash), (named.GetProperty("review").GetProperty("subject").GetProperty("id").GetString(), named.GetProperty("review").GetProperty("subject").GetProperty("hash").GetString()));
        Assert.Equal(["lyrics"], named.GetProperty("review").GetProperty("items").EnumerateArray().Select(i => i.GetString()));
        string outcome = assignment.Body.GetProperty("brief").GetProperty("outcome").GetString()!;
        Assert.StartsWith($"draft review: review item lyrics in result {lyrics.Id} at {lyrics.Hash}, from step draft", outcome, StringComparison.Ordinal);
        Assert.Contains("It takes an independent review, by a principal other than its authors, carried as its reviewer.", outcome, StringComparison.Ordinal);
        Assert.StartsWith($"Will you review item lyrics in result {lyrics.Id}", EstateModel.BodyString(run.Estate.Load().ById[asked.Ask!.Id], "question"), StringComparison.Ordinal);

        // The review is held by its assignment; the operator's pass hands the lyrics on, and the master is next.
        Assert.Equal("assigned", Assert.Single(run.Service.GoalNext(goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("review").ValueKind == JsonValueKind.Object).GetProperty("route").GetProperty("state").GetString());
        FlowStepRoutingTests.Review(run, lyrics);
        Assert.Equal("master", run.Service.GoalNext(goal)!.Json.GetProperty("next").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnAgentReviewsAPersonsWorkInARunOfItsOwnReviewAssignment()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string goal = FlowStepRoutingTests.Run(run, change: body =>
        {
            body["plan"]!["flow"]!["options"] = new JsonObject { ["review"] = "independent", ["step.review-lyrics"] = "off" };
            body["roles"]![0] = FlowFixture.Role("writer", op);
            body["roles"]![1] = FlowFixture.Role("reviewer", builder);
        });

        // The operator writes the lyrics; their review goes to the builder's agent, which did not write them, in a run of its own.
        GoalStepResponse written = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "draft", "person"), (written.Outcome, written.Step, written.Route));
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, written.Assignment!.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"), by: op);
        GoalStepResponse reviewing = FlowStepRoutingTests.Step(run, goal, "agent-review");
        Assert.True(reviewing.Ok, reviewing.Message);
        Assert.Equal(("run", "draft review", "agent", builder), (reviewing.Outcome, reviewing.Step, reviewing.Route, reviewing.Principal));
        RecordEnvelope assignment = run.Estate.Load().ById[reviewing.Assignment!.Id];
        Assert.Equal(lyrics.Id, assignment.Body.GetProperty("step").GetProperty("review").GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("observed-success", run.Settled(reviewing.Run!.Run).Status);
        Assert.Equal("work/song-1-draft", AgentRuns.Find(run.Estate.Load(), reviewing.Run.Run)!.Target("branch"));

        // Its review of the exact result hands the lyrics on; the review's run counts against the review's attempts, not the draft's.
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", builder,
            RunEstate.Json("""{"independence":"independent-agent","outcome":"pass","summary":"Read the exact lyrics.","findings":[]}"""), $"result:{lyrics.Id}:{lyrics.Hash}")));
        Assert.Equal("done", Step(run, goal, "draft").GetProperty("state").GetString());
        Assert.Equal(0, Step(run, goal, "draft").GetProperty("attemptsUsed").GetInt32());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepWhoseResultNamesAnUnresolvedEffectIsNotRunAgainUntilItIsReconciled()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, change: body => body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off" });
        GoalStepResponse drafted = FlowStepRoutingTests.Step(run, goal, "unresolved-draft");
        Assert.True(drafted.Ok, drafted.Message);
        run.Settled(drafted.Run!.Run);
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(drafted.Assignment!.Id, run.Estate.BuilderId, "song repository, a work branch", "Carry the step."))).Id;
        var body = new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Uploaded the lyrics.", ["outputs"] = new JsonArray(new JsonObject { ["name"] = "lyrics", ["hash"] = FlowFixture.Hash('a'), ["mediaType"] = "text/markdown" }),
            ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray("the lyric site's reply to the upload was lost"), ["nextStep"] = "Reconcile the upload.",
        };
        RecordWritten unresolved = RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, JsonSerializer.SerializeToElement(body))));

        // goal next routes the effect to the sponsor, and a worker start on the step's assignment is refused before anything is recorded.
        Assert.Equal("attention", Route(run, goal, "draft").GetProperty("state").GetString());
        int runs = AgentRuns.All(run.Estate.Load()).Count;
        RunResponse refused = run.Service.RunStart(new RunStartRequest(drafted.Assignment.Id, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 30, Settings: FlowStepRoutingTests.Command(run, "unresolved-again")));
        Assert.False(refused.Ok);
        Assert.Contains($"Result {unresolved.Id} of this flow step's work names effects whose outcome is not known", refused.Message, StringComparison.Ordinal);
        Assert.Equal(runs, AgentRuns.All(run.Estate.Load()).Count);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ALinkedRoleDocumentIsNeverRead()
    {
        using var run = new RunEstate(recordFormat: 3);
        JsonObject flow = FlowFixture.Song();
        flow["roles"]![0]!["document"] = "docs/roles/writer.md";
        Directory.CreateDirectory(Path.Combine(run.Estate.Root, "docs", "roles"));
        string outside = Path.Combine(run.Estate.Root, "docs", "elsewhere.md");
        File.WriteAllText(outside, "# Not the brief\n");
        File.CreateSymbolicLink(Path.Combine(run.Estate.Root, "docs", "roles", "writer.md"), outside);
        string goal = FlowStepRoutingTests.Run(run, flow, body => body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off" });
        GoalStepResponse drafted = FlowStepRoutingTests.Step(run, goal, "linked-draft");
        Assert.True(drafted.Ok, drafted.Message);
        run.Settled(drafted.Run!.Run);
        string outcome = run.Estate.Load().ById[drafted.Assignment!.Id].Body.GetProperty("brief").GetProperty("outcome").GetString()!;
        string hash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(outside))).ToLowerInvariant();
        Assert.Contains("The estate's copy at docs/roles/writer.md is not a plain file of at most 1024 KiB (a link, a directory, a device or an empty file), so it is not read and the flow's brief stands.", outcome, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void ABriefNamesItsInputsAsHandedOnAndTheEstatesLivingCopyOfItsRoleBrief()
    {
        using var run = new RunEstate(recordFormat: 3);
        JsonObject flow = FlowFixture.Song();
        flow["roles"]![0]!["document"] = "docs/roles/writer.md";
        Directory.CreateDirectory(Path.Combine(run.Estate.Root, "docs", "roles"));
        File.WriteAllText(Path.Combine(run.Estate.Root, "docs", "roles", "writer.md"), "# Writer\n\nWrite the song's material.\n");
        string goal = FlowStepRoutingTests.Run(run, flow, body => body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off" });
        GoalStepResponse drafted = FlowStepRoutingTests.Step(run, goal, "brief-draft");
        Assert.True(drafted.Ok, drafted.Message);
        string outcome = run.Estate.Load().ById[drafted.Assignment!.Id].Body.GetProperty("brief").GetProperty("outcome").GetString()!;
        string hash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(run.Estate.Root, "docs", "roles", "writer.md")))).ToLowerInvariant();
        Assert.Contains($"The writer role's brief: Write and produce the song's material. The estate keeps it as a living document at docs/roles/writer.md ({hash}): read it before you start; no living-document declaration covers it.", outcome, StringComparison.Ordinal);
        run.Settled(drafted.Run!.Run);
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, drafted.Assignment.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));

        // The master's brief names the lyrics it takes exactly as they were handed on: the file's hash and the result that carries it.
        GoalStepResponse mastered = FlowStepRoutingTests.Step(run, goal, "brief-master");
        Assert.True(mastered.Ok, mastered.Message);
        Assert.Contains($"Its inputs, exactly as handed on: lyrics, draft output lyrics at {FlowFixture.Hash('a')} in result {lyrics.Id}; use these and no other version.",
            run.Estate.Load().ById[mastered.Assignment!.Id].Body.GetProperty("brief").GetProperty("outcome").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AFailedRunBacksOffAndASpentRetryBudgetIsAskedOfTheSponsorWhileWhatDoesNotDependOnItGoesOn()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        JsonObject flow = FlowFixture.Song();
        JsonObject notes = FlowFixture.Step("notes", "work", "writer", optional: "on");
        notes["after"] = new JsonArray();
        flow["steps"]!.AsArray().Add(notes);
        string goal = FlowStepRoutingTests.Run(run, flow, body =>
        {
            body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off" };
            body["budget"]!["attemptsPerStep"] = 2;
        });

        // The first run fails: the step backs off, and goal step takes the notes instead, which do not wait on it.
        GoalStepResponse failing = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude-fails", run.Report("fails")) }, WallMinutes: 30));
        Assert.True(failing.Ok, failing.Message);
        Assert.Equal("known-failure", run.Settled(failing.Run!.Run).Status);
        JsonElement draft = Step(run, goal, "draft");
        Assert.Equal("blocked", draft.GetProperty("state").GetString());
        Assert.StartsWith($"it backs off after run {failing.Run.Run} failed", draft.GetProperty("reasons")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(1, draft.GetProperty("attemptsUsed").GetInt32());
        GoalStepResponse other = FlowStepRoutingTests.Step(run, goal, "notes");
        Assert.True(other.Ok, other.Message);
        Assert.Equal("notes", other.Step);
        run.Settled(other.Run!.Run);
        FlowStepRoutingTests.Done(run, other.Assignment!.Id, ("notes", FlowFixture.Hash('e'), null));

        // A second run, made directly on the same assignment, records a result; a review finds what to change in it, and with two of
        // the two attempts per step spent the step stops for the sponsor.
        RunResponse second = run.Service.RunStart(new RunStartRequest(failing.Assignment!.Id, "claude-code", run.Estate.BuilderId, op, run.Resource, WallMinutes: 30, Settings: FlowStepRoutingTests.Command(run, "second")));
        Assert.True(second.Ok, second.Message);
        run.Settled(second.Run);
        RecordWritten lyrics = FlowStepRoutingTests.Done(run, failing.Assignment.Id, ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        string findings = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", op,
            RunEstate.Json("""{"independence":"operator","outcome":"findings","summary":"The chorus repeats.","findings":[{"severity":"warning","summary":"The chorus repeats."}]}"""), $"result:{lyrics.Id}:{lyrics.Hash}"))).Id;
        draft = Step(run, goal, "draft");
        Assert.Equal("stopped", draft.GetProperty("state").GetString());
        Assert.Equal(("exhausted", findings), (draft.GetProperty("attention").GetProperty("kind").GetString(), draft.GetProperty("attention").GetProperty("trigger").GetProperty("id").GetString()));
        Assert.Equal((1, 2, 2), (draft.GetProperty("attempts").GetArrayLength(), draft.GetProperty("attemptsUsed").GetInt32(), draft.GetProperty("attemptsAllowed").GetInt32()));

        // goal step asks the sponsor about it once; nothing is dispatched for it, and it is asked no more while the ask waits.
        GoalStepResponse exhausted = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(exhausted.Ok, exhausted.Message);
        Assert.Equal(("ask", "draft", "attention", op), (exhausted.Outcome, exhausted.Step, exhausted.Route, exhausted.Principal));
        Assert.Null(exhausted.Assignment);
        RecordEnvelope ask = run.Estate.Load().ById[exhausted.Ask!.Id];
        Assert.Equal(findings, ask.Subject!.Id);
        Assert.Equal(goal, EstateModel.BodyString(ask, "goal"));
        Assert.StartsWith("Required attention in goal Release the song, step draft: its retry budget is spent", EstateModel.BodyString(ask, "question"), StringComparison.Ordinal);
        Assert.Equal("asked", Route(run, goal, "draft").GetProperty("state").GetString());
        GoalStepResponse nothing = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal("none", nothing.Outcome);
        Assert.DoesNotContain(run.Estate.Load().OfType("ask"), a => a.Id != ask.Id && a.Subject?.Id == findings);

        // Nothing of the step's work is lost: both runs, the attempt and its result are recorded and shown.
        Assert.Equal(2, AgentRuns.All(run.Estate.Load()).Count(r => r.Assignment == failing.Assignment.Id));
        Assert.Equal(lyrics.Id, Step(run, goal, "draft").GetProperty("attempts")[0].GetProperty("result").GetProperty("id").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheRunsOwnAttentionHoldsEveryDispatchUntilTheSponsorAnswers()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, change: body => body["plan"]!["flow"]!["options"] = new JsonObject { ["step.review-lyrics"] = "off" });

        // An evaluation judges the release impossible: the run asks its sponsor and dispatches nothing until the ask is answered.
        RunEstate.Ok(run.Service.GoalEvaluate(goal, new GoalEvaluateRequest(op, "Read the platform's terms.", [new GoalVerdict(1, "impossible", "The platform no longer takes releases.")])));
        NextResponse held = run.Service.GoalNext(goal)!;
        Assert.True(held.Json.GetProperty("held").GetBoolean());
        Assert.Equal("impossible", held.Json.GetProperty("attention")[0].GetProperty("kind").GetString());
        Assert.Null(held.Json.GetProperty("next").GetString());
        GoalStepResponse asked = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource, FlowStepRoutingTests.Command(run, "held"), WallMinutes: 30));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "the run"), (asked.Outcome, asked.Step));
        Assert.Null(asked.Run);
        GoalStepResponse waiting = run.Service.GoalStep(goal, new GoalStepRequest(op, run.Resource, FlowStepRoutingTests.Command(run, "waiting"), WallMinutes: 30));
        Assert.Equal("none", waiting.Outcome);
        Assert.StartsWith($"Nothing to dispatch: goal {goal}'s run is held for its required attention", waiting.Message, StringComparison.Ordinal);

        // The sponsor answers: the run goes on, and draft is dispatched.
        var decision = new JsonObject
        {
            ["title"] = "Go on with the release", ["statement"] = "Go on.", ["summary"] = "The sponsor read the evaluation.", ["synthesis"] = "Go on with the run.",
            ["effects"] = new JsonArray("The run goes on."), ["affects"] = new JsonArray(), ["decidedBy"] = op,
            ["decidedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["stake"] = "low", ["notAuthorized"] = new JsonArray(), ["answers"] = new JsonArray(asked.Ask!.Id),
        };
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json(decision.ToJsonString()))));
        GoalStepResponse goes = FlowStepRoutingTests.Step(run, goal, "goes-on");
        Assert.True(goes.Ok, goes.Message);
        Assert.Equal(("run", "draft"), (goes.Outcome, goes.Step));
        run.Settled(goes.Run!.Run);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static JsonElement Step(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null);

    private static JsonElement Route(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.GoalNext(goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("review").ValueKind == JsonValueKind.Null).GetProperty("route");
}
