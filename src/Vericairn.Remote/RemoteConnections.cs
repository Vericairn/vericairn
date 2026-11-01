// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Remote;

/// <summary>What Remote needs of the connections this phone keeps: the SDK's, or a stand-in in tests.</summary>
public interface IRemoteConnections
{
    /// <summary>Every connection kept on this phone, oldest first.</summary>
    IReadOnlyList<RuntimeConnection> List();

    /// <summary>Pairs with the runtime at an address using the code its administrator issued, and keeps the connection.</summary>
    Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken);

    /// <summary>Checks a connection now: connecting, then current, partial, denied, cached or unavailable, as the SDK observes it.</summary>
    Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken);

    /// <summary>A client of the connection's runtime over the local network, signed by this phone's device key.</summary>
    IEstateApi Connect(RuntimeConnection connection);
}

/// <summary>
/// The phone's connections, kept by the SDK under the app's own storage, with the device keys in the store the platform head
/// supplies. Pairing, the runtime identity check, request signing and connection states are the SDK's, never re-implemented here.
/// </summary>
public sealed class LocalNetworkConnections : IRemoteConnections
{
    private readonly RuntimeConnections store;
    private readonly IDeviceKeyStore keys;

    /// <summary>Keeps connections under <paramref name="home"/> with device keys in <paramref name="keys"/>.</summary>
    public LocalNetworkConnections(string home, IDeviceKeyStore keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        store = new RuntimeConnections(home, keys);
    }

    /// <inheritdoc />
    public IReadOnlyList<RuntimeConnection> List() => store.List();

    /// <inheritdoc />
    public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken) =>
        store.AddAsync(address, code, new PairingOptions(deviceName) { KeyStore = keys }, cancellationToken);

    /// <inheritdoc />
    public Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken) =>
        store.ProbeAsync(connection, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public IEstateApi Connect(RuntimeConnection connection) => store.Connect(connection);
}
