// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vericairn.Records.Authoring;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Service;

/// <summary>Verifies a key's Android Keystore attestation against the pinned Google trust anchors.</summary>
internal sealed class AndroidKeyAttestation(X509Certificate2Collection roots, TimeProvider clock, Func<IReadOnlySet<string>?> revokedSerials)
{
    internal const string KeyDescriptionOid = "1.3.6.1.4.1.11129.2.1.17";
    private const string ProvisioningInfoOid = "1.3.6.1.4.1.11129.2.1.30";
    private static readonly int[] ContradictsPerUse = [503, 505, 506];
    private static readonly int[] DirectDeviceIdentifiers = [713, 714, 715, 720, 723];
    private static readonly byte[] RemotePackageUtf8 = Encoding.UTF8.GetBytes(AndroidAttestationEvidence.RemotePackage);

    internal static AndroidKeyAttestation Google { get; } = new(AndroidAttestationRoots.Load(), TimeProvider.System, AndroidAttestationStatus.Get);

    internal AndroidAttestationEvidence? Verify(IReadOnlyList<string> certificates, string challenge, string publicKey, DateTimeOffset? at = null)
    {
        if (certificates.Count is < 1 or > AndroidAttestationEvidence.MaxCertificates
            || challenge.Length != 43 || !Base64Url.IsValid(challenge)) return null;
        byte[] nonce = Base64Url.DecodeFromChars(challenge);
        if (nonce.Length != 32 || challenge != Base64Url.EncodeToString(nonce)) return null;

        var supplied = new X509Certificate2Collection();
        try
        {
            foreach (string text in certificates)
            {
                if (text is not { Length: > 0 and <= AndroidAttestationEvidence.MaxCertificateText } || !Base64Url.IsValid(text)) return null;
                X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(Base64Url.DecodeFromChars(text));
                supplied.Add(certificate);
                if (text != Base64Url.EncodeToString(certificate.RawData)) return null;
            }

            X509Certificate2 leaf = supplied[0];
            X509Extension? description = leaf.Extensions[KeyDescriptionOid];
            if (description is null || publicKey != Base64Url.EncodeToString(leaf.PublicKey.ExportSubjectPublicKeyInfo())) return null;
            using ECDsa? key = leaf.GetECDsaPublicKey();
            if (key is null || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) return null;

            AndroidAttestationEvidence? evidence = ReadEvidence(certificates, leaf, description.RawData, nonce, challenge);
            return evidence is not null && ChainIsTrusted(supplied, at ?? clock.GetUtcNow()) ? evidence : null;
        }
        catch (Exception error) when (error is CryptographicException or AsnContentException or FormatException or ArgumentException)
        {
            return null;
        }
        finally
        {
            foreach (X509Certificate2 certificate in supplied) certificate.Dispose();
        }
    }

    internal bool ChainIsTrusted(X509Certificate2Collection supplied, DateTimeOffset at)
    {
        if (supplied.Count is < 1 or > AndroidAttestationEvidence.MaxCertificates) return false;
        X509Certificate2? suppliedRoot = supplied.Count > 1
            && supplied[^1].SubjectName.RawData.AsSpan().SequenceEqual(supplied[^1].IssuerName.RawData)
            && roots.Cast<X509Certificate2>().Any(root => SameKey(root, supplied[^1]))
            ? supplied[^1] : null;
        int nonRoots = supplied.Count - (suppliedRoot is null ? 0 : 1);

        // Use only the canonical pinned certificates as trust anchors. The caller may omit the root
        // or supply an older certificate over the same pinned key. Never use that copy to build a path.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        for (int index = 1; index < nonRoots; index++) chain.ChainPolicy.ExtraStore.Add(supplied[index]);
        chain.ChainPolicy.DisableCertificateDownloads = true;
        // Google publishes a JSON status list independently of platform X.509 revocation.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = at.UtcDateTime;
        if (!chain.Build(supplied[0])) return false;

        X509Certificate2[] built = chain.ChainElements.Cast<X509ChainElement>().Select(element => element.Certificate).ToArray();
        if (built.Length != nonRoots + 1) return false;
        X509Certificate2 anchor = built[^1];
        if (!roots.Cast<X509Certificate2>().Any(root => SameKey(root, anchor))
            || (suppliedRoot is not null && !SameKey(anchor, suppliedRoot))) return false;
        for (int index = 0; index < nonRoots; index++)
        {
            if (!supplied[index].RawData.AsSpan().SequenceEqual(built[index].RawData)) return false;
        }

        // Only the attestation extension nearest the root is trustworthy. This protocol attests
        // the leaf key, never a descendant of a certificate that already claims an attested key.
        if (built.Skip(1).Any(certificate => certificate.Extensions[KeyDescriptionOid] is not null)) return false;
        int provisioning = Array.FindLastIndex(built, certificate => certificate.Extensions[ProvisioningInfoOid] is not null);
        if (provisioning >= 0 && provisioning != 1) return false;

        IReadOnlySet<string>? revoked = revokedSerials();
        return revoked is not null
            && !built.Any(certificate => revoked.Contains(Serial(certificate)))
            && (suppliedRoot is null || !revoked.Contains(Serial(suppliedRoot)));
    }

