// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using System.Text.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", service.Session.Load().CurrentEstate!.Body.GetProperty("operator").GetString()!, "Test Operator", ["local-network"], "2026-11-01T12:00:00Z");
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

    public EstateService Service => service;
    public int ConnectionsMade { get; private set; }
    public int ApiCalls { get; private set; }
    public bool LoseAcknowledgmentResponse { get; set; }
    public bool CannotKeepObservation { get; set; }
    public IEstateApi Connect(RuntimeConnection connection)
    {
        ConnectionsMade++;
        IEstateApi client = DispatchProxy.Create<IEstateApi, CountingApi>();
        ((CountingApi)client).Inner = service;
        ((CountingApi)client).Called = () => ApiCalls++;
        ((CountingApi)client).LoseAcknowledgmentResponse = () => LoseAcknowledgmentResponse;
        return client;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy generates a subclass at runtime.")]
    public class CountingApi : DispatchProxy
    {
        public IEstateApi Inner { get; set; } = null!;
        public Action Called { get; set; } = null!;
        public Func<bool> LoseAcknowledgmentResponse { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Called();
            try
            {
                object? result = targetMethod!.Invoke(Inner, args);
                return result is Task<AttentionAcknowledgeResponse> acknowledgment && LoseAcknowledgmentResponse()
                    ? LostResponse(acknowledgment) : result;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        private static async Task<AttentionAcknowledgeResponse> LostResponse(Task<AttentionAcknowledgeResponse> response)
        {
            _ = await response.ConfigureAwait(false);
            throw new HttpRequestException("The response was lost.");
        }
    }
    private readonly Dictionary<string, RemoteObservation> observations = [];
    public RemoteObservation? LoadObservation(RuntimeConnection connection) => observations.GetValueOrDefault(connection.Id);
    public void SaveObservation(RuntimeConnection connection, RemoteObservation observation)
    {
        if (CannotKeepObservation) throw new IOException("App storage is unavailable.");
        observations[connection.Id] = JsonSerializer.Deserialize<RemoteObservation>(JsonSerializer.Serialize(observation, ProtocolJson.Options), ProtocolJson.Options)!;
    }

    public void Dispose() => service.Dispose();
}
