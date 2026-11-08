// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.History;

/// <summary>What an estate's history checkpoints establish: the graph's heads, what they witness, the unwitnessed tail and every problem.</summary>
public sealed class HistoryState
{
    internal HistoryState(bool adopted, IReadOnlyList<RecordReference> heads, int witnessed, int tail, IReadOnlyList<Finding> findings, bool inventoriesRead,
        IReadOnlySet<string> altered, IReadOnlyDictionary<string, WitnessedEntry> entries, IReadOnlySet<string> acknowledged, IReadOnlySet<string> counting)
    {
        CountingCheckpoints = counting;
        Adopted = adopted;
        Heads = heads;
        Witnessed = witnessed;
        Tail = tail;
        Findings = findings;
        InventoriesRead = inventoriesRead;
        Altered = altered;
        Entries = entries;
        Acknowledged = acknowledged;
    }

    /// <summary>Whether the estate has adopted checkpoints: it holds a signed checkpoint that counts.</summary>
    public bool Adopted { get; }

    /// <summary>The counting checkpoints no counting checkpoint follows, sorted by id, each pinned to its hash.</summary>
    public IReadOnlyList<RecordReference> Heads { get; }

    /// <summary>How many records the counting checkpoints witness, through their inventories and those of the checkpoints they follow, less acknowledged removals.</summary>
    public int Witnessed { get; }

    /// <summary>How many records present here no checkpoint witnesses yet: the unwitnessed tail.</summary>
    public int Tail { get; }

    /// <summary>The history findings: removed, altered or missing witnessed records, missing checkpoints and objects, invalid checkpoints and unauthorized anchors.</summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>Whether the inventories were read; a model built without the estate's objects judges anchors and adoption only.</summary>
    public bool InventoriesRead { get; }

    /// <summary>Whether a counting checkpoint, or one it follows, lists the record.</summary>
    public bool IsWitnessed(string id) => Entries.ContainsKey(id);

    /// <summary>Whether an anchoring checkpoint acknowledges the removal of any version of the record.</summary>
    public bool IsAcknowledgedRemoval(string id) => Acknowledged.Any(key => key.StartsWith(id + " ", StringComparison.Ordinal));

    /// <summary>The ids of witnessed records present with another hash, or listed with two hashes; none of them counts.</summary>
    internal IReadOnlySet<string> Altered { get; }

    /// <summary>Every witnessed record by id, with the checkpoint that first lists it.</summary>
    internal IReadOnlyDictionary<string, WitnessedEntry> Entries { get; }

    /// <summary>Acknowledged removals, as id and hash keys.</summary>
    internal IReadOnlySet<string> Acknowledged { get; }

    /// <summary>The ids of the checkpoints that count.</summary>
    internal IReadOnlySet<string> CountingCheckpoints { get; }

    /// <summary>The earliest time a record of the unwitnessed tail states, or null when the tail is empty: what the interval trigger reads.</summary>
    public string? OldestTailAt { get; internal init; }
}

/// <summary>A witnessed record: its inventory line and the checkpoint whose inventory lists it.</summary>
/// <param name="Entry">The inventory line.</param>
/// <param name="Checkpoint">The id of the checkpoint that lists it, the earliest by id when several do.</param>
internal sealed record WitnessedEntry(InventoryEntry Entry, string Checkpoint);

/// <summary>What adopting history checkpoints would witness and anchor in a copy (design/signed-history.md § 10).</summary>
/// <param name="Adopted">Whether the copy has already adopted: it holds a counting checkpoint.</param>
/// <param name="Witnessed">How many records the adoption checkpoint would witness: every record present.</param>
/// <param name="Anchored">The unsigned records that count today, which the adoption anchors, sorted by id.</param>
/// <param name="NotCounting">The unsigned records that count for nothing today; adoption witnesses them and they stay so.</param>
/// <param name="ByRevokedDevices">Records signed by a device revoked now, each with that device, sorted by id; a device's own records are left out.</param>
public sealed record AdoptionPlan(bool Adopted, int Witnessed, IReadOnlyList<RecordEnvelope> Anchored, IReadOnlyList<RecordEnvelope> NotCounting,
    IReadOnlyList<(RecordEnvelope Record, string Device)> ByRevokedDevices);

/// <summary>A checkpoint prepared for signing: its body and the inventory objects to store before it.</summary>
/// <param name="Body">The record body.</param>
/// <param name="Objects">The inventory objects, in the order the body names them.</param>
public sealed record CheckpointDraft(JsonElement Body, IReadOnlyList<byte[]> Objects);

/// <summary>A checkpoint's body as read for judging the graph, tolerant of fields its validator reports.</summary>
internal sealed record CheckpointBody(RecordEnvelope Record, IReadOnlyList<(string Id, string Hash)> Previous, IReadOnlyList<string> Inventory,
    IReadOnlyList<(string Id, string Hash)> Anchored, IReadOnlyList<(string Id, string Hash)> Removed, long? Records, string? RecordSetHash)
{
    internal static CheckpointBody Read(RecordEnvelope record) => new(record, Pins(record.Body, "previous"), Strings(record.Body, "inventory"),
        Pins(record.Body, "anchored"), Pins(record.Body, "removed"),
        record.Body.TryGetProperty("records", out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out long n) ? n : null,
        EstateModel.BodyString(record, "recordSetHash"));

    private static List<(string Id, string Hash)> Pins(JsonElement body, string name) =>
        body.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String &&
                    e.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String)
                .Select(e => (e.GetProperty("id").GetString()!, e.GetProperty("hash").GetString()!))]
            : [];

    private static List<string> Strings(JsonElement body, string name) =>
        body.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
            : [];
}

