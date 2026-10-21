// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>
/// The runtimes this machine reaches over the local network, from 0.6.0: each added by pairing with a code, kept owner-only under
/// the Vericairn home as machine configuration, never in a repository, checked on demand, and removed on demand. Removing a
/// connection forgets the runtime here; the device stays admitted until an administrator revokes it, since disconnection alone is
/// never revocation.
/// </summary>
public sealed class RuntimeConnections
{
    /// <summary>How long a check waits for a runtime by default.</summary>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(10);

    private readonly string directory;
    private readonly IDeviceKeyStore? keys;
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The connections kept under a home (the Vericairn home by default), with device keys in a given store or in this machine's stores.</summary>
    public RuntimeConnections(string? home = null, IDeviceKeyStore? keys = null)
    {
        directory = Path.Combine(home ?? VericairnHome.Path(), "connections");
        this.keys = keys;
    }

    /// <summary>
    /// Pairs with the runtime at an address using a code and keeps the connection; throws <see cref="PairingException"/> when the pairing
    /// does not complete. Pairing again replaces the earlier connections to the same estate that pin another runtime identity, which
    /// the runtime replaced, or that are for the same principal: their devices stay admitted until an administrator revokes them.
    /// </summary>
    public RuntimeConnection Add(string address, string code, PairingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Keep(RuntimePairing.Pair(address, code, keys is null || options.KeyStore is not null ? options : options with { KeyStore = keys }));
    }

    /// <summary>Pairs and keeps a connection asynchronously. Interruption does not claim that a remote admission was undone.</summary>
    public async Task<RuntimeConnection> AddAsync(string address, string code, PairingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        PairedRuntime paired = await RuntimePairing.PairAsync(address, code, keys is null || options.KeyStore is not null ? options : options with { KeyStore = keys }, cancellationToken).ConfigureAwait(false);
        return Keep(paired);
    }

