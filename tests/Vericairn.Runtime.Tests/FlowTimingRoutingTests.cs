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
/// Timed steps under goal step (the estate's design/flow.md § 7; PROC-18): Community takes a due step when goal step is called, never one
/// not yet due, whose due time the goal view shows; and a step that missed its deadline is asked of the sponsor once, then runs once, late.
/// The clock is the runtime's own, so the times are years before and after it.
/// </summary>
[Collection("home")]
public class FlowTimingRoutingTests
{
    [Fact]
    public void GoalStepTakesNoStepBeforeItIsDueAndTheGoalViewShowsItsDueTime()
    {
        using var run = new RunEstate(recordFormat: 3);
        string goal = FlowStepRoutingTests.Run(run, Song(new JsonObject { ["at"] = new JsonObject { ["time"] = "2099-01-01T09:00:00Z" } }));
        JsonElement draft = Step(run, goal, "draft");
        Assert.Equal("blocked", draft.GetProperty("state").GetString());
        Assert.Equal(("2099-01-01T09:00:00Z", "UTC"), (draft.GetProperty("due").GetProperty("at").GetString(), draft.GetProperty("due").GetProperty("zone").GetString()));
        Assert.StartsWith("it is due at 2099-01-01T09:00:00Z: at 2099-01-01T09:00:00Z", draft.GetProperty("reasons")[0].GetString(), StringComparison.Ordinal);
        GoalStepResponse nothing = run.Service.GoalStep(goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, FlowStepRoutingTests.Command(run, "early"), WallMinutes: 30));
        Assert.Equal("none", nothing.Outcome);
        Assert.DoesNotContain(run.Estate.Load().OfType("assignment"), a => FlowRuns.StepOf(a) is not null);
    }

    [Fact]
    public void AStepThatMissedItsDeadlineIsAskedOfTheSponsorOnceAndThenRunsOnceLate()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        string goal = FlowStepRoutingTests.Run(run, Song(new JsonObject { ["at"] = new JsonObject { ["time"] = "2026-01-01T09:00:00Z" }, ["deadline"] = "PT1H" }));
        JsonElement draft = Step(run, goal, "draft");
        Assert.Equal(("stopped", "attention", "missed"), (draft.GetProperty("state").GetString(), draft.GetProperty("stop").GetString(), draft.GetProperty("attention").GetProperty("kind").GetString()));
        Assert.Equal("attention", draft.GetProperty("due").GetProperty("missed").GetString());

        GoalStepResponse asked = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(("ask", "draft", "attention", op), (asked.Outcome, asked.Step, asked.Route, asked.Principal));
        RecordEnvelope ask = run.Estate.Load().ById[asked.Ask!.Id];
        Assert.StartsWith("Required attention in goal Release the song, step draft: step draft missed its deadline at 2026-01-01T10:00:00Z", EstateModel.BodyString(ask, "question"), StringComparison.Ordinal);
        Assert.StartsWith("Read why it was not taken by its deadline", EstateModel.BodyString(ask, "recommendation"), StringComparison.Ordinal);
        Assert.Equal("none", run.Service.GoalStep(goal, new GoalStepRequest(op)).Outcome);

        // The sponsor answers: the step runs once, late, routed by its role as any step is.
        var answer = new JsonObject
        {
            ["title"] = "Run the draft late", ["statement"] = "Let it run once, late.", ["summary"] = "The draft runs once, late.", ["synthesis"] = "It missed its deadline.",
            ["effects"] = new JsonArray("The draft runs once."), ["affects"] = new JsonArray(), ["decidedBy"] = op,
            ["decidedAt"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["stake"] = "low", ["notAuthorized"] = new JsonArray("publication"),
            ["answers"] = new JsonArray(ask.Id),
        };
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, JsonSerializer.SerializeToElement(answer))));
        draft = Step(run, goal, "draft");
        Assert.Equal("next", draft.GetProperty("state").GetString());
        Assert.EndsWith("and the sponsor answered the ask about it: it runs once, late", draft.GetProperty("reasons").EnumerateArray().Last().GetString(), StringComparison.Ordinal);
        GoalStepResponse late = FlowStepRoutingTests.Step(run, goal, "late");
        Assert.True(late.Ok, late.Message);
        Assert.Equal(("draft", "agent"), (late.Step, late.Route));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>The song flow with its first step, the draft, taking a timing.</summary>
    private static JsonObject Song(JsonObject when)
    {
        JsonObject song = FlowFixture.Song();
        song["steps"]![0]!["when"] = when;
        return song;
    }

    private static JsonElement Step(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key && s.GetProperty("instance").ValueKind == JsonValueKind.Null);
}