/// <summary>What checkpoints list together, every version of each record, and the removals they acknowledge as id and hash keys.</summary>
/// <param name="Versions">Each listed record's id, with every hash a listing gives it.</param>
/// <param name="Removed">The acknowledged removals.</param>
internal sealed record Closure(Dictionary<string, HashSet<string>> Versions, HashSet<string> Removed)
{
    /// <summary>Whether some version of the record is listed and not acknowledged removed, counting any further removals given.</summary>
    internal bool Stands(string id, IEnumerable<(string Id, string Hash)> removals) =>
        Versions.TryGetValue(id, out HashSet<string>? versions) &&
        versions.Any(hash => !Removed.Contains(SignatureChecks.AnchorKey(id, hash)) && !removals.Contains((id, hash)));

    /// <summary>
    /// The witnessed set, by id and hash, once acknowledged removals, and any further ones given, take their versions out; null while a
    /// record keeps two versions, which a checkpoint cannot witness at once.
    /// </summary>
    internal Dictionary<string, string>? Witnessed(IEnumerable<(string Id, string Hash)> removals)
    {
        var removed = new HashSet<string>(Removed, StringComparer.Ordinal);
        foreach ((string id, string hash) in removals) removed.Add(SignatureChecks.AnchorKey(id, hash));
        var set = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, HashSet<string> versions) in Versions)
        {
            List<string> standing = [.. versions.Where(h => !removed.Contains(SignatureChecks.AnchorKey(id, h)))];
            if (standing.Count > 1) return null;
            if (standing.Count == 1) set[id] = standing[0];
        }

        return set;
    }
}

/// <summary>
/// Judges the history graph (design/signed-history.md §§ 5 to 7) in two steps: first the anchors and acknowledged removals, by
/// signatures alone, then, with every anchor known, which checkpoints count, what they witness and what is missing or altered.
/// </summary>
internal sealed class HistoryAnalysis
{
    private readonly EstateTrust trust;
    private readonly EstateModel model;
    private readonly List<CheckpointBody> checkpoints;
    private readonly FindingList findings = new();
    private readonly HashSet<string> acknowledged = new(StringComparer.Ordinal);
    private readonly HashSet<string> eligible = new(StringComparer.Ordinal);

    private HistoryAnalysis(EstateTrust trust, EstateModel model)
    {
        this.trust = trust;
        this.model = model;
        // The type belongs to record format 3; a record stating an earlier format is refused by validation and witnesses nothing.
        checkpoints = [.. model.OfType("history-checkpoint").Where(r => r.Format >= 3).OrderBy(r => r.Id, StringComparer.Ordinal).Select(CheckpointBody.Read)];
    }

    /// <summary>The anchors of every checkpoint whose author may anchor, as id and hash keys.</summary>
    internal HashSet<string> AnchorKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads the anchors and acknowledged removals, judged by signatures alone.</summary>
    internal static HistoryAnalysis Anchoring(EstateTrust trust, EstateModel model)
    {
        var analysis = new HistoryAnalysis(trust, model);
        foreach (CheckpointBody checkpoint in analysis.checkpoints.Where(c => c.Previous.Count == 0 || c.Anchored.Count > 0 || c.Removed.Count > 0))
        {
            bool eligible = trust.MayAnchor(checkpoint.Record);
            if (eligible) analysis.eligible.Add(checkpoint.Record.Id);
            if (checkpoint.Previous.Count == 0 && !eligible && checkpoint.Record.Signatures.Count > 0)
            {
                // Adoption changes what counts in the whole estate, so only those who may anchor adopt (design/signed-history.md § 10).
                analysis.findings.Error("history.adoption-author", checkpoint.Record.ExpectedPath + "#body.previous",
                    "An estate's first checkpoint, its adoption, is signed by the root operator or a person holding administer through a grant that rests on signatures, never an agent, with devices whose own admissions rest on signatures. This one counts for nothing and adopts nothing.");
            }

            if (checkpoint.Anchored.Count == 0 && checkpoint.Removed.Count == 0) continue;
            if (eligible)
            {
                foreach ((string id, string hash) in checkpoint.Anchored) analysis.AnchorKeys.Add(SignatureChecks.AnchorKey(id, hash));
                foreach ((string id, string hash) in checkpoint.Removed) analysis.acknowledged.Add(SignatureChecks.AnchorKey(id, hash));
            }
            else if (checkpoint.Record.Signatures.Count > 0)
            {
                analysis.findings.Error("history.anchor-author", checkpoint.Record.ExpectedPath + "#body." + (checkpoint.Anchored.Count > 0 ? "anchored" : "removed"),
                    "Only the root operator, or a person holding administer through a grant that rests on signatures, anchors records or acknowledges removals, never an agent, and with devices whose own admissions rest on signatures. This checkpoint's anchored and removed lists count for nothing.");
            }
        }

        return analysis;
    }

