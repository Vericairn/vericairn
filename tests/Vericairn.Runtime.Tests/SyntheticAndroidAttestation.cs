// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>A fixture authority for test-only Android chains, with no access to platform keys or Google.</summary>
public sealed class SyntheticAndroidAttestation : IDisposable
{
    private static readonly DateTimeOffset From = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2044, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly ECDsa issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly X509Certificate2 root;

    public SyntheticAndroidAttestation()
    {
        var subject = new X500DistinguishedName("CN=Vericairn Test Android Attestation Root");
        var request = new CertificateRequest(subject, issuer, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        root = request.Create(subject, X509SignatureGenerator.CreateForECDsa(issuer), From, To, [1]);
    }

    internal AndroidKeyAttestation Verifier(Func<IReadOnlySet<string>?>? revocations = null, bool nativeTrust = false) =>
        new(new X509Certificate2Collection(root), TimeProvider.System, revocations ?? (() => new HashSet<string>()), nativeTrust ? null : CheckFixtureChain);

    // The two-certificate fixture chains have only P-256 and SHA-256. Exercise those signatures even in a sandbox without SecTrust.
    internal static bool CheckFixtureChain(X509Certificate2Collection chain, DateTimeOffset now)
    {
        if (chain.Count != 2 || chain.Cast<X509Certificate2>().Any(c => c.NotBefore.ToUniversalTime() > now || c.NotAfter.ToUniversalTime() < now)) return false;
        using ECDsa? signer = chain[1].GetECDsaPublicKey();
        if (signer is null) return false;
        var reader = new AsnReader(chain[0].RawData, AsnEncodingRules.DER);
        AsnReader certificate = reader.ReadSequence();
        ReadOnlyMemory<byte> signed = certificate.ReadEncodedValue();
        AsnReader algorithm = certificate.ReadSequence();
        if (algorithm.ReadObjectIdentifier() != "1.2.840.10045.4.3.2" || algorithm.HasData) return false;
        ReadOnlyMemory<byte> signature = certificate.ReadBitString(out int unused);
        return unused == 0 && !certificate.HasData && !reader.HasData
            && signer.VerifyData(signed.Span, signature.Span, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>Uses this test root and an empty recorded status list for a test runtime.</summary>
    public void UseFor(EstateService service) => service.PairingAttestation = Verifier(nativeTrust: true);

    /// <summary>Issues a synthetic certificate for a fixture public key and challenge.</summary>
    public IReadOnlyList<string> Issue(byte[] key, byte[] nonce, string policy = "per-use", bool expired = false)
    {
        using ECDsa leafKey = ECDsa.Create();
        leafKey.ImportSubjectPublicKeyInfo(key, out _);
        var request = new CertificateRequest("CN=Vericairn Test Android Key", leafKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509Extension(AndroidKeyAttestation.KeyDescriptionOid, Description(nonce, policy), false));
        using X509Certificate2 leaf = request.Create(root.SubjectName, X509SignatureGenerator.CreateForECDsa(issuer), From, expired ? From.AddDays(1) : To, [2]);
        return [Base64Url.EncodeToString(leaf.RawData), Base64Url.EncodeToString(root.RawData)];
    }

    public void Dispose()
    {
        root.Dispose();
        issuer.Dispose();
    }

    private static byte[] Description(byte[] nonce, string policy)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(300);
            writer.WriteEnumeratedValue(policy == "software" ? SecurityLevel.Software : SecurityLevel.TrustedEnvironment);
            writer.WriteInteger(300);
            writer.WriteEnumeratedValue(policy == "software" ? SecurityLevel.Software : SecurityLevel.TrustedEnvironment);
            writer.WriteOctetString(nonce);
            writer.WriteOctetString([]);
            using (writer.PushSequence())
            {
                if (policy == "software-auth") Integer(writer, 504, 3);
            }
            using (writer.PushSequence())
            {
                if (policy == "no-auth")
                {
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 503, isConstructed: true))) writer.WriteNull();
                }
                if (policy is not ("no-auth" or "software-auth")) Integer(writer, 504, 3);
                if (policy == "timed") Integer(writer, 505, 30);
                Integer(writer, 702, 0);
            }
        }
        return writer.Encode();
    }

    private static void Integer(AsnWriter writer, int tag, int value)
    {
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, tag, isConstructed: true))) writer.WriteInteger(value);
    }

    private enum SecurityLevel { Software = 0, TrustedEnvironment = 1 }
}
