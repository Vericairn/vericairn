// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Signing;

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
        foreach (RecordEnvelope record in model.Records)
        {
            if (record.Signatures.Count == 0)
            {
                if (required && string.CompareOrdinal(record.At, estate!.At) > 0)
                {
                    findings.Error("signature.required", record.ExpectedPath, "This estate requires signed records; author it on an admitted device.");
                }

                continue;
            }

            foreach (RecordSignature signature in record.Signatures)
            {
                Verify(model, record, signature, rootOperator, firstAuthority, findings);
            }

            Rotation(model, record, findings);
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
    /// add records can mint a device for the operator and sign as the operator with it.
    /// </summary>
    public static RecordEnvelope? FirstAuthority(EstateModel model, string? rootOperator)
    {
        ArgumentNullException.ThrowIfNull(model);
        return rootOperator is null ? null : model.OfType("admission")
            .Where(a => a.By == rootOperator && SelfSigned(model, a))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The current admission of a device: the latest admission record about any revision of the device.</summary>
    public static RecordEnvelope? AdmissionOf(EstateModel model, RecordEnvelope device)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(device);
        IReadOnlyList<string> chain = model.Chain(model.Latest(device));
        return chain.SelectMany(id => model.About(id, "admission")).Where(model.IsCurrent).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
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
        string? rootOperator = model.CurrentEstate is { } estate ? EstateModel.BodyString(estate, "operator") : null;
        string? firstAuthority = FirstAuthority(model, rootOperator)?.Id;
        var events = new List<(string Time, int Order, bool Admitted)>();
        foreach (RecordEnvelope admission in model.Chain(model.Latest(device)).SelectMany(id => model.About(id, "admission")))
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

    private static void Verify(EstateModel model, RecordEnvelope record, RecordSignature signature, string? rootOperator, string? firstAuthority, FindingList findings)
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

        if (ownRecord)
        {
            return;
        }

        bool selfAdmission = record.Type == "admission" && record.By == rootOperator && IdentityOf(model, record.Subject?.Id) == chain[0];
        if (selfAdmission && record.Id == firstAuthority)
        {
            return;
        }

        if (selfAdmission && AdmissionStateAt(model, device, record.At) != AdmissionState.Admitted)
        {
            findings.Error("signature.self-admission", location, $"Device {chain[0]} admitted itself for the operator; that is accepted once, for the estate's first device ({firstAuthority}). A later device is admitted under the signature of a device already admitted.");
            return;
        }

        switch (AdmissionStateAt(model, device, record.At))
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
