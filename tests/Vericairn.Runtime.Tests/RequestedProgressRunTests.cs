// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RequestedProgressRunTests
{
    [Fact]
    public void PreflightRetainsTheSelectedGoalsRequestBeforeItsRunExists()
    {
        using var run = new RunEstate(recordFormat: 3);
        EstateModel model = run.Estate.Load();
        RecordEnvelope original = model.Latest(model.ById[run.Goal]);
        JsonObject body = JsonNode.Parse(original.Body.GetRawText())!.AsObject();
        body["progressRequest"] = JsonNode.Parse(ProgressRequestTests.DefaultRequest);
        string revised = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), original.Id))).Id;
        RunEstate.Ok(run.Service.GoalActivate(revised, new GoalActionRequest(run.Estate.OperatorId)));

        RecordEnvelope order = WorkView.FindOrder(run.Estate.Load(), "W-1")!;
        body["plan"] = new JsonObject
        {
            ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order.Id),
        };
        body["progressRequest"] = JsonNode.Parse(ProgressRequestTests.FullRequest);
        string proposed = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(body.ToJsonString())))).Id;
        RunEstate.Ok(run.Service.GoalActivate(proposed, new GoalActionRequest(run.Estate.OperatorId)));
        model = run.Estate.Load();
        RecordEnvelope selected = GoalView.Find(model, proposed)!;
        Assert.NotNull(ProgressRequests.Resolve(model, model.ById[run.Assignment]).Problem);
        Assert.Empty(run.Service.Runs().Runs);

        RunResponse started = run.Service.RunStart(new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId,
            run.Estate.OperatorId, run.Resource, Goal: selected.Id, Settings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["command"] = FakeHarness.Setting("generic", run.Report("requested-progress")),
            }));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(directory.ContextFile));
        ContextPackage package = retained.RootElement.GetProperty("context").Deserialize<ContextPackage>(ProtocolJson.Options)!;
        Assert.True(ContextAccounting.Verify(package));
        JsonElement assignment = retained.RootElement.GetProperty("assignments")[0];
        Assert.Equal(ProgressRequestTests.FullRequest, CanonicalJson.Serialize(assignment.GetProperty("progressRequest")));
        Assert.Equal(selected.Id, assignment.GetProperty("progressRequestSource").GetProperty("record").GetString());
        Assert.False(assignment.TryGetProperty("progressRequestProblem", out _));
        string brief = File.ReadAllText(directory.BriefFile);
        Assert.Contains("Source: goal record " + selected.Id, brief, StringComparison.Ordinal);
        Assert.DoesNotContain("Unavailable: Several goals", brief, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
