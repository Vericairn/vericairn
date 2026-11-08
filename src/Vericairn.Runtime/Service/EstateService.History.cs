// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Writing and adopting history checkpoints (design/signed-history.md §§ 9 and 10; OPS-07): <c>estate checkpoint</c> and its adoption,
/// the checkpoints the runtime writes by itself in the same batch as an act, and the machine's memory of the newest checkpoint it verified.
/// </summary>
public sealed partial class EstateService
{
    private const string Adoption = "adoption";
    private const string Interval = "interval";

    /// <summary>A test's Vericairn home for the machine's memory of history; the session's home when null.</summary>
    internal string? HistoryMemoryHome { get; set; }

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
                return (null, new Problem("refused", "This estate has not adopted history checkpoints, and until it does only its adoption writes one. Read what adoption would witness and anchor with vericairn estate checkpoint --adopt --check; the root operator then signs it once with vericairn estate checkpoint --adopt --by <root operator>.", []));
            if (model.Manifest is not { RecordFormat: >= 3 })
                return (null, new Problem("refused", "History checkpoints are records of format 3; upgrade the estate to record format 3 first.", []));
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
                IReadOnlyList<RecordEnvelope>? anchored = request.Adopt ? SignedHistory.PlanAdoption(current).Anchored : null;
                (AuthoredRecord? checkpoint, string? skipped) = CheckpointLocked(current, trigger, request.By!, anchored);
                if (checkpoint is null) return (null, new Problem("refused", skipped ?? "No history checkpoint could be written.", []));
                (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([checkpoint], request.Adopt ? "Adopt history checkpoints." : "Write a history checkpoint.", null, null, null);
                if (written.Count == 0) return (null, problem ?? new Problem("refused", "The checkpoint was not written.", []));
                EstateModel after = LoadWith([]);
                return (Plan(current, trigger, request.Adopt, written[0]) with { Adopted = SignedHistory.Adopted(after) }, problem);
            }, problem => (null, problem));
        }
    }

    /// <summary>
    /// Why a principal may not adopt (design/signed-history.md §§ 6 and 10): adoption anchors records and changes what counts for the whole
    /// estate, so it is the root operator's, or a person's holding administer, never an agent's. The model then judges the signed adoption
    /// as it judges every checkpoint, devices whose admissions rest on signatures included.
    /// </summary>
    private static string? AdoptionRefusal(EstateModel model, string by)
    {
        if (by == model.RootOperator) return null;
        RecordEnvelope? principal = model.CurrentPrincipal(by);
        if (principal is not null && EstateModel.BodyString(principal, "kind") == "human" && model.HasScope(by, Scopes.Administer, DateTimeOffset.UtcNow)) return null;
        return $"Adopting history checkpoints anchors the unsigned records that count today and changes what counts in the whole estate, so it is signed by the root operator or a person holding administer, never an agent; principal {by} is neither. Nothing was written.";
    }

    /// <summary>What a checkpoint witnesses and anchors, or would, with the commits that added the anchored records and the deletions in history.</summary>
    private HistoryCheckpointResponse Plan(EstateModel model, string trigger, bool adopt, RecordWritten? written)
    {
        HistoryState state = SignedHistory.Of(model);
        IReadOnlyList<RecordEnvelope> tail = SignedHistory.TailRecords(model);
        List<HistoryCheckpointPin> heads = [.. state.Heads.Select(h => new HistoryCheckpointPin(h.Id, h.Hash!))];
        if (!adopt)
            return new HistoryCheckpointResponse(trigger, written is not null, state.Adopted, state.Witnessed + tail.Count, tail.Count, heads, [], [], [], [], null, written);

        AdoptionPlan plan = SignedHistory.PlanAdoption(model);
        var unavailable = new List<string>();
        ISourceAdapter? git = model.Manifest?.Profile == "repository-synced" ? Providers.Source("git") : null;
        RecordOrigins? origins = git?.RecordFileOrigins(model.Root);
        bool repository = Directory.Exists(Path.Combine(model.Root, ".git")) || File.Exists(Path.Combine(model.Root, ".git"));
        if (git is null) unavailable.Add("The commits that added records and the record files deleted in history come from the git adapter, which does not serve this copy.");
        else if (!repository) unavailable.Add("This estate is not a git repository, so the commits that added records and the record files deleted in history cannot be listed.");
        else if (origins is null) unavailable.Add("The commits that added records could not be read from the repository.");
        else if (origins.Shallow) unavailable.Add("This clone is shallow, so the commits that added records and the record files deleted in history cannot be listed; a full clone lists them.");
        List<HistoryDeletedFile> deleted = [];
        if (git?.DeletedRecordFiles(model.Root) is { Shallow: false } deletions)
            deleted = [.. deletions.Deleted.Where(d => model.FilePaths?.Contains(d.Path) != true && !model.ById.ContainsKey(Path.GetFileNameWithoutExtension(d.Path))).Select(d => new HistoryDeletedFile(d.Path, d.Commit))];
        else if (git is not null && repository && origins is { Shallow: false }) unavailable.Add("The record files deleted in history could not be read from the repository.");

        HistoryRecordItem Item(RecordEnvelope record, string? device = null) =>
            new(record.Id, record.Type, record.At, record.ExpectedPath, origins?.Added.GetValueOrDefault(record.ExpectedPath), device);
        return new HistoryCheckpointResponse(trigger, written is not null, state.Adopted, plan.Witnessed, tail.Count, heads,
            [.. plan.Anchored.Select(r => Item(r))], [.. plan.NotCounting.Select(r => Item(r))], [.. plan.ByRevokedDevices.Select(r => Item(r.Record, r.Device))],
            deleted, unavailable.Count == 0 ? null : string.Join(" ", unavailable), written);
    }

    /// <summary>
    /// Prepares and signs a checkpoint over a model, its inventory objects stored first, signed by the principal's admitted device held on
    /// this machine: the device then writing (design/signed-history.md § 9). Null with the reason when it cannot be; null without one when
    /// no checkpoint is asked: before adoption, for any trigger but adoption, and for an interval when the device then writing is a
    /// person's with presence, which is never asked for one, or there is none. The model must be read under the write lock when the
    /// checkpoint is stored with it.
    /// </summary>
    /// <remarks>
    /// The hook for closings (design/signed-history.md § 8, ID-06): a revocation, key rotation or narrowing grant revision stores, in one
    /// batch and before its own record, the checkpoint this prepares over the copy as it is, with trigger closing, <paramref name="closes"/>
    /// naming what it closes and <paramref name="signer"/> the key that performs the act, since only someone who could perform the act signs
    /// its closing; StoreTogetherUnderLock then stores [closing, act].
    /// </remarks>
    private (AuthoredRecord? Checkpoint, string? Skipped) CheckpointLocked(EstateModel model, string trigger, string by, IEnumerable<RecordEnvelope>? anchored = null,
        (string Kind, string Id)? closes = null, IRecordSigner? signer = null)
    {
        if (requestDevice.Value is { Closed: false, Signing: not null })
            return (null, trigger == Interval ? null : "A device-signed request signs only its own record, so no history checkpoint was written with it; the next checkpoint witnesses it.");
        if (trigger != Adoption && !SignedHistory.Adopted(model)) return (null, null);
        using JsonDocument probe = JsonDocument.Parse("{}");
        if (signer is null && (ResolveSigner(model, new RecordDraft("history-checkpoint", by, probe.RootElement), out signer) is not null || signer is null))
            return (null, trigger == Interval ? null : $"Principal {by} has no admitted device whose key this machine holds, so no history checkpoint with trigger {trigger} was written; the next checkpoint witnesses what this one would have.");
        if (trigger == Interval && model.Current("device").FirstOrDefault(d => model.Chain(d)[0] == signer.DeviceId) is { } device && EstateModel.BodyString(device, "presence") == "required")
            return (null, null);

        CheckpointDraft draft;
        try
        {
            draft = SignedHistory.Prepare(model, trigger, anchored, closes: closes);
            foreach (byte[] bytes in draft.Objects) session.Objects.Put(bytes);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, $"No history checkpoint with trigger {trigger} was written: {exception.Message}");
        }

        (AuthoredRecord? checkpoint, string? refusal) = AuthorLocked(new RecordDraft("history-checkpoint", by, draft.Body), model, [signer]);
        if (checkpoint is null) return (null, $"No history checkpoint with trigger {trigger} was written: {refusal}");
        return checkpoint.Envelope.Signatures.Count == 0 ? (null, $"No history checkpoint with trigger {trigger} was written: it would be unsigned.") : (checkpoint, null);
    }

    /// <summary>
    /// The checkpoint a single write sets off (design/signed-history.md § 9; OPS-07), or null: the root operator's first device admitting
    /// itself in a newborn estate adopts at birth; in an estate that has adopted, a charter revision writes one with trigger charter, and
    /// any other write signed by this machine's device for its author one with trigger interval once the tail is due. A device-signed
    /// request, and a write signed with keys the caller names, such as a device signing its own record, set off none.
    /// </summary>
    private string? TriggerOf(RecordDraft draft, EstateModel current, IReadOnlyList<IRecordSigner>? signers)
    {
        if (draft.Type == "history-checkpoint" || requestDevice.Value is { Closed: false, Signing: not null } || session.IsReadOnly) return null;
        if (draft.Type == "admission" && signers is null && Newborn(current, draft)) return Adoption;
        if (!SignedHistory.Adopted(current)) return null;
        if (draft.Type == "estate" && draft.Supersedes is not null) return "charter";
        return signers is null && SignedHistory.IntervalDue(current, DateTimeOffset.UtcNow) ? Interval : null;
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
        EstateModel withRecord = LoadWith([record]);
        IReadOnlyList<RecordEnvelope>? anchored = trigger switch
        {
            Adoption => SignedHistory.PlanAdoption(withRecord).Anchored,
            "charter" => CharterAnchors(current, record.Envelope),
            _ => null,
        };
        (AuthoredRecord? checkpoint, string? skipped) = CheckpointLocked(withRecord, trigger, by, anchored);
        if (checkpoint is null)
        {
            WriteOutcome alone = StoreAuthoredLocked(record, current);
            return skipped is null || alone.Written is null ? alone : alone with { Checkpoint = new TriggeredCheckpoint(trigger, null, skipped) };
        }

        (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([record, checkpoint],
            $"Store the record and the history checkpoint its {trigger} trigger writes with it.", null, null, null);
        if (written.Count == 0)
        {
            WriteOutcome alone = StoreAuthoredLocked(record, current);
            return alone.Written is null ? alone : alone with { Checkpoint = new TriggeredCheckpoint(trigger, null, "The history checkpoint was refused, so the record was written alone: " + Describe(problem)) };
        }

        RecordWritten? stored = written.Count > 1 ? written[1] : null;
        return new WriteOutcome(written[0], problem) { Checkpoint = new TriggeredCheckpoint(trigger, stored, stored is null ? "The history checkpoint was not stored: " + Describe(problem) : null) };
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
            (AuthoredRecord? checkpoint, string? skipped) = CheckpointLocked(LoadWith([]), trigger, by);
            if (checkpoint is null) return new TriggeredCheckpoint(trigger, null, skipped ?? "No history checkpoint was asked.");
            (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherUnderLock([checkpoint], $"Write the history checkpoint with trigger {trigger}.", null, null, null);
            return written.Count == 1 ? new TriggeredCheckpoint(trigger, written[0], null) : new TriggeredCheckpoint(trigger, null, "The history checkpoint was not written: " + Describe(problem));
        }, problem => new TriggeredCheckpoint(trigger, null, problem.Message));
    }

    private static string Describe(Problem? problem) => problem is null ? "no reason was given."
        : problem.Findings.Count == 0 ? problem.Message : problem.Message + " " + string.Join(" ", problem.Findings.Where(f => f.Severity == "error").Select(f => $"{f.Code}: {f.Message}"));

    /// <summary>
    /// The machine's memory at check time (design/signed-history.md § 7): warns history.rolled-back when the main line no longer holds the
    /// newest checkpoint this machine verified, a branch checkout judged through the main line; otherwise remembers the newest counting
    /// head the main line holds. It depends on the machine, so it stays out of the canonical status.
    /// </summary>
    private FindingList RolledBack(EstateModel model)
    {
        var findings = new FindingList();
        // Only a clone has a main line; an estate directory that is not a repository has nothing to judge here.
        if (model.Manifest is not { Profile: "repository-synced" } manifest || Providers.Source("git") is not { } git ||
            !Directory.Exists(Path.Combine(model.Root, ".git")) && !File.Exists(Path.Combine(model.Root, ".git"))) return findings;
        HistoryMemory memory;
        HistoryMemory.Remembered? remembered;
        try
        {
            memory = new HistoryMemory(model.Root, manifest.Estate, manifest.Profile, HistoryMemoryHome ?? session.Home);
            remembered = memory.Read();
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            findings.Info("history.rolled-back", EstateLayout.RecordsDirectory, "Unavailable: this machine's memory of the newest history checkpoint it verified cannot be read (" + exception.Message + ").");
            return findings;
        }

        EstateMainLine? main = git.MainLine(model.Root);
        if (main is null)
        {
            if (remembered?.Checkpoint is not null || SignedHistory.Adopted(model))
                findings.Info("history.rolled-back", EstateLayout.RecordsDirectory, "Unavailable: this clone has no main line (a local branch the origin's HEAD names, main or master), so whether it still holds the newest history checkpoint this machine verified cannot be told.");
            return findings;
        }

        if (remembered?.Checkpoint is { } verified)
        {
            if (!HistoryMemory.Holds(git, model.Root, main, verified.Path, verified.Hash))
            {
                findings.Warning("history.rolled-back", verified.Path,
                    $"This machine verified history checkpoint {verified.Id} ({HistoryLine.ShortHash(verified.Hash)}) on {verified.VerifiedAt}, held by commit {verified.Commit} of {verified.Branch}, and the main line ({main.Branch} at {main.Commit}) no longer holds it: its history may have been rewritten to remove the newest checkpoints together with the records after them. Restore that history from a clone or remote that holds it; if the rewrite was intended, check this copy against a head known elsewhere with vericairn estate check --expect <checkpoint>, which this machine then remembers instead.");
                return findings;
            }
        }

        HistoryState state = SignedHistory.Of(model);
        RecordReference? newest = state.Heads.Where(h => model.ById.TryGetValue(h.Id, out RecordEnvelope? head) && HistoryMemory.Holds(git, model.Root, main, head.ExpectedPath, h.Hash!))
            .OrderBy(h => h.Id, StringComparer.Ordinal).LastOrDefault();
        if (newest is null || remembered?.Checkpoint is { } known && string.CompareOrdinal(newest.Id, known.Id) <= 0 || session.IsReadOnly) return findings;
        try
        {
            memory.Write(new HistoryMemory.Remembered(manifest.Estate,
                new HistoryMemory.Verified(newest.Id, newest.Hash!, model.ById[newest.Id].ExpectedPath, main.Branch, main.Commit, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)), remembered?.Expect));
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            findings.Info("history.rolled-back", EstateLayout.RecordsDirectory, "Unavailable: this machine cannot remember the newest history checkpoint it verified (" + exception.Message + ").");
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
            if (remembered.Expect is { } expect)
                parts.Add($"its latest estate check --expect {expect.Expected} found the checkpoint {(expect.Outcome == "held" ? "held" : "missing")} on {expect.CheckedAt}");
            return parts.Count == 0 ? null : new Finding(Severity.Info, "history.remembered", EstateLayout.RecordsDirectory, "This machine remembers that " + string.Join("; ", parts) + ".");
        }
        catch (Exception exception) when (HistoryMemory.Unavailable(exception))
        {
            return null;
        }
    }
}
