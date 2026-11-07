// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>History checkpoints (design/signed-history.md §§ 5 to 7, 9 and 14): the record, its graph, anchoring and removed records.</summary>
public class HistoryCheckpointTests
{
    [Fact]
    public void AnEstateWithoutCheckpointsKeepsTodaysReading()
    {
        using var f = new HistoryFixture();
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-1"));
        f.Require();
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        HistoryState history = SignedHistory.Of(model);
        Assert.False(history.Adopted);
        Assert.Empty(history.Heads);
        Assert.Equal(model.Records.Count, history.Tail);
        Assert.Equal(model.Records.Count, model.CountingRecords.Count);
        // Before adoption, an unsigned record dated before the required revision still counts.
        Assert.True(model.Counts(unsigned));
        Assert.Contains(model.Current("work-order"), r => r.Id == unsigned.Id);
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        JsonElement json = status.RootElement.GetProperty("history");
        Assert.False(json.GetProperty("adopted").GetBoolean());
        Assert.Equal(0, json.GetProperty("heads").GetArrayLength());
        Assert.Equal(model.Records.Count, json.GetProperty("tail").GetInt32());
    }

    [Fact]
    public void ABackdatedUnsignedRecordIsRefusedAfterAdoptionWhateverItsDate()
    {
        using var f = new HistoryFixture();
        RecordEnvelope early = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-1"));
        RecordEnvelope charter = f.Require();
        f.Adopt();
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(SignedHistory.Adopted(model));
        Assert.True(model.Counts(early));

        // Dated before the required revision, as today's rule would let pass: after adoption only an anchor counts.
        f.Estate.SetClock(charter.AtTime.AddMinutes(-30));
        RecordEnvelope backdated = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-2", "Backdated"));
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.unanchored" && x.Location == backdated.ExpectedPath && x.Severity == Severity.Error);
        Assert.False(model.Counts(backdated));
        Assert.DoesNotContain(model.Current("work-order"), r => r.Id == backdated.Id);
        Assert.DoesNotContain(EstateStatus.WorkItems(model), i => i.Key == "HIS-2");
        Assert.Contains(model.Records, r => r.Id == backdated.Id);

        // The same record in an estate that has not adopted passes, as the guides say.
        File.Delete(Path.Combine(f.Root, model.OfType("history-checkpoint").Single().ExpectedPath));
        model = f.Load();
        Assert.True(model.Counts(backdated));
    }

    [Fact]
    public void AnAnchoringCheckpointOfTheRootOperatorMakesAnUnsignedRecordCount()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-3", "Late"));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "signature.required" && x.Location == late.ExpectedPath);
        Assert.False(model.Counts(late));

