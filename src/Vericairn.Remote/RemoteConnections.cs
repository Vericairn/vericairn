// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vericairn.Remote;

/// <summary>What Remote needs of the connections this phone keeps: the SDK's, or a stand-in in tests.</summary>
public interface IRemoteConnections
{
    /// <summary>Every connection kept on this phone, oldest first.</summary>
    IReadOnlyList<RuntimeConnection> List();

    /// <summary>
    /// Pairs with the runtime at an address using the code its administrator issued, and keeps the connection. Where the phone has a
    /// presence store, the same exchange also registers its presence key, which asks for the owner's presence when it signs.
    /// </summary>
    Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken);

    /// <summary>Checks a connection now: connecting, then current, partial, denied, cached or unavailable, as the SDK observes it.</summary>
    Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken);

    /// <summary>A client of the connection's runtime over the local network, signed by this phone's device key.</summary>
    IEstateApi Connect(RuntimeConnection connection);

    /// <summary>Restores this connection's last observation from app storage; it always starts cached.</summary>
    RemoteObservation? LoadObservation(RuntimeConnection connection) => null;

    /// <summary>Keeps an observation in app storage without queuing requests. Older implementations retain no persistent cache.</summary>
    void SaveObservation(RuntimeConnection connection, RemoteObservation observation) { }

    /// <summary>
    /// Prepares a record with this phone's request device, gives the caller its exact unsigned form in <paramref name="review"/>, then
    /// signs only with this phone's presence key, which asks for its owner's presence, and calls <paramref name="beforeSend"/> before the
    /// signature leaves. Either callback returns false to send nothing. A phone without a presence key signs nothing.
    /// </summary>
    Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review, Func<RecordPrepared, bool> beforeSend,
        CancellationToken cancellationToken = default) => Task.FromResult(WriteOutcome.Fail(new("unsupported", "This connection cannot sign records on the phone.", [])));

    /// <summary>Whether this phone holds the connection's presence key, the only key it signs a decision with. Older implementations hold none.</summary>
    bool HasPresenceKey(RuntimeConnection connection) => false;

    /// <summary>
    /// Adds a presence key to an existing connection with a new pairing code the administrator issued for the same principal, and
    /// returns the connection as kept. Older implementations cannot add one.
    /// </summary>
    Task<RuntimeConnection> AddPresenceKeyAsync(RuntimeConnection connection, string code, string deviceName, CancellationToken cancellationToken) =>
        Task.FromException<RuntimeConnection>(new NotSupportedException("This phone cannot add a presence key."));

    /// <summary>Restores drafts and attempted submissions for this exact connection. Unreadable storage must refuse, never appear empty.</summary>
    IReadOnlyList<RemoteDecisionDraft> LoadDecisions(RuntimeConnection connection) => [];

    /// <summary>Durably retains input or reserves a prepared identity before signing. An attempted identity cannot be replaced or cleared.</summary>
    void SaveDecision(RuntimeConnection connection, RemoteDecisionDraft draft) => throw new NotSupportedException("This connection cannot retain decision drafts and submission identities.");
}

/// <summary>
/// The phone's connections, kept by the SDK under the app's own storage, with the device keys in the stores the platform head
/// supplies: the request key, which signs every request without asking, and the presence key, which asks for its owner's presence
/// at every signature and is the only key that signs a decision. Pairing, the runtime identity check, request signing and connection
/// states are the SDK's, never re-implemented here.
/// </summary>
public sealed partial class LocalNetworkConnections : IRemoteConnections
{
    private readonly RuntimeConnections store;
    private readonly IDeviceKeyStore keys;
    private readonly IDeviceKeyStore? presenceKeys;
    private readonly string observations;

    /// <summary>Keeps connections under <paramref name="home"/> with request keys in <paramref name="keys"/> and no presence keys: such a phone signs no decision.</summary>
    public LocalNetworkConnections(string home, IDeviceKeyStore keys)
        : this(home, keys, null)
    {
    }

    /// <summary>
    /// Keeps connections under <paramref name="home"/> with request keys in <paramref name="keys"/> and presence keys in
    /// <paramref name="presenceKeys"/>, whose custody must enforce presence; null where the phone has none, such as the iOS simulator.
    /// </summary>
    public LocalNetworkConnections(string home, IDeviceKeyStore keys, IDeviceKeyStore? presenceKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        if (presenceKeys is not null && DeviceKeyStores.PresenceKinds.IndexOf(presenceKeys.Kind) < 0)
            throw new ArgumentException($"{presenceKeys.Kind} custody cannot enforce its owner's presence, so it cannot hold presence keys.", nameof(presenceKeys));
        this.presenceKeys = presenceKeys;
        store = new RuntimeConnections(home, keys);
        observations = Path.Combine(home, "remote-observations");
    }

