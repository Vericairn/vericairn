// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// A run's position derived from the records alone (the estate's design/flow.md §§ 5 to 8): golden states for each step through a run,
/// staleness when an upstream output changes after use, the failing predicate of a blocked step, and the same position from a fresh
/// read of the same records.
/// </summary>
public class FlowStepStateTests
{
    [Fact]
    public void EachStepsStateComesFromTheRecordsThroughARun()
    {
        using var run = new FlowFixture();
        Assert.Equal(0, run.Estate.Load().Findings.Count(f => f.Severity == Severity.Error));

        // At the start the writer's agent is next; everything after waits on what comes before it.
        Assert.Equal(
            ["draft next", "review-lyrics blocked", "master blocked", "shorts[intro] blocked", "shorts[outro] blocked", "listen blocked", "accept blocked", "publish blocked"],
            States(run.Position()));
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal(run.Estate.BuilderId, draft.Principal);
        Assert.StartsWith("routed by the writer role to Test Builder", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("it waits on draft, which is next", run.Step("review-lyrics").Reasons[0]);

        // The builder carries draft: its result names the lyrics by hash, and it is done (the flow's review option is none).
        RecordEnvelope assigned = run.Assign("draft", run.Estate.BuilderId);
        Assert.Equal("next", run.Step("draft").State);
        RecordEnvelope lyrics = run.Result(assigned, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        draft = run.Step("draft");
        Assert.Equal("done", draft.State);
        FlowPosition.Output output = Assert.Single(draft.Outputs);
        Assert.Equal(("lyrics", "file", lyrics.Id, FlowFixture.Hash('a')), (output.Name, output.Kind, output.Carrier.Id, output.Hash));

        // The review of the lyrics is the reviewer's, a person in the roster: it stops for them until they review the exact result.
        FlowPosition.StepInstance review = run.Step("review-lyrics");
        Assert.Equal("stopped", review.State);
        Assert.StartsWith("a person holds the reviewer role: Test Operator", review.Reasons[0], StringComparison.Ordinal);
        RecordEnvelope verdict = run.Review(lyrics);
        review = run.Step("review-lyrics");
        Assert.Equal("done", review.State);
        Assert.Equal(verdict.Id, review.Review!.Id);
        Assert.Equal(verdict.Id, Assert.Single(review.Outputs).Carrier.Id);
        Assert.Equal("next", run.Step("master").State);

        // The master's brief pins the lyrics it uses; each track's clip then waits on the master, and the listening review on the clips.
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics")));
        RecordEnvelope master = run.Result(mastering, "completed", ("master", FlowFixture.Hash('b'), null));
        Assert.Equal(["shorts[intro] next", "shorts[outro] next", "listen blocked"], States(run.Position()).Skip(3).Take(3));
        foreach (string track in new[] { "intro", "outro" })
        {
            RecordEnvelope clip = run.Assign("shorts", run.Estate.BuilderId, track, new JsonArray(FlowFixture.PinOf(master, "master")));
            run.Result(clip, "completed", ("clip", FlowFixture.Hash(track == "intro" ? '1' : '2'), null));
        }

        // Listening is a person's judgment; acceptance and publication are reserved acts, stopped for the operator until their records exist.
        Assert.Equal(["listen stopped", "accept blocked", "publish blocked"], States(run.Position()).Skip(5));
        run.Review(master);
        Assert.Equal(["listen done", "accept stopped", "publish blocked"], States(run.Position()).Skip(5));
        Assert.StartsWith("a reserved act, accept-release: it waits on a person who holds it", run.Step("accept").Reasons[0], StringComparison.Ordinal);
        run.Marked("ACCEPTED");
        Assert.Equal(["accept done", "publish stopped"], States(run.Position()).Skip(6));
        Assert.False(run.Position().Complete);
        run.Marked("PUBLISHED");
        Assert.True(run.Position().Complete);
        Assert.All(run.Position().Steps, s => Assert.Equal("done", s.State));
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
    }

    [Fact]
    public void AStepWhoseUpstreamOutputChangedAfterUseIsStaleAndRerunsFromAFreshAssignment()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope first = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(first, "lyrics")));
        RecordEnvelope master = run.Result(mastering, "completed", ("master", FlowFixture.Hash('b'), null));
        Assert.Equal("done", run.Step("master").State);

