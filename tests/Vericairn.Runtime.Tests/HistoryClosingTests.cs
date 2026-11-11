// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Closings written with their acts (design/signed-history.md § 8; OPS-07): in an estate that has adopted history checkpoints, a
/// revocation, a key rotation and a narrowing grant revision are stored after their closing in one batch, signed by the key that
/// performs the act; an act that cannot carry its closing is refused with nothing written; an estate that has not adopted writes them
/// as before. Also adoption's listing of what the at-time reading changes, and the adoption cases the re-check named.
/// </summary>
public class HistoryClosingTests
{
    private const string Passphrase = "a recovery passphrase";

    [Fact]
    public void RevokingADeviceStoresItsClosingFirstInTheSameBatchSignedByTheRevoker()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "The build machine was lost."));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.Equal(("closing", null), (revoked.Checkpoint!.Trigger, revoked.Checkpoint.Skipped));

        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        RecordEnvelope closing = model.ById[revoked.Checkpoint.Written!.Id];
        RecordEnvelope revocation = model.ById[revoked.Written!.Id];
        Assert.Equal(("device", f.BuilderDevice), CheckpointBody.ReadCloses(closing.Body));
        Assert.Equal("closing", closing.Body.GetProperty("trigger").GetString());
        Assert.Equal(op, closing.By);
        Assert.Equal(f.OperatorDevice, Assert.Single(closing.Signatures).Device);
        Assert.True(string.CompareOrdinal(closing.At, EstateModel.BodyString(revocation, "revokedAt")) <= 0);
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id && c.Kind == "device");
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-unfollowed");
        Assert.False(SignatureChecks.IsAdmitted(model, model.ById[f.BuilderDevice]));
        // The closing follows the heads and witnesses what came before it; the revocation after it is the act it precedes.
        Assert.Equal([closing.Id], SignedHistory.Of(model).Heads.Select(h => h.Id));
        Assert.Contains(revocation.Id, SignedHistory.TailRecords(model).Select(r => r.Id));
        Assert.DoesNotContain(f.BuilderDevice, SignedHistory.TailRecords(model).Select(r => r.Id));
    }

    [Fact]
    public void TheOperatorsRecoveryDeviceSignsTheClosingOfARevocationItSignsAndItsAdmissionsCarryNone()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        string key = Path.Combine(Path.GetTempPath(), "vericairn-closing-recovery-" + Ulid.NewUlid().Value + ".key");
        string? replacement = null;
        try
        {
            WriteOutcome recovery = f.Api.DeviceRecovery(new DeviceRecoveryRequest(op, "Recovery key", key, Passphrase));
            Assert.True(recovery.Ok, Describe(recovery));
            string recoveryDevice = recovery.Written!.Id;

            // A replacement admitted with the recovery key closes nothing, so nothing is written with it.
            WriteOutcome created = f.Api.DeviceCreate(new(f.Records.Estate.BuilderId, "Replacement build machine", "file"));
            Assert.True(created.Ok, Describe(created));
            replacement = created.Written!.Id;
            WriteOutcome admitted = f.Api.DeviceAdmit(new AdmitRequest(replacement, op, "Admitted with the recovery key.", key, Passphrase));
            Assert.True(admitted.Ok, Describe(admitted));
            Assert.Null(admitted.Checkpoint);

            // The lost machine is revoked with the recovery key, which signs the closing before it in the same batch.
            WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Lost with every other device.", key, Passphrase));
            Assert.True(revoked.Ok, Describe(revoked));
            EstateModel model = f.Api.Session.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
            RecordEnvelope closing = model.ById[revoked.Checkpoint!.Written!.Id];
            Assert.Equal(("device", f.BuilderDevice), CheckpointBody.ReadCloses(closing.Body));
            Assert.Equal(recoveryDevice, Assert.Single(closing.Signatures).Device);
            Assert.Equal(recoveryDevice, Assert.Single(model.ById[revoked.Written!.Id].Signatures).Device);
            Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id);
            Assert.False(SignatureChecks.IsAdmitted(model, model.ById[f.BuilderDevice]));
            Assert.True(SignatureChecks.IsAdmitted(model, model.ById[replacement]));
        }
        finally
        {
            if (File.Exists(key)) File.Delete(key);
            var store = new FileKeyStore(VericairnHome.Path());
            if (replacement is not null && store.Has(replacement)) store.Remove(replacement);
        }
    }

    [Fact]
    public void RotatingAKeyStoresTheClosingOfTheReplacedKeySignedByThatKey()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string builder = f.Records.Estate.BuilderId;
        WriteOutcome rotated = f.Api.DeviceRotate(new DeviceRotateRequest(f.BuilderDevice));
        Assert.True(rotated.Ok, Describe(rotated));
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        RecordEnvelope closing = model.ById[rotated.Checkpoint!.Written!.Id];
        Assert.Equal(("key", f.BuilderDevice), CheckpointBody.ReadCloses(closing.Body));
        Assert.Equal(builder, closing.By);
        Assert.Equal(f.BuilderDevice, Assert.Single(closing.Signatures).Device);
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id && c.Kind == "key" && c.Rotation == rotated.Written!.Id);

        // The device signs with its new key from then on, and what it signs counts.
        WriteOutcome order = f.Api.NewRecord(new NewRecordRequest("work-order", builder, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("ROT-1"))!)));
        Assert.True(order.Ok, Describe(order));
        Assert.True(f.Api.Session.Load().Counts(f.Api.Session.Load().ById[order.Written!.Id]));
    }

    [Fact]
    public void NarrowingAGrantStoresItsClosingSignedByTheOperatorAndWideningStoresNone()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering, Scopes.AcceptWork));

        WriteOutcome narrowed = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering)), Supersedes: grant.Id));
        Assert.True(narrowed.Ok, Describe(narrowed));
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        RecordEnvelope closing = model.ById[narrowed.Checkpoint!.Written!.Id];
        Assert.Equal(("grant", grant.Id), CheckpointBody.ReadCloses(closing.Body));
        Assert.Equal(f.OperatorDevice, Assert.Single(closing.Signatures).Device);
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id && c.Kind == "grant");

        int checkpoints = Checkpoints(f).Count;
        WriteOutcome widened = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering, Scopes.AcceptWork, Scopes.Qualify)), Supersedes: narrowed.Written!.Id));
        Assert.True(widened.Ok, Describe(widened));
        Assert.NotEqual("closing", widened.Checkpoint?.Trigger);
        Assert.Equal(checkpoints, Checkpoints(f).Count);

        // Adding an expiry narrows it too.
        JsonObject expiring = Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering, Scopes.AcceptWork, Scopes.Qualify);
        expiring["expires"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(30));
        WriteOutcome expires = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(expiring), Supersedes: widened.Written!.Id));
        Assert.True(expires.Ok, Describe(expires));
        Assert.Equal("closing", expires.Checkpoint!.Trigger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANarrowingOnTopOfAGrantRevisionThatDoesNotCountIsJudgedWithTheClosingOfTheGrantItReplaces(bool unsignedBelow)
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering, Scopes.AcceptWork));
        if (unsignedBelow)
        {
            JsonObject required = Charter(f, "Exercise the record format.");
            required["policies"]!["signatures"] = "required";
            f.Records.Add("estate", required, supersedes: f.Records.Estate.Estate.Id);
        }

        f.Adopt();
        // Unsigned, or dated before the grant it supersedes: it counts for nothing, and its error holds every write until it is settled.
        if (!unsignedBelow) f.Records.Estate.SetClock(grant.AtTime.AddMinutes(-5));
        RecordEnvelope below = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering), supersedes: grant.Id, sign: !unsignedBelow);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Counts(model.ById[below.Id]));
        int checkpoints = Checkpoints(f).Count;

        // Compared with the revision below it, it changes nothing; compared with the grant it replaces, it drops a scope. So it is written
        // after a closing of that grant, and the batch is judged with it.
        WriteOutcome narrowed = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, builder, Scopes.LocalEngineering)), Supersedes: below.Id));
        Assert.False(narrowed.Ok);
        Assert.Contains($"a checkpoint closing grant {grant.Id},", narrowed.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("The act and its closing were judged together, and neither was written.", narrowed.Problem.Message, StringComparison.Ordinal);
        Assert.Contains(narrowed.Problem.Findings, x => x.Location == below.ExpectedPath);
        Assert.Equal(checkpoints, Checkpoints(f).Count);
    }

    [Fact]
    public void AnEstateThatHasNotAdoptedRotatesNarrowsAndRevokesWithoutAClosing()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering, Scopes.AcceptWork));

        WriteOutcome rotated = f.Api.DeviceRotate(new DeviceRotateRequest(f.BuilderDevice));
        WriteOutcome narrowed = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, f.Records.Estate.BuilderId, Scopes.LocalEngineering)), Supersedes: grant.Id));
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Retired."));
        foreach (WriteOutcome outcome in new[] { rotated, narrowed, revoked })
        {
            Assert.True(outcome.Ok, Describe(outcome));
            Assert.Null(outcome.Checkpoint);
        }

        EstateModel model = f.Api.Session.Load();
        Assert.Empty(model.OfType("history-checkpoint"));
        Assert.False(SignedHistory.Adopted(model));
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
    }

    [Fact]
    public void ARevocationWhoseClosingWouldNotActIsRefusedWithNothingWritten()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string builder = f.Records.Estate.BuilderId;
        // The builder, an agent, holds administer, so it may revoke a device; but only a person or an operator closes one.
        f.Records.Add("authority", Grant(f, builder, Scopes.Administer));
        string target = f.HeldDevice(f.Records.Estate.OperatorId);
        int records = f.Api.Session.Load().Records.Count;
        int objects = f.Api.Session.Objects.List().Count;

        WriteOutcome refused = f.Api.DeviceRevoke(new RevokeRequest(target, builder, "An agent revokes."));
        Assert.False(refused.Ok);
        Assert.Contains("judged together, and neither was written", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains(refused.Problem.Findings, x => x.Code == "history.closing-author");
        EstateModel model = f.Api.Session.Load();
        Assert.Equal(records, model.Records.Count);
        Assert.Equal(objects, f.Api.Session.Objects.List().Count);
        Assert.True(SignatureChecks.IsAdmitted(model, model.ById[target]));
    }

    [Fact]
    public void AnActStoredWithoutItsClosingInAnEstateThatAdoptedIsRefused()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        // Another writer adopts while this one signs its revocation over the copy it read before: the batch it would store holds no closing.
        bool adopted = false;
        f.Api.BeforeSigningRecord = () =>
        {
            if (adopted) return;
            adopted = true;
            using EstateService other = EstateService.Open(f.Records.Estate.Root);
            other.HistoryMemoryHome = f.MemoryHome;
            string plan = other.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!.PlanHash!;
            Assert.True(other.HistoryCheckpoint(new HistoryCheckpointRequest(op, Adopt: true, ExpectPlan: plan)).Checkpoint?.Applied);
        };

        WriteOutcome refused = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Revoked over a copy read before its adoption."));
        f.Api.BeforeSigningRecord = null;
        Assert.True(adopted);
        Assert.False(refused.Ok);
        Assert.Contains($"a checkpoint closing device {f.BuilderDevice}", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("comes without it, so nothing was written", refused.Problem.Message, StringComparison.Ordinal);
        EstateModel model = f.Api.Session.Load();
        Assert.True(SignedHistory.Adopted(model));
        Assert.True(SignatureChecks.IsAdmitted(model, model.ById[f.BuilderDevice]));

        // Written again over the adopted copy, it carries its closing.
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Revoked over the adopted copy."));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.Equal("closing", revoked.Checkpoint!.Trigger);
    }

    [Fact]
    public void AnActSignedElsewhereCannotCarryItsClosingAndIsRefused()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        EstateModel before = f.Api.Session.Load();
        RecordEnvelope device = before.ById[f.BuilderDevice];
        RecordEnvelope admission = SignatureChecks.AdmissionOf(before, device)!;
        string publicKey = EstateModel.BodyString(before.ById[f.OperatorDevice], "publicKey")!;
        var body = new JsonObject
        {
            ["principal"] = f.Records.Estate.BuilderId, ["status"] = "revoked", ["note"] = "Revoked from the phone.",
            ["revokedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(1)),
        };
        var request = new NewRecordRequest("admission", op, PolicyFixture.Element(body), Subject: $"device:{device.Id}:{device.ComputedHash}", Supersedes: admission.Id);

        // Prepared on the phone before the estate adopted, signed after: the runtime writes no closing for a record signed elsewhere.
        RecordPrepareResponse prepared;
        using (f.Api.ForDeviceRequest(f.OperatorDevice, publicKey)) prepared = f.Api.PrepareRecord(request);
        Assert.True(prepared.Ok, prepared.Problem?.Message);
        f.Adopt();
        int records = f.Api.Session.Load().Records.Count;
        string signature = new StoreSigner(new FileKeyStore(VericairnHome.Path()), f.OperatorDevice).Sign(RecordSigning.Digest(prepared.Prepared!.Hash)).Signature;
        WriteOutcome written;
        using (f.Api.ForDeviceRequest(f.OperatorDevice, publicKey)) written = f.Api.NewSignedRecord(new SignedRecordRequest(prepared.Prepared.Plan, signature));
        Assert.False(written.Ok);
        Assert.Contains("carries no closing", written.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);

        // Once the estate has adopted, such an act is not even prepared.
        using (f.Api.ForDeviceRequest(f.OperatorDevice, publicKey)) prepared = f.Api.PrepareRecord(request);
        Assert.False(prepared.Ok);
        Assert.Contains("carries no closing", prepared.Problem!.Message, StringComparison.Ordinal);
        Assert.True(SignatureChecks.IsAdmitted(f.Api.Session.Load(), f.Api.Session.Load().ById[f.BuilderDevice]));
    }

    [Fact]
    public void AdoptionsCheckListsTheRecordsTheAtTimeReadingChangesAndAdoptionKeepsTheirReading()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string builder = f.Records.Estate.BuilderId;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        f.Records.Estate.SetClock(now.AddHours(-2));
        StoreSigner held = f.Records.AdmitSigner(f.Records.Estate.OperatorId);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[held.DeviceId];
        RecordEnvelope admission = SignatureChecks.AdmissionOf(model, device)!;
        // The builder records a principal, and revokes a device, both dated before the grant of administer that today's reading lets
        // the principal record rest on.
        f.Records.Estate.SetClock(now.AddHours(-1));
        RecordEnvelope helper = f.Records.Add("principal", new JsonObject
        {
            ["kind"] = "agent", ["name"] = "Helper", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Example Agent" },
        }, builder: true);
        RecordEnvelope revocation = f.Records.Add("admission", new JsonObject
        {
            ["principal"] = f.Records.Estate.OperatorId, ["status"] = "revoked", ["note"] = "Revoked before the grant.",
            ["revokedAt"] = RecordParser.FormatTimestamp(now.AddHours(-1)),
        }, builder: true, subject: device.Reference, supersedes: admission.Id);
        f.Records.Estate.SetClock(now.AddMinutes(-30));
        f.Records.Add("authority", Grant(f, builder, Scopes.Administer));
        EstateModel today = f.Api.Session.Load();
        Assert.DoesNotContain(today.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(helper.ExpectedPath, StringComparison.Ordinal));
        Assert.True(today.Counts(today.ById[helper.Id]));
        // An admission or revocation is judged by its signer's authority at its own time, and administer counts for that only for a
        // person: the agent's revocation counts for nothing, so the device stays admitted.
        Assert.False(today.Counts(today.ById[revocation.Id]));
        Assert.True(SignatureChecks.IsAdmitted(today, today.ById[device.Id]));

        HistoryCheckpointResponse check = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!;
        HistoryRecordItem changed = Assert.Single(check.AuthorityChanges);
        Assert.Equal((helper.Id, "principal"), (changed.Id, changed.Type));
        Assert.StartsWith("read at its own time, principal.author: ", changed.Note, StringComparison.Ordinal);
        // Adoption neither anchors the revocation nor lists it among the readings it keeps: it counts for nothing in either reading.
        Assert.DoesNotContain(check.Anchored, x => x.Id == revocation.Id);
        Assert.DoesNotContain(check.AuthorityChanges, x => x.Id == revocation.Id);

        // Adoption keeps the reading each record has then: the principal record still holds once the estate has adopted, and the
        // revocation still counts for nothing.
        f.Adopt();
        EstateModel adopted = f.Api.Session.Load();
        Assert.True(SignedHistory.Adopted(adopted));
        Assert.DoesNotContain(adopted.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(helper.ExpectedPath, StringComparison.Ordinal));
        Assert.True(adopted.Counts(adopted.ById[helper.Id]));
        Assert.False(adopted.Counts(adopted.ById[revocation.Id]));
        Assert.True(SignatureChecks.IsAdmitted(adopted, adopted.ById[device.Id]));
    }

    [Fact]
    public void APersonHoldingAdministerAdoptsInsteadOfTheRootOperator()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        // The person and the grant are recorded before now: adoption reads the administrator's standing as of its own time.
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddMinutes(-10));
        RecordEnvelope person = f.Records.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Estate administrator", ["roles"] = new JsonArray("contributor") });
        f.Records.Add("authority", Grant(f, person.Id, Scopes.Administer));
        string device = f.HeldDevice(person.Id);

        string plan = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!.PlanHash!;
        (HistoryCheckpointResponse? adopted, Problem? problem) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(person.Id, Adopt: true, ExpectPlan: plan));
        Assert.True(adopted?.Applied == true, JsonSerializer.Serialize(problem, ProtocolJson.Options));
        Assert.True(adopted!.Adopted);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope adoption = Assert.Single(model.OfType("history-checkpoint"));
        Assert.Equal(person.Id, adoption.By);
        Assert.Equal(device, Assert.Single(adoption.Signatures).Device);
        Assert.True(SignedHistory.Adopted(model));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.adoption-author" or "history.anchor-author");
    }

    [Fact]
    public void ABirthAdoptionIsNotWrittenOverACopyWhoseVerifiedCheckpointIsGone()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-closing-birth-" + Ulid.NewUlid().Value);
        string home = Path.Combine(Path.GetTempPath(), "vericairn-closing-birth-home-" + Ulid.NewUlid().Value);
        string? key = null;
        try
        {
            (RecordEnvelope op, _) = EstateInitializer.Initialize(root, new EstateInitialization("Born", "born", "Adopt at birth.", "Operator", "Org", null, "Vericairn/vericairn", new string('a', 40)));
            using EstateService api = EstateService.Open(root);
            api.HistoryMemoryHome = home;
            // This machine verified a checkpoint of this clone that the copy no longer holds: it may have been rewritten back to its birth.
            EstateManifest manifest = api.Session.Load().Manifest!;
            new HistoryMemory(root, manifest.Estate, manifest.Profile, home).Write(new HistoryMemory.Remembered(manifest.Estate,
                new HistoryMemory.Verified(Ulid.NewUlid().Value, "sha256:" + new string('b', 64), "records/history-checkpoint/2026-11/gone.json", "main", new string('c', 40), "2026-11-08T00:00:00Z"), null));

            WriteOutcome created = api.DeviceCreate(new(op.Id, "Operator's device", "file"));
            Assert.True(created.Ok, Describe(created));
            key = created.Written!.Id;
            WriteOutcome admitted = api.DeviceAdmit(new AdmitRequest(key, op.Id, "The first authority."));
            Assert.True(admitted.Ok, Describe(admitted));
            Assert.Equal("adoption", admitted.Checkpoint!.Trigger);
            Assert.Null(admitted.Checkpoint.Written);
            Assert.Contains("this copy no longer holds it", admitted.Checkpoint.Skipped, StringComparison.Ordinal);
            Assert.Contains("written without the adoption checkpoint", admitted.Checkpoint.Skipped, StringComparison.Ordinal);
            Assert.False(SignedHistory.Adopted(api.Session.Load()));
            Assert.Empty(api.Session.Load().OfType("history-checkpoint"));
        }
        finally
        {
            var store = new FileKeyStore(VericairnHome.Path());
            if (key is not null && store.Has(key)) store.Remove(key);
            foreach (string directory in new[] { root, home }.Where(Directory.Exists)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ABatchStoppedBeforeItsCheckpointStaysATaskOnlyWhileWhatTheCheckpointCarriedIsMissing()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        string? device = null;
        try
        {
            // An admission stored without its interval checkpoint in an estate that has adopted needs nothing more.
            f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddDays(-2));
            f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("OLD-9"))!.AsObject());
            f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type == "history-checkpoint") throw new IOException("The test stops the batch before its checkpoint."); };
            WriteOutcome created = f.Api.DeviceCreate(new(f.Records.Estate.BuilderId, "Second build machine", "file"));
            Assert.True(created.Ok, Describe(created));
            device = created.Written!.Id;
            WriteOutcome admitted = f.Api.DeviceAdmit(new AdmitRequest(device, op, "Admitted while an interval was due."));
            Assert.NotNull(admitted.Written);
            Assert.Equal("interval", admitted.Checkpoint!.Trigger);
            Assert.Null(admitted.Checkpoint.Written);
            Assert.DoesNotContain(f.Api.Reconcile(), t => t.Code == "batch.incomplete");

            // A revision to required without the checkpoint anchoring what counted before stays a task, saying how to settle it, until
            // no unsigned record is left unanchored.
            RecordEnvelope unsigned = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("CUT-9"))!.AsObject(), sign: false);
            JsonObject required = Charter(f, "Exercise the record format.");
            required["policies"]!["signatures"] = "required";
            WriteOutcome moved = f.Api.NewRecord(new NewRecordRequest("estate", op, PolicyFixture.Element(required), Supersedes: f.Records.Estate.Estate.Id));
            Assert.NotNull(moved.Written);
            Assert.Null(moved.Checkpoint!.Written);
            ReconcileTask task = Assert.Single(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
            Assert.Contains("anchors each, after reading it, in a checkpoint's anchored list", task.Message, StringComparison.Ordinal);
            File.Delete(Path.Combine(f.Records.Estate.Root, unsigned.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
        }
        finally
        {
            f.Api.BeforeStoringTogether = null;
            var store = new FileKeyStore(VericairnHome.Path());
            if (device is not null && store.Has(device)) store.Remove(device);
        }
    }

    [Theory]
    [InlineData("rotation")]
    [InlineData("self-revocation")]
    [InlineData("revocation")]
    [InlineData("narrowing")]
    public void ABatchStoppedAfterItsClosingRemovesTheClosingAgainAndTheActIsWrittenAgain(string act)
    {
        using var f = new HistoryTriggerTests.Fixture();
        Func<WriteOutcome> perform = Act(f, act);
        EstateModel before = f.Api.Session.Load();
        int objects = f.Api.Session.Objects.List().Count;

        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
        WriteOutcome stopped = perform();
        f.Api.BeforeStoringTogether = null;
        Assert.False(stopped.Ok);
        Assert.Null(stopped.Written);
        Assert.Equal("refused", stopped.Problem!.Code);
        Assert.Contains("was removed again, so nothing was written", stopped.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("The test stops the batch", stopped.Problem.Message, StringComparison.Ordinal);
        EstateModel after = f.Api.Session.Load();
        Assert.Equal(before.Records.Count, after.Records.Count);
        Assert.Equal(objects, f.Api.Session.Objects.List().Count);
        Assert.Empty(after.Trust.Closings);
        Assert.DoesNotContain(f.Api.Reconcile(), t => t.Code == "batch.incomplete");

        WriteOutcome retried = perform();
        Assert.True(retried.Ok, Describe(retried));
        Assert.Equal("closing", retried.Checkpoint!.Trigger);
        Assert.NotNull(retried.Checkpoint.Written);
        AssertSettled(f);
    }

    [Theory]
    [InlineData("rotation")]
    [InlineData("self-revocation")]
    [InlineData("revocation")]
    [InlineData("narrowing")]
    public void AClosingKeptAfterItsBatchStoppedIsReportedAndTheActWrittenAgainFollowsItAlone(string act)
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        Func<WriteOutcome> perform = Act(f, act);

        // The closing cannot be removed again, as after a process stopped between the two writes: it stays and closes.
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
        f.Api.BeforeUndoingClosing = _ => throw new IOException("The test keeps the closing.");
        WriteOutcome stopped = perform();
        f.Api.BeforeStoringTogether = null;
        f.Api.BeforeUndoingClosing = null;
        Assert.Null(stopped.Written);
        Assert.Equal(EstateService.ClosingStoredCode, stopped.Problem!.Code);
        Assert.Contains("uses the stored closing", stopped.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("The test stops the batch", stopped.Problem.Message, StringComparison.Ordinal);
        RecordWritten closing = stopped.Checkpoint!.Written!;
        Assert.Equal("closing", stopped.Checkpoint.Trigger);
        Assert.Contains(closing.Id, stopped.Problem.Message, StringComparison.Ordinal);
        EstateModel half = f.Api.Session.Load();
        Assert.Contains(half.Trust.Closings, c => c.Record.Id == closing.Id);
        Assert.Contains(half.Findings, x => x.Code == "history.closing-unfollowed");
        ReconcileTask task = Assert.Single(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
        Assert.Contains($"Closing {closing.Id} closes", task.Message, StringComparison.Ordinal);
        Assert.Contains("uses the stored closing", task.Message, StringComparison.Ordinal);
        int checkpoints = Checkpoints(f).Count;

        // Written again, by the same command, the act follows the stored closing alone: no new closing is written.
        WriteOutcome retried = perform();
        Assert.True(retried.Ok, Describe(retried));
        Assert.Equal("closing", retried.Checkpoint!.Trigger);
        Assert.Null(retried.Checkpoint.Written);
        Assert.Contains($"it follows closing {closing.Id}", retried.Checkpoint.Skipped, StringComparison.Ordinal);
        Assert.Equal(checkpoints, Checkpoints(f).Count);
        AssertSettled(f);
        EstateModel model = f.Api.Session.Load();
        Closing used = Assert.Single(model.Trust.Closings);
        Assert.Equal(closing.Id, used.Record.Id);

        // The operator whose only device rotated signs with its new key, and what it signs counts.
        if (act == "rotation")
        {
            WriteOutcome order = f.Api.NewRecord(new NewRecordRequest("work-order", op, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("ROT-9"))!)));
            Assert.True(order.Ok, Describe(order));
            Assert.True(f.Api.Session.Load().Counts(f.Api.Session.Load().ById[order.Written!.Id]));
        }
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("revoked by a file")]
    [InlineData("closed")]
    public void AKeyClosingKeptAfterItsBatchStoppedIsSettledByRetiringTheDeviceInstead(string instead)
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
        f.Api.BeforeUndoingClosing = _ => throw new IOException("The test keeps the closing.");
        WriteOutcome stopped = f.Api.DeviceRotate(new DeviceRotateRequest(f.BuilderDevice));
        f.Api.BeforeStoringTogether = null;
        f.Api.BeforeUndoingClosing = null;
        Assert.Equal(EstateService.ClosingStoredCode, stopped.Problem!.Code);
        string closing = stopped.Checkpoint!.Written!.Id;
        Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.closing-unfollowed" && x.Location.Contains(closing, StringComparison.Ordinal));
        ReconcileTask task = Assert.Single(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
        Assert.Contains($"Write the key rotation again as {builder}, with the same command", task.Message, StringComparison.Ordinal);
        Assert.Contains($"Or revoke device {f.BuilderDevice} instead, with vericairn devices revoke", task.Message, StringComparison.Ordinal);

        // The operator retires the device instead of rotating its key again, as the guide advises when a key may have leaked: a
        // revocation with its closing, a revocation arriving as a file, or a closing of the device alone.
        Thread.Sleep(1100);
        string? deviceClosing = null;
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[f.BuilderDevice];
        switch (instead)
        {
            case "revoked":
                WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Retired instead of rotated."));
                Assert.True(revoked.Ok, Describe(revoked));
                deviceClosing = revoked.Checkpoint!.Written!.Id;
                break;
            case "revoked by a file":
                f.Records.Estate.SetClock(DateTimeOffset.UtcNow);
                f.Records.Add("admission", new JsonObject
                {
                    ["principal"] = builder, ["status"] = "revoked", ["note"] = "Retired by a file.", ["revokedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock),
                }, subject: device.Reference, supersedes: SignatureChecks.AdmissionOf(model, device)!.Id);
                break;
            default:
                CheckpointDraft draft = SignedHistory.Prepare(model, "closing", closes: ("device", device.Id));
                foreach (byte[] bytes in draft.Objects) f.Api.Session.Objects.Put(bytes);
                WriteOutcome closed = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", op, draft.Body));
                Assert.True(closed.Ok, Describe(closed));
                deviceClosing = closed.Written!.Id;
                break;
        }

        // The key's closing is followed: no warning for it, and no task, on this service or on one opened afresh.
        EstateModel after = f.Api.Session.Load();
        Assert.False(after.Findings.HasErrors, string.Join('\n', after.Findings.Where(x => x.Severity == Severity.Error)));
        Assert.Contains(after.Trust.Closings, c => c.Record.Id == closing);
        Assert.DoesNotContain(after.Findings, x => x.Code == "history.closing-unfollowed" && x.Location.Contains(closing, StringComparison.Ordinal));
        using (EstateService again = EstateService.Open(f.Records.Estate.Root))
        {
            again.HistoryMemoryHome = f.MemoryHome;
            Assert.DoesNotContain(again.Reconcile(), t => t.Code == "batch.incomplete");
        }

        Assert.DoesNotContain(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
        if (instead == "closed")
        {
            // The device's own closing waits for its revocation.
            Assert.Single(after.Findings, x => x.Code == "history.closing-unfollowed" && x.Location.Contains(deviceClosing!, StringComparison.Ordinal));
        }
        else
        {
            Assert.DoesNotContain(after.Findings, x => x.Code == "history.closing-unfollowed");
        }

        // The retired device's key is rotated no more: the rotation is refused, and nothing is written.
        int records = after.Records.Count;
        WriteOutcome rotation = f.Api.DeviceRotate(new DeviceRotateRequest(f.BuilderDevice));
        Assert.False(rotation.Ok);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);
    }

    [Fact]
    public void AClosedDevicesRecordsStillCountForNothingWhenTheActFollowsAStoredClosing()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
        f.Api.BeforeUndoingClosing = _ => throw new IOException("The test keeps the closing.");
        WriteOutcome stopped = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "The build machine was lost."));
        f.Api.BeforeStoringTogether = null;
        f.Api.BeforeUndoingClosing = null;
        RecordEnvelope closing = f.Api.Session.Load().ById[stopped.Checkpoint!.Written!.Id];

        // The closed device signs nothing that counts through the runtime meanwhile.
        WriteOutcome meanwhile = f.Api.NewRecord(new NewRecordRequest("work-order", builder, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("MID-1"))!)));
        Assert.False(meanwhile.Ok);
        Assert.Contains(meanwhile.Problem!.Findings, x => x.Code == "history.after-closing");

        Thread.Sleep(1100);
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "The build machine was lost."));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.Null(revoked.Checkpoint!.Written);

        // A record the closed device signed dated between the closing and the act, arriving afterwards, is witnessed by nothing.
        f.Records.Estate.SetClock(closing.AtTime);
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), f.BuilderDevice);
        RecordEnvelope between = f.Records.Estate.Add("work-order", builder, HistoryFixture.Order("MID-2"), signer: signer);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Counts(model.ById[between.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(between.ExpectedPath, StringComparison.Ordinal));
        Assert.False(SignatureChecks.IsAdmitted(model, model.ById[f.BuilderDevice]));
    }

    [Fact]
    public void AStoredClosingCarriesNoActOfAnotherAuthorOrForAnotherTarget()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        // The builder, an agent, holds administer, so it may revoke a device; but only a person or an operator closes one.
        f.Records.Add("authority", Grant(f, builder, Scopes.Administer));
        string target = f.HeldDevice(op);
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
        f.Api.BeforeUndoingClosing = _ => throw new IOException("The test keeps the closing.");
        WriteOutcome stopped = f.Api.DeviceRevoke(new RevokeRequest(target, op, "Retired."));
        f.Api.BeforeStoringTogether = null;
        f.Api.BeforeUndoingClosing = null;
        string closing = stopped.Checkpoint!.Written!.Id;

        // The agent's revocation of the same device needs a closing of its own, which closes nothing: refused, nothing written.
        int records = f.Api.Session.Load().Records.Count;
        WriteOutcome byAgent = f.Api.DeviceRevoke(new RevokeRequest(target, builder, "An agent revokes after the operator's closing."));
        Assert.False(byAgent.Ok);
        Assert.Contains(byAgent.Problem!.Findings, x => x.Code == "history.closing-author");
        Assert.Equal(records, f.Api.Session.Load().Records.Count);

        // The operator's revocation of another device carries its own closing.
        WriteOutcome other = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Another device."));
        Assert.True(other.Ok, Describe(other));
        Assert.NotNull(other.Checkpoint!.Written);
        Assert.NotEqual(closing, other.Checkpoint.Written.Id);
        Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.closing-unfollowed" && x.Location.Contains(closing, StringComparison.Ordinal));

        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(target, op, "Retired."));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.Null(revoked.Checkpoint!.Written);
        AssertSettled(f);
    }

    [Fact]
    public void AnActStatedBeforeWhatItsClosingWouldWitnessIsRefusedAndASelfRevocationSaysWhy()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        // The grant states a time hours before adoption, so a revision stated an hour ago is not moved up to it.
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddHours(-3));
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering, Scopes.AcceptWork));
        int records = f.Api.Session.Load().Records.Count;

        string hourAgo = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(-1));
        // A grant revision takes the runtime's time, so one stating an earlier time is refused before any closing is prepared.
        WriteOutcome early = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, builder, Scopes.LocalEngineering)), Supersedes: grant.Id, At: hourAgo));
        Assert.False(early.Ok);
        Assert.Contains("Principal and authority records are written at the runtime's time", early.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);

        // A revocation may state its time. The builder's first device was admitted weeks ago, so a revocation stated an hour ago is not
        // moved up to its admission, and its closing cannot be dated before the head checkpoint.
        EstateModel before = f.Api.Session.Load();
        RecordEnvelope first = before.ById[f.Records.BuilderDevice.Id];
        RecordEnvelope firstAdmission = SignatureChecks.AdmissionOf(before, first)!;
        var stated = new JsonObject { ["principal"] = builder, ["status"] = "revoked", ["note"] = "Stated an hour ago.", ["revokedAt"] = hourAgo };
        WriteOutcome earlyRevocation = f.Api.NewRecord(new NewRecordRequest("admission", op, PolicyFixture.Element(stated), Subject: $"device:{first.Id}:{first.ComputedHash}",
            Supersedes: firstAdmission.Id, At: hourAgo));
        Assert.False(earlyRevocation.Ok);
        Assert.Contains($"states {hourAgo}, more than five minutes before head checkpoint", earlyRevocation.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("Write the revocation again at the present time", earlyRevocation.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);
        WriteOutcome narrowed = f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, builder, Scopes.LocalEngineering)), Supersedes: grant.Id));
        Assert.True(narrowed.Ok, Describe(narrowed));
        Assert.DoesNotContain(f.Api.Session.Load().Findings, x => x.Code == "history.dated-after-witness");

        // A device revoking itself as of an earlier time, the record dated now, would revoke nothing after its own closing.
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[f.OperatorDevice];
        RecordEnvelope admission = SignatureChecks.AdmissionOf(model, device)!;
        string halfHourAgo = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(-30));
        var body = new JsonObject { ["principal"] = op, ["status"] = "revoked", ["note"] = "Lost half an hour ago.", ["revokedAt"] = halfHourAgo };
        records = model.Records.Count;
        WriteOutcome self = f.Api.NewRecord(new NewRecordRequest("admission", op, PolicyFixture.Element(body), Subject: $"device:{device.Id}:{device.ComputedHash}", Supersedes: admission.Id));
        Assert.False(self.Ok);
        Assert.Contains($"signs its own revocation with revokedAt {halfHourAgo}, before its closing", self.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("Write the revocation with revokedAt at the present time", self.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);

        body["revokedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        WriteOutcome revoked = f.Api.NewRecord(new NewRecordRequest("admission", op, PolicyFixture.Element(body), Subject: $"device:{device.Id}:{device.ComputedHash}", Supersedes: admission.Id));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.False(f.Api.Session.Load().Findings.HasErrors);
    }

    [Fact]
    public void AnActArrivingAsAFileWithoutAClosingIsWarnedUntilAClosingNamesItsTarget()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering, Scopes.AcceptWork));
        RecordEnvelope narrowed = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering), supersedes: grant.Id);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[f.BuilderDevice];
        RecordEnvelope admission = SignatureChecks.AdmissionOf(model, device)!;
        RecordEnvelope revocation = f.Records.Add("admission", new JsonObject
        {
            ["principal"] = builder, ["status"] = "revoked", ["note"] = "Revoked by a file.", ["revokedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock.AddSeconds(1)),
        }, subject: device.Reference, supersedes: admission.Id);

        model = f.Api.Session.Load();
        Assert.True(model.Counts(model.ById[narrowed.Id]));
        Assert.True(model.Counts(model.ById[revocation.Id]));
        Finding onGrant = Assert.Single(model.Findings, x => x.Code == "history.closing-missing" && x.Location == narrowed.ExpectedPath);
        Assert.Equal(Severity.Warning, onGrant.Severity);
        Assert.Contains($"no closing names the grant", onGrant.Message, StringComparison.Ordinal);
        Assert.Contains($"vericairn records new history-checkpoint, its closes naming grant {grant.Id}", onGrant.Message, StringComparison.Ordinal);
        Finding onDevice = Assert.Single(model.Findings, x => x.Code == "history.closing-missing" && x.Location == revocation.ExpectedPath);
        Assert.Contains("a record the device signs with an earlier date still counts", onDevice.Message, StringComparison.Ordinal);
        Assert.Contains($"its closes naming device {device.Id}", onDevice.Message, StringComparison.Ordinal);

        // A closing written afterwards, as any checkpoint, names the device: from it on the device accounts for nothing it does not witness.
        CheckpointDraft draft = SignedHistory.Prepare(model, "closing", closes: ("device", device.Id));
        foreach (byte[] bytes in draft.Objects) f.Api.Session.Objects.Put(bytes);
        WriteOutcome closed = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", op, draft.Body));
        Assert.True(closed.Ok, Describe(closed));
        model = f.Api.Session.Load();
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-missing" && x.Location == revocation.ExpectedPath);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-missing" && x.Location == narrowed.ExpectedPath);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-unfollowed");
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
    }

    [Fact]
    public void ActsFromBeforeAdoptionAndInAnEstateThatHasNotAdoptedAreNotWarned()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string builder = f.Records.Estate.BuilderId;
        RecordEnvelope grant = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering, Scopes.AcceptWork));
        f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering), supersedes: grant.Id);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[f.BuilderDevice];
        f.Records.Add("admission", new JsonObject
        {
            ["principal"] = builder, ["status"] = "revoked", ["note"] = "Revoked before adoption.", ["revokedAt"] = RecordParser.FormatTimestamp(f.Records.Estate.Clock.AddSeconds(1)),
        }, subject: device.Reference, supersedes: SignatureChecks.AdmissionOf(model, device)!.Id);
        Assert.DoesNotContain(f.Api.Session.Load().Findings, x => x.Code == "history.closing-missing");

        f.Adopt();
        model = f.Api.Session.Load();
        Assert.True(SignedHistory.Adopted(model));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-missing");
    }

    [Fact]
    public void AStoreFailureIsDescribedByItsKindWithoutItsMessageOrAPath()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        string named = Path.Combine(f.Records.Estate.Root, "records", "history-checkpoint");
        // An interval checkpoint stopped by a failure whose message names a path on this machine.
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddDays(-2));
        f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("OLD-8"))!.AsObject());
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type == "history-checkpoint") throw new IOException($"Could not write {named}."); };
        WriteOutcome order = f.Api.NewRecord(new NewRecordRequest("work-order", op, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("NEW-8"))!)));
        Assert.NotNull(order.Written);
        Assert.Equal("interval", order.Checkpoint!.Trigger);
        string skipped = order.Checkpoint.Skipped!;
        Assert.DoesNotContain(named, skipped, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Records.Estate.Root, skipped, StringComparison.Ordinal);
        Assert.Contains("could not be stored (the file system refused the write, IOException)", skipped, StringComparison.Ordinal);

        // The act a kept closing precedes reports its failure the same way.
        f.Api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException($"Could not write {named}."); };
        f.Api.BeforeUndoingClosing = _ => throw new IOException($"Could not remove {named}.");
        WriteOutcome stopped = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Lost."));
        f.Api.BeforeStoringTogether = null;
        f.Api.BeforeUndoingClosing = null;
        Assert.Equal(EstateService.ClosingStoredCode, stopped.Problem!.Code);
        Assert.DoesNotContain(f.Records.Estate.Root, stopped.Problem.Message, StringComparison.Ordinal);
        Assert.Contains("could not be stored (the file system refused the write, IOException)", stopped.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASigningFailureIsDescribedByItsKindWithoutItsMessageOrAPath()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        string named = Path.Combine(f.Records.Estate.Root, "keys", "device.key");
        const string Described = "The record could not be signed on this machine (the signing key could not be read, IOException).";

        // The closing's signature, the second the revocation needs, fails with a message naming a path on this machine.
        int signatures = 0;
        f.Api.BeforeSigningRecord = () => { if (++signatures == 2) throw new IOException($"Could not read {named}."); };
        int records = f.Api.Session.Load().Records.Count;
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "Lost."));
        Assert.Equal(2, signatures);
        Assert.False(revoked.Ok);
        Assert.Contains("The closing could not be written", revoked.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains(Described, revoked.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Records.Estate.Root, revoked.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(records, f.Api.Session.Load().Records.Count);

        // An interval checkpoint whose signature fails the same way is skipped with the same description; the record is written.
        signatures = 0;
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddDays(-2));
        f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("OLD-9"))!.AsObject());
        WriteOutcome order = f.Api.NewRecord(new NewRecordRequest("work-order", op, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("NEW-9"))!)));
        f.Api.BeforeSigningRecord = null;
        Assert.Equal(2, signatures);
        Assert.NotNull(order.Written);
        Assert.Equal("interval", order.Checkpoint!.Trigger);
        Assert.Null(order.Checkpoint.Written);
        Assert.Contains(Described, order.Checkpoint.Skipped, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Records.Estate.Root, order.Checkpoint.Skipped!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableSigningKeyIsDescribedByItsKindWithoutAPath()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("This permission-denial case requires Unix file modes and an unprivileged reader.");
            return;
        }

        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("KEY-1"))!.AsObject());
        string key = Path.Combine(VericairnHome.Path(), "keys", f.OperatorDevice + ".p8");
        Assert.True(File.Exists(key));

        // The key store's own failure names the key's path; the refusal names its kind and type only.
        UnixFileMode mode = File.GetUnixFileMode(key);
        File.SetUnixFileMode(key, UnixFileMode.None);
        Problem? problem;
        try
        {
            problem = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(op)).Problem;
        }
        finally
        {
            File.SetUnixFileMode(key, mode);
        }

        Assert.NotNull(problem);
        Assert.Contains("The record could not be signed on this machine (access to the signing key was denied, UnauthorizedAccessException).", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(VericairnHome.Path(), problem.Message, StringComparison.Ordinal);
    }

    /// <summary>The act a test performs again and again: a rotation of the operator's only device, its revocation of itself, its revocation of the builder's device, or a narrowing.</summary>
    private static Func<WriteOutcome> Act(HistoryTriggerTests.Fixture f, string act)
    {
        string op = f.Records.Estate.OperatorId, builder = f.Records.Estate.BuilderId;
        switch (act)
        {
            case "rotation":
                return () => f.Api.DeviceRotate(new DeviceRotateRequest(f.OperatorDevice));
            case "self-revocation":
                return () => f.Api.DeviceRevoke(new RevokeRequest(f.OperatorDevice, op, "The operator's only device revokes itself."));
            case "revocation":
                return () => f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, op, "The build machine was lost."));
            default:
                RecordEnvelope grant = f.Records.Add("authority", Grant(f, builder, Scopes.LocalEngineering, Scopes.AcceptWork));
                return () => f.Api.NewRecord(new NewRecordRequest("authority", op, PolicyFixture.Element(Grant(f, builder, Scopes.LocalEngineering)), Supersedes: grant.Id));
        }
    }

    /// <summary>No error, no closing without its act, no act without its closing and no unfinished batch is left.</summary>
    private static void AssertSettled(HistoryTriggerTests.Fixture f)
    {
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-unfollowed" or "history.closing-missing" or "history.dated-after-witness");
        Assert.DoesNotContain(f.Api.Reconcile(), t => t.Code == "batch.incomplete");
    }

    private static JsonObject Grant(HistoryTriggerTests.Fixture f, string grantee, params string[] scopes) => new()
    {
        ["grantee"] = grantee, ["scope"] = new JsonArray([.. scopes.Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(),
        ["basis"] = f.Records.Decision.Id,
    };

    private static JsonObject Charter(HistoryTriggerTests.Fixture f, string purpose) => new()
    {
        ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = purpose, ["operator"] = f.Records.Estate.OperatorId, ["recordFormat"] = 3,
        ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
    };

    private static List<RecordEnvelope> Checkpoints(HistoryTriggerTests.Fixture f) => [.. f.Api.Session.Load().Records.Where(r => r.Type == "history-checkpoint")];

    private static string Describe(WriteOutcome outcome) => JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options);
}
