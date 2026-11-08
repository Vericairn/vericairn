// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>What counts once history checkpoints are in play: principals, device purposes, adoption, anchorers, listings and removals.</summary>
public class HistoryCountingTests
{
    [Fact]
    public void APrincipalRecordThatDoesNotCountGrantsNoRole()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // The builder's anchored principal record, rewritten without a key to claim the operator role, is altered and counts for nothing.
        RecordEnvelope builder = f.Estate.Builder;
        File.Delete(f.PathOf(builder));
        RecordAuthor.Write(f.Root, RecordAuthor.Create(new RecordDraft("principal", builder.By, JsonDocument.Parse("""{"kind":"human","name":"Test Builder","roles":["operator","builder"]}""").RootElement, null, null, builder.AtTime, builder.Id, 3), []));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.altered" && x.Location == builder.ExpectedPath);
        Assert.False(model.Counts(model.ById[builder.Id]));
        Assert.Null(model.CurrentPrincipal(builder.Id));
        Assert.False(model.HasRole(builder.Id, Roles.Operator));
        Assert.Empty(model.RolesOf(builder.Id));

        // A new unsigned principal after adoption does not count either.
        RecordEnvelope ghost = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Ghost","roles":["operator"]}""");
        model = f.Load();
        Assert.False(model.Counts(ghost));
        Assert.Null(model.CurrentPrincipal(ghost.Id));
        Assert.False(model.HasRole(ghost.Id, Roles.Operator));
    }

    [Fact]
    public void AnUnsignedDeviceRevisionChangesNoPurpose()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-1"), signer: f.OperatorSigner);
        f.Require();
        JsonObject body = HistoryFixture.Body(f.OperatorDevice);
        body["purpose"] = "recovery";
        RecordEnvelope revision = f.Estate.Add("device", f.Estate.OperatorId, body.ToJsonString(), supersedes: f.OperatorDevice.Id);
        EstateModel model = f.Load();
        Assert.False(model.Counts(revision));
        Assert.False(SignatureChecks.Recovers(model, f.OperatorDevice.Id));
        Assert.True(model.Counts(order));
        Assert.DoesNotContain(model.Findings, x => x.Code == "signature.recovery-scope");
        Assert.NotNull(SignatureChecks.FirstAuthority(model, f.Estate.OperatorId));
        // The operator still anchors: the adoption checkpoint counts.
        f.Adopt();
        Assert.True(SignedHistory.Adopted(f.Load()));
    }

    [Fact]
    public void ACheckpointWithABrokenPathAdoptsNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope early = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-2"));
        f.Require();
        EstateModel model = f.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, "manual");
        f.StoreObjects(draft);
        JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        body["previous"] = new JsonArray(new JsonObject { ["id"] = Ulid.NewUlid().Value, ["hash"] = "sha256:" + new string('a', 64) });
        body["trigger"] = "interval";
        RecordEnvelope agents = f.Estate.Add("history-checkpoint", f.Estate.BuilderId, body.ToJsonString(), signer: f.BuilderSigner);
        model = f.Load();
        Assert.False(SignedHistory.Adopted(model));
        Assert.False(model.Counts(agents));
        Assert.True(model.Counts(early));
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-missing" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.unanchored");
    }

    [Fact]
    public void ACheckpointFollowingOneThatDoesNotCountWitnessesNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope early = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-3"));
        f.Require();
        RecordEnvelope unsigned = f.Checkpoint(f.Estate.BuilderId, trigger: "adoption", sign: false);
        EstateModel model = f.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, "manual");
        f.StoreObjects(draft);
        JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        body["previous"] = new JsonArray(new JsonObject { ["id"] = unsigned.Id, ["hash"] = unsigned.ComputedHash });
        body["trigger"] = "interval";
        RecordEnvelope agents = f.Estate.Add("history-checkpoint", f.Estate.BuilderId, body.ToJsonString(), signer: f.BuilderSigner);
        model = f.Load();
        Assert.False(SignedHistory.Adopted(model));
        Assert.False(model.Counts(agents));
        Assert.True(model.Counts(early));
        Assert.True(model.Counts(f.Estate.Estate));
        Assert.True(model.Counts(f.Estate.Operator));
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-invalid" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, x => x.Code == "signature.required" && x.Location == unsigned.ExpectedPath);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.unanchored");
    }

    [Fact]
    public void StatusLeadsWithUnanchoredAndRefusedUnsignedRecordsOnceAdopted()
    {
        using var f = new HistoryFixture();
        RecordEnvelope charter = f.Require();
        f.Adopt();
        f.Estate.SetClock(charter.AtTime.AddSeconds(-5));
        RecordEnvelope backdated = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-4", "Backdated"));
        EstateModel model = f.Load();
        Assert.Contains(EstateStatus.HistoryProblems(model), x => x.Code == "history.unanchored" && x.Location == backdated.ExpectedPath);
        string text = EstateStatus.Render(model, new FindingList());
        Assert.True(text.IndexOf("History:", StringComparison.Ordinal) < text.IndexOf("Principals:", StringComparison.Ordinal));
    }

    [Fact]
    public void RevisionsByOthersNeitherGrantNorWithdrawAnAdministratorsAnchoring()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Basis","statement":"Grant authority.","summary":"A basis.","synthesis":"For tests.","effects":["grant"],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["publication"]}""", signer: f.OperatorSigner);
        RecordEnvelope admin = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", signer: f.OperatorSigner);
        RecordEnvelope adminDevice = f.Device(admin.Id, "Admin device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(admin.Id, "Admin device."), adminDevice.Reference, signer: f.OperatorSigner);
        string grantBody = $$"""{"grantee":"{{admin.Id}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""";
        RecordEnvelope grant = f.Estate.Add("authority", f.Estate.OperatorId, grantBody, signer: f.OperatorSigner);
        f.Require();
        f.Adopt();
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-5"));
        f.Checkpoint(by: admin.Id, signer: f.Signer(adminDevice), anchored: [late]);
        EstateModel model = f.Load();
        Assert.True(model.Counts(late));

        // An agent's signed revision of the grant withdraws nothing.
        RecordEnvelope agents = f.Estate.Add("authority", f.Estate.BuilderId, grantBody, supersedes: grant.Id, signer: f.BuilderSigner);
        model = f.Load();
        Assert.True(model.Counts(late));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.anchor-author");
        File.Delete(f.PathOf(agents));

        // Nor does an unsigned revision of the administrator's principal record written without a key.
        f.Estate.SetClock(admin.AtTime);
        f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", supersedes: admin.Id);
        model = f.Load();
        Assert.True(model.Counts(late));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.anchor-author");
    }

    [Fact]
    public void AListingByAnAuthorWhoMayNotAnchorTakesNoRecordAnAnchorerSignedAway()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-6"), signer: f.OperatorSigner);
        EstateModel model = f.Load();
        HistoryState state = SignedHistory.Of(model);
        string bogus = "sha256:" + new string('b', 64);
        var lines = new StringBuilder();
        var set = new List<(string, string)>();
        foreach (RecordEnvelope record in model.Records.Where(r => state.IsWitnessed(r.Id))) set.Add((record.Id, record.ComputedHash));
        foreach (RecordEnvelope record in model.Records.Where(r => !state.IsWitnessed(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            string hash = record.Id == order.Id ? bogus : record.ComputedHash;
            lines.Append(record.ExpectedPath["records/".Length..]).Append(' ').Append(hash).Append('\n');
            set.Add((record.Id, hash));
        }

        byte[] inventory = Encoding.UTF8.GetBytes(lines.ToString());
        f.StoreObjects(new CheckpointDraft(default, [inventory]));
        RecordReference head = state.Heads.Single();
        RecordEnvelope agents = f.Estate.Add("history-checkpoint", f.Estate.BuilderId, new JsonObject
        {
            ["previous"] = new JsonArray(new JsonObject { ["id"] = head.Id, ["hash"] = head.Hash }),
            ["inventory"] = new JsonArray(ContentHash.Sha256(inventory)),
            ["records"] = set.Count,
            ["recordSetHash"] = RecordSets.Hash(set),
            ["trigger"] = "interval",
        }.ToJsonString(), signer: f.BuilderSigner);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(model.Counts(model.ById[order.Id]));
        Finding mismatch = Assert.Single(model.Findings, x => x.Code == "history.listing-mismatch");
        Assert.Equal(Severity.Warning, mismatch.Severity);
        Assert.Equal(order.ExpectedPath, mismatch.Location);
        Assert.Contains(agents.Id, mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(f.Estate.BuilderId, mismatch.Message, StringComparison.Ordinal);
        Assert.Equal("planned", EstateStatus.WorkItems(model).Single(i => i.Key == "CNT-6").Status);

        // The root operator acknowledges the listed version's removal, and the record is listed again as it is.
        f.Checkpoint(removed: [(order.Id, bogus, "The listed bytes were never this record's.")]);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.listing-mismatch");
        Assert.True(SignedHistory.Of(model).IsWitnessed(order.Id));
    }

    [Fact]
    public void ARecordReaddedAfterAnAcknowledgedRemovalIsPartOfTheTail()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-7"));
        f.Checkpoint();
        f.Remove(order);
        f.Checkpoint(removed: [(order.Id, order.ComputedHash, "Leaked secret.")]);
        Assert.False(f.Load().Findings.HasErrors);
        JsonObject body = HistoryFixture.Body(order);
        body["title"] = "Replacement";
        RecordAuthor.Write(f.Root, RecordAuthor.Create(new RecordDraft("work-order", order.By, PolicyFixture.Element(body), null, null, order.AtTime, order.Id, 3), []));
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.False(SignedHistory.Of(model).IsWitnessed(order.Id));
        int tail = SignedHistory.Of(model).Tail;

        // The next checkpoint lists the re-added record, which is then witnessed: removing it is an error again.
        RecordEnvelope next = f.Checkpoint();
        model = f.Load();
        Assert.True(SignedHistory.Of(model).IsWitnessed(order.Id));
        Assert.Equal(1, SignedHistory.Of(model).Tail);
        Assert.True(tail > 1);
        File.Delete(f.PathOf(model.ById[order.Id]));
        Assert.Contains(f.Load().Findings, x => x.Code == "history.removed" && x.Location == order.ExpectedPath);
        _ = next;
    }

    [Fact]
    public void OnlyAnAnchorersRemovalsLeaveWhatAHeadWitnesses()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CNT-8"), signer: f.OperatorSigner);
        f.Checkpoint();
        f.Remove(order);
        // The agent's checkpoint states what it witnesses as if its removal counted; it does not, so the head is invalid as well.
        RecordEnvelope agents = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, removed: [(order.Id, order.ComputedHash, "Not the agent's to acknowledge.")]);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.anchor-author" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-invalid" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, x => x.Code == "history.removed" && x.Location == order.ExpectedPath);
    }

    [Fact]
    public void ASignerRewritingItsOwnWitnessedRecordAltersIt()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("CNT-9", "Original"), signer: f.BuilderSigner);
        RecordEnvelope agents = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(SignedHistory.Of(model).IsWitnessed(order.Id));

        // The agent rewrites its own witnessed work order under the same id, signed again with its own key.
        JsonObject body = HistoryFixture.Body(order);
        body["title"] = "Rewritten after witness";
        File.Delete(f.PathOf(order));
        RecordAuthor.Write(f.Root, RecordAuthor.Create(new RecordDraft("work-order", order.By, PolicyFixture.Element(body), null, null, order.AtTime, order.Id, 3), [f.BuilderSigner]));
        model = f.Load();
        RecordEnvelope rewritten = model.ById[order.Id];
        Assert.True(SignatureChecks.IsAuthentic(model, rewritten));
        Assert.False(model.Counts(rewritten));
        Finding altered = Assert.Single(model.Findings, x => x.Code == "history.altered" && x.Location == order.ExpectedPath);
        Assert.Contains(agents.Id, altered.Message, StringComparison.Ordinal);
        Assert.Contains(f.Estate.BuilderId, altered.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.listing-mismatch");
        Assert.Contains(EstateStatus.HistoryProblems(model), x => x.Code == "history.altered" && x.Location == order.ExpectedPath);
        Assert.DoesNotContain(EstateStatus.WorkItems(model), i => i.Key == "CNT-9");

        // A later checkpoint by the root operator does not witness the rewritten bytes: the record stays altered.
        f.Checkpoint();
        model = f.Load();
        Assert.False(model.Counts(model.ById[order.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.altered" && x.Location == order.ExpectedPath);

        // Restoring the witnessed bytes settles it.
        File.Delete(f.PathOf(order));
        RecordAuthor.Write(f.Root, RecordAuthor.Create(new RecordDraft("work-order", order.By, PolicyFixture.Element(HistoryFixture.Body(order)), null, null, order.AtTime, order.Id, 3), [f.BuilderSigner]));
        model = f.Load();
        Assert.Equal(order.ComputedHash, model.ById[order.Id].ComputedHash);
        Assert.True(model.Counts(model.ById[order.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.altered");
    }

    [Fact]
    public void ALongChainOfCheckpointsWithDescendingIdsLoads()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Estate.SetClock(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        f.Adopt();
        EstateModel model = f.Load();
        HistoryState state = SignedHistory.Of(model);
        var set = model.Records.Where(r => state.IsWitnessed(r.Id)).Select(r => (r.Id, r.ComputedHash)).ToList();
        RecordReference head = state.Heads.Single();
        RecordEnvelope previous = model.ById[head.Id];

        // Each checkpoint follows the one before it and has a lower id, so judging them in id order walks the whole chain at once.
        const int Length = 3000;
        DateTimeOffset at = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < Length; i++)
        {
            set.Add((previous.Id, previous.ComputedHash));
            byte[] inventory = Encoding.UTF8.GetBytes(previous.ExpectedPath["records/".Length..] + " " + previous.ComputedHash + "\n");
            f.StoreObjects(new CheckpointDraft(default, [inventory]));
            DateTimeOffset moment = at.AddSeconds(-i);
            f.Estate.SetClock(moment.AddSeconds(-1));
            previous = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, new JsonObject
            {
                ["previous"] = new JsonArray(new JsonObject { ["id"] = previous.Id, ["hash"] = previous.ComputedHash }),
                ["inventory"] = new JsonArray(ContentHash.Sha256(inventory)),
                ["records"] = set.Count,
                ["recordSetHash"] = RecordSets.Hash(set),
                ["trigger"] = "manual",
            }.ToJsonString(), id: IdAt(moment), signer: f.OperatorSigner);
        }

        // A small stack: judging the chain by recursion would exhaust it.
        EstateModel? loaded = null;
        var thread = new Thread(() => loaded = f.Load(), 256 * 1024);
        thread.Start();
        thread.Join();
        HistoryState chain = SignedHistory.Of(loaded!);
        Assert.Equal(Length + 1, chain.CountingCheckpoints.Count);
        Assert.Equal(previous.Id, Assert.Single(chain.Heads).Id);
        Assert.DoesNotContain(loaded!.Findings, x => x.Code.StartsWith("history.", StringComparison.Ordinal) && x.Severity == Severity.Error);
    }

    /// <summary>An identifier for a moment, outside the process's monotonic generator, so that a later record can have a lower id.</summary>
    private static string IdAt(DateTimeOffset moment)
    {
        var bytes = new byte[16];
        long ms = moment.ToUnixTimeMilliseconds();
        for (int i = 5; i >= 0; i--, ms >>= 8) bytes[i] = (byte)ms;
        bytes[15] = 1;
        return Ulid.FromBytes(bytes).Value;
    }
}
