// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Store;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Writing and adopting history checkpoints (design/signed-history.md §§ 9 and 10; OPS-07): <c>estate checkpoint</c> and its adoption,
/// the checkpoints the runtime writes by itself in the same batch as an act, and the machine's memory of the newest checkpoint it verified.
/// </summary>
public sealed partial class EstateService
{
    private const string Adoption = "adoption";
    private const string Interval = "interval";
    private const string ClosingTrigger = "closing";

    /// <summary>How long before the commit that added it a record's stated time may be before adoption's check points it out.</summary>
    private static readonly TimeSpan BackdatingNotice = TimeSpan.FromDays(1);

    /// <summary>A test's Vericairn home for the machine's memory of history; the session's home when null.</summary>
    internal string? HistoryMemoryHome { get; set; }

    /// <summary>A prepared checkpoint and the inventory objects this preparation stored, which a refusal removes again.</summary>
    private sealed record PreparedCheckpoint(AuthoredRecord Record, IReadOnlyList<string> NewObjects);

    /// <summary>What this clone's memory says of the copy: the newest checkpoint verified, whether the copy holds it counting, and whether the main line still does (null when that cannot be told).</summary>
    private sealed record MemoryStanding(HistoryMemory.Verified Verified, bool CopyHolds, bool? MainHolds);

    /// <inheritdoc />
    public (HistoryCheckpointResponse? Checkpoint, Problem? Problem) HistoryCheckpoint(HistoryCheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string trigger = request.Adopt ? Adoption : "manual";
        lock (gate)
        {
            EstateModel model = session.Load();
            HistoryState state = SignedHistory.Of(model);
            if (request.Adopt && state.Adopted)
                return (null, new Problem("refused", $"This estate has adopted history checkpoints already; its {(state.Heads.Count == 1 ? "head is" : "heads are")} {string.Join(", ", state.Heads.Select(h => h.Id))}. Write a checkpoint with vericairn estate checkpoint --by <principal>.", []));
            if (!request.Adopt && !state.Adopted)
                return (null, new Problem("refused", "This estate has not adopted history checkpoints, and until it does only its adoption writes one. Read what adoption would witness and anchor with vericairn estate checkpoint --adopt --check; the root operator then signs it once with vericairn estate checkpoint --adopt --expect-plan <plan hash> --by <root operator>.", []));
            if (!request.Adopt && request.ExpectPlan is not null)
                return (null, new Problem("refused", "--expect-plan names an adoption's plan; a checkpoint written without --adopt anchors nothing.", []));
            if (model.Manifest is not { RecordFormat: >= 3 })
                return (null, new Problem("refused", "History checkpoints are records of format 3; upgrade the estate to record format 3 first.", []));
            // A copy whose newest verified checkpoint is gone may have been rewritten: adopting it would witness and anchor what the rewrite
            // left, a backdated record among them, so neither the adoption nor its check goes ahead (design/signed-history.md § 7).
            if (request.Adopt && RolledBackRefusal(model) is string rolledBack) return (null, new Problem("refused", rolledBack, []));
            if (!request.Check && (request.By is null || !Ulid.IsValid(request.By)))
                return (null, new Problem("refused", "The principal whose admitted device signs the checkpoint must be a ULID: pass --by.", []));
            if (request.Adopt && !request.Check && AdoptionRefusal(model, request.By!) is string refusal)
                return (null, new Problem("refused", refusal, []));
            if (!request.Check && RequestDeviceRefusal(model) is Problem denied) return (null, denied);
            if (!request.Check && requestDevice.Value is { Closed: false, Signing: not null })
                return (null, new Problem("refused", "A history checkpoint is signed on the runtime's machine by a device it holds; a device-signed request cannot write one.", []));

            if (request.Check) return (Plan(model, trigger, request.Adopt, null), null);
            if (session.IsReadOnly) return (null, new Problem("refused", "This copy is opened read-only; nothing was written.", []));

            // The checkpoint follows the heads and lists the tail of the files as they are under the working tree's write lock.
            return UnderWriteLock(() =>
            {
                EstateModel current = LoadWith([]);
                if (request.Adopt && SignedHistory.Adopted(current)) return ((HistoryCheckpointResponse?)null, (Problem?)new Problem("refused", "Another writer adopted history checkpoints first; nothing was written.", []));
                IReadOnlyList<RecordEnvelope>? anchored = null;
                if (request.Adopt)
                {
                    AdoptionPlan plan = SignedHistory.PlanAdoption(current);
                    if (PlanRefusal(plan, request.ExpectPlan) is string mismatch) return (null, new Problem("refused", mismatch, []));
                    anchored = plan.Anchored;
                }

                // The files the adoption judged, so that its response lists what the at-time reading changes in the copy it signed.
                IReadOnlyList<RecordFile>? judged = request.Adopt ? session.Files() : null;
                (PreparedCheckpoint? checkpoint, string? skipped) = CheckpointLocked(current, trigger, request.By!, anchored);
                if (checkpoint is null) return (null, new Problem("refused", skipped ?? "No history checkpoint could be written.", []));
                (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([checkpoint.Record], request.Adopt ? "Adopt history checkpoints." : "Write a history checkpoint.", null, null, null);
                if (written.Count == 0)
                {
                    RemoveObjects(checkpoint);
                    return (null, problem ?? new Problem("refused", "The checkpoint was not written.", []));
                }

                EstateModel after = LoadWith([]);
                return (Plan(current, trigger, request.Adopt, written[0], judged) with { Adopted = SignedHistory.Adopted(after) }, problem);
            }, problem => (null, problem));
        }
    }

    /// <summary>
    /// Why an adoption may not anchor the plan given (design/signed-history.md § 10): an adoption that anchors anything is bound to the
    /// check its signer read, by the hash of the anchored set the check printed, so that what is signed is what was read.
    /// </summary>
    private static string? PlanRefusal(AdoptionPlan plan, string? expected)
    {
        if (expected is null)
            return plan.Anchored.Count == 0 ? null
                : $"Adoption anchors {plan.Anchored.Count} unsigned records. Read them with vericairn estate checkpoint --adopt --check and pass the plan hash it prints with --expect-plan, so that the adoption anchors exactly what you read. Nothing was signed.";
        string named = expected.Trim();
        if (!named.StartsWith(ContentHash.Prefix, StringComparison.Ordinal)) named = ContentHash.Prefix + named;
        return string.Equals(named, plan.AnchoredHash, StringComparison.OrdinalIgnoreCase) ? null
            : "The records adoption would anchor are not the plan you named: the estate changed since that check, or the hash is another plan's. Read vericairn estate checkpoint --adopt --check again. Nothing was signed.";
    }

    /// <summary>
    /// Why a principal may not adopt (design/signed-history.md §§ 6 and 10): adoption anchors records and changes what counts for the whole
    /// estate, so it is the root operator's, or a person's holding administer now through a grant that rests on signatures, as the history
    /// judges anchors, never an agent's. The model then judges the signed adoption as it judges every checkpoint, its devices included.
    /// </summary>
    private static string? AdoptionRefusal(EstateModel model, string by) =>
        model.Trust.AnchorerAt(by, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)) ? null
            : $"Adopting history checkpoints anchors the unsigned records that count today and changes what counts in the whole estate, so it is signed by the root operator or a person holding administer through a grant that rests on signatures, never an agent; principal {by} is neither. Nothing was written.";

