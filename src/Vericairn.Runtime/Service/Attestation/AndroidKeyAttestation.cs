// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Runtime.Service;

/// <summary>Verifies a key's Android Keystore attestation against the pinned Google trust anchors.</summary>
internal sealed class AndroidKeyAttestation(X509Certificate2Collection roots, TimeProvider clock, Func<IReadOnlySet<string>?> revokedSerials,
    Func<X509Certificate2Collection, DateTimeOffset, bool>? fixtureChain = null)
{
    internal const string KeyDescriptionOid = "1.3.6.1.4.1.11129.2.1.17";
    private const string ProvisioningInfoOid = "1.3.6.1.4.1.11129.2.1.30";
    private const int MaxCertificates = 8;
    private const int MaxCertificateText = 8192;

    internal static AndroidKeyAttestation Google { get; } = new(AndroidAttestationRoots.Load(), TimeProvider.System, AndroidAttestationStatus.Get);

    internal bool Verify(IReadOnlyList<string> certificates, string challenge, string publicKey)
    {
        if (certificates.Count is < 1 or > MaxCertificates) return false;
        var supplied = new X509Certificate2Collection();
        try
        {
            foreach (string text in certificates)
            {
                if (text is not { Length: > 0 and <= MaxCertificateText } || !Base64Url.IsValid(text)) return false;
                supplied.Add(X509CertificateLoader.LoadCertificate(Base64Url.DecodeFromChars(text)));
            }

            using var chain = new X509Chain();
            X509Certificate2[] built;
            DateTimeOffset now = clock.GetUtcNow();
            if (fixtureChain is null)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                chain.ChainPolicy.ExtraStore.AddRange(supplied);
                chain.ChainPolicy.DisableCertificateDownloads = true;
                // Android publishes a JSON status list, independently of the X.509 platform chain engine.
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime = now.UtcDateTime;
                if (!chain.Build(supplied[0])) return false;
                built = chain.ChainElements.Cast<X509ChainElement>().Select(element => element.Certificate).ToArray();
            }
            else
            {
                if (!fixtureChain(supplied, now)) return false;
                built = supplied.Cast<X509Certificate2>().ToArray();
            }
            if (built.Length < 2) return false;
            if (!roots.Cast<X509Certificate2>().Any(root => root.RawData.AsSpan().SequenceEqual(built[^1].RawData))) return false;
            if (supplied.Count > built.Length || supplied.Cast<X509Certificate2>().Where((certificate, index) => !certificate.RawData.AsSpan().SequenceEqual(built[index].RawData)).Any()) return false;

            // Only the attestation extension nearest the root is trusted. This protocol attests the leaf key, so an
            // extension on a higher certificate must never be used to authenticate a different leaf public key.
            if (built.Skip(1).Any(certificate => certificate.Extensions[KeyDescriptionOid] is not null)) return false;
            int provisioning = Array.FindLastIndex(built, certificate => certificate.Extensions[ProvisioningInfoOid] is not null);
            if (provisioning >= 0 && provisioning != 1) return false;
            X509Extension? description = built[0].Extensions[KeyDescriptionOid];
            using ECDsa? key = built[0].GetECDsaPublicKey();
            if (description is null || key is null || !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(Base64Url.DecodeFromChars(publicKey))
                || !RequiresPresence(description.RawData, Base64Url.DecodeFromChars(challenge))) return false;

            IReadOnlySet<string>? revoked = revokedSerials();
            return revoked is not null && !built.Any(certificate => revoked.Contains(certificate.SerialNumber.TrimStart('0')));
        }
        catch (Exception error) when (error is CryptographicException or AsnContentException or FormatException or ArgumentException)
        {
            return false;
        }
        finally
        {
            foreach (X509Certificate2 certificate in supplied) certificate.Dispose();
        }
    }

    private static bool RequiresPresence(byte[] description, byte[] challenge)
    {
        var reader = new AsnReader(description, AsnEncodingRules.DER);
        AsnReader keyDescription = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        if (keyDescription.ReadInteger() < 1 || !Hardware(keyDescription.ReadEnumeratedValue<SecurityLevel>())
            || keyDescription.ReadInteger() < 1 || !Hardware(keyDescription.ReadEnumeratedValue<SecurityLevel>())) return false;
        if (!CryptographicOperations.FixedTimeEquals(keyDescription.ReadOctetString(), challenge)) return false;
        _ = keyDescription.ReadOctetString(); // uniqueId, not used or retained
        var software = ReadAuthorizations(keyDescription.ReadSequence());
        var hardware = ReadAuthorizations(keyDescription.ReadSequence());
        keyDescription.ThrowIfNotEmpty();

        // KeyMint defines per-operation use by hardware-enforced userAuthType and the absence of authTimeout.
        // Tag 506 is the legacy on-body extension of a timed authorization; it must also be absent.
        int[] contradictsPerUse = [503, 505, 506];
        return !contradictsPerUse.Any(tag => software.ContainsKey(tag) || hardware.ContainsKey(tag))
            && !software.ContainsKey(504)
            && ReadSmallInteger(hardware, 504, out int authentication) && authentication is 1 or 2 or 3
            && ReadSmallInteger(hardware, 702, out int origin) && origin == 0; // Generated, not imported.
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

    private static bool ReadSmallInteger(Dictionary<int, ReadOnlyMemory<byte>> list, int tag, out int value)
    {
        value = 0;
        if (!list.TryGetValue(tag, out ReadOnlyMemory<byte> encoded)) return false;
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        AsnReader wrapper = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, tag, isConstructed: true));
        if (!wrapper.TryReadInt32(out value)) return false;
        wrapper.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return true;
    }

    private static bool Hardware(SecurityLevel level) => level is SecurityLevel.TrustedEnvironment or SecurityLevel.StrongBox;

    private enum SecurityLevel { Software = 0, TrustedEnvironment = 1, StrongBox = 2 }
}