    private RuntimeConnection Keep(PairedRuntime paired)
    {
        var connection = new RuntimeConnection(Ulid.NewUlid().Value, paired.Address, paired.Estate, paired.EstateName, paired.Identity, paired.Device, paired.DevicePublicKey, paired.Principal, paired.PrincipalName, paired.Profiles, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        Save(connection);
        foreach (RuntimeConnection earlier in List().Where(c => c.Id != connection.Id && c.Estate == connection.Estate && (c.Identity != connection.Identity || c.Principal == connection.Principal)))
        {
            File.Delete(PathOf(earlier.Id));
        }

        return connection;
    }

    /// <summary>Every connection kept here, oldest first.</summary>
    public IReadOnlyList<RuntimeConnection> List()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var connections = new List<RuntimeConnection>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<RuntimeConnection>(File.ReadAllBytes(file), ProtocolJson.Options) is RuntimeConnection connection && Ulid.IsValid(connection.Id))
                {
                    connections.Add(connection);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A file this build cannot read is not a connection it can use.
            }
        }

        return connections.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>A connection by its id, or null.</summary>
    public RuntimeConnection? Find(string id) => List().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Forgets a connection here and returns it; null when there is none. The device stays admitted, and its key stays in this machine's custody.</summary>
    public RuntimeConnection? Remove(string id)
    {
        RuntimeConnection? connection = Find(id);
        if (connection is not null)
        {
            File.Delete(PathOf(connection.Id));
        }

        return connection;
    }

    /// <summary>The signer of a connection's device: its key held in the given store or in any of this machine's; null when none holds it.</summary>
    public DeviceRequestSigner? Signer(RuntimeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return keys is not null ? (keys.Has(connection.Device) ? new DeviceRequestSigner(keys, connection.Device) : null) : DeviceRequestSigner.Find(connection.Device);
    }

    /// <summary>A client of the connection's runtime over the local network; refused when this machine no longer holds the device's key.</summary>
    public VericairnClient Connect(RuntimeConnection connection, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        DeviceRequestSigner signer = Signer(connection) ?? throw new InvalidOperationException($"This machine no longer holds the key of device {connection.Device}; pair again.");
        return VericairnClient.LocalNetwork(connection, signer, timeout);
    }

    /// <summary>
    /// Checks a connection now and keeps what the check found. <paramref name="progress"/> hears connecting first and then the state
    /// the check came to: current when the runtime proved the paired identity, accepted the device and answered its reads; partial
    /// when it answered some; denied when it refused the device; otherwise cached when an earlier check was current, or unavailable.
    /// </summary>
    public ConnectionStatus Probe(RuntimeConnection connection, TimeSpan? timeout = null, Action<string>? progress = null) =>
        ProbeCoreAsync(connection, timeout, progress, preserveInterruption: false, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Checks a connection without blocking, distinguishing caller cancellation and deadline from a completed observation.</summary>
    public Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, TimeSpan? timeout = null, Action<string>? progress = null, CancellationToken cancellationToken = default) =>
        ProbeCoreAsync(connection, timeout, progress, preserveInterruption: true, cancellationToken);

    private async Task<ConnectionStatus> ProbeCoreAsync(RuntimeConnection connection, TimeSpan? timeout, Action<string>? progress, bool preserveInterruption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (cancellationToken.IsCancellationRequested) throw new ClientCancelledException(false, cancellationToken);
        progress?.Invoke(ConnectionStates.Connecting);
        bool answered = false;
        bool denied = false;
        bool complete = false;
        string message;
        DeviceRequestSigner? signer = Signer(connection);
        if (signer is null)
        {
            message = $"This machine no longer holds the key of device {connection.Device}; pair again.";
        }
        else
        {
            try
            {
                using VericairnClient client = VericairnClient.LocalNetwork(connection, signer, timeout ?? DefaultTimeout, Clock);
                HealthResponse health = preserveInterruption ? await client.HealthAsync(cancellationToken).ConfigureAwait(false) : client.Health();
                answered = true;
                message = $"Vericairn {health.Vericairn} serving {connection.EstateName ?? health.Estate} at {connection.Address}; the runtime proved its identity.";
                try
                {
                    _ = preserveInterruption ? await client.StatusAsync(cancellationToken).ConfigureAwait(false) : client.Status();
                    complete = true;
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
                {
                    denied = true;
                    message = $"The runtime refused device {connection.Device} while it was being read.";
                }
                catch (Exception exception) when (preserveInterruption && ClientFailure.FromException(exception) is { Code: "refused" } refusal)
                {
                    denied = true;
                    message = refusal.Message;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException || (!preserveInterruption && exception is TaskCanceledException))
                {
                    message = $"The runtime proved its identity and accepted device {connection.Device}, but did not answer every read: {exception.Message}";
                }
            }
            catch (Exception exception) when (preserveInterruption && ClientFailure.FromException(exception) is { Code: "refused" } refusal)
            {
                denied = true;
                message = refusal.Message;
            }
            catch (Exception exception) when (Refusal(exception) is RuntimeConnectionException refusal)
            {
                denied = refusal.State == ConnectionStates.Denied;
                message = refusal.Message;
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                denied = true;
                message = $"The runtime refused device {connection.Device}.";
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or AuthenticationException or JsonException || (!preserveInterruption && exception is TaskCanceledException))
            {
                message = $"No answer from the runtime at {connection.Address}: {exception.Message}";
            }
        }

        string state = ConnectionStates.Of(answered, denied, complete, connection.LastCurrent is not null);
        if (state == ConnectionStates.Cached)
        {
            message += $" Last current at {connection.LastCurrent!.At}.";
        }

        var observation = new ConnectionObservation(state, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), message);
        RuntimeConnection updated = connection with { Observed = observation, LastCurrent = state == ConnectionStates.Current ? observation : connection.LastCurrent };
        if (File.Exists(PathOf(connection.Id)))
        {
            Save(updated);
        }

        progress?.Invoke(state);
        return new ConnectionStatus(updated, state, message);
    }

    private static RuntimeConnectionException? Refusal(Exception exception)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is RuntimeConnectionException refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private string PathOf(string id) => Ulid.IsValid(id) ? Path.Combine(directory, id + ".json") : throw new ArgumentException("A connection id is a ULID.", nameof(id));

    private void Save(RuntimeConnection connection)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string path = PathOf(connection.Id);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(connection, ProtocolJson.Options));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
