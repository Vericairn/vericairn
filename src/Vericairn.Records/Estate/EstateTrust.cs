// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// What an estate's signatures, anchors and history checkpoints establish, judged once from every record the loader read and before
/// any rule reads them: which signatures verify, which admissions and device revisions count, the charter, the history graph and
/// which records count at all (design/signed-history.md §§ 5 to 8). A record that does not count is kept as evidence and reported,
/// and the model's indexes leave it out, so no rule that admits, authorizes, accepts, clears or holds reads it.
/// </summary>
/// <remarks>
/// Everything here reads the records as loaded, never the model's counting indexes, so the judgment cannot depend on itself. The root
/// operator is read from the first charter, and admissions count outward from the first authority, the root operator's self-signed
/// admission that witnessed history or the manifest names, never the earliest by date. Signatures
/// are judged in two modes: without anchors, where an admission counts only when signed by a device whose own admission rests on
/// signatures, which is the mode anchors and acknowledged removals are judged in, so that nothing they anchor can be what makes them count;
/// and with anchors, where an unsigned admission also counts when the format-2 migration or an anchoring checkpoint lists it. Closings are
/// judged before anything else, in checkpoint order, and every later judgment reads the closings that act (EstateTrust.Closings.cs).
/// </remarks>
internal sealed partial class EstateTrust
{
    internal enum Mode
    {
        /// <summary>Admissions count only when signed, by devices whose own admissions rest on signatures.</summary>
        Signatures,

        /// <summary>Unsigned admissions also count when anchored.</summary>
        Anchors,
    }

    private static readonly IReadOnlySet<string> NoAnchors = new HashSet<string>(StringComparer.Ordinal);
    private static readonly string[] TrustTypes = ["admission", "device", "authority", "principal"];

