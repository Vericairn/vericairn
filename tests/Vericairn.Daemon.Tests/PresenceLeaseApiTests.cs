// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// ID-11 through the daemon (design/events-and-presence.md § 8): POST /v1/presence registers a session, its heartbeat renews it and
/// DELETE releases it, each only for the principal and device that registered it; a lease left silent lapses, and a heartbeat restores
/// it. The lapse is judged by the daemon's clock, which the test moves by hand.
/// </summary>
[Collection("daemon")]
public sealed class PresenceLeaseApiTests
{
    public PresenceLeaseApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task ALeaseIsRegisteredRenewedAndReleasedByItsHolderOnlyAndLapsesWhenSilent()
    {
        using var estate = new TestEstate();
        var time = new ManualTime(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), running: true);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, options: new DaemonOptions { Time = time, PresenceTick = TimeSpan.FromHours(1) },
            cancellationToken: TestContext.Current.CancellationToken);
        using var socket = new VericairnClient(host.Endpoint);
        string builder = estate.BuilderId;

        // Over the local socket the builder's session registers, is listed without its lease identity, renews and is released.
        PresenceLeaseResponse registered = socket.PresenceRegister(new PresenceRegisterRequest(builder, "a socket session", LeaseSeconds: 30));
        Assert.True(registered.Ok, registered.Message);
        PresenceLease lease = registered.Lease!;
        Assert.Equal(10, lease.HeartbeatSeconds);
        PresenceEntry listed = Assert.Single(socket.Presence(builder).Sessions);
        Assert.Equal((lease.Session, "live"), (listed.Session, listed.State));
        Assert.True(socket.PresenceHeartbeat(lease.Lease).Ok);
        Assert.Equal("refused", socket.PresenceRegister(new PresenceRegisterRequest(estate.OperatorId)).Problem!.Code);

        // A device of the builder registers for its own principal only, and each lease answers only the requester that registered it.
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(builder, "Builder's device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        WriteOutcome admitted = socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, estate.OperatorId, "Presence test."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        using VericairnClient loopback = VericairnClient.Loopback(host.Endpoint, DeviceRequestSigner.Find(device.Written.Id));
        PresenceLease signed = loopback.PresenceRegister(new PresenceRegisterRequest(Label: "a device session")).Lease!;
        Assert.Equal(builder, signed.Principal);
        HttpRequestException other = Assert.Throws<HttpRequestException>(() => loopback.PresenceRegister(new PresenceRegisterRequest(estate.OperatorId)));
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        Assert.Equal("not-found", socket.PresenceHeartbeat(signed.Lease).Problem!.Code);
        Assert.Equal("not-found", loopback.PresenceHeartbeat(lease.Lease).Problem!.Code);
        Assert.Equal("not-found", (await loopback.PresenceReleaseAsync(lease.Lease, TestContext.Current.CancellationToken)).Problem!.Code);
        Assert.True((await loopback.PresenceReleaseAsync(signed.Lease, TestContext.Current.CancellationToken)).Ok);

        // Silence past the lease: the daemon's sweep finds it lapsed, and a heartbeat restores it.
        time.Advance(TimeSpan.FromSeconds(31));
        host.Service.SweepPresence();
        Assert.Equal("lapsed", Assert.Single(socket.Presence().Sessions).State);
        PresenceLeaseResponse restored = socket.PresenceHeartbeat(lease.Lease);
        Assert.True(restored.Ok, restored.Message);
        Assert.Equal("live", Assert.Single(socket.Presence().Sessions).State);
        Assert.True(socket.PresenceRelease(lease.Lease).Ok);
        Assert.Empty(socket.Presence().Sessions);
    }
}