    /// <summary>
    /// What a checkpoint witnesses and anchors, or would, with the commits that added the anchored records and the deletions in history;
    /// for adoption also the records that read otherwise at their own time, judged from the files given or the copy's files now.
    /// </summary>
    private HistoryCheckpointResponse Plan(EstateModel model, string trigger, bool adopt, RecordWritten? written, IReadOnlyList<RecordFile>? files = null)
    {
        HistoryState state = SignedHistory.Of(model);
        IReadOnlyList<RecordEnvelope> tail = SignedHistory.TailRecords(model);
        List<HistoryCheckpointPin> heads = [.. state.Heads.Select(h => new HistoryCheckpointPin(h.Id, h.Hash!))];
        if (!adopt)
            return new HistoryCheckpointResponse(trigger, written is not null, state.Adopted, state.Witnessed + tail.Count, tail.Count, heads, [], [], [], [], null, written);

        AdoptionPlan plan = SignedHistory.PlanAdoption(model);
        var unavailable = new List<string>();
        ISourceAdapter? git = model.Manifest?.Profile == "repository-synced" ? Providers.Source("git") : null;
        bool repository = IsRepository(model.Root);
        RecordOrigins? origins = git is not null && repository ? git.RecordFileOrigins(model.Root) : null;
        if (git is null) unavailable.Add("The commits that added records and the record files deleted in history come from the git adapter, which does not serve this copy.");
        else if (!repository) unavailable.Add("This estate is not a git repository, so the commits that added records and the record files deleted in history cannot be listed.");
        else if (origins is null) unavailable.Add("The commits that added records could not be read from the repository.");
        else if (origins.Shallow) unavailable.Add("This clone is shallow, so the commits that added records and the record files deleted in history cannot be listed; a full clone lists them.");
        bool complete = origins is { Shallow: false };
        List<HistoryDeletedFile> deleted = [];
        if (git is not null && repository && git.DeletedRecordFiles(model.Root) is { Shallow: false } deletions)
            deleted = [.. deletions.Deleted.Where(d => model.FilePaths?.Contains(d.Path) != true && !model.ById.ContainsKey(Path.GetFileNameWithoutExtension(d.Path))).Select(d => new HistoryDeletedFile(d.Path, d.Commit))];
        else if (git is not null && repository && complete) unavailable.Add("The record files deleted in history could not be read from the repository.");

        IReadOnlySet<string> migration = model.Trust.MigrationAnchors;
        HistoryRecordItem Item(RecordEnvelope record, string? device = null)
        {
            string? commit = origins?.Added.GetValueOrDefault(record.ExpectedPath);
            DateTimeOffset? committed = origins is not null && origins.AddedAt.TryGetValue(record.ExpectedPath, out DateTimeOffset at) ? at : null;
            var notes = new List<string>();
            if (complete && commit is null) notes.Add("no commit holds this file yet");
            if (committed is { } time && record.AtTime < time - BackdatingNotice)
                notes.Add($"dated {record.At}, more than a day before the commit that added it ({RecordParser.FormatTimestamp(time)})");
            if (plan.AlreadyAnchored.Contains(record.Id))
                notes.Add(migration.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))
                    ? "already anchored by the format-2 migration; adoption lists it again" : "already anchored; adoption lists it again");
            return new(record.Id, record.Type, record.At, record.ExpectedPath, commit, device, committed is { } c ? RecordParser.FormatTimestamp(c) : null,
                notes.Count == 0 ? null : string.Join("; ", notes));
        }

        List<string> uncommitted = complete ? [.. model.Records.Select(r => r.ExpectedPath).Where(p => !origins!.Added.ContainsKey(p)).Order(StringComparer.Ordinal)] : [];
        // What the at-time reading changes (design/signed-history.md § 10): the same files judged with every record's roles and grants
        // read at its own time, as the records no adoption witnesses are read once the estate adopts.
        EstateModel atTime = session.LoadFilesAtRecordTime(files ?? session.Files());
        List<HistoryRecordItem> changes = [.. AtTimeReading.Changes(model, atTime).Select(c => Item(c.Record) with { Note = c.Note })];
        return new HistoryCheckpointResponse(trigger, written is not null, state.Adopted, plan.Witnessed, tail.Count, heads,
            [.. plan.Anchored.Select(r => Item(r))], [.. plan.NotCounting.Select(r => Item(r))], [.. plan.ByRevokedDevices.Select(r => Item(r.Record, r.Device))],
            deleted, unavailable.Count == 0 ? null : string.Join(" ", unavailable), written) { PlanHash = plan.AnchoredHash, Uncommitted = uncommitted, AuthorityChanges = changes };
    }

    private static bool IsRepository(string root) => Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"));

    /// <summary>
    /// Prepares and signs a checkpoint over a model, its inventory objects stored first, signed by the principal's admitted device held on
    /// this machine: the device then writing (design/signed-history.md § 9). Null with the reason when it cannot be; null without one when
    /// no checkpoint is asked: before adoption, for any trigger but adoption, and for an interval when the device then writing is a
    /// person's with presence, which is never asked for one, or there is none. The model must be read under the write lock when the
    /// checkpoint is stored with it. Objects this preparation stored are removed again when it fails, and by the caller when the
    /// checkpoint is not stored, so a refused checkpoint leaves none behind.
    /// </summary>
    /// <remarks>
    /// Closings (design/signed-history.md § 8, ID-06) use it through <see cref="StoreWithClosingLocked"/>: a revocation, key rotation or
    /// narrowing grant revision stores, in one batch and before its own record, the checkpoint this prepares over the copy as it is, with
    /// trigger closing, <paramref name="closes"/> naming what it closes, <paramref name="signer"/> the key that performs the act, since only
    /// someone who could perform the act signs its closing, and <paramref name="at"/> the act's own time, so that the closing is dated no
    /// later than the act it precedes.
    /// </remarks>
    private (PreparedCheckpoint? Checkpoint, string? Skipped) CheckpointLocked(EstateModel model, string trigger, string by, IEnumerable<RecordEnvelope>? anchored = null,
        (string Kind, string Id)? closes = null, IRecordSigner? signer = null, DateTimeOffset? at = null)
    {
        if (requestDevice.Value is { Closed: false, Signing: not null })
            return (null, trigger == Interval ? null : "A device-signed request signs only its own record, so no history checkpoint was written with it; the next checkpoint witnesses it.");
        if (trigger != Adoption && !SignedHistory.Adopted(model)) return (null, null);
        using JsonDocument probe = JsonDocument.Parse("{}");
        if (signer is null && (ResolveSigner(model, new RecordDraft("history-checkpoint", by, probe.RootElement), out signer) is not null || signer is null))
            return (null, trigger == Interval ? null : $"Principal {by} has no admitted device whose key this machine holds, so no history checkpoint with trigger {trigger} was written; the next checkpoint witnesses what this one would have.");
        if (trigger == Interval && model.Current("device").FirstOrDefault(d => model.Chain(d)[0] == signer.DeviceId) is { } device && EstateModel.BodyString(device, "presence") == "required")
            return (null, null);

        var stored = new List<string>();
        CheckpointDraft draft;
        try
        {
            draft = SignedHistory.Prepare(model, trigger, anchored, closes: closes);
        }
        catch (InvalidOperationException exception)
        {
            // The history's own refusals, which name records and checkpoints and no path.
            return (null, $"No history checkpoint with trigger {trigger} was written: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, $"No history checkpoint with trigger {trigger} was written: the heads' inventory objects could not be read.");
        }

        try
        {
            foreach (byte[] bytes in draft.Objects)
            {
                string hash = ContentHash.Sha256(bytes);
                if (session.Objects.Has(hash)) continue;
                session.Objects.Put(bytes);
                stored.Add(hash);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Described by its kind: an object store's own message may name a path on this machine.
            RemoveObjects(stored);
            return (null, $"No history checkpoint with trigger {trigger} was written: its inventory could not be stored in the estate's objects ({(exception is UnauthorizedAccessException ? "access was denied" : "the store refused it")}).");
        }

        (AuthoredRecord? checkpoint, string? refusal) = AuthorLocked(new RecordDraft("history-checkpoint", by, draft.Body, At: at), model, [signer]);
        if (checkpoint is null || checkpoint.Envelope.Signatures.Count == 0)
        {
            RemoveObjects(stored);
            return (null, $"No history checkpoint with trigger {trigger} was written: {refusal ?? "it would be unsigned."}");
        }

        return (new PreparedCheckpoint(checkpoint, stored), null);
    }

    private void RemoveObjects(PreparedCheckpoint checkpoint) => RemoveObjects(checkpoint.NewObjects);

    // Content-addressed objects this preparation added and nothing stored refers to; one that cannot be removed stays, inert.
    private void RemoveObjects(IEnumerable<string> hashes)
    {
        foreach (string hash in hashes)
        {
            try { session.Objects.Remove(hash); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
    }

    /// <summary>
    /// The checkpoint a single write sets off (design/signed-history.md § 9; OPS-07), or null: the root operator's first device admitting
    /// itself in a newborn estate adopts at birth; in an estate that has adopted, a charter revision writes one with trigger charter, and
    /// any other write signed by this machine's device for its author one with trigger interval once the tail is due. Only a write this
    /// machine signs for its author sets one off: a device-signed request, a record presigned elsewhere and a write signed with keys the
    /// caller names, such as a device signing its own record, set off none, so the runtime never signs or anchors for a signer elsewhere.
    /// </summary>
    private string? TriggerOf(RecordDraft draft, EstateModel current, IReadOnlyList<IRecordSigner>? signers)
    {
        if (signers is not null || draft.Type == "history-checkpoint" || requestDevice.Value is { Closed: false, Signing: not null } || session.IsReadOnly) return null;
        if (draft.Type == "admission" && Newborn(current, draft)) return Adoption;
        if (!SignedHistory.Adopted(current)) return null;
        if (draft.Type == "estate" && draft.Supersedes is not null) return "charter";
        return SignedHistory.IntervalDue(current, DateTimeOffset.UtcNow) ? Interval : null;
    }

    /// <summary>
    /// Whether an admission is the first authority of a newborn estate, which adopts at birth (design/signed-history.md § 10: estate init
    /// adopts at birth): the root operator admitting its own first device while the estate holds nothing but what estate init wrote, the
    /// root operator's devices and no checkpoint. An estate with any other record adopts as the operator's explicit step instead.
    /// </summary>
    private static bool Newborn(EstateModel model, RecordDraft draft)
    {
        string? root = model.RootOperator;
        if (root is null || draft.By != root || draft.Subject is not { Type: "device" } subject || SignatureChecks.FirstAuthority(model, root) is not null) return false;
        if (draft.Body.ValueKind != JsonValueKind.Object || !draft.Body.TryGetProperty("status", out JsonElement status) || status.ValueKind != JsonValueKind.String || status.GetString() != "admitted" || model.Manifest is not { RecordFormat: >= 3 }) return false;
        if (!model.ById.TryGetValue(subject.Id, out RecordEnvelope? device) || EstateModel.BodyString(device, "principal") != root) return false;
        return model.Records.All(r => r.Type switch
        {
            "estate" => r.Supersedes is null && r.Id == model.Manifest.Estate,
            "principal" => r.Id == root && r.Supersedes is null,
            "observation" => VericairnReleaseObservation.Is(r) && model.Manifest.Release?.Id == r.Id,
            "device" => EstateModel.BodyString(r, "principal") == root,
            _ => false,
        });
    }

    /// <summary>
    /// Stores a record with the checkpoint its trigger writes, in one batch, the record first. A checkpoint that cannot be written never
    /// blocks the record: the record is judged and stored alone, and the outcome says why no checkpoint was written. Caller holds the gate
    /// and the write lock.
    /// </summary>
    private WriteOutcome StoreWithCheckpointLocked(AuthoredRecord record, EstateModel current, string trigger, string by)
    {
        // A copy whose newest verified checkpoint is gone may have been rewritten back to its birth: adopting it would witness and
        // anchor what the rewrite left, so the first authority is written alone and says why (design/signed-history.md § 7).
        if (trigger == Adoption && RolledBackRefusal(current, "The admission was written without the adoption checkpoint.") is string rolledBack)
        {
            WriteOutcome first = StoreAuthoredLocked(record, current);
            return first.Written is null ? first : first with { Checkpoint = new TriggeredCheckpoint(trigger, null, rolledBack) };
        }

        EstateModel withRecord = LoadWith([record]);
        IReadOnlyList<RecordEnvelope>? anchored = trigger switch
        {
            Adoption => SignedHistory.PlanAdoption(withRecord).Anchored,
            "charter" => CharterAnchors(current, record.Envelope),
            _ => null,
        };
        (PreparedCheckpoint? checkpoint, string? skipped) = CheckpointLocked(withRecord, trigger, by, anchored);
        if (checkpoint is null)
        {
            WriteOutcome alone = StoreAuthoredLocked(record, current);
            return skipped is null || alone.Written is null ? alone : alone with { Checkpoint = new TriggeredCheckpoint(trigger, null, skipped) };
        }

        (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([record, checkpoint.Record],
            $"Store the record and the history checkpoint its {trigger} trigger writes with it.", null, null, null);
        if (written.Count == 0)
        {
            RemoveObjects(checkpoint);
            WriteOutcome alone = StoreAuthoredLocked(record, current);
            return alone.Written is null ? alone : alone with { Checkpoint = new TriggeredCheckpoint(trigger, null, "The history checkpoint was refused, so the record was written alone: " + Describe(problem)) };
        }

        // The record was stored; a checkpoint stopped after it is the checkpoint's failure alone, reported with it and never as the record's.
        RecordWritten? stored = written.Count > 1 ? written[1] : null;
        if (stored is null) RemoveObjects(checkpoint);
        return new WriteOutcome(written[0], stored is null ? null : problem) { Checkpoint = new TriggeredCheckpoint(trigger, stored, stored is null ? "The history checkpoint was not stored: " + Describe(problem) : null) };
    }

    /// <summary>
    /// What the closing before an act names, when the act needs one (design/signed-history.md § 8; OPS-07): in an estate that has adopted
    /// history checkpoints, a revocation, a key rotation or a grant revision that narrows the grant; null otherwise, and always null in an
    /// estate that has not adopted, which writes these acts as before.
    /// </summary>
    private static (string Kind, string Id)? ClosingRequired(EstateModel current, string type, JsonElement body, RecordReference? subject, string? supersedes) =>
        type is "admission" or "device" or "authority" && supersedes is not null && SignedHistory.Adopted(current)
            ? ClosingActs.For(current, type, body, subject, supersedes) : null;

    /// <summary>
    /// Stores an act with the closing before it (design/signed-history.md § 8; OPS-07): over the copy as it is under the write lock, a
    /// checkpoint with trigger closing naming what the act closes, signed by the key that performs the act (the revoking device or the
    /// operator's recovery device for a revocation, the device's current key for a rotation, the operator's device for a grant revision),
    /// dated no later than the act, then the act, in one batch. The judgment of the batch decides whether that signer may close: a
    /// closing it would not let act refuses both. In an estate that has adopted, an act whose closing cannot be written is refused, and
    /// nothing is written; one that has not adopted stores the act alone, as before. An act stated more than five minutes before what
    /// its closing would follow or witness is refused, since its closing would be dated before what it lists. When a batch stopped
    /// between a closing and its act, the closing is removed again where that can be done cleanly; one that stays, as after a process
    /// stopped between the two, is used by the act written again, which is then stored alone after it. Caller holds the gate and the
    /// write lock.
    /// </summary>
    private WriteOutcome StoreWithClosingLocked(AuthoredRecord record, EstateModel current, IReadOnlyList<IRecordSigner>? signers)
    {
        EstateModel copy = LoadWith([]);
        if (!SignedHistory.Adopted(copy) || ClosingActs.For(copy, record.Envelope) is not { } closes) return StoreAuthoredLocked(record, current);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (StatedEarlyRefusal(copy, record.Envelope, closes, now) is string early) return Refuse(early);

        // A closing stored earlier without its act, by the act's author, is the act's closing: the act follows it alone, since a closed
        // key or device could not sign another, and no new checkpoint witnesses what was written since (OPS-07).
        if (ClosingActs.Stored(copy, closes, record.Envelope.By) is { } stored)
        {
            if (SelfRevocationRefusal(copy, record.Envelope, closes, stored.Record.AtTime) is string late) return Refuse(late);
            WriteOutcome alone = StoreAuthoredLocked(record, current);
            return alone.Written is null ? alone : alone with
            {
                Checkpoint = new TriggeredCheckpoint(ClosingTrigger, null,
                    $"No closing was written with this {Act(closes.Kind)}: it follows closing {stored.Record.Id} ({stored.Record.ExpectedPath}), which an earlier attempt stored without its act."),
            };
        }

        IRecordSigner? closer = null;
        if (closes.Kind == "key")
        {
            // A key's closing is signed by its own device, with the key the rotation replaces.
            string identity = copy.Chain(copy.ById[closes.Id])[0];
            closer = signers?.FirstOrDefault(s => s.DeviceId == identity);
            if (closer is null && DeviceKeyStores.All().FirstOrDefault(s => s.Has(identity)) is { } store) closer = new StoreSigner(store, identity);
            if (closer is null) return Refuse(Unclosed(closes, $"a key's closing is signed by its own device, and this machine holds no key of device {identity}."));
        }
        else if (signers is { Count: > 0 })
        {
            closer = signers[0];
        }

        DateTimeOffset at = record.Envelope.AtTime < now ? record.Envelope.AtTime : now;
        if (SelfRevocationRefusal(copy, record.Envelope, closes, at) is string self) return Refuse(self);
        (PreparedCheckpoint? closing, string? skipped) = CheckpointLocked(copy, ClosingTrigger, record.Envelope.By, closes: closes, signer: closer, at: at);
        if (closing is null) return Refuse(Unclosed(closes, skipped ?? "no checkpoint could be prepared."));
        (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([closing.Record, record], $"Store the closing and the {Act(closes.Kind)} it precedes.", null, null, null);
        if (written.Count == 0)
        {
            RemoveObjects(closing);
            Problem refused = problem ?? new Problem("refused", "Nothing was written.", []);
            return WriteOutcome.Fail(refused with { Message = $"{refused.Message.TrimEnd()} {Needs(closes)} The act and its closing were judged together, and neither was written." });
        }

        var checkpoint = new TriggeredCheckpoint(ClosingTrigger, written[0], null);
        if (written.Count > 1) return new WriteOutcome(written[1], problem) { Checkpoint = checkpoint };

        // The batch stopped between the closing and its act. The closing is removed again when that can be done cleanly; otherwise it
        // stays and closes all the same (design/signed-history.md § 8), and the act written again follows it alone.
        string reason = Reason(problem);
        if (UndoStoredClosing(closing.Record, record))
        {
            RemoveObjects(closing);
            return Refuse($"The {Act(closes.Kind)} was not written: {reason} Its closing, stored just before it in the same batch, was removed again, so nothing was written: write the {Act(closes.Kind)} again.");
        }

        return new WriteOutcome(null, new Problem(ClosingStoredCode,
            $"The {Act(closes.Kind)} was not written: {reason} Closing {written[0].Id}, stored just before it in the same batch, was kept and closes {closes.Kind} {closes.Id} all the same, warned as history.closing-unfollowed until an act follows it. Writing the {Act(closes.Kind)} again as {record.Envelope.By} uses the stored closing: the {Act(closes.Kind)} is written after it, alone, with no new closing. Until then this machine reports the batch as a reconcile task (batch.incomplete).", []))
        { Checkpoint = checkpoint };
    }

    /// <summary>The outcome's code when a closing was stored and the act after it was not (OPS-07).</summary>
    internal const string ClosingStoredCode = "closing-stored";

    /// <summary>A test's hook, called before a closing whose act was not stored is removed again: a test throws from it to keep the closing.</summary>
    internal Action<AuthoredRecord>? BeforeUndoingClosing { get; set; }

    /// <summary>
    /// Removes again a closing whose act was not stored (OPS-07), when that can be done cleanly: the act is surely absent, the closing's
    /// file is removed and is gone, and the batch's journal is closed. False, with the closing kept, when any of that fails; a journal
    /// that cannot be closed names nothing stored and is forgotten at the next judgment of interrupted batches. Caller holds the gate
    /// and the write lock.
    /// </summary>
    private bool UndoStoredClosing(AuthoredRecord closing, AuthoredRecord act)
    {
        try
        {
            if (session.Holds(act)) return false;
            BeforeUndoingClosing?.Invoke(closing);
            session.Discard(closing);
            if (session.Holds(closing)) return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }

        try
        {
            foreach (BatchJournal journal in BatchJournal.All(session).Journals.Where(j => j.Records.Any(r => r.Id == closing.Envelope.Id))) journal.Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Nothing the journal names is stored now, so the next judgment of interrupted batches forgets it.
        }

        return true;
    }

    /// <summary>A store's refusal without its leading "Not written: ", to follow another sentence.</summary>
    private static string Reason(Problem? problem)
    {
        const string Prefix = "Not written: ";
        string message = problem?.Message.Trim() ?? "the batch stopped.";
        return message.StartsWith(Prefix, StringComparison.Ordinal) ? message[Prefix.Length..] : message;
    }

    /// <summary>
    /// Why an act a closing precedes is refused for the time it states (OPS-07): in an estate that has adopted, its closing is dated no
    /// later than the act, so an act stated more than five minutes before the newest record its closing would witness, or before the
    /// head checkpoint, would date that closing before what it lists and leave history.dated-after-witness on those records for good.
    /// A record or checkpoint dated past the runtime's clock is read at that clock. Null when the act's time stands.
    /// </summary>
    private static string? StatedEarlyRefusal(EstateModel copy, RecordEnvelope act, (string Kind, string Id) closes, DateTimeOffset now)
    {
        HistoryState state = SignedHistory.Of(copy);
        RecordEnvelope? newest = SignedHistory.TailRecords(copy)
            .Concat(state.Heads.Select(h => copy.ById.GetValueOrDefault(h.Id)).OfType<RecordEnvelope>())
            .OrderByDescending(r => r.AtTime).ThenByDescending(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        if (newest is null) return null;
        DateTimeOffset limit = newest.AtTime < now ? newest.AtTime : now;
        if (act.AtTime + SignedHistory.ClockTolerance >= limit) return null;
        string what = state.Heads.Any(h => h.Id == newest.Id) ? $"head checkpoint {newest.Id}" : $"{newest.Type} {newest.Id}";
        return $"This {Act(closes.Kind)} states {act.At}, more than five minutes before {what}, dated {newest.At}: in an estate that has adopted history checkpoints, the closing before such an act is dated no later than the act and follows or witnesses what this copy holds, so it cannot be dated before them. Write the {Act(closes.Kind)} again at the present time, without an earlier stated time. Nothing was written.";
    }

    /// <summary>
    /// Why a device's revocation of itself is refused for its revokedAt (OPS-07): from its closing on, a closed device's signature counts
    /// only on its own revocation from that closing's time on, so a revocation the device signs states revokedAt at or after the closing,
    /// here <paramref name="closingAt"/>, and after every other closing of the device. Null for any other act, or when it does.
    /// </summary>
    private static string? SelfRevocationRefusal(EstateModel copy, RecordEnvelope act, (string Kind, string Id) closes, DateTimeOffset closingAt)
    {
        if (closes.Kind != "device" || EstateModel.BodyString(act, "revokedAt") is not { } revokedAt || !copy.ById.TryGetValue(closes.Id, out RecordEnvelope? device)) return null;
        string identity = copy.Chain(device)[0];
        if (!act.Signatures.Any(s => copy.ById.TryGetValue(s.Device, out RecordEnvelope? signer) && signer.Type == "device" && copy.Chain(signer)[0] == identity)) return null;
        string closed = RecordParser.FormatTimestamp(closingAt);
        foreach (string other in copy.Trust.Closings.Where(c => c.Kind == "device" && c.Identity == identity).Select(c => c.Record.At))
        {
            if (string.CompareOrdinal(other, closed) > 0) closed = other;
        }

        return string.CompareOrdinal(revokedAt, closed) >= 0 ? null
            : $"Device {identity} signs its own revocation with revokedAt {revokedAt}, before its closing, dated {closed}: from its closing on, a closed device's signature counts only on its own revocation from the closing's time on, so the revocation would revoke nothing. Write the revocation with revokedAt at the present time, or have another operator's device, a device of a person holding administer, or the operator's recovery key revoke the device as of the earlier time. Nothing was written.";
    }

    /// <summary>
    /// The batch's refusal of an act without its closing (OPS-07): in an estate that has adopted, each such act follows, in its own batch,
    /// a closing naming what it closes, or, written again after a batch stopped between them, the closing its author stored then, which
    /// counts, acts and no act follows yet, read in <paramref name="current"/>, the copy without the batch.
    /// </summary>
    private static Problem? UnclosedActRefusal(EstateModel proposed, IReadOnlyList<AuthoredRecord> records, Func<EstateModel> current)
    {
        if (!records.Any(r => r.Envelope.Type is "admission" or "device" or "authority" && r.Envelope.Supersedes is not null) || !SignedHistory.Adopted(proposed)) return null;
        EstateModel? before = null;
        for (int i = 0; i < records.Count; i++)
        {
            if (ClosingActs.For(proposed, records[i].Envelope) is not { } closes) continue;
            if (records.Take(i).Any(r => r.Envelope.Type == "history-checkpoint" && CheckpointBody.ReadCloses(r.Envelope.Body) is { } named && named == closes)) continue;
            before ??= current();
            if (ClosingActs.Stored(before, closes, records[i].Envelope.By) is not null) continue;
            return new Problem("refused", $"{Needs(closes)} Record {records[i].Envelope.Id} comes without it, so nothing was written.", []);
        }

        return null;
    }

    /// <summary>What an act needs in an estate that has adopted (design/signed-history.md § 8).</summary>
    private static string Needs((string Kind, string Id) closes) =>
        $"In an estate that has adopted history checkpoints, {Article(closes.Kind)} is written after its closing, in the same batch: a checkpoint closing {closes.Kind} {closes.Id}, signed on the runtime's machine by one who may perform the act.";

    /// <summary>Why an act is refused when its closing cannot be written.</summary>
    private static string Unclosed((string Kind, string Id) closes, string why) =>
        $"{Needs(closes)} The closing could not be written: {why.TrimEnd()} Nothing was written.";

    /// <summary>Why an act signed elsewhere is refused in an estate that has adopted: it cannot carry its closing.</summary>
    private static string SignedElsewhereRefusal((string Kind, string Id) closes) =>
        $"{Needs(closes)} A record signed elsewhere, presigned or by a device-signed request, carries no closing: write it on a machine that holds a device of one who may perform it{(closes.Kind == "device" ? ", or with the operator's recovery key" : string.Empty)}. Nothing was written.";

    /// <summary>Why operator record refuses an act a closing precedes in an estate that has adopted: it carries one signed record, never its closing.</summary>
    private static string OperatorRecordClosingRefusal((string Kind, string Id) closes) =>
        $"{Needs(closes)} An operator record carries one signed record through a checked merge and cannot carry the closing, so this {Act(closes.Kind)} is not recorded through it: write it with {ClosingCommand(closes, "<operator id>")}, which writes the closing and the act together. Nothing was written.";

    /// <summary>The command that writes an act with its closing (OPS-07).</summary>
    private static string ClosingCommand((string Kind, string Id) closes, string by) => closes.Kind switch
    {
        "device" => $"vericairn devices revoke {closes.Id} --by {by} --reason \"<why>\"",
        "key" => $"vericairn devices rotate {closes.Id}",
        _ => $"vericairn records new authority --by {by} --supersedes {closes.Id} --body <file>",
    };

    /// <summary>
    /// Why operator record refuses a revision that takes a role away (<see cref="TakesRoleAway"/>), with the step that writes it, or null
    /// when it does not: in an estate that has adopted history checkpoints, such a revision is written after a checkpoint, in the same
    /// batch (<see cref="StoreRemovalLocked"/>), which one signed record carried through a checked merge cannot carry; in one that has
    /// not, a revision that would make records fail is refused, naming them (<see cref="RemovalRefusal"/>). Caller holds the gate.
    /// </summary>
    private (string Message, string NextStep, Problem? Problem)? OperatorRecordRemovalRefusal(RecordDraft draft, EstateModel current, AuthoredRecord authored)
    {
        if (!TakesRoleAway(draft, current)) return null;
        if (SignedHistory.Adopted(current))
        {
            return ("In an estate that has adopted history checkpoints, a revision that takes a role away is written after a history checkpoint, in the same batch, " +
                "signed on the runtime's machine by the acting principal's device, so what was written under the role before it keeps its reading. An operator record " +
                $"carries one signed record through a checked merge and cannot carry the checkpoint, so this revision is not recorded through it: write it with {RemovalCommand(draft, "<operator id>")}, " +
                "which writes the checkpoint and the revision together. Nothing was written.", RemovalCommand(draft, draft.By), null);
        }

        return RemovalRefusal(current, authored) is { } refused
            ? (refused.Message, "Adopt history checkpoints with vericairn estate checkpoint --adopt --check and vericairn estate checkpoint --adopt --expect-plan <plan hash> --by <root operator>, " +
                $"then write the revision with {RemovalCommand(draft, draft.By)}, which writes a checkpoint before it.", refused)
            : null;
    }

    /// <summary>The command that writes a revision taking a role away after a history checkpoint (<see cref="StoreRemovalLocked"/>).</summary>
    private static string RemovalCommand(RecordDraft draft, string by) => $"vericairn records new {draft.Type} --by {by} --supersedes {draft.Supersedes} --body <file>";

    private static string Act(string kind) => kind switch { "device" => "revocation", "key" => "key rotation", _ => "grant revision" };

    private static string Article(string kind) => kind switch { "device" => "a device's revocation", "key" => "a key rotation", _ => "a grant revision that narrows the grant" };

    /// <summary>
    /// Whether a draft takes a role away (signed history and policy authority, 0.9.2): a principal revision whose line gives the operator
    /// role and that gives it no more, or a grant revision whose line gives its grantee administer and that gives it no more, each line as
    /// read from the records their authors may write.
    /// </summary>
    private static bool TakesRoleAway(RecordDraft draft, EstateModel model)
    {
        if (draft.Type is not ("principal" or "authority") || draft.Supersedes is null || !model.ById.TryGetValue(draft.Supersedes, out RecordEnvelope? previous) ||
            previous.Type != draft.Type || draft.Body.ValueKind != JsonValueKind.Object)
            return false;
        RecordEnvelope? held = model.Authority.Current(model.Chain(previous)[0]);
        if (held is null) return false;
        return draft.Type == "principal"
            ? AuthorityReplay.IsOperator(held) && !(Text(draft.Body, "kind") == "human" && Strings(draft.Body, "roles").Contains(Vericairn.Records.Types.Roles.Operator))
            : Strings(held.Body, "scope").Contains(Vericairn.Records.Types.Scopes.Administer) &&
              !(Text(draft.Body, "grantee") == EstateModel.BodyString(held, "grantee") && Strings(draft.Body, "scope").Contains(Vericairn.Records.Types.Scopes.Administer));

        static string? Text(JsonElement body, string name) => body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        static HashSet<string> Strings(JsonElement body, string name) => body.TryGetProperty(name, out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!)]
            : [];
    }

    /// <summary>
    /// Stores a revision that takes a role away (<see cref="TakesRoleAway"/>). In an estate that has adopted history checkpoints, a
    /// checkpoint signed by the acting principal's device is written first, in the same batch, over the copy as it is, so what the person
    /// wrote before the removal stays witnessed before it and keeps its reading (design/signed-history.md § 8); the revision is authored
    /// after it; a grant revision that needs a closing is stored after its closing instead (<see cref="StoreWithClosingLocked"/>), which
    /// is such a checkpoint, and does not come here. A revision signed elsewhere is stored alone, since this machine signs nothing for it,
    /// and the next checkpoint witnesses it. In an estate that has not adopted, nothing places what the person wrote before the removal, so a revision that would make any
    /// record fail is refused, naming those records (<see cref="RemovalRefusal"/>); one that would make none fail is written alone.
    /// Caller holds the gate and the write lock.
    /// </summary>
    private WriteOutcome StoreRemovalLocked(RecordDraft draft, IReadOnlyList<IRecordSigner>? signers)
    {
        EstateModel current = LoadWith([]);
        if (!SignedHistory.Adopted(current) || signers is not null)
        {
            (AuthoredRecord? alone, string? refusal) = AuthorLocked(draft, current, signers);
            if (alone is null) return EstateServiceValues.Refuse(refusal!);
            if (!SignedHistory.Adopted(current) && RemovalRefusal(current, alone) is { } refused) return WriteOutcome.Fail(refused);
            return StoreAuthoredLocked(alone, current);
        }

        (PreparedCheckpoint? checkpoint, string? skipped) = CheckpointLocked(current, "manual", draft.By);
        (AuthoredRecord? record, string? why) = AuthorLocked(draft, current);
        if (record is null)
        {
            if (checkpoint is not null) RemoveObjects(checkpoint);
            return EstateServiceValues.Refuse(why!);
        }

        if (checkpoint is null)
        {
            WriteOutcome outcome = StoreAuthoredLocked(record, current);
            return skipped is null || outcome.Written is null ? outcome : outcome with { Checkpoint = new TriggeredCheckpoint("manual", null, skipped) };
        }

        (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([checkpoint.Record, record],
            "Store a history checkpoint and then the revision that takes a role away, so what was written before the removal keeps its reading.", null, null, null);
        if (written.Count < 2)
        {
            if (written.Count == 0) RemoveObjects(checkpoint);
            return WriteOutcome.Fail(problem ?? new Problem("refused", "The revision was not written.", []));
        }

        return new WriteOutcome(written[1], problem) { Checkpoint = new TriggeredCheckpoint("manual", written[0], null) };
    }

    /// <summary>
    /// Why a removal is refused in an estate that has not adopted history checkpoints (FIX-111): the records other than the removal that
    /// would fail with it and do not fail now, each with the findings that say so: an error, such as a decision by the person whose role
    /// is read as it is now, or policy.operator-removed, a policy record or approval that would count for nothing. Null when none would,
    /// or when the removal fails on its own, which the write's own judgment reports.
    /// </summary>
    private Problem? RemovalRefusal(EstateModel current, AuthoredRecord removal)
    {
        static bool Fails(Finding finding) => finding.Severity == Severity.Error || finding.Code == "policy.operator-removed";
        static string RecordPath(Finding finding) => finding.Location.Split('#')[0];
        var failing = current.Findings.Where(Fails).Select(RecordPath).ToHashSet(StringComparer.Ordinal);
        EstateModel proposed = LoadWith([removal]);
        if (proposed.Findings.Any(f => f.Severity == Severity.Error && RecordPath(f) == removal.RelativePath)) return null;
        List<Finding> ended = [.. proposed.Findings.Where(f => Fails(f) && RecordPath(f) != removal.RelativePath && !failing.Contains(RecordPath(f)))
            .OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal)];
        if (ended.Count == 0) return null;
        var byPath = proposed.Records.ToDictionary(r => r.ExpectedPath, StringComparer.Ordinal);
        List<string> named = [.. ended.GroupBy(RecordPath, StringComparer.Ordinal).Select(g =>
            (byPath.TryGetValue(g.Key, out RecordEnvelope? record) ? $"{record.Type} {record.Id}" : g.Key) +
            $" ({string.Join(", ", g.Select(f => f.Code).Distinct(StringComparer.Ordinal))})")];
        const int Shown = 20;
        string list = string.Join("; ", named.Take(Shown)) + (named.Count > Shown ? $"; and {named.Count - Shown} more, each in the findings" : "");
        return new Problem("refused",
            $"Not written: this estate has not adopted history checkpoints, so nothing places what was written under the role before this removal, " +
            $"and {named.Count} {(named.Count == 1 ? "record" : "records")} would fail with it: {list}. Adopt history checkpoints first: " +
            $"the runtime then writes a checkpoint before the removal, and {(named.Count == 1 ? "this record keeps its" : "these records keep their")} reading.",
            [.. ended.Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message))]);
    }

    /// <summary>
    /// What a charter revision's checkpoint anchors (design/signed-history.md § 6): a revision moving the charter to required signatures,
    /// by the root operator or a person holding administer, is followed in the same batch by a checkpoint anchoring the unsigned records
    /// that count before it and no anchor lists yet, so an earlier optional period leaves nothing to backdate into and no reading changes.
    /// Any other revision, and any other author's, anchors nothing; a revision to required that would leave unsigned records counting
    /// for nothing is then refused as before.
    /// </summary>
    private static List<RecordEnvelope>? CharterAnchors(EstateModel current, RecordEnvelope revision)
    {
        if (RequiresSignatures(current.CurrentEstate) || !RequiresSignatures(revision) || AdoptionRefusal(current, revision.By) is not null) return null;
        IReadOnlySet<string> anchors = SignatureChecks.Anchored(current);
        List<RecordEnvelope> anchored = [.. SignedHistory.PlanAdoption(current).Anchored.Where(r => !anchors.Contains(SignatureChecks.AnchorKey(r.Id, r.ComputedHash)))];
        return anchored.Count == 0 ? null : anchored;
    }

    private static bool RequiresSignatures(RecordEnvelope? charter) =>
        charter is not null && charter.Body.ValueKind == JsonValueKind.Object && charter.Body.TryGetProperty("policies", out JsonElement policies) &&
        policies.ValueKind == JsonValueKind.Object && policies.TryGetProperty("signatures", out JsonElement policy) && policy.ValueKind == JsonValueKind.String && policy.GetString() == "required";

    /// <summary>
    /// Writes a checkpoint by itself for an act that records nothing in the same batch, such as the release gate's before the submission
    /// check (design/release-procedure.md § 7): null before adoption; otherwise the checkpoint written or why none could be. Caller holds the gate.
    /// </summary>
    internal TriggeredCheckpoint? WriteCheckpointLocked(string trigger, string by)
    {
        // Read from the canonical files, as the release gates do, so that an estate that has not adopted leaves the live cache untouched.
        if (!SignedHistory.Adopted(session.Read())) return null;
        return UnderWriteLock(() =>
        {
            (PreparedCheckpoint? checkpoint, string? skipped) = CheckpointLocked(LoadWith([]), trigger, by);
            if (checkpoint is null) return new TriggeredCheckpoint(trigger, null, skipped ?? "No history checkpoint was asked.");
            (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([checkpoint.Record], $"Write the history checkpoint with trigger {trigger}.", null, null, null);
            if (written.Count == 1) return new TriggeredCheckpoint(trigger, written[0], null);
            RemoveObjects(checkpoint);
            return new TriggeredCheckpoint(trigger, null, "The history checkpoint was not written: " + Describe(problem));
        }, problem => new TriggeredCheckpoint(trigger, null, problem.Message));
    }

    /// <summary>
    /// The release gate's objections from this machine's memory (design/signed-history.md § 7; design/release-procedure.md § 7): while
    /// the memory holds a verified checkpoint, a copy that has not adopted, or whose main line no longer holds that checkpoint, may have
    /// been rewritten, and no release is submitted from it.
    /// </summary>
    internal IReadOnlyList<FindingDto> HistoryObjections(EstateModel model)
    {
        if (Standing(model) is not { } standing) return [];
        string? why = !SignedHistory.Adopted(model) ? "this copy has not adopted history checkpoints"
            : standing.MainHolds == false ? "the main line no longer holds it"
            : !standing.CopyHolds && standing.MainHolds is null ? "this copy no longer holds it"
            : null;
        return why is null ? [] :
        [
            new FindingDto("error", "history.rolled-back", standing.Verified.Path,
                $"This machine verified history checkpoint {standing.Verified.Id} ({HistoryLine.ShortHash(standing.Verified.Hash)}) on {standing.Verified.VerifiedAt}, and {why}: its history may have been rewritten to remove the newest checkpoints together with the records after them. Restore that history from a clone or remote that holds it, or check this copy against a head the main line holds with vericairn estate check --expect <checkpoint>, before submitting a release."),
        ];
    }

    /// <summary>Why adoption, and its check, do not go ahead on this clone: its memory holds a verified checkpoint the copy no longer holds.</summary>
    private string? RolledBackRefusal(EstateModel model, string outcome = "Nothing was signed.") =>
        Standing(model) is { CopyHolds: false } standing
            ? $"This machine verified history checkpoint {standing.Verified.Id} ({HistoryLine.ShortHash(standing.Verified.Hash)}) on {standing.Verified.VerifiedAt}, and this copy no longer holds it: its history may have been rewritten to remove the checkpoints together with the records after them, and adopting now would witness and anchor what the rewrite left. Restore that history from a clone or remote that holds it, or check this copy against a head the main line holds with vericairn estate check --expect <checkpoint>, which this machine then remembers instead. {outcome}"
            : null;

    /// <summary>What this clone's memory says of the copy, or null when it remembers no checkpoint or cannot be read.</summary>
    private MemoryStanding? Standing(EstateModel model)
    {
        if (model.Manifest is not { } manifest) return null;
        HistoryMemory.Remembered? remembered;
        try
        {
            remembered = new HistoryMemory(model.Root, manifest.Estate, manifest.Profile, HistoryMemoryHome ?? session.Home).Read();
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            return null;
        }

        if (remembered?.Checkpoint is not { } verified) return null;
        bool copy = model.ById.TryGetValue(verified.Id, out RecordEnvelope? held) && held.ComputedHash == verified.Hash && model.Counts(held);
        bool? main = null;
        if (manifest.Profile == "repository-synced" && IsRepository(model.Root) && Providers.Source("git") is { } git && git.MainLine(model.Root) is { } line)
            main = HistoryMemory.Holds(git, model.Root, line, verified.Path, verified.Hash);
        return new MemoryStanding(verified, copy, main);
    }

    private static string Describe(Problem? problem) => problem is null ? "no reason was given."
        : problem.Findings.Count == 0 ? problem.Message : problem.Message + " " + string.Join(" ", problem.Findings.Where(f => f.Severity == "error").Select(f => $"{f.Code}: {f.Message}"));

    /// <summary>
    /// The machine's memory at check time (design/signed-history.md § 7): warns history.rolled-back when the main line no longer holds the
    /// newest checkpoint this machine verified, a branch checkout judged through the main line; otherwise remembers the newest counting
    /// head the main line holds that follows the one remembered. It depends on the machine, so it stays out of the canonical status.
    /// </summary>
    private FindingList RolledBack(EstateModel model)
    {
        var findings = new FindingList();
        // Only a clone has a main line; an estate directory that is not a repository has nothing to judge here.
        if (model.Manifest is not { Profile: "repository-synced" } manifest || Providers.Source("git") is not { } git || !IsRepository(model.Root)) return findings;
        HistoryMemory memory;
        HistoryMemory.Remembered? remembered;
        try
        {
            memory = new HistoryMemory(model.Root, manifest.Estate, manifest.Profile, HistoryMemoryHome ?? session.Home);
            remembered = memory.Read();
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            findings.Warning("history.rolled-back", EstateLayout.RecordsDirectory, $"This machine's memory of the newest history checkpoint it verified cannot be read, since {HistoryMemory.Describe(exception)}, so a rewrite of this clone's history cannot be told.");
            return findings;
        }

        EstateMainLine? main = git.MainLine(model.Root);
        if (main is null)
        {
            if (remembered?.Checkpoint is not null || SignedHistory.Adopted(model))
                findings.Info("history.rolled-back", EstateLayout.RecordsDirectory, "Unavailable: this clone has no main line (a local branch the origin's HEAD names, main or master), so whether it still holds the newest history checkpoint this machine verified cannot be told.");
            return findings;
        }

        if (remembered?.Checkpoint is { } verified && !HistoryMemory.Holds(git, model.Root, main, verified.Path, verified.Hash))
        {
            findings.Warning("history.rolled-back", verified.Path,
                $"This machine verified history checkpoint {verified.Id} ({HistoryLine.ShortHash(verified.Hash)}) on {verified.VerifiedAt}, held by commit {verified.Commit} of {verified.Branch}, and the main line ({main.Branch} at {main.Commit}) no longer holds it: its history may have been rewritten to remove the newest checkpoints together with the records after them. Restore that history from a clone or remote that holds it; if the rewrite was intended, check this copy against a head the main line holds with vericairn estate check --expect <checkpoint>, which this machine then remembers instead. Until then this clone neither adopts nor submits a release.");
            return findings;
        }

        // The memory moves only forward along the history it verified: to a head that follows the checkpoint remembered.
        HistoryState state = SignedHistory.Of(model);
        RecordReference? newest = state.Heads
            .Where(h => remembered?.Checkpoint is not { } known || h.Id != known.Id && SignedHistory.Follows(model, h.Id, known.Id))
            .Where(h => model.ById.TryGetValue(h.Id, out RecordEnvelope? head) && HistoryMemory.Holds(git, model.Root, main, head.ExpectedPath, h.Hash!))
            .OrderBy(h => h.Id, StringComparer.Ordinal).LastOrDefault();
        if (newest is null || session.IsReadOnly) return findings;
        try
        {
            memory.Write(new HistoryMemory.Remembered(manifest.Estate,
                new HistoryMemory.Verified(newest.Id, newest.Hash!, model.ById[newest.Id].ExpectedPath, main.Branch, main.Commit, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)), remembered?.Expect));
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            findings.Warning("history.rolled-back", EstateLayout.RecordsDirectory, $"This machine cannot remember the newest history checkpoint it verified, since {HistoryMemory.Describe(exception)}.");
        }

        return findings;
    }

    /// <summary>What this machine remembers of the estate's history, for the check's information line; null when nothing.</summary>
    private Finding? Remembered(EstateModel model)
    {
        if (model.Manifest is not { } manifest) return null;
        try
        {
            if (new HistoryMemory(model.Root, manifest.Estate, manifest.Profile, HistoryMemoryHome ?? session.Home).Read() is not { } remembered) return null;
            var parts = new List<string>();
            if (remembered.Checkpoint is { } verified)
                parts.Add($"the newest history checkpoint it verified is {verified.Id} ({HistoryLine.ShortHash(verified.Hash)}), held by commit {verified.Commit} of {verified.Branch} on {verified.VerifiedAt}");
            if (remembered.Expect is { Checkpoint: { } expected } expect)
                parts.Add($"its latest estate check --expect found checkpoint {expected} held on {expect.CheckedAt}");
            return parts.Count == 0 ? null : new Finding(Severity.Info, "history.remembered", EstateLayout.RecordsDirectory, "This machine remembers that " + string.Join("; ", parts) + ".");
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            return null;
        }
    }
}
