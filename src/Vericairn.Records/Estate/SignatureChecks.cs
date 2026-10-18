// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Verifies every signature against the device records of the estate, and enforces the estate's signature policy.</summary>
public static class SignatureChecks
{
    /// <summary>Runs after all records are indexed.</summary>
    public static void Run(EstateModel model, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(findings);
        RecordEnvelope? estate = model.CurrentEstate;
        bool required = estate is not null && estate.Body.TryGetProperty("policies", out System.Text.Json.JsonElement policies) && policies.TryGetProperty("signatures", out System.Text.Json.JsonElement policy) && policy.GetString() == "required";
        string? rootOperator = estate is null ? null : EstateModel.BodyString(estate, "operator");
        string? firstAuthority = FirstAuthority(model, rootOperator)?.Id;
        IReadOnlySet<string> anchored = Anchored(model);
        foreach (RecordEnvelope record in model.Records)
        {
            if (record.Signatures.Count == 0)
            {
                if (!Counts(model, record, anchored))
                {
                    findings.Error("signature.admission-unsigned", record.ExpectedPath, "In an estate at record format 2 an admission is signed: by a device already admitted or, for the operator's first device, by the device it admits. An unsigned one counts only when the signed migration that moved the estate to format 2 lists it; otherwise, whatever format or date it states, it admits and revokes nothing.");
                }
                else if (PolicyAuthority.RequiresSignature(model, record) || (required && string.CompareOrdinal(record.At, estate!.At) > 0))
                {
                    findings.Error("signature.required", record.ExpectedPath, "This record requires an admitted-device signature, including under an optional charter signature policy.");
                }

                continue;
            }

            foreach (RecordSignature signature in record.Signatures)
            {
                Verify(model, record, signature, rootOperator, firstAuthority, findings, anchored);
            }

            Rotation(model, record, findings);
            RecoveryUse(model, record, rootOperator, firstAuthority, anchored, findings);
        }
    }

    /// <summary>Verifies attribution at authorship, including admission and key history, without changing any record.</summary>
    public static bool IsAuthentic(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        if (record.Signatures.Count == 0 || record.DeclaredHash != record.ComputedHash) return false;
        string? rootOperator = model.CurrentEstate is { } estate ? EstateModel.BodyString(estate, "operator") : null;
        var findings = new FindingList();
        foreach (RecordSignature signature in record.Signatures)
            Verify(model, record, signature, rootOperator, FirstAuthority(model, rootOperator)?.Id, findings, Anchored(model));
        Rotation(model, record, findings);
        return !findings.HasErrors;
    }

    /// <summary>True when the id names a recovery device, one whose key is kept offline for emergency access.</summary>
    public static bool Recovers(EstateModel model, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return deviceId is not null && model.ById.TryGetValue(deviceId, out RecordEnvelope? device) && device.Type == "device" && EstateModel.BodyString(model.Latest(device), "purpose") == "recovery";
    }

    /// <summary>
    /// Every use of a recovery device stays in view until the operator reviews it: a record it signed, other than its own device
    /// records, carries a warning until a current review by an operator names that exact record and is signed by an admitted device
    /// that is not a recovery device. An unsigned review clears nothing, since anyone able to add records can write one in the
    /// operator's name while the charter leaves signatures optional.
    /// </summary>
    private static void RecoveryUse(EstateModel model, RecordEnvelope record, string? rootOperator, string? firstAuthority, IReadOnlySet<string> anchored, FindingList findings)
    {
        string? recovery = record.Signatures.Select(s => IdentityOf(model, s.Device)).FirstOrDefault(identity => Recovers(model, identity));
        if (recovery is null || (record.Type == "device" && model.Chain(record)[0] == recovery))
        {
            return;
        }

        bool reviewed = model.About(record.Id, "review").Any(review =>
            model.IsCurrent(review) && model.HasRole(review.By, Types.Roles.Operator) && review.Subject?.Hash == record.ComputedHash && SignedByAdmittedDevices(model, review, rootOperator, firstAuthority, anchored));
        if (!reviewed)
        {
            findings.Warning("recovery.unreviewed", record.ExpectedPath, $"Signed by recovery device {recovery}: emergency access stays in view until an operator records a review of this record that an admitted device other than a recovery device signs.");
        }
    }

