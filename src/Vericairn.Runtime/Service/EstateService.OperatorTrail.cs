// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// An operator action's push and merge trail (FIX-126): the operation records of its push, opening and merge with every revision, and
/// the observations and asks that point to them. They are written after the action's commit, so they stay untracked in the clone that
/// ran it; once the action is complete its trail is settled, and the next operator action in that clone carries whatever of it is still
/// untracked into its own commit. Status and the estate check report a clone that holds such a trail, naming the action.
/// </summary>
public sealed partial class EstateService
{
    private const string OperatorKeyPrefix = "operator-record:";

    /// <summary>The finding that names a completed operator action whose settled trail this clone still holds untracked.</summary>
    internal const string TrailUncommittedCode = "operator.trail-uncommitted";

    // An action's receipts: its own push, opening and merge operations with every revision, and the observations and asks that point
    // to them, all authored by the action's own principal. Caller holds the gate.
    private HashSet<string> OperatorReceiptsLocked(OperatorCheckpoint checkpoint)
    {
        EstateModel model = session.Load();
        return OperatorTrail(model, new TrailIndex(model), checkpoint);
    }

    private static HashSet<string> OperatorTrail(EstateModel model, TrailIndex index, OperatorCheckpoint checkpoint)
    {
        string? by = checkpoint.By ?? (model.ById.TryGetValue(checkpoint.Id, out RecordEnvelope? signed) ? signed.By : null);
        if (by is null || checkpoint.Head is null || !index.ByHead.TryGetValue(checkpoint.Head, out List<RecordEnvelope>? pinned)) return new(StringComparer.Ordinal);
        HashSet<string> pulls = pinned.Where(o => o.By == by && EstateModel.BodyString(o, "action") == "change-open"
            && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "product") == checkpoint.Product && ChangeText(t, "branch") == checkpoint.Branch)
            .Select(o => ChangeText(o.Body.GetProperty("target"), "pullRequest")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        HashSet<string> owned = pinned.Where(o => o.By == by
            && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "product") == checkpoint.Product
            && (ChangeText(t, "branch") == checkpoint.Branch
                || EstateModel.BodyString(o, "action") == "change-merge" && pulls.Contains(ChangeText(t, "pullRequest") ?? string.Empty))
            && EstateModel.BodyString(o, "action") is "change-push" or "change-open" or "change-merge").Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        // Include every revision and the observations and asks that point to those original operation records, by the same author.
        var pending = new Queue<string>(owned);
        while (pending.TryDequeue(out string? id))
        {
            if (!index.Followers.TryGetValue(id, out List<RecordEnvelope>? followers)) continue;
            foreach (RecordEnvelope record in followers)
                if (record.By == by && owned.Add(record.Id)) pending.Enqueue(record.Id);
        }
        return owned.Select(id => model.ById[id].ExpectedPath).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The records a trail can hold, indexed once per model: operations by the head they pin, and for each record the records that
    /// supersede it and the operations and asks that name it as their subject.
    /// </summary>
    private sealed class TrailIndex
    {
        internal Dictionary<string, List<RecordEnvelope>> ByHead { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<RecordEnvelope>> Followers { get; } = new(StringComparer.Ordinal);

        internal TrailIndex(EstateModel model)
        {
            foreach (RecordEnvelope operation in model.OfType("operation"))
                if (operation.Body.TryGetProperty("target", out JsonElement target) && ChangeText(target, "head") is string head)
                    Add(ByHead, head, operation);
            foreach (RecordEnvelope record in model.Records)
            {
                if (record.Supersedes is not null) Add(Followers, record.Supersedes, record);
                if (record.Subject is not null && record.Subject.Id != record.Supersedes && record.Type is "operation" or "ask") Add(Followers, record.Subject.Id, record);
            }
        }

        private static void Add(Dictionary<string, List<RecordEnvelope>> index, string key, RecordEnvelope record)
        {
            if (!index.TryGetValue(key, out List<RecordEnvelope>? list)) index.Add(key, list = []);
            list.Add(record);
        }
    }

    // Every operator action this clone's runtime completed, by its local checkpoint, in key order. A checkpoint that cannot be read names
    // no action. Caller holds the gate.
    private List<(string Identity, OperatorCheckpoint Checkpoint)> CompletedActionsLocked()
    {
        List<(string, OperatorCheckpoint)> completed = [];
        foreach (string key in session.Live.MetaKeys(OperatorKeyPrefix))
        {
            string suffix = key[OperatorKeyPrefix.Length..];
            if (suffix.Length != 64 || !suffix.All(char.IsAsciiHexDigitLower)) continue;
            OperatorCheckpoint? checkpoint;
            try { checkpoint = OperatorCheckpointLocked(key); }
            catch (JsonException) { continue; }
            if (checkpoint?.Completed is not null) completed.Add((key, checkpoint));
        }
        return completed;
    }