    /// <inheritdoc />
    public IReadOnlyList<RuntimeConnection> List() => store.List();

    /// <inheritdoc />
    public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken)
    {
        var options = new PairingOptions(deviceName) { KeyStore = keys, PresenceKeyStore = presenceKeys };
        // A presence prompt blocks the signing thread, so the exchange never runs on the interface's thread.
        return Task.Run(() => store.AddAsync(address, code, presenceKeys is null ? options : options with { Timeout = RuntimeConnections.PresencePairingTimeout }, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public bool HasPresenceKey(RuntimeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.PresenceDevice is string device && presenceKeys?.Has(device) == true;
    }

    /// <inheritdoc />
    public Task<RuntimeConnection> AddPresenceKeyAsync(RuntimeConnection connection, string code, string deviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (presenceKeys is not IDeviceKeyStore presence)
            return Task.FromException<RuntimeConnection>(new PairingException("refused", "This phone's key custody cannot require your presence, so it holds no presence key and signs no decision. Decide on the desktop."));
        return Task.Run(() => store.AddPresenceAsync(connection, code, deviceName + " (presence key)", presence, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken) =>
        store.ProbeAsync(connection, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public IEstateApi Connect(RuntimeConnection connection) => store.Connect(connection);

    /// <inheritdoc />
    public async Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review, Func<RecordPrepared, bool> beforeSend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(beforeSend);
        // The request key never signs a decision: without the presence key nothing is prepared, signed or sent.
        if (!HasPresenceKey(connection))
            return WriteOutcome.Fail(new("presence-required", "This phone holds no presence key for this connection, so it cannot sign a decision. Add one with a new pairing code, or decide on the desktop. Nothing was prepared or sent.", []));
        using VericairnClient client = store.Connect(connection);
        return await DeviceSignedRecords.WriteWithPresenceAsync(client, request, presenceKeys!, connection.PresenceDevice!, review, beforeSend, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public RemoteObservation? LoadObservation(RuntimeConnection connection)
    {
        try
        {
            string path = ObservationPath(connection);
            if (!File.Exists(path)) return null;
            CachedObservation? cached = JsonSerializer.Deserialize<CachedObservation>(File.ReadAllText(path), ProtocolJson.Options);
            return cached is { Version: 1, Observation: { AsOf: not null, Status.Json.ValueKind: JsonValueKind.Object, Attention.Items: not null, Communication: not null, Details: not null, Products: not null } } &&
                cached.Binding == Binding(connection) && cached.Observation.Attention.Principal == connection.Principal && Complete(cached.Observation) ? cached.Observation : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <inheritdoc />
    public void SaveObservation(RuntimeConnection connection, RemoteObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Directory.CreateDirectory(observations);
        string path = ObservationPath(connection);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CachedObservation(1, Binding(connection), observation), ProtocolJson.Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string ObservationPath(RuntimeConnection connection) => Path.Combine(observations, Binding(connection) + ".json");
    private static bool Complete(RemoteObservation observation) =>
        observation.Attention.Items.All(n => n is { Kind: not null, Subject: not null, Why: not null, State: not null, AsOf: not null, NextAction: not null, SubjectType: not null, SubjectId: not null, Revision: not null }) &&
        observation.Products.All(p => p is { Name: not null, Id: not null }) &&
        observation.PolicyDetails is not null && observation.PolicyDetails.Values.All(d => d is { AsOf: not null, Notice.Policy: not null }) &&
        observation.Details.Values.All(d => d is { Id: not null, AsOf: not null, Work.Json.ValueKind: JsonValueKind.Object, Runs: not null, Progress: not null } &&
            d.Runs.All(r => r is not null) && d.Progress.All(p => p is { AsOf: not null }));
    private static string Binding(RuntimeConnection connection) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { connection.Id, connection.Estate, connection.Principal, connection.Identity, connection.Device }, ProtocolJson.Options)));
    private sealed record CachedObservation(int Version, string Binding, RemoteObservation Observation);
}
