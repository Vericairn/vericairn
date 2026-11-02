// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A phone pairs two keys under one code: its request key, which signs requests unattended, and its presence key, a second device of
/// the same principal stating presence required, recorded and admitted with it under the same administrator's authority. An
/// existing pairing adds its presence key with a new code for the same principal; custody that cannot require presence claims none.
/// </summary>
[Collection("daemon")]
public class PresenceDevicePairingTests
{
    public PresenceDevicePairingTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task OneCodeRecordsAndAdmitsTheRequestKeyAndThePresenceKeyOfTheSamePerson()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone Person","roles":["contributor"]}""").Id;
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        int before = harness.Estate.Load().Current("device").Count();

        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Phone") { PresenceKeyStore = presence });

        Assert.NotNull(phone.PresenceDevice);
        Assert.NotEqual(phone.Device, phone.PresenceDevice);
        EstateModel model = harness.Estate.Load();
        Assert.Equal(before + 2, model.Current("device").Count());
        RecordEnvelope request = model.ById[phone.Device];
        RecordEnvelope second = model.ById[phone.PresenceDevice!];
        Assert.Equal((person, person), (EstateModel.BodyString(request, "principal"), EstateModel.BodyString(second, "principal")));
        Assert.Equal(("none", "required"), (EstateModel.BodyString(request, "presence"), EstateModel.BodyString(second, "presence")));
        Assert.Equal(("file-owner-only", DeviceKeyStores.IosSecureEnclaveKind), (EstateModel.BodyString(request, "custody"), EstateModel.BodyString(second, "custody")));
        Assert.Equal(phone.PresenceDevicePublicKey, EstateModel.BodyString(second, "publicKey"));
        Assert.Equal(["local-network"], second.Body.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(phone.PresenceDevice, Assert.Single(second.Signatures).Device);

        // Both are admitted by the administrator who issued the code, from the same issuing device; the note names the pair.
        foreach (string id in new[] { phone.Device, phone.PresenceDevice! })
        {
            RecordEnvelope admission = Assert.Single(model.About(id, "admission"));
            Assert.Equal(harness.Estate.OperatorId, admission.By);
            Assert.Equal(harness.OperatorDevice, Assert.Single(admission.Signatures).Device);
            Assert.Contains(harness.OperatorDevice, EstateModel.BodyString(admission, "note"), StringComparison.Ordinal);
            Assert.Equal("admitted", harness.Host.Service.Standing(id)!.Admission);
        }
        Assert.Contains("presence device of device " + phone.Device, EstateModel.BodyString(Assert.Single(model.About(phone.PresenceDevice!, "admission")), "note"), StringComparison.Ordinal);
        Assert.Equal(0, harness.Socket.Check().Errors);

        // The presence key signed only its own record, with its owner's presence; the keys are held apart.
        Assert.Equal(1, presence.Signatures);
        Assert.True(presence.Has(phone.PresenceDevice!));
        Assert.False(presence.Has(phone.Device));
        Assert.False(harness.DeviceKeys.Has(phone.PresenceDevice!));

        // The kept connection keeps both, and its requests are still signed by the request key alone.
        RuntimeConnection kept = Assert.Single(harness.Connections.List());
        Assert.Equal((phone.PresenceDevice, phone.PresenceDevicePublicKey), (kept.PresenceDevice, kept.PresenceDevicePublicKey));
        using VericairnClient client = harness.Connections.Connect(kept);
        Assert.False((await client.StatusAsync(TestContext.Current.CancellationToken)).HasErrors);
        Assert.Equal(1, presence.Signatures);
    }

    [Fact]
    public async Task ADeclinedPromptWhilePairingPairsTheRequestKeyAloneAndKeepsNoPresenceKey()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone Person","roles":["contributor"]}""").Id;
        var presence = new PresenceKeyStandIn(harness.DeviceHome) { Decline = true };
        int before = harness.Estate.Load().Current("device").Count();
        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Phone") { PresenceKeyStore = presence });
        Assert.Null(phone.PresenceDevice);
        Assert.Equal(before + 1, harness.Estate.Load().Current("device").Count());
        Assert.Equal("none", EstateModel.BodyString(harness.Estate.Load().ById[phone.Device], "presence"));
        Assert.False(Directory.Exists(Path.Combine(harness.DeviceHome, "presence", "keys")) && Directory.EnumerateFiles(Path.Combine(harness.DeviceHome, "presence", "keys")).Any());
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task AnAgentsPairingIsOfferedNoPresenceKeyAndRecordsOneDevice()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        int before = harness.Estate.Load().Current("device").Count();
        RuntimeConnection agent = harness.Connections.Add(harness.Address, harness.Issue(harness.Estate.BuilderId).Code, new PairingOptions("Agent laptop") { PresenceKeyStore = presence });
        Assert.Null(agent.PresenceDevice);
        Assert.Equal(before + 1, harness.Estate.Load().Current("device").Count());
        Assert.Equal(0, presence.Signatures);
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task AnExistingPairingAddsItsPresenceKeyOnlyWithACodeForItsOwnPrincipal()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone Person","roles":["contributor"]}""").Id;
        string other = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Other Person","roles":["contributor"]}""").Id;
        RuntimeConnection phone = harness.Pair(person);
        Assert.Null(phone.PresenceDevice);
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        int before = harness.Estate.Load().Current("device").Count();

        // A code for someone else is refused before any key is made, and nothing is recorded.
        await Assert.ThrowsAsync<ClientRefusedException>(() => harness.Connections.AddPresenceAsync(phone, harness.Issue(other).Code, "Phone (presence key)", presence, TestContext.Current.CancellationToken));
        Assert.Equal(before, harness.Estate.Load().Current("device").Count());
        Assert.Equal(0, presence.Signatures);
        Assert.Null(Assert.Single(harness.Connections.List()).PresenceDevice);

        RuntimeConnection added = await harness.Connections.AddPresenceAsync(phone, harness.Issue(person).Code, "Phone (presence key)", presence, TestContext.Current.CancellationToken);
        Assert.Equal((phone.Id, phone.Device), (added.Id, added.Device));
        Assert.NotNull(added.PresenceDevice);
        EstateModel model = harness.Estate.Load();
        Assert.Equal(before + 1, model.Current("device").Count());
        Assert.Equal("required", EstateModel.BodyString(model.ById[added.PresenceDevice!], "presence"));
        Assert.Equal(person, EstateModel.BodyString(model.ById[added.PresenceDevice!], "principal"));
        Assert.Equal("admitted", harness.Host.Service.Standing(added.PresenceDevice!)!.Admission);
        Assert.Equal(added.PresenceDevice, Assert.Single(harness.Connections.List()).PresenceDevice);
        Assert.Equal(1, presence.Signatures);
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task CustodyThatCannotRequirePresenceClaimsNoneAndRecordsNothing()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Simulator Person","roles":["contributor"]}""").Id;
        var simulator = new FileKeyStore(Path.Combine(harness.DeviceHome, "simulator"));
        int before = harness.Estate.Load().Current("device").Count();

        ClientRefusedException asked = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            harness.Connections.AddAsync(harness.Address, harness.Issue(person).Code, new PairingOptions("Simulator") { PresenceKeyStore = simulator }, TestContext.Current.CancellationToken));
        Assert.Contains("cannot enforce", asked.Message, StringComparison.Ordinal);
        Assert.Equal(before, harness.Estate.Load().Current("device").Count());
        Assert.Empty(harness.Connections.List());

        // Paired without a presence store, as the simulator build is, the file custody states presence none.
        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Simulator"));
        Assert.Null(phone.PresenceDevice);
        Assert.Equal("none", EstateModel.BodyString(harness.Estate.Load().ById[phone.Device], "presence"));
        await Assert.ThrowsAsync<ClientRefusedException>(() => harness.Connections.AddPresenceAsync(phone, harness.Issue(person).Code, "Simulator (presence key)", simulator, TestContext.Current.CancellationToken));
        Assert.Equal(before + 1, harness.Estate.Load().Current("device").Count());
        Assert.Equal(0, harness.Socket.Check().Errors);
    }
}
