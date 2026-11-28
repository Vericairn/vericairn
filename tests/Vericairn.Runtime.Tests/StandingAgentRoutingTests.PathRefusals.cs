// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public partial class StandingAgentRoutingTests
{
    [Fact]
    public void StandingReadsWarnAnActiveLegacyGoalBeforeItsFirstRequest()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        JsonObject body = StandingBody(run, triggers: new JsonArray(new JsonObject { ["key"] = "asked", ["kind"] = "request" }),
            scope: new JsonArray("Research the widget"));
        string goal = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("goal", op, JsonSerializer.SerializeToElement(body)))).Id;
        run.Estate.Add("goal-status", op, """{"status":"active","note":"Activated by an earlier release."}""", run.Estate.Load().ById[goal].Reference);
        int before = run.Estate.Load().ById.Count;

        GoalResponse show = run.Service.Goal(goal)!;
        NextResponse next = run.Service.GoalNext(goal)!;
        GoalDueResponse due = run.Service.GoalDue(goal)!;
        StatusResponse status = run.Service.Status();

        Assert.False(status.HasErrors);
        Assert.Empty(due.Json.GetProperty("occurrences").EnumerateArray());
        AssertRefusal(show.Text, show.Json.GetProperty("standing"));
        AssertRefusal(next.Text, next.Json.GetProperty("standing"));
        AssertRefusal(due.Text, due.Json);
        AssertRefusal(status.Text, Assert.Single(status.Json.GetProperty("standingAgents").EnumerateArray()));
        GoalStepResponse step = Step(run, goal, "legacy-standing-request", occurrence: "request");
        Assert.False(step.Ok);
        Assert.Equal("flow.permitted-paths", step.Problem!.Code);
        Assert.Equal(before, run.Estate.Load().ById.Count);
    }

    [Fact]
    public void StandingNextWarnsWhenTheChosenOccurrenceCannotReuseItsHeldBrief()
    {
        using var run = new RunEstate(recordFormat: 3);
        string goal = Standing(run);
        GoalStepResponse first = Step(run, goal, "standing-held-brief");
        Assert.True(first.Ok, first.Message);
        Assert.Equal("observed-success", run.Settled(first.Run!.Run).Status);
        string assignment = first.Assignment!.Id;
        Blocked(run, assignment);
        ReviseStandingOrder(run, run.Estate.Load().ById[assignment].Subject!.Id, new JsonArray("path:research/notes/**"));
        int before = run.Estate.Load().ById.Count;

        NextResponse next = run.Service.GoalNext(goal)!;
        Assert.Equal(first.Step, next.Json.GetProperty("next").GetString());
        string warning = AssertRefusal(next.Text, next.Json.GetProperty("standing"));
        Assert.Contains($"vericairn work takeover {assignment} --by <person>", warning, StringComparison.Ordinal);
        Assert.Contains("revise the goal", warning, StringComparison.Ordinal);
        GoalStepResponse step = Step(run, goal, "standing-held-brief-retry");
        Assert.False(step.Ok);
        Assert.Equal("flow.permitted-paths", step.Problem!.Code);
        Assert.Equal(warning, step.Problem.Message);
        Assert.Equal(before, run.Estate.Load().ById.Count);
    }

    private static string AssertRefusal(string text, JsonElement standing)
    {
        Assert.Contains("Dispatch to an agent would be refused (flow.permitted-paths)", text, StringComparison.Ordinal);
        JsonElement refusal = Assert.Single(standing.GetProperty("pathRefusals").EnumerateArray());
        Assert.Equal("research", refusal.GetProperty("step").GetString());
        Assert.Equal("flow.permitted-paths", refusal.GetProperty("code").GetString());
        return refusal.GetProperty("message").GetString()!;
    }
}