    /// <summary>Judges the graph once anchors are known.</summary>
    internal HistoryState Complete(Func<string, byte[]?>? objects, IReadOnlySet<string>? files)
    {
        var byId = checkpoints.ToDictionary(c => c.Record.Id, StringComparer.Ordinal);
        var altered = new HashSet<string>(StringComparer.Ordinal);

        // A checkpoint counts when its signatures verify and it is either an adoption checkpoint by an author who may anchor, or follows
        // only checkpoints that count, through hashes that match. So the estate has adopted only when the counting graph reaches its
        // adoption checkpoint, and a checkpoint whose path is broken or leads to one that does not count witnesses nothing.
        // The graph is walked with a stack of its own rather than by recursion, so a long chain read in any order cannot exhaust the
        // call stack. A checkpoint is marked as not counting while its predecessors are judged.
        var judged = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Counting(CheckpointBody start)
        {
            if (judged.TryGetValue(start.Record.Id, out bool known)) return known;
            var path = new Stack<(CheckpointBody Checkpoint, int Next)>();
            Enter(start);
            while (path.TryPop(out (CheckpointBody Checkpoint, int Next) frame))
            {
                (CheckpointBody checkpoint, int next) = frame;
                bool value = true;
                bool descended = false;
                for (; next < checkpoint.Previous.Count; next++)
                {
                    (string id, string hash) = checkpoint.Previous[next];
                    if (!byId.TryGetValue(id, out CheckpointBody? predecessor) || predecessor.Record.ComputedHash != hash)
                    {
                        value = false;
                        break;
                    }

                    if (judged.TryGetValue(id, out bool counts))
                    {
                        if (counts) continue;
                        value = false;
                        break;
                    }

                    path.Push((checkpoint, next));
                    Enter(predecessor);
                    descended = true;
                    break;
                }

                if (!descended) judged[checkpoint.Record.Id] = value;
            }

            return judged[start.Record.Id];

            void Enter(CheckpointBody checkpoint)
            {
                string id = checkpoint.Record.Id;
                judged[id] = false;
                if (!trust.Authentic(checkpoint.Record)) return;
                if (checkpoint.Previous.Count == 0)
                {
                    judged[id] = eligible.Contains(id);
                    return;
                }

                path.Push((checkpoint, 0));
            }
        }

        List<CheckpointBody> counting = [.. checkpoints.Where(Counting)];
        foreach (CheckpointBody checkpoint in checkpoints.Where(c => trust.Authentic(c.Record)))
        {
            foreach ((string id, string hash) in checkpoint.Previous)
            {
                if (!byId.TryGetValue(id, out CheckpointBody? predecessor))
                {
                    if (model.ById.ContainsKey(id))
                        findings.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.previous", $"History checkpoint {checkpoint.Record.Id} follows {id}, which is not a format-3 history checkpoint; it witnesses nothing.");
                    else
                        findings.Error("history.checkpoint-missing", checkpoint.Record.ExpectedPath + "#body.previous",
                            $"History checkpoint {checkpoint.Record.Id} follows checkpoint {id} at {hash}, which is not here; until it is restored from any clone or the repository's history, verified against that hash, this checkpoint witnesses nothing.");
                }
                else if (predecessor.Record.ComputedHash != hash)
                {
                    if (altered.Add(id))
                        findings.Error("history.altered", predecessor.Record.ExpectedPath,
                            $"History checkpoint {checkpoint.Record.Id} follows this checkpoint at {hash}, and it is present with hash {predecessor.Record.ComputedHash}. Restore the witnessed bytes from any clone or the repository's history.");
                }
                else if (!Counting(predecessor))
                {
                    findings.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.previous",
                        $"History checkpoint {checkpoint.Record.Id} follows checkpoint {id}, which does not count, so this checkpoint witnesses nothing: a checkpoint counts only on a path of counting checkpoints back to an adoption checkpoint by the root operator or a person holding administer.");
                }
            }
        }

        var followed = new HashSet<string>(counting.SelectMany(c => c.Previous.Select(p => p.Id)), StringComparer.Ordinal);
        List<RecordReference> heads = [.. counting.Where(c => !followed.Contains(c.Record.Id)).Select(c => c.Record.Reference)];

        // Whether a checkpoint's author may anchor, read once per checkpoint.
        var anchorers = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Anchorer(string id)
        {
            if (!anchorers.TryGetValue(id, out bool value)) anchorers[id] = value = eligible.Contains(id) || trust.MayAnchor(byId[id].Record);
            return value;
        }