    // The settled trails of this clone's completed actions, each with the paths the working tree still holds untracked; empty when there
    // are none or the adapter cannot say which are untracked. It reads the model the caller already loaded, never loading another, since a
    // load reads and judges every record. Caller holds the gate.
    private List<(OperatorCheckpoint Checkpoint, IReadOnlyList<string> Untracked)> UntrackedTrailsLocked(IRecordCommitAdapter adapter, EstateModel model)
    {
        List<(OperatorCheckpoint, IReadOnlyList<string>)> trails = [];
        List<(string Identity, OperatorCheckpoint Checkpoint)> completed = CompletedActionsLocked();
        if (completed.Count == 0) return trails;
        var index = new TrailIndex(model);
        List<(OperatorCheckpoint Checkpoint, HashSet<string> Paths)> settled = [.. completed.Select(c => (c.Checkpoint, OperatorTrail(model, index, c.Checkpoint))).Where(t => t.Item2.Count > 0)];
        HashSet<string> all = [.. settled.SelectMany(t => t.Paths)];
        if (all.Count == 0 || adapter.Untracked(Root, all) is not { Count: > 0 } untracked) return trails;
        foreach ((OperatorCheckpoint checkpoint, HashSet<string> paths) in settled)
        {
            string[] held = [.. paths.Where(untracked.Contains).Order(StringComparer.Ordinal)];
            if (held.Length > 0) trails.Add((checkpoint, held));
        }
        return trails;
    }

    // What a new action carries into its commit: every settled trail record of an earlier action that the working tree holds untracked,
    // by path and byte hash. Caller holds the gate.
    private Dictionary<string, string> UncommittedTrailLocked(IRecordCommitAdapter adapter)
    {
        var carried = new Dictionary<string, string>(StringComparer.Ordinal);
        List<(OperatorCheckpoint Checkpoint, IReadOnlyList<string> Untracked)> trails = UntrackedTrailsLocked(adapter, session.Load());
        if (trails.Count == 0) return carried;
        foreach (string path in trails.SelectMany(t => t.Untracked).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            RecordFile file = session.LoadedFile(path) ?? session.Files().Single(f => f.Path == path);
            carried.Add(path, ContentHash.Sha256(file.Bytes));
        }
        return carried;
    }

    /// <summary>
    /// One warning for each completed operator action whose settled push and merge trail this clone still holds untracked, naming the
    /// action and how many of its records wait. It depends on the clone, like an interrupted batch, so a fresh clone reports none.
    /// Caller holds the gate.
    /// </summary>
    private FindingList UncommittedTrails(EstateModel model)
    {
        var findings = new FindingList();
        if (session.Profile != "repository-synced" || Providers.RecordCommitAdapter is not { } adapter) return findings;
        foreach ((OperatorCheckpoint checkpoint, IReadOnlyList<string> untracked) in UntrackedTrailsLocked(adapter, model))
        {
            RecordEnvelope? record = model.ById.TryGetValue(checkpoint.Id, out RecordEnvelope? found) && found.ComputedHash == checkpoint.Hash ? found : null;
            string type = record?.Type ?? checkpoint.Type ?? "record";
            string? subject = record?.Subject is { } reference ? reference.Type + ":" + reference.Id + (reference.Hash is null ? string.Empty : ":" + reference.Hash) : checkpoint.Subject;
            string named = $"{type} {checkpoint.Id}" + (subject is null ? string.Empty : " of " + subject);
            string count = untracked.Count == 1 ? "1 record" : $"{untracked.Count} records";
            findings.Warning(TrailUncommittedCode, "operator-action:" + checkpoint.Id,
                $"This clone holds {count} of the push and merge trail of operator action {named} uncommitted. The next operator action carries them into its own commit; until a commit holds them they exist only in this clone.");
        }
        return findings;
    }

    // The next step of a completed action: its own trail stays uncommitted in this clone until the next action carries it.
    private static string CompletedStep(OperatorCheckpoint checkpoint)
    {
        int carried = checkpoint.CarriedFiles.Count;
        return "The signed record and checked merge are complete."
            + (carried == 0 ? string.Empty : $" Its commit carried {(carried == 1 ? "1 trail record" : carried + " trail records")} of earlier actions.")
            + " Its own push and merge trail stays uncommitted in this clone until the next operator action carries it into its commit.";
    }
}
