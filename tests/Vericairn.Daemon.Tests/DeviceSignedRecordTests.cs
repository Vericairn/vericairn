// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A paired device writes a record signed by its own key: the runtime prepares the exact record for that device, the device signs it,
/// and the runtime verifies the signature against that admitted device before writing. Nothing on the runtime's machine signs it, so a
/// phone's decision needs no device of its principal there, and never asks the person at that machine to approve it.
/// </summary>
public class DeviceSignedRecordTests
{
    private const string Order = """{"key":"PHONE-SIGNED","kind":"ordinary","title":"Signed on the phone","intent":"Exercise device-signed writes.","scope":["tests"],"exitQuestion":"Signed?","inputs":[],"deliverables":["evidence"]}""";

    [Fact]
    public async Task APairedDeviceSignsItsOwnRecordAndTheRecordCarriesThatDevicesSignature()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection phone = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient client = harness.Connections.Connect(phone);
        NewRecordRequest request = Request(harness);

        RecordPrepared? seen = null;
        WriteOutcome signed = await DeviceSignedRecords.WriteAsync(client, request, harness.DeviceKeys, phone.Device, prepared => { seen = prepared; return true; },
            TestContext.Current.CancellationToken);
        Assert.True(signed.Ok, signed.Problem?.Message);
        Assert.NotNull(seen);
        Assert.Equal(seen.Id, signed.Written!.Id);
        Assert.Equal(seen.Hash, signed.Written.Hash);
        Assert.Equal("PHONE-SIGNED", seen.Record.GetProperty("body").GetProperty("key").GetString());

        RecordResponse written = Assert.Single((await client.RecordsAsync("work-order", TestContext.Current.CancellationToken)), r => r.Envelope.GetProperty("id").GetString() == seen.Id);
        JsonElement signature = Assert.Single(written.Envelope.GetProperty("signatures").EnumerateArray());
        Assert.Equal(phone.Device, signature.GetProperty("device").GetString());
        Assert.Equal(0, harness.Host.Service.Check().Errors);

        // A person who declines at review signs and writes nothing.
        WriteOutcome declined = await DeviceSignedRecords.WriteAsync(client, Request(harness, "PHONE-DECLINED"), harness.DeviceKeys, phone.Device, _ => false,
            TestContext.Current.CancellationToken);
        Assert.Equal("declined", declined.Problem?.Code);
        Assert.DoesNotContain(await client.RecordsAsync("work-order", TestContext.Current.CancellationToken), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "PHONE-DECLINED");
    }

    [Fact]
    public async Task APlanIsItsOwnDevicesOnceAndAForeignWrongOrLateSignatureWritesNothing()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection first = harness.Pair(harness.Estate.BuilderId, "First phone");
        RuntimeConnection second = harness.Pair(harness.Estate.BuilderId, "Second phone");
        using VericairnClient one = harness.Connections.Connect(first);
        using VericairnClient two = harness.Connections.Connect(second);
        CancellationToken token = TestContext.Current.CancellationToken;

        // Another device of the same principal cannot sign the plan, and does not use it up.
        RecordPrepared plan = (await one.PrepareRecordAsync(Request(harness), token)).Prepared!;
        WriteOutcome foreign = await two.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, Sign(harness, second.Device, plan.Hash)), token);
        Assert.Equal("denied", foreign.Problem?.Code);

        // A signature over anything else writes nothing, and the plan is spent.
        WriteOutcome wrong = await one.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, Sign(harness, first.Device, "sha256:" + new string('0', 64))), token);
        Assert.False(wrong.Ok);
        Assert.Contains("does not verify", wrong.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome replay = await one.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, Sign(harness, first.Device, plan.Hash)), token);
        Assert.Contains("No prepared record waits", replay.Problem!.Message, StringComparison.Ordinal);

        // A plan lapses unsigned.
        var clock = new MovableTime(DateTimeOffset.UtcNow);
        harness.Host.Service.PreparedClock = clock;
        RecordPrepared late = (await one.PrepareRecordAsync(Request(harness, "PHONE-LATE"), token)).Prepared!;
        clock.Now += TimeSpan.FromMinutes(11);
        WriteOutcome lapsed = await one.NewSignedRecordAsync(new SignedRecordRequest(late.Plan, Sign(harness, first.Device, late.Hash)), token);
        Assert.Contains("lapsed", lapsed.Problem!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await one.RecordsAsync("work-order", token), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() is "PHONE-SIGNED" or "PHONE-LATE");

        // Only a device-signed request prepares: the local socket has no device to hold a plan to.
        RecordPrepareResponse local = await harness.Socket.PrepareRecordAsync(Request(harness), token);
        Assert.False(local.Ok);
    }

    [Fact]
    public async Task WhereThePersonsDeviceHereNeedsPresenceTheirPhoneStillSignsItsOwnRecord()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string op = harness.Estate.OperatorId;
        string present = harness.Estate.Add("principal", op, """{"kind":"human","name":"Present Person","roles":["contributor"]}""").Id;
        // As in F7: a file key here whose record states the enclave custody and presence a Mac with Touch ID would.
        var keys = new FileKeyStore(VericairnHome.Path());
        string id = Records.Identity.Ulid.NewUlid(harness.Estate.Clock.AddSeconds(1)).Value;
        byte[] spki = keys.Create(id);
        try
        {
            System.Text.Json.Nodes.JsonObject body = Records.Authoring.DeviceRecords.Body(present, "Laptop with Touch ID", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(spki), ["embedded"], 2, "required");
            harness.Estate.Add("device", present, body.ToJsonString(), id: id, signer: new StoreSigner(keys, id));
            Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(id, op, "Present Person's laptop.")).Ok);
            Assert.NotNull(harness.Host.Service.PresenceRefusal(present));

            RuntimeConnection phone = harness.Pair(present, "Phone");
            using VericairnClient client = harness.Connections.Connect(phone);
            using JsonDocument order = JsonDocument.Parse(Order.Replace("PHONE-SIGNED", "PHONE-PRESENT", StringComparison.Ordinal));
            var request = new NewRecordRequest("work-order", present, order.RootElement.Clone());
            Assert.Throws<HttpRequestException>(() => client.NewRecord(request));

            WriteOutcome signed = await DeviceSignedRecords.WriteAsync(client, request, harness.DeviceKeys, phone.Device, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(signed.Ok, signed.Problem?.Message);
            Assert.Equal(phone.Device, Assert.Single(harness.Estate.Load().ById[signed.Written!.Id].Signatures).Device);
            LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!));
            Assert.Equal((phone.Device, present, "POST /v1/records/signed", signed.Written.Id), (kept.Device, kept.Principal, kept.Operation, kept.Record));
            Assert.Equal(0, harness.Socket.Check().Errors);
        }
        finally
        {
            keys.Remove(id);
        }
    }

    private static NewRecordRequest Request(LocalNetworkHarness harness, string key = "PHONE-SIGNED")
    {
        using JsonDocument body = JsonDocument.Parse(Order.Replace("PHONE-SIGNED", key, StringComparison.Ordinal));
        return new NewRecordRequest("work-order", harness.Estate.BuilderId, body.RootElement.Clone());
    }

    private static string Sign(LocalNetworkHarness harness, string device, string hash) =>
        new StoreSigner(harness.DeviceKeys, device).Sign(RecordSigning.Digest(hash)).Signature;

    private sealed class MovableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
