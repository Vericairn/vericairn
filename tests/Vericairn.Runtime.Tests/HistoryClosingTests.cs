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
        // The builder revokes a device in a revocation dated before the grant of administer that today's reading lets it rest on.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        f.Records.Estate.SetClock(now.AddHours(-2));
        StoreSigner held = f.Records.AdmitSigner(f.Records.Estate.OperatorId);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope device = model.ById[held.DeviceId];
        RecordEnvelope admission = SignatureChecks.AdmissionOf(model, device)!;
        f.Records.Estate.SetClock(now.AddHours(-1));
        RecordEnvelope revocation = f.Records.Add("admission", new JsonObject
        {
            ["principal"] = f.Records.Estate.OperatorId, ["status"] = "revoked", ["note"] = "Revoked before the grant.",
            ["revokedAt"] = RecordParser.FormatTimestamp(now.AddHours(-1)),
        }, builder: true, subject: device.Reference, supersedes: admission.Id);
        f.Records.Estate.SetClock(now.AddMinutes(-30));
        f.Records.Add("authority", Grant(f, builder, Scopes.Administer));
        Assert.DoesNotContain(f.Api.Session.Load().Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(revocation.ExpectedPath, StringComparison.Ordinal));

        HistoryCheckpointResponse check = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!;
        HistoryRecordItem changed = Assert.Single(check.AuthorityChanges);
        Assert.Equal((revocation.Id, "admission"), (changed.Id, changed.Type));
        Assert.StartsWith("read at its own time, admission.author: ", changed.Note, StringComparison.Ordinal);

        // Adoption keeps the reading each record has then: the revocation still counts once the estate has adopted.
        f.Adopt();
        EstateModel adopted = f.Api.Session.Load();
        Assert.True(SignedHistory.Adopted(adopted));
        Assert.DoesNotContain(adopted.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(revocation.ExpectedPath, StringComparison.Ordinal));
        Assert.False(SignatureChecks.IsAdmitted(adopted, adopted.ById[device.Id]));
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
