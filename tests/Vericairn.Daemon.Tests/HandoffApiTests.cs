// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class HandoffApiTests
{
    public HandoffApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task HandoffIsTheSameThroughTheSdkAndRefusesOverflowWithoutTruncation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        HandoffResponse local = host.Service.Handoff();
        HandoffResponse remote = client.Handoff();
        Assert.NotNull(remote.Package);
        Assert.Equal(local.Text, remote.Text);
        Assert.Equal(local.Package!.Hash, remote.Package.Hash);
        Assert.Equal(CanonicalJson.SerializePretty(local.Package.Payload), CanonicalJson.SerializePretty(remote.Package.Payload));
        Assert.Equal("handoff-over-budget", client.Handoff(1).Problem!.Code);
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty("/v1/handoff").GetProperty("get");
        Assert.Equal("getHandoff", operation.GetProperty("operationId").GetString());
        Assert.False(operation.TryGetProperty("requestBody", out _));
    }
}
