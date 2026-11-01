// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Remote;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Stands in for the phone's connections: pairing either keeps a connection or fails as told, each check reports the next state
/// queued (current by default), and the client is the estate's in-process service. The real path is covered by the Daemon tests.
/// </summary>
internal sealed class FakeConnections(string root) : IRemoteConnections, IDisposable
{
    private readonly EstateService service = EstateService.Open(root);

    public List<RuntimeConnection> Kept { get; } = [];

    public Exception? PairFailure { get; set; }

    public Queue<(string State, string Message)> Checks { get; } = new();

    public IReadOnlyList<RuntimeConnection> List() => Kept;

    public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken)
    {
        if (PairFailure is not null) throw PairFailure;
        var connection = new RuntimeConnection(Ulid.NewUlid().Value, address, Ulid.NewUlid().Value, "Test Estate", "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", Ulid.NewUlid().Value,
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", Ulid.NewUlid().Value, "Test Builder", ["local-network"], "2026-11-01T12:00:00Z");
        Kept.Add(connection);
        return Task.FromResult(connection);
    }

    public Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken)
    {
        (string state, string message) = Checks.Count > 0 ? Checks.Dequeue() : (ConnectionStates.Current, "The runtime proved its identity and answered.");
        var observed = new ConnectionObservation(state, "2026-11-01T12:00:00Z", message);
        RuntimeConnection updated = connection with { Observed = observed, LastCurrent = state == ConnectionStates.Current ? observed : connection.LastCurrent };
        Kept[Kept.FindIndex(c => c.Id == connection.Id)] = updated;
        return Task.FromResult(new ConnectionStatus(updated, state, message));
    }

    public IEstateApi Connect(RuntimeConnection connection) => service;

    public void Dispose() => service.Dispose();
}
