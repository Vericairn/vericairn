// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The sponsor closes a run that will never complete with a reason for each unsettled step (FIX-143, decision
/// 01M7GAX5DD9BMV5QDXM7RJNAZ4; the estate's design/flow.md § 8): the run reads closed in goal show, status and progress, each of those
/// steps unsettled with its reason and none done; a required reserved step reads not done with its reason; closing is who decides for
/// the goal's, a person's, never an agent's; and it stays distinct from stopping, which abandons the goal.
/// </summary>
public class RunClosingTests
{
    [Fact]
    public void AClosedReleaseRunReadsClosedWithItsUnsettledStepsAndNoneDone()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        RecordEnvelope candidate = ToPublication(run);

        // The X post is skipped by the operator's dismissal; the listing is never confirmed, and its request is still open.
        run.Dismiss(run.Request("announce-x"), "No post on X for this release.");
        run.Done(run.Request("announce-discussions"));
        RecordEnvelope listing = run.Request("mcp-registry");
        run.Accept(run.Carrier);
        Assert.Equal(("off", "stopped", "done"), (run.State("announce-x"), run.State("mcp-registry"), run.State("close")));
        Assert.False(run.Position().Complete);

        // The sponsor closes the run with a reason for the listing and one for the rest.
        RunClosings.Plan plan = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "Nothing else remains of this release.",
            [("mcp-registry", "The registry listing is retired for this product.")], run.Estate.Clock);
        Assert.Empty(plan.Refusals);
        RunClosings.Unsettled named = Assert.Single(plan.Steps);
        Assert.Equal(("mcp-registry", null, "The registry listing is retired for this product."), (named.Key, named.Instance, named.Reason));
        RecordEnvelope closing = run.Add("goal-status", plan.Body(), subject: run.Goal.Reference);
        run.AssertValid();
        EstateModel model = run.Estate.Load();
        Assert.Equal(("closed", closing.Id), (GoalRules.Status(model, run.Goal), GoalRules.Closing(model, run.Goal)?.Id));

        // The run reads closed: the listing unsettled with its reason, never done, the skipped post still off, and no stop or request due.
        FlowPosition.Position position = run.Position();
        Assert.Equal(closing.Id, position.Closing?.Id);
        Assert.False(position.Complete);
        FlowPosition.StepInstance unsettled = run.Step("mcp-registry");
        Assert.Equal("unsettled", unsettled.State);
        Assert.StartsWith($"unsettled: its run was closed by goal-status {closing.Id} at ", unsettled.Reasons[0], StringComparison.Ordinal);
        Assert.EndsWith(": The registry listing is retired for this product.", unsettled.Reasons[0], StringComparison.Ordinal);
        Assert.Null(unsettled.Command);
        Assert.Null(unsettled.Stop);
        Assert.Equal("off", run.State("announce-x"));
        Assert.Equal(["unsettled"], position.Steps.Where(s => s.State is not ("done" or "off")).Select(s => s.State).Distinct().ToArray());
        Assert.Empty(position.Stops);
        FlowStops.StepRequest withdrawn = Assert.Single(FlowStops.Classify(model, position, new FlowRuns.StepRef("mcp-registry", null)));
        Assert.Equal((listing.Id, "withdrawn"), (withdrawn.Request.Id, withdrawn.State));
        Assert.Equal("published", EstateModel.BodyString(model.Latest(candidate), "status"));

        // goal show, its JSON, status and the goal's progress all read it closed, with the unsettled step and its reason.
        string shown = GoalView.RenderText(model, run.Goal, run.Estate.Clock);
        Assert.Contains("Status: closed", shown, StringComparison.Ordinal);
        Assert.Contains($"the run was closed by goal-status {closing.Id}", shown, StringComparison.Ordinal);
        Assert.Contains("1 unsettled step, never done", shown, StringComparison.Ordinal);
        Assert.Contains("mcp-registry  unsettled  MCP Registry listing confirmed", shown, StringComparison.Ordinal);
        using JsonDocument goal = JsonDocument.Parse(GoalView.RenderJson(model, run.Goal, run.Estate.Clock));
        JsonElement closed = goal.RootElement.GetProperty("flow").GetProperty("closed");
        Assert.Equal(closing.Id, closed.GetProperty("record").GetProperty("id").GetString());
        JsonElement entry = Assert.Single(closed.GetProperty("unsettled").EnumerateArray());
        Assert.Equal(("mcp-registry", "The registry listing is retired for this product.", "unsettled"), (entry.GetProperty("key").GetString(), entry.GetProperty("reason").GetString(), entry.GetProperty("state").GetString()));
        Assert.Equal("unsettled", goal.RootElement.GetProperty("flow").GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "mcp-registry").GetProperty("state").GetString());
        string status = EstateStatus.Render(model, new FindingList(), run.Estate.Clock);
        Assert.Contains("closed: flow vericairn.first-party/release 1.0.1", status, StringComparison.Ordinal);
        Assert.Contains("unsettled: mcp-registry (The registry listing is retired for this product.)", status, StringComparison.Ordinal);
        using JsonDocument canonical = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        JsonElement shownRun = Assert.Single(canonical.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal("closed", shownRun.GetProperty("goal").GetProperty("status").GetString());
        Assert.Equal(1, shownRun.GetProperty("closed").GetProperty("unsettled").GetArrayLength());
        Assert.False(shownRun.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void ARequiredReservedStepReadsNotDoneWithItsReasonAndTheGoalMayStillBeAccepted()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        ToAcceptance(run);

        // The release is never published: the sponsor closes the run with one reason, and publication reads not done with it.
        RunClosings.Plan plan = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "The release is replaced by the next one.", [], run.Estate.Clock);
        Assert.Empty(plan.Refusals);
        Assert.Contains(plan.Steps, s => s.Key == "publish");
        Assert.All(plan.Steps, s => Assert.Equal("The release is replaced by the next one.", s.Reason));
        RecordEnvelope closing = run.Add("goal-status", plan.Body(), subject: run.Goal.Reference);
        run.AssertValid();
        FlowPosition.StepInstance publish = run.Step("publish");
        Assert.Equal("unsettled", publish.State);
        Assert.EndsWith(": The release is replaced by the next one.", publish.Reasons[0], StringComparison.Ordinal);
        Assert.Contains(publish.Done, d => d.Verdict.Value != "met");
        Assert.Null(publish.Command);
        Assert.DoesNotContain(run.Position().Steps, s => plan.Steps.Any(u => u.Key == s.Step.Key) && s.State == "done");

        // A record that comes after the closing makes no unsettled step done: the run stays closed, the step unsettled while its act is missing.
        Assert.Equal("closed", GoalRules.Status(run.Estate.Load(), run.Goal));
        Assert.Equal(closing.Id, run.Position().Closing?.Id);

        // Closing is neither acceptance nor stopping: the goal may still be accepted on its evaluation, and its run stays closed.
        run.Accept(run.Carrier);
        RecordEnvelope evaluation = run.ByReviewer("review", new JsonObject
        {
            ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read the carrier's acceptance.", ["findings"] = new JsonArray(),
            ["conditions"] = new JsonArray(new JsonObject { ["verdict"] = "met", ["reason"] = "The carrier is accepted.", ["read"] = new JsonArray() }),
        }, run.Goal.Reference);
        Assert.Empty(GoalView.NotReady(run.Estate.Load(), run.Goal));
        run.Add("acceptance", new JsonObject { ["statement"] = "Accepted on the evaluation; the run is closed.", ["basis"] = new JsonArray(evaluation.Id), ["self"] = true }, subject: run.Goal.Reference);
        run.AssertValid();
        Assert.Equal("achieved", GoalRules.Status(run.Estate.Load(), run.Goal));
        Assert.Equal("unsettled", run.State("publish"));
    }

    [Fact]
    public void AnAgentsCloseIsRefusedAndClosesNothing()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        RunClosings.Plan refused = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.BuilderId, "The builder would close it.", [], run.Estate.Clock);
        Assert.Contains("A run is closed by who decides for its goal, a person, as accepting and stopping are; an agent never closes a run.", refused.Refusals);
        Assert.Empty(refused.Steps);

        // A closing an agent writes around the runtime is refused by its record type and closes nothing.
        RunClosings.Plan plan = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "Closed.", [], run.Estate.Clock);
        run.Add("goal-status", plan.Body(), builder: true, subject: run.Goal.Reference);
        EstateModel model = run.Estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "goal-status.closer");
        Assert.Null(GoalRules.Closing(model, run.Goal));
        Assert.Equal("active", GoalRules.Status(model, run.Goal));
        Assert.Null(run.Position().Closing);
        Assert.DoesNotContain(run.Position().Steps, s => s.State == "unsettled");
    }

    [Theory]
    [InlineData("unknown-step")]
    [InlineData("instance")]
    [InlineData("blank")]
    [InlineData("twice")]
    [InlineData("unsettled-without-close")]
    [InlineData("resumed")]
    public void AClosingKeepsItsShape(string change)
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        JsonObject body = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "Closed.", [], run.Estate.Clock).Body();
        var steps = (JsonArray)body["unsettled"]!;
        string code = "goal-status.unsettled";
        switch (change)
        {
            case "unknown-step":
                steps.Add(new JsonObject { ["key"] = "no-such-step", ["reason"] = "Gone." });
                break;
            case "instance":
                steps[0]!["instance"] = "intro";
                break;
            case "blank":
                steps[0]!["reason"] = " ";
                break;
            case "twice":
                steps.Add(steps[0]!.DeepClone());
                break;
            case "unsettled-without-close":
                body["status"] = "paused";
                break;
            case "resumed":
                run.Add("goal-status", body, subject: run.Goal.Reference);
                run.AssertValid();
                body = new JsonObject { ["status"] = "active", ["note"] = "Resumed." };
                code = "goal-status.closed";
                break;
        }

        run.Add("goal-status", body, subject: run.Goal.Reference);
        Assert.Contains(run.Estate.Load().Findings, f => f.Code == code && f.Severity == Severity.Error);
    }

    [Fact]
    public void AClosedRunIsStoppedOnlyAsAbandonedAndNotClosedAgain()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        RunClosings.Plan plan = RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "Closed.", [], run.Estate.Clock);
        run.Add("goal-status", plan.Body(), subject: run.Goal.Reference);
        Assert.Contains($"Goal {run.Goal.Id}'s run was already closed by goal-status ", Assert.Single(RunClosings.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "Again.", [], run.Estate.Clock).Refusals), StringComparison.Ordinal);
        run.Add("goal-status", new JsonObject { ["status"] = "abandoned", ["note"] = "Stopped after the close." }, subject: run.Goal.Reference);
        run.AssertValid();
        Assert.Equal("abandoned", GoalRules.Status(run.Estate.Load(), run.Goal));
    }

    [Fact]
    public void ACloseNeedsAReasonForEveryUnsettledStepAndNamesOnlyUnsettledSteps()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        run.Accept(run.First);
        run.Accept(run.Second);
        EstateModel model = run.Estate.Load();
        Assert.Equal("done", run.State("placed-work"));
        IReadOnlyList<string> refusals = RunClosings.Prepare(model, run.Goal, run.Estate.OperatorId, null, [("placed-work", "Done already."), ("no-such", "x"), ("publish", " ")], run.Estate.Clock).Refusals;
        Assert.Contains("Step placed-work is done, not unsettled; only a step that is neither done nor off is closed with a reason.", refusals);
        Assert.Contains("The run has no step no-such; name a step instance as key, or key[instance] for a step that runs over a list.", refusals);
        Assert.Contains("The reason for step publish is blank.", refusals);
        Assert.Contains(refusals, r => r.StartsWith("Every unsettled step needs a reason: give one for all, or one for each of docs-before-freeze (next)", StringComparison.Ordinal));

        // A run whose steps are all done or off has nothing to close: it is complete, and its goal is accepted.
        using var complete = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        ToPublication(complete);
        complete.Dismiss(complete.Request("announce-x"));
        complete.Done(complete.Request("mcp-registry"));
        complete.Done(complete.Request("announce-discussions"));
        complete.Accept(complete.Carrier);
        Assert.True(complete.Position().Complete);
        Assert.Equal($"Goal {complete.Goal.Id}'s run has no unsettled step: every step is done or off, so the run is complete and its goal is accepted, not closed.",
            Assert.Single(RunClosings.Prepare(complete.Estate.Load(), complete.Goal, complete.Estate.OperatorId, "x", [], complete.Estate.Clock).Refusals));
    }

    private static void OptionalX(JsonObject extension)
    {
        extension["version"] = "1.0.1";
        var steps = (JsonArray)extension["steps"]!;
        steps.Remove(steps.First(s => s!["key"]!.GetValue<string>() == "nuget"));
        JsonObject x = steps.First(s => s!["key"]!.GetValue<string>() == "announce-x")!.AsObject();
        x.Remove("required");
        x["optional"] = true;
        x["default"] = "on";
    }

    private static RecordEnvelope ToAcceptance(ReleaseFlowFixture run)
    {
        run.Accept(run.First);
        run.Accept(run.Second);
        run.GuidesChecked();
        run.WorkStep("docs-set", ("manifest", ReleaseFlowFixture.Hash('1'), "application/json"), ("notes", ReleaseFlowFixture.Hash('2'), "text/markdown"));
        run.WorkStep("release-commit", ("frozen-index", ReleaseFlowFixture.Hash('3'), "application/json"));
        run.Settle(run.Intent(), "dispatched");
        RecordEnvelope candidate = run.Candidate();
        run.ReleaseReview(candidate);
        run.Accept(candidate);
        RecordEnvelope drafts = run.WorkStep("draft-announcement", ("discussion", ReleaseFlowFixture.Hash('d'), "text/markdown"), ("x-post", ReleaseFlowFixture.Hash('e'), "text/plain"));
        run.ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read both drafts.", ["findings"] = new JsonArray() }, drafts.Reference);
        return candidate;
    }

    private static RecordEnvelope ToPublication(ReleaseFlowFixture run)
    {
        RecordEnvelope candidate = ToAcceptance(run);
        run.Published(candidate);
        return candidate;
    }
}
