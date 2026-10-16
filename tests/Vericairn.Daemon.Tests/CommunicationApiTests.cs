// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class CommunicationApiTests
{
    public CommunicationApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task SdkReadsAndWritesTheSameAttributableProfileAsTheRuntime()
    {
        using var estate = new TestEstate(2);
        estate.Add("work-order", estate.OperatorId, """{"key":"COMM-1","kind":"ordinary","title":"A brief","intent":"Complete intent.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        WriteOutcome saved = client.CommunicationSet(new(estate.OperatorId, "person", new(Status: "comprehensive"), estate.OperatorId));
        Assert.True(saved.Ok, saved.Problem?.Message);
        CommunicationProfile profile = client.Communication(estate.OperatorId);
        Assert.Equal(host.Service.Communication(estate.OperatorId).RevisionHash, profile.RevisionHash);
        Assert.Equal(saved.Written!.Hash, profile.Revisions.Single().Hash);
        Assert.Equal("short", client.Communication(estate.OperatorId, view: new(Status: "short")).Fields["status"].Value);
        Assert.Equal("conflict", client.CommunicationSet(new(estate.OperatorId, "person", new(), estate.OperatorId)).Problem!.Code);
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement paths = contract.RootElement.GetProperty("paths");
        Assert.Equal("getCommunication", paths.GetProperty("/v1/communication").GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(paths.GetProperty("/v1/communication/delegations").TryGetProperty("post", out _));
        BriefResponse brief = client.BriefFor("COMM-1", estate.OperatorId, view: new(Handoff: "short"))!;
        Assert.Contains("handoff (short)", brief.Markdown, StringComparison.Ordinal);
        Assert.Equal(client.Brief("COMM-1")!.Json.GetRawText(), brief.Json.GetRawText());
    }

    [Fact]
    public async Task SignedRequestsCannotImpersonateTheRecipientOrDelegateTheirConsent()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient agent = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        string person = harness.Estate.OperatorId;
        Assert.Throws<HttpRequestException>(() => agent.CommunicationSet(new(person, "person", new(), person)));
        Assert.False(agent.CommunicationSet(new(harness.Estate.BuilderId, "person", new(), person)).Ok);
        Assert.Throws<HttpRequestException>(() => agent.CommunicationDelegate(new(person, person, harness.Estate.BuilderId, "person")));
        Assert.Equal("shipped", agent.Communication(person).Fields["status"].Source);
        Assert.Empty(harness.Estate.Load().OfType("communication-setting"));
        WriteOutcome own = harness.Socket.CommunicationSet(new(person, "person", new(Status: "standard"), person));
        Assert.True(own.Ok, own.Problem?.Message);
        RecordResponse record = harness.Socket.Record(own.Written!.Id)!;
        Assert.Equal(person, record.Envelope.GetProperty("by").GetString());
        Assert.Equal(harness.OperatorDevice, record.Envelope.GetProperty("signatures")[0].GetProperty("device").GetString());
    }
}
