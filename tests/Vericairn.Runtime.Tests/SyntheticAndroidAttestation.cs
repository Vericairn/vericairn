// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>A fixture authority for test-only Android chains, with no access to platform keys or Google.</summary>
public sealed class SyntheticAndroidAttestation : IDisposable
{
    private static readonly DateTimeOffset From = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2044, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly ECDsa issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly X500DistinguishedName subject = new("CN=Vericairn Test Android Attestation Root");
    private readonly X509Certificate2 root;

    public SyntheticAndroidAttestation()
    {
        root = Root(1);
    }

    internal AndroidKeyAttestation Verifier(Func<IReadOnlySet<string>?>? revocations = null) =>
        new(new X509Certificate2Collection(root), TimeProvider.System, revocations ?? (() => new HashSet<string>()));

    /// <summary>Uses this test root and an empty recorded status list for a test runtime.</summary>
    public void UseFor(EstateService service)
    {
        RequireNativeTrust();
        service.PairingAttestation = Verifier();
    }

    /// <summary>Checks whether the environment supports a custom-root path with the native certificate verifier.</summary>
    public static void RequireNativeTrust()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var authority = new SyntheticAndroidAttestation();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string leafText = authority.Issue(key.ExportSubjectPublicKeyInfo(), new byte[32])[0];
        using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(Base64Url.DecodeFromChars(leafText));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority.root);
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try
        {
            Assert.True(chain.Build(leaf));
        }
        catch (CryptographicException error) when (error.GetType().Name == "AppleCommonCryptoCryptographicException")
        {
            Assert.Skip("macOS SecTrust is unavailable in this sandbox.");
        }
    }

    /// <summary>Issues a synthetic certificate for a fixture public key and challenge.</summary>
    public IReadOnlyList<string> Issue(byte[] key, byte[] nonce, string policy = "per-use", bool expired = false, bool rootVariant = false)
    {
        using ECDsa leafKey = ECDsa.Create();
        leafKey.ImportSubjectPublicKeyInfo(key, out _);
        var request = new CertificateRequest("CN=Vericairn Test Android Key", leafKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509Extension(AndroidKeyAttestation.KeyDescriptionOid, Description(nonce, policy), false));
        using X509Certificate2 leaf = request.Create(root.SubjectName, X509SignatureGenerator.CreateForECDsa(issuer), From, expired ? From.AddDays(1) : To, [2]);
        using X509Certificate2? variant = rootVariant ? Root(3) : null;
        return [Base64Url.EncodeToString(leaf.RawData), Base64Url.EncodeToString((variant ?? root).RawData)];
    }

    public void Dispose()
    {
        root.Dispose();
        issuer.Dispose();
    }

    private X509Certificate2 Root(byte serial)
    {
        var request = new CertificateRequest(subject, issuer, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.Create(subject, X509SignatureGenerator.CreateForECDsa(issuer), From, To, [serial]);
    }

    private static byte[] Description(byte[] nonce, string policy)
    {
        SecurityLevel level = policy switch
        {
            "software" => SecurityLevel.Software,
            "strongbox" => SecurityLevel.StrongBox,
            _ => SecurityLevel.TrustedEnvironment,
        };
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(300);
            writer.WriteEnumeratedValue(level);
            writer.WriteInteger(300);
            writer.WriteEnumeratedValue(level);
            writer.WriteOctetString(nonce);
            writer.WriteOctetString(policy == "unique-id" ? SHA256.HashData("fixture identifier"u8) : []);
            using (writer.PushSequence())
            {
                if (policy == "software-auth") Integer(writer, 504, 3);
                if (policy == "software-boot") RootOfTrust(writer, policy);
                if (policy != "missing-app") AttestationApplicationId(writer, policy);
            }
            using (writer.PushSequence())
            {
                if (policy == "no-auth")
                {
                    using (writer.PushSequence(Explicit(503))) writer.WriteNull();
                }
                if (policy is not ("no-auth" or "software-auth")) Integer(writer, 504, 3);
                if (policy == "timed") Integer(writer, 505, 30);
                Integer(writer, 702, 0);
                if (policy is not ("missing-boot" or "software-boot")) RootOfTrust(writer, policy);
                Integer(writer, 705, policy == "old-os" ? 100000 : 140000);
                if (policy != "missing-patch") Integer(writer, 706, policy == "bad-patch" ? 202413 : 202411);
                if (policy == "device-id")
                {
                    using (writer.PushSequence(Explicit(713))) writer.WriteOctetString("fixture serial"u8);
                }
                Integer(writer, 718, policy == "bad-vendor-patch" ? 20241131 : 20241105);
                Integer(writer, 719, policy == "bad-boot-patch" ? 20241100 : 20241105);
            }
        }
        return writer.Encode();
    }

    private static void RootOfTrust(AsnWriter writer, string policy)
    {
        using (writer.PushSequence(Explicit(704)))
        using (writer.PushSequence())
        {
            writer.WriteOctetString(SHA256.HashData("fixture boot key"u8));
            writer.WriteBoolean(policy != "unlocked");
            writer.WriteEnumeratedValue(policy == "unverified-boot" ? BootState.Unverified : BootState.Verified);
            writer.WriteOctetString(SHA256.HashData("fixture boot hash"u8));
        }
    }

    private static void AttestationApplicationId(AsnWriter writer, string policy)
    {
        var embedded = new AsnWriter(AsnEncodingRules.DER);
        using (embedded.PushSequence())
        {
            using (embedded.PushSetOf())
            using (embedded.PushSequence())
            {
                string package = policy == "wrong-app" ? "org.example.other" : "com.vericairn.remote";
                embedded.WriteOctetString(Encoding.UTF8.GetBytes(package));
                embedded.WriteInteger(1);
            }
            using (embedded.PushSetOf())
            {
                embedded.WriteOctetString(policy == "bad-app-signer" ? [1, 2, 3] : SHA256.HashData("fixture app signer"u8));
            }
        }
        using (writer.PushSequence(Explicit(709))) writer.WriteOctetString(embedded.Encode());
    }

    private static void Integer(AsnWriter writer, int tag, int value)
    {
        using (writer.PushSequence(Explicit(tag))) writer.WriteInteger(value);
    }

    private static Asn1Tag Explicit(int tag) => new(TagClass.ContextSpecific, tag, isConstructed: true);

    private enum SecurityLevel { Software = 0, TrustedEnvironment = 1, StrongBox = 2 }
    private enum BootState { Verified = 0, Unverified = 2 }
}
