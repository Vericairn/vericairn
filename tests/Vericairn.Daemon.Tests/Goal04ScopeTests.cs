// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>GOAL-04 operations retain the principal bound by ID-02 and the worker stays on this machine.</summary>
[Collection("daemon")]
public class Goal04ScopeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EachNewRequestActsOnlyForItsBoundPrincipal(bool step, bool lan)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var scope = new RequestScope(harness.Host.Service, new LocalNetworkLedger(harness.DeviceHome));
        foreach (string? by in new[] { harness.Estate.BuilderId, harness.Estate.OperatorId, "", null })
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = step ? "/v1/goals/{id}/step" : "/v1/work/takeover";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", harness.Estate.BuilderId, lan);
            object request = step ? new GoalStepRequest(by!) : new TakeoverRequest("assignment", by!, harness.Estate.OperatorId);
            bool reached = false;
            object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
            {
                reached = true;
                return ValueTask.FromResult<object?>(null);
            });
            Assert.Equal(by == harness.Estate.BuilderId, reached);
            if (!reached)
            {
                Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            }
        }
    }

    [Fact]
    public async Task GoalStepIsMachineLocalBeforeAnyWorkerIsReached()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection paired = harness.Pair(harness.Estate.OperatorId);
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        (HttpStatusCode status, string body) = await lan.SendSignedAsync(signer, HttpMethod.Post, "/v1/goals/" + harness.Estate.Estate.Id + "/step", JsonSerializer.Serialize(new GoalStepRequest(harness.Estate.OperatorId), ProtocolJson.Options));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("runtime's own machine", JsonSerializer.Deserialize<Problem>(body, ProtocolJson.Options)!.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Socket.Records("assignment"));
        Assert.Empty(harness.Socket.Records("operation"));
    }

    [Fact]
    public async Task TakeoverRequiresAPersonAndCannotBorrowOneFromItsBody()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        using JsonDocument order = JsonDocument.Parse("""{"key":"W-SCOPE","kind":"ordinary","title":"Scope","intent":"Test scope.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        Assert.True(harness.Socket.NewRecord(new NewRecordRequest("work-order", harness.Estate.OperatorId, order.RootElement)).Ok);
        using JsonDocument brief = JsonDocument.Parse("""{"outcome":"A result.","sources":[],"permittedChanges":["x"],"prohibitedEffects":["no publication"],"workspace":["a work branch"],"limits":[],"expectedOutputs":["y"],"reviewCriteria":["scope holds"],"recovery":"Record a result."}""");
        WriteOutcome assigned = harness.Socket.Assign(new AssignRequest("W-SCOPE", harness.Estate.BuilderId, harness.Estate.OperatorId, brief.RootElement));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient agent = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        HttpRequestException borrowed = Assert.Throws<HttpRequestException>(() => agent.WorkTakeover(new TakeoverRequest(assigned.Written!.Id, harness.Estate.OperatorId)));
        Assert.Equal(HttpStatusCode.Unauthorized, borrowed.StatusCode);
        TakeoverResponse refused = agent.WorkTakeover(new TakeoverRequest(assigned.Written!.Id, harness.Estate.BuilderId, harness.Estate.OperatorId));
        Assert.False(refused.Ok);
        Assert.Contains("never by an agent", refused.Message, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Records("assignment"));

        RuntimeConnection human = harness.Pair(harness.Estate.OperatorId);
        using VericairnClient person = VericairnClient.LocalNetwork(human, harness.Signer(human));
        TakeoverResponse taken = person.WorkTakeover(new TakeoverRequest(assigned.Written.Id, harness.Estate.OperatorId));
        Assert.True(taken.Ok, taken.Message);
        Assert.Equal(harness.Estate.OperatorId, harness.Socket.Record(taken.Assignment!.Id)!.Envelope.GetProperty("by").GetString());
        string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        LocalNetworkWrite[] writes = [.. LocalNetworkLedger.Read(live)];
        Assert.Contains(writes, w => w.Device == paired.Device && w.State == "failed");
        Assert.Contains(writes, w => w.Device == human.Device && w.State == "written" && w.Record == taken.Assignment.Id && w.Hash == taken.Assignment.Hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsksAndRoutingAreReadViewsWithNoActingPrincipal(bool routing)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient lan = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        if (routing)
        {
            Assert.Null(lan.GoalNext(harness.Estate.Estate.Id));
        }
        else
        {
            Assert.Equal(harness.Socket.Asks(harness.Estate.OperatorId).Json.GetRawText(), lan.Asks(harness.Estate.OperatorId).Json.GetRawText());
        }
        Assert.Empty(harness.Socket.Records("ask"));
        Assert.Empty(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!));
    }
}
