// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Remote's first use against a real runtime on the local-network profile: the phone pairs with the code an administrator issued,
/// keeps its own key and connection, and shows the estate current; when the runtime stops, the same content is shown as cached and
/// nothing is revoked. The pairing, identity check and signing are the SDK's, so this is the path a phone takes.
/// </summary>
public class RemotePairingJourneyTests
{
    [Fact]
    public async Task APhonePairsOverTheLocalNetworkAndShowsTheEstateCurrentThenCached()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var connections = new LocalNetworkConnections(harness.DeviceHome, harness.DeviceKeys);
        var remote = new RemoteViewModel(connections);
        Assert.True(remote.Pairing);
        Assert.False(remote.PairCommand.CanExecute(null));

        remote.Address = harness.Address;
        remote.Code = harness.Issue(harness.Estate.BuilderId).Code;
        remote.DeviceName = "Phone";
        Assert.True(remote.PairCommand.CanExecute(null));
        await remote.PairCommand.ExecuteAsync();

        Assert.False(remote.Pairing, remote.PairingMessage);
        RemoteEstate estate = Assert.Single(remote.Estates);
        Assert.Same(estate, remote.Selected);
        Assert.Equal("Test Estate", estate.Name);
        Assert.Equal(ConnectionStates.Current, remote.ConnectionState);
        Assert.StartsWith("current as of ", remote.Freshness, StringComparison.Ordinal);
        Assert.Equal(RuntimeIdentityScheme.Fingerprint(harness.Host.Identity!.PublicKey), remote.Node!.Runtime);
        Assert.Equal(estate.Connection.Device, remote.Node.Device);
        Assert.True(harness.DeviceKeys.Has(estate.Connection.Device));

        // The phone keeps its connection: the next start shows the home, not first use.
        var reopened = new RemoteViewModel(connections);
        Assert.False(reopened.Pairing);
        Assert.Equal(estate.Connection.Id, reopened.Selected!.Connection.Id);

        // A runtime that stops leaves the last observation shown as cached, with its time; the device stays admitted.
        await harness.Host.StopAsync();
        await remote.RefreshCommand.ExecuteAsync();
        Assert.Equal(ConnectionStates.Cached, remote.ConnectionState);
        Assert.StartsWith("cached from ", remote.Freshness, StringComparison.Ordinal);
        Assert.Equal("cached; freshness shown", remote.ConnectionLabel);
        using var embedded = Runtime.Service.EstateService.Open(harness.Estate.Root);
        Assert.Equal("admitted", Assert.Single(embedded.Devices(), d => d.Id == estate.Connection.Device).Admission);
    }

    [Fact]
    public async Task AWrongCodeLeavesThePhoneUnpairedAndSaysWhy()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var remote = new RemoteViewModel(new LocalNetworkConnections(harness.DeviceHome, harness.DeviceKeys));
        _ = harness.Issue(harness.Estate.BuilderId);
        remote.Address = harness.Address;
        remote.Code = "AAAA-AAAA-AAAA-AAAA";
        await remote.PairCommand.ExecuteAsync();

        Assert.True(remote.Pairing);
        Assert.Empty(remote.Estates);
        Assert.StartsWith("Not paired (", remote.PairingMessage, StringComparison.Ordinal);
    }
}
