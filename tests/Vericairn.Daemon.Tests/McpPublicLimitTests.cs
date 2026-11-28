// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;

namespace Vericairn.Daemon.Tests;

public sealed class McpPublicLimitTests
{
    [Fact]
    public void ConnectionsAreBoundedPerPeerAndInTotal()
    {
        var connections = new McpPublicConnections();
        for (int peer = 0; peer < 3; ++peer)
        {
            for (int count = 0; count < 4; ++count)
                Assert.True(connections.TryEnter(Peer(peer)));
            Assert.False(connections.TryEnter(Peer(peer)));
        }

        Assert.False(connections.TryEnter(Peer(3)));
        connections.Leave(Peer(0));
        Assert.True(connections.TryEnter(Peer(3)));
        Assert.False(connections.TryEnter(Peer(0)));
    }

    [Fact]
    public void MappedAndRotatingIpv6AddressesShareOnePeerBudget()
    {
        Assert.Equal(McpPublicPeer.Of(IPAddress.Parse("192.0.2.5")), McpPublicPeer.Of(IPAddress.Parse("::ffff:192.0.2.5")));
        Assert.Equal(McpPublicPeer.Of(IPAddress.Parse("2001:db8:7:1::1")), McpPublicPeer.Of(IPAddress.Parse("2001:db8:7:1:aaaa::2")));
        Assert.NotEqual(McpPublicPeer.Of(IPAddress.Parse("2001:db8:7:1::1")), McpPublicPeer.Of(IPAddress.Parse("2001:db8:7:2::1")));
    }

    [Fact]
    public void PendingRegistrationsAreBoundedPerPeerAndInTotalAndExpire()
    {
        using var scratch = new Scratch();
        var clock = new LimitClock();
        var registrations = new McpClientRegistry(scratch.Path, clock);
        for (int index = 0; index < 8; ++index)
            Assert.True(registrations.Add(Client(index), Peer(0)));
        Assert.False(registrations.Add(Client(8), Peer(0)));

        for (int index = 8; index < 128; ++index)
            Assert.True(registrations.Add(Client(index), Peer(index)));
        Assert.False(registrations.Add(Client(128), Peer(128)));

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(registrations.Find(Client(0).Id));
        Assert.True(registrations.Add(Client(128), Peer(0)));
    }

    [Fact]
    public void ApprovedRegistrationFreesItsSlotOnExpiryAcrossRestart()
    {
        using var scratch = new Scratch();
        var clock = new LimitClock();
        string path = Path.Combine(scratch.Path, "mcp-registrations.json");
        // The old metadata-only file is still accepted. Its dated entries also eventually leave room for new clients.
        var legacy = Enumerable.Range(0, 255).Select(Client).ToDictionary(c => c.Id, StringComparer.Ordinal);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(legacy, ProtocolJson.Options));
        File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime);

        var registrations = new McpClientRegistry(scratch.Path, clock);
        Assert.Equal(Client(1).Name, registrations.Find(Client(1).Id)?.Name);

        McpClientMetadata newClient = Client(255);
        Assert.True(registrations.Add(newClient, Peer(0)));
        Assert.False(registrations.Add(Client(256), Peer(1)));
        registrations.Use(newClient.Id, clock.GetUtcNow().AddDays(1));

        registrations = new McpClientRegistry(scratch.Path, clock);
        Assert.Equal(newClient.Name, registrations.Find(newClient.Id)?.Name);
        Assert.False(registrations.Add(Client(256), Peer(1)));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(registrations.Find(newClient.Id));
        Assert.True(registrations.Add(Client(256), Peer(1)));

        clock.Advance(TimeSpan.FromDays(89));
        Assert.Null(registrations.Find(Client(1).Id));
    }

    [Fact]
    public void PendingBrowserFlowsAreBoundedPerPeerAndInTotalAndExpire()
    {
        var clock = new LimitClock();
        var browser = new McpBrowserAuthorization(clock, "https://runtime.example");
        McpClientMetadata client = Client(1);
        var request = new McpAuthorizationRequest(client, client.Redirects[0], McpTokenStore.RandomValue(32), "read", null);
        string? Begin(int peer)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = Peer(peer);
            return browser.Begin(http, request);
        }

        for (int index = 0; index < 8; ++index)
            Assert.NotNull(Begin(0));
        Assert.Null(Begin(0));

        for (int index = 8; index < 256; ++index)
            Assert.NotNull(Begin(index));
        Assert.Null(Begin(256));

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(Begin(0));
    }

    [Fact]
    public void TheRequestBudgetIsBoundedPerPeerAndInTotalAndResets()
    {
        var clock = new LimitClock();
        var throttle = new McpPublicThrottle(clock, totalLimit: 5, peerLimit: 2);
        Assert.True(throttle.Admit(Peer(0)));
        Assert.True(throttle.Admit(Peer(0)));
        Assert.False(throttle.Admit(Peer(0)));
        Assert.True(throttle.Admit(Peer(1)));
        Assert.True(throttle.Admit(Peer(1)));
        Assert.True(throttle.Admit(Peer(2)));
        Assert.False(throttle.Admit(Peer(3)));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(throttle.Admit(Peer(0)));
        Assert.True(throttle.Admit(Peer(3)));
    }

    private static IPAddress Peer(int peer) => new([192, 0, (byte)(peer / 256), (byte)(peer % 256)]);

    private static McpClientMetadata Client(int index) => new("fixture-" + index, "Fixture " + index, ["https://client.example/callback"], "web");

    private sealed class LimitClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch.AddYears(50);

        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan by) => now += by;
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("vc-mcp-limit-test-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
