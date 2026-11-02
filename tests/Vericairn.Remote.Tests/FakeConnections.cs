// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using System.Text.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Vericairn.Records.Identity;
using Vericairn.Records.Authoring;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
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
    private readonly DirectoryInfo phoneHome = Directory.CreateTempSubdirectory("vericairn-test-phone-");

    public List<RuntimeConnection> Kept { get; } = [];

    public Exception? PairFailure { get; set; }

    public Queue<(string State, string Message)> Checks { get; } = new();

    public IReadOnlyList<RuntimeConnection> List() => Kept;

    public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken)
    {
        if (PairFailure is not null) throw PairFailure;
        var connection = new RuntimeConnection(Ulid.NewUlid().Value, address, Ulid.NewUlid().Value, "Test Estate", "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", Ulid.NewUlid().Value,
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", service.Session.Load().CurrentEstate!.Body.GetProperty("operator").GetString()!, "Test Operator", ["local-network"], "2026-11-01T12:00:00Z");
        if (EnableSignedWrites)
        {
            var keys = new FileKeyStore(phoneHome.FullName);
            string publicKey = RecordSigning.EncodePublicKey(keys.Create(connection.Device));
            connection = connection with { DevicePublicKey = publicKey };
            var body = DeviceRecords.Body(connection.Principal, "Test phone", "other", "file-owner-only", publicKey, ["local-network"], 3);
            var signer = new StoreSigner(keys, connection.Device);
            AuthoredRecord device = RecordAuthor.Create(new("device", connection.Principal, JsonSerializer.SerializeToElement(body), Id: connection.Device, Format: 3), signer);
            RecordAuthor.Write(root, device);
            RecordAuthor.Write(root, RecordAuthor.Create(new("admission", connection.Principal,
                JsonSerializer.SerializeToElement(new { principal = connection.Principal, status = "admitted", note = "Test phone is the fixture's first authority." }), device.Envelope.Reference, Format: 3), signer));
        }
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
    public bool EnableSignedWrites { get; set; }
    public bool CannotKeepDecision { get; set; }
    public bool LoseSignedResponse { get; set; }
    public bool EmptySignedResponse { get; set; }
    public bool HideWrittenRecord { get; set; }
    public bool FailRecordRead { get; set; }
    public Problem? WriteProblem { get; set; }
    public bool RefusePrepared { get; set; }
    public Action? BeforeReview { get; set; }
    public Action? AfterWrite { get; set; }
    public Action<string>? BeforeApiCall { get; set; }
    public Func<Task>? WaitBeforeReview { get; set; }
    public Func<RecordPrepared, RecordPrepared>? ChangePrepared { get; set; }
    public int PreparedCalls { get; private set; }
    public int SignedWrites { get; private set; }
    public RecordWritten? LastWritten { get; private set; }
    public IEstateApi Connect(RuntimeConnection connection)
    {
        ConnectionsMade++;
        IEstateApi client = DispatchProxy.Create<IEstateApi, CountingApi>();
        ((CountingApi)client).Inner = service;
        ((CountingApi)client).Called = () => ApiCalls++;
        ((CountingApi)client).BeforeCall = name => BeforeApiCall?.Invoke(name);
        ((CountingApi)client).LoseAcknowledgmentResponse = () => LoseAcknowledgmentResponse;
        ((CountingApi)client).RecordRead = record =>
        {
            if (FailRecordRead) throw new HttpRequestException("The record read failed.");
            return HideWrittenRecord && record?.Id == LastWritten?.Id ? null : record;
        };
        return client;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy generates a subclass at runtime.")]
    public class CountingApi : DispatchProxy
    {
        public IEstateApi Inner { get; set; } = null!;
        public Action Called { get; set; } = null!;
        public Action<string> BeforeCall { get; set; } = null!;
        public Func<bool> LoseAcknowledgmentResponse { get; set; } = null!;
        public Func<RecordResponse?, RecordResponse?> RecordRead { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Called();
            BeforeCall(targetMethod!.Name);
            try
            {
                object? result = targetMethod!.Invoke(Inner, args);
                if (targetMethod.Name == nameof(IEstateApi.RecordAsync) && result is Task<RecordResponse?> record) return ReadRecord(record);
                return result is Task<AttentionAcknowledgeResponse> acknowledgment && LoseAcknowledgmentResponse()
                    ? LostResponse(acknowledgment) : result;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        private async Task<RecordResponse?> ReadRecord(Task<RecordResponse?> response) => RecordRead(await response.ConfigureAwait(false));
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

    private readonly Dictionary<string, Dictionary<string, RemoteDecisionDraft>> decisions = [];
    public IReadOnlyList<RemoteDecisionDraft> LoadDecisions(RuntimeConnection connection)
    {
        lock (decisions) return decisions.TryGetValue(connection.Id, out var saved) ? saved.Values.ToArray() : [];
    }
    public void SaveDecision(RuntimeConnection connection, RemoteDecisionDraft draft)
    {
        if (CannotKeepDecision) throw new IOException("App storage is unavailable.");
        lock (decisions)
        {
            if (!decisions.TryGetValue(connection.Id, out var saved)) decisions[connection.Id] = saved = [];
            if (saved.TryGetValue(draft.Key, out var previous) && previous.Attempted && previous.Prepared?.Id != draft.Prepared?.Id)
                throw new InvalidOperationException("The original was already attempted.");
            saved[draft.Key] = JsonSerializer.Deserialize<RemoteDecisionDraft>(JsonSerializer.Serialize(draft, ProtocolJson.Options), ProtocolJson.Options)!;
        }
    }

    public async Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review, CancellationToken cancellationToken = default)
    {
        PreparedCalls++;
        if (RefusePrepared) return WriteOutcome.Fail(new("refused", "The runtime refused preparation.", []));
        string[]? subject = request.Subject?.Split(':', 3);
        var draft = new RecordDraft(request.Type, request.By, request.Body, subject is null ? null : new(subject[0], subject[1], subject[2]),
            At: DateTimeOffset.UtcNow, Id: Ulid.NewUlid().Value, Format: 3);
        AuthoredRecord unsigned = RecordAuthor.Create(draft);
        using JsonDocument json = JsonDocument.Parse(unsigned.Bytes);
        var prepared = new RecordPrepared(Ulid.NewUlid().Value, unsigned.Envelope.Id, unsigned.Envelope.At, unsigned.Envelope.ComputedHash,
            RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(10)), json.RootElement.Clone());
        if (WaitBeforeReview is not null) await WaitBeforeReview().ConfigureAwait(false);
        BeforeReview?.Invoke();
        if (ChangePrepared is not null) prepared = ChangePrepared(prepared);
        if (!review(prepared)) return WriteOutcome.Fail(new("declined", "Nothing was signed or written.", []));
        AuthoredRecord written = RecordAuthor.Create(draft, new StoreSigner(new FileKeyStore(phoneHome.FullName), connection.Device));
        SignedWrites++;
        RecordAuthor.Write(root, written);
        LastWritten = new(written.RelativePath, written.Envelope.Id, written.Envelope.ComputedHash);
        AfterWrite?.Invoke();
        if (LoseSignedResponse) throw new HttpRequestException("The signed write response was lost.");
        if (EmptySignedResponse) return new(null, null);
        return new(LastWritten, WriteProblem);
    }

    public void Dispose()
    {
        service.Dispose();
        phoneHome.Delete(recursive: true);
    }
}
