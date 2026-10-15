// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>A goal carried from proposal to acceptance through the SDK against a running daemon: the same goal view and the same refusals as the embedded runtime.</summary>
[Collection("daemon")]
public class GoalApiTests
{
    public GoalApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task AGoalGoesFromProposalToItsSponsorsAcceptanceThroughTheApi()
    {
        using var estate = new TestEstate(recordFormat: 2);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(estate.Root)!);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        string reviewer = Ok(api.NewRecord(new NewRecordRequest("principal", op, Json("""{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Reviewer"}}""")))).Id;

        string goal = Ok(api.GoalPropose(new GoalProposeRequest(builder, Json($$$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}},{"evidence":"The guide describes the widget.","check":"Read the guide."}],"constraints":[],"kind":"finite","sponsor":"{{{op}}}","budget":{"wallMinutes":600,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""")))).Id;
        Assert.Equal("proposed", Assert.Single(api.Goals()).Status);
        Assert.Equal("refused", api.GoalActivate(goal, new GoalActionRequest(builder)).Problem!.Code);
        Ok(api.GoalActivate(goal, new GoalActionRequest(op)));
        Ok(api.GoalPause(goal, new GoalActionRequest(op, "Waiting.")));
        Ok(api.GoalResume(goal, new GoalActionRequest(op)));

        Ok(api.NewRecord(new NewRecordRequest("work-order", op, Json($$$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"The goal's work.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{{goal}}}"}],"deliverables":["widget"]}"""))));
        string assignment = Ok(api.Assign(new AssignRequest("W-1", builder, op, Json("""{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests pass"],"recovery":"Record a result."}""")))).Id;
        string attempt = Ok(api.Attempt(new AttemptRequest(assignment, builder, "example repository, work branch", "Build it."))).Id;
        string result = Ok(api.Result(new ResultRequest(attempt, builder, Json("""{"outcome":"completed","summary":"Built the widget and its guide.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")))).Id;
        JsonElement order = api.Brief("W-1")!.Json.GetProperty("workOrder");
        Ok(api.NewRecord(new NewRecordRequest("acceptance", op, Json("""{"statement":"Accepted.","basis":[],"self":true}"""), $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}")));

        // The step's author is refused, the reviewer evaluates, and only the sponsor accepts; refusals are 422s with the reasons, a missing goal a 404.
        Assert.Equal("refused", api.GoalEvaluate(goal, new GoalEvaluateRequest(builder, "Mine.", [new GoalVerdict(2, "met", "Built.", [result])])).Problem!.Code);
        Ok(api.GoalEvaluate(goal, new GoalEvaluateRequest(reviewer, "Read the work and the guide.", [new GoalVerdict(2, "met", "The guide describes the widget.", [result])])));
        WriteOutcome byReviewer = api.GoalAccept(goal, new GoalAcceptRequest(reviewer, "Done."));
        Assert.Equal("refused", byReviewer.Problem!.Code);
        Assert.Contains("never accepted by its evaluator", byReviewer.Problem.Message, StringComparison.Ordinal);
        Assert.Equal("not-found", api.GoalAccept("01M4TRNE1GYEK4P8MXQAB3C8Q4", new GoalAcceptRequest(op, "Done.")).Problem!.Code);
        Assert.Null(api.Goal("01M4TRNE1GYEK4P8MXQAB3C8Q4"));
        Assert.Equal(CanonicalJson.SerializePretty(host.Service.Goal(goal)!.Json), CanonicalJson.SerializePretty(api.Goal(goal)!.Json));
        Assert.True(api.Goal(goal)!.Json.GetProperty("ready").GetBoolean());

        Ok(api.GoalAccept(goal, new GoalAcceptRequest(op, "Accepted on the reviewer's evaluation.")));
        Assert.Equal("achieved", Assert.Single(api.Goals()).Status);
        Assert.Contains("Achieved: acceptance", api.Goal(goal)!.Text, StringComparison.Ordinal);
        Assert.Equal("refused", api.GoalStop(goal, new GoalActionRequest(op)).Problem!.Code);
        Assert.Equal(0, api.Check().Errors);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem is null ? "no problem reported" : outcome.Problem.Message + "\n" + string.Join('\n', outcome.Problem.Findings.Select(f => $"{f.Severity} {f.Code} {f.Location}: {f.Message}")));
        return outcome.Written!;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
