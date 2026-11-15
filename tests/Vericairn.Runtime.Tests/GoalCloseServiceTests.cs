// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Closing a run through the runtime (FIX-143, decision 01M7GAX5DD9BMV5QDXM7RJNAZ4): goal close records each unsettled step of the
/// goal's run with the sponsor's reason, one for all or one per step, as a signed goal-status of closed; goal accept with a reason closes
/// the run and accepts the goal in one batch; an agent's close is refused before anything is written; and closing stays distinct from
/// stop, which abandons the goal.
/// </summary>
[Collection("home")]
public class GoalCloseServiceTests
{
    [Fact]
    public void TheSponsorClosesARunWithAReasonForEachUnsettledStepWhileAnAgentIsRefused()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run);
        int records = run.Estate.Load().Records.Count;

        // Every refusal comes before anything is written: an agent, no reason, and a reason that leaves steps without one or names a step
        // the run does not have.
        Refused(run.Service.GoalClose(goal, new GoalCloseRequest(run.Estate.BuilderId, "The builder would close it.")), "an agent never closes a run");
        Refused(run.Service.GoalClose(goal, new GoalCloseRequest(op)), "A run is closed with a reason");
        Refused(run.Service.GoalClose(goal, new GoalCloseRequest(op, null, [new GoalStepReason("publish", "No release this year.")])), "Every unsettled step needs a reason");
        Refused(run.Service.GoalClose(goal, new GoalCloseRequest(op, "Shelved.", [new GoalStepReason("encore", "None.")])), "The run has no step encore");
        Assert.Equal(records, run.Estate.Load().Records.Count);

        // The sponsor closes it: one reason for the publication and one for the instance of a clip, another for the rest.
        RecordWritten written = RunEstate.Ok(run.Service.GoalClose(goal, new GoalCloseRequest(op, "The song is shelved.",
            [new GoalStepReason("publish", "No release this year."), new GoalStepReason("shorts[intro]", "The intro has no clip.")])));
        EstateModel model = run.Estate.Load();
        RecordEnvelope closing = model.ById[written.Id];
        Assert.Equal(("goal-status", op, "closed", "The song is shelved."), (closing.Type, closing.By, EstateModel.BodyString(closing, "status"), EstateModel.BodyString(closing, "note")));
        Assert.NotEmpty(closing.Signatures);
        IReadOnlyList<RunClosings.Unsettled> unsettled = RunClosings.Of(closing);
        Assert.Contains(new RunClosings.Unsettled("publish", null, "No release this year."), unsettled);
        Assert.Contains(new RunClosings.Unsettled("shorts", "intro", "The intro has no clip."), unsettled);
        Assert.Contains(new RunClosings.Unsettled("shorts", "outro", "The song is shelved."), unsettled);
        Assert.Contains(new RunClosings.Unsettled("draft", null, "The song is shelved."), unsettled);
        Assert.Equal(0, run.Service.Check().Errors);

        // goal show reads the run closed, each of those steps unsettled with its reason, and no step done by the closing.
        JsonElement shown = run.Service.Goal(goal)!.Json;
        Assert.Equal("closed", shown.GetProperty("status").GetString());
        JsonElement flow = shown.GetProperty("flow");
        Assert.Equal(written.Id, flow.GetProperty("closed").GetProperty("record").GetProperty("id").GetString());
        Assert.All(flow.GetProperty("steps").EnumerateArray().Where(s => unsettled.Any(u => u.Key == s.GetProperty("key").GetString())),
            s => Assert.Equal("unsettled", s.GetProperty("state").GetString()));
        Assert.Empty(flow.GetProperty("stops").EnumerateArray());
        Assert.Contains("publish (No release this year.)", run.Service.Status().Text, StringComparison.Ordinal);

        // A closed run is closed once and never resumed; stopping still abandons the goal, which closing never does.
        Refused(run.Service.GoalClose(goal, new GoalCloseRequest(op, "Again.")), "was already closed by goal-status " + written.Id);
        Refused(run.Service.GoalResume(goal, new GoalActionRequest(op)), "run was closed");
        Refused(run.Service.GoalPause(goal, new GoalActionRequest(op)), "run was closed");
        GoalStepResponse step = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.False(step.Ok);
        RunEstate.Ok(run.Service.GoalStop(goal, new GoalActionRequest(op, "Shelved for good.")));
        Assert.Equal("abandoned", run.Service.Goal(goal)!.Json.GetProperty("status").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AcceptingWithAReasonClosesTheRunAndAcceptsTheGoalTogether()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json("""{"key":"PUBLISHED","kind":"ordinary","title":"Published","intent":"Stand in for the publication.","scope":["marker"],"exitQuestion":"Is it published?","inputs":[],"deliverables":["the release"]}"""))));
        string evaluation = RunEstate.Ok(run.Service.GoalEvaluate(goal, new GoalEvaluateRequest(run.Estate.BuilderId, "Read the marker."))).Id;

        // A reason naming a step the run does not have refuses the acceptance with it, before anything is written.
        int records = run.Estate.Load().Records.Count;
        Refused(run.Service.GoalAccept(goal, new GoalAcceptRequest(op, "Done.") { StepReasons = [new GoalStepReason("encore", "None.")] }), "The run has no step encore");
        Assert.Equal(records, run.Estate.Load().Records.Count);

        RecordWritten accepted = RunEstate.Ok(run.Service.GoalAccept(goal, new GoalAcceptRequest(op, "Accepted on the evaluation; the song is not produced.") { Reason = "The release went out without the song." }));
        EstateModel model = run.Estate.Load();
        Assert.Equal(records + 2, model.Records.Count);
        RecordEnvelope acceptance = model.ById[accepted.Id];
        Assert.Equal("acceptance", acceptance.Type);
        Assert.Equal([evaluation], GoalRules.Basis(acceptance));
        RecordEnvelope closing = Assert.Single(model.Current("goal-status"), s => EstateModel.BodyString(s, "status") == "closed");
        Assert.All(RunClosings.Of(closing), u => Assert.Equal("The release went out without the song.", u.Reason));
        JsonElement shown = run.Service.Goal(goal)!.Json;
        Assert.Equal("achieved", shown.GetProperty("status").GetString());
        Assert.Equal(closing.Id, shown.GetProperty("flow").GetProperty("closed").GetProperty("record").GetProperty("id").GetString());
        Assert.DoesNotContain(shown.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => RunClosings.Of(closing).Any(u => u.Key == s.GetProperty("key").GetString()) && s.GetProperty("state").GetString() == "done");
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static void Refused(WriteOutcome outcome, string diagnostic)
    {
        Assert.False(outcome.Ok);
        Assert.Contains(diagnostic, outcome.Problem!.Message, StringComparison.Ordinal);
    }
}
