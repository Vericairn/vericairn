// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

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
                Verify(model, record, signature, rootOperator, findings);
            }
        }
    }

    /// <summary>The current admission of a device: the latest admission record about any revision of the device.</summary>
    public static RecordEnvelope? AdmissionOf(EstateModel model, RecordEnvelope device)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(device);
        IReadOnlyList<string> chain = model.Chain(device);
        return chain.SelectMany(id => model.About(id, "admission")).Where(model.IsCurrent).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>True when the device is currently admitted (and not revoked).</summary>
    public static bool IsAdmitted(EstateModel model, RecordEnvelope device)
    {
        RecordEnvelope? admission = AdmissionOf(model, device);
        return admission is not null && EstateModel.BodyString(admission, "status") == "admitted";
    }

    private static void Verify(EstateModel model, RecordEnvelope record, RecordSignature signature, string? rootOperator, FindingList findings)
    {
        string location = record.ExpectedPath + "#signatures";
        if (signature.Algorithm != RecordSigning.Algorithm)
        {
            findings.Error("signature.algorithm", location, $"Unsupported signature algorithm '{signature.Algorithm}'.");
            return;
        }

        if (!model.ById.TryGetValue(signature.Device, out RecordEnvelope? device) || device.Type != "device")
        {
            findings.Error("signature.device", location, $"Signed by device {signature.Device}, which has no device record.");
            return;
        }

        string? principal = EstateModel.BodyString(device, "principal");
        if (principal != record.By)
        {
            findings.Error("signature.principal", location, $"Signed by a device of {principal}, but the record is authored by {record.By}.");
            return;
        }

        string? publicKey = EstateModel.BodyString(device, "publicKey");
        if (publicKey is null || !RecordSigning.Verify(publicKey, RecordSigning.Digest(record.ComputedHash), signature.Signature))
        {
            findings.Error("signature.invalid", location, $"The signature by device {device.Id} does not verify against the record's content hash.");
            return;
        }

        if (device.Id == record.Id)
        {
            return;
        }

        RecordEnvelope? admission = AdmissionOf(model, device);
        bool selfAdmission = record.Type == "admission" && record.Subject?.Id == device.Id && record.By == rootOperator;
        if (admission is null)
        {
            if (!selfAdmission)
            {
                findings.Error("signature.unadmitted", location, $"Device {device.Id} is not admitted to this estate; an administrator records an admission first.");
            }

            return;
        }

        string? status = EstateModel.BodyString(admission, "status");
        string? revokedAt = EstateModel.BodyString(admission, "revokedAt");
        if (status == "revoked" && revokedAt is not null && string.CompareOrdinal(record.At, revokedAt) >= 0)
        {
            findings.Error("signature.revoked", location, $"Device {device.Id} was revoked at {revokedAt}; records signed by it from then on are invalid.");
            return;
        }

        RecordEnvelope first = model.ById.TryGetValue(model.Chain(admission)[0], out RecordEnvelope? f) ? f : admission;
        if (string.CompareOrdinal(record.At, first.At) < 0 && !selfAdmission && record.Id != first.Id)
        {
            findings.Error("signature.before-admission", location, $"Device {device.Id} signed this record before it was admitted at {first.At}.");
        }
    }
}
