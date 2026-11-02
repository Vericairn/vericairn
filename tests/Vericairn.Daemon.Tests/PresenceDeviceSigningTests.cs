// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A plan the phone's request device prepared is signed by its presence device: another admitted device of the same principal whose
/// every signature needs its owner's presence. No unattended device and no other principal's device signs it, the record carries
/// the presence device's signature, and the write ledger names the request device that sent it.
/// </summary>
public class PresenceDeviceSigningTests
{
    private const string Order = """{"key":"KEY","kind":"ordinary","title":"Signed with presence","intent":"Exercise presence-signed writes.","scope":["tests"],"exitQuestion":"Signed?","inputs":[],"deliverables":["evidence"]}""";

    [Fact]
    public async Task OnlyThePhonesPresenceDeviceSignsAPlanItsRequestDevicePrepared()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        CancellationToken token = TestContext.Current.CancellationToken;
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone Person","roles":["contributor"]}""").Id;
        string stranger = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Other Person","roles":["contributor"]}""").Id;
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        var strangerPresence = new PresenceKeyStandIn(Path.Combine(harness.DeviceHome, "stranger"));
        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Phone") { PresenceKeyStore = presence });
        RuntimeConnection laptop = harness.Pair(person, "Laptop");
        RuntimeConnection strangerPhone = harness.Connections.Add(harness.Address, harness.Issue(stranger).Code, new PairingOptions("Other phone") { PresenceKeyStore = strangerPresence });

        // Another device of the person that signs unattended cannot sign the phone's plan; the refusal spends the plan.
        using (VericairnClient client = harness.Connections.Connect(phone))
        {
            RecordPrepared plan = (await client.PrepareRecordAsync(Request(person, "PRESENCE-UNATTENDED"), token)).Prepared!;
            WriteOutcome unattended = await client.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, Sign(harness.DeviceKeys, laptop.Device, plan.Hash)) { Device = laptop.Device }, token);
            Assert.Equal("refused", unattended.Problem?.Code);
            Assert.Contains("presence", unattended.Problem!.Message, StringComparison.Ordinal);
            WriteOutcome spent = await client.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, Sign(presence, phone.PresenceDevice!, plan.Hash)) { Device = phone.PresenceDevice }, token);
            Assert.Contains("No prepared record waits", spent.Problem!.Message, StringComparison.Ordinal);

            // A signature over another digest, named as the presence device's, writes nothing.
            RecordPrepared wrong = (await client.PrepareRecordAsync(Request(person, "PRESENCE-WRONG"), token)).Prepared!;
            WriteOutcome forged = await client.NewSignedRecordAsync(new SignedRecordRequest(wrong.Plan, Sign(harness.DeviceKeys, phone.Device, wrong.Hash)) { Device = phone.PresenceDevice }, token);
            Assert.Contains("does not verify", forged.Problem!.Message, StringComparison.Ordinal);
        }

        // Another principal's presence device is refused before the request reaches the service.
        using (VericairnClient client = harness.Connections.Connect(phone))
        {
            RecordPrepared plan = (await client.PrepareRecordAsync(Request(person, "PRESENCE-STRANGER"), token)).Prepared!;
            await Assert.ThrowsAsync<ClientRefusedException>(() => client.NewSignedRecordAsync(
                new SignedRecordRequest(plan.Plan, Sign(strangerPresence, strangerPhone.PresenceDevice!, plan.Hash)) { Device = strangerPhone.PresenceDevice }, token));
        }

        // The phone's presence device signs; the request device sends.
        using (VericairnClient client = harness.Connections.Connect(phone))
        {
            int before = presence.Signatures;
            WriteOutcome signed = await DeviceSignedRecords.WriteWithPresenceAsync(client, Request(person, "PRESENCE-SIGNED"), presence, phone.PresenceDevice!, cancellationToken: token);
            Assert.True(signed.Ok, signed.Problem?.Message);
            Assert.Equal(before + 1, presence.Signatures);
            RecordEnvelope record = harness.Estate.Load().ById[signed.Written!.Id];
            Assert.Equal(phone.PresenceDevice, Assert.Single(record.Signatures).Device);
            LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!), w => w.Record == signed.Written.Id);
            Assert.Equal((phone.Device, person, "POST /v1/records/signed"), (kept.Device, kept.Principal, kept.Operation));
        }

        Assert.Equal(["PRESENCE-SIGNED"], harness.Estate.Load().Current("work-order").Select(o => EstateModel.BodyString(o, "key")).Where(k => k!.StartsWith("PRESENCE-", StringComparison.Ordinal)));
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task ADeclinedPromptOrADiscardedSignatureSendsNothing()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        CancellationToken token = TestContext.Current.CancellationToken;
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone Person","roles":["contributor"]}""").Id;
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Phone") { PresenceKeyStore = presence });
        using VericairnClient client = harness.Connections.Connect(phone);

        presence.Decline = true;
        bool reviewed = false;
        WriteOutcome declined = await DeviceSignedRecords.WriteWithPresenceAsync(client, Request(person, "PRESENCE-DECLINED"), presence, phone.PresenceDevice!,
            _ => reviewed = true, _ => throw new InvalidOperationException("Nothing is sent after a declined prompt."), token);
        Assert.True(reviewed);
        Assert.Equal("not-signed", declined.Problem?.Code);
        Assert.Null(declined.Written);

        presence.Decline = false;
        WriteOutcome discarded = await DeviceSignedRecords.WriteWithPresenceAsync(client, Request(person, "PRESENCE-DISCARDED"), presence, phone.PresenceDevice!, beforeSend: _ => false, cancellationToken: token);
        Assert.Equal("declined", discarded.Problem?.Code);
        Assert.DoesNotContain(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!), w => w.Operation == "POST /v1/records/signed");
        Assert.DoesNotContain(harness.Estate.Load().Current("work-order"), o => EstateModel.BodyString(o, "key")!.StartsWith("PRESENCE-", StringComparison.Ordinal));
    }

    private static NewRecordRequest Request(string person, string key)
    {
        using JsonDocument body = JsonDocument.Parse(Order.Replace("KEY", key, StringComparison.Ordinal));
        return new NewRecordRequest("work-order", person, body.RootElement.Clone());
    }

    private static string Sign(IDeviceKeyStore keys, string device, string hash) => new StoreSigner(keys, device).Sign(RecordSigning.Digest(hash)).Signature;
}
