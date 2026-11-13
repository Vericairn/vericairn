// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Estate;

/// <summary>
/// A closing that acts (design/signed-history.md § 8): a history checkpoint naming a device, a key or a grant revision, signed by one
/// who may perform the act. From it on, what that device or key signed, or what relied on that grant revision, counts only when the
/// closing, or a checkpoint it follows, witnesses it, whatever date the record states.
/// </summary>
/// <param name="Record">The closing checkpoint.</param>
/// <param name="Kind">device, key or grant.</param>
/// <param name="Target">The id the closing names: a device record, the device revision whose key is replaced, or the grant revision.</param>
/// <param name="Identity">For a device or a key, the device's identity.</param>
/// <param name="Key">For a key, the public key closed.</param>
/// <param name="Grant">For a grant, the revision named.</param>
/// <param name="Rotation">For a key, the rotation that replaces it: the act the closing precedes, which the closing does not refuse.</param>
/// <param name="Before">The closing and every checkpoint it follows: what they list is what the closing witnesses.</param>
/// <param name="Line">For a grant, the line of the revision named: the id of its first record.</param>
/// <param name="Closed">For a grant, the revisions closed: the one named and every revision of its line dated at or before it.</param>
internal sealed record Closing(RecordEnvelope Record, string Kind, string Target, string? Identity, string? Key, RecordEnvelope? Grant, string? Rotation, IReadOnlySet<string> Before,
    string? Line = null, IReadOnlyList<RecordEnvelope>? Closed = null)
{
    /// <summary>Whether this closing closes a grant revision.</summary>
    internal bool Closes(RecordEnvelope grant) => Closed is not null && Closed.Any(r => r.Id == grant.Id);
}

/// <summary>
/// Closings (design/signed-history.md § 8). They are judged first, in checkpoint order: every checkpoint after those it follows, ids
/// breaking ties. A closing acts when the checkpoints it follows, present with the hashes it names, reach an adoption by one who may
/// anchor, read against the closings that act, when it verifies against the closings before it in that order, and when it is signed by
/// one who may perform the act: an operator, a person holding administer or an operator's recovery device for a device, the device itself
/// for its own key, and an operator for a grant revision, the role or grant still standing as read against the closings before it, never
/// against itself, and closed by no closing before it. A grant closing closes the
/// revision it names and every revision of the same line dated at or before it. A closing acts by itself, so the checkpoints between it and the adoption need not count: closings
/// only ever refuse. Concurrent closings each act, since neither is before the other. Every later judgment reads the closings that act:
/// a record one of them refuses does not count, an admission or revocation it refuses neither admits nor revokes, and a device revision
/// it refuses changes no key or name.
/// </summary>
internal sealed partial class EstateTrust
{
    private static readonly IReadOnlyList<Closing> NoClosings = [];
    private readonly List<Finding> closingFindings = [];
    private IReadOnlyList<Closing> closings = NoClosings;
    private bool closingsKnown;
    private bool grantClosings;
    private Dictionary<string, List<(string Checkpoint, string Hash)>>? listings;
    private readonly Dictionary<(string Revision, string Previous), bool> revisionRefusals = [];
    private Dictionary<string, RecordSigning.ImportedKey>? closedKeys = new(StringComparer.Ordinal);
    private readonly RecoveredKeys recoveredKeys = new();
    private readonly Dictionary<(string Hash, string Previous, string Signature), byte[][]?> revisionSignatureKeys = [];
    private long closedKeyVerifications;

    /// <summary>The closings that act, in checkpoint order.</summary>
    internal IReadOnlyList<Closing> Closings => closings;

    /// <summary>The findings of judging closings: closings that close nothing, closings that no act follows yet, and acts that no closing names.</summary>
    internal IReadOnlyList<Finding> ClosingFindings => closingFindings;

    /// <summary>The closings every judgment reads: none while closings are being judged, then those that act.</summary>
    private IReadOnlyList<Closing> Acting => closingsKnown ? closings : NoClosings;

