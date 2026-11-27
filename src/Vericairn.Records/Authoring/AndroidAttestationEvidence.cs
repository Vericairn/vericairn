// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Authoring;

/// <summary>Android presence evidence retained in the signed admission for independent verification.</summary>
/// <param name="Certificates">The supplied DER certificates, leaf first, encoded as base64url.</param>
/// <param name="LeafDigest">SHA-256 of the leaf's DER.</param>
/// <param name="Challenge">The runtime's 32-byte pairing nonce, encoded as base64url.</param>
/// <param name="AttestationSecurityLevel">The security level that produced the attestation.</param>
/// <param name="KeySecurityLevel">The security level that enforces the key's authorizations.</param>
/// <param name="UserAuthType">The hardware-enforced user authentication mask.</param>
/// <param name="DeviceLocked">Whether the bootloader was locked at attestation.</param>
/// <param name="VerifiedBootState">The attested verified boot state.</param>
/// <param name="PackageName">The attested package name.</param>
/// <param name="PackageVersion">The attested package version.</param>
/// <param name="SigningDigests">The app's attested signing certificate digests.</param>
/// <param name="OsVersion">The attested Android OS version.</param>
/// <param name="OsPatchLevel">The OS patch level as YYYYMM.</param>
/// <param name="VendorPatchLevel">The vendor patch level as YYYYMMDD, if present.</param>
/// <param name="BootPatchLevel">The boot patch level as YYYYMMDD, if present.</param>
public sealed record AndroidAttestationEvidence(IReadOnlyList<string> Certificates, string LeafDigest, string Challenge,
    string AttestationSecurityLevel, string KeySecurityLevel, int UserAuthType, bool DeviceLocked, string VerifiedBootState,
    string PackageName, long PackageVersion, IReadOnlyList<string> SigningDigests, int OsVersion, int OsPatchLevel,
    int? VendorPatchLevel, int? BootPatchLevel)
{
    /// <summary>The admission body field that holds evidence obtained during pairing.</summary>
    public const string Field = "androidAttestation";

    /// <summary>The Android application id that creates Remote's keys.</summary>
    public const string RemotePackage = "com.vericairn.remote";

    /// <summary>Maximum number of supplied certificates.</summary>
    public const int MaxCertificates = 8;

    /// <summary>Maximum length of one encoded certificate.</summary>
    public const int MaxCertificateText = 8192;

    /// <summary>Maximum number of attested application signer digests.</summary>
    public const int MaxSigningDigests = 8;

    /// <summary>The verifier established hardware authentication for every signing operation.</summary>
    public bool PerUse { get; } = true;

    /// <summary>Serializes the evidence as a record body field.</summary>
    public JsonElement ToElement() => JsonSerializer.SerializeToElement(this, JsonSerializerOptions.Web);

    internal static void Validate(BodyReader body)
    {
        IReadOnlyList<string>? certs = body.StringArray("certificates", true, allowEmpty: false, maxItemLength: MaxCertificateText);
        if (certs is not null && (certs.Count > MaxCertificates || certs.Any(c => !Base64Url.IsValid(c))))
            body.Error("admission.attestation-certificates", "certificates", $"Supply up to {MaxCertificates} base64url DER certificates, leaf first.");

        body.Hash("leafDigest", true);
        string? challenge = body.Text("challenge", true, 43);
        if (challenge is not null && (challenge.Length != 43 || !Base64Url.IsValid(challenge)))
            body.Error("admission.attestation-challenge", "challenge", "The challenge must be a base64url-encoded 32-byte pairing nonce.");

        body.Enum("attestationSecurityLevel", true, "trusted-environment", "strongbox");
        body.Enum("keySecurityLevel", true, "trusted-environment", "strongbox");
        body.Number("userAuthType", true, 1, 3);
        if (body.Flag("perUse", true) == false)
            body.Error("admission.attestation-per-use", "perUse", "The key must require authentication for every use.");
        if (body.Flag("deviceLocked", true) == false)
            body.Error("admission.attestation-boot", "deviceLocked", "The bootloader must be locked.");
        body.Enum("verifiedBootState", true, "verified");
        body.Enum("packageName", true, RemotePackage);
        body.Number("packageVersion", true, 1, 9_007_199_254_740_991);
        IReadOnlyList<string>? signers = body.StringArray("signingDigests", true, allowEmpty: false, maxItemLength: ContentHash.TextLength);
        if (signers is not null && (signers.Count > MaxSigningDigests || signers.Distinct(StringComparer.Ordinal).Count() != signers.Count
            || signers.Any(digest => !ContentHash.IsValid(digest))))
            body.Error("admission.attestation-signers", "signingDigests", $"Supply up to {MaxSigningDigests} distinct SHA-256 signing certificate digests.");

        body.Number("osVersion", true, 1, 999_999);
        body.Number("osPatchLevel", true, 100_001, 999_912);
        // Optional tags are retained as null when absent in the attestation.
        foreach (string field in new[] { "vendorPatchLevel", "bootPatchLevel" })
        {
            if (body.Element.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Null)
                body.Allow(field);
            else
                body.Number(field, true, 10_000_101, 99_991_231);
        }
        body.NoOtherKeys();
    }
}
