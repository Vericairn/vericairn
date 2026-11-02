// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class RemoteDecisionJourneyTests
{
    [Fact]
    public async Task AnAnswerAndAnAcceptanceCarryThePhonesSignatureWhenThePersonsRuntimeDeviceRequiresPresence()
    {
        await using PhoneJourney journey = await PhoneJourney.StartAsync();
        RemoteViewModel remote = journey.Remote;
        RecordEnvelope ask = journey.Ask;
        RecordEnvelope result = journey.Result;
        using VericairnClient client = journey.Harness.Connections.Connect(remote.Selected!.Connection);
        var ordinary = new NewRecordRequest("acceptance", journey.Person,
            JsonSerializer.SerializeToElement(new { statement = "Accept this result.", basis = new[] { journey.Review.Id }, self = false }),
            Subject: "result:" + result.Id + ":" + result.ComputedHash);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.NewRecordAsync(ordinary, TestContext.Current.CancellationToken));

        await journey.Open(ask);
        Assert.Contains(ask.ComputedHash, remote.DecisionIdentity, StringComparison.Ordinal);
        Assert.Contains(journey.Order.ComputedHash, remote.DecisionMaterial, StringComparison.Ordinal);
        const string words = "  Keep the current scope.\nNo publication.  ";
        Ready(remote, words);
        await remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.Equal("completed", remote.Decision!.State);
        RecordEnvelope answer = Assert.Single(journey.Harness.Estate.Load().Current("decision"));
        Assert.Equal(ask.Id, Assert.Single(answer.Body.GetProperty("answers").EnumerateArray()).GetString());
        Assert.Equal(ask.ComputedHash, answer.Body.GetProperty("answer").GetProperty("hash").GetString());
        Assert.Equal(ask.Id, answer.Body.GetProperty("answer").GetProperty("ask").GetString());
        Assert.Equal(words, answer.Body.GetProperty("statement").GetString());
        Assert.Equal(journey.Person, answer.By);
        Assert.Equal(remote.Decision.Prepared!.Hash, answer.ComputedHash);
        VerifyPhone(answer, remote.Selected.Connection);
        await remote.SubmitDecisionCommand.ExecuteAsync();

        await remote.CloseDecisionCommand.ExecuteAsync();
        await remote.RefreshCommand.ExecuteAsync();
        await journey.Open(result);
        Assert.Contains(journey.Review.ComputedHash, remote.DecisionMaterial, StringComparison.Ordinal);
        Assert.Contains("The exact result was inspected.", remote.DecisionMaterial, StringComparison.Ordinal);
        Ready(remote, "I accept this exact result from its review and evidence summaries.");
        await remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.Equal("completed", remote.Decision!.State);
        RecordEnvelope accepted = Assert.Single(journey.Harness.Estate.Load().Current("acceptance"));
        Assert.Equal(result.Reference, accepted.Subject);
        Assert.Equal(journey.Review.Id, Assert.Single(accepted.Body.GetProperty("basis").EnumerateArray()).GetString());
        Assert.Equal(journey.Person, accepted.By);
        VerifyPhone(accepted, remote.Selected.Connection);
        await remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.Single(journey.Harness.Estate.Load().Current("decision"));
        Assert.Single(journey.Harness.Estate.Load().Current("acceptance"));
        LocalNetworkWrite[] writes = LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(journey.Harness.Estate.Root))!)
            .Where(w => w.Operation == "POST /v1/records/signed").ToArray();
        Assert.Equal(2, writes.Length);
        Assert.All(writes, w => Assert.Equal(remote.Selected.Connection.Device, w.Device));
        Assert.Equal(0, journey.Harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task ASubjectChangedAfterPreparationIsNotSignedAndItsAnswerStaysADraft()
    {
        await using PhoneJourney journey = await PhoneJourney.StartAsync();
        await journey.Open(journey.Ask);
        Ready(journey.Remote, "Keep the current scope.");
        RecordEnvelope? changed = null;
        journey.Connections.BeforeReview = () => changed = journey.Harness.Estate.Add("ask", journey.Harness.Estate.BuilderId,
            journey.Ask.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), journey.Ask.Subject, journey.Ask.Id);
        await journey.Remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.NotNull(changed);
        Assert.Equal("draft", journey.Remote.Decision!.State);
        Assert.Equal(journey.Ask.ComputedHash, journey.Remote.Decision.Original.Subject.Hash);
        Assert.Equal(changed.Id, journey.Remote.Decision.Review.Subject.Id);
        Assert.Equal("Keep the current scope.", journey.Remote.DecisionStatement);
        Assert.False(journey.Remote.MaterialReviewed);
        Assert.Empty(journey.Harness.Estate.Load().Current("decision"));
        Assert.DoesNotContain(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(journey.Harness.Estate.Root))!), w => w.Operation == "POST /v1/records/signed");
    }

    [Fact]
    public async Task OfflineDraftsSurviveAppStorageAndReconnectRequiresANewReview()
    {
        await using PhoneJourney journey = await PhoneJourney.StartAsync();
        await journey.Open(journey.Ask);
        journey.Connections.Offline = true;
        await journey.Remote.RefreshCommand.ExecuteAsync();
        journey.Remote.DecisionStatement = "Keep this as a draft until I review again.";
        journey.Remote.MaterialReviewed = true;
        await journey.Remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.Equal(0, journey.Connections.SignedRequests);
        var restarted = new RemoteViewModel(journey.Connections);
        restarted.SelectedDraft = Assert.Single(restarted.DecisionDrafts);
        await restarted.OpenDraftCommand.ExecuteAsync();
        Assert.Equal(journey.Remote.DecisionStatement, restarted.DecisionStatement);
        Assert.Equal(journey.Ask.ComputedHash, restarted.Decision!.Original.Subject.Hash);
        await restarted.SubmitDecisionCommand.ExecuteAsync();
        journey.Connections.Offline = false;
        await restarted.RefreshCommand.ExecuteAsync();
        Assert.Equal(ConnectionStates.Current, restarted.ConnectionState);
        Assert.False(restarted.MaterialReviewed);
        Assert.False(restarted.SubmitDecisionCommand.CanExecute(null));
        Assert.Equal(0, journey.Connections.SignedRequests);
        Assert.Empty(journey.Harness.Estate.Load().Current("decision"));
    }

    [Fact]
    public async Task MaterialUnavailableOnThePhonePointsToTheDesktopWithoutAnAcceptance()
    {
        await using PhoneJourney journey = await PhoneJourney.StartAsync(missingEvidence: true);
        await journey.Open(journey.Result);
        Ready(journey.Remote, "Accept when the evidence can be inspected.");
        Assert.Contains("desktop", journey.Remote.DecisionEligibility, StringComparison.Ordinal);
        Assert.False(journey.Remote.SubmitDecisionCommand.CanExecute(null));
        await journey.Remote.SubmitDecisionCommand.ExecuteAsync();
        Assert.Equal(0, journey.Connections.SignedRequests);
        Assert.Empty(journey.Harness.Estate.Load().Current("acceptance"));
    }

    private static void VerifyPhone(RecordEnvelope record, RuntimeConnection phone)
    {
        RecordSignature signature = Assert.Single(record.Signatures);
        Assert.Equal(phone.Device, signature.Device);
        Assert.True(RecordSigning.Verify(phone.DevicePublicKey, RecordSigning.Digest(record.ComputedHash), signature.Signature));
    }

    private static void Ready(RemoteViewModel remote, string statement)
    {
        remote.DecisionStatement = statement;
        if (remote.IsAsk)
        {
            remote.DecisionStake = "low";
            remote.DecisionBaseCommit = new string('a', 40);
            remote.DecisionHasNoDocumentChanges = true;
        }
        remote.MaterialReviewed = true;
    }

    private sealed class PhoneJourney(LocalNetworkHarness harness, FileKeyStore runtimeKeys, string presenceDevice, string person,
        RecordEnvelope order, RecordEnvelope ask, RecordEnvelope result, RecordEnvelope review, JourneyConnections connections, RemoteViewModel remote) : IAsyncDisposable
    {
        internal LocalNetworkHarness Harness { get; } = harness;
        internal string Person { get; } = person;
        internal RecordEnvelope Order { get; } = order;
        internal RecordEnvelope Ask { get; } = ask;
        internal RecordEnvelope Result { get; } = result;
        internal RecordEnvelope Review { get; } = review;
        internal JourneyConnections Connections { get; } = connections;
        internal RemoteViewModel Remote { get; } = remote;

        internal static async Task<PhoneJourney> StartAsync(bool missingEvidence = false)
        {
            LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
            // StartAsync creates the daemon suite's isolated test home before any key store is opened.
            var runtimeKeys = new FileKeyStore(VericairnHome.Path());
            string device = Ulid.NewUlid(harness.Estate.Clock.AddSeconds(2)).Value;
            try
            {
                string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone operator","roles":["operator"]}""").Id;
                byte[] publicKey = runtimeKeys.Create(device);
                var body = DeviceRecords.Body(person, "Presence device", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(publicKey), ["embedded"], 3, "required");
                harness.Estate.Add("device", person, body.ToJsonString(), id: device, signer: new StoreSigner(runtimeKeys, device));
                Assert.True(harness.Socket.DeviceAdmit(new(device, harness.Estate.OperatorId, "Test presence device.")).Ok);
                Assert.NotNull(harness.Host.Service.PresenceRefusal(person));
                RecordEnvelope order = harness.Estate.Add("work-order", harness.Estate.OperatorId,
                    """{"key":"PHONE-DECISION","kind":"ordinary","title":"Phone decision","intent":"Inspect exact phone decisions.","scope":["tests"],"exitQuestion":"Exact and signed?","inputs":[],"deliverables":["evidence"]}""");
                RecordEnvelope ask = harness.Estate.Add("ask", harness.Estate.BuilderId, JsonSerializer.Serialize(new
                {
                    to = person, question = "Keep the current scope?", recommendation = "Keep the current scope.", consequence = "Work waits for the answer.", state = "open",
                }), order.Reference);
                RecordEnvelope assignment = harness.Estate.Add("assignment", harness.Estate.OperatorId,
                    """{"contributor":"CONTRIBUTOR","brief":{"outcome":"Inspect the phone behavior.","sources":[],"permittedChanges":["path:tests/**"],"prohibitedEffects":["no publication"],"workspace":["widget branch work/phone"],"limits":[],"expectedOutputs":["evidence"],"reviewCriteria":["Exact hashes."],"recovery":"Read the result."}}""".Replace("CONTRIBUTOR", harness.Estate.BuilderId, StringComparison.Ordinal), order.Reference);
                RecordEnvelope attempt = harness.Estate.Add("attempt", harness.Estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget branch work/phone","note":"Test the phone decision."}""", assignment.Reference);
                string evidence = missingEvidence ? """[{"summary":"Inspect evidence on the desktop.","hash":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]""" : """[{"summary":"The intended behavior was observed in the fixture."}]""";
                RecordEnvelope result = harness.Estate.Add("result", harness.Estate.BuilderId,
                    """{"outcome":"completed","summary":"The exact result was inspected.","outputs":[],"evidence":EVIDENCE,"checks":[{"name":"phone","outcome":"passed","summary":"The fixture behaved as expected."}],"limitations":["No publication is claimed."],"unresolvedEffects":[],"nextStep":"Review this exact result."}""".Replace("EVIDENCE", evidence, StringComparison.Ordinal), attempt.Reference);
                RecordEnvelope review = harness.Estate.Add("review", harness.Estate.OperatorId,
                    """{"independence":"operator","outcome":"pass","summary":"Read the exact result and evidence.","findings":[]}""", result.Reference);
                var connections = new JourneyConnections(new(harness.DeviceHome, harness.DeviceKeys));
                var remote = new RemoteViewModel(connections) { Address = harness.Address, Code = harness.Issue(person).Code };
                await remote.PairCommand.ExecuteAsync();
                Assert.Equal(ConnectionStates.Current, remote.ConnectionState);
                return new(harness, runtimeKeys, device, person, order, ask, result, review, connections, remote);
            }
            catch
            {
                runtimeKeys.Remove(device);
                await harness.DisposeAsync();
                throw;
            }
        }

        internal async Task Open(RecordEnvelope subject)
        {
            Remote.SelectedAttention = Remote.Attention.First(a => a.SubjectId == subject.Id);
            await Remote.OpenAttentionCommand.ExecuteAsync();
            Assert.NotNull(Remote.Decision);
        }

        public async ValueTask DisposeAsync()
        {
            runtimeKeys.Remove(presenceDevice);
            await Harness.DisposeAsync();
        }
    }

    private sealed class JourneyConnections(LocalNetworkConnections inner) : IRemoteConnections
    {
        internal Action? BeforeReview { get; set; }
        internal bool Offline { get; set; }
        internal int SignedRequests { get; private set; }
        public IReadOnlyList<RuntimeConnection> List() => inner.List();
        public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken) => inner.PairAsync(address, code, deviceName, cancellationToken);
        public Task<ConnectionStatus> ProbeAsync(RuntimeConnection connection, CancellationToken cancellationToken) => Offline
            ? Task.FromResult(new ConnectionStatus(connection, ConnectionStates.Cached, "Offline.")) : inner.ProbeAsync(connection, cancellationToken);
        public IEstateApi Connect(RuntimeConnection connection) => inner.Connect(connection);
        public RemoteObservation? LoadObservation(RuntimeConnection connection) => inner.LoadObservation(connection);
        public void SaveObservation(RuntimeConnection connection, RemoteObservation observation) => inner.SaveObservation(connection, observation);
        public IReadOnlyList<RemoteDecisionDraft> LoadDecisions(RuntimeConnection connection) => inner.LoadDecisions(connection);
        public void SaveDecision(RuntimeConnection connection, RemoteDecisionDraft draft) => inner.SaveDecision(connection, draft);
        public Task<WriteOutcome> WriteSignedAsync(RuntimeConnection connection, NewRecordRequest request, Func<RecordPrepared, bool> review, CancellationToken cancellationToken = default)
        {
            SignedRequests++;
            return inner.WriteSignedAsync(connection, request, prepared => { BeforeReview?.Invoke(); return review(prepared); }, cancellationToken);
        }
    }
}
