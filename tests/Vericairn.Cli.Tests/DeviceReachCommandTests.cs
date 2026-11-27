// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DeviceReachCommandTests
{
    private const string ClaimBody = """{"kind":"service","name":"Claim","role":"Test service","location":{"url":"https://example.invalid"},"visibility":"private"}""";

    [Fact]
    public void ReachIncludesBackdatedSignaturesFirstWitnessedByALaterCheckpoint()
    {
        using var estate = new TestEstate(3);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        string device = MakeDevice(estate, estate.BuilderId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        using EstateService service = EstateService.Open(estate.Root);
        var keys = new FileKeyStore(VericairnHome.Path());
        var signer = new StoreSigner(keys, device);
        var operatorSigner = new StoreSigner(keys, operatorDevice);
        estate.SetClock(estate.Load().OfType("admission").Max(r => r.AtTime));
        RecordEnvelope earlier = Claim();
        estate.SetClock(estate.Clock.AddSeconds(10));
        RecordEnvelope adoption = Checkpoint(estate, service, operatorSigner, "adoption");
        Assert.True(SignedHistory.Of(service.Session.Load()).IsWitnessed(earlier.Id));
        DateTimeOffset cutoff = adoption.AtTime.AddSeconds(1);
        estate.SetClock(earlier.AtTime);
        RecordEnvelope later = Claim();
        DateTimeOffset stated = estate.Clock;
        estate.SetClock(cutoff.AddSeconds(-1));
        RecordEnvelope checkpoint = Checkpoint(estate, service, operatorSigner, "manual");
        estate.SetClock(stated);
        RecordEnvelope unwitnessed = Claim();
        Assert.True(SignedHistory.Of(service.Session.Load()).IsWitnessed(later.Id));
        var ledger = new PresenceSessionLedger(PresenceSessionLedger.DirectoryOf(estate.Root));
        const string session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";
        ledger.Append(new(checkpoint.At, session, "announced") { Device = device });
        ledger.Append(new(checkpoint.At, session, "signed") { Record = earlier.Id, Type = earlier.Type, Hash = earlier.ComputedHash });

        string[] args = ["--estate", estate.Root, "devices", "reach", device, "--since", checkpoint.At];
        JsonElement[] records = CliHarness.Ok([.. args, "--json"]).Json.GetProperty("records").EnumerateArray().ToArray();
        JsonElement tail = Assert.Single(records, r => r.GetProperty("id").GetString() == unwitnessed.Id);
        Assert.Equal("unwitnessed", tail.GetProperty("evidence").GetString());
        Assert.False(tail.TryGetProperty("firstWitness", out _));
        JsonElement row = Assert.Single(records, r => r.GetProperty("id").GetString() == later.Id);
        Assert.Equal(later.At, row.GetProperty("at").GetString());
        Assert.Equal("witnessed-after-cutoff", row.GetProperty("evidence").GetString());
        JsonElement witness = row.GetProperty("firstWitness");
        Assert.Equal((checkpoint.Id, checkpoint.ComputedHash, checkpoint.At),
            (witness.GetProperty("id").GetString(), witness.GetProperty("hash").GetString(), witness.GetProperty("at").GetString()));
        // Witnessed before the cutoff, but this machine's ledger logged the device signing it at the cutoff: a checkpoint pins
        // content, not signatures, so the row is listed on the ledger's time.
        Assert.Equal("ledger-time", Assert.Single(records, r => r.GetProperty("id").GetString() == earlier.Id).GetProperty("evidence").GetString());
        string text = CliHarness.Ok(args).Out;
        Assert.Contains($"stated at {later.At}", text, StringComparison.Ordinal);
        Assert.Contains($"witnessed by checkpoint {checkpoint.Id} at {checkpoint.At}", text, StringComparison.Ordinal);
        Assert.Contains("witnessed after the cutoff", Assert.Single(text.Split('\n'), line => line.Contains(later.Id, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("unwitnessed", Assert.Single(text.Split('\n'), line => line.Contains(unwitnessed.Id, StringComparison.Ordinal)), StringComparison.Ordinal);

        RecordEnvelope Claim() => estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
    }

    [Fact]
    public void ReachIncludesABackdatedSignatureWithOnlyASuspectCheckpoint()
    {
        using var estate = new TestEstate(3);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        string device = MakeDevice(estate, estate.BuilderId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        using EstateService service = EstateService.Open(estate.Root);
        var keys = new FileKeyStore(VericairnHome.Path());
        var signer = new StoreSigner(keys, device);
        var operatorSigner = new StoreSigner(keys, operatorDevice);
        estate.SetClock(estate.Load().OfType("admission").Max(r => r.AtTime));
        Checkpoint(estate, service, operatorSigner, "adoption");
        RecordEnvelope backdated = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        RecordEnvelope suspect = Checkpoint(estate, service, signer, "manual", by: estate.BuilderId);
        string body = estate.Load().ById[device].Body.GetRawText().Replace("Test device", "Renamed device", StringComparison.Ordinal);
        RecordEnvelope revision = estate.Add("device", estate.BuilderId, body, supersedes: device, signer: signer);
        var model = service.Session.Load();
        Assert.True(model.Counts(suspect));
        Assert.True(SignedHistory.Of(model).IsWitnessed(backdated.Id));
        DateTimeOffset cutoff = suspect.AtTime.AddSeconds(2);

        JsonElement row = Assert.Single(RecordsSince(cutoff), r => r.GetProperty("id").GetString() == backdated.Id);
        Assert.Equal("unwitnessed", row.GetProperty("evidence").GetString());
        Assert.False(row.TryGetProperty("firstWitness", out _));

        estate.SetClock(cutoff);
        RecordEnvelope independent = Checkpoint(estate, service, operatorSigner, "manual");
        row = Assert.Single(RecordsSince(cutoff), r => r.GetProperty("id").GetString() == backdated.Id);
        Assert.Equal("witnessed-after-cutoff", row.GetProperty("evidence").GetString());
        JsonElement witness = row.GetProperty("firstWitness");
        Assert.Equal((independent.Id, independent.ComputedHash, independent.At),
            (witness.GetProperty("id").GetString(), witness.GetProperty("hash").GetString(), witness.GetProperty("at").GetString()));
        DateTimeOffset afterWitness = independent.AtTime.AddSeconds(1);
        Assert.DoesNotContain(RecordsSince(afterWitness), r => r.GetProperty("id").GetString() == backdated.Id);

        using var changedBody = JsonDocument.Parse(ClaimBody.Replace("Claim", "Changed claim", StringComparison.Ordinal));
        AuthoredRecord changed = RecordAuthor.Create(new RecordDraft(backdated.Type, backdated.By, changedBody.RootElement,
            null, null, backdated.AtTime, backdated.Id, 3), [signer]);
        File.WriteAllBytes(Path.Combine(estate.Root, backdated.ExpectedPath), changed.Bytes);
        var ledger = new PresenceSessionLedger(PresenceSessionLedger.DirectoryOf(estate.Root));
        const string session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";
        string at = RecordParser.FormatTimestamp(afterWitness);
        ledger.Append(new(at, session, "announced") { Device = device });
        ledger.Append(new(at, session, "signed") { Record = backdated.Id, Type = backdated.Type, Hash = backdated.ComputedHash });
        JsonElement[] rows = [.. RecordsSince(afterWitness).Where(r => r.GetProperty("id").GetString() == backdated.Id)];
        row = Assert.Single(rows, r => r.GetProperty("hash").GetString() == changed.Envelope.ComputedHash);
        Assert.Equal("unwitnessed", row.GetProperty("evidence").GetString());
        // The ledger logged the device signing the earlier content after the cutoff, which a checkpoint witnessed before it.
        JsonElement signedLate = Assert.Single(rows, r => r.GetProperty("hash").GetString() == backdated.ComputedHash);
        Assert.Equal(("presence-ledger", "ledger-time"), (signedLate.GetProperty("source").GetString(), signedLate.GetProperty("evidence").GetString()));

        JsonElement.ArrayEnumerator RecordsSince(DateTimeOffset since) =>
            CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", revision.Id, "--since", RecordParser.FormatTimestamp(since))
                .Json.GetProperty("records").EnumerateArray();
    }

    [Fact]
    public void ReachIncludesABackdatedSignatureInAnEstateWithoutCheckpoints()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), device);
        RecordEnvelope backdated = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        string cutoff = RecordParser.FormatTimestamp(backdated.AtTime.AddSeconds(1));
        RecordEnvelope atCutoff = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);

        string[] args = ["--estate", estate.Root, "devices", "reach", device, "--since", cutoff];
        JsonElement[] records = CliHarness.Ok([.. args, "--json"]).Json.GetProperty("records").EnumerateArray().ToArray();
        JsonElement tail = Assert.Single(records, r => r.GetProperty("id").GetString() == backdated.Id);
        Assert.Equal("unwitnessed", tail.GetProperty("evidence").GetString());
        Assert.False(tail.TryGetProperty("firstWitness", out _));
        JsonElement claimed = Assert.Single(records, r => r.GetProperty("id").GetString() == atCutoff.Id);
        Assert.Equal("claimed-time", claimed.GetProperty("evidence").GetString());
        string text = CliHarness.Ok(args).Out;
        Assert.Contains("listed on the record's own claimed time",
            Assert.Single(text.Split('\n'), line => line.Contains(atCutoff.Id, StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void ReachIncludesBackdatedSignaturesOutsideTheSignedDeviceClosing()
    {
        using var estate = new TestEstate(3);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        string device = MakeDevice(estate, estate.BuilderId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        using EstateService service = EstateService.Open(estate.Root);
        var keys = new FileKeyStore(VericairnHome.Path());
        var signer = new StoreSigner(keys, device);
        var operatorSigner = new StoreSigner(keys, operatorDevice);
        estate.SetClock(estate.Load().OfType("admission").Max(r => r.AtTime));
        RecordEnvelope earlier = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        estate.SetClock(estate.Clock.AddSeconds(10));
        Checkpoint(estate, service, operatorSigner, "adoption");
        RecordEnvelope closing = Checkpoint(estate, service, operatorSigner, "closing", ("device", device));
        estate.SetClock(earlier.AtTime);
        RecordEnvelope outside = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        Assert.Equal(closing.Id, Assert.Single(estate.Load().Trust.Closings).Record.Id);

        JsonElement[] records = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", device, "--since", closing.At)
            .Json.GetProperty("records").EnumerateArray().ToArray();
        JsonElement row = Assert.Single(records, r => r.GetProperty("id").GetString() == outside.Id);
        Assert.Equal(outside.At, row.GetProperty("at").GetString());
        Assert.Equal("unwitnessed", row.GetProperty("evidence").GetString());
        Assert.False(row.TryGetProperty("firstWitness", out _));
        Assert.Equal(closing.Id, row.GetProperty("outsideClosing").GetProperty("id").GetString());
        Assert.DoesNotContain(records, r => r.GetProperty("id").GetString() == earlier.Id);
    }

    [Fact]
    public void ReachReportsVerifiedSignaturesByIdentityAndExactSessionEvidence()
    {
        using var estate = new TestEstate(3);
        string home = CliHarness.TempDirectory("reach-keys");
        try
        {
            var keys = new FileKeyStore(home);
            string id = Ulid.NewUlid(estate.Clock).Value;
            var signer = new StoreSigner(keys, id);
            string publicKey = RecordSigning.EncodePublicKey(keys.Create(id));
            string body = DeviceRecords.Body(estate.OperatorId, "Device", "macos", "file", publicKey, ["embedded"], 3).ToJsonString();
            estate.Add("device", estate.OperatorId, body, id: id, signer: signer);
            RecordEnvelope old = Claim(signer);
            string revision = Ulid.NewUlid(estate.Clock).Value;
            estate.Add("device", estate.OperatorId, body.Replace("Device", "Renamed", StringComparison.Ordinal), id: revision, supersedes: id, signer: signer);
            RecordEnvelope signed = Claim(new StoreSigner(keys, id, revision));
            RecordEnvelope current = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Renamed claim","roles":[]}""", supersedes: signed.Id, signer: signer);
            RecordEnvelope unsigned = Claim(null);
            string other = Ulid.NewUlid(estate.Clock).Value;
            string fakeKey = RecordSigning.EncodePublicKey(keys.Create(other));
            RecordEnvelope forged = Claim(new StoreSigner(keys, other, id));
            estate.Add("device", estate.OperatorId, body.Replace(publicKey, fakeKey, StringComparison.Ordinal),
                supersedes: id, signer: new StoreSigner(keys, other, id));
            var ledger = new PresenceSessionLedger(PresenceSessionLedger.DirectoryOf(estate.Root));
            const string session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";
            const string unrelated = "01J9Z3K4M5N6P7Q8R9S0T1V2W6";
            const string terms = """{"entries":[{"kind":"exact"}]}""";
            string ledgerOnly = Ulid.NewUlid(estate.Clock).Value;
            ledger.Append(new(signed.At, session, "announced") { Device = id, Terms = terms, Named = "Test action" });
            ledger.Append(new(signed.At, session, "signed") { Record = signed.Id, Type = signed.Type, Hash = signed.ComputedHash });
            ledger.Append(new(current.At, session, "signed") { Record = ledgerOnly, Type = "principal", Hash = unsigned.ComputedHash });
            ledger.Append(new(current.At, unrelated, "announced") { Device = other, Terms = terms, Named = "Other device" });
            ledger.Append(new(current.At, unrelated, "signed") { Record = current.Id, Type = current.Type, Hash = current.ComputedHash });

            string[] args = ["--estate", estate.Root, "devices", "reach", revision, "--since", signed.At];
            JsonElement json = CliHarness.Ok([.. args, "--json"]).Json;
            Assert.Equal(id, json.GetProperty("device").GetString());
            JsonElement[] rows = json.GetProperty("records").EnumerateArray().ToArray();
            Assert.Equal([id, old.Id, revision, signed.Id, current.Id, ledgerOnly], rows.Select(r => r.GetProperty("id").GetString()));
            Assert.Equal("presence-ledger", rows[^1].GetProperty("source").GetString());
            Assert.Equal("unwitnessed", rows[1].GetProperty("evidence").GetString());
            Assert.Equal("unwitnessed", rows[^1].GetProperty("evidence").GetString());
            Assert.Equal(terms, Assert.Single(json.GetProperty("sessions").EnumerateArray()).GetProperty("terms").GetString());
            Assert.Equal(session, Assert.Single(rows[3].GetProperty("sessions").EnumerateArray()).GetString());
            Assert.Empty(rows[4].GetProperty("sessions").EnumerateArray());

            string text = CliHarness.Ok(args).Out;
            Assert.Contains("unwitnessed", Assert.Single(text.Split('\n'), line => line.Contains(old.Id, StringComparison.Ordinal)), StringComparison.Ordinal);
            Assert.Contains(signed.Id, text, StringComparison.Ordinal);
            Assert.Contains(current.Id, text, StringComparison.Ordinal);
            Assert.Contains(terms, text, StringComparison.Ordinal);
            foreach (string absent in new[] { forged.Id, unsigned.Id, unrelated })
                Assert.DoesNotContain(absent, text, StringComparison.Ordinal);

            RecordEnvelope Claim(IRecordSigner? claimSigner) =>
                estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Claim","roles":[]}""", signer: claimSigner);
        }
        finally
        {
            CliHarness.Delete(home, PresenceSessionLedger.DirectoryOf(estate.Root));
        }
    }

    [Fact]
    public void ReachKeepsTheStatedTimeAndMatchesLaterLedgerTimesByExactHash()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), device);
        RecordEnvelope signed = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        RecordEnvelope unrelated = estate.Add("resource", estate.BuilderId, ClaimBody, signer: signer);
        string wrongHash = estate.Builder.ComputedHash;
        string directory = PresenceSessionLedger.DirectoryOf(estate.Root);
        var ledger = new PresenceSessionLedger(directory);
        const string session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";
        string cutoff = RecordParser.FormatTimestamp(estate.Clock.AddMinutes(1));
        ledger.Append(new(signed.At, session, "announced") { Device = device });
        ledger.Append(new(cutoff, session, "signed") { Record = signed.Id, Type = signed.Type, Hash = signed.ComputedHash });
        ledger.Append(new(cutoff, session, "signed") { Record = unrelated.Id, Type = unrelated.Type, Hash = wrongHash });

        string[] args = ["--estate", estate.Root, "devices", "reach", device, "--since", cutoff];
        JsonElement[] records = CliHarness.Ok([.. args, "--json"]).Json.GetProperty("records").EnumerateArray().ToArray();
        JsonElement exact = Assert.Single(records, r => r.GetProperty("source").GetString() == "record-signature" && r.GetProperty("id").GetString() == signed.Id);
        Assert.Equal(["at", "evidence", "hash", "id", "ledgerSignedAt", "path", "sessions", "source", "type"], exact.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(signed.At, exact.GetProperty("at").GetString());
        Assert.Equal(cutoff, Assert.Single(exact.GetProperty("ledgerSignedAt").EnumerateArray()).GetString());
        JsonElement original = Assert.Single(records, r => r.GetProperty("id").GetString() == unrelated.Id && r.GetProperty("hash").GetString() == unrelated.ComputedHash);
        Assert.Equal("unwitnessed", original.GetProperty("evidence").GetString());
        Assert.Empty(original.GetProperty("ledgerSignedAt").EnumerateArray());
        JsonElement mismatch = Assert.Single(records, r => r.GetProperty("id").GetString() == unrelated.Id && r.GetProperty("hash").GetString() == wrongHash);
        Assert.Equal(("presence-ledger", wrongHash, cutoff), (mismatch.GetProperty("source").GetString(), mismatch.GetProperty("hash").GetString(), mismatch.GetProperty("at").GetString()));
        Assert.Contains($"ledger signing times {cutoff}", CliHarness.Ok(args).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ReachReportsOldAndNewKeysAndASignatureOnAnUnreadableLine()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        var keys = new FileKeyStore(VericairnHome.Path());
        estate.SetClock(estate.Load().OfType("admission").Max(r => r.AtTime));
        RecordEnvelope earlier = estate.Add("resource", estate.BuilderId, ClaimBody, signer: new StoreSigner(keys, device));
        string rotation = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(keys.Create(rotation));
        RecordEnvelope initial = estate.Load().ById[device];
        string initialKey = initial.Body.GetProperty("publicKey").GetString()!;
        RecordEnvelope rotated = estate.Add("device", estate.BuilderId, initial.Body.GetRawText().Replace(initialKey, publicKey, StringComparison.Ordinal),
            id: rotation, supersedes: device, signers: [new StoreSigner(keys, device), new StoreSigner(keys, rotation, device)]);
        Assert.Equal(2, rotated.Signatures.Count);
        Assert.Contains(estate.Load().Trust.DeviceChain(device), r => r.Id == rotation);
        RecordEnvelope later = estate.Add("resource", estate.BuilderId, ClaimBody,
            signers: [new StoreSigner(keys, operatorDevice), new StoreSigner(keys, rotation, device)]);
        string copy = later.ExpectedPath.Replace(".json", "-copy.json", StringComparison.Ordinal);
        estate.WriteFile(copy, File.ReadAllText(Path.Combine(estate.Root, later.ExpectedPath)));
        Assert.Contains(estate.Load().Unreadable, r => r.Id == later.Id);

        JsonElement report = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", rotation, "--since", "2020-01-01T00:00:00Z").Json;
        string?[] seen = report.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToArray();
        Assert.Contains(earlier.Id, seen);
        Assert.Contains(rotation, seen);
        Assert.Contains(later.Id, seen);
    }

    [Fact]
    public void RevokeReportsTheWritingRuntimeAndReachReadsTheExactRevocation()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        JsonElement revoked = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "revoke", device, "--by", estate.OperatorId, "--reason", "Suspected compromise.").Json;
        string revocation = revoked.GetProperty("id").GetString()!;
        string hash = revoked.GetProperty("hash").GetString()!;
        JsonElement observed = Assert.Single(revoked.GetProperty("revocationReach").EnumerateArray());
        Assert.Equal(("runtime", "written"), (observed.GetProperty("kind").GetString(), observed.GetProperty("evidence").GetString()));

        string[] args = ["--estate", estate.Root, "devices", "reach", device, "--since", "2019-12-31T19:00:00-05:00"];
        JsonElement report = CliHarness.Ok([.. args, "--json"]).Json;
        Assert.Equal(["device", "records", "revocations", "sessions", "since"], report.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("2020-01-01T00:00:00Z", report.GetProperty("since").GetString());
        JsonElement read = Assert.Single(report.GetProperty("revocations").EnumerateArray());
        Assert.Equal(["at", "counts", "current", "hash", "id", "observations", "revokedAt"], read.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal((revocation, hash, true, true), (read.GetProperty("id").GetString(), read.GetProperty("hash").GetString(),
            read.GetProperty("counts").GetBoolean(), read.GetProperty("current").GetBoolean()));
        JsonElement observation = Assert.Single(read.GetProperty("observations").EnumerateArray());
        Assert.Equal(("runtime", "record-read"), (observation.GetProperty("kind").GetString(), observation.GetProperty("evidence").GetString()));
        Assert.Equal(observed.GetProperty("identity").GetString(), observation.GetProperty("identity").GetString());
        string text = CliHarness.Ok(args).Out;
        Assert.Contains($"revocation {revocation}  {hash}", text, StringComparison.Ordinal);
        Assert.Contains("counts  current admission revision", text, StringComparison.Ordinal);
        Assert.Contains($"    stated effective time {read.GetProperty("revokedAt").GetString()}", text.Split('\n').Select(line => line.TrimEnd('\r')));
        Assert.Contains("Absence of an observation leaves that observer's status unknown", text, StringComparison.Ordinal);
        RecordEnvelope rejected = estate.Add("admission", estate.OperatorId,
            $$"""{"principal":"{{estate.BuilderId}}","status":"revoked","note":"Unsigned fixture."}""",
            subject: estate.Load().ById[device].Reference, supersedes: revocation);
        JsonElement ignored = Assert.Single(CliHarness.Ok([.. args, "--json"]).Json.GetProperty("revocations").EnumerateArray(),
            r => r.GetProperty("id").GetString() == rejected.Id);
        Assert.False(ignored.GetProperty("counts").GetBoolean());
        Assert.False(ignored.GetProperty("current").GetBoolean());
        Assert.Contains("does not count", CliHarness.Ok(args).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void RevokeTextNamesTheProcessThatWroteTheRecord()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        string output = CliHarness.Ok("--estate", estate.Root, "devices", "revoke", device, "--by", estate.OperatorId, "--reason", "Suspected compromise.").Out;
        string runtime = Assert.Single(output.Split('\n'), line => line.StartsWith("  runtime ", StringComparison.Ordinal));
        Assert.Contains("  written  ", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void ReachRefusesAnInvalidCutoffAndAnUnknownDevice()
    {
        using var estate = new TestEstate(3);
        CliHarness.Outcome invalid = CliHarness.Run("--estate", estate.Root, "devices", "reach", estate.BuilderId, "--since", "2020-01-01");
        Assert.Equal(2, invalid.Code);
        Assert.Contains("--since requires an ISO 8601 timestamp with seconds", invalid.Err, StringComparison.Ordinal);
        CliHarness.Outcome unknown = CliHarness.Run("--estate", estate.Root, "devices", "reach", estate.BuilderId, "--since", "2020-01-01T00:00:00Z");
        Assert.Equal(2, unknown.Code);
        Assert.Contains("is not a device of this estate", unknown.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ReachNamesTheDaemonJournalThatObservedTheExactRevocation()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        string directory = PresenceSessionLedger.DirectoryOf(estate.Root);
        RecordWritten revocation;
        using (EstateService observer = EstateService.Open(estate.Root))
        {
            observer.EnableEvents(directory);
            observer.DeriveEvents();
            revocation = CliHarness.RevokeAfterRecords(estate.Root, new(device, estate.OperatorId, "Suspected compromise."));
            observer.DeriveEvents();
            Assert.Equal(observer.RevocationJournalIdentity, ReadJournal().GetProperty("identity").GetString());
        }

        using (EventJournal journal = EventJournal.Open(Path.Combine(directory, EventJournal.FileName)))
            journal.Commit("2026-01-01T00:00:00Z", [new(estate.Builder.ComputedHash, "record", "written",
                new("admission", revocation.Id, estate.Builder.ComputedHash), [], null, null, false, [])], [], [], [], []);
        string[] before = Directory.GetFiles(directory).Order().ToArray();
        JsonElement found = ReadJournal();
        Assert.Equal("journal-found", found.GetProperty("evidence").GetString());
        Assert.NotEmpty(found.GetProperty("identity").GetString()!);
        Assert.Equal(before, Directory.GetFiles(directory).Order());

        JsonElement ReadJournal()
        {
            JsonElement revoked = Assert.Single(CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", device,
                "--since", "2020-01-01T00:00:00Z").Json.GetProperty("revocations").EnumerateArray());
            return Assert.Single(revoked.GetProperty("observations").EnumerateArray(), o => o.GetProperty("kind").GetString() == "server");
        }
    }

    [Fact]
    public async Task RevokeReportsTheServerThatServedTheWrite()
    {
        CliHarness.RequireDaemonTransport(tls: false);
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        host.Service.DeriveEvents();

        string[] args = ["--estate", estate.Root, "devices", "revoke", device, "--by", estate.OperatorId, "--reason", "Suspected compromise."];
        JsonElement[] observers = CliHarness.Ok([.. args, "--json"]).Json.GetProperty("revocationReach").EnumerateArray().ToArray();
        Assert.Equal(["runtime", "server"], observers.Select(o => o.GetProperty("kind").GetString()));
        Assert.Equal(["written", "write-served"], observers.Select(o => o.GetProperty("evidence").GetString()));
        Assert.NotEqual(observers[0].GetProperty("identity").GetString(), observers[1].GetProperty("identity").GetString());
        host.Service.DeriveEvents();
        JsonElement revocation = Assert.Single(CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", device,
            "--since", "2020-01-01T00:00:00Z").Json.GetProperty("revocations").EnumerateArray());
        JsonElement[] serving = revocation.GetProperty("observations").EnumerateArray().Where(o => o.GetProperty("kind").GetString() == "server").ToArray();
        JsonElement live = Assert.Single(serving, o => o.GetProperty("evidence").GetString() == "server-read");
        // The daemon wrote the revocation itself, so its journal names it as written, by the guide's definitions.
        JsonElement retained = Assert.Single(serving, o => o.GetProperty("evidence").GetString() == "journal-written");
        Assert.Equal(observers[1].GetProperty("identity").GetString(), live.GetProperty("identity").GetString());
        Assert.Equal(live.GetProperty("identity").GetString(), retained.GetProperty("identity").GetString());
    }

    private static string MakeDevice(TestEstate estate, string principal) =>
        CliHarness.Ok("--estate", estate.Root, "--json", "devices", "create", "--principal", principal, "--name", "Test device", "--custody", "file").Id;

    private static RecordEnvelope Checkpoint(TestEstate estate, EstateService service, IRecordSigner signer, string trigger,
        (string Kind, string Id)? closes = null, string? by = null)
    {
        var model = service.Session.Load();
        var anchors = trigger == "adoption" ? SignedHistory.PlanAdoption(model).Anchored : [];
        CheckpointDraft draft = SignedHistory.Prepare(model, trigger, anchors, closes: closes);
        foreach (byte[] inventory in draft.Objects) service.Session.Objects.Put(inventory);
        return estate.Add("history-checkpoint", by ?? estate.OperatorId, draft.Body.GetRawText(), signer: signer);
    }
}
