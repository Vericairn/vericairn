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
/// which records count at all (design/signed-history.md §§ 5 to 7). A record that does not count is kept as evidence and reported,
/// and the model's indexes leave it out, so no rule that admits, authorizes, accepts, clears or holds reads it.
/// </summary>
/// <remarks>
/// Everything here reads the records as loaded, never the model's counting indexes, so the judgment cannot depend on itself. The root
/// operator is read from the first charter. Signatures are judged in two modes: without anchors, where only signed admissions count,
/// which is the mode anchors and acknowledged removals are judged in, so that nothing they anchor can be what makes them count; and with
/// anchors, where an unsigned admission also counts when the format-2 migration or an anchoring checkpoint lists it.
/// </remarks>
internal sealed class EstateTrust
{
    internal enum Mode
    {
        /// <summary>No anchor keeps an unsigned admission counting.</summary>
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
    private readonly IReadOnlyDictionary<string, string> rawSupersededBy;
    private bool anchorsKnown;
    private readonly Dictionary<(string Id, Mode Mode), FindingList> verified = [];
    private readonly Dictionary<string, bool> counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> trustedOperators = new(StringComparer.Ordinal);
    private readonly HashSet<string> checkingOperators = new(StringComparer.Ordinal);
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
        FirstEstate = model.RawFirstEstate();
        RootOperator = FirstEstate is { } first ? EstateModel.BodyString(first, "operator") : null;
        Format = manifest?.RecordFormat ?? (model.Records.Count == 0 ? RecordFormats.First : model.Records.Max(r => r.Format));
        FirstAuthority = model.OfType("admission").Where(r => r.By == RootOperator && RootOperator is not null && SelfSigned(r) && !Recovers(r.Subject?.Id))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).FirstOrDefault();

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
    /// <see cref="AdmissionCounts"/>, device revisions <see cref="DeviceRevisionCounts"/>, checkpoints the history graph, charter revisions
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
            if (r.Type == "admission") return AdmissionCounts(r, Mode.Anchors);
            if (r.Type == "history-checkpoint") return History.CountingCheckpoints.Contains(r.Id);
            // A revision of a required charter that the root operator did not sign changes nothing (FIX-79), so it does not count.
            if (r.Type == "estate" && r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? revised) && revised.Type == "estate" &&
                !CharterRevisionCounts(revised, r))
                return false;
            // A device revision counts when its device signed it and the revision it follows counts.
            if (r.Type == "device" && r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? previous) && previous.Type == "device")
                return DeviceRevisionCounts(r, previous) && Counts(previous);
            return r.Signatures.Count > 0 ? Authentic(r) : Unsigned(r).Counts;
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
            var findings = new FindingList();
            for (int i = 0; i < record.Signatures.Count; i++) Verify(record, i, (deviceId, at) => AdmissionStateAt(deviceId, at, mode), findings);
            Rotation(record, findings);
            if (model.ById.TryGetValue(record.Id, out RecordEnvelope? loaded) && ReferenceEquals(loaded, record)) verified[(record.Id, mode)] = findings;
            return findings;
        }
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

    /// <summary>A device's revisions that count, first record first: each next one the lowest-id successor its device signed.</summary>
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
                while (successors[current.Id].Where(s => s.Type == "device" && !seen.Contains(s.Id) && DeviceRevisionCounts(s, current))
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
                bool withBefore = before is not null && RecordSigning.Verify(before, digest, signature.Signature);
                bool withAfter = after is not null && RecordSigning.Verify(after, digest, signature.Signature);
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
    /// Whether an admission can admit or revoke. A signed one does. An unsigned one does at record format 1, and otherwise only with
    /// anchors, when the format-2 migration or an anchoring checkpoint lists it by id and hash.
    /// </summary>
    internal bool AdmissionCounts(RecordEnvelope admission, Mode mode)
    {
        if (mode == Mode.Anchors && !anchorsKnown) throw new InvalidOperationException("Admissions with anchors are judged before every anchor is known.");
        return admission.Signatures.Count > 0 || Format < 2 || (mode == Mode.Anchors && anchors.Contains(SignatureChecks.AnchorKey(admission.Id, admission.ComputedHash)));
    }

    /// <summary>The admission state of a device at a time, from the admissions about its line that count in the mode.</summary>
    internal SignatureChecks.AdmissionState AdmissionStateAt(string deviceId, string at, Mode mode)
    {
        var events = new List<(string Time, int Order, bool Admitted)>();
        foreach (RecordEnvelope admission in AdmissionsAbout(deviceId).Where(a => AdmissionCounts(a, mode)))
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
    private sealed record SignatureProof(Finding? Problem, string Identity, string Named, bool OwnRecord, bool SelfAdmission);

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
        if (!keys.Any(key => key is not null && RecordSigning.Verify(key, digest, signature.Signature)))
        {
            // A key the device held at another time names the revision, so a key used after its rotation, or a rotation dated
            // earlier than it was made, reads as what it is.
            string? other = ownRecord ? null : chain.FirstOrDefault(r => EstateModel.BodyString(r, "publicKey") is string key && !keys.Contains(key) && RecordSigning.Verify(key, digest, signature.Signature))?.Id;
            return other is not null
                ? new(new Finding(Severity.Error, "signature.key-time", location, $"The signature by device {identity} verifies only with the key of its revision {other}, which was not the device's key at this record's time {record.At}."), identity, signature.Device, ownRecord, false)
                : new(new Finding(Severity.Error, "signature.invalid", location, $"The signature by device {identity} does not verify against the record's content hash with the key the device held when it was authored."), identity, signature.Device, ownRecord, false);
        }

        // A recovery device signs its own device records, admissions and revocations, and nothing else.
        if (Recovers(signature.Device) && record.Type is not ("device" or "admission"))
            return new(new Finding(Severity.Error, "signature.recovery-scope", location, $"Device {identity} is a recovery device, which signs only device records, admissions and revocations."), identity, signature.Device, ownRecord, false);
        bool selfAdmission = record.Type == "admission" && record.By == RootOperator && record.Subject?.Id is { } subject &&
            model.ById.TryGetValue(subject, out RecordEnvelope? admitted) && admitted.Type == "device" && IdentityOf(admitted) == identity;
        return new(null, identity, signature.Device, ownRecord, selfAdmission);
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

    private RecordEnvelope? ComputeCharter()
    {
        if (FirstEstate is not { } current)
            return model.OfType("estate").Where(r => !model.SupersededBy.ContainsKey(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
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
    /// operator, or a person holding administer through a grant that rests on signatures, never an agent; and the checkpoint signed by
    /// devices whose own admissions rest on signatures, none of them a recovery device.
    /// </summary>
    internal bool MayAnchor(RecordEnvelope checkpoint)
    {
        if (!AuthenticBySignatures(checkpoint) || checkpoint.Signatures.Any(s => Recovers(s.Device))) return false;
        if (checkpoint.By == RootOperator && RootOperator is not null) return true;
        if (PrincipalAt(checkpoint.By, checkpoint.At) is not { } person || EstateModel.BodyString(person, "kind") != "human") return false;
        DateTimeOffset at = checkpoint.AtTime;
        // Only grant revisions that rest on signatures and were written by a trusted operator decide: a revision anyone else writes
        // neither grants nor withdraws administer.
        return model.OfType("authority").Any(grant => EstateModel.BodyString(grant, "grantee") == checkpoint.By &&
            grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == Scopes.Administer) &&
            !EstateModel.Expired(grant, at) && TrustedRevision(grant) &&
            !successors[grant.Id].Any(s => s.Type == "authority" && TrustedRevision(s)));
    }

    /// <summary>Whether a revision rests on signatures and was written by a trusted operator, or is the root operator's own first record.</summary>
    private bool TrustedRevision(RecordEnvelope revision) =>
        (revision.Type == "principal" && revision.Id == RootOperator && revision.By == RootOperator && revision.Supersedes is null) ||
        (AuthenticBySignatures(revision) && TrustedOperator(revision.By, revision.At));

    /// <summary>
    /// The principal record that describes a principal at a time: among the revisions of its line dated at or before the time that rest on
    /// signatures and were written by a trusted operator, the single one no other of them supersedes. Revisions anyone else wrote, signed
    /// or not, are not read.
    /// </summary>
    private RecordEnvelope? PrincipalAt(string principal, string at)
    {
        List<RecordEnvelope> revisions = model.OfType("principal")
            .Where(p => PrincipalIdentity(p) == principal && string.CompareOrdinal(p.At, at) <= 0 && TrustedRevision(p)).ToList();
        List<RecordEnvelope> heads = revisions.Where(p => !revisions.Any(n => n.Supersedes == p.Id)).ToList();
        return heads.Count == 1 ? heads[0] : null;
    }

    /// <summary>Whether a principal is the root operator, or held the operator role at a time through revisions that rest on signatures.</summary>
    private bool TrustedOperator(string principal, string at)
    {
        if (principal == RootOperator && RootOperator is not null) return true;
        string key = principal + " " + at;
        lock (gate)
        {
            if (trustedOperators.TryGetValue(key, out bool known)) return known;
            if (!checkingOperators.Add(key)) return false;
            RecordEnvelope? head = PrincipalAt(principal, at);
            bool value = head is not null && EstateModel.BodyString(head, "kind") == "human" &&
                head.Body.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array &&
                roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == Roles.Operator);
            checkingOperators.Remove(key);
            trustedOperators[key] = value;
            return value;
        }
    }

    private string PrincipalIdentity(RecordEnvelope principal)
    {
        RecordEnvelope current = principal;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (current.Supersedes is not null && model.ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && previous.Type == "principal" && seen.Add(previous.Id))
            current = previous;
        return current.Id;
    }
}
