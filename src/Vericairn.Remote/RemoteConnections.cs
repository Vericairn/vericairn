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

    /// <summary>Pairs with the runtime at an address using the code its administrator issued, and keeps the connection.</summary>
    Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken);

    /// <summary>Checks a connection now: connecting, then current, partial, denied, cached or unavailable, as the SDK observes it.</summary>
    Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken);

    /// <summary>A client of the connection's runtime over the local network, signed by this phone's device key.</summary>
    IEstateApi Connect(RuntimeConnection connection);

    /// <summary>Restores this connection's last observation from app storage; it always starts cached.</summary>
    RemoteObservation? LoadObservation(RuntimeConnection connection) => null;

    /// <summary>Keeps an observation in app storage without queuing requests. Older implementations retain no persistent cache.</summary>
    void SaveObservation(RuntimeConnection connection, RemoteObservation observation) { }

    /// <summary>Prepares a record, gives the caller its exact unsigned form, then signs only with this phone's device key.</summary>
    Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review,
        CancellationToken cancellationToken = default) => Task.FromResult(WriteOutcome.Fail(new("unsupported", "This connection cannot sign records on the phone.", [])));

    /// <summary>Restores drafts and attempted submissions for this exact connection. Unreadable storage must refuse, never appear empty.</summary>
    IReadOnlyList<RemoteDecisionDraft> LoadDecisions(RuntimeConnection connection) => [];

    /// <summary>Durably retains input or reserves a prepared identity before signing. An attempted identity cannot be replaced or cleared.</summary>
    void SaveDecision(RuntimeConnection connection, RemoteDecisionDraft draft) => throw new NotSupportedException("This connection cannot retain decision drafts and submission identities.");
}

/// <summary>
/// The phone's connections, kept by the SDK under the app's own storage, with the device keys in the store the platform head
/// supplies. Pairing, the runtime identity check, request signing and connection states are the SDK's, never re-implemented here.
/// </summary>
public sealed partial class LocalNetworkConnections : IRemoteConnections
{
    private readonly RuntimeConnections store;
    private readonly IDeviceKeyStore keys;
    private readonly string observations;

    /// <summary>Keeps connections under <paramref name="home"/> with device keys in <paramref name="keys"/>.</summary>
    public LocalNetworkConnections(string home, IDeviceKeyStore keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        store = new RuntimeConnections(home, keys);
        observations = Path.Combine(home, "remote-observations");
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

    /// <inheritdoc />
    public async Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(review);
        using VericairnClient client = store.Connect(connection);
        return await DeviceSignedRecords.WriteAsync(client, request, keys, connection.Device, review, cancellationToken).ConfigureAwait(false);
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
        observation.Details.Values.All(d => d is { Id: not null, AsOf: not null, Work.Json.ValueKind: JsonValueKind.Object, Runs: not null, Progress: not null } &&
            d.Runs.All(r => r is not null) && d.Progress.All(p => p is { AsOf: not null }));
    private static string Binding(RuntimeConnection connection) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { connection.Id, connection.Estate, connection.Principal, connection.Identity, connection.Device }, ProtocolJson.Options)));
    private sealed record CachedObservation(int Version, string Binding, RemoteObservation Observation);
}
