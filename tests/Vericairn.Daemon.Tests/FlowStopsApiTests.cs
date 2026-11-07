// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A run's stops through the SDK against a running daemon (the estate's design/flow.md § 8; design/cohesion-contract.md § 4): the goal,
/// next-step and status reads carry the same stops the embedded runtime derives, the operator's next reserved step with its exact command
/// among them, and the request queue delivers its request; every client's rows come from them alone.
/// </summary>
[Collection("daemon")]
public class FlowStopsApiTests
{
    public FlowStopsApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheSdksReadsCarryARunsStopsAsTheEmbeddedRuntimeDerivesThem()
    {
        using FlowFixture run = FlowFixture.AtAcceptance();
        await using DaemonHost host = await DaemonTestHome.StartAsync(run.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(run.Estate.Root)!);
        string goal = run.Goal.Id;

        JsonNode? embedded = JsonNode.Parse(host.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("stops").GetRawText());
        GoalResponse shown = (await api.GoalAsync(goal, TestContext.Current.CancellationToken))!;
        NextResponse routed = (await api.GoalNextAsync(goal, TestContext.Current.CancellationToken))!;
        StatusResponse status = await api.StatusAsync(TestContext.Current.CancellationToken);
        Assert.True(JsonNode.DeepEquals(embedded, JsonNode.Parse(shown.Json.GetProperty("flow").GetProperty("stops").GetRawText())));
        Assert.True(JsonNode.DeepEquals(embedded, JsonNode.Parse(routed.Json.GetProperty("flow").GetProperty("stops").GetRawText())));
        Assert.True(JsonNode.DeepEquals(embedded, JsonNode.Parse(Assert.Single(status.Json.GetProperty("runs").EnumerateArray()).GetProperty("stops").GetRawText())));

        // The operator's next reserved step, its exact command and its delivered request, as every read gives it.
        JsonElement stop = Assert.Single(shown.Json.GetProperty("flow").GetProperty("stops").EnumerateArray());
        Assert.Equal(("reserved", "accept", "delivered", run.AcceptCommandNow, run.Request!.Id),
            (stop.GetProperty("kind").GetString(), stop.GetProperty("name").GetString(), stop.GetProperty("delivery").GetString(), stop.GetProperty("command").GetString(), stop.GetProperty("record").GetProperty("id").GetString()));
        JsonElement route = Assert.Single(routed.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "accept").GetProperty("route");
        Assert.Equal(("requested", run.Request.Id), (route.GetProperty("state").GetString(), route.GetProperty("request").GetString()));
        OperatorRequestsResponse queue = await api.OperatorRequestsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(run.Request.Id, Assert.Single(queue.Json.GetProperty("delivered").EnumerateArray()).GetProperty("revision").GetString());
        Assert.Empty(queue.Json.GetProperty("withdrawn").EnumerateArray());

        // Every client's rows come from these stops alone, the same from status and from the goal view.
        IReadOnlyList<StopRow> fromStatus = EstateView.From(status, "now").Stops;
        Assert.Equal(fromStatus, GoalPresentation.From(shown.Json, routed.Json).Stops);
        Assert.Equal(run.AcceptCommandNow, Assert.Single(fromStatus).Command);
    }

    [Fact]
    public void AGoalStepsRequestTravelsInItsResponseOnlyWhenOneWasWritten()
    {
        var written = new RecordWritten("records/operator-request/2026-11/01M6VTAZC9A0244GVQ5RNGH7KD.json", "01M6VTAZC9A0244GVQ5RNGH7KD", "sha256:" + new string('a', 64));
        string json = JsonSerializer.Serialize(new GoalStepResponse("01M6VTAZC8CYK49GHEENJFTWPJ", "request", "Recorded.", true, "accept", "request") { Request = written }, ProtocolJson.Options);
        Assert.Equal(written, JsonSerializer.Deserialize<GoalStepResponse>(json, ProtocolJson.Options)!.Request);
        Assert.DoesNotContain("\"request\":", JsonSerializer.Serialize(new GoalStepResponse("01M6VTAZC8CYK49GHEENJFTWPJ", "none", "Nothing.", true), ProtocolJson.Options), StringComparison.Ordinal);
    }
}
