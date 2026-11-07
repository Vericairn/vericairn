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
/// operator is read from the first charter, and admissions count from the root operator's first self-signed admission outward. Signatures
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
    private bool anchorsKnown;
    private readonly Dictionary<(string Id, Mode Mode), FindingList> verified = [];
    private readonly Dictionary<string, bool> counts = new(StringComparer.Ordinal);
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
    private IReadOnlySet<string> anchors = NoAnchors;

    private EstateTrust(EstateModel model, EstateManifest? manifest, Func<string, byte[]?>? objects, IReadOnlySet<string>? files)
    {
        this.model = model;
        Manifest = manifest;
        // Read through the type lookups, which hold every record until the loader keeps only those that count, so that judging a model
        // walks only the records trust rests on.
        admissionsBySubject = model.OfType("admission").Where(r => r.Subject is not null).ToLookup(r => r.Subject!.Id, StringComparer.Ordinal);
        successors = TrustTypes.SelectMany(model.OfType).Where(r => r.Supersedes is not null).ToLookup(r => r.Supersedes!, StringComparer.Ordinal);
        rawSupersededBy = model.SupersededBy;
        principalRecords = [.. model.OfType("principal")];
        authorityRecords = [.. model.OfType("authority")];
        // The first charter is the one the manifest names; another charter that supersedes nothing never displaces it. Only a model read
        // without a manifest falls back to the first charter by id.
        FirstEstate = manifest is null ? model.RawFirstEstate() : NamedCharter(model, manifest.Estate);
        RootOperator = FirstEstate is { } first ? EstateModel.BodyString(first, "operator") : null;
        Format = manifest?.RecordFormat ?? (model.Records.Count == 0 ? RecordFormats.First : model.Records.Max(r => r.Format));
        FirstAuthority = model.OfType("admission").Where(r => r.By == RootOperator && RootOperator is not null && SelfSigned(r) && !Recovers(r.Subject?.Id))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).FirstOrDefault();

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

    /// <summary>The root operator's first self-signed admission.</summary>
    internal RecordEnvelope? FirstAuthority { get; }

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

    /// <summary>Judges a model's records.</summary>
    internal static EstateTrust Build(EstateModel model, EstateManifest? manifest, Func<string, byte[]?>? objects = null, IReadOnlySet<string>? files = null) =>
        new(model, manifest, objects, files);

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
    /// Whether a record counts. A witnessed record present with another hash does not; otherwise each kind follows its rule: admissions
    /// <see cref="AdmissionCounts(RecordEnvelope, Mode)"/>, device revisions <see cref="DeviceRevisionCounts"/>, checkpoints the history graph, charter revisions
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
            if (History.Altered.Contains(r.Id)) return false;
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
    /// Whether an admission can admit or revoke. The root operator's first self-signed admission does. A signed one does when every
    /// signature verifies by a device admitted when it was signed, through admissions that count in turn. An unsigned one does at record
    /// format 1, and otherwise only with anchors, when the format-2 migration or an anchoring checkpoint lists it by id and hash. One that a
    /// closing refuses neither admits nor revokes.
    /// </summary>
    internal bool AdmissionCounts(RecordEnvelope admission, Mode mode) => AdmissionCounts(admission, mode, Acting);

    private bool AdmissionCounts(RecordEnvelope admission, Mode mode, IReadOnlyList<Closing> bound) => CountingAdmissions(mode, bound).Contains(admission.Id);

    /// <summary>
    /// The admissions that count in a mode, against the closings given. Starting from the root operator's first self-signed admission and,
    /// with anchors, from the unsigned admissions anchored or read under record format 1, every signed admission whose signatures verify by
    /// devices those admissions admit at its time joins, until none does; then any whose signer a later-joined revocation ended is dropped,
    /// until every one verifies. Each step judges every admission against the same set, so the result does not depend on the order records
    /// are read in. An admission one of the closings refuses never joins. The result is kept for each set of closings.
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
            List<RecordEnvelope> all = [.. model.OfType("admission").Where(Open)];
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (FirstAuthority is { } first && Open(first)) known.Add(first.Id);
            foreach (RecordEnvelope admission in all.Where(a => a.Signatures.Count == 0))
            {
                if (mode == Mode.Anchors && (Format < 2 || anchors.Contains(SignatureChecks.AnchorKey(admission.Id, admission.ComputedHash)))) known.Add(admission.Id);
            }

            List<RecordEnvelope> undecided = [.. all.Where(a => a.Signatures.Count > 0 && a.Id != FirstAuthority?.Id && a.DeclaredHash == a.ComputedHash)];
            while (true)
            {
                List<RecordEnvelope> joined = [.. undecided.Where(a => VerifiesAgainst(a, known))];
                if (joined.Count == 0) break;
                foreach (RecordEnvelope admission in joined) known.Add(admission.Id);
                undecided.RemoveAll(joined.Contains);
            }

            while (true)
            {
                List<RecordEnvelope> failed = [.. all.Where(a => known.Contains(a.Id) && a.Signatures.Count > 0 && a.Id != FirstAuthority?.Id && !VerifiesAgainst(a, known))];
                if (failed.Count == 0) break;
                foreach (RecordEnvelope admission in failed) known.Remove(admission.Id);
            }

            return known;
        }

        bool Open(RecordEnvelope admission) => bound.Count == 0 || Refuser(admission, bound, mode) is null;
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
            if (migration.By != RootOperator && !model.HasScope(migration.By, Scopes.Administer, migration.AtTime)) continue;
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
    /// Whether a grant or principal revision counts for nothing in every read, whoever signed it: one dated before the record it attaches
    /// to (<see cref="AttachedTo"/>), the nearest record below it in its line that counts.
    /// </summary>
    internal bool RefusedAuthorityRevision(RecordEnvelope revision) =>
        AttachedTo(revision) is { } below && string.CompareOrdinal(revision.At, below.At) < 0;

    /// <summary>
    /// The record a grant or principal revision attaches to in its line: the nearest record below it that counts, skipping any that does
    /// not, whether unsigned, refused for its date or not counting otherwise. Null for a line's first record, for a revision naming no
    /// record of its own type and for one with no record below it that counts. So a revision on top of one that does not count is
    /// compared with, and follows, the line where that one would have joined it. Read only once history is judged, since what counts
    /// depends on it.
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
    /// root operator's first record. A revision the model holds is read through the view of those closings (<see cref="View"/>).
    /// </summary>
    private bool Considered(RecordEnvelope revision, IReadOnlyList<Closing> bound, StrictView view) =>
        revision.Type == "principal" && revision.Id == RootOperator && revision.By == RootOperator && revision.Supersedes is null ||
        (revision.Type is "principal" or "authority" && model.ById.TryGetValue(revision.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, revision)
            ? StrictVerdict(revision, null) != view.Changed.Contains(revision.Id)
            : AuthenticBySignaturesWith(revision, bound));

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