    /// <summary>
    /// Judges which closings act. Each checkpoint is judged against the closings before it, with the format-2 migration's anchors as they
    /// stand before any closing, so no closing can be what makes an earlier one act, or itself. Whether a checkpoint following none roots
    /// the path is judged against the closings that act, and those act only when reached from such a root, so the two are settled
    /// together: from every root that verifies, the closings reached are judged, each root whose author may not anchor with those
    /// closings acting is dropped, and this is repeated until none is. Afterwards every judgment is made afresh.
    /// </summary>
    private void JudgeClosings(Func<string, byte[]?>? objects)
    {
        List<CheckpointBody> checkpoints = [.. model.OfType("history-checkpoint").Where(r => r.Format >= 3).OrderBy(r => r.Id, StringComparer.Ordinal).Select(CheckpointBody.Read)];
        // A closing acts through what checkpoints list, so a model built without the estate's objects, such as a policy basis made from
        // records already judged with them, judges no closing.
        if (objects is null || !checkpoints.Any(c => c.Closes is not null))
        {
            closingsKnown = true;
            return;
        }

        anchors = ComputeMigrationAnchors();
        anchorsKnown = true;
        var byId = checkpoints.ToDictionary(c => c.Record.Id, StringComparer.Ordinal);
        listings = new(StringComparer.Ordinal);
        foreach (CheckpointBody checkpoint in checkpoints)
        {
            foreach (InventoryEntry entry in HistoryAnalysis.Inventory(checkpoint, objects, null))
            {
                if (!listings.TryGetValue(entry.Id, out List<(string Checkpoint, string Hash)>? listed)) listings[entry.Id] = listed = [];
                listed.Add((checkpoint.Record.Id, entry.Hash));
            }
        }

        // The checkpoints each one follows, through hashes that match.
        Dictionary<string, List<CheckpointBody>> predecessors = checkpoints.ToDictionary(c => c.Record.Id, c => c.Previous
            .Where(p => byId.TryGetValue(p.Id, out CheckpointBody? previous) && previous.Record.ComputedHash == p.Hash)
            .Select(p => byId[p.Id]).Distinct().ToList(), StringComparer.Ordinal);
        ILookup<string, CheckpointBody> followers = checkpoints.SelectMany(c => predecessors[c.Record.Id].Select(p => (Previous: p.Record.Id, Next: c)))
            .ToLookup(x => x.Previous, x => x.Next, StringComparer.Ordinal);
        var roots = checkpoints.Where(c => c.Previous.Count == 0 && AuthenticWith(c.Record, NoClosings)).Select(c => c.Record.Id).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var findings = new List<Finding>();
            List<Closing> acting = Judge(findings);
            // Dropping a root leaves every checkpoint still reached with the same closings before it, so this ends.
            var kept = roots.Where(id => MayAnchor(byId[id].Record, acting)).ToHashSet(StringComparer.Ordinal);
            if (kept.Count < roots.Count)
            {
                roots = kept;
                continue;
            }

            closings = acting;
            closingFindings.AddRange(findings);
            break;
        }

        grantClosings = closings.Any(c => c.Kind == "grant");
        closingsKnown = true;
        // What was judged while closings were judged read none of them, or only those before one checkpoint: judge it afresh.
        deviceChains.Clear();
        proofs.Clear();
        verified.Clear();
        counts.Clear();
        ForgetAdmissions();
        ForgetStrictViews();
        policyOperators = null;
        policyOperatorsBeforeHistory = null;
        anchors = NoAnchors;
        anchorsKnown = false;

