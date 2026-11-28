// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class AndroidKeyAttestationTests
{
    private static readonly DateTimeOffset FixtureAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("unlocked")]
    [InlineData("unverified-boot")]
    [InlineData("missing-boot")]
    [InlineData("software-boot")]
    [InlineData("wrong-app")]
    [InlineData("missing-app")]
    [InlineData("bad-app-signer")]
    [InlineData("old-os")]
    [InlineData("bad-patch")]
    [InlineData("missing-patch")]
    [InlineData("bad-vendor-patch")]
    [InlineData("bad-boot-patch")]
    [InlineData("unique-id")]
    [InlineData("device-id")]
    public void LeafClaimsMustShowLockedVerifiedBootRemoteIdentityAndValidPatchMetadata(string policy)
    {
        using var authority = new SyntheticAndroidAttestation();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        IReadOnlyList<string> certificates = authority.Issue(key.ExportSubjectPublicKeyInfo(), nonce, policy);
        using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(Base64Url.DecodeFromChars(certificates[0]));
        byte[] description = leaf.Extensions[AndroidKeyAttestation.KeyDescriptionOid]!.RawData;
        Assert.Null(AndroidKeyAttestation.ReadEvidence(certificates, leaf, description, nonce, Base64Url.EncodeToString(nonce)));
    }

    [Theory]
    [InlineData("per-use", "trusted-environment")]
    [InlineData("strongbox", "strongbox")]
    public void EvidenceRecordsClaimsFromTheLeaf(string policy, string hardwareLevel)
    {
        using var authority = new SyntheticAndroidAttestation();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        string challenge = Base64Url.EncodeToString(nonce);
        IReadOnlyList<string> certificates = authority.Issue(key.ExportSubjectPublicKeyInfo(), nonce, policy);
        using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(Base64Url.DecodeFromChars(certificates[0]));
        byte[] description = leaf.Extensions[AndroidKeyAttestation.KeyDescriptionOid]!.RawData;

        var evidence = AndroidKeyAttestation.ReadEvidence(certificates, leaf, description, nonce, challenge);
        Assert.NotNull(evidence);
        Assert.Equal(certificates, evidence.Certificates);
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(leaf.RawData)), evidence.LeafDigest);
        Assert.Equal(challenge, evidence.Challenge);
        Assert.Equal(hardwareLevel, evidence.AttestationSecurityLevel);
        Assert.Equal(hardwareLevel, evidence.KeySecurityLevel);
        Assert.Equal(3, evidence.UserAuthType);
        Assert.True(evidence.PerUse);
        Assert.True(evidence.DeviceLocked);
        Assert.Equal("verified", evidence.VerifiedBootState);
        Assert.Equal("com.vericairn.remote", evidence.PackageName);
        Assert.Equal(1, evidence.PackageVersion);
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData("fixture app signer"u8)), Assert.Single(evidence.SigningDigests));
        Assert.Equal(140000, evidence.OsVersion);
        Assert.Equal(202411, evidence.OsPatchLevel);
        Assert.Equal(20241105, evidence.VendorPatchLevel);
        Assert.Equal(20241105, evidence.BootPatchLevel);
    }

    [Fact]
    public void ASecondRootCertificateOverTheSameKeyUsesThePinnedAnchorAndChecksBothSerials()
    {
        SyntheticAndroidAttestation.RequireNativeTrust();
        using var authority = new SyntheticAndroidAttestation();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        string challenge = Base64Url.EncodeToString(nonce);
        string publicKey = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
        IReadOnlyList<string> certificates = authority.Issue(key.ExportSubjectPublicKeyInfo(), nonce, rootVariant: true);
        var revoked = new HashSet<string>();
        var verifier = authority.Verifier(() => revoked);

        Assert.NotNull(verifier.Verify(certificates, challenge, publicKey, FixtureAt));
        Assert.NotNull(verifier.Verify([certificates[0]], challenge, publicKey, FixtureAt));
        revoked.Add("3"); // Both the supplied root variant and the canonical anchor are checked.
        Assert.Null(verifier.Verify(certificates, challenge, publicKey, FixtureAt));
        revoked.Clear();
        revoked.Add("1");
        Assert.Null(verifier.Verify(certificates, challenge, publicKey, FixtureAt));
    }

    [Theory]
    [InlineData("akita_sdk34_TEE_EC_NONE", "2024-09-12", true)]
    [InlineData("caiman_sdk36_SB_EC_RKP", "2025-09-26", true)]
    [InlineData("blueline_sdk28_TEE_EC_NONE", "2026-11-27", true)]
    [InlineData("tegu_sdk36_TEE_EC_2026_ROOT", "2026-02-23", true)]
    [InlineData("sony-xperia10-iii_sdk33_TEE_EC", "2025-10-01", false)]
    [InlineData("akita_sdk34_TEE_EC_NONE", "2026-11-27", false)]
    public void RecordedChainsUsePinnedGoogleKeysAndStillRequireValidPaths(string fixture, string date, bool trusted)
    {
        SyntheticAndroidAttestation.RequireNativeTrust();
        X509Certificate2Collection roots = AndroidAttestationRoots.Load();
        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPemFile(Path.Combine(AppContext.BaseDirectory, "AndroidAttestation", fixture + ".pem"));
            DateTimeOffset at = DateTimeOffset.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var verifier = new AndroidKeyAttestation(roots, TimeProvider.System, () => new HashSet<string>());
            Assert.Equal(trusted, verifier.ChainIsTrusted(chain, at));
        }
        finally
        {
            foreach (X509Certificate2 certificate in roots) certificate.Dispose();
            foreach (X509Certificate2 certificate in chain) certificate.Dispose();
        }
    }
}