        var entries = new Dictionary<string, WitnessedEntry>(StringComparer.Ordinal);
        var inventories = new Dictionary<string, IReadOnlyList<InventoryEntry>>(StringComparer.Ordinal);
        bool read = objects is not null;
        if (read)
        {
            // Every listing of every record by a counting checkpoint, earliest checkpoint first. A version whose removal is acknowledged
            // stands no more, and a record re-added after it is part of the tail until a checkpoint lists it.
            var listings = new Dictionary<string, List<WitnessedEntry>>(StringComparer.Ordinal);
            foreach (CheckpointBody checkpoint in counting.OrderBy(c => c.Record.Id, StringComparer.Ordinal))
            {
                List<InventoryEntry> listed = Inventory(checkpoint, objects!, findings);
                inventories[checkpoint.Record.Id] = listed;
                foreach (InventoryEntry entry in listed)
                {
                    if (entry.Id == checkpoint.Record.Id)
                    {
                        findings.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.inventory", "A checkpoint cannot list itself; the next checkpoint witnesses it.");
                        continue;
                    }

                    if (!listings.TryGetValue(entry.Id, out List<WitnessedEntry>? versions)) listings[entry.Id] = versions = [];
                    versions.Add(new WitnessedEntry(entry, checkpoint.Record.Id));
                }
            }

            foreach ((string id, List<WitnessedEntry> versions) in listings.OrderBy(l => l.Key, StringComparer.Ordinal))
            {
                List<WitnessedEntry> standing = [.. versions.Where(v => !acknowledged.Contains(SignatureChecks.AnchorKey(v.Entry.Id, v.Entry.Hash)))];
                if (standing.Count == 0) continue;
                model.ById.TryGetValue(id, out RecordEnvelope? present);
                if (present is not null)
                {
                    List<WitnessedEntry> agreeing = [.. standing.Where(v => v.Entry.Hash == present.ComputedHash)];
                    List<WitnessedEntry> disagreeing = [.. standing.Where(v => v.Entry.Hash != present.ComputedHash)];
                    entries[id] = agreeing.Count > 0 ? agreeing[0] : standing[0];
                    if (disagreeing.Count == 0) continue;

                    // Only a listing by an author who may anchor can make a present record that one who may anchor signed, or that is anchored
                    // with these bytes, count for nothing; another author's listing of other bytes is reported, and the record keeps counting.
                    // Any other record listed with other bytes is altered, whoever signed it, its own signer included.
                    if ((trust.MayAnchor(present) || trust.Anchors.Contains(SignatureChecks.AnchorKey(id, present.ComputedHash))) &&
                        disagreeing.All(v => !Anchorer(v.Checkpoint)))
                    {
                        foreach (WitnessedEntry listing in disagreeing)
                        {
                            findings.Warning("history.listing-mismatch", present.ExpectedPath,
                                $"History checkpoint {listing.Checkpoint}, by {byId[listing.Checkpoint].Record.By}, lists this record with hash {listing.Entry.Hash}, and the record here has hash {present.ComputedHash} and is signed by one who may anchor, or is anchored with that hash. A listing by an author who may not anchor makes no such record count for nothing; the root operator or a person holding administer acknowledges the listed version's removal to settle it.");
                        }

                        continue;
                    }

                    altered.Add(id);
                    findings.Error("history.altered", present.ExpectedPath, agreeing.Count > 0
                        ? $"History checkpoints {agreeing[0].Checkpoint}, by {byId[agreeing[0].Checkpoint].Record.By}, and {disagreeing[0].Checkpoint}, by {byId[disagreeing[0].Checkpoint].Record.By}, witness this record with different hashes, {agreeing[0].Entry.Hash} and {disagreeing[0].Entry.Hash}. Restore the bytes a counting checkpoint witnessed, or have the root operator or a person holding administer acknowledge the other version's removal."
                        : $"Witnessed by history checkpoint {disagreeing[0].Checkpoint}, by {byId[disagreeing[0].Checkpoint].Record.By}, with hash {disagreeing[0].Entry.Hash}, and present with hash {present.ComputedHash}. It counts for nothing until the witnessed bytes are restored from any clone or the repository's history, or the root operator or a person holding administer acknowledges the witnessed version's removal.");
                    continue;
                }

                WitnessedEntry witnessed = standing[0];
                entries[id] = witnessed;
                InventoryEntry absent = witnessed.Entry;
                if (standing.Select(v => v.Entry.Hash).Distinct(StringComparer.Ordinal).Skip(1).FirstOrDefault() is { } other)
                {
                    altered.Add(id);
                    findings.Error("history.altered", absent.RepositoryPath,
                        $"History checkpoints {witnessed.Checkpoint} and {standing.First(v => v.Entry.Hash == other).Checkpoint} witness this record with different hashes, {absent.Hash} and {other}, and it is not here. Restore the bytes a counting checkpoint witnessed, or have the root operator or a person holding administer acknowledge the removal of each version.");
                }
                else if (files?.Contains(absent.RepositoryPath) == true)
                {
                    altered.Add(id);
                    findings.Error("history.altered", absent.RepositoryPath,
                        $"Witnessed by history checkpoint {witnessed.Checkpoint} with hash {absent.Hash}, and the file here is not that record. Restore the witnessed bytes from any clone or the repository's history, verified against that hash.");
                }
                else
                {
                    findings.Error("history.removed", absent.RepositoryPath,
                        $"Witnessed by history checkpoint {witnessed.Checkpoint} with hash {absent.Hash}, and absent here. Restore the file from any clone or the repository's history, verified against that hash, or have the root operator or a person holding administer acknowledge the removal, with a reason, in an anchoring checkpoint's removed list.");
                }
            }

            foreach (RecordReference head in heads)
            {
                CheckpointBody checkpoint = byId[head.Id];
                if (CloseOver([checkpoint.Record.Id], byId, c => inventories.GetValueOrDefault(c.Record.Id), c => Anchorer(c.Record.Id))?.Witnessed([]) is not { } set) continue;
                string hash = RecordSets.Hash(set.Select(p => (p.Key, p.Value)));
                if (checkpoint.Records != set.Count || checkpoint.RecordSetHash != hash)
                {
                    findings.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.recordSetHash",
                        $"This checkpoint states {checkpoint.Records?.ToString(CultureInfo.InvariantCulture) ?? "no count"} records with set hash {checkpoint.RecordSetHash ?? "(none)"}, and what it witnesses is {set.Count.ToString(CultureInfo.InvariantCulture)} records with set hash {hash}.");
                }
            }
        }

        // Without the inventories nothing is known to be witnessed, and the records are not walked: a projection that reads none of them
        // stays as cheap as before. With them, the tail and the time its oldest record states, which the interval trigger reads.
        int tail = read ? 0 : model.Records.Count;
        string? oldest = null;
        if (read && counting.Count > 0)
        {
            foreach (RecordEnvelope record in model.Records)
            {
                if (entries.ContainsKey(record.Id)) continue;
                tail++;
                if (RecordParser.IsTimestamp(record.At) && (oldest is null || string.CompareOrdinal(record.At, oldest) < 0)) oldest = record.At;
            }
        }
        else if (read)
        {
            tail = model.Records.Count;
        }

        return new HistoryState(counting.Count > 0, heads, entries.Count, tail, [.. findings], read, altered, entries, acknowledged,
            counting.Select(c => c.Record.Id).ToHashSet(StringComparer.Ordinal)) { OldestTailAt = oldest };
    }

    /// <summary>
    /// What checkpoints witness together: every listing in their inventories and those of the checkpoints they follow, through hashes
    /// that match, and the removals they acknowledge; null when an inventory cannot be read.
    /// </summary>
    internal static Closure? CloseOver(IEnumerable<string> starts, IReadOnlyDictionary<string, CheckpointBody> byId, Func<CheckpointBody, IReadOnlyList<InventoryEntry>?> inventory,
        Func<CheckpointBody, bool> mayAcknowledge)
    {
        var closure = new Closure(new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (string start in starts)
        {
            if (seen.Add(start)) queue.Enqueue(start);
        }

        while (queue.Count > 0)
        {
            CheckpointBody checkpoint = byId[queue.Dequeue()];
            if (inventory(checkpoint) is not { } listed) return null;
            foreach (InventoryEntry entry in listed)
            {
                if (!closure.Versions.TryGetValue(entry.Id, out HashSet<string>? versions)) closure.Versions[entry.Id] = versions = new(StringComparer.Ordinal);
                versions.Add(entry.Hash);
            }

            // Only an author who may anchor acknowledges a removal.
            if (checkpoint.Removed.Count > 0 && mayAcknowledge(checkpoint))
            {
                foreach ((string id, string hash) in checkpoint.Removed) closure.Removed.Add(SignatureChecks.AnchorKey(id, hash));
            }

            foreach ((string id, string hash) in checkpoint.Previous)
            {
                if (byId.TryGetValue(id, out CheckpointBody? predecessor) && predecessor.Record.ComputedHash == hash && seen.Add(id)) queue.Enqueue(id);
            }
        }

        return closure;
    }

    /// <summary>Reads a checkpoint's inventory objects in order, reporting each missing or malformed one.</summary>
    internal static List<InventoryEntry> Inventory(CheckpointBody checkpoint, Func<string, byte[]?> objects, FindingList? findings)
    {
        var listed = new List<InventoryEntry>();
        string? last = null;
        foreach (string hash in checkpoint.Inventory.Where(ContentHash.IsValid))
        {
            byte[]? bytes;
            try { bytes = objects(hash); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { bytes = null; }
            if (bytes is null || ContentHash.Sha256(bytes) != hash)
            {
                findings?.Error("history.checkpoint-missing", checkpoint.Record.ExpectedPath + "#body.inventory",
                    $"Inventory object {hash} of history checkpoint {checkpoint.Record.Id} is not here. Restore objects/ from any clone or the repository's history; until then the records it lists cannot be checked.");
                continue;
            }

            InventoryObject parsed = HistoryInventory.Read(hash, bytes);
            foreach (string problem in parsed.Problems)
                findings?.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.inventory", $"Inventory object {hash}: {problem}");
            foreach (InventoryEntry entry in parsed.Entries)
            {
                if (last is not null && string.CompareOrdinal(entry.Id, last) <= 0)
                {
                    findings?.Error("history.checkpoint-invalid", checkpoint.Record.ExpectedPath + "#body.inventory", $"Inventory object {hash} lists {entry.Id} out of order: an inventory is sorted by id across its objects.");
                    continue;
                }

                last = entry.Id;
                listed.Add(entry);
            }
        }

        return listed;
    }
}

/// <summary>
/// History checkpoints (design/signed-history.md): the state they establish, a checkpoint prepared for signing, the checks a new one
/// meets when written, and the check that a copy holds a checkpoint known elsewhere.
/// </summary>
public static class SignedHistory
{
    /// <summary>The codes of the history errors that hold release submission and lead status while one stands.</summary>
    internal static readonly IReadOnlyList<string> ProblemCodes = ["history.removed", "history.altered", "history.checkpoint-missing", "history.checkpoint-invalid", "history.anchor-author", "history.adoption-author", "history.unanchored"];

    /// <summary>How many records the unwitnessed tail holds when the runtime writes an interval checkpoint (design/signed-history.md § 9).</summary>
    public const int IntervalRecords = 500;

    /// <summary>How old the tail's oldest record is when the runtime writes an interval checkpoint (design/signed-history.md § 9).</summary>
    public static readonly TimeSpan IntervalAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether an interval checkpoint falls due as a write adds records: in an estate that has adopted checkpoints, once the tail would
    /// hold <see cref="IntervalRecords"/> records or its oldest record is <see cref="IntervalAge"/> old at the time given.
    /// </summary>
    public static bool IntervalDue(EstateModel model, DateTimeOffset now, int adding = 1)
    {
        ArgumentNullException.ThrowIfNull(model);
        HistoryState state = Of(model);
        if (!state.Adopted) return false;
        if ((long)state.Tail + adding >= IntervalRecords) return true;
        return state.OldestTailAt is { } oldest && DateTimeOffset.TryParseExact(oldest, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at) && now - at >= IntervalAge;
    }

    /// <summary>The records present that no counting checkpoint witnesses: the unwitnessed tail, sorted by id.</summary>
    public static IReadOnlyList<RecordEnvelope> TailRecords(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        HistoryState state = Of(model);
        return [.. model.Records.Where(r => !state.InventoriesRead || !state.Entries.ContainsKey(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What adopting checkpoints would witness and anchor in this copy (design/signed-history.md § 10): every record present, the
    /// unsigned records that count today, which the adoption anchors so that it judges history as found, the unsigned records that count
    /// for nothing today and stay so, and the records signed by a device revoked now. Adoption keeps every reading as it is: an unsigned
    /// admission or record that does not count is never anchored, so adoption admits and authorizes nothing new.
    /// </summary>
    public static AdoptionPlan PlanAdoption(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        HistoryState state = Of(model);
        List<RecordEnvelope> unsigned = [.. model.Records.Where(r => r.Signatures.Count == 0).OrderBy(r => r.Id, StringComparer.Ordinal)];
        List<RecordEnvelope> anchored = [.. unsigned.Where(r => r.Type != "history-checkpoint" && model.Counts(r))];
        List<RecordEnvelope> excluded = [.. unsigned.Where(r => !anchored.Contains(r))];
        var revoked = new List<(RecordEnvelope Record, string Device)>();
        Dictionary<string, bool> revokedDevices = new(StringComparer.Ordinal);
        foreach (RecordEnvelope record in model.Records.Where(r => r.Signatures.Count > 0).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            foreach (string device in record.Signatures.Select(s => s.Device).Distinct(StringComparer.Ordinal))
            {
                // A device's own records name it, and say nothing more about what it signed.
                if (record.Type == "device" && model.Chain(record)[0] == device) continue;
                if (!revokedDevices.TryGetValue(device, out bool isRevoked))
                {
                    RecordEnvelope? current = model.ById.TryGetValue(device, out RecordEnvelope? first) && first.Type == "device" ? model.Latest(first) : null;
                    isRevoked = current is not null && SignatureChecks.AdmissionOf(model, current) is { } admission && EstateModel.BodyString(admission, "status") == "revoked";
                    revokedDevices[device] = isRevoked;
                }

                if (isRevoked)
                {
                    revoked.Add((record, device));
                    break;
                }
            }
        }

        return new AdoptionPlan(state.Adopted, model.Records.Count, anchored, excluded, revoked);
    }

    /// <summary>The estate's history state.</summary>
    public static HistoryState Of(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Trust.History;
    }

    /// <summary>Whether the estate has adopted history checkpoints: it holds one that counts.</summary>
    public static bool Adopted(EstateModel model) => Of(model).Adopted;

    /// <summary>
    /// Prepares the next checkpoint of a copy: it follows every head, its inventory lists every record no head witnesses, sorted by id
    /// and split under the object limit, and it states the count and set hash of what it witnesses. Anchors and acknowledged removals
    /// are the caller's choice and count only for the root operator or a person holding administer. The runtime writes nothing here.
    /// </summary>
    public static CheckpointDraft Prepare(EstateModel model, string trigger, IEnumerable<RecordEnvelope>? anchored = null, IEnumerable<(string Id, string Hash, string Reason)>? removed = null,
        (string Kind, string Id)? closes = null, Func<string, byte[]?>? objects = null, long maxObjectBytes = HistoryInventory.MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        objects ??= model.ObjectReader;
        HistoryState state = Of(model);
        Closure closure = Heads(model, state.Heads, objects) ?? throw new InvalidOperationException("The heads' inventories cannot be read; restore them before writing a checkpoint.");
        List<(string Id, string Hash, string Reason)> removals = [.. (removed ?? []).OrderBy(r => r.Id, StringComparer.Ordinal)];
        List<RecordEnvelope> tail = [.. model.Records.Where(r => !closure.Stands(r.Id, removals.Select(x => (x.Id, x.Hash)))).OrderBy(r => r.Id, StringComparer.Ordinal)];
        IReadOnlyList<byte[]> inventory = HistoryInventory.Write(tail, maxObjectBytes);
        Dictionary<string, string> set = closure.Witnessed(removals.Select(r => (r.Id, r.Hash)))
            ?? throw new InvalidOperationException("The heads witness a record with two hashes; acknowledge the removal of the version that is not here.");
        foreach (RecordEnvelope record in tail) set[record.Id] = record.ComputedHash;
        var body = new JsonObject
        {
            ["previous"] = new JsonArray([.. state.Heads.OrderBy(h => h.Id, StringComparer.Ordinal).Select(h => (JsonNode)new JsonObject { ["id"] = h.Id, ["hash"] = h.Hash })]),
            ["inventory"] = new JsonArray([.. inventory.Select(o => (JsonNode)JsonValue.Create(ContentHash.Sha256(o))!)]),
            ["records"] = set.Count,
            ["recordSetHash"] = RecordSets.Hash(set.Select(p => (p.Key, p.Value))),
            ["trigger"] = trigger,
        };
        if (closes is { } closing) body["closes"] = new JsonObject { ["kind"] = closing.Kind, ["id"] = closing.Id };
        List<RecordEnvelope> anchors = [.. (anchored ?? []).OrderBy(r => r.Id, StringComparer.Ordinal)];
        if (anchors.Count > 0) body["anchored"] = new JsonArray([.. anchors.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["hash"] = r.ComputedHash })]);
        if (removals.Count > 0) body["removed"] = new JsonArray([.. removals.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["hash"] = r.Hash, ["reason"] = r.Reason })]);
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return new CheckpointDraft(document.RootElement.Clone(), inventory);
    }

    /// <summary>
    /// Why a checkpoint cannot be written to this copy, or null: it must follow exactly the copy's heads, list exactly the records no
    /// head witnesses, sorted by id under the object limit and in objects held here, state the count and set hash of what it witnesses,
    /// anchor only unsigned records present with the hash it names, and acknowledge only the removal of a witnessed record that is absent.
    /// </summary>
    public static string? AuthoringRefusal(EstateModel model, JsonElement body)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (body.ValueKind != JsonValueKind.Object) return null;
        Func<string, byte[]?>? objects = model.ObjectReader;
        if (objects is null) return "This copy's objects cannot be read, so a checkpoint cannot be checked before it is written.";
        HistoryState state = Of(model);
        var record = new RecordEnvelope("history-checkpoint", Ulid.NewUlid().Value, "2000-01-01T00:00:00Z", "", null, null, body, "", "", []);
        CheckpointBody draft = CheckpointBody.Read(record);
        if (!draft.Previous.OrderBy(p => p.Id, StringComparer.Ordinal).SequenceEqual(state.Heads.OrderBy(h => h.Id, StringComparer.Ordinal).Select(h => (h.Id, h.Hash!))))
            return $"A checkpoint follows every head this copy holds, and only those: {(state.Heads.Count == 0 ? "none, so it is the adoption checkpoint" : string.Join(", ", state.Heads.Select(h => h.Id + " at " + h.Hash)))}.";
        if (Heads(model, state.Heads, objects) is not { } closure) return "The heads' inventories cannot be read; restore them before writing a checkpoint.";
        var findings = new FindingList();
        List<InventoryEntry> listed = HistoryAnalysis.Inventory(draft, objects, findings);
        if (findings.Count > 0 || listed.Count == 0 && draft.Inventory.Count > 0) return "Its inventory is missing or malformed: " + string.Join(" ", findings.Select(f => f.Message));
        foreach (string hash in draft.Inventory.Where(ContentHash.IsValid))
        {
            if (objects(hash) is { } bytes && bytes.LongLength > HistoryInventory.MaxObjectBytes) return $"Inventory object {hash} exceeds the object limit.";
        }

        List<RecordEnvelope> tail = [.. model.Records.Where(r => !closure.Stands(r.Id, draft.Removed)).OrderBy(r => r.Id, StringComparer.Ordinal)];
        if (!listed.Select(e => (e.Id, e.Path, e.Hash)).SequenceEqual(tail.Select(r => (r.Id, r.ExpectedPath[(EstateLayout.RecordsDirectory.Length + 1)..], r.ComputedHash))))
            return $"Its inventory lists every record no head witnesses, and only those: {tail.Count.ToString(CultureInfo.InvariantCulture)} records here, sorted by id, each by its path under records/ and its hash.";
        foreach ((string id, string hash) in draft.Anchored)
        {
            if (!model.ById.TryGetValue(id, out RecordEnvelope? anchored) || anchored.ComputedHash != hash) return $"It anchors {id} at {hash}, which is not here with that hash.";
            if (anchored.Signatures.Count > 0) return $"It anchors {id}, which is signed and needs no anchor; only records recorded without a signature are anchored.";
        }

        foreach ((string id, string hash) in draft.Removed)
        {
            if (!closure.Versions.TryGetValue(id, out HashSet<string>? versions) || !versions.Contains(hash)) return $"It acknowledges the removal of {id} at {hash}, which no head witnesses with that hash.";
            if (model.ById.TryGetValue(id, out RecordEnvelope? present) && present.ComputedHash == hash) return $"It acknowledges the removal of {id}, which is present here with the witnessed hash.";
        }

        if (closure.Witnessed(draft.Removed) is not { } set) return "The heads witness a record with two hashes; acknowledge the removal of the version that is not here.";
        foreach (RecordEnvelope tailRecord in tail) set[tailRecord.Id] = tailRecord.ComputedHash;
        string setHash = RecordSets.Hash(set.Select(p => (p.Key, p.Value)));
        if (draft.Records != set.Count || draft.RecordSetHash != setHash)
            return $"It witnesses {set.Count.ToString(CultureInfo.InvariantCulture)} records with set hash {setHash}; records and recordSetHash state exactly that.";
        return null;
    }

    /// <summary>
    /// Whether this copy holds an expected checkpoint, named by id, by content hash or by at least sixteen hexadecimal digits of it: present,
    /// signed and counting. A fresh machine cannot tell whether the newest checkpoints were removed with the tail, so it checks against
    /// a head known elsewhere (design/signed-history.md § 9). Null when it does; otherwise the error that refuses the copy.
    /// </summary>
    public static Finding? Expect(EstateModel model, string expected)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Expect(model.Records.Where(r => r.Type == "history-checkpoint").Select(r => (r.Id, r.ComputedHash, r.ExpectedPath, model.Counts(r))), expected);
    }

    /// <summary>The fewest hexadecimal digits of a checkpoint's hash that name it for <see cref="Expect(EstateModel, string)"/>.</summary>
    public const int MinimumExpectedDigits = 16;

    /// <summary>Whether the given checkpoints, by id, hash, path and whether each counts, hold the expected one; null when they do.</summary>
    public static Finding? Expect(IEnumerable<(string Id, string Hash, string Path, bool Counts)> checkpoints, string expected) => ExpectMatch(checkpoints, expected).Missing;

    /// <summary>
    /// Whether the given checkpoints hold the expected one, as <see cref="Expect(IEnumerable{ValueTuple{string, string, string, bool}}, string)"/>
    /// says, with the one the name matches when it does: what a machine remembers of an <c>estate check --expect</c>.
    /// </summary>
    public static (Finding? Missing, (string Id, string Hash, string Path)? Matched) ExpectMatch(IEnumerable<(string Id, string Hash, string Path, bool Counts)> checkpoints, string expected)
    {
        (Finding? missing, (string Id, string Hash, string Path, bool Counts)? match) = ExpectCore(checkpoints, expected);
        return (missing, missing is null && match is { } held ? (held.Id, held.Hash, held.Path) : null);
    }

    private static (Finding? Missing, (string Id, string Hash, string Path, bool Counts)? Match) ExpectCore(IEnumerable<(string Id, string Hash, string Path, bool Counts)> checkpoints, string expected)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);
        string text = expected.Trim();
        string hex = text.StartsWith(ContentHash.Prefix, StringComparison.Ordinal) ? text[ContentHash.Prefix.Length..] : text;
        bool byId = text.Length == 26 && Ulid.IsValid(text);
        bool byHash = !byId && hex.Length is >= MinimumExpectedDigits and <= 64 && hex.All(char.IsAsciiHexDigit);
        if (!byId && !byHash)
            return (new Finding(Severity.Error, "history.expected-missing", EstateLayout.RecordsDirectory, $"'{expected}' names no checkpoint: give its id, its full content hash or at least {MinimumExpectedDigits} hexadecimal digits of the hash."), null);
        string lower = hex.ToLowerInvariant();
        List<(string Id, string Hash, string Path, bool Counts)> matches = [.. checkpoints.Where(c =>
            byId ? c.Id == text : c.Hash.StartsWith(ContentHash.Prefix, StringComparison.Ordinal) && c.Hash[ContentHash.Prefix.Length..].StartsWith(lower, StringComparison.Ordinal))];
        if (matches.Count > 1)
            return (new Finding(Severity.Error, "history.expected-missing", EstateLayout.RecordsDirectory, $"'{expected}' names {matches.Count.ToString(CultureInfo.InvariantCulture)} checkpoints here; give more of its hash."), null);
        if (matches.Count == 0)
            return (new Finding(Severity.Error, "history.expected-missing", EstateLayout.RecordsDirectory, $"This copy lacks the expected history checkpoint {expected}: its newest history may have been removed with the tail, or it is behind the copy that checkpoint came from. Fetch or restore that history before relying on this copy."), null);
        if (!matches[0].Counts)
            return (new Finding(Severity.Error, "history.expected-missing", matches[0].Path, $"The expected history checkpoint {matches[0].Id} is here but does not count: it is unsigned, its signature does not verify, it was altered, or its author may not adopt."), matches[0]);
        return (null, matches[0]);
    }

    /// <summary>What a copy's heads witness together, read through its objects; null when an inventory is missing or malformed.</summary>
    private static Closure? Heads(EstateModel model, IReadOnlyList<RecordReference> heads, Func<string, byte[]?>? objects)
    {
        if (heads.Count == 0) return new Closure(new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        if (objects is null) return null;
        var byId = model.Records.Where(r => r.Type == "history-checkpoint" && r.Format >= 3).Select(CheckpointBody.Read).ToDictionary(c => c.Record.Id, StringComparer.Ordinal);
        return HistoryAnalysis.CloseOver(heads.Select(h => h.Id), byId, checkpoint =>
        {
            var problems = new FindingList();
            List<InventoryEntry> listed = HistoryAnalysis.Inventory(checkpoint, objects, problems);
            return problems.Count > 0 ? null : listed;
        }, checkpoint => model.Trust.MayAnchor(checkpoint.Record));
    }
}
