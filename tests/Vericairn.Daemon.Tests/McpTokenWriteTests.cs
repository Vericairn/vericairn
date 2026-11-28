// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon.Tests;

public sealed class McpTokenWriteTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task APreparedTokenWriteRechecksAdmissionAndExpiryBeforeStoringItsRecord(string change)
    {
        DaemonTestHome.Ensure();
        using var estate = new TestEstate(3);
        using EstateService writer = EstateService.Open(estate.Root);
        using EstateService administrator = EstateService.Open(estate.Root);
        string builder = estate.BuilderId;
        string op = estate.OperatorId;

        WriteOutcome first = administrator.DeviceCreate(new DeviceCreateRequest(op, "Fixture operator", "file"));
        Assert.True(first.Ok, first.Problem?.Message);
        WriteOutcome admitted = administrator.DeviceAdmit(new AdmitRequest(first.Written!.Id, op, "The first authority."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        PairingAuthority issuer = Assert.IsType<PairingAuthority>(administrator.McpTokenIssuer(builder, op).Authority);
        var custody = new FileKeyStore(estate.Root);
        McpTokenDevice device = Assert.IsType<McpTokenDevice>(administrator.CreateMcpTokenDevice(builder, issuer, custody).Device);

        estate.Add("work-order", op, NetworkMcp.WorkOrder);
        using JsonDocument brief = JsonDocument.Parse(NetworkMcp.Brief);
        WriteOutcome assigned = administrator.Assign(new AssignRequest("W-1", builder, op, brief.RootElement));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        var request = new AttemptRequest(assigned.Written!.Id, builder, "fixture workspace", "Record this attempt.");

        var clock = new AccessClock();
        var store = new McpTokenStore(estate.Root, clock);
        const string resource = "https://runtime.example/mcp";
        var grant = new McpTokenGrant(device.Device, builder, device.PublicKey, device.Admission, op,
            "https://client.example/metadata", resource, "contribute", clock.GetUtcNow().AddDays(1));
        McpTokenResponse issued = Assert.IsType<McpTokenResponse>(store.Create(grant));
        McpTokenAccess access = Assert.IsType<McpTokenAccess>(store.Authenticate(issued.AccessToken, resource));
        var token = new McpTokenRequest(access.Grant.Scope, access.ExpiresAt, () => store.Current(access),
            hash => Base64Url.EncodeToString(custody.Sign(device.Device, RecordSigning.Digest(hash))));
        var authenticated = new AuthenticatedDevice(device.Device, builder, device.PublicKey, LocalNetwork: false) { Token = token };
        var ledger = new LocalNetworkLedger(estate.Root);

        DeviceSigning preparation = DeviceSigning.Prepare();
        await InvokeAsync(preparation);
        RecordPrepared prepared = Assert.IsType<RecordPrepared>(preparation.Prepared);
        Assert.True(token.Current());
        DeviceSigning signing = DeviceSigning.Sign(prepared.Plan, token.Sign(prepared.Hash));

        object? response = await InvokeAsync(signing, () =>
        {
            if (change == "revoked")
            {
                WriteOutcome revoked = administrator.DeviceRevoke(new RevokeRequest(device.Device, op, "Revoked before the prepared record was stored."));
                Assert.True(revoked.Ok, revoked.Problem?.Message);
            }
            else if (change == "expired")
            {
                clock.Now = access.ExpiresAt;
            }
        });

        bool current = change == "current";
        Assert.Equal(current ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Equal(current, estate.Load().ById.ContainsKey(prepared.Id));
        LocalNetworkWrite entry = Assert.Single(LocalNetworkLedger.Read(estate.Root));
        Assert.Equal((device.Device, "POST /v1/work/attempt", current ? "written" : "failed", current ? prepared.Id : null),
            (entry.Device, entry.Operation, entry.State, entry.Record));

        async ValueTask<object?> InvokeAsync(DeviceSigning rule, Action? beforeWrite = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/v1/work/attempt";
            context.Items[RequestAuthenticator.DeviceItem] = authenticated;
            context.Items[RequestScope.SigningItem] = rule;
            var scope = new RequestScope(writer, ledger, beforeWrite: beforeWrite);
            return await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request),
                _ => ValueTask.FromResult<object?>(Results.Ok(writer.Attempt(request))));
        }
    }

    private sealed class AccessClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