    private static bool SameKey(X509Certificate2 left, X509Certificate2 right) =>
        left.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(right.PublicKey.ExportSubjectPublicKeyInfo());

    private static string Serial(X509Certificate2 certificate) => certificate.SerialNumber.TrimStart('0');

    // Parsing claims alone establishes no trust. Verify also validates the key, X.509 path and revocation.
    internal static AndroidAttestationEvidence? ReadEvidence(IReadOnlyList<string> certificates, X509Certificate2 leaf, byte[] description, byte[] nonce, string challenge)
    {
        var reader = new AsnReader(description, AsnEncodingRules.DER);
        AsnReader keyDescription = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        if (keyDescription.ReadInteger() < 1) return null;
        string? attestationLevel = HardwareLevel(keyDescription.ReadEnumeratedValue<SecurityLevel>());
        if (attestationLevel is null || keyDescription.ReadInteger() < 1) return null;
        string? keyLevel = HardwareLevel(keyDescription.ReadEnumeratedValue<SecurityLevel>());
        if (keyLevel is null || !CryptographicOperations.FixedTimeEquals(keyDescription.ReadOctetString(), nonce)) return null;
        // Remote never asks Android for device identifiers. Don't put them in retained evidence.
        if (keyDescription.ReadOctetString().Length != 0) return null; // uniqueId
        var software = ReadAuthorizations(keyDescription.ReadSequence());
        var hardware = ReadAuthorizations(keyDescription.ReadSequence());
        keyDescription.ThrowIfNotEmpty();

        // KeyMint defines per-operation use by hardware-enforced userAuthType and the absence of authTimeout.
        // Tag 506 extends timed authorization while worn and also contradicts per-use authentication.
        int? authentication = ReadInteger(hardware, 504);
        if (ContradictsPerUse.Any(tag => software.ContainsKey(tag) || hardware.ContainsKey(tag))
            || DirectDeviceIdentifiers.Any(tag => software.ContainsKey(tag) || hardware.ContainsKey(tag))
            || software.ContainsKey(504) || authentication is not (>= 1 and <= 3)
            || ReadInteger(hardware, 702) != 0 || !VerifiedBoot(software, hardware)) return null;

        // Android places the package and its signature hashes in softwareEnforced. Verify the boot state
        // first so this assertion comes from the verified Android platform.
        if (hardware.ContainsKey(709) || !ReadApplication(software, out long packageVersion, out string[] signingDigests)) return null;

        int? osVersion = ReadInteger(hardware, 705);
        int? osPatch = ReadInteger(hardware, 706);
        int? vendorPatch = ReadInteger(hardware, 718);
        int? bootPatch = ReadInteger(hardware, 719);
        if (osVersion is not (>= 110000 and <= 999999) || osPatch is not int month || !ValidPatchMonth(month)
            || (vendorPatch is int vendor && !ValidPatchDate(vendor))
            || (bootPatch is int boot && !ValidPatchDate(boot))
            || software.ContainsKey(705) || software.ContainsKey(706) || software.ContainsKey(718) || software.ContainsKey(719)) return null;

        return new AndroidAttestationEvidence(certificates.ToArray(), ContentHash.Sha256(leaf.RawData), challenge, attestationLevel, keyLevel,
            authentication.Value, true, "verified", AndroidAttestationEvidence.RemotePackage, packageVersion, signingDigests, osVersion.Value, month, vendorPatch, bootPatch);
    }

