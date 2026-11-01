// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class RemoteAttentionJourneyTests
{
    [Fact]
    public async Task TwoPhonesShareOnlyTheirPrincipalsAcknowledgmentWithPresenceRequiredAndRestartOffline()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone reader","roles":["contributor"]}""").Id;
        var keys = new FileKeyStore(VericairnHome.Path()); // The harness created this test's isolated home.
        string id = Ulid.NewUlid(harness.Estate.Clock.AddSeconds(1)).Value;
        byte[] publicKey = keys.Create(id);
        try
        {
            var body = DeviceRecords.Body(person, "Presence device", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(publicKey), ["embedded"], 3, "required");
            harness.Estate.Add("device", person, body.ToJsonString(), id: id, signer: new StoreSigner(keys, id));
            Assert.True(harness.Socket.DeviceAdmit(new(id, harness.Estate.OperatorId, "Test presence device.")).Ok);
            Assert.NotNull(harness.Host.Service.PresenceRefusal(person));
            string source = AttentionApiTests.SeedFailure(harness.Estate);
            RuntimeConnection second = harness.Pair(person, "Second phone");
            RuntimeConnection other = harness.Pair(harness.Estate.BuilderId, "Other principal");
            var connections = new LocalNetworkConnections(harness.DeviceHome, harness.DeviceKeys);
            var remote = new RemoteViewModel(connections) { Address = harness.Address, Code = harness.Issue(person).Code };
            await remote.PairCommand.ExecuteAsync();
            Assert.Equal(ConnectionStates.Current, remote.ConnectionState);
            remote.SelectedAttention = Assert.Single(remote.Attention, a => a.Notice?.SubjectId == source);
            using VericairnClient samePerson = harness.Connections.Connect(second);
            using VericairnClient otherPerson = harness.Connections.Connect(other);
            int before = harness.Estate.Load().Records.Count;
            await remote.AcknowledgeCommand.ExecuteAsync();
            Assert.Equal("acknowledged", Assert.Single(remote.Attention, a => a.Notice?.SubjectId == source).AttentionState);
            AttentionNotice shared = Assert.Single((await samePerson.AttentionGetAsync(cancellationToken: TestContext.Current.CancellationToken)).Items, a => a.SubjectId == source);
            Assert.Equal("acknowledged", shared.AttentionState);
            Assert.Equal("unacknowledged", Assert.Single(otherPerson.AttentionGet().Items, a => a.SubjectId == source).AttentionState);
            Assert.Throws<HttpRequestException>(() => otherPerson.AttentionAcknowledge(AttentionApiTests.Request(person, shared)));
            using JsonDocument draft = JsonDocument.Parse("""{"key":"PRESENCE","kind":"ordinary","title":"Presence still needed","intent":"Test signing boundary.","scope":["tests"],"exitQuestion":"Held?","inputs":[],"deliverables":["evidence"]}""");
            Assert.Throws<HttpRequestException>(() => samePerson.NewRecord(new("work-order", person, draft.RootElement)));
            Assert.Equal(before, harness.Estate.Load().Records.Count);
            LocalNetworkWrite written = Assert.Single(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!), w => w.Operation == "POST /v1/attention/acknowledge");
            Assert.Equal("written", written.State);
            Assert.Null(written.Record);
            Assert.Null(written.Hash);

            // The app's own storage survives a new view model and an unreachable runtime.
            await harness.Host.StopAsync();
            var reopened = new RemoteViewModel(connections);
            Assert.Equal(ConnectionStates.Cached, reopened.ConnectionState);
            Assert.StartsWith("cached from ", reopened.Freshness, StringComparison.Ordinal);
            Assert.Contains("seconds old", reopened.ObservationAge, StringComparison.Ordinal);
            Assert.Contains(reopened.Attention, a => a.Notice?.SubjectId == source && a.AttentionState == "acknowledged");
            Assert.False(reopened.AcknowledgeCommand.CanExecute(null));
        }
        finally { keys.Remove(id); }
    }
}
