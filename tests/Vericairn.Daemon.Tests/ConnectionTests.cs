// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The client's connections (design/identity-and-connectivity.md §§ 6 and 7): added by pairing, listed with their state in the
/// state language (connecting, current, cached, partial, denied, unavailable), removed on demand; disconnection is never revocation.
/// </summary>
[Collection("daemon")]
public class ConnectionTests
{
    public ConnectionTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public void TheStatesAreTheStateLanguagesAndFollowWhatACheckFound()
    {
        Assert.Equal(["connecting", "current", "cached", "partial", "denied", "unavailable"], ConnectionStates.All);
        Assert.Equal(ConnectionStates.Current, ConnectionStates.Of(answered: true, denied: false, complete: true, cached: false));
        Assert.Equal(ConnectionStates.Partial, ConnectionStates.Of(answered: true, denied: false, complete: false, cached: true));
        Assert.Equal(ConnectionStates.Denied, ConnectionStates.Of(answered: true, denied: true, complete: false, cached: true));
        Assert.Equal(ConnectionStates.Denied, ConnectionStates.Of(answered: false, denied: true, complete: false, cached: false));
        Assert.Equal(ConnectionStates.Cached, ConnectionStates.Of(answered: false, denied: false, complete: false, cached: true));
        Assert.Equal(ConnectionStates.Unavailable, ConnectionStates.Of(answered: false, denied: false, complete: false, cached: false));
    }

    [Fact]
    public async Task AConnectionIsConnectingUntilSomethingAnswersAndUnavailableWhenNothingDoes()
    {
        // A listener on 127.0.0.1 that accepts and never answers.
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        string home = Path.Combine(Path.GetTempPath(), "vericairn-connections-" + Ulid.NewUlid().Value);
        try
        {
            var keys = new Records.Signing.FileKeyStore(home);
            string device = Ulid.NewUlid().Value;
            byte[] spki = keys.Create(device);
            var connections = new RuntimeConnections(home, keys);
            var connection = new RuntimeConnection(Ulid.NewUlid().Value, $"127.0.0.1:{((IPEndPoint)silent.LocalEndpoint).Port}", Ulid.NewUlid().Value, null, Records.Signing.RecordSigning.EncodePublicKey(spki), device, Records.Signing.RecordSigning.EncodePublicKey(spki), Ulid.NewUlid().Value, null, ["local-network"], "2026-10-15T00:00:00Z");
            var heard = new List<string>();
            Task<TcpClient> accepted = silent.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
            ConnectionStatus status = await Task.Run(() => connections.Probe(connection, TimeSpan.FromSeconds(2), heard.Add), TestContext.Current.CancellationToken);
            Assert.Equal([ConnectionStates.Connecting, ConnectionStates.Unavailable], heard);
            Assert.Equal(ConnectionStates.Unavailable, status.State);
            Assert.Contains("No answer", status.Message, StringComparison.Ordinal);
            (await accepted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Dispose();
        }
        finally
        {
            silent.Stop();
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task AStoppedRuntimeLeavesTheConnectionCachedAndRemovingItRevokesNothing()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        ConnectionStatus current = harness.Connections.Probe(connection);
        Assert.Equal(ConnectionStates.Current, current.State);
        RuntimeConnection kept = harness.Connections.Find(connection.Id)!;
        Assert.Equal(ConnectionStates.Current, kept.Observed!.State);
        Assert.Equal(kept.Observed, kept.LastCurrent);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(harness.DeviceHome, "connections", connection.Id + ".json")));
        }

        // A disconnected client, and a runtime that stops and starts again, revoke nothing: the device is still admitted.
        await harness.Host.StopAsync();
        ConnectionStatus stopped = harness.Connections.Probe(kept, TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionStates.Cached, stopped.State);
        Assert.Contains("Last current at " + kept.LastCurrent!.At, stopped.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionStates.Cached, harness.Connections.Find(connection.Id)!.Observed!.State);
        using (var embedded = Runtime.Service.EstateService.Open(harness.Estate.Root))
        {
            Assert.Equal("admitted", Assert.Single(embedded.Devices(), d => d.Id == connection.Device).Admission);
        }

        // Removing the connection forgets the runtime on this machine; the device stays admitted and its key stays in custody here.
        Assert.Equal(connection.Id, harness.Connections.Remove(connection.Id)!.Id);
        Assert.Null(harness.Connections.Find(connection.Id));
        Assert.Empty(harness.Connections.List());
        Assert.Null(harness.Connections.Remove(connection.Id));
        Assert.True(harness.DeviceKeys.Has(connection.Device));
        using (var embedded = Runtime.Service.EstateService.Open(harness.Estate.Root))
        {
            Assert.Equal("admitted", Assert.Single(embedded.Devices(), d => d.Id == connection.Device).Admission);
        }
    }

    [Fact]
    public void AnAddressIsAnExplicitHostAndPort()
    {
        Assert.True(RuntimeConnection.TryParseAddress("192.168.1.20:7443", out string host, out int port));
        Assert.Equal(("192.168.1.20", 7443), (host, port));
        Assert.True(RuntimeConnection.TryParseAddress("[fe80::1]:7443", out host, out port));
        Assert.Equal(("fe80::1", 7443), (host, port));
        Assert.True(RuntimeConnection.TryParseAddress("runtime.example:7443", out host, out _));
        Assert.Equal("runtime.example", host);
        Assert.False(RuntimeConnection.TryParseAddress("192.168.1.20", out _, out _));
        Assert.False(RuntimeConnection.TryParseAddress("fe80::1:7443", out _, out _));
        Assert.False(RuntimeConnection.TryParseAddress("192.168.1.20:70000", out _, out _));
        Assert.False(RuntimeConnection.TryParseAddress(":7443", out _, out _));
    }
}
