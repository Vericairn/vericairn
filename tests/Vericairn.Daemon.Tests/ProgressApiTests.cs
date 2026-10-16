// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>The progress report through the daemon and the SDK (GET /v1/progress, additive to API v1): the embedded runtime's report, byte for byte.</summary>
[Collection("daemon")]
public class ProgressApiTests
{
    public ProgressApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedProgressKeepsItsActualOutcomeWhileHandoffAndResumeRemainReads(bool refused)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        TestEstate estate = harness.Estate;
        estate.Add("work-order", estate.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Progress.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}""");
        using JsonDocument brief = JsonDocument.Parse("""{"outcome":"Widget","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["widget repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests"],"recovery":"Record a result."}""");
        string assignment = harness.Socket.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, brief.RootElement)).Written!.Id;
        string attempt = harness.Socket.Attempt(new AttemptRequest(assignment, estate.BuilderId, "widget repository", "Build it.")).Written!.Id;
        RuntimeConnection paired = harness.Pair(estate.BuilderId);
        using VericairnClient client = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!;
        int records = estate.Load().Records.Count;
        HandoffPackage basis = client.Handoff().Package!;

        WorkProgressResponse active = client.WorkProgress(new WorkProgressRequest(attempt, estate.BuilderId, "build", "Compiling the widget."));
        Assert.True(active.Ok, active.Problem?.Message);
        LocalNetworkWrite live = Assert.Single(LocalNetworkLedger.Read(directory));
        Assert.Equal("written", live.State);
        Assert.Null(live.Record);
        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal(basis.Hash, client.Handoff().Package!.Hash);

        WorkProgressResponse response = client.WorkProgress(new WorkProgressRequest(refused ? estate.Estate.Id : attempt, estate.BuilderId, "build", "The widget compiles.", Completed: true));
        Assert.Equal(!refused, response.Ok);
        IReadOnlyList<LocalNetworkWrite> writes = LocalNetworkLedger.Read(directory);
        Assert.Equal(2, writes.Count);
        LocalNetworkWrite outcome = writes[1];
        Assert.Equal("POST /v1/work/progress", outcome.Operation);
        Assert.Equal(paired.Device, outcome.Device);
        Assert.Equal(estate.BuilderId, outcome.Principal);
        Assert.Equal(refused ? "failed" : "written", outcome.State);
        Assert.Equal(response.Written?.Id, outcome.Record);
        Assert.Equal(response.Written?.Hash, outcome.Hash);
        Assert.Equal(response.Written?.Path, outcome.Path);
        Assert.Equal(records + (refused ? 0 : 1), estate.Load().Records.Count);

        // A POST carrying a retained package remains a read, and live claims never enter that package.
        ResumeResponse comparison = client.ResumeHandoff(basis);
        Assert.Null(comparison.Problem);
        Assert.Equal(!refused, comparison.Json!.Value.GetProperty("changed").GetBoolean());
        if (!refused)
        {
            Assert.Equal(response.Written!.Id, Assert.Single(comparison.Json.Value.GetProperty("added").EnumerateArray()).GetProperty("id").GetString());
        }

        Assert.NotNull(client.Handoff().Package);
        Assert.Equal(writes, LocalNetworkLedger.Read(directory));
    }

    [Fact]
    public async Task TheReportThroughTheDaemonIsTheEmbeddedRuntimesByteForByte()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = estate.Add("work-order", estate.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Progress.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}""");
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"W-2","kind":"ordinary","title":"Ship the widget","intent":"Progress.","scope":["widget"],"exitQuestion":"Is it shipped?","inputs":[],"deliverables":["widget"],"after":["{{first.Id}}"]}""");
        estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Blue?","recommendation":"Yes.","consequence":"It stays grey.","state":"open"}""");
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);

        ProgressResponse remote = client.Progress();
        ProgressResponse local = host.Service.Progress();
        Assert.Equal(local.Text, remote.Text);
        Assert.Equal(CanonicalJson.SerializePretty(local.Json), CanonicalJson.SerializePretty(remote.Json));
        Assert.False(remote.HasErrors);
        Assert.Contains("    W-2  ordinary   Ship the widget [work-order ", remote.Text, StringComparison.Ordinal);
        Assert.Equal("open", remote.Json.GetProperty("operator").GetProperty("asks")[0].GetProperty("state").GetString());

        // The contract describes the operation; it takes no body and changes nothing.
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty("/v1/progress").GetProperty("get");
        Assert.Equal("getProgress", operation.GetProperty("operationId").GetString());
        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.Equal(local.Text, client.Progress().Text);

        using JsonDocument brief = JsonDocument.Parse("""{"outcome":"Widget","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["widget repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests"],"recovery":"Record a result."}""");
        string assignment = client.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, brief.RootElement)).Written!.Id;
        string attempt = client.Attempt(new AttemptRequest(assignment, estate.BuilderId, "widget repository", "Build it.")).Written!.Id;
        WorkProgressResponse claim = client.WorkProgress(new WorkProgressRequest(attempt, estate.BuilderId, "build", "Compiling the widget."));
        Assert.True(claim.Ok, claim.Problem?.Message);
        Assert.Null(claim.Written);
        Assert.Contains("Compiling the widget.", client.Progress().Text, StringComparison.Ordinal);
        Assert.Equal("build", client.Progress().Json.GetProperty("agentClaims")[0].GetProperty("phase").GetString());
        Assert.False(client.WorkProgress(new WorkProgressRequest(attempt, estate.OperatorId, "build", "Not mine.")).Ok);
    }
}