        // A new result of draft with the same lyrics changes nothing the master used.
        RecordEnvelope same = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        Assert.Equal("done", run.Step("master").State);

        // Changed lyrics: the master used the old ones, so it is stale and reruns under its recovery; what used the master waits.
        RecordEnvelope changed = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('c'), "text/markdown"));
        FlowPosition.StepInstance stale = run.Step("master");
        Assert.Equal("stale", stale.State);
        Assert.Contains($"its input lyrics changed after use: its brief pinned result {first.Id} at {FlowFixture.Hash('a')}, and draft output lyrics is now {FlowFixture.Hash('c')} in result {changed.Id}", stale.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("it reruns under its recovery: Carry master again from its pinned inputs.", stale.Reasons[^1]);
        FlowPosition.Input input = Assert.Single(stale.Inputs);
        Assert.Equal((first.Id, changed.Id), (input.Pinned!.Id, input.Carrier!.Id));
        Assert.Equal("it waits on master, which is stale", run.Step("shorts[intro]").Reasons[0]);

        // A fresh assignment pins the lyrics as they are now: its result makes the master done again, and the clips use the new master.
        RecordEnvelope again = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(changed, "lyrics")));
        Assert.Equal("next", run.Step("master").State);
        run.Result(again, "completed", ("master", FlowFixture.Hash('d'), null));
        Assert.Equal("done", run.Step("master").State);
        Assert.Equal("next", run.Step("shorts[intro]").State);
        _ = (master, same);
    }

    [Fact]
    public void AnAssignmentMadeWithoutPinsIsStaleWhenItsInputIsRecordedAfterIt()
    {
        using var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope unpinned = run.Assign("master", run.Estate.BuilderId);
        run.Result(unpinned, "completed", ("master", FlowFixture.Hash('b'), null));
        Assert.Equal("done", run.Step("master").State);
        RecordEnvelope later = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('c'), "text/markdown"));
        FlowPosition.StepInstance stale = run.Step("master");
        Assert.Equal("stale", stale.State);
        Assert.Contains($"after assignment {unpinned.Id}, which pins no input lyrics, was made", stale.Reasons[0], StringComparison.Ordinal);
        _ = (lyrics, later);
    }

    [Fact]
    public void AStepDoneWithoutAnAssignmentIsStaleWhenAnInputIsRecordedAfterIt()
    {
        JsonObject flow = FlowFixture.Song();
        JsonArray steps = flow["steps"]!.AsArray();
        steps.Add(FlowFixture.Step("archive", "operation", "decider", inputs: new JsonArray(FlowFixture.Input("master", "master", "master")), done: new JsonArray(FlowFixture.Marker("ARCHIVED"))));
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off", ["step.publish"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics")));
        RecordEnvelope master = run.Result(mastering, "completed", ("master", FlowFixture.Hash('b'), null));
        run.Review(master);
        run.Marked("ACCEPTED");
        run.Marked("ARCHIVED");
        Assert.Equal("done", run.Step("archive").State);

        // A new master after the archive was recorded: the archive used the older one.
        RecordEnvelope remastered = run.Result(mastering, "completed", ("master", FlowFixture.Hash('e'), null));
        FlowPosition.StepInstance archive = run.Step("archive");
        Assert.Equal("stale", archive.State);
        Assert.Contains($"master output master is now result {remastered.Id}", archive.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockedStepNamesItsFailingPredicateTrapAndDueTime()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject publish = flow["steps"]!.AsArray().Last()!.AsObject();
        publish["requires"] = new JsonArray(FlowFixture.Marker("CLEARED"));
        publish["traps"] = new JsonArray(new JsonObject { ["name"] = "no-hold", ["summary"] = "No hold stands on the release.", ["holds"] = new JsonArray(new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = "HOLD" }, ["max"] = 0 }) });
        publish["when"] = new JsonObject { ["due"] = new JsonObject { ["after"] = "P1D", ["from"] = "accept" } };
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope master = run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null));
        run.Review(master);
        RecordEnvelope hold = run.Marked("HOLD");
        RecordEnvelope accepted = run.Marked("ACCEPTED");

        FlowPosition.StepInstance blocked = run.Step("publish");
        Assert.Equal("blocked", blocked.State);
        Assert.Equal(3, blocked.Reasons.Count);
        Assert.StartsWith("a precondition fails: not-met: work-order records with key CLEARED", blocked.Reasons[0], StringComparison.Ordinal);
        Assert.StartsWith("trap no-hold: No hold stands on the release. (not-met:", blocked.Reasons[1], StringComparison.Ordinal);
        Assert.StartsWith("it is due at " + RecordParser.FormatTimestamp(accepted.AtTime.AddDays(1)), blocked.Reasons[2], StringComparison.Ordinal);

        // Each reason clears on its own: the precondition by its record, the due time by the clock, the trap when the hold is lifted.
        run.Marked("CLEARED");
        Assert.Equal(2, run.Step("publish").Reasons.Count);
        DateTimeOffset due = accepted.AtTime.AddDays(1).AddSeconds(1);
        Assert.StartsWith("trap no-hold", Assert.Single(run.Step("publish", due).Reasons), StringComparison.Ordinal);
        JsonObject lifted = PolicyFixture.Body(hold);
        lifted["key"] = "HOLD-LIFTED";
        run.Add("work-order", lifted, supersedes: hold.Id);
        Assert.Equal("stopped", run.Step("publish", due).State);
    }

    [Fact]
    public void AReviewWithFindingsSendsTheWorkBackToItsAuthorAndTheReviewWaits()
    {
        using var run = new FlowFixture();
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        run.Review(lyrics, "findings");
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("next", draft.State);
        Assert.StartsWith($"review ", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Contains($"found what to change in result {lyrics.Id}: Test Builder goes on with another attempt", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("it waits on draft, which is next", run.Step("review-lyrics").Reasons[0]);

        // A result short of its declared output waits for a review rather than counting as done.
        RecordEnvelope partial = run.Result(drafting, "completed");
        Assert.Equal("in-review", run.Step("draft").State);
        Assert.Contains("its result names no output lyrics", run.Step("draft").Reasons[0], StringComparison.Ordinal);
        _ = partial;
    }

    [Fact]
    public void AReviewStepClosesOnlyOnAReviewByAPrincipalOfItsRoleOrItsOwnAssignee()
    {
        using var run = new FlowFixture();
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope outsider = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Outside Reviewer", ["roles"] = new JsonArray("contributor"), ["agent"] = new JsonObject { ["vendor"] = "Another", ["product"] = "Another Agent" } });

        // A pass by a principal who is not the lyrics' author, but whom neither the reviewer role nor the step's assignments name, closes nothing.
        RecordEnvelope passed = run.Estate.Add("review", outsider.Id, new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read the lyrics.", ["findings"] = new JsonArray() }.ToJsonString(), lyrics.Reference);
        FlowPosition.StepInstance review = run.Step("review-lyrics");
        Assert.NotEqual("done", review.State);
        Assert.Null(review.Review);
        Assert.Equal("no review of the exact result it reviews passes, by a principal of the reviewer role other than its author", Assert.Single(review.Items).Reasons[0]);
        Assert.Equal("blocked", run.Step("master").State);

        // Assigned the review step, the same principal carries it: its pass now closes the step.
        run.Assign("review-lyrics", outsider.Id, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics")));
        review = run.Step("review-lyrics");
        Assert.Equal("done", review.State);
        Assert.Equal(passed.Id, review.Review!.Id);

        // A principal the reviewer role names closes it as well: the operator, through the roster.
        using var named = new FlowFixture();
        RecordEnvelope theirs = named.Result(named.Assign("draft", named.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope verdict = named.Review(theirs);
        Assert.Equal(("done", verdict.Id), (named.Step("review-lyrics").State, named.Step("review-lyrics").Review!.Id));
    }

    [Fact]
    public void AStepTheFlowsReviewOptionCoversIsInReviewUntilAnIndependentReviewPassesIt()
    {
        using var run = new FlowFixture(options: new JsonObject { ["review"] = "independent" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("in-review", draft.State);
        Assert.StartsWith("item lyrics is in-review: it awaits an independent review at its exact hash", draft.Reasons[0], StringComparison.Ordinal);

        // A review by the result's own author does not count; the operator's does.
        run.Add("review", new JsonObject { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "My own.", ["findings"] = new JsonArray() }, builder: true, subject: lyrics.Reference);
        Assert.Equal("in-review", run.Step("draft").State);
        run.Review(lyrics);
        Assert.Equal("done", run.Step("draft").State);
    }

    [Fact]
    public void ThePositionIsTheSameFromAFreshReadOfTheSameRecords()
    {
        using var run = new FlowFixture();
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        EstateModel first = run.Estate.Load();
        EstateModel second = Vericairn.Records.Estate.EstateLoader.Load(run.Estate.Root);
        Assert.Equal(
            FlowPosition.RenderJson(first, FlowPosition.Derive(first, run.Goal, run.Estate.Clock)).ToJsonString(),
            FlowPosition.RenderJson(second, FlowPosition.Derive(second, run.Goal, run.Estate.Clock)).ToJsonString());
        Assert.Contains("Flow: example/song 1.0.0", GoalView.RenderText(first, first.Latest(run.Goal)), StringComparison.Ordinal);
        Assert.Contains("\"flow\"", GoalView.RenderJson(first, first.Latest(run.Goal)), StringComparison.Ordinal);
    }

    [Fact]
    public void BranchStepsAreOffUntilTheBranchIsEntered()
    {
        JsonObject song = FlowFixture.Song();
        song["steps"]!.AsArray().Add(FlowFixture.Step("withdraw", "operation", "decider", optional: "on", done: new JsonArray(FlowFixture.Marker("WITHDRAWAL-OBSERVED"))));
        song["branches"] = new JsonArray(new JsonObject { ["name"] = "withdrawal", ["when"] = new JsonArray(FlowFixture.Marker("WITHDRAWN")), ["steps"] = new JsonArray("withdraw"), ["returnTo"] = "accept" });
        using var run = new FlowFixture(song);
        Assert.Equal("off", run.Step("withdraw").State);
        Assert.StartsWith("its branch withdrawal is not entered: not-met:", run.Step("withdraw").Reasons[0], StringComparison.Ordinal);
        run.Marked("WITHDRAWN");
        Assert.NotEqual("off", run.Step("withdraw").State);
    }

    [Fact]
    public void AWatchIsNextUntilItsPredicatesHoldAndStopsForAttentionPastItsLimit()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject observe = FlowFixture.Step("observe", "operation", "writer", done: new JsonArray(FlowFixture.Marker("DELIVERED")));
        observe["after"] = new JsonArray();
        observe["when"] = new JsonObject { ["watch"] = new JsonObject { ["every"] = "PT6H", ["limit"] = "P2D" } };
        flow["steps"]!.AsArray().Add(observe);
        using var run = new FlowFixture(flow);
        FlowPosition.StepInstance watched = run.Step("observe");
        Assert.Equal("next", watched.State);
        Assert.StartsWith("watched every PT6H until its done predicates hold (work-order records with key DELIVERED", watched.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("stopped", run.Step("observe", run.Estate.Clock.AddDays(3)).State);
        Assert.EndsWith("required attention", run.Step("observe", run.Estate.Clock.AddDays(3)).Reasons[0], StringComparison.Ordinal);
        run.Marked("DELIVERED");
        Assert.Equal("done", run.Step("observe", run.Estate.Clock.AddDays(3)).State);
    }

    private static List<string> States(FlowPosition.Position position) => position.Steps.Select(s => $"{s.Name} {s.State}").ToList();
}