    private readonly object gate = new();
    private readonly EstateModel model;
    private readonly ILookup<string, RecordEnvelope> admissionsBySubject;
    private readonly ILookup<string, RecordEnvelope> successors;
    private readonly Dictionary<string, IReadOnlyList<RecordEnvelope>> deviceChains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> deviceRevisions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Record, int Index), SignatureProof> proofs = [];
    private readonly Dictionary<(string Key, string Hash, string Signature), bool> keyVerdicts = [];
    private readonly IReadOnlyDictionary<string, string> rawSupersededBy;
    private readonly HashSet<string>?[] admittedActing = new HashSet<string>?[2];
    private readonly Dictionary<string, HashSet<string>>[] admittedByClosings = [new(StringComparer.Ordinal), new(StringComparer.Ordinal)];
    private readonly Dictionary<HashSet<string>, HashSet<string>> unseating = new(ReferenceEqualityComparer.Instance);
    private bool anchorsKnown;
    private readonly Dictionary<(string Id, Mode Mode), FindingList> verified = [];
    private readonly Dictionary<string, bool> counts = new(StringComparer.Ordinal);
    private readonly List<RecordEnvelope> admissionRecords;
    private readonly List<RecordEnvelope> principalRecords;
    private readonly List<RecordEnvelope> authorityRecords;
    private readonly Dictionary<string, RecordEnvelope?> attached = new(StringComparer.Ordinal);
    private readonly Dictionary<IReadOnlyList<Closing>, StrictView> strictViews = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HashSet<string>, StrictView> strictViewsByChange = new(HashSet<string>.CreateSetComparer());
    private readonly Dictionary<(string Revision, string Refused), bool> strictVerdicts = [];
    private readonly Dictionary<string, int[]> strictRefusals = new(StringComparer.Ordinal);
    private StrictIndex? strictIndex;
    private StrictView? strictNone;
    private List<RecordEnvelope>? principalsInOrder;
    private AuthorityReplay? loadedAuthority;
    private OperatorReplay? policyOperators;
    private OperatorReplay? policyOperatorsBeforeHistory;
    private readonly Dictionary<string, HashSet<string>> checkpointAncestry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> checkpointDescendants = new(StringComparer.Ordinal);
    private ILookup<string, string>? checkpointFollowers;
    private IReadOnlySet<string> anchors = NoAnchors;

    private EstateTrust(EstateModel model, EstateManifest? manifest, Func<string, byte[]?>? objects, IReadOnlySet<string>? files, RecordEnvelope? firstCharter,
        bool unsettled = false, bool authorityGiven = false, RecordEnvelope? authority = null)
    {
        this.model = model;
        Manifest = manifest;
        Unsettled = unsettled;
        // Read through the type lookups, which hold every record until the loader keeps only those that count, so that judging a model
        // walks only the records trust rests on.
        admissionRecords = [.. model.OfType("admission")];
        admissionsBySubject = admissionRecords.Where(r => r.Subject is not null).ToLookup(r => r.Subject!.Id, StringComparer.Ordinal);
        successors = TrustTypes.SelectMany(model.OfType).Where(r => r.Supersedes is not null).ToLookup(r => r.Supersedes!, StringComparer.Ordinal);
        rawSupersededBy = model.SupersededBy;
        principalRecords = [.. model.OfType("principal")];
        authorityRecords = [.. model.OfType("authority")];
        FirstEstate = firstCharter;
        RootOperator = FirstEstate is { } first ? EstateModel.BodyString(first, "operator") : null;
        Format = manifest?.RecordFormat ?? (model.Records.Count == 0 ? RecordFormats.First : model.Records.Max(r => r.Format));
        AuthorityCandidates = [.. admissionRecords.Where(r => r.By == RootOperator && RootOperator is not null && SelfSigned(r) && !Recovers(r.Subject?.Id))
            .OrderBy(a => a.Id, StringComparer.Ordinal)];
        FirstAuthority = authorityGiven ? authority : FirstAuthorityByManifest();

        // Closings come first: each is judged against the closings before it in checkpoint order, with the format-2 migration's anchors
        // as they stand before any closing, and then every judgment is made afresh with the closings that act (design/signed-history.md § 8).
        JudgeClosings(objects);

        // Anchors are judged by signatures alone, so that nothing they anchor can be what makes them count.
        MigrationAnchors = ComputeMigrationAnchors();
        HistoryAnalysis analysis = HistoryAnalysis.Anchoring(this, model);
        var all = new HashSet<string>(MigrationAnchors, StringComparer.Ordinal);
        all.UnionWith(analysis.AnchorKeys);
        anchors = all;
        anchorsKnown = true;
        Charter = ComputeCharter();
        Required = Charter is not null && RequiresSignatures(Charter);
        History = analysis.Complete(objects, files);
        closingFindings.AddRange(Unfollowed());
        closingFindings.AddRange(Unclosed());
        closingFindings.AddRange(RevisionsReadAsRemovals());
        SettleStrictViews();
        ReleaseClosedKeys();
    }

    /// <summary>The manifest this judgment read the record format from.</summary>
    internal EstateManifest? Manifest { get; }

    /// <summary>The estate's record format: its manifest's, or the newest its records state.</summary>
    internal long Format { get; }

    /// <summary>The first charter, which every later revision supersedes.</summary>
    internal RecordEnvelope? FirstEstate { get; }

    /// <summary>The root operator the first charter names.</summary>
    internal string? RootOperator { get; }

    /// <summary>
    /// The estate's first authority: the root operator's admission of its own device, signed by that device, from which every admission
    /// counts. Never chosen by a stated date or id (<see cref="Build"/>, <see cref="FirstAuthorityByManifest"/>).
    /// </summary>
    internal RecordEnvelope? FirstAuthority { get; }

    /// <summary>Every admission that could be the first authority: by the root operator, of a device that signed it, not a recovery device; by id.</summary>
    internal IReadOnlyList<RecordEnvelope> AuthorityCandidates { get; }

    /// <summary>Unsigned admissions the signed format-2 migration lists, as id and hash keys.</summary>
    internal IReadOnlySet<string> MigrationAnchors { get; }

    /// <summary>Every anchor: the migration's and those of counting anchoring checkpoints.</summary>
    internal IReadOnlySet<string> Anchors => anchors;

    /// <summary>The current charter: the latest revision along the first charter's line that counts.</summary>
    internal RecordEnvelope? Charter { get; }

    /// <summary>Whether the current charter requires signatures.</summary>
    internal bool Required { get; }

    /// <summary>The history graph and what it witnesses.</summary>
    internal HistoryState History { get; }

    /// <summary>Whether the estate has adopted history checkpoints: it holds a checkpoint that counts.</summary>
    internal bool Adopted => History.Adopted;

    /// <summary>The errors about which charter is the estate's first, reported with the history findings.</summary>
    internal List<Finding> RootFindings { get; } = [];

    /// <summary>Whether witnessed history names more than one first charter or first authority, so that no record counts until the copy is settled.</summary>
    internal bool Unsettled { get; }

    /// <summary>
    /// Judges a model's records. The first charter, which names the root operator, is the one the manifest names; another charter that
    /// supersedes nothing never displaces it, and only a model read without a manifest falls back to the first such charter by id. Once
    /// the estate has adopted checkpoints, witnessed history decides instead: the first charter is the one its counting checkpoints
    /// witnessed first, judged under that charter's root operator. A manifest that names another charter is then an error and changes
    /// nothing, and a charter that first appears after adoption never becomes the first. When more than one charter is so witnessed,
    /// each under its own root operator, no charter is read as the first and no record counts until the copy is settled, so that nothing
    /// built from the records that count can take either root. Under each charter the first authority is chosen the same way
    /// (<see cref="UnderCharter"/>).
    /// </summary>
    internal static EstateTrust Build(EstateModel model, EstateManifest? manifest, Func<string, byte[]?>? objects = null, IReadOnlySet<string>? files = null)
    {
        RecordEnvelope? named = manifest is null ? model.RawFirstEstate() : NamedCharter(model, manifest.Estate);
        EstateTrust chosen = UnderCharter(model, manifest, objects, files, named);
        List<RecordEnvelope> charters = [.. model.OfType("estate").Where(r => r.Supersedes is null).OrderBy(r => r.Id, StringComparer.Ordinal)];
        if (objects is null || manifest is null || charters.Count == 0) return chosen;
        if (charters.Count == 1 && ReferenceEquals(charters[0], named)) return chosen;

        List<EstateTrust> witnessed = [];
        foreach (RecordEnvelope charter in charters)
        {
            EstateTrust trust = ReferenceEquals(charter, named) ? chosen : UnderCharter(model, manifest, objects, files, charter);
            if (trust.WitnessedFirst(trust.FirstEstate, charters)) witnessed.Add(trust);
        }

        if (witnessed.Count == 0) return chosen;
        if (witnessed.Count > 1)
        {
            var unsettled = new EstateTrust(model, manifest, objects, files, null, unsettled: true);
            unsettled.RootFindings.Add(new Finding(Severity.Error, "history.root-ambiguous", EstateLayout.RecordsDirectory,
                $"History checkpoints that count, each set under its own root operator, witness different first charters: {string.Join(", ", witnessed.Select(t => t.FirstEstate!.Id))}. No root operator is read and no record counts until one set is removed; compare this copy with a head known elsewhere (estate check --expect) to tell which history belongs."));
            return unsettled;
        }

        EstateTrust root = witnessed[0];
        if (!ReferenceEquals(root.FirstEstate, named))
        {
            root.RootFindings.Add(new Finding(Severity.Error, "history.manifest-root", EstateLayout.ManifestPath,
                $"The manifest names {manifest.Estate ?? "no charter"} as the estate's first charter, and its history checkpoints witnessed charter {root.FirstEstate!.Id} first. Witnessed history decides, so the root operator is the one charter {root.FirstEstate.Id} names; restore the manifest's estate to {root.FirstEstate.Id}."));
        }

        return root;
    }

    /// <summary>
    /// The judgment under a first charter, with its first authority chosen. Once the estate has adopted checkpoints, the first authority is
    /// the root operator's self-admission that its counting checkpoints witnessed first, judged under that admission as the first authority,
    /// as the first charter is (<see cref="Build"/>): the manifest naming another is an error and changes nothing, and two self-admissions
    /// each witnessed first under itself leave the copy unsettled, so that no record counts. When none is so witnessed, or the model has no
    /// objects or no manifest to judge history with, the manifest decides (<see cref="FirstAuthorityByManifest"/>). Nothing extra is judged
    /// when the estate holds one such admission and the manifest names it or none.
    /// </summary>
    private static EstateTrust UnderCharter(EstateModel model, EstateManifest? manifest, Func<string, byte[]?>? objects, IReadOnlySet<string>? files, RecordEnvelope? charter)
    {
        var byManifest = new EstateTrust(model, manifest, objects, files, charter);
        IReadOnlyList<RecordEnvelope> candidates = byManifest.AuthorityCandidates;
        if (objects is null || manifest is null || candidates.Count == 0) return byManifest;
        if (candidates.Count == 1 && (manifest.FirstAuthority is null || manifest.FirstAuthority == candidates[0].Id)) return byManifest;

        List<EstateTrust> witnessed = [];
        foreach (RecordEnvelope candidate in candidates)
        {
            EstateTrust trust = ReferenceEquals(candidate, byManifest.FirstAuthority) ? byManifest
                : new EstateTrust(model, manifest, objects, files, charter, authorityGiven: true, authority: candidate);
            if (trust.WitnessedFirst(trust.FirstAuthority, candidates)) witnessed.Add(trust);
        }

        if (witnessed.Count == 0) return byManifest;
        if (witnessed.Count > 1)
        {
            var unsettled = new EstateTrust(model, manifest, objects, files, charter, unsettled: true, authorityGiven: true);
            unsettled.RootFindings.Add(new Finding(Severity.Error, "history.root-ambiguous", EstateLayout.RecordsDirectory,
                $"History checkpoints that count, each set under its own first authority, witness different admissions of the root operator's own devices first: {string.Join(", ", witnessed.Select(t => t.FirstAuthority!.Id))}. No record counts until one set is removed; compare this copy with a head known elsewhere (estate check --expect) to tell which history belongs."));
            return unsettled;
        }

        EstateTrust root = witnessed[0];
        if (manifest.FirstAuthority is { } named && named != root.FirstAuthority!.Id)
        {
            root.RootFindings.Add(new Finding(Severity.Error, "history.manifest-root", EstateLayout.ManifestPath,
                $"The manifest names {named} as the estate's first authority, and its history checkpoints witnessed admission {root.FirstAuthority.Id} first. Witnessed history decides, so admissions count from {root.FirstAuthority.Id}; restore the manifest's firstAuthority to {root.FirstAuthority.Id}."));
        }

        return root;
    }

    /// <summary>
    /// The first authority read without history: the self-admission of the root operator's device that the manifest names, which no other
    /// displaces; or, when the manifest names none, the only one. A manifest naming an admission that is not such a self-admission, or
    /// naming none while there are several, is an error and there is no first authority, so no signed admission counts: a first authority
    /// is never chosen by its stated date or id.
    /// </summary>
    private RecordEnvelope? FirstAuthorityByManifest()
    {
        string candidates = AuthorityCandidates.Count == 0 ? "the estate holds none" : "the estate holds " + string.Join(", ", AuthorityCandidates.Select(c => c.Id));
        if (Manifest?.FirstAuthority is { } named)
        {
            RecordEnvelope? match = AuthorityCandidates.FirstOrDefault(c => c.Id == named);
            if (match is null)
            {
                RootFindings.Add(new Finding(Severity.Error, "manifest.first-authority", EstateLayout.ManifestPath,
                    $"The manifest names {named} as the estate's first authority, and that is not an admission by the root operator of its own device, signed by that device ({candidates}). No admission counts from a first authority until the manifest's firstAuthority names one that is."));
            }

            return match;
        }

        if (AuthorityCandidates.Count <= 1) return AuthorityCandidates.Count == 0 ? null : AuthorityCandidates[0];
        RootFindings.Add(new Finding(Severity.Error, "manifest.first-authority", EstateLayout.ManifestPath,
            $"The root operator's devices admitted themselves more than once ({string.Join(", ", AuthorityCandidates.Select(c => c.Id))}), and the manifest names none of them as the estate's first authority. A first authority is never chosen by its date or id, so no admission counts from one until the manifest's firstAuthority names the admission that is."));
        return null;
    }

    /// <summary>
    /// Whether this judgment's estate has adopted checkpoints and its counting checkpoints witnessed a record, with the bytes present,
    /// before any of its rivals: the charters that supersede nothing, or the self-admissions that could be the first authority.
    /// </summary>
    private bool WitnessedFirst(RecordEnvelope? record, IReadOnlyList<RecordEnvelope> rivals)
    {
        if (!Adopted || record is null) return false;
        List<(string Id, string Checkpoint)> seen = [.. rivals
            .Where(c => History.Entries.TryGetValue(c.Id, out WitnessedEntry? entry) && entry.Entry.Hash == c.ComputedHash)
            .Select(c => (c.Id, History.Entries[c.Id].Checkpoint))];
        int index = seen.FindIndex(s => s.Id == record.Id);
        if (index < 0) return false;
        string earliest = seen[index].Checkpoint;
        return seen.All(s => s.Id == record.Id || string.CompareOrdinal(s.Checkpoint, earliest) > 0);
    }

    /// <summary>The records that do not count, each with the reason the loader reports it under.</summary>
    internal IReadOnlySet<string> NotCounting()
    {
        var not = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in model.Records)
        {
            if (!Counts(record)) not.Add(record.Id);
        }

        return not;
    }

    /// <summary>
    /// Whether a record counts. No record counts in an unsettled copy, and a witnessed record present with another hash does not; otherwise
    /// each kind follows its rule: admissions <see cref="AdmissionCounts(RecordEnvelope, Mode)"/>, device revisions <see cref="DeviceRevisionCounts"/>,
    /// checkpoints the history graph, charter revisions
    /// <see cref="CharterRevisionCounts"/>, other signed records <see cref="Authentic"/> and unsigned records <see cref="Unsigned"/>.
    /// </summary>
    internal bool Counts(RecordEnvelope record)
    {
        lock (gate)
        {
            if (counts.TryGetValue(record.Id, out bool known) && model.ById.TryGetValue(record.Id, out RecordEnvelope? same) && ReferenceEquals(same, record)) return known;
            bool value = Judge(record);
            if (model.ById.TryGetValue(record.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, record)) counts[record.Id] = value;
            return value;
        }

        bool Judge(RecordEnvelope r)
        {
            if (Unsettled || History.Altered.Contains(r.Id)) return false;
            // A grant or principal revision dated before the record below it in its line counts for nothing.
            if (RefusedAuthorityRevision(r)) return false;
            if (r.Type == "admission") return AdmissionCounts(r, Mode.Anchors);
            if (r.Type == "history-checkpoint") return History.CountingCheckpoints.Contains(r.Id);
            // A revision of a required charter that the root operator did not sign changes nothing (FIX-79), so it does not count.
            if (r.Type == "estate" && r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? revised) && revised.Type == "estate" &&
                !CharterRevisionCounts(revised, r))
                return false;
            // A device revision counts when its device signed it, no closing refuses it and the revision it follows counts.
            if (r.Type == "device" && r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? previous) && previous.Type == "device")
                return DeviceRevisionCounts(r, previous) && !RevisionRefused(r, previous) && Counts(previous);
            bool counts = r.Signatures.Count > 0 ? Authentic(r) : Unsigned(r).Counts;
            // A record relying on a grant revision that a closing closed counts only when that closing witnesses it.
            return counts && GrantRefuser(r) is null;
        }
    }

    /// <summary>
    /// The verdict on an unsigned record other than an admission. Policy records, history checkpoints and device revisions are always
    /// signed. Under an optional charter an unsigned record counts. Under a required charter, in an estate that has adopted checkpoints
    /// it counts only when an anchoring checkpoint lists its exact id and hash, whatever its date; before adoption, when it is dated at
    /// or before the current charter revision.
    /// </summary>
    internal (bool Counts, string? Code, string? Message) Unsigned(RecordEnvelope record)
    {
        const string Always = "This record requires an admitted-device signature, including under an optional charter signature policy.";
        if (record.Type == "history-checkpoint")
            return (false, "signature.required", Always + " An unsigned history checkpoint witnesses nothing.");
        if (PolicyAuthority.RequiresSignature(model, record)) return (false, "signature.required", Always);
        if (record.Type == "device" && record.Supersedes is not null)
            return (false, "signature.required", "A device revision is signed by the device: a rename by its current key, a rotation by its previous and its new key. Unsigned, it changes nothing.");
        if (!Required) return (true, null, null);
        bool after = string.CompareOrdinal(record.At, Charter!.At) > 0;
        if (Adopted)
        {
            if (anchors.Contains(SignatureChecks.AnchorKey(record.Id, record.ComputedHash))) return (true, null, null);
            return after
                ? (false, "signature.required", Always)
                : (false, "history.unanchored", "This unsigned record counts for nothing: in an estate that has adopted history checkpoints, while the charter requires signatures, an unsigned record counts only when an anchoring checkpoint lists its exact id and hash, whatever its date. The root operator or a person holding administer anchors it after reading it.");
        }

        return after ? (false, "signature.required", Always) : (true, null, null);
    }

    /// <summary>Whether every signature of a signed record verifies, with anchors, and a rotation carries both keys.</summary>
    internal bool Authentic(RecordEnvelope record) => record.Signatures.Count > 0 && record.DeclaredHash == record.ComputedHash && !Verification(record, Mode.Anchors).HasErrors;

    /// <summary>Whether every signature verifies by signatures alone: each device admitted through signed admissions only.</summary>
    internal bool AuthenticBySignatures(RecordEnvelope record) =>
        record.Signatures.Count > 0 && record.DeclaredHash == record.ComputedHash && !Verification(record, Mode.Signatures).HasErrors;

    /// <summary>The findings of verifying every signature of a record, and a rotation's, in a mode.</summary>
    internal FindingList Verification(RecordEnvelope record, Mode mode)
    {
        lock (gate)
        {
            if (verified.TryGetValue((record.Id, mode), out FindingList? known) && model.ById.TryGetValue(record.Id, out RecordEnvelope? same) && ReferenceEquals(same, record)) return known;
            FindingList findings = VerifyWith(record, mode, Acting);
            if (model.ById.TryGetValue(record.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, record)) verified[(record.Id, mode)] = findings;
            return findings;
        }
    }

    /// <summary>
    /// The findings of verifying a record's signatures and rotation in a mode, bounded by the closings given: an admission they refuse
    /// neither admits nor revokes, and a record they refuse is after its closing.
    /// </summary>
    private FindingList VerifyWith(RecordEnvelope record, Mode mode, IReadOnlyList<Closing> bound)
    {
        var findings = new FindingList();
        for (int i = 0; i < record.Signatures.Count; i++) Verify(record, i, (deviceId, at) => AdmissionStateAt(deviceId, at, mode, bound), findings);
        Rotation(record, findings);
        if (Refuser(record, bound, mode) is { } closing) findings.Add(AfterClosing(record, closing));
        return findings;
    }

    /// <summary>The identity of a device record: the id of the first record of its line.</summary>
    internal string IdentityOf(RecordEnvelope device)
    {
        RecordEnvelope current = device;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && previous.Type == "device" && seen.Add(previous.Id))
            current = previous;
        return current.Id;
    }

    /// <summary>A device's revisions that count, first record first: each next one the lowest-id successor its device signed that no closing refuses.</summary>
    internal IReadOnlyList<RecordEnvelope> DeviceChain(string identity)
    {
        lock (gate)
        {
            if (deviceChains.TryGetValue(identity, out IReadOnlyList<RecordEnvelope>? known)) return known;
            var chain = new List<RecordEnvelope>();
            if (model.ById.TryGetValue(identity, out RecordEnvelope? first) && first.Type == "device")
            {
                chain.Add(first);
                var seen = new HashSet<string>(StringComparer.Ordinal) { first.Id };
                RecordEnvelope current = first;
                while (successors[current.Id].Where(s => s.Type == "device" && !seen.Contains(s.Id) && DeviceRevisionCounts(s, current) && !RevisionRefused(s, current))
                    .OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() is { } next)
                {
                    seen.Add(next.Id);
                    chain.Add(next);
                    current = next;
                }
            }

            deviceChains[identity] = chain;
            return chain;
        }
    }

    /// <summary>
    /// Whether a device revision counts: signed only by its device, with its previous key or its new one, by the device's principal,
    /// and, for a rotation at record format 2 or later, by both keys. A revision its device did not sign changes no key or name.
    /// </summary>
    internal bool DeviceRevisionCounts(RecordEnvelope revision, RecordEnvelope previous)
    {
        lock (gate)
        {
            if (deviceRevisions.TryGetValue(revision.Id, out bool known)) return known;
            bool value = Judge();
            deviceRevisions[revision.Id] = value;
            return value;
        }

        bool Judge()
        {
            if (revision.Signatures.Count == 0 || revision.DeclaredHash != revision.ComputedHash) return false;
            if (EstateModel.BodyString(previous, "principal") is not { } principal || revision.By != principal || EstateModel.BodyString(revision, "principal") != principal) return false;
            string identity = IdentityOf(previous);
            string? before = EstateModel.BodyString(previous, "publicKey");
            string? after = EstateModel.BodyString(revision, "publicKey");
            byte[] digest = RecordSigning.Digest(revision.ComputedHash);
            bool byBefore = false, byAfter = false;
            foreach (RecordSignature signature in revision.Signatures)
            {
                if (signature.Algorithm != RecordSigning.Algorithm || !model.ById.TryGetValue(signature.Device, out RecordEnvelope? named) || named.Type != "device" || IdentityOf(named) != identity) return false;
                bool withBefore = before is not null && VerifiesWith(before, revision.ComputedHash, digest, signature.Signature);
                bool withAfter = after is not null && VerifiesWith(after, revision.ComputedHash, digest, signature.Signature);
                if (!withBefore && !withAfter) return false;
                byBefore |= withBefore;
                byAfter |= withAfter;
            }

            return revision.Format < 2 || before == after || (byBefore && byAfter);
        }
    }

    /// <summary>A device's line as the estate reads it: from its latest revision, by supersession, back to its first record.</summary>
    private List<string> RawChain(string deviceId)
    {
        if (!model.ById.TryGetValue(deviceId, out RecordEnvelope? current)) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (rawSupersededBy.TryGetValue(current.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && seen.Add(successor.Id))
            current = successor;
        var ids = new List<string> { current.Id };
        var back = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && back.Add(previous.Id))
        {
            ids.Insert(0, previous.Id);
            current = previous;
        }

        return ids;
    }

    /// <summary>The admission records about any record of a device's line.</summary>
    private IEnumerable<RecordEnvelope> AdmissionsAbout(string deviceId) => RawChain(deviceId).SelectMany(id => admissionsBySubject[id]);

    /// <summary>
    /// Whether an admission can admit or revoke. The estate's first authority does. A signed one does when every signature verifies by a
    /// device admitted when it was signed, through admissions that count in turn, and its author may admit at its time: an operator or a
    /// person holding administer (<see cref="Standing"/>). An unsigned one does at record format 1, and otherwise only with anchors, when
    /// the format-2 migration or an anchoring checkpoint lists it by id and hash. One that a closing refuses neither admits nor revokes,
    /// and neither does one that would unseat what it rests on (<see cref="CountingAdmissions"/>).
    /// </summary>
    internal bool AdmissionCounts(RecordEnvelope admission, Mode mode) => AdmissionCounts(admission, mode, Acting);

    private bool AdmissionCounts(RecordEnvelope admission, Mode mode, IReadOnlyList<Closing> bound) => CountingAdmissions(mode, bound).Contains(admission.Id);

    /// <summary>
    /// The admissions that count in a mode, against the closings given. Starting from the estate's first authority and, with anchors, from
    /// the unsigned admissions anchored or read under record format 1, every signed admission whose signatures verify by devices those
    /// admissions admit at its time, and whose author may admit by the revisions that stand with them, joins, until none does. Each step
    /// judges every admission against the same set, so the result does not depend on the order records are read in. A set that holds an
    /// admission that no longer passes against it, such as one signed by a device a revocation in the set revoked before, is judged again
    /// without some of those admissions, from the start, so that one left out unseats nothing: first those of the admissions that helped
    /// make another fail which fail by themselves, judged with every admission that failed beside them left out, such as a revocation that
    /// revokes what its own signature or its author's authority rests on; failing those, those of them that failed themselves, which unseat
    /// one another; and failing those, every admission that failed, each of which another admission that stands revoked. An admission one
    /// of the closings refuses never joins, and the author's authority is read against the same closings. The result is kept for each set
    /// of closings, with the admissions left out that would otherwise pass (<see cref="Unseating"/>).
    /// </summary>
    private HashSet<string> CountingAdmissions(Mode mode, IReadOnlyList<Closing> bound)
    {
        lock (gate)
        {
            if (mode == Mode.Anchors && !anchorsKnown) throw new InvalidOperationException("Admissions with anchors are judged before every anchor is known.");
            bool acting = ReferenceEquals(bound, Acting);
            if (acting && admittedActing[(int)mode] is { } settled) return settled;
            string key = bound.Count == 0 ? "" : string.Join(",", bound.Select(c => c.Record.Id).Order(StringComparer.Ordinal));
            if (!admittedByClosings[(int)mode].TryGetValue(key, out HashSet<string>? judged))
            {
                judged = Judge();
                admittedByClosings[(int)mode][key] = judged;
            }

            if (acting) admittedActing[(int)mode] = judged;
            return judged;
        }

        HashSet<string> Judge()
        {
            List<RecordEnvelope> all = [.. admissionRecords.Where(a => Open(this, a, bound, mode))];
            var start = new HashSet<string>(StringComparer.Ordinal);
            if (FirstAuthority is { } first && Open(this, first, bound, mode)) start.Add(first.Id);
            foreach (RecordEnvelope admission in all.Where(a => a.Signatures.Count == 0))
            {
                if (mode == Mode.Anchors && (Format < 2 || anchors.Contains(SignatureChecks.AnchorKey(admission.Id, admission.ComputedHash)))) start.Add(admission.Id);
            }

            List<RecordEnvelope> signed = [.. all.Where(a => a.Signatures.Count > 0 && a.Id != FirstAuthority?.Id && a.DeclaredHash == a.ComputedHash)];
            var left = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                HashSet<string> known = Join(start, signed, left);
                Standing found = Against(known);
                List<RecordEnvelope> failed = [.. signed.Where(a => known.Contains(a.Id) && !Passes(a, known, found))];
                if (failed.Count == 0)
                {
                    unseating[known] = [.. signed.Where(a => left.Contains(a.Id) && Passes(a, known, found)).Select(a => a.Id)];
                    return known;
                }

                left.UnionWith(LeftOut(known, failed, signed).Select(a => a.Id));
            }
        }

        // Every signed admission not left out whose signatures verify against the set, and whose author may admit, joins, until none does.
        HashSet<string> Join(HashSet<string> start, List<RecordEnvelope> signed, HashSet<string> left)
        {
            var known = new HashSet<string>(start, StringComparer.Ordinal);
            List<RecordEnvelope> undecided = [.. signed.Where(a => !left.Contains(a.Id))];
            while (true)
            {
                Standing found = Against(known);
                List<RecordEnvelope> joined = [.. undecided.Where(a => VerifiesAgainst(a, known) && found.MayAdmit(a))];
                if (joined.Count == 0) return known;
                foreach (RecordEnvelope admission in joined) known.Add(admission.Id);
                undecided.RemoveAll(joined.Contains);
            }
        }

        // Which admissions a set that holds failing ones is judged again without (above).
        List<RecordEnvelope> LeftOut(HashSet<string> known, List<RecordEnvelope> failed, List<RecordEnvelope> signed)
        {
            var failing = new HashSet<string>(failed.Select(a => a.Id), StringComparer.Ordinal);
            // Only an admission about a device that signed a failing admission, or a principal or grant revision, can be what made one fail.
            var signers = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecordEnvelope record in failed.Concat(principalRecords).Concat(authorityRecords))
            {
                for (int i = 0; i < record.Signatures.Count; i++) signers.Add(Proof(record, i).Identity);
            }

            List<RecordEnvelope> helped = [];
            foreach (RecordEnvelope candidate in signed.Where(a => known.Contains(a.Id) && a.Subject?.Id is { } subject && model.ById.TryGetValue(subject, out RecordEnvelope? device) &&
                device.Type == "device" && signers.Contains(IdentityOf(device))))
            {
                var without = new HashSet<string>(known, StringComparer.Ordinal);
                without.Remove(candidate.Id);
                Standing found = Against(without);
                if (failed.Any(a => a.Id != candidate.Id && VerifiesAgainst(a, without) && found.MayAdmit(a))) helped.Add(candidate);
            }

            var rest = new HashSet<string>(known, StringComparer.Ordinal);
            rest.ExceptWith(failing);
            List<RecordEnvelope> alone = [.. helped.Where(a => new HashSet<string>(rest, StringComparer.Ordinal) { a.Id } is var with && !Passes(a, with, Against(with)))];
            if (alone.Count > 0) return alone;
            List<RecordEnvelope> mutual = [.. helped.Where(a => failing.Contains(a.Id))];
            return mutual.Count > 0 ? mutual : failed;
        }

        bool Passes(RecordEnvelope admission, HashSet<string> counting, Standing found) => VerifiesAgainst(admission, counting) && found.MayAdmit(admission);

        Standing Against(HashSet<string> counting) => StandingAgainst(counting, bound, mode);
    }

    /// <summary>Whether no closing given refuses a record.</summary>
    private static bool Open(EstateTrust trust, RecordEnvelope record, IReadOnlyList<Closing> bound, Mode mode) => bound.Count == 0 || trust.Refuser(record, bound, mode) is null;

    /// <summary>Who may admit, judged from revisions whose signatures verify by the devices a set of admissions admits, against the same closings.</summary>
    private Standing StandingAgainst(HashSet<string> counting, IReadOnlyList<Closing> bound, Mode mode) =>
        new(this, bound, mode, r => r.Signatures.Count > 0 && r.DeclaredHash == r.ComputedHash && Open(this, r, bound, mode) && VerifiesAgainst(r, counting));

    /// <summary>
    /// The admissions left out of those that count with anchors and the closings that act which would pass against them: each would unseat
    /// what it rests on, directly or through another that would unseat it in turn (<see cref="CountingAdmissions"/>).
    /// </summary>
    internal IReadOnlySet<string> Unseating()
    {
        lock (gate)
        {
            return unseating.TryGetValue(CountingAdmissions(Mode.Anchors, Acting), out HashSet<string>? left) ? left : NoAnchors;
        }
    }

    /// <summary>
    /// The findings about admissions that do not count where nothing else says why: one left out because it would unseat what it rests on
    /// (<c>admission.unseats-support</c>).
    /// </summary>
    internal IEnumerable<Finding> AdmissionFindings()
    {
        lock (gate)
        {
            IReadOnlySet<string> left = Unseating();
            var findings = new List<Finding>();
            foreach (RecordEnvelope admission in admissionRecords.Where(a => left.Contains(a.Id)))
            {
                string kind = EstateModel.BodyString(admission, "status") == "revoked" ? "revocation" : "admission";
                findings.Add(new Finding(Severity.Error, "admission.unseats-support", admission.ExpectedPath,
                    $"This {kind} counts for nothing: counting it would unseat what its own signature or its author's authority rests on, directly or through another admission or revocation that would unseat it in turn. One that does not count admits and revokes nothing, so what it would have unseated is read without it. Write it again from a device whose admission does not rest on what it revokes, or as of a time after the admissions it rests on."));
            }

            return findings;
        }
    }

    /// <summary>
    /// Who may admit or revoke devices at an admission's time, judged from the principal and grant revisions a given test finds authentic:
    /// the root operator's own first principal record, and revisions that pass the test and were written by a person holding the operator
    /// role at their time, read the same way. Authority is read as it stood at the admission's time, bounded by the closings given: the
    /// author's principal line as its trusted revisions stood then, and a trusted grant revision in force then, unexpired then, and closed by
    /// none of those closings without witnessing the admission; so a later grant authorizes nothing earlier and a later narrowing withdraws
    /// nothing already written, as <see cref="EstateModel.HasRoleAt"/> and <see cref="EstateModel.HasScopeAt"/> read authority. Admissions
    /// are judged with a test built from the admissions found so far (<see cref="CountingAdmissions"/>), so the first authority is the base
    /// and nothing here reads which admissions count.
    /// </summary>
    private sealed class Standing(EstateTrust trust, IReadOnlyList<Closing> bound, Mode mode, Func<RecordEnvelope, bool> authentic)
    {
        private readonly Dictionary<string, bool> verdicts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> operators = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> formers = new(StringComparer.Ordinal);
        private readonly HashSet<string> checking = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether a revision is the root operator's own first record, or passes the test and was written by a trusted operator at its time,
        /// or is a removal by one who held the operator role earlier (<see cref="TrustedRemoval"/>); never a principal's own record, which
        /// counts for nothing in every read (<see cref="SelfAuthored"/>).
        /// </summary>
        private bool TrustedRevision(RecordEnvelope revision) =>
            (revision.Type == "principal" && revision.Id == trust.RootOperator && revision.By == trust.RootOperator && revision.Supersedes is null) ||
            (!trust.SelfAuthored(revision) && Authentic(revision) && (TrustedOperator(revision.By, revision.At) || TrustedRemoval(revision)));

        /// <summary>
        /// Whether a revision is a removal that keeps counting after its author lost the operator role, as the records that give roles read
        /// it (<see cref="AuthorityReplay"/>): a principal revision giving no operator role on top of a trusted revision that gives it, or a
        /// grant revision that only narrows the trusted revision it supersedes, written by one who held the operator role at its time or
        /// earlier through trusted revisions. So it takes away and never gives.
        /// </summary>
        private bool TrustedRemoval(RecordEnvelope revision)
        {
            if (revision.Supersedes is null || !trust.model.ById.TryGetValue(revision.Supersedes, out RecordEnvelope? previous) || previous.Type != revision.Type) return false;
            bool takesAway = revision.Type == "principal" ? !IsOperator(revision) && IsOperator(previous) : revision.Type == "authority" && AuthorityReplay.Narrows(revision, previous);
            return takesAway && TrustedRevision(previous) && FormerOperator(revision.By, revision.At);
        }

        /// <summary>Whether a principal is the root operator, or held the operator role through trusted revisions at some time up to a time.</summary>
        private bool FormerOperator(string principal, string at)
        {
            if (principal == trust.RootOperator && trust.RootOperator is not null) return true;
            string key = principal + " " + at;
            if (formers.TryGetValue(key, out bool known)) return known;
            if (!checking.Add("former " + key)) return false;
            bool value = trust.principalRecords.Any(p => trust.PrincipalIdentity(p) == principal && string.CompareOrdinal(p.At, at) <= 0 && IsOperator(p) && TrustedOperator(principal, p.At));
            checking.Remove("former " + key);
            formers[key] = value;
            return value;
        }

        private bool Authentic(RecordEnvelope revision)
        {
            if (verdicts.TryGetValue(revision.Id, out bool known)) return known;
            bool value = authentic(revision);
            verdicts[revision.Id] = value;
            return value;
        }

        /// <summary>
        /// The principal record that describes a principal at a time: among the revisions of its line dated at or before the time that are
        /// trusted, the single one no other of them supersedes. Revisions anyone else wrote, signed or not, are not read.
        /// </summary>
        private RecordEnvelope? PrincipalAt(string principal, string at)
        {
            List<RecordEnvelope> revisions = [.. trust.principalRecords.Where(p => trust.PrincipalIdentity(p) == principal && string.CompareOrdinal(p.At, at) <= 0 && TrustedRevision(p))];
            List<RecordEnvelope> heads = [.. revisions.Where(p => !revisions.Any(n => n.Supersedes == p.Id))];
            return heads.Count == 1 ? heads[0] : null;
        }

        /// <summary>Whether a principal is the root operator, or held the operator role at a time through trusted revisions.</summary>
        private bool TrustedOperator(string principal, string at)
        {
            if (principal == trust.RootOperator && trust.RootOperator is not null) return true;
            string key = principal + " " + at;
            if (operators.TryGetValue(key, out bool known)) return known;
            if (!checking.Add(key)) return false;
            bool value = IsOperator(PrincipalAt(principal, at));
            checking.Remove(key);
            operators[key] = value;
            return value;
        }

        /// <summary>
        /// Whether a record's author, a person at its time, held administer then through a grant: a trusted grant revision dated at or before
        /// the record, followed by no trusted revision dated by then, unexpired at the record's time, and closed by none of the closings given
        /// without witnessing the record. Only trusted grant revisions decide, so a revision anyone else writes neither grants nor withdraws it.
        /// </summary>
        private bool HoldsAdminister(RecordEnvelope record)
        {
            if (PrincipalAt(record.By, record.At) is not { } person || EstateModel.BodyString(person, "kind") != "human") return false;
            return trust.authorityRecords.Any(grant => Administers(grant, record.By, record.AtTime) && string.CompareOrdinal(grant.At, record.At) <= 0 && TrustedRevision(grant) &&
                !TrustedSuccessors(grant).Any(s => string.CompareOrdinal(s.At, record.At) <= 0) && !trust.GrantClosedWithin(grant, record, bound, mode));
        }

        /// <summary>The trusted revisions that follow a grant revision: its successors, through any that are not trusted.</summary>
        private IEnumerable<RecordEnvelope> TrustedSuccessors(RecordEnvelope grant)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { grant.Id };
            var queue = new Queue<string>([grant.Id]);
            while (queue.TryDequeue(out string? id))
            {
                foreach (RecordEnvelope next in trust.successors[id].Where(s => s.Type == "authority" && seen.Add(s.Id)))
                {
                    if (TrustedRevision(next)) yield return next;
                    else queue.Enqueue(next.Id);
                }
            }
        }

        /// <summary>Whether an admission's author may admit or revoke devices at its time: an operator, or a person holding administer.</summary>
        internal bool MayAdmit(RecordEnvelope admission) => TrustedOperator(admission.By, admission.At) || HoldsAdminister(admission);
    }

    /// <summary>Whether every signature of a record verifies by devices that a set of admissions admits at its time.</summary>
    private bool VerifiesAgainst(RecordEnvelope record, HashSet<string> counting)
    {
        var findings = new FindingList();
        for (int i = 0; i < record.Signatures.Count; i++) Verify(record, i, (deviceId, at) => StateFrom(deviceId, at, counting), findings);
        return !findings.HasErrors;
    }

    /// <summary>Forgets which admissions count, which reads proofs, anchors and closings that are judged afresh.</summary>
    private void ForgetAdmissions()
    {
        Array.Clear(admittedActing);
        foreach (Dictionary<string, HashSet<string>> judged in admittedByClosings) judged.Clear();
        unseating.Clear();
    }

    /// <summary>The admission state of a device at a time, from the admissions about its line that count in the mode.</summary>
    internal SignatureChecks.AdmissionState AdmissionStateAt(string deviceId, string at, Mode mode) => AdmissionStateAt(deviceId, at, mode, Acting);

    private SignatureChecks.AdmissionState AdmissionStateAt(string deviceId, string at, Mode mode, IReadOnlyList<Closing> bound) =>
        StateFrom(deviceId, at, CountingAdmissions(mode, bound));

    private SignatureChecks.AdmissionState StateFrom(string deviceId, string at, HashSet<string> counting)
    {
        var events = new List<(string Time, int Order, bool Admitted)>();
        foreach (RecordEnvelope admission in AdmissionsAbout(deviceId).Where(a => counting.Contains(a.Id)))
        {
            if (admission.Id != FirstAuthority?.Id && SelfSigned(admission)) continue;
            string? status = EstateModel.BodyString(admission, "status");
            if (status == "admitted") events.Add((admission.At, 0, true));
            else if (status == "revoked" && EstateModel.BodyString(admission, "revokedAt") is string revokedAt) events.Add((revokedAt, 1, false));
        }

        if (events.Count == 0) return SignatureChecks.AdmissionState.Unadmitted;
        bool? admitted = null;
        foreach ((string time, int _, bool value) in events.OrderBy(e => e.Time, StringComparer.Ordinal).ThenBy(e => e.Order))
        {
            if (string.CompareOrdinal(time, at) > 0) break;
            admitted = value;
        }

        return admitted switch
        {
            null => SignatureChecks.AdmissionState.NotYetAdmitted,
            true => SignatureChecks.AdmissionState.Admitted,
            false => SignatureChecks.AdmissionState.Revoked,
        };
    }

    /// <summary>The current admission of a device: the latest counting admission about its line that no counting admission supersedes.</summary>
    internal RecordEnvelope? AdmissionOf(string deviceId) =>
        AdmissionsAbout(deviceId).Where(a => AdmissionCounts(a, Mode.Anchors))
            .Where(a => !rawSupersededBy.TryGetValue(a.Id, out string? next) || !model.ById.TryGetValue(next, out RecordEnvelope? successor) || !AdmissionCounts(successor, Mode.Anchors))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();

    /// <summary>True when an admission is signed by the very device it admits.</summary>
    internal bool SelfSigned(RecordEnvelope admission)
    {
        if (admission.Subject?.Id is not { } subject || !model.ById.TryGetValue(subject, out RecordEnvelope? device) || device.Type != "device") return false;
        string identity = IdentityOf(device);
        return admission.Signatures.Any(s => model.ById.TryGetValue(s.Device, out RecordEnvelope? named) && named.Type == "device" && IdentityOf(named) == identity);
    }

    /// <summary>True when the id names a recovery device, by the purpose its latest revision that counts states.</summary>
    internal bool Recovers(string? deviceId)
    {
        if (deviceId is null || !model.ById.TryGetValue(deviceId, out RecordEnvelope? device) || device.Type != "device") return false;
        IReadOnlyList<RecordEnvelope> chain = DeviceChain(IdentityOf(device));
        return chain.Count > 0 && EstateModel.BodyString(chain[^1], "purpose") == "recovery";
    }

    /// <summary>What a signature proves by itself, before admission is judged: a problem, or the device identity it names and how.</summary>
    /// <param name="Problem">The finding when the signature proves nothing: an unknown algorithm or device, another principal's device, a key that does not verify, or a recovery device outside its scope.</param>
    /// <param name="Identity">The device identity the signature names.</param>
    /// <param name="Named">The device record the signature names, whose line admissions are read along.</param>
    /// <param name="OwnRecord">Whether the record is one of that device's own records, which its keys alone verify.</param>
    /// <param name="SelfAdmission">Whether the record is the root operator's admission of that very device.</param>
    /// <param name="Key">The public key the signature verifies with, when it verifies.</param>
    private sealed record SignatureProof(Finding? Problem, string Identity, string Named, bool OwnRecord, bool SelfAdmission, string? Key = null);

    private SignatureProof Proof(RecordEnvelope record, int index)
    {
        lock (gate)
        {
            bool cacheable = model.ById.TryGetValue(record.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, record);
            if (cacheable && proofs.TryGetValue((record.Id, index), out SignatureProof? known)) return known;
            SignatureProof proof = Prove(record, record.Signatures[index]);
            if (cacheable) proofs[(record.Id, index)] = proof;
            return proof;
        }
    }

    private SignatureProof Prove(RecordEnvelope record, RecordSignature signature)
    {
        string location = record.ExpectedPath + "#signatures";
        if (signature.Algorithm != RecordSigning.Algorithm)
            return new(new Finding(Severity.Error, "signature.algorithm", location, $"Unsupported signature algorithm '{signature.Algorithm}'."), signature.Device, signature.Device, false, false);
        if (!model.ById.TryGetValue(signature.Device, out RecordEnvelope? named) || named.Type != "device")
            return new(new Finding(Severity.Error, "signature.device", location, $"Signed by device {signature.Device}, which has no device record."), signature.Device, signature.Device, false, false);

        // A signature names a device's identity and verifies against the key the device had when the record was authored. A record of
        // the device itself (its first record, a rename or a rotation) verifies against its own key or the previous revision's.
        string identity = IdentityOf(named);
        IReadOnlyList<RecordEnvelope> chain = DeviceChain(identity);
        RecordEnvelope device = chain[^1];
        string? principal = EstateModel.BodyString(device, "principal");
        if (principal != record.By)
            return new(new Finding(Severity.Error, "signature.principal", location, $"Signed by a device of {principal}, but the record is authored by {record.By}."), identity, signature.Device, false, false);
        bool ownRecord = record.Type == "device" && IdentityOf(record) == identity;
        var keys = new List<string?>();
        if (ownRecord)
        {
            keys.Add(EstateModel.BodyString(record, "publicKey"));
            if (record.Supersedes is not null && model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) && previous.Type == "device")
                keys.Add(EstateModel.BodyString(previous, "publicKey"));
        }
        else
        {
            // Timestamps are whole seconds, so a record dated in the very second of a rotation cannot be ordered against it and
            // verifies with either key; at any other time exactly one revision's key is current.
            for (int i = 0; i < chain.Count; i++)
            {
                bool started = i == 0 || string.CompareOrdinal(chain[i].At, record.At) <= 0;
                bool ended = i + 1 < chain.Count && string.CompareOrdinal(chain[i + 1].At, record.At) < 0;
                if (started && !ended) keys.Add(EstateModel.BodyString(chain[i], "publicKey"));
            }
        }

        byte[] digest = RecordSigning.Digest(record.ComputedHash);
        string? verifying = keys.FirstOrDefault(key => key is not null && VerifiesWith(key, record.ComputedHash, digest, signature.Signature));
        if (verifying is null)
        {
            // A key the device held at another time names the revision, so a key used after its rotation, or a rotation dated
            // earlier than it was made, reads as what it is.
            string? other = ownRecord ? null : chain.FirstOrDefault(r => EstateModel.BodyString(r, "publicKey") is string key && !keys.Contains(key) && VerifiesWith(key, record.ComputedHash, digest, signature.Signature))?.Id;
            return other is not null
                ? new(new Finding(Severity.Error, "signature.key-time", location, $"The signature by device {identity} verifies only with the key of its revision {other}, which was not the device's key at this record's time {record.At}."), identity, signature.Device, ownRecord, false)
                : new(new Finding(Severity.Error, "signature.invalid", location, $"The signature by device {identity} does not verify against the record's content hash with the key the device held when it was authored."), identity, signature.Device, ownRecord, false);
        }

        // A recovery device signs its own device records, admissions and revocations, and the closing written before a revocation it
        // signs (design/identity-and-connectivity.md § 3), and nothing else.
        if (Recovers(signature.Device) && record.Type is not ("device" or "admission") && !ClosesDevice(record))
            return new(new Finding(Severity.Error, "signature.recovery-scope", location, $"Device {identity} is a recovery device, which signs only device records, admissions, revocations and the closing checkpoint before a revocation."), identity, signature.Device, ownRecord, false);
        bool selfAdmission = record.Type == "admission" && record.By == RootOperator && record.Subject?.Id is { } subject &&
            model.ById.TryGetValue(subject, out RecordEnvelope? admitted) && admitted.Type == "device" && IdentityOf(admitted) == identity;
        return new(null, identity, signature.Device, ownRecord, selfAdmission, verifying);
    }

    /// <summary>
    /// Whether a signature over a content hash verifies with a key. The answer depends on nothing else, so it is kept when judgments are
    /// made afresh after closings: the proofs that read it depend on device lines, which closings change, but the arithmetic does not.
    /// </summary>
    private bool VerifiesWith(string key, string hash, byte[] digest, string signature)
    {
        lock (gate)
        {
            if (keyVerdicts.TryGetValue((key, hash, signature), out bool known)) return known;
            bool value = RecordSigning.Verify(key, digest, signature);
            keyVerdicts[(key, hash, signature)] = value;
            return value;
        }
    }

    private void Verify(RecordEnvelope record, int index, Func<string, string, SignatureChecks.AdmissionState> state, FindingList findings)
    {
        SignatureProof proof = Proof(record, index);
        if (proof.Problem is { } problem)
        {
            findings.Add(problem);
            return;
        }

        if (proof.OwnRecord || (proof.SelfAdmission && record.Id == FirstAuthority?.Id)) return;
        string location = record.ExpectedPath + "#signatures";
        string identity = proof.Identity;
        SignatureChecks.AdmissionState admission = state(proof.Named, record.At);
        if (proof.SelfAdmission && admission != SignatureChecks.AdmissionState.Admitted)
        {
            findings.Error("signature.self-admission", location, $"Device {identity} admitted itself for the operator; that is accepted once, for the estate's first device ({FirstAuthority?.Id}). A later device is admitted under the signature of a device already admitted.");
            return;
        }

        switch (admission)
        {
            case SignatureChecks.AdmissionState.Admitted:
                return;
            case SignatureChecks.AdmissionState.Revoked:
                findings.Error("signature.revoked", location, $"Device {identity} was revoked before this record was authored; records signed by it from the revocation on are invalid.");
                return;
            case SignatureChecks.AdmissionState.NotYetAdmitted:
                findings.Error("signature.before-admission", location, $"Device {identity} signed this record before it was admitted.");
                return;
            default:
                findings.Error("signature.unadmitted", location, $"Device {identity} is not admitted to this estate; an administrator records an admission first.");
                return;
        }
    }

    /// <summary>A rotation, a device revision with a new key, carries a signature by the previous key and one by the new key.</summary>
    private void Rotation(RecordEnvelope record, FindingList findings)
    {
        if (record.Type != "device" || record.Format < 2 || record.Supersedes is null || !model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) || previous.Type != "device")
        {
            return;
        }

        string? before = EstateModel.BodyString(previous, "publicKey");
        string? after = EstateModel.BodyString(record, "publicKey");
        if (before is null || after is null || before == after) return;
        byte[] digest = RecordSigning.Digest(record.ComputedHash);
        string identity = IdentityOf(previous);
        List<RecordSignature> named = record.Signatures.Where(s => s.Device == identity).ToList();
        if (!named.Any(s => RecordSigning.Verify(before, digest, s.Signature)) || !named.Any(s => RecordSigning.Verify(after, digest, s.Signature)))
            findings.Error("signature.rotation", record.ExpectedPath + "#signatures", "A rotation is signed both by the device's previous key and by its new key.");
    }

    private static bool RequiresSignatures(RecordEnvelope charter) =>
        charter.Body.TryGetProperty("policies", out JsonElement policies) && policies.ValueKind == JsonValueKind.Object &&
        policies.TryGetProperty("signatures", out JsonElement policy) && policy.ValueKind == JsonValueKind.String && policy.GetString() == "required";

    /// <summary>Whether a charter revision counts: a required charter changes only by the root operator's signed revision.</summary>
    internal bool CharterRevisionCounts(RecordEnvelope revised, RecordEnvelope revision) =>
        !RequiresSignatures(revised) || (revision.By == RootOperator && Authentic(revision));

    private static RecordEnvelope? NamedCharter(EstateModel model, string? id) =>
        id is not null && model.ById.TryGetValue(id, out RecordEnvelope? charter) && charter.Type == "estate" && charter.Supersedes is null ? charter : null;

    private RecordEnvelope? ComputeCharter()
    {
        if (FirstEstate is not { } current)
            return Manifest is not null ? null : model.OfType("estate").Where(r => !model.SupersededBy.ContainsKey(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (model.SupersededBy.TryGetValue(current.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && successor.Type == "estate" &&
            seen.Add(successor.Id) && CharterRevisionCounts(current, successor))
        {
            current = successor;
        }

        return current;
    }

    /// <summary>
    /// The unsigned admissions the format-2 migration keeps counting: those listed by a migration naming the step record-format-2,
    /// by the root operator or a principal with the 'administer' scope, signed by devices admitted through signed admissions alone.
    /// </summary>
    private HashSet<string> ComputeMigrationAnchors()
    {
        var anchored = new HashSet<string>(StringComparer.Ordinal);
        if (Format < 2) return anchored;
        foreach (RecordEnvelope migration in model.OfType("migration").Where(m => m.Format >= 2 && m.Signatures.Count > 0))
        {
            if (!migration.Body.TryGetProperty("migrations", out JsonElement steps) || steps.ValueKind != JsonValueKind.Array ||
                !steps.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == "record-format-2"))
                continue;
            if (migration.By != RootOperator && !LoadedAdministers(migration.By, migration.AtTime)) continue;
            if (Verification(migration, Mode.Signatures).HasErrors || !migration.Body.TryGetProperty("anchored", out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String &&
                    entry.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String)
                    anchored.Add(SignatureChecks.AnchorKey(id.GetString()!, hash.GetString()!));
            }
        }

        return anchored;
    }

    /// <summary>
    /// Whether a grant its author may write gives a principal administer, unexpired at a time, read as <see cref="EstateModel.HasScope"/>
    /// reads it (<see cref="AuthorityReplay"/>) from every principal and authority record loaded, under this judgment's root operator: the
    /// model takes its root operator from this judgment, so it cannot be asked while the judgment is made.
    /// </summary>
    private bool LoadedAdministers(string principal, DateTimeOffset at)
    {
        loadedAuthority ??= AuthorityReplay.Run(principalRecords, authorityRecords, RootOperator, r => model.Chain(r)[0],
            id => model.ById.TryGetValue(id, out RecordEnvelope? found) ? found : null);
        return loadedAuthority.GrantLines.Select(loadedAuthority.Current).OfType<RecordEnvelope>().Any(g => Administers(g, principal, at));
    }

    /// <summary>
    /// Whether the author of a checkpoint may anchor records and acknowledge removals in it (design/signed-history.md § 6): the root
    /// operator, or a person holding administer through a grant that rests on signatures and still stands (<see cref="StandingAdministrator"/>),
    /// never an agent; and the checkpoint signed by devices whose own admissions rest on signatures, none of them a recovery device. The
    /// same test tells whether any other record was signed by one who may anchor.
    /// </summary>
    internal bool MayAnchor(RecordEnvelope checkpoint) => MayAnchor(checkpoint, Acting);

    /// <summary>Whether the author of a checkpoint may anchor, reading authority against the closings given.</summary>
    private bool MayAnchor(RecordEnvelope checkpoint, IReadOnlyList<Closing> bound)
    {
        if (!AuthenticBySignatures(checkpoint) || checkpoint.Signatures.Any(s => Recovers(s.Device))) return false;
        if (checkpoint.By == RootOperator && RootOperator is not null) return true;
        return StandingAdministrator(checkpoint, bound);
    }

    /// <summary>
    /// Whether a principal holds the operator role for the strongest acts, signing a device's or a grant's closing: the root operator
    /// always; otherwise a person who stands now and whose line, as those acts read it against the closings given, gives the role at the
    /// record's time (<see cref="ReadStrictly"/>), so a later revision taking it away withdraws the power for every checkpoint.
    /// </summary>
    private bool StandingOperator(string principal, string at, IReadOnlyList<Closing> bound) =>
        principal == RootOperator && RootOperator is not null || ReadStrictly(bound).Standing.Contains(principal) && StrictHead(principal, at, bound) is { Operator: true };

    /// <summary>
    /// Whether a record's author is a person holding administer for the strongest acts: anchoring, acknowledging a removal, rooting an
    /// adoption and signing a device's closing. The author's line, as those acts read it, describes a person at the record's time; a grant
    /// revision they read as written (<see cref="GrantClass"/>) stood at the record's time, dated at or before it, unexpired then and
    /// followed by no revision they read by then; every revision they read that has followed it since, whatever its date, still gives the
    /// author administer as written, unexpired at the record's time; and no closing among those given closed a revision of that grant's
    /// line without witnessing the record. So a later narrowing, revocation or closing withdraws these powers for every checkpoint, as
    /// before signed history, and so does a revision read as a removal; only other records read a grant purely as it stood.
    /// </summary>
    private bool StandingAdministrator(RecordEnvelope record, IReadOnlyList<Closing> bound)
    {
        if (StrictHead(record.By, record.At, bound) is not { Human: true }) return false;
        DateTimeOffset at = record.AtTime;
        return model.OfType("authority").Any(grant => Administers(grant, record.By, at) && string.CompareOrdinal(grant.At, record.At) <= 0 && GrantClass(grant, bound) == StrictClass.Written &&
            !StrictSuccessors(grant, bound).Any(s => string.CompareOrdinal(s.At, record.At) <= 0) &&
            Still(grant) && !bound.Any(c => c.Kind == "grant" && c.Line == LineOf(grant) && !Witnesses(c, record)));

        // Every revision read that has followed the grant since still gives administer as written, unexpired at the record's time.
        bool Still(RecordEnvelope grant)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { grant.Id };
            var queue = new Queue<RecordEnvelope>([grant]);
            while (queue.TryDequeue(out RecordEnvelope? current))
            {
                foreach (RecordEnvelope next in StrictSuccessors(current, bound).Where(s => seen.Add(s.Id)))
                {
                    if (GrantClass(next, bound) != StrictClass.Written || !Administers(next, record.By, at)) return false;
                    queue.Enqueue(next);
                }
            }

            return true;
        }
    }

    /// <summary>Whether a grant revision gives a principal administer, unexpired at a time.</summary>
    private static bool Administers(RecordEnvelope grant, string principal, DateTimeOffset at) =>
        EstateModel.BodyString(grant, "grantee") == principal && GivesAdminister(grant) && !EstateModel.Expired(grant, at);

    /// <summary>Whether a grant revision's scope includes administer, whoever it names and whenever it expires.</summary>
    private static bool GivesAdminister(RecordEnvelope grant) =>
        grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
        scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == Scopes.Administer);

    /// <summary>The line of an authority revision: the id of its first record.</summary>
    internal string LineOf(RecordEnvelope grant)
    {
        RecordEnvelope current = grant;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && previous.Type == "authority" && seen.Add(previous.Id))
            current = previous;
        return current.Id;
    }

    /// <summary>
    /// Whether a grant or principal revision counts for nothing in every read, whoever signed it: a principal record the principal it
    /// describes wrote, at any revision, unless that principal is the root operator (<see cref="SelfAuthored"/>); and one dated before the
    /// record it attaches to (<see cref="AttachedTo"/>), the nearest record below it in its line that counts.
    /// </summary>
    internal bool RefusedAuthorityRevision(RecordEnvelope revision) =>
        SelfAuthored(revision) || AttachedTo(revision) is { } below && string.CompareOrdinal(revision.At, below.At) < 0;

    /// <summary>Whether a principal record was written by the principal it describes, which only the root operator may do.</summary>
    internal bool SelfAuthored(RecordEnvelope revision) =>
        revision.Type == "principal" && revision.By != RootOperator && PrincipalIdentity(revision) == revision.By;

    /// <summary>
    /// The record a grant or principal revision attaches to in its line: the nearest record below it that counts, skipping any that does
    /// not, whether unsigned, refused for its date, a principal's own record (<see cref="SelfAuthored"/>) or not counting otherwise. Null
    /// for a line's first record, for a revision naming no record of its own type and for one with no record below it that counts. So a
    /// revision on top of one that does not count is compared with, and follows, the line where that one would have joined it. Read only
    /// once history is judged, since what counts depends on it.
    /// </summary>
    internal RecordEnvelope? AttachedTo(RecordEnvelope revision)
    {
        if (revision.Type is not ("principal" or "authority")) return null;
        lock (gate)
        {
            bool loaded = model.ById.TryGetValue(revision.Id, out RecordEnvelope? same) && ReferenceEquals(same, revision);
            if (loaded && attached.TryGetValue(revision.Id, out RecordEnvelope? known)) return known;
            // Walk down to a record whose attachment is known or that supersedes nothing of its type, then settle each one on the way up,
            // so that whether the record below counts is judged once its own attachment is known.
            var pending = new Stack<RecordEnvelope>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            RecordEnvelope? current = revision;
            while (current is not null && seen.Add(current.Id) && !(!ReferenceEquals(current, revision) && attached.ContainsKey(current.Id)))
            {
                pending.Push(current);
                current = Below(current);
            }

            RecordEnvelope? result = null;
            while (pending.TryPop(out RecordEnvelope? next))
            {
                RecordEnvelope? previous = Below(next);
                // A record seen twice closes a cycle of supersession; it is taken as it is.
                result = previous is null ? null
                    : !attached.TryGetValue(previous.Id, out RecordEnvelope? under) || Counts(previous) ? previous
                    : under;
                if (!ReferenceEquals(next, revision) || loaded) attached[next.Id] = result;
            }

            return result;
        }
    }

    /// <summary>The record a grant or principal revision names as the one it supersedes, when that is a record of its own type.</summary>
    private RecordEnvelope? Below(RecordEnvelope record) =>
        record.Supersedes is not null && model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) && previous.Type == record.Type ? previous : null;

    /// <summary>The counting revisions that follow a grant or principal revision (<see cref="AttachedTo"/>): its successors, and through one that does not count, that one's.</summary>
    internal IEnumerable<RecordEnvelope> AttachedSuccessors(RecordEnvelope revision)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { revision.Id };
        var queue = new Queue<string>([revision.Id]);
        while (queue.TryDequeue(out string? id))
        {
            foreach (RecordEnvelope next in successors[id].Where(s => s.Type == revision.Type && seen.Add(s.Id)))
            {
                if (Counts(next)) yield return next;
                else queue.Enqueue(next.Id);
            }
        }
    }

    /// <summary>
    /// Whether a principal may anchor in a checkpoint written at a time, the part of <see cref="MayAnchor(RecordEnvelope)"/> that does not depend on a
    /// signature: the root operator, or a person holding administer that still stands (<see cref="StandingAdministrator"/>), judged for a
    /// checkpoint no closing witnesses yet. What the runtime checks before it signs an adoption or a charter revision's anchors.
    /// </summary>
    internal bool AnchorerAt(string by, string at)
    {
        if (by == RootOperator && RootOperator is not null) return true;
        if (!RecordParser.IsTimestamp(at)) return false;
        using JsonDocument empty = JsonDocument.Parse("{}");
        var probe = new RecordEnvelope("history-checkpoint", Identity.Ulid.NewUlid().Value, at, by, null, null, empty.RootElement.Clone(), "", "", []);
        return StandingAdministrator(probe, Acting);
    }

    /// <summary>How the strongest acts read a principal or authority revision, by its author at the revision's time.</summary>
    private enum StrictClass
    {
        /// <summary>
        /// Not read, and the line stays as it was: not authentic by signatures, dated before the record it attaches to, or written by a
        /// person who had never been given the operator role by then.
        /// </summary>
        Unread,

        /// <summary>Read as written: by the root operator, or by a person who held the operator role at its time and stands now.</summary>
        Written,

        /// <summary>
        /// Read as a removal whatever it says: by a person given the operator role by then who had lost it by then or does not stand now.
        /// From it on the line gives no operator role or administer until a revision read as written follows it.
        /// </summary>
        Removal,
    }

    /// <summary>The principal revisions the strongest acts consider, how they read each, and who stands now (<see cref="ReadStrictly"/>).</summary>
    private sealed class StrictReading
    {
        /// <summary>Principal revisions authentic by signatures and not refused, and the root operator's first record, in time order, ids breaking ties.</summary>
        public required List<RecordEnvelope> Revisions { get; init; }

        /// <summary>Each revision's place in <see cref="Revisions"/>.</summary>
        public required Dictionary<string, int> Index { get; init; }

        /// <summary>For each revision read, the place of the nearest revision read below it in its line, or -1.</summary>
        public required int[] Below { get; init; }

        /// <summary>How each revision is read.</summary>
        public required StrictClass[] Classes { get; init; }

        /// <summary>Each person's revisions read, by place, in order.</summary>
        public required Dictionary<string, List<int>> Lines { get; init; }

        /// <summary>The time each person was first given the operator role, the root operator aside.</summary>
        public required Dictionary<string, string> GivenAt { get; init; }

        /// <summary>Who stands now: the root operator and every person whose line gives the operator role at the end.</summary>
        public required HashSet<string> Standing { get; init; }
    }

    /// <summary>
    /// How the strongest acts read principal lines against the closings given. The revisions considered are authentic by signatures with
    /// those closings, so none they refuse, and not refused for their date against the nearest revision below them considered, with the
    /// root operator's first record. A person has been given the operator role by a time when a revision considered of their line
    /// dated by then gives it and was written by the root operator or by a person given the role by an earlier revision, taken in time
    /// order, ids breaking ties, removals aside; so the role reaches a person only from the root operator, through earlier revisions, and a
    /// circle of appointments gives nothing. Walking every line in that order, a revision is read as written when its author is the root
    /// operator, or stands now and held the role as their line stood before this revision; read as a removal, whatever it says, when its
    /// author had been given the role but had lost it by then or does not stand now; and not read at all, leaving the line as it was, when
    /// its author had never been given the role. Who stands now is the least set holding the root operator and every person whose line
    /// gives the role at the end when only members of the set give: built up from the root operator alone, one pass per level of
    /// appointment, until nothing is added. Grants do not count towards it. So a removal keeps counting after its author is removed, a
    /// revision by one never given the role changes nothing, and two operators other than the root operator who remove each other both
    /// lose these acts, closings included, until the root operator settles it. The reading depends on the closings only through which
    /// revisions verify against them, so it is made once for each view (<see cref="View"/>) and shared by every set of closings reaching it.
    /// </summary>
    private StrictReading ReadStrictly(IReadOnlyList<Closing> bound)
    {
        lock (gate)
        {
            StrictView view = View(bound);
            if (view.Reading is { } known) return known;
            string? root = RootOperator;
            principalsInOrder ??= [.. principalRecords.OrderBy(p => p.At, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal)];
            List<RecordEnvelope> revisions = [.. principalsInOrder.Where(p => Considered(p, bound, view) && !StrictlyRefused(p, bound, view))];
            int count = revisions.Count;
            var index = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++) index[revisions[i].Id] = i;
            var persons = new Dictionary<string, int>(StringComparer.Ordinal);
            int[] line = new int[count];
            int[] author = new int[count];
            bool[] gives = new bool[count];
            for (int i = 0; i < count; i++)
            {
                line[i] = Person(PrincipalIdentity(revisions[i]));
                gives[i] = IsOperator(revisions[i]);
            }

            for (int i = 0; i < count; i++) author[i] = persons.TryGetValue(revisions[i].By, out int a) ? a : -1;

            // Given by then, in time order: a revision gives the role only when its author was given it by an earlier one.
            var classes = new StrictClass[count];
            var givenAt = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (revisions[i].By != root && !givenAt.ContainsKey(revisions[i].By)) continue;
                classes[i] = StrictClass.Removal;
                if (gives[i]) givenAt.TryAdd(PrincipalIdentity(revisions[i]), revisions[i].At);
            }

            // Each revision read follows the nearest revision read below it in its line; one not read leaves the line as it was.
            int[] below = new int[count];
            for (int i = 0; i < count; i++) below[i] = classes[i] == StrictClass.Unread ? -1 : NearestRead(revisions[i]);

            var standing = new bool[persons.Count];
            if (root is not null && persons.TryGetValue(root, out int rootPerson)) standing[rootPerson] = true;
            var heads = new List<int>[persons.Count];
            for (int p = 0; p < heads.Length; p++) heads[p] = [];
            bool[] followed = new bool[count];
            while (true)
            {
                foreach (List<int> h in heads) h.Clear();
                Array.Clear(followed);
                for (int i = 0; i < count; i++)
                {
                    if (classes[i] == StrictClass.Unread) continue;
                    // The author is read as their line stands before this revision in time order.
                    classes[i] = revisions[i].By == root || author[i] >= 0 && standing[author[i]] && Gives(author[i]) ? StrictClass.Written : StrictClass.Removal;
                    if (below[i] >= 0)
                    {
                        followed[below[i]] = true;
                        heads[line[i]].Remove(below[i]);
                    }

                    if (!followed[i]) heads[line[i]].Add(i);
                }

                bool added = false;
                for (int p = 0; p < standing.Length; p++)
                {
                    if (standing[p] || !Gives(p)) continue;
                    standing[p] = true;
                    added = true;
                }

                if (!added) break;
            }

            var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (classes[i] == StrictClass.Unread) continue;
                string person = PrincipalIdentity(revisions[i]);
                if (!lines.TryGetValue(person, out List<int>? read)) lines[person] = read = [];
                read.Add(i);
            }

            var now = new HashSet<string>(persons.Where(p => standing[p.Value]).Select(p => p.Key), StringComparer.Ordinal);
            if (root is not null) now.Add(root);
            var reading = new StrictReading { Revisions = revisions, Index = index, Below = below, Classes = classes, Lines = lines, GivenAt = givenAt, Standing = now };
            view.Reading = reading;
            return reading;

            int Person(string id)
            {
                if (!persons.TryGetValue(id, out int p)) persons[id] = p = persons.Count;
                return p;
            }

            bool Gives(int person) => heads[person].Count == 1 && classes[heads[person][0]] == StrictClass.Written && gives[heads[person][0]];

            int NearestRead(RecordEnvelope revision)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { revision.Id };
                RecordEnvelope current = revision;
                while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && previous.Type == "principal" && seen.Add(previous.Id))
                {
                    if (index.TryGetValue(previous.Id, out int k) && classes[k] != StrictClass.Unread) return k;
                    current = previous;
                }

                return -1;
            }
        }
    }

    /// <summary>
    /// The principal record describing a person at a time as the strongest acts read it: among the revisions of its line they read dated
    /// at or before the time, the single one no other of them follows; null when there is none or more than one. It gives the operator
    /// role only when read as written; read as a removal it keeps the person's kind and other roles as written, except that it describes a
    /// human only where the revision read below it did, so it takes away and never gives (<see cref="DescribesHuman"/>).
    /// </summary>
    private (bool Human, bool Operator)? StrictHead(string principal, string at, IReadOnlyList<Closing> bound)
    {
        StrictReading reading = ReadStrictly(bound);
        if (!reading.Lines.TryGetValue(principal, out List<int>? line)) return null;
        var followed = new HashSet<int>();
        int last = -1;
        foreach (int k in line)
        {
            if (string.CompareOrdinal(reading.Revisions[k].At, at) > 0) break;
            last++;
            if (reading.Below[k] >= 0) followed.Add(reading.Below[k]);
        }

        List<int> heads = [.. line.Take(last + 1).Where(k => !followed.Contains(k))];
        if (heads.Count != 1) return null;
        int head = heads[0];
        return (DescribesHuman(reading, head), reading.Classes[head] == StrictClass.Written && IsOperator(reading.Revisions[head]));
    }

    /// <summary>
    /// Whether a principal revision the strongest acts read describes a human: it says so, and so does each revision read below it down
    /// to one read as written or the line's first, so that a revision read as a removal never makes a person human.
    /// </summary>
    private static bool DescribesHuman(StrictReading reading, int revision)
    {
        for (int k = revision; ; k = reading.Below[k])
        {
            if (EstateModel.BodyString(reading.Revisions[k], "kind") != "human") return false;
            if (reading.Classes[k] == StrictClass.Written || reading.Below[k] < 0) return true;
        }
    }

    /// <summary>
    /// How the strongest acts read a grant revision, by its author at its time (<see cref="StrictClass"/>): not read unless authentic by
    /// signatures and not refused for its date; as written by the root operator, or by a person who stands now and held the operator role
    /// at its time as those acts read it; as a removal by a person given the role by then who had lost it or does not stand now.
    /// </summary>
    private StrictClass GrantClass(RecordEnvelope grant, IReadOnlyList<Closing> bound)
    {
        lock (gate)
        {
            StrictView view = View(bound);
            if (view.GrantClasses.TryGetValue(grant.Id, out StrictClass known)) return known;
            StrictClass value = Judge(view);
            view.GrantClasses[grant.Id] = value;
            return value;
        }

        StrictClass Judge(StrictView view)
        {
            if (!Considered(grant, bound, view) || StrictlyRefused(grant, bound, view)) return StrictClass.Unread;
            if (grant.By == RootOperator && RootOperator is not null) return StrictClass.Written;
            StrictReading reading = ReadStrictly(bound);
            if (reading.Standing.Contains(grant.By) && StrictHead(grant.By, grant.At, bound) is { Operator: true }) return StrictClass.Written;
            return reading.GivenAt.TryGetValue(grant.By, out string? given) && string.CompareOrdinal(given, grant.At) <= 0 ? StrictClass.Removal : StrictClass.Unread;
        }
    }

    /// <summary>The grant revisions the strongest acts read that follow a grant revision: its successors, through any they do not read.</summary>
    private IEnumerable<RecordEnvelope> StrictSuccessors(RecordEnvelope grant, IReadOnlyList<Closing> bound)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { grant.Id };
        var queue = new Queue<string>([grant.Id]);
        while (queue.TryDequeue(out string? id))
        {
            foreach (RecordEnvelope next in successors[id].Where(s => s.Type == "authority" && seen.Add(s.Id)))
            {
                if (GrantClass(next, bound) == StrictClass.Unread) queue.Enqueue(next.Id);
                else yield return next;
            }
        }
    }

    /// <summary>
    /// A warning, in an estate holding a checkpoint, for each principal or grant revision that counts and that the strongest acts read as a
    /// removal where that reading differs from the ordinary one in what those acts use: a principal revision giving the operator role or
    /// describing a human they do not read as one, and a grant revision giving administer. Anchoring and closings no longer rest on it.
    /// </summary>
    private IEnumerable<Finding> RevisionsReadAsRemovals()
    {
        string? root = RootOperator;
        if (!model.OfType("history-checkpoint").Any()) yield break;
        StrictReading reading = ReadStrictly(Acting);
        foreach (RecordEnvelope record in model.Records.Where(r => r.Type is "principal" or "authority" && r.By != root))
        {
            string? gives;
            if (record.Type == "principal")
            {
                if (!reading.Index.TryGetValue(record.Id, out int k) || reading.Classes[k] != StrictClass.Removal) continue;
                gives = IsOperator(record) ? "gives the operator role" : EstateModel.BodyString(record, "kind") == "human" && !DescribesHuman(reading, k) ? "describes a human" : null;
            }
            else gives = GivesAdminister(record) && GrantClass(record, Acting) == StrictClass.Removal ? "gives administer" : null;
            if (gives is null || !Counts(record)) continue;
            yield return new Finding(Severity.Warning, "history.revision-author", record.ExpectedPath,
                $"This {record.Type} revision {gives}, but that no longer counts for anchoring and closings: its author's operator role is removed or disputed. A standing operator should write it again.");
        }
    }

    /// <summary>
    /// Whether the strongest acts consider a principal or grant revision against the closings given: authentic by its signatures, or the
    /// root operator's first record; never a principal's own record, which counts for nothing in every read (<see cref="SelfAuthored"/>). A
    /// revision the model holds is read through the view of those closings (<see cref="View"/>).
    /// </summary>
    private bool Considered(RecordEnvelope revision, IReadOnlyList<Closing> bound, StrictView view) =>
        !SelfAuthored(revision) &&
        (revision.Type == "principal" && revision.Id == RootOperator && revision.By == RootOperator && revision.Supersedes is null ||
        (revision.Type is "principal" or "authority" && model.ById.TryGetValue(revision.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, revision)
            ? StrictVerdict(revision, null) != view.Changed.Contains(revision.Id)
            : AuthenticBySignaturesWith(revision, bound)));

    /// <summary>
    /// Whether the strongest acts refuse a principal or grant revision for its date, against the closings given: it is dated before the
    /// nearest revision below it in its line that they consider and do not refuse (<see cref="StrictBelow"/>). Rule 1 as they read it.
    /// </summary>
    private bool StrictlyRefused(RecordEnvelope revision, IReadOnlyList<Closing> bound, StrictView view) =>
        StrictBelow(revision, bound, view) is { } below && string.CompareOrdinal(revision.At, below.At) < 0;

    /// <summary>The nearest revision below a principal or grant revision in its line that the strongest acts consider and do not refuse for its date, against the closings given.</summary>
    private RecordEnvelope? StrictBelow(RecordEnvelope revision, IReadOnlyList<Closing> bound, StrictView view)
    {
        lock (gate)
        {
            Dictionary<string, RecordEnvelope?> known = view.Below;
            if (known.TryGetValue(revision.Id, out RecordEnvelope? settled)) return settled;
            var pending = new Stack<RecordEnvelope>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            RecordEnvelope? current = revision;
            while (current is not null && seen.Add(current.Id) && !(!ReferenceEquals(current, revision) && known.ContainsKey(current.Id)))
            {
                pending.Push(current);
                current = Below(current);
            }

            RecordEnvelope? result = null;
            while (pending.TryPop(out RecordEnvelope? next))
            {
                RecordEnvelope? previous = Below(next);
                // A record seen twice closes a cycle of supersession; it is taken as it is.
                result = previous is null ? null
                    : !known.TryGetValue(previous.Id, out RecordEnvelope? under) ? previous
                    : Considered(previous, bound, view) && (under is null || string.CompareOrdinal(previous.At, under.At) >= 0) ? previous : under;
                known[next.Id] = result;
            }

            return result;
        }
    }

    /// <summary>
    /// What the strongest acts read against a set of closings. It depends on the closings only through which principal and grant revisions
    /// verify by signatures alone against them, so one view serves every set of closings that leaves the same revisions verifying.
    /// </summary>
    private sealed class StrictView
    {
        /// <summary>The principal and grant revisions the model holds whose verdict by signatures alone differs from their verdict with no closings, by id.</summary>
        public required HashSet<string> Changed { get; init; }

        /// <summary>The reading of principal lines (<see cref="ReadStrictly"/>), once made.</summary>
        public StrictReading? Reading { get; set; }

        /// <summary>How each grant revision is read (<see cref="GrantClass"/>), by id.</summary>
        public Dictionary<string, StrictClass> GrantClasses { get; } = new(StringComparer.Ordinal);

        /// <summary>For each principal or grant revision, the nearest revision below it considered and not refused for its date (<see cref="StrictBelow"/>), by id.</summary>
        public Dictionary<string, RecordEnvelope?> Below { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// What the verdicts by signatures alone of the principal and grant revisions the model holds read of the closings. A closing refuses a
    /// record or not by itself, whatever other closings there are (<see cref="Refuses"/>), and a revision's verification reads closings only
    /// through whether one refuses the revision or an admission about a device that signed it. So a set of closings matters to a verdict
    /// only through which of those records it refuses.
    /// </summary>
    private sealed class StrictIndex
    {
        /// <summary>The records the verifications read, each once, by number.</summary>
        public required List<RecordEnvelope> Records { get; init; }

        /// <summary>For each principal or grant revision the model holds, by id: the numbers of the records its verification reads, in order.</summary>
        public required Dictionary<string, int[]> Reads { get; init; }

        /// <summary>For each record read, by number: the revisions whose verification reads it.</summary>
        public required List<List<RecordEnvelope>> Readers { get; init; }

        /// <summary>For each device identity: the numbers of the records read that a device of that identity signed, the only ones a closing naming it can refuse.</summary>
        public required Dictionary<string, List<int>> SignedBy { get; init; }
    }

    /// <summary>
    /// The view of the strongest acts against the closings given. It reads which of the records the verifications read (<see cref="StrictIndex"/>)
    /// a closing among them refuses, each closing's refusals found once (<see cref="Refusals"/>), judges again only the revisions that read
    /// one of those records, each verdict kept by the records refused (<see cref="StrictVerdict"/>), and shares one view among all the sets of
    /// closings that change the same verdicts, so a closing that refuses nothing a verdict reads keeps the view, and the reading, of none.
    /// </summary>
    private StrictView View(IReadOnlyList<Closing> bound)
    {
        lock (gate)
        {
            if (strictViews.TryGetValue(bound, out StrictView? known)) return known;
            Dictionary<int, Closing>? refusers = null;
            foreach (Closing closing in bound)
            {
                foreach (int record in Refusals(closing))
                {
                    refusers ??= [];
                    refusers.TryAdd(record, closing);
                }
            }

            StrictView view = None();
            if (refusers is not null)
            {
                StrictIndex index = Index();
                var changed = new HashSet<string>(StringComparer.Ordinal);
                var judged = new HashSet<RecordEnvelope>(ReferenceEqualityComparer.Instance);
                foreach (int record in refusers.Keys)
                {
                    foreach (RecordEnvelope revision in index.Readers[record])
                    {
                        if (judged.Add(revision) && StrictVerdict(revision, refusers) != StrictVerdict(revision, null)) changed.Add(revision.Id);
                    }
                }

                if (changed.Count > 0)
                {
                    if (!strictViewsByChange.TryGetValue(changed, out StrictView? shared)) strictViewsByChange[changed] = shared = new StrictView { Changed = changed };
                    view = shared;
                }
            }

            strictViews[bound] = view;
            return view;
        }
    }

    /// <summary>The view with no closings, which no verdict differs from.</summary>
    private StrictView None() => strictNone ??= new StrictView { Changed = new HashSet<string>(StringComparer.Ordinal) };

    /// <summary>The records the verifications read that a closing refuses by itself, by number, found once for each closing.</summary>
    private int[] Refusals(Closing closing)
    {
        lock (gate)
        {
            if (strictRefusals.TryGetValue(closing.Record.Id, out int[]? known)) return known;
            int[] refused = [];
            if (closing.Kind != "grant" && closing.Identity is { } identity && Index() is var index && index.SignedBy.TryGetValue(identity, out List<int>? signed))
                refused = [.. signed.Where(record => Refuses(closing, index.Records[record]))];
            strictRefusals[closing.Record.Id] = refused;
            return refused;
        }
    }

    /// <summary>Builds what the verdicts by signatures alone read (<see cref="StrictIndex"/>) once.</summary>
    private StrictIndex Index()
    {
        lock (gate)
        {
            if (strictIndex is not null) return strictIndex;
            var records = new List<RecordEnvelope>();
            var numbers = new Dictionary<RecordEnvelope, int>(ReferenceEqualityComparer.Instance);
            var reads = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var readers = new List<List<RecordEnvelope>>();
            var signedBy = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var admissions = new Dictionary<string, RecordEnvelope[]>(StringComparer.Ordinal);
            foreach (RecordEnvelope revision in principalRecords.Concat(authorityRecords))
            {
                if (!model.ById.TryGetValue(revision.Id, out RecordEnvelope? loaded) || !ReferenceEquals(loaded, revision) || reads.ContainsKey(revision.Id)) continue;
                // The revision itself, which a closing may refuse, and every admission about each device that signed it, whose refusal changes
                // whether that device was admitted at the revision's time.
                var read = new SortedSet<int> { Number(revision) };
                for (int i = 0; i < revision.Signatures.Count; i++)
                {
                    string named = Proof(revision, i).Named;
                    if (!admissions.TryGetValue(named, out RecordEnvelope[]? about)) admissions[named] = about = [.. AdmissionsAbout(named)];
                    foreach (RecordEnvelope admission in about) read.Add(Number(admission));
                }

                reads[revision.Id] = [.. read];
                foreach (int record in read) readers[record].Add(revision);
            }

            strictIndex = new StrictIndex { Records = records, Reads = reads, Readers = readers, SignedBy = signedBy };
            return strictIndex;

            int Number(RecordEnvelope record)
            {
                if (numbers.TryGetValue(record, out int number)) return number;
                number = records.Count;
                numbers[record] = number;
                records.Add(record);
                readers.Add([]);
                var identities = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < record.Signatures.Count; j++)
                {
                    string identity = Proof(record, j).Identity;
                    if (!identities.Add(identity)) continue;
                    if (!signedBy.TryGetValue(identity, out List<int>? signed)) signedBy[identity] = signed = [];
                    signed.Add(number);
                }

                return number;
            }
        }
    }

    /// <summary>
    /// Whether a principal or grant revision the model holds verifies by signatures alone, against no closings when <paramref name="refusers"/>
    /// is null, and otherwise against closings refusing the records given, each by the closing named. It is kept by the records its
    /// verification reads that are refused, which is all the verdict depends on, and judged against one refusing closing for each.
    /// </summary>
    private bool StrictVerdict(RecordEnvelope revision, Dictionary<int, Closing>? refusers)
    {
        lock (gate)
        {
            var refused = new List<int>();
            var closings = new List<Closing>();
            if (refusers is not null)
            {
                foreach (int record in Index().Reads[revision.Id])
                {
                    if (!refusers.TryGetValue(record, out Closing? closing)) continue;
                    refused.Add(record);
                    if (!closings.Contains(closing)) closings.Add(closing);
                }
            }

            (string, string) key = (revision.Id, string.Join(",", refused));
            if (strictVerdicts.TryGetValue(key, out bool known)) return known;
            bool value = AuthenticBySignaturesWith(revision, closings.Count == 0 ? NoClosings : closings);
            strictVerdicts[key] = value;
            return value;
        }
    }

    /// <summary>Forgets every view, verdict and reading of the strongest acts, which read proofs and closings that are judged afresh.</summary>
    private void ForgetStrictViews()
    {
        strictViews.Clear();
        strictViewsByChange.Clear();
        strictVerdicts.Clear();
        strictRefusals.Clear();
        strictIndex = null;
        strictNone = null;
    }

    /// <summary>
    /// Keeps, once the estate is judged, only the view of the closings that act, which every later read uses, and releases what was kept
    /// to build it and the views of the sets of closings each closing was judged against.
    /// </summary>
    private void SettleStrictViews()
    {
        lock (gate)
        {
            bool known = strictViews.TryGetValue(Acting, out StrictView? acting);
            ForgetStrictViews();
            if (known) strictViews[Acting] = acting!;
        }
    }

    /// <summary>Whether every signature of a record verifies by signatures alone against the closings given.</summary>
    private bool AuthenticBySignaturesWith(RecordEnvelope record, IReadOnlyList<Closing> bound) =>
        ReferenceEquals(bound, Acting) ? AuthenticBySignatures(record)
            : record.Signatures.Count > 0 && record.DeclaredHash == record.ComputedHash && !VerifyWith(record, Mode.Signatures, bound).HasErrors;

    /// <summary>
    /// Who acts as the operator for policy authority (<see cref="OperatorReplay"/>): the principal records that count, of which only those
    /// authentic by signatures widen, and the root operator's own first record; a record by an author who no longer stands is read as
    /// written only when signed history places it before the revision that took the role away (<see cref="WitnessedBefore"/>).
    /// </summary>
    internal OperatorReplay PolicyOperators
    {
        get
        {
            lock (gate)
            {
                return policyOperators ??= ReadPolicyOperators(beforeHistory: false);
            }
        }
    }

    /// <summary>
    /// <see cref="PolicyOperators"/> with every removal set aside that came before the estate's history checkpoints could place a record
    /// earlier (<see cref="BeforeHistory"/>): how policy records would stand but for such a removal, which tells them apart from records
    /// that fail for any other reason.
    /// </summary>
    internal OperatorReplay PolicyOperatorsBeforeHistory
    {
        get
        {
            lock (gate)
            {
                return policyOperatorsBeforeHistory ??= ReadPolicyOperators(beforeHistory: true);
            }
        }
    }

    private OperatorReplay ReadPolicyOperators(bool beforeHistory) =>
        OperatorReplay.Run(principalRecords.Where(Counts), RootOperator, PrincipalIdentity, Authentic,
            (record, loss) => WitnessedBefore(record, loss) || beforeHistory && BeforeHistory(record));

    /// <summary>
    /// Whether no history checkpoint could have placed a record before a later revision: the estate has not adopted checkpoints, or its
    /// adoption witnesses the record, which was written before any checkpoint was.
    /// </summary>
    internal bool BeforeHistory(RecordEnvelope record) => !History.Adopted || History.AdoptedAt(record) is not null;

    /// <summary>
    /// Whether signed history places a record before the revision that took its author's operator role away: a counting checkpoint signed
    /// by the root operator, or by an operator who held the role when it was written and stands now (<see cref="StandingOperator"/>, read
    /// against the closings that act), lists the record with its bytes or follows, through its predecessors, a counting checkpoint that
    /// does; is dated no later than the revision and does not list it; and precedes every counting checkpoint that lists the revision. So
    /// a checkpoint by anyone else witnesses only through such a checkpoint that follows it before the revision, and one beside the
    /// revision or after it witnesses nothing for this, whoever follows it later; nor does any in an estate that has not adopted
    /// checkpoints.
    /// </summary>
    internal bool WitnessedBefore(RecordEnvelope record, RecordEnvelope loss)
    {
        if (!History.Adopted || !History.Listings.TryGetValue(record.Id, out IReadOnlyList<string>? listing) ||
            !History.Entries.TryGetValue(record.Id, out WitnessedEntry? entry) || entry.Entry.Hash != record.ComputedHash)
            return false;
        IReadOnlyList<string> carrying = History.Listings.GetValueOrDefault(loss.Id) ?? [];
        return listing.SelectMany(Descendants).Distinct(StringComparer.Ordinal).Any(id => !carrying.Contains(id) &&
            model.ById.TryGetValue(id, out RecordEnvelope? checkpoint) && string.CompareOrdinal(checkpoint.At, loss.At) <= 0 &&
            carrying.All(c => Ancestry(c).Contains(id)) && StandingOperator(checkpoint.By, checkpoint.At, Acting));

        // A counting checkpoint and every counting checkpoint that follows it, through hashes that match.
        HashSet<string> Descendants(string checkpoint)
        {
            lock (gate)
            {
                if (checkpointDescendants.TryGetValue(checkpoint, out HashSet<string>? known)) return known;
                checkpointFollowers ??= History.Predecessors.SelectMany(p => p.Value.Select(previous => (Previous: previous, Next: p.Key)))
                    .ToLookup(x => x.Previous, x => x.Next, StringComparer.Ordinal);
                var set = new HashSet<string>(StringComparer.Ordinal) { checkpoint };
                var queue = new Queue<string>([checkpoint]);
                while (queue.TryDequeue(out string? current))
                {
                    foreach (string next in checkpointFollowers[current])
                    {
                        if (set.Add(next)) queue.Enqueue(next);
                    }
                }

                checkpointDescendants[checkpoint] = set;
                return set;
            }
        }

        HashSet<string> Ancestry(string checkpoint)
        {
            lock (gate)
            {
                if (checkpointAncestry.TryGetValue(checkpoint, out HashSet<string>? known)) return known;
                var set = new HashSet<string>(StringComparer.Ordinal) { checkpoint };
                var queue = new Queue<string>([checkpoint]);
                while (queue.TryDequeue(out string? current))
                {
                    foreach (string previous in History.Predecessors.GetValueOrDefault(current) ?? [])
                    {
                        if (set.Add(previous)) queue.Enqueue(previous);
                    }
                }

                checkpointAncestry[checkpoint] = set;
                return set;
            }
        }
    }

    /// <summary>Whether a principal record describes a person holding the operator role.</summary>
    private static bool IsOperator(RecordEnvelope? head) =>
        head is not null && EstateModel.BodyString(head, "kind") == "human" && head.Body.TryGetProperty("roles", out JsonElement roles) &&
        roles.ValueKind == JsonValueKind.Array && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == Roles.Operator);

    private string PrincipalIdentity(RecordEnvelope principal)
    {
        RecordEnvelope current = principal;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && previous.Type == "principal" && seen.Add(previous.Id))
            current = previous;
        return current.Id;
    }
}
