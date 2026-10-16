// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class ContextApiTests
{
    public ContextApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task ContextAndOriginalIdentityAreTheSameThroughTheAuthenticatedSdk()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        harness.Estate.Add("work-order", harness.Estate.OperatorId, """{"key":"C-1","kind":"ordinary","title":"Context","intent":"Continue.","scope":["work"],"exitQuestion":"Complete?","inputs":[],"deliverables":["result"]}""");
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient client = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        int writes = LocalNetworkLedger.Read(directory).Count;
        ContextResponse local = harness.Host.Service.Context("C-1");
        ContextResponse served = client.Context("C-1");
        Assert.Null(served.Problem);
        Assert.Equal(local.Package!.Hash, served.Package!.Hash);
        string manifest = served.Package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        foreach (JsonElement source in served.Package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray())
        {
            string key = source.GetProperty("key").GetString()!;
            ContextReadResponse expected = harness.Host.Service.ContextRead(manifest, key);
            ContextReadResponse fetched = client.ContextRead(manifest, key);
            Assert.Null(fetched.Problem);
            Assert.Equal(expected.Content, fetched.Content);
            Assert.True(JsonElement.DeepEquals(expected.Identity!.Value, fetched.Identity!.Value));
        }

        Assert.Equal(writes, LocalNetworkLedger.Read(directory).Count);
        Assert.True(harness.Host.Service.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "Withdraw read permission.")).Ok);
        Assert.Throws<HttpRequestException>(() => client.ContextRead(manifest, "s000000"));
    }
}