        // The closings that act when the roots given root the path, in checkpoint order.
        List<Closing> Judge(List<Finding> findings)
        {
            var waiting = checkpoints.ToDictionary(c => c.Record.Id, c => predecessors[c.Record.Id].Count, StringComparer.Ordinal);
            var ready = new SortedSet<string>(waiting.Where(w => w.Value == 0).Select(w => w.Key), StringComparer.Ordinal);
            var reached = new Dictionary<string, bool>(StringComparer.Ordinal);
            var before = new Dictionary<string, IReadOnlyList<Closing>>(StringComparer.Ordinal);
            var acting = new List<Closing>();
            var actingIds = new HashSet<string>(StringComparer.Ordinal);
            while (ready.Count > 0)
            {
                string id = ready.Min!;
                ready.Remove(id);
                CheckpointBody checkpoint = byId[id];
                IReadOnlyList<Closing> bound = Before(id);
                before[id] = bound;
                // Reached from a root, through checkpoints present with the hashes each names.
                bool path = checkpoint.Previous.Count == 0
                    ? roots.Contains(id)
                    : checkpoint.Previous.All(p => byId.TryGetValue(p.Id, out CheckpointBody? previous) && previous.Record.ComputedHash == p.Hash && reached[p.Id]);
                reached[id] = path;
                if (path && checkpoint.Closes is { } closes)
                {
                    if (!AuthenticWith(checkpoint.Record, bound))
                    {
                        // Its signature findings say why it does not verify; one that verifies but for a closing before it is said here.
                        if (bound.Count > 0 && AuthenticWith(checkpoint.Record, NoClosings))
                            findings.Add(new Finding(Severity.Error, "history.closing-author", checkpoint.Record.ExpectedPath + "#body.closes",
                                "This closing does not verify against the closings before it: one of them closed the device or the key that signed it, or refused that device's admission. It closes nothing, anchored or not, and still witnesses what it lists if it counts."));
                    }
                    else if (Close(checkpoint, closes, predecessors, bound, findings) is { } closing)
                    {
                        acting.Add(closing);
                        actingIds.Add(id);
                    }
                }

                foreach (CheckpointBody next in followers[id])
                {
                    if (--waiting[next.Record.Id] == 0) ready.Add(next.Record.Id);
                }
            }

            return acting;

            // The closings before a checkpoint: those before each checkpoint it follows, and each of those that acts, in checkpoint order.
            IReadOnlyList<Closing> Before(string id)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (CheckpointBody previous in predecessors[id])
                {
                    foreach (Closing closing in before[previous.Record.Id]) ids.Add(closing.Record.Id);
                    if (actingIds.Contains(previous.Record.Id)) ids.Add(previous.Record.Id);
                }

                return ids.Count == 0 ? NoClosings : [.. acting.Where(c => ids.Contains(c.Record.Id))];
            }
        }
    }

    /// <summary>The closing a checkpoint on the path makes, or null with the finding that says why it closes nothing.</summary>
    private Closing? Close(CheckpointBody checkpoint, (string Kind, string Id) closes, Dictionary<string, List<CheckpointBody>> predecessors, IReadOnlyList<Closing> bound,
        List<Finding> findings)
    {
        RecordEnvelope record = checkpoint.Record;
        string location = record.ExpectedPath + "#body.closes";
        string? expected = closes.Kind switch { "grant" => "authority", "device" or "key" => "device", _ => null };
        if (expected is null || !model.ById.TryGetValue(closes.Id, out RecordEnvelope? target) || target.Type != expected)
        {
            findings.Add(new Finding(Severity.Error, "history.checkpoint-invalid", location,
                $"This closing names {closes.Kind} {closes.Id}, which is not {(expected == "authority" ? "an authority" : "a device")} record here; it closes nothing."));
            return null;
        }

        string? identity = expected == "device" ? IdentityOf(target) : null;
        string? key = closes.Kind == "key" ? EstateModel.BodyString(target, "publicKey") : null;
        // The strongest acts read the signer's role or grant as it still stands: a later revision taking it away, or a closing before this
        // one closing that grant, withdraws the power. They read it against the closings before this one, which refuse what they do not
        // witness, and never against this closing itself. A recovery device signs for an operator only.
        bool recovery = record.Signatures.Any(s => Recovers(s.Device));
        string? problem = closes.Kind switch
        {
            "device" => StandingOperator(record.By, record.At, bound) || !recovery && StandingAdministrator(record, bound) ? null
                : "A device is closed only by an operator, a person holding administer or an operator's recovery device, the role or grant still standing.",
            "key" => key is not null && record.By == EstateModel.BodyString(target, "principal") &&
                record.Signatures.Any(s => model.ById.TryGetValue(s.Device, out RecordEnvelope? device) && device.Type == "device" && IdentityOf(device) == identity) ? null
                : "A key is closed only by its own device, for that device's principal.",
            _ => StandingOperator(record.By, record.At, bound) ? null : "A grant revision is closed only by an operator, who may revise it, the role still standing.",
        };
        if (problem is not null)
        {
            findings.Add(new Finding(Severity.Error, "history.closing-author", location,
                problem + " Only someone who may perform the act signs its closing; this one closes nothing, and it still witnesses what it lists."));
            return null;
        }

        var ancestors = new HashSet<string>(StringComparer.Ordinal) { record.Id };
        var queue = new Queue<CheckpointBody>([checkpoint]);
        while (queue.TryDequeue(out CheckpointBody? current))
        {
            foreach (CheckpointBody previous in predecessors[current.Record.Id])
            {
                if (ancestors.Add(previous.Record.Id)) queue.Enqueue(previous);
            }
        }

        // The rotation a key closing precedes: the lowest-id revision its device signed that replaces the closed key with another.
        string? rotation = key is null ? null : model.OfType("device")
            .Where(r => r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? previous) && previous.Type == "device" &&
                IdentityOf(previous) == identity && EstateModel.BodyString(previous, "publicKey") == key && EstateModel.BodyString(r, "publicKey") is { } replacing &&
                replacing != key && DeviceRevisionCounts(r, previous))
            .OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
        if (closes.Kind != "grant") return new Closing(record, closes.Kind, closes.Id, identity, key, null, rotation, ancestors);
        // A grant closing closes the revision it names and every revision of its line dated at or before it, so a record dated into an
        // earlier revision's time is bounded as one dated into the named revision's.
        string line = LineOf(target);
        List<RecordEnvelope> closed = [.. model.OfType("authority").Where(r => LineOf(r) == line && string.CompareOrdinal(r.At, target.At) <= 0)
            .Append(target).DistinctBy(r => r.Id).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return new Closing(record, closes.Kind, closes.Id, identity, key, target, rotation, ancestors, line, closed);
    }

    /// <summary>Whether a signed record verifies, in the mode with anchors, against the closings given.</summary>
    private bool AuthenticWith(RecordEnvelope record, IReadOnlyList<Closing> bound) =>
        record.Signatures.Count > 0 && record.DeclaredHash == record.ComputedHash && !VerifyWith(record, Mode.Anchors, bound).HasErrors;

    /// <summary>Whether a closing witnesses a record with these bytes: the closing or a checkpoint it follows lists it.</summary>
    private bool Witnesses(Closing closing, RecordEnvelope record) =>
        record.Id == closing.Record.Id || listings is not null && listings.TryGetValue(record.Id, out List<(string Checkpoint, string Hash)>? listed) &&
        listed.Any(l => l.Hash == record.ComputedHash && closing.Before.Contains(l.Checkpoint));

    /// <summary>
    /// The first of the closings given that refuses a signed record: one closing the device that signed it, or the key it was signed
    /// with, that does not witness it. The revocation of a closed device from its closing on, and the rotation a key closing precedes,
    /// are the acts their closings precede, and are not refused. In the mode with anchors an anchored record is not refused, except a
    /// device revision, which changes no key or name once its device or key is closed.
    /// </summary>
    private Closing? Refuser(RecordEnvelope record, IReadOnlyList<Closing> bound, Mode mode)
    {
        if (bound.Count == 0 || record.Signatures.Count == 0) return null;
        if (mode == Mode.Anchors && anchorsKnown && record.Type != "device" && anchors.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))) return null;
        foreach (Closing closing in bound)
        {
            if (Refuses(closing, record)) return closing;
        }

        return null;
    }

    /// <summary>
    /// Whether one closing refuses a signed record, anchors aside: it closes the device that signed it, or the key it was signed with, and
    /// does not witness it, the record being neither the revocation nor the rotation the closing precedes. The answer depends on no other
    /// closing.
    /// </summary>
    private bool Refuses(Closing closing, RecordEnvelope record)
    {
        if (closing.Kind == "grant" || Witnesses(closing, record)) return false;
        for (int i = 0; i < record.Signatures.Count; i++)
        {
            SignatureProof proof = Proof(record, i);
            if (proof.Identity != closing.Identity) continue;
            if (closing.Kind == "device" ? !RevokesFrom(record, closing) : proof.Key == closing.Key && record.Id != closing.Rotation) return true;
        }

        return false;
    }

    /// <summary>Whether a record revokes a closed device from its closing's time on: the act the closing precedes.</summary>
    private bool RevokesFrom(RecordEnvelope record, Closing closing) =>
        record.Type == "admission" && EstateModel.BodyString(record, "status") == "revoked" && record.Subject?.Id is { } subject &&
        model.ById.TryGetValue(subject, out RecordEnvelope? device) && device.Type == "device" && IdentityOf(device) == closing.Identity &&
        EstateModel.BodyString(record, "revokedAt") is { } revokedAt && string.CompareOrdinal(revokedAt, closing.Record.At) >= 0;

    /// <summary>
    /// Whether a closing that acts refuses a device revision: its device is closed, or it is signed with a closed key and is not the
    /// rotation replacing it, and the closing does not witness it. Read without proofs, since proofs read the device's line. Once the
    /// closings that act are known nothing it reads changes, so each answer is kept.
    /// <para>
    /// Whether a signature was made with a closed key is whether the closed key is among the keys the signature verifies under, which are
    /// recovered once per signature and confirmed by the platform's verification (<see cref="RevisionSignatureKeys"/>), not found by
    /// verifying the signature under each closed key in turn (FIX-121): with a closing before each of many rotations of one device, that
    /// was quadratic in its rotations. Where the recovered keys are not certain to be all of them, and for a closed key whose point the
    /// arithmetic does not take, the signature is verified under the closed key as before (<see cref="VerifiesWithClosedKey"/>), so every
    /// answer is the one verifying under each closed key gives (<see cref="RevisionRefusedByVerifying"/>). What remains for each pair of a
    /// revision and a closing of its device is comparing identities, a witness lookup and at most eight 64-byte comparisons.
    /// </para>
    /// </summary>
    private bool RevisionRefused(RecordEnvelope revision, RecordEnvelope previous)
    {
        if (!closingsKnown || closings.Count == 0) return false;
        lock (gate)
        {
            (string, string) key = (revision.Id, previous.Id);
            if (revisionRefusals.TryGetValue(key, out bool known)) return known;
            bool value = Judge();
            if (model.ById.TryGetValue(revision.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, revision) &&
                model.ById.TryGetValue(previous.Id, out RecordEnvelope? below) && ReferenceEquals(below, previous))
                revisionRefusals[key] = value;
            return value;
        }

        bool Judge()
        {
            string identity = IdentityOf(previous);
            byte[]? digest = null;
            byte[][]?[]? keys = null;
            foreach (Closing closing in closings)
            {
                if (closing.Identity != identity || Witnesses(closing, revision)) continue;
                if (closing.Kind == "device") return true;
                if (closing.Kind == "key" && revision.Id != closing.Rotation)
                {
                    digest ??= RecordSigning.Digest(revision.ComputedHash);
                    keys ??= RevisionSignatureKeys(revision, previous, digest);
                    if (SignedWithClosedKey(closing.Key!, revision, digest, keys)) return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The key-closing check as it was before FIX-121, kept as the oracle the tests compare <see cref="RevisionRefused"/> with: whether a
    /// closing that acts refuses a device revision, each signature verified under every closed key it is checked against with
    /// <see cref="RecordSigning.Verify"/>. Nothing is kept.
    /// </summary>
    internal bool RevisionRefusedByVerifying(RecordEnvelope revision, RecordEnvelope previous)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(previous);
        if (!closingsKnown || closings.Count == 0) return false;
        lock (gate)
        {
            string identity = IdentityOf(previous);
            byte[]? digest = null;
            foreach (Closing closing in closings)
            {
                if (closing.Identity != identity || Witnesses(closing, revision)) continue;
                if (closing.Kind == "device") return true;
                if (closing.Kind == "key" && revision.Id != closing.Rotation)
                {
                    digest ??= RecordSigning.Digest(revision.ComputedHash);
                    if (revision.Signatures.Any(s => RecordSigning.Verify(closing.Key!, digest, s.Signature))) return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// For each signature of a device revision, every key it verifies under, recovered once and confirmed by the platform; null for a
    /// signature whose recovered keys are not certain to be all of them (<see cref="RecoveredKeys.Confirmed"/>). The keys known to verify
    /// a signature are those of the device's previous key and the revision's own that the platform verifies it with: a revision its
    /// device signed carries no other.
    /// </summary>
    private byte[][]?[] RevisionSignatureKeys(RecordEnvelope revision, RecordEnvelope previous, byte[] digest)
    {
        lock (gate)
        {
            string hash = revision.ComputedHash;
            List<string> keys = [.. new[] { EstateModel.BodyString(previous, "publicKey"), EstateModel.BodyString(revision, "publicKey") }.OfType<string>().Distinct(StringComparer.Ordinal)];
            var result = new byte[][]?[revision.Signatures.Count];
            for (int i = 0; i < result.Length; i++)
            {
                string signature = revision.Signatures[i].Signature;
                if (!revisionSignatureKeys.TryGetValue((hash, previous.Id, signature), out byte[][]? recovered))
                {
                    List<string> signing = [.. keys.Where(k => VerifiesWith(k, hash, digest, signature))];
                    revisionSignatureKeys[(hash, previous.Id, signature)] = recovered = recoveredKeys.Confirmed(digest, signature, signing);
                }

                result[i] = recovered;
            }

            return result;
        }
    }

    /// <summary>
    /// Whether one of a revision's signatures was made with a closed key: the closed key's point is among the keys a signature verifies
    /// under, or, where those keys are not certain or the closed key's point is not one the arithmetic takes, the signature verifies under
    /// the closed key as before.
    /// </summary>
    private bool SignedWithClosedKey(string key, RecordEnvelope revision, byte[] digest, byte[][]?[] keys)
    {
        lock (gate)
        {
            byte[]? point = recoveredKeys.Point(key);
            for (int i = 0; i < keys.Length; i++)
            {
                bool verifies = point is null || keys[i] is not { } recovered
                    ? VerifiesWithClosedKey(key, revision.ComputedHash, digest, revision.Signatures[i].Signature)
                    : RecoveredKeys.Among(recovered, point);
                if (verifies) return true;
            }

            return false;
        }
    }

    /// <summary>How many signatures the key-closing check recovered keys from: once each, however many closings read it.</summary>
    internal long KeyRecoveries => recoveredKeys.Recoveries;

    /// <summary>How many recovered keys were not points on the curve or the platform did not confirm: each is dropped, and its signature verified as before.</summary>
    internal long DroppedKeys => recoveredKeys.Dropped;

    /// <summary>How many signatures the key-closing check verified under each closed key as before, their recovered keys not certain.</summary>
    internal long RecoveryFallbacks => recoveredKeys.Fallbacks;

    /// <summary>How many times the key-closing check verified a signature under a closed key, rather than reading recovered keys.</summary>
    internal long ClosedKeyVerifications
    {
        get
        {
            lock (gate)
            {
                return closedKeyVerifications;
            }
        }
    }

    /// <summary>
    /// Whether a signature over a content hash verifies with a closed key, as <see cref="RecordSigning.Verify"/> answers it: what the
    /// key-closing check reads where recovered keys cannot answer. The answer is kept with every other verdict on a key, and while the
    /// estate is judged each closed key is imported once.
    /// </summary>
    private bool VerifiesWithClosedKey(string key, string hash, byte[] digest, string signature)
    {
        lock (gate)
        {
            if (keyVerdicts.TryGetValue((key, hash, signature), out bool known)) return known;
            closedKeyVerifications++;
            bool value;
            if (closedKeys is null) value = RecordSigning.Verify(key, digest, signature);
            else
            {
                if (!closedKeys.TryGetValue(key, out RecordSigning.ImportedKey? imported)) closedKeys[key] = imported = RecordSigning.ImportedKey.Import(key);
                value = imported.Verify(digest, signature);
            }

            keyVerdicts[(key, hash, signature)] = value;
            return value;
        }
    }

    /// <summary>Releases the closed keys imported while the estate was judged; a later verdict imports its key for that verdict alone.</summary>
    private void ReleaseClosedKeys()
    {
        lock (gate)
        {
            if (closedKeys is null) return;
            foreach (RecordSigning.ImportedKey imported in closedKeys.Values) imported.Dispose();
            closedKeys = null;
        }
    }

    /// <summary>
    /// The closing that refuses a record relying on a grant revision it closed: a record by a closed revision's grantee, dated while that
    /// revision was in force, that the closing does not witness and no anchor lists. Since a closing closes every revision of the line
    /// up to the one it names, the window runs from the line's first revision to the first revision after them. The rule fails closed: it
    /// refuses every such record by the grantee, not only those that needed the grant. Admissions, devices, grants and checkpoints are
    /// judged by their own rules and rely on no grant here.
    /// </summary>
    internal Closing? GrantRefuser(RecordEnvelope record, bool anchorsCount = true)
    {
        if (!closingsKnown || !grantClosings || record.Type is "admission" or "authority" or "history-checkpoint" or "device") return null;
        if (anchorsCount && anchorsKnown && anchors.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))) return null;
        foreach (Closing closing in closings)
        {
            if (closing.Closed is not { } closed || Witnesses(closing, record)) continue;
            // In force at the record's time: dated at or before it, unexpired then, and no revision that counts replaces it by then.
            if (closed.Any(grant => EstateModel.BodyString(grant, "grantee") == record.By && string.CompareOrdinal(grant.At, record.At) <= 0 &&
                    !EstateModel.Expired(grant, record.AtTime) &&
                    !AttachedSuccessors(grant).Any(s => string.CompareOrdinal(s.At, record.At) <= 0 && Counts(s))))
                return closing;
        }

        return null;
    }

    /// <summary>
    /// Whether one of the closings given closed a grant revision without witnessing a record, which the revision then does not authorize; in
    /// the mode with anchors an anchored record is not closed. What an admission's author is read against while admissions are judged.
    /// </summary>
    private bool GrantClosedWithin(RecordEnvelope grant, RecordEnvelope record, IReadOnlyList<Closing> bound, Mode mode)
    {
        if (bound.Count == 0) return false;
        if (mode == Mode.Anchors && anchorsKnown && anchors.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))) return false;
        return bound.Any(c => c.Closes(grant) && !Witnesses(c, record));
    }

    /// <summary>Whether a closing that acts closed a grant revision without witnessing a record, which the revision then does not authorize.</summary>
    internal bool GrantClosedFor(RecordEnvelope grant, RecordEnvelope record, bool anchorsCount)
    {
        if (!closingsKnown || !grantClosings) return false;
        if (anchorsCount && anchorsKnown && anchors.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))) return false;
        return closings.Any(c => c.Closes(grant) && !Witnesses(c, record));
    }

    /// <summary>
    /// Whether a closing that acts refuses a record, anchors aside: what an anchoring checkpoint may anchor after reading it. A device
    /// revision a closing refuses is not among them, since no anchor restores it.
    /// </summary>
    internal bool RefusedByClosing(RecordEnvelope record) =>
        !(record.Type == "device" && record.Supersedes is not null) &&
        (Refuser(record, Acting, Mode.Signatures) is not null || GrantRefuser(record, anchorsCount: false) is not null);

    /// <summary>Whether a record is a device revision that a closing refuses, which changes no key or name, anchored or not.</summary>
    internal bool DeviceRevisionRefused(RecordEnvelope record) =>
        record.Type == "device" && record.Supersedes is not null && model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) && previous.Type == "device" &&
        RevisionRefused(record, previous);

    /// <summary>Whether a record is a closing checkpoint naming a device: the one checkpoint a recovery device signs.</summary>
    private static bool ClosesDevice(RecordEnvelope record) =>
        record.Type == "history-checkpoint" && CheckpointBody.ReadCloses(record.Body) is { Kind: "device" };

    /// <summary>The finding for a record a closing refuses.</summary>
    private static Finding AfterClosing(RecordEnvelope record, Closing closing)
    {
        string what = closing.Kind == "device"
            ? $"Signed by device {closing.Identity}, which history checkpoint {closing.Record.Id} closed"
            : $"Signed with the key of device revision {closing.Target}, which history checkpoint {closing.Record.Id} closed";
        string after = record.Type == "device"
            ? " A revision of a closed device, or one signed with a closed key, changes no key or name."
            : record.Type == "admission"
                ? " It neither admits nor revokes, and counts for nothing until the root operator or a person holding administer anchors it after reading it."
                : " It counts for nothing until the root operator or a person holding administer anchors it after reading it.";
        return new Finding(Severity.Error, "history.after-closing", record.ExpectedPath + "#signatures",
            what + ", and that closing does not witness it: from a closing on, only what the closing or a checkpoint it follows lists counts, whatever date a record states." + after);
    }

    /// <summary>The finding for a record relying on a grant revision a closing closed.</summary>
    internal static Finding AfterGrantClosing(RecordEnvelope record, Closing closing) =>
        new(Severity.Error, "history.after-closing", record.ExpectedPath,
            $"Written by {record.By} while grant revision {closing.Target} was in force, and history checkpoint {closing.Record.Id} closed that revision without witnessing it: a narrowed grant accounts only for what its closing or a checkpoint it follows lists, whatever date a record states. It counts for nothing until the root operator or a person holding administer anchors it after reading it.");

    /// <summary>
    /// A warning for each closing that acts while no act follows it: no revocation of its device that counts, no rotation replacing its
    /// key and no revocation or device closing of that key's device, no revision of its grant. A closing closes all the same.
    /// </summary>
    private IEnumerable<Finding> Unfollowed()
    {
        // A closing that counts as a checkpoint yet acts not, with no finding of its own, verifies only through an anchoring
        // checkpoint's anchors, which closings are not judged with: say so rather than leave it silent.
        foreach (RecordEnvelope checkpoint in model.OfType("history-checkpoint").Where(r => History.CountingCheckpoints.Contains(r.Id) && CheckpointBody.ReadCloses(r.Body) is not null))
        {
            string location = checkpoint.ExpectedPath + "#body.closes";
            if (closings.Any(c => c.Record.Id == checkpoint.Id) || closingFindings.Any(f => f.Location == location)) continue;
            yield return new Finding(Severity.Error, "history.closing-author", location,
                "This closing counts as a checkpoint but closes nothing: closings are judged with the format-2 migration's anchors alone, and a device that signed it is admitted only through an anchoring checkpoint. Write the closing from a device admitted by a signed admission.");
        }


        foreach (Closing closing in closings)
        {
            if (Followed(closing)) continue;
            string act = closing.Kind switch { "device" => "revocation of the device", "key" => "rotation to another key, revocation of its device or closing of its device", _ => "revision of the grant" };
            yield return new Finding(Severity.Warning, "history.closing-unfollowed", closing.Record.ExpectedPath + "#body.closes",
                string.Create(CultureInfo.InvariantCulture, $"This checkpoint closes {closing.Kind} {closing.Target} as of {closing.Record.At}, and no {act} follows it yet. It closes all the same: from it on, only what it witnesses counts for that {closing.Kind}."));
        }
    }

    /// <summary>
    /// Whether the act a closing that acts precedes follows it: a revocation of its device that counts, the rotation replacing its key
    /// that counts, or a revision of its grant that counts, superseding it directly or through revisions that do not count
    /// (<see cref="AttachedSuccessors"/>). A key closing is followed as well when its device is revoked instead, by a
    /// revocation that counts, or closed by a device closing that acts: the device is retired with its key, and no rotation of that key
    /// can be written any more.
    /// </summary>
    internal bool Followed(Closing closing)
    {
        ArgumentNullException.ThrowIfNull(closing);
        return closing.Kind switch
        {
            "device" => Revoked(closing.Target),
            "key" => closing.Rotation is { } rotation && model.ById.TryGetValue(rotation, out RecordEnvelope? replacing) && Counts(replacing) ||
                Revoked(closing.Target) || closings.Any(c => c.Kind == "device" && c.Identity == closing.Identity),
            _ => closing.Grant is { } grant && AttachedSuccessors(grant).Any(Counts),
        };
    }

    /// <summary>Whether a revocation that counts names a record of a device's line.</summary>
    private bool Revoked(string device) =>
        AdmissionsAbout(device).Any(a => EstateModel.BodyString(a, "status") == "revoked" && AdmissionCounts(a, Mode.Anchors));

    /// <summary>
    /// A warning for each act a closing precedes that counts in an estate that has adopted, that its adoption does not witness, and
    /// whose target no closing that acts names (OPS-07): a revocation, a key rotation or a narrowing grant revision merged from a copy
    /// that had not adopted yet, or added as a file, takes effect, and what the device, the key or the grantee signs with an earlier
    /// date still counts. A closing written after the act names its target too, and from it on bounds what that target accounts for.
    /// An act the adoption witnesses keeps the reading it had then, and an estate that has not adopted warns of none.
    /// </summary>
    private IEnumerable<Finding> Unclosed()
    {
        if (!History.Adopted) yield break;
        foreach (RecordEnvelope act in ClosingActs.Types.SelectMany(model.OfType).Where(r => r.Supersedes is not null).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (History.AdoptedAt(act) is not null || ClosingActs.For(model, this, act.Type, act.Body, act.Subject, act.Supersedes) is not { } closes || !Counts(act) || Named(closes)) continue;
            string message = closes.Kind switch
            {
                "device" => $"This revocation of device {closes.Id} counts, and no closing names the device, so a record the device signs with an earlier date still counts.",
                "key" => $"This rotation replaces the key of device revision {closes.Id}, and no closing names that key, so a record signed with it with an earlier date still counts.",
                _ => $"This revision narrows grant revision {closes.Id}, and no closing names the grant, so a record its grantee writes with a date while that revision was in force still counts.",
            };
            yield return new Finding(Severity.Warning, "history.closing-missing", act.ExpectedPath,
                $"{message} Write the closing with vericairn records new history-checkpoint, its closes naming {closes.Kind} {closes.Id}, signed by one who may perform the act.");
        }
    }

    /// <summary>
    /// Whether a closing that acts names what an act closes: a device closing of its device's line; for a key, a closing of that key of
    /// the device or of the device itself; for a grant, a closing that closes the revision.
    /// </summary>
    private bool Named((string Kind, string Id) closes)
    {
        if (!model.ById.TryGetValue(closes.Id, out RecordEnvelope? target)) return false;
        if (closes.Kind == "grant") return closings.Any(c => c.Closes(target));
        string identity = IdentityOf(target);
        string? key = closes.Kind == "key" ? EstateModel.BodyString(target, "publicKey") : null;
        return closings.Any(c => c.Identity == identity && (c.Kind == "device" || key is not null && c.Kind == "key" && c.Key == key));
    }
}
