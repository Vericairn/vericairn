// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DeviceReachCommandTests
{
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
            Assert.Equal([signed.Id, current.Id, ledgerOnly], rows.Select(r => r.GetProperty("id").GetString()));
            Assert.Equal("presence-ledger", rows[^1].GetProperty("source").GetString());
            Assert.Equal(terms, Assert.Single(json.GetProperty("sessions").EnumerateArray()).GetProperty("terms").GetString());
            Assert.Equal(session, Assert.Single(rows[0].GetProperty("sessions").EnumerateArray()).GetString());
            Assert.Empty(rows[1].GetProperty("sessions").EnumerateArray());

            string text = CliHarness.Ok(args).Out;
            Assert.Contains(signed.Id, text, StringComparison.Ordinal);
            Assert.Contains(current.Id, text, StringComparison.Ordinal);
            Assert.Contains(terms, text, StringComparison.Ordinal);
            foreach (string absent in new[] { old.Id, forged.Id, unsigned.Id, unrelated })
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

        JsonElement read = Assert.Single(CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", device,
            "--since", "2020-01-01T00:00:00Z").Json.GetProperty("revocations").EnumerateArray());
        Assert.Equal((revocation, hash, true, true), (read.GetProperty("id").GetString(), read.GetProperty("hash").GetString(),
            read.GetProperty("counts").GetBoolean(), read.GetProperty("current").GetBoolean()));
        JsonElement observation = Assert.Single(read.GetProperty("observations").EnumerateArray());
        Assert.Equal(("runtime", "record-read"), (observation.GetProperty("kind").GetString(), observation.GetProperty("evidence").GetString()));
    }

    [Fact]
    public void ReachNamesTheDaemonJournalThatObservedTheExactRevocation()
    {
        using var estate = new TestEstate(3);
        string device = MakeDevice(estate, estate.BuilderId);
        string operatorDevice = MakeDevice(estate, estate.OperatorId);
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", operatorDevice, "--by", estate.OperatorId, "--note", "First authority.");
        CliHarness.Ok("--estate", estate.Root, "devices", "admit", device, "--by", estate.OperatorId, "--note", "Builder authority.");
        using (EstateService observer = EstateService.Open(estate.Root))
        {
            observer.EnableEvents(PresenceSessionLedger.DirectoryOf(estate.Root));
            observer.DeriveEvents();
            _ = CliHarness.RevokeAfterRecords(estate.Root, new(device, estate.OperatorId, "Suspected compromise."));
            observer.DeriveEvents();
        }

        JsonElement revoked = Assert.Single(CliHarness.Ok("--estate", estate.Root, "--json", "devices", "reach", device,
            "--since", "2020-01-01T00:00:00Z").Json.GetProperty("revocations").EnumerateArray());
        JsonElement journal = Assert.Single(revoked.GetProperty("observations").EnumerateArray(), o => o.GetProperty("kind").GetString() == "server");
        Assert.Equal("journal-found", journal.GetProperty("evidence").GetString());
        Assert.NotEmpty(journal.GetProperty("identity").GetString()!);
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

        string[] args = ["--estate", estate.Root, "devices", "revoke", device, "--by", estate.OperatorId, "--reason", "Suspected compromise."];
        JsonElement[] observers = CliHarness.Ok([.. args, "--json"]).Json.GetProperty("revocationReach").EnumerateArray().ToArray();
        Assert.Equal(["runtime", "server"], observers.Select(o => o.GetProperty("kind").GetString()));
        Assert.Equal(["written", "write-served"], observers.Select(o => o.GetProperty("evidence").GetString()));
        Assert.NotEqual(observers[0].GetProperty("identity").GetString(), observers[1].GetProperty("identity").GetString());
    }

    private static string MakeDevice(TestEstate estate, string principal) =>
        CliHarness.Ok("--estate", estate.Root, "--json", "devices", "create", "--principal", principal, "--name", "Test device", "--custody", "file").Id;
}