    /// <summary>
    /// True when a record is signed, by no recovery device, and every signature verifies by a device admitted when the record was
    /// authored, through admissions that count.
    /// </summary>
    private static bool SignedByAdmittedDevices(EstateModel model, RecordEnvelope record, string? rootOperator, string? firstAuthority, IReadOnlySet<string> anchored)
    {
        if (record.Signatures.Count == 0 || record.Signatures.Any(s => Recovers(model, s.Device)))
        {
            return false;
        }

        var verified = new FindingList();
        foreach (RecordSignature signature in record.Signatures)
        {
            Verify(model, record, signature, rootOperator, firstAuthority, verified, anchored);
        }

        return !verified.HasErrors;
    }

    /// <summary>The identity of a device: the id of its first device record, which every revision keeps; null when the id names none.</summary>
    public static string? IdentityOf(EstateModel model, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return deviceId is not null && model.ById.TryGetValue(deviceId, out RecordEnvelope? device) && device.Type == "device" ? model.Chain(device)[0] : null;
    }

    /// <summary>True when an admission is signed by the very device it admits, compared by identity so that no revision hides it.</summary>
    public static bool SelfSigned(EstateModel model, RecordEnvelope admission)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(admission);
        string? subject = IdentityOf(model, admission.Subject?.Id);
        return subject is not null && admission.Signatures.Any(s => IdentityOf(model, s.Device) == subject);
    }

    /// <summary>A rotation, a device revision with a new key, carries a signature by the previous key and one by the new key.</summary>
    private static void Rotation(EstateModel model, RecordEnvelope record, FindingList findings)
    {
        if (record.Type != "device" || record.Format < 2 || record.Supersedes is null || !model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) || previous.Type != "device")
        {
            return;
        }

        string? before = EstateModel.BodyString(previous, "publicKey");
        string? after = EstateModel.BodyString(record, "publicKey");
        if (before is null || after is null || before == after)
        {
            return;
        }

        byte[] digest = RecordSigning.Digest(record.ComputedHash);
        string identity = model.Chain(previous)[0];
        List<RecordSignature> named = record.Signatures.Where(s => s.Device == identity).ToList();
        if (!named.Any(s => RecordSigning.Verify(before, digest, s.Signature)) || !named.Any(s => RecordSigning.Verify(after, digest, s.Signature)))
        {
            findings.Error("signature.rotation", record.ExpectedPath + "#signatures", "A rotation is signed both by the device's previous key and by its new key.");
        }
    }

    /// <summary>
    /// The estate's first authority: the earliest admission by the root operator of a device that signed that admission itself.
    /// It is the only self-admission ever accepted; every later device is admitted by a device already admitted, so nobody who can
    /// add records can mint a device for the operator and sign as the operator with it. A recovery device is never the first: an
    /// admitted device of the operator makes and admits it.
    /// </summary>
    public static RecordEnvelope? FirstAuthority(EstateModel model, string? rootOperator)
    {
        ArgumentNullException.ThrowIfNull(model);
        return rootOperator is null ? null : model.OfType("admission")
            .Where(a => a.By == rootOperator && SelfSigned(model, a) && !Recovers(model, a.Subject?.Id))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The current admission of a device: the latest admission record about any revision of the device that counts.</summary>
    public static RecordEnvelope? AdmissionOf(EstateModel model, RecordEnvelope device)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(device);
        IReadOnlySet<string> anchored = Anchored(model);
        IReadOnlyList<string> chain = model.Chain(model.Latest(device));
        return chain.SelectMany(id => model.About(id, "admission")).Where(a => Counts(model, a, anchored)).Where(a => CurrentAmongCounting(model, a, anchored)).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>Current unless superseded by a record that counts: an unsigned revision that counts for nothing cannot unseat an admission either.</summary>
    private static bool CurrentAmongCounting(EstateModel model, RecordEnvelope admission, IReadOnlySet<string> anchored) =>
        !model.SupersededBy.TryGetValue(admission.Id, out string? next) || !model.ById.TryGetValue(next, out RecordEnvelope? successor) || !Counts(model, successor, anchored);

    /// <summary>
    /// Whether a record can admit or revoke. In an estate still at record format 1 an unsigned admission counts while the charter leaves
    /// signatures optional, as format 1 has it. Once the estate is at record format 2 an unsigned admission counts only when the signed
    /// migration that moved the estate lists it (<see cref="Anchored"/>), whatever format or date the admission states: a format and a
    /// date are what the record's author wrote, and only a signature is something an author without an admitted key cannot write.
    /// </summary>
    public static bool Counts(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        return Counts(model, record, Anchored(model));
    }

    private static bool Counts(EstateModel model, RecordEnvelope record, IReadOnlySet<string> anchored) =>
        record.Type != "admission" || record.Signatures.Count > 0 || EstateFormat(model) < 2 || anchored.Contains(AnchorKey(record.Id, record.ComputedHash));

    /// <summary>The estate's record format: its manifest's, or, where a model has no manifest, the newest format its records state.</summary>
    private static long EstateFormat(EstateModel model) =>
        model.Manifest?.RecordFormat ?? (model.Records.Count == 0 ? RecordFormats.First : model.Records.Max(r => r.Format));

    private static readonly ConditionalWeakTable<EstateModel, IReadOnlySet<string>> AnchorCache = [];

    private static readonly IReadOnlySet<string> NoAnchors = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The unsigned admissions an estate at record format 2 keeps counting, as id and hash keys: those listed by a migration record that
    /// names the step record-format-2 and is signed by the root operator, or by a principal with the 'administer' scope, with a device
    /// whose admission rests on signed admissions alone. A list in an unsigned migration, in one by another principal, or in one signed by
    /// a device that only an unsigned admission admitted, anchors nothing.
    /// </summary>
    public static IReadOnlySet<string> Anchored(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return AnchorCache.GetValue(model, ComputeAnchors);
    }

    /// <summary>The key of an anchored admission: its id and the hash of its content.</summary>
    public static string AnchorKey(string id, string hash) => id + " " + hash;

    private static IReadOnlySet<string> ComputeAnchors(EstateModel model)
    {
        var anchored = new HashSet<string>(StringComparer.Ordinal);
        if (EstateFormat(model) < 2)
        {
            return anchored;
        }

        string? rootOperator = model.CurrentEstate is { } estate ? EstateModel.BodyString(estate, "operator") : null;
        string? firstAuthority = FirstAuthority(model, rootOperator)?.Id;
        foreach (RecordEnvelope migration in model.OfType("migration").Where(m => m.Format >= 2 && m.Signatures.Count > 0))
        {
            if (!migration.Body.TryGetProperty("migrations", out JsonElement steps) || steps.ValueKind != JsonValueKind.Array || !steps.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == "record-format-2"))
            {
                continue;
            }

            if (migration.By != rootOperator && !model.HasScope(migration.By, Scopes.Administer, migration.AtTime))
            {
                continue;
            }

            // The signer's own admission must rest on signatures: verified with no anchors, so that a migration cannot anchor the
            // unsigned admission of the device that signs it.
            var findings = new FindingList();
            foreach (RecordSignature signature in migration.Signatures)
            {
                Verify(model, migration, signature, rootOperator, firstAuthority, findings, NoAnchors);
            }

            if (findings.HasErrors || !migration.Body.TryGetProperty("anchored", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String && entry.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String)
                {
                    anchored.Add(AnchorKey(id.GetString()!, hash.GetString()!));
                }
            }
        }

        return anchored;
    }

    /// <summary>True when the device is currently admitted (and not revoked).</summary>
    public static bool IsAdmitted(EstateModel model, RecordEnvelope device)
    {
        RecordEnvelope? admission = AdmissionOf(model, device);
        return admission is not null && EstateModel.BodyString(admission, "status") == "admitted";
    }

    /// <summary>Whether a device was admitted at a moment.</summary>
    public enum AdmissionState
    {
        /// <summary>No admission record exists for the device.</summary>
        Unadmitted,

        /// <summary>The moment precedes the device's first admission.</summary>
        NotYetAdmitted,

        /// <summary>Admitted at that moment.</summary>
        Admitted,

        /// <summary>Revoked at or before that moment, with no later admission.</summary>
        Revoked,
    }

    /// <summary>
    /// The admission state of a device at a record time, from every admission record about the device in time order:
    /// an admitted record starts admission at its authoring time, a revoked record ends it at revokedAt, and a later
    /// admitted record starts it again. An admission the device signed for itself counts only as the estate's first authority.
    /// </summary>
    public static AdmissionState AdmissionStateAt(EstateModel model, RecordEnvelope device, string at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(device);
        return AdmissionStateAt(model, device, at, Anchored(model));
    }

    private static AdmissionState AdmissionStateAt(EstateModel model, RecordEnvelope device, string at, IReadOnlySet<string> anchored)
    {
        string? rootOperator = model.CurrentEstate is { } estate ? EstateModel.BodyString(estate, "operator") : null;
        string? firstAuthority = FirstAuthority(model, rootOperator)?.Id;
        var events = new List<(string Time, int Order, bool Admitted)>();
        foreach (RecordEnvelope admission in model.Chain(model.Latest(device)).SelectMany(id => model.About(id, "admission")).Where(a => Counts(model, a, anchored)))
        {
            if (admission.Id != firstAuthority && SelfSigned(model, admission))
            {
                continue;
            }

            string? status = EstateModel.BodyString(admission, "status");
            if (status == "admitted")
            {
                events.Add((admission.At, 0, true));
            }
            else if (status == "revoked" && EstateModel.BodyString(admission, "revokedAt") is string revokedAt)
            {
                events.Add((revokedAt, 1, false));
            }
        }

        if (events.Count == 0)
        {
            return AdmissionState.Unadmitted;
        }

        bool? state = null;
        foreach ((string time, int _, bool admitted) in events.OrderBy(e => e.Time, StringComparer.Ordinal).ThenBy(e => e.Order))
        {
            if (string.CompareOrdinal(time, at) > 0)
            {
                break;
            }

            state = admitted;
        }

        return state switch
        {
            null => AdmissionState.NotYetAdmitted,
            true => AdmissionState.Admitted,
            false => AdmissionState.Revoked,
        };
    }

    private static void Verify(EstateModel model, RecordEnvelope record, RecordSignature signature, string? rootOperator, string? firstAuthority, FindingList findings, IReadOnlySet<string> anchored)
    {
        string location = record.ExpectedPath + "#signatures";
        if (signature.Algorithm != RecordSigning.Algorithm)
        {
            findings.Error("signature.algorithm", location, $"Unsupported signature algorithm '{signature.Algorithm}'.");
            return;
        }

        if (!model.ById.TryGetValue(signature.Device, out RecordEnvelope? named) || named.Type != "device")
        {
            findings.Error("signature.device", location, $"Signed by device {signature.Device}, which has no device record.");
            return;
        }

        // A signature names a device's identity and verifies against the key the device had when the record was authored. A record of
        // the device itself (its first record, a rename or a rotation) verifies against its own key or the previous revision's.
        RecordEnvelope device = model.Latest(named);
        IReadOnlyList<string> chain = model.Chain(device);
        string? principal = EstateModel.BodyString(device, "principal");
        if (principal != record.By)
        {
            findings.Error("signature.principal", location, $"Signed by a device of {principal}, but the record is authored by {record.By}.");
            return;
        }

        bool ownRecord = record.Type == "device" && model.Chain(record)[0] == chain[0];
        var keys = new List<string?>();
        if (ownRecord)
        {
            keys.Add(EstateModel.BodyString(record, "publicKey"));
            if (record.Supersedes is not null && model.ById.TryGetValue(record.Supersedes, out RecordEnvelope? previous) && previous.Type == "device")
            {
                keys.Add(EstateModel.BodyString(previous, "publicKey"));
            }
        }
        else
        {
            // Timestamps are whole seconds, so a record dated in the very second of a rotation cannot be ordered against it and
            // verifies with either key; at any other time exactly one revision's key is current.
            for (int i = 0; i < chain.Count; i++)
            {
                bool started = i == 0 || string.CompareOrdinal(model.ById[chain[i]].At, record.At) <= 0;
                bool ended = i + 1 < chain.Count && string.CompareOrdinal(model.ById[chain[i + 1]].At, record.At) < 0;
                if (started && !ended)
                {
                    keys.Add(EstateModel.BodyString(model.ById[chain[i]], "publicKey"));
                }
            }
        }

        byte[] digest = RecordSigning.Digest(record.ComputedHash);
        if (!keys.Any(key => key is not null && RecordSigning.Verify(key, digest, signature.Signature)))
        {
            // A key the device held at another time names the revision, so a key used after its rotation, or a rotation dated
            // earlier than it was made, reads as what it is.
            string? other = ownRecord ? null : chain.FirstOrDefault(id => EstateModel.BodyString(model.ById[id], "publicKey") is string key && !keys.Contains(key) && RecordSigning.Verify(key, digest, signature.Signature));
            if (other is not null)
            {
                findings.Error("signature.key-time", location, $"The signature by device {chain[0]} verifies only with the key of its revision {other}, which was not the device's key at this record's time {record.At}.");
                return;
            }

            findings.Error("signature.invalid", location, $"The signature by device {chain[0]} does not verify against the record's content hash with the key the device held when it was authored.");
            return;
        }

        // A recovery device signs its own device records, admissions and revocations, and nothing else.
        if (EstateModel.BodyString(device, "purpose") == "recovery" && record.Type is not ("device" or "admission"))
        {
            findings.Error("signature.recovery-scope", location, $"Device {chain[0]} is a recovery device, which signs only device records, admissions and revocations.");
            return;
        }

        if (ownRecord)
        {
            return;
        }

        bool selfAdmission = record.Type == "admission" && record.By == rootOperator && IdentityOf(model, record.Subject?.Id) == chain[0];
        if (selfAdmission && record.Id == firstAuthority)
        {
            return;
        }

        if (selfAdmission && AdmissionStateAt(model, device, record.At, anchored) != AdmissionState.Admitted)
        {
            findings.Error("signature.self-admission", location, $"Device {chain[0]} admitted itself for the operator; that is accepted once, for the estate's first device ({firstAuthority}). A later device is admitted under the signature of a device already admitted.");
            return;
        }

        switch (AdmissionStateAt(model, device, record.At, anchored))
        {
            case AdmissionState.Admitted:
                return;
            case AdmissionState.Revoked:
                findings.Error("signature.revoked", location, $"Device {chain[0]} was revoked before this record was authored; records signed by it from the revocation on are invalid.");
                return;
            case AdmissionState.NotYetAdmitted:
                findings.Error("signature.before-admission", location, $"Device {chain[0]} signed this record before it was admitted.");
                return;
            default:
                findings.Error("signature.unadmitted", location, $"Device {chain[0]} is not admitted to this estate; an administrator records an admission first.");
                return;
        }
    }
}
