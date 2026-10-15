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
    }
}
