// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Verifies every signature against the device records of the estate, and enforces the estate's signature policy.</summary>
public static class SignatureChecks
{
    private const string RequiredMessage = "This record requires an admitted-device signature, including under an optional charter signature policy.";

    /// <summary>Reports, after all records are indexed, every signature that does not verify and every unsigned record that does not count.</summary>
    public static void Run(EstateModel model, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(findings);
        EstateTrust trust = model.Trust;
        foreach (RecordEnvelope record in model.Records)
        {
            // A record relying on a grant revision that a closing closed counts only when that closing witnesses it (design/signed-history.md § 8).
            if (trust.GrantRefuser(record) is { } closing)
            {
                findings.Add(EstateTrust.AfterGrantClosing(record, closing));
            }

            if (record.Signatures.Count == 0)
            {
                if (record.Type == "admission")
                {
                    if (!trust.AdmissionCounts(record, EstateTrust.Mode.Anchors))
                    {
                        findings.Error("signature.admission-unsigned", record.ExpectedPath, "In an estate at record format 2 an admission is signed: by a device already admitted or, for the operator's first device, by the device it admits. An unsigned one counts only when the signed migration that moved the estate to format 2, or an anchoring history checkpoint, lists it; otherwise, whatever format or date it states, it admits and revokes nothing.");
                    }
                    else if (trust.Required && string.CompareOrdinal(record.At, trust.Charter!.At) > 0 &&
                        !(trust.Adopted && trust.Anchors.Contains(AnchorKey(record.Id, record.ComputedHash))))
                    {
                        // An admission that counts by the admission rule still meets the charter's, as at record format 1.
                        findings.Error("signature.required", record.ExpectedPath, RequiredMessage);
                    }
                }
                else if (trust.Unsigned(record) is { Counts: false, Code: { } code, Message: { } message })
                {
                    findings.Error(code, record.ExpectedPath, message);
                }

                continue;
            }

            foreach (Finding finding in trust.Verification(record, EstateTrust.Mode.Anchors))
            {
                findings.Add(finding);
            }

            RecoveryUse(model, trust, record, findings);
        }

        foreach (RecordEnvelope revision in model.Records.Where(r => r.Type == "estate" && r.Supersedes is not null))
        {
            if (model.ById.TryGetValue(revision.Supersedes!, out RecordEnvelope? revised) && revised.Type == "estate" && !trust.CharterRevisionCounts(revised, revision))
            {
                findings.Error("signature.charter", revision.ExpectedPath, "While the charter it revises requires signatures, a charter revision counts only when the root operator signs it with an admitted device. This one changes nothing: the estate keeps the charter it revises.");
            }
        }
    }

    /// <summary>
    /// Whether a charter revision counts. A charter that requires signatures changes only by the root operator's signed revision: a
    /// revision another principal authored, an unsigned one or one whose signature does not verify counts for nothing, whatever it
    /// says or whatever date it states, so it cannot leave signatures optional again, change who accepts a release or change the root
    /// operator. While a charter leaves signatures optional it protects nothing, and any revision of it counts as before.
    /// </summary>
    public static bool CharterRevisionCounts(EstateModel model, RecordEnvelope revised, RecordEnvelope revision)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(revised);
        ArgumentNullException.ThrowIfNull(revision);
        return model.Trust.CharterRevisionCounts(revised, revision);
    }

    /// <summary>Verifies attribution at authorship, including admission and key history, without changing any record.</summary>
    public static bool IsAuthentic(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        return model.Trust.Authentic(record);
    }

    /// <summary>True when the id names a recovery device, one whose key is kept offline for emergency access.</summary>
    public static bool Recovers(EstateModel model, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Trust.Recovers(deviceId);
    }

    /// <summary>
    /// Every use of a recovery device stays in view until the operator reviews it: a record it signed, other than its own device
    /// records, carries a warning until a current review by an operator names that exact record and is signed by an admitted device
    /// that is not a recovery device. An unsigned review clears nothing, since anyone able to add records can write one in the
    /// operator's name while the charter leaves signatures optional; nor does a review that does not count.
    /// </summary>
    private static void RecoveryUse(EstateModel model, EstateTrust trust, RecordEnvelope record, FindingList findings)
    {
        string? recovery = record.Signatures.Select(s => IdentityOf(model, s.Device)).FirstOrDefault(trust.Recovers);
        if (recovery is null || (record.Type == "device" && model.Chain(record)[0] == recovery))
        {
            return;
        }

        bool reviewed = model.About(record.Id, "review").Any(review =>
            model.IsCurrent(review) && model.HasRoleAt(review.By, Roles.Operator, review) && review.Subject?.Hash == record.ComputedHash &&
            !review.Signatures.Any(s => trust.Recovers(s.Device)) && trust.Authentic(review));
        if (!reviewed)
        {
            findings.Warning("recovery.unreviewed", record.ExpectedPath, $"Signed by recovery device {recovery}: emergency access stays in view until an operator records a review of this record that an admitted device other than a recovery device signs.");
        }
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
        return model.Trust.SelfSigned(admission);
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
        if (rootOperator is null) return null;
        EstateTrust trust = model.Trust;
        if (rootOperator == trust.RootOperator) return trust.FirstAuthority;
        return model.Records.Where(a => a.Type == "admission" && a.By == rootOperator && trust.SelfSigned(a) && !trust.Recovers(a.Subject?.Id))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The current admission of a device: the latest admission record about any revision of the device that counts.</summary>
    public static RecordEnvelope? AdmissionOf(EstateModel model, RecordEnvelope device)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(device);
        return model.Trust.AdmissionOf(device.Id);
    }

    /// <summary>
    /// Whether a record can admit or revoke. In an estate still at record format 1 an unsigned admission counts while the charter leaves
    /// signatures optional, as format 1 has it. Once the estate is at record format 2 an unsigned admission counts only when the signed
    /// migration that moved the estate lists it, or an anchoring history checkpoint does (<see cref="Anchored"/>), whatever format or
    /// date the admission states: a format and a date are what the record's author wrote, and only a signature is something an author
    /// without an admitted key cannot write. A signed admission counts only when its signatures verify. Every other record counts here.
    /// </summary>
    public static bool Counts(EstateModel model, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(record);
        return record.Type != "admission" || model.Trust.AdmissionCounts(record, EstateTrust.Mode.Anchors);
    }

    /// <summary>
    /// The unsigned records an estate keeps counting because they are anchored, as id and hash keys: those listed by a migration record
    /// that names the step record-format-2, and those listed by an anchoring history checkpoint, each signed by the root operator, or by
    /// a principal with the 'administer' scope, with devices whose admissions rest on signed admissions alone. A list in an unsigned
    /// record, in one by another principal, or in one signed by a device that only an unsigned admission admitted, anchors nothing.
    /// </summary>
    public static IReadOnlySet<string> Anchored(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Trust.Anchors;
    }

    /// <summary>The key of an anchored record: its id and the hash of its content.</summary>
    public static string AnchorKey(string id, string hash) => id + " " + hash;

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
        return model.Trust.AdmissionStateAt(device.Id, at, EstateTrust.Mode.Anchors);
    }
}