    private static bool VerifiedBoot(Dictionary<int, ReadOnlyMemory<byte>> software, Dictionary<int, ReadOnlyMemory<byte>> hardware)
    {
        if (software.ContainsKey(704) || !hardware.TryGetValue(704, out var encoded)) return false;
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        AsnReader wrapper = reader.ReadSequence(Explicit(704));
        AsnReader root = wrapper.ReadSequence();
        if (root.ReadOctetString().Length == 0 || !root.ReadBoolean() || root.ReadEnumeratedValue<BootState>() != BootState.Verified) return false;
        if (root.HasData && root.ReadOctetString().Length == 0) return false;
        root.ThrowIfNotEmpty();
        wrapper.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return true;
    }

    private static bool ReadApplication(Dictionary<int, ReadOnlyMemory<byte>> software, out long packageVersion, out string[] signingDigests)
    {
        packageVersion = 0;
        signingDigests = [];
        if (!software.TryGetValue(709, out var encoded)) return false;
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        AsnReader wrapper = reader.ReadSequence(Explicit(709));
        var embedded = new AsnReader(wrapper.ReadOctetString(), AsnEncodingRules.DER);
        AsnReader application = embedded.ReadSequence();
        AsnReader packages = application.ReadSetOf();
        AsnReader package = packages.ReadSequence();
        if (!package.ReadOctetString().AsSpan().SequenceEqual(RemotePackageUtf8)
            || !package.TryReadInt64(out packageVersion) || packageVersion is < 1 or > 9_007_199_254_740_991) return false;
        package.ThrowIfNotEmpty();
        packages.ThrowIfNotEmpty();

        AsnReader signatures = application.ReadSetOf();
        var digests = new SortedSet<string>(StringComparer.Ordinal);
        while (signatures.HasData)
        {
            byte[] digest = signatures.ReadOctetString();
            if (digest.Length != 32 || digests.Count == AndroidAttestationEvidence.MaxSigningDigests || !digests.Add(Sha256Label(digest))) return false;
        }
        application.ThrowIfNotEmpty();
        embedded.ThrowIfNotEmpty();
        wrapper.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        signingDigests = digests.ToArray();
        return signingDigests.Length > 0;
    }

    private static Dictionary<int, ReadOnlyMemory<byte>> ReadAuthorizations(AsnReader authorizations)
    {
        var values = new Dictionary<int, ReadOnlyMemory<byte>>();
        while (authorizations.HasData)
        {
            Asn1Tag tag = authorizations.PeekTag();
            if (tag.TagClass != TagClass.ContextSpecific || !tag.IsConstructed || !values.TryAdd(tag.TagValue, authorizations.ReadEncodedValue()))
                throw new AsnContentException("An Android authorization must have a unique, explicit tag.");
        }
        return values;
    }

    private static int? ReadInteger(Dictionary<int, ReadOnlyMemory<byte>> list, int tag)
    {
        if (!list.TryGetValue(tag, out ReadOnlyMemory<byte> encoded)) return null;
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        AsnReader wrapper = reader.ReadSequence(Explicit(tag));
        if (!wrapper.TryReadInt32(out int value)) throw new AsnContentException("The Android authorization integer is out of range.");
        wrapper.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return value;
    }

    private static Asn1Tag Explicit(int tag) => new(TagClass.ContextSpecific, tag, isConstructed: true);

    private static bool ValidPatchMonth(int patch) => patch is >= 100001 and <= 999912 && patch % 100 is >= 1 and <= 12;

    private static bool ValidPatchDate(int patch)
    {
        int month = patch / 100;
        return ValidPatchMonth(month) && patch % 100 >= 1 && patch % 100 <= DateTime.DaysInMonth(month / 100, month % 100);
    }

    private static string Sha256Label(ReadOnlySpan<byte> hash) => "sha256:" + Convert.ToHexStringLower(hash);

    private static string? HardwareLevel(SecurityLevel level) => level switch
    {
        SecurityLevel.TrustedEnvironment => "trusted-environment",
        SecurityLevel.StrongBox => "strongbox",
        _ => null,
    };

    private enum SecurityLevel { Software = 0, TrustedEnvironment = 1, StrongBox = 2 }
    private enum BootState { Verified = 0, SelfSigned = 1, Unverified = 2, Failed = 3 }
}