        f.Checkpoint(anchored: [late]);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(model.Counts(late));
        Assert.Contains(model.Current("work-order"), r => r.Id == late.Id);
        Assert.Contains(SignatureChecks.AnchorKey(late.Id, late.ComputedHash), SignatureChecks.Anchored(model));
    }

    [Fact]
    public void AnAgentIsRefusedAsAnchorerAndItsAnchorsCountForNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-4", "Late"));
        RecordEnvelope agents = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, anchored: [late]);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.anchor-author" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.False(model.Counts(late));
        Assert.DoesNotContain(SignatureChecks.AnchorKey(late.Id, late.ComputedHash), SignatureChecks.Anchored(model));
        // Witnessing grants nothing, so the agent's checkpoint still counts and witnesses.
        Assert.True(model.Counts(agents));
        Assert.Contains(SignedHistory.Of(model).Heads, h => h.Id == agents.Id);
    }

    [Fact]
    public void OnlyTheRootOperatorOrAnAdministratorAdopts()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope agents = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "adoption");
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.adoption-author" && x.Location.StartsWith(agents.ExpectedPath, StringComparison.Ordinal));
        Assert.False(model.Counts(agents));
        Assert.False(SignedHistory.Adopted(model));
        Assert.Empty(SignedHistory.Of(model).Heads);
        // The unsigned records the estate began with still read under the earlier boundary.
        Assert.True(model.Counts(f.Estate.Operator));
        Assert.Equal("history.expected-missing", SignedHistory.Expect(model, agents.Id)!.Code);
    }

    [Fact]
    public void AnyAdmittedDeviceMayWitnessWithoutAnchors()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("HIS-5"), signer: f.BuilderSigner);
        RecordEnvelope interval = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        HistoryState history = SignedHistory.Of(model);
        Assert.Equal(interval.Id, Assert.Single(history.Heads).Id);
        Assert.Equal(model.Records.Count - 1, history.Witnessed);
        Assert.Equal(1, history.Tail);
    }

    [Fact]
    public void AnUnsignedCheckpointIsAnErrorAndWitnessesNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-6"), signer: f.OperatorSigner);
        RecordEnvelope unsigned = f.Checkpoint(sign: false);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "signature.required" && x.Location == unsigned.ExpectedPath);
        Assert.False(model.Counts(unsigned));
        Assert.False(SignedHistory.Adopted(model));
        Assert.Empty(SignedHistory.Of(model).Heads);
        // What it lists is not witnessed: removing it raises nothing.
        f.Remove(order);
        Assert.DoesNotContain(f.Load().Findings, x => x.Code.StartsWith("history.", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeletedWitnessedRevocationIsCaughtOnAFreshCopyThenRestoredOrAcknowledged()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope laptop = f.Device(f.Estate.BuilderId, "Second build machine");
        RecordEnvelope admitted = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Admitted."), laptop.Reference, signer: f.OperatorSigner);
        string revokedAt = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
        RecordEnvelope revocation = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Revoked.", "revoked", revokedAt), laptop.Reference, supersedes: admitted.Id, signer: f.OperatorSigner);
        RecordEnvelope checkpoint = f.Adopt();
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.False(SignatureChecks.IsAdmitted(model, laptop));

        // Deleting the revocation would make the device current again; the checkpoint makes the removal an error on any copy.
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-history-copy-" + Identity.Ulid.NewUlid().Value);
        byte[] bytes = f.Remove(revocation);
        try
        {
            CopyDirectory(f.Root, copy);
            EstateModel fresh = EstateLoader.Load(copy);
            Finding removed = Assert.Single(fresh.Findings, x => x.Code == "history.removed");
            Assert.Equal(Severity.Error, removed.Severity);
            Assert.Equal(revocation.ExpectedPath, removed.Location);
            Assert.Contains(checkpoint.Id, removed.Message, StringComparison.Ordinal);
            string text = EstateStatus.Render(fresh, new FindingList());
            Assert.True(text.IndexOf("History:", StringComparison.Ordinal) < text.IndexOf("Principals:", StringComparison.Ordinal));
            Assert.Contains("history.removed", text, StringComparison.Ordinal);
            using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(fresh, new FindingList()));
            Assert.Equal("history.removed", status.RootElement.GetProperty("history").GetProperty("problems")[0].GetProperty("code").GetString());
        }
        finally { Directory.Delete(copy, recursive: true); }

        // Restored with other bytes it is altered; with the witnessed bytes the estate is whole again.
        JsonObject forged = HistoryFixture.Body(revocation);
        forged["note"] = "Not the revocation that was witnessed.";
        f.Restore(revocation, RecordAuthor.Create(new RecordDraft("admission", revocation.By, PolicyFixture.Element(forged), revocation.Subject, revocation.Supersedes, revocation.AtTime, revocation.Id, 3), []).Bytes);
        Assert.Contains(f.Load().Findings, x => x.Code == "history.altered" && x.Location == revocation.ExpectedPath);
        f.Restore(revocation, bytes);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));

        // An agent cannot acknowledge the removal; the root operator can, with a reason.
        f.Remove(revocation);
        f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, removed: [(revocation.Id, revocation.ComputedHash, "A test removal.")]);
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.anchor-author");
        Assert.Contains(model.Findings, x => x.Code == "history.removed");
        File.Delete(Path.Combine(f.Root, model.OfType("history-checkpoint").OrderBy(r => r.Id, StringComparer.Ordinal).Last().ExpectedPath));
        f.Checkpoint(removed: [(revocation.Id, revocation.ComputedHash, "The revocation carried a leaked secret.")]);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(SignedHistory.Of(model).IsAcknowledgedRemoval(revocation.Id));
    }

    [Fact]
    public void AnAlteredWitnessedRecordCountsForNothingUntilItsBytesAreRestored()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-7", "Original"));
        f.Adopt();
        byte[] original = File.ReadAllBytes(f.PathOf(order));
        JsonObject body = HistoryFixture.Body(order);
        body["title"] = "Rewritten";
        f.Restore(order, RecordAuthor.Create(new RecordDraft("work-order", order.By, PolicyFixture.Element(body), null, null, order.AtTime, order.Id, 3), []).Bytes);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.altered" && x.Location == order.ExpectedPath);
        Assert.False(model.Counts(model.ById[order.Id]));
        Assert.DoesNotContain(EstateStatus.WorkItems(model), i => i.Key == "HIS-7");
        f.Restore(order, original);
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Contains(EstateStatus.WorkItems(model), i => i.Key == "HIS-7" && i.Title == "Original");
    }

    [Fact]
    public void AMissingPredecessorOrInventoryObjectIsReported()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = f.Adopt();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-8"), signer: f.OperatorSigner);
        RecordEnvelope second = f.Checkpoint();
        f.Remove(first);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-missing" && x.Location.StartsWith(second.ExpectedPath, StringComparison.Ordinal) && x.Message.Contains(first.Id, StringComparison.Ordinal));
        Assert.Contains(model.Findings, x => x.Code == "history.removed" && x.Location == first.ExpectedPath);
        // The adoption checkpoint's absence does not undo adoption: a later checkpoint still counts.
        Assert.True(SignedHistory.Adopted(model));

        string inventory = second.Body.GetProperty("inventory")[0].GetString()!;
        File.Delete(f.ObjectPath(inventory));
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-missing" && x.Message.Contains(inventory, StringComparison.Ordinal));
    }

    [Fact]
    public void ConcurrentCheckpointsAreJoinedAfterAMergeWithoutReconciliation()
    {
        using var f = new HistoryFixture();
        RecordEnvelope adoption = f.Adopt();
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-history-clone-" + Identity.Ulid.NewUlid().Value);
        try
        {
            CopyDirectory(f.Root, clone);
            // This clone writes a record and a checkpoint; the other does the same from the same head.
            RecordEnvelope here = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-9", "Here"), signer: f.OperatorSigner);
            RecordEnvelope ours = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
            EstateModel other = EstateLoader.Load(clone);
            RecordEnvelope there = AddTo(clone, "work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-10", "There"), f.Estate.Clock.AddSeconds(5), f.OperatorSigner);
            other = EstateLoader.Load(clone);
            CheckpointDraft draft = SignedHistory.Prepare(other, "interval");
            Assert.Equal(adoption.Id, draft.Body.GetProperty("previous")[0].GetProperty("id").GetString());
            foreach (byte[] bytes in draft.Objects) WriteObject(clone, bytes);
            RecordEnvelope theirs = AddTo(clone, "history-checkpoint", f.Estate.BuilderId, draft.Body.GetRawText(), f.Estate.Clock.AddSeconds(6), f.BuilderSigner);

            // The merge is a pure addition: their record, checkpoint and object files join ours.
            foreach (string file in Directory.EnumerateFiles(clone, "*", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + ".vericairn" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                string target = Path.Combine(f.Root, Path.GetRelativePath(clone, file));
                if (File.Exists(target)) Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(file));
                else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
            }

            EstateModel merged = f.Load();
            Assert.False(merged.Findings.HasErrors, string.Join('\n', merged.Findings));
            HistoryState history = SignedHistory.Of(merged);
            Assert.Equal(new[] { ours.Id, theirs.Id }.Order(StringComparer.Ordinal), history.Heads.Select(h => h.Id));
            Assert.True(history.IsWitnessed(here.Id) && history.IsWitnessed(there.Id));

            // The next checkpoint names both heads and witnesses the union.
            RecordEnvelope joined = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
            merged = f.Load();
            Assert.False(merged.Findings.HasErrors, string.Join('\n', merged.Findings));
            Assert.Equal(2, joined.Body.GetProperty("previous").GetArrayLength());
            Assert.Equal(joined.Id, Assert.Single(SignedHistory.Of(merged).Heads).Id);
            Assert.Equal(merged.Records.Count - 1, SignedHistory.Of(merged).Witnessed);
            Assert.Equal(merged.Records.Count - 1, joined.Body.GetProperty("records").GetInt32());
        }
        finally { Directory.Delete(clone, recursive: true); }
    }

    [Fact]
    public void TwoCheckpointsThatDisagreeAboutARecordAreAnAlteration()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-11"), signer: f.OperatorSigner);
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-history-clone-" + Identity.Ulid.NewUlid().Value);
        try
        {
            CopyDirectory(f.Root, clone);
            f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
            // The other clone holds another version of the same record and witnesses that one.
            File.Delete(Path.Combine(clone, order.ExpectedPath));
            JsonObject body = HistoryFixture.Body(order);
            body["title"] = "Another version";
            AuthoredRecord version = RecordAuthor.Create(new RecordDraft("work-order", order.By, PolicyFixture.Element(body), null, null, order.AtTime, order.Id, 3), [f.OperatorSigner]);
            RecordAuthor.Write(clone, version);
            CheckpointDraft draft = SignedHistory.Prepare(EstateLoader.Load(clone), "interval");
            foreach (byte[] bytes in draft.Objects) WriteObject(f.Root, bytes);
            f.Estate.Add("history-checkpoint", f.Estate.BuilderId, draft.Body.GetRawText(), signer: f.BuilderSigner);
            EstateModel model = f.Load();
            Assert.Contains(model.Findings, x => x.Code == "history.altered" && x.Location == order.ExpectedPath && x.Message.Contains("different hashes", StringComparison.Ordinal));
            Assert.False(model.Counts(model.ById[order.Id]));

            // The root operator acknowledges the removal of the version that is not here, and the one present counts again.
            f.Checkpoint(removed: [(order.Id, version.Envelope.ComputedHash, "The other clone's version was replaced.")]);
            model = f.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.True(model.Counts(model.ById[order.Id]));
        }
        finally { Directory.Delete(clone, recursive: true); }
    }

    [Fact]
    public void ACheckpointInAnEarlierFormatOrFollowingAnotherTypeWitnessesNothingItClaims()
    {
        using var f = new HistoryFixture();
        RecordEnvelope adoption = f.Adopt();
        EstateModel model = f.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, "manual");
        f.StoreObjects(draft);
        // A format-2 record of the type is refused and counts for nothing.
        RecordEnvelope early = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, draft.Body.GetRawText(), signer: f.OperatorSigner, format: 2);
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "record.type-format" && x.Location == early.ExpectedPath);
        Assert.False(model.Counts(early));
        Assert.Equal(adoption.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
        File.Delete(f.PathOf(early));

        JsonObject body = JsonNode.Parse(SignedHistory.Prepare(f.Load(), "manual").Body.GetRawText())!.AsObject();
        body["previous"] = new JsonArray(new JsonObject { ["id"] = f.OperatorDevice.Id, ["hash"] = f.OperatorDevice.ComputedHash });
        RecordEnvelope wrong = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, body.ToJsonString(), signer: f.OperatorSigner);
        Assert.Contains(f.Load().Findings, x => x.Code == "history.checkpoint-invalid" && x.Location.StartsWith(wrong.ExpectedPath, StringComparison.Ordinal) && x.Message.Contains("not a format-3 history checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpectRefusesACopyThatLacksTheCheckpointAndAcceptsOneThatHoldsIt()
    {
        using var f = new HistoryFixture();
        RecordEnvelope adoption = f.Adopt();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-12"), signer: f.OperatorSigner);
        RecordEnvelope newest = f.Checkpoint();
        EstateModel model = f.Load();
        Assert.Null(SignedHistory.Expect(model, newest.Id));
        Assert.Null(SignedHistory.Expect(model, newest.ComputedHash));
        Assert.Null(SignedHistory.Expect(model, newest.ComputedHash[ContentHash.Prefix.Length..][..12]));
        Assert.Null(SignedHistory.Expect(model, adoption.Id));
        Assert.Equal("history.expected-missing", SignedHistory.Expect(model, "abc")!.Code);

        // A copy whose newest history was removed with the tail still validates; only the expected head shows what it lacks.
        f.Remove(newest);
        File.Delete(Path.Combine(f.Root, model.OfType("work-order").Single(r => EstateModel.BodyString(r, "key") == "HIS-12").ExpectedPath));
        model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Finding missing = SignedHistory.Expect(model, newest.Id)!;
        Assert.Equal("history.expected-missing", missing.Code);
        Assert.Equal(Severity.Error, missing.Severity);
        Assert.Equal("history.expected-missing", SignedHistory.Expect(model, newest.ComputedHash[ContentHash.Prefix.Length..][..12])!.Code);
    }

    [Fact]
    public void ColdStartStatusIsIdenticalOnEveryCopy()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("HIS-13"), signer: f.BuilderSigner);
        f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("HIS-14"), signer: f.BuilderSigner);
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-history-copy-" + Identity.Ulid.NewUlid().Value);
        try
        {
            CopyDirectory(f.Root, copy);
            string first = EstateStatus.RenderJson(f.Load(), new FindingList());
            string second = EstateStatus.RenderJson(EstateLoader.Load(copy), new FindingList());
            Assert.Equal(first, second);
            using JsonDocument status = JsonDocument.Parse(first);
            JsonElement history = status.RootElement.GetProperty("history");
            Assert.True(history.GetProperty("adopted").GetBoolean());
            Assert.Equal(1, history.GetProperty("heads").GetArrayLength());
            Assert.Equal(2, history.GetProperty("tail").GetInt32());
        }
        finally { Directory.Delete(copy, recursive: true); }
    }

    [Fact]
    public void RecordsThatDoNotCountGrantNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Estate.Add("decision", f.Estate.OperatorId, """{"title":"Basis","statement":"Grant authority.","summary":"A basis.","synthesis":"For tests.","effects":["grant"],"affects":[],"decidedBy":"OPERATOR","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["publication"]}""".Replace("OPERATOR", f.Estate.OperatorId, StringComparison.Ordinal), signer: f.OperatorSigner);
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-15"), signer: f.OperatorSigner);
        RecordEnvelope ask = f.Estate.Add("ask", f.Estate.BuilderId, $$"""{"to":"{{f.Estate.OperatorId}}","question":"Proceed?","recommendation":"Proceed.","consequence":"Work waits.","state":"open"}""", order.Reference, signer: f.BuilderSigner);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope charter = f.Require();
        f.Adopt();
        f.Estate.SetClock(charter.AtTime.AddMinutes(-5));
        // Backdated and unsigned: a grant, a role, an acceptance and a status that no rule may read.
        RecordEnvelope grant = f.Estate.Add("authority", f.Estate.OperatorId, $$"""{"grantee":"{{f.Estate.BuilderId}}","scope":["accept-work","administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""");
        RecordEnvelope promoted = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Test Builder","roles":["operator"]}""", supersedes: f.Estate.BuilderId);
        RecordEnvelope acceptance = f.Estate.Add("acceptance", f.Estate.OperatorId, """{"basis":[],"note":"Forged."}""", order.Reference);
        RecordEnvelope status = f.Estate.Add("work-status", f.Estate.OperatorId, """{"status":"in-progress","note":"Forged."}""", order.Reference);
        RecordEnvelope answer = f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Answer","statement":"Proceed.","summary":"A forged answer.","synthesis":"None.","effects":["proceed"],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"{{RecordParser.FormatTimestamp(f.Estate.Clock)}}","stake":"medium","notAuthorized":["publication"],"answers":["{{ask.Id}}"]}""");
        EstateModel model = f.Load();
        foreach (RecordEnvelope forged in new[] { grant, promoted, acceptance, status, answer })
        {
            Assert.False(model.Counts(forged), forged.Type);
            Assert.Contains(model.Findings, x => x.Code == "history.unanchored" && x.Location == forged.ExpectedPath);
            Assert.False(model.IsCurrent(forged));
            Assert.Contains(model.Records, r => r.Id == forged.Id);
        }

        Assert.False(model.HasScope(f.Estate.BuilderId, Scopes.AcceptWork));
        Assert.False(model.HasScope(f.Estate.BuilderId, Scopes.Administer));
        Assert.False(model.HasRole(f.Estate.BuilderId, Roles.Operator));
        Assert.Equal("agent", EstateModel.BodyString(model.CurrentPrincipal(f.Estate.BuilderId)!, "kind"));
        Assert.Equal(f.Estate.BuilderId, model.CurrentPrincipal(f.Estate.BuilderId)!.Id);
        Assert.Equal("planned", EstateStatus.WorkItems(model).Single(i => i.Key == "HIS-15").Status);
        Assert.Null(EstateStatus.StatusRecord(model, order));
        Assert.Empty(model.About(order.Id, "acceptance"));
        Assert.DoesNotContain(model.CountingRecords, r => r.Id == grant.Id);
        Assert.Equal("open", Asks.State(model, ask).State);
        Assert.Empty(Asks.State(model, ask).Answers);
    }

    [Fact]
    public void BeforeAdoptionARecordFailingRequiredSignaturesGrantsNothingEither()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-16"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope acceptance = f.Estate.Add("acceptance", f.Estate.OperatorId, """{"basis":[],"note":"Unsigned after the boundary."}""", order.Reference);
        EstateModel model = f.Load();
        Assert.False(SignedHistory.Adopted(model));
        Assert.Contains(model.Findings, x => x.Code == "signature.required" && x.Location == acceptance.ExpectedPath);
        Assert.False(model.Counts(acceptance));
        Assert.Equal("planned", EstateStatus.WorkItems(model).Single(i => i.Key == "HIS-16").Status);
    }

    [Fact]
    public void AnUnsignedDeviceRevisionChangesNoKey()
    {
        using var f = new HistoryFixture();
        JsonObject body = HistoryFixture.Body(f.BuilderDevice);
        body["name"] = "Renamed without a signature";
        RecordEnvelope rename = f.Estate.Add("device", f.Estate.BuilderId, body.ToJsonString(), supersedes: f.BuilderDevice.Id);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "signature.required" && x.Location == rename.ExpectedPath);
        Assert.False(model.Counts(rename));
        Assert.Equal(f.BuilderDevice.Id, model.Current("device").Single(d => EstateModel.BodyString(d, "principal") == f.Estate.BuilderId).Id);
    }

    [Fact]
    public void TheCheckpointValidatorRefusesMalformedShapes()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        EstateModel model = f.Load();
        RecordEnvelope checkpoint = model.OfType("history-checkpoint").Single();
        Assert.Equal(3, checkpoint.Format);
        foreach ((string field, JsonNode? value, string code) in new (string, JsonNode?, string)[]
        {
            ("trigger", JsonValue.Create("weekly"), "body.enum"),
            ("trigger", JsonValue.Create("interval"), "history.checkpoint-invalid"),
            ("closes", new JsonObject { ["kind"] = "device", ["id"] = f.OperatorDevice.Id }, "history.checkpoint-invalid"),
            ("anchored", new JsonArray(new JsonObject { ["id"] = checkpoint.Id, ["hash"] = checkpoint.ComputedHash }), "history.checkpoint-invalid"),
            ("unknown", JsonValue.Create(true), "body.unknown-key"),
        })
        {
            JsonObject body = HistoryFixture.Body(checkpoint);
            body[field] = value;
            AuthoredRecord changed = RecordAuthor.Create(new RecordDraft("history-checkpoint", checkpoint.By, PolicyFixture.Element(body), null, null, checkpoint.AtTime, checkpoint.Id, 3));
            var findings = new FindingList();
            new HistoryCheckpointType().Validate(new ValidationContext(model, changed.Envelope, findings));
            Assert.Contains(findings, x => x.Code == code);
        }

        // A closing checkpoint's shape is accepted; closing semantics belong to ID-06.
        JsonObject closing = HistoryFixture.Body(checkpoint);
        closing["trigger"] = "closing";
        closing["previous"] = new JsonArray(new JsonObject { ["id"] = checkpoint.Id, ["hash"] = checkpoint.ComputedHash });
        closing["closes"] = new JsonObject { ["kind"] = "key", ["id"] = f.OperatorDevice.Id };
        var ok = new FindingList();
        new HistoryCheckpointType().Validate(new ValidationContext(model, RecordAuthor.Create(new RecordDraft("history-checkpoint", checkpoint.By, PolicyFixture.Element(closing), null, null, checkpoint.AtTime, Identity.Ulid.NewUlid().Value, 3)).Envelope, ok));
        Assert.False(ok.HasErrors, string.Join('\n', ok));
        // A format-2 record of the type is refused, as an older format-3 reader refuses the unknown type.
        Assert.Equal(3, RecordTypeRegistry.Find("history-checkpoint")!.SinceFormat);
    }

    [Fact]
    public void InventoriesAreSortedLinesSplitUnderTheObjectLimit()
    {
        using var f = new HistoryFixture();
        for (int i = 0; i < 12; i++) f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("INV-" + i), signer: f.OperatorSigner);
        RecordEnvelope checkpoint = f.Checkpoint(maxObjectBytes: 512);
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        JsonElement objects = checkpoint.Body.GetProperty("inventory");
        Assert.True(objects.GetArrayLength() > 1);
        var lines = new List<string>();
        foreach (JsonElement hash in objects.EnumerateArray())
        {
            byte[] bytes = File.ReadAllBytes(f.ObjectPath(hash.GetString()!));
            Assert.True(bytes.Length <= 512);
            Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            lines.AddRange(Encoding.UTF8.GetString(bytes).TrimEnd('\n').Split('\n'));
        }

        Assert.Equal(model.Records.Where(r => r.Id != checkpoint.Id).OrderBy(r => r.Id, StringComparer.Ordinal).Select(HistoryInventory.LineOf), lines);
        Assert.All(lines, l => Assert.Matches(@"^[a-z-]+/\d{4}-\d{2}/[0-9A-Z]{26}\.json sha256:[0-9a-f]{64}$", l));
        Assert.Equal(model.Records.Count - 1, checkpoint.Body.GetProperty("records").GetInt32());
        Assert.Equal(RecordSets.Hash(model.Records.Where(r => r.Id != checkpoint.Id).Select(r => (r.Id, r.ComputedHash))), checkpoint.Body.GetProperty("recordSetHash").GetString());
        Assert.NotEmpty(HistoryInventory.Read(Encoding.UTF8.GetBytes("work-order/2026-10/01J00000000000000000000000.json sha256:00\n")).Problems);
        Assert.NotEmpty(HistoryInventory.Read(Encoding.UTF8.GetBytes(lines[1] + "\n" + lines[0] + "\n")).Problems);
    }

    [Fact]
    public void TheRecordSetHashIsTheOneEstateObservationsState()
    {
        using var f = new HistoryFixture();
        EstateModel model = f.Load();
        var identities = new JsonArray([.. model.Records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["hash"] = r.ComputedHash })]);
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(identities))), RecordSets.Hash(model.Records.Select(r => (r.Id, r.ComputedHash))));
    }

    [Fact]
    public void AWrongCountOrSetHashOnAHeadIsInvalid()
    {
        using var f = new HistoryFixture();
        EstateModel model = f.Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, "adoption");
        f.StoreObjects(draft);
        JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        body["records"] = body["records"]!.GetValue<int>() + 1;
        RecordEnvelope wrong = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, body.ToJsonString(), signer: f.OperatorSigner);
        Assert.Contains(f.Load().Findings, x => x.Code == "history.checkpoint-invalid" && x.Location.StartsWith(wrong.ExpectedPath, StringComparison.Ordinal));
        Assert.Null(SignedHistory.AuthoringRefusal(model, draft.Body));
        Assert.NotNull(SignedHistory.AuthoringRefusal(model, PolicyFixture.Element(body)));
    }

    private static RecordEnvelope AddTo(string root, string type, string by, string body, DateTimeOffset at, Signing.IRecordSigner signer)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, null, null, at, null, 3), [signer]);
        RecordAuthor.Write(root, record);
        return record.Envelope;
    }

    private static void WriteObject(string root, byte[] bytes)
    {
        string path = Path.Combine(root, EstateLayout.ObjectsDirectory, ContentHash.Sha256(bytes)[ContentHash.Prefix.Length..]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    internal static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
