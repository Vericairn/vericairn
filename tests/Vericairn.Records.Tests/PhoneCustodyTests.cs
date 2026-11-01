// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>A phone's device key, held by its Secure Enclave or Android Keystore, is recorded as such and claims no presence it does not enforce.</summary>
public sealed class PhoneCustodyTests : IDisposable
{
    private readonly string home = Directory.CreateTempSubdirectory("vericairn-phone-custody-").FullName;

    [Theory]
    [InlineData("ios", DeviceKeyStores.IosSecureEnclaveKind)]
    [InlineData("android", DeviceKeyStores.AndroidKeystoreKind)]
    public void APhoneDeviceRecordsItsKeystoreAndNeverClaimsPresence(string platform, string custody)
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope phone = Device(estate, platform, custody, "none");
        RecordEnvelope claims = Device(estate, platform, custody, "required");
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error && f.Location.StartsWith(phone.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "device.presence" && f.Location == claims.ExpectedPath);

        // Format 1 knows neither.
        using var early = new TestEstate();
        RecordEnvelope old = Device(early, platform, custody, null);
        Assert.Contains(early.Load().Findings, f => f.Code == "body.enum" && f.Location == old.ExpectedPath + "#body.custody");
    }

    [Fact]
    public void ThePlatformsPointAndDerSignatureBecomeTheDeviceKeyContractsEncodings()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(false);
        byte[] point = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), PhoneKeyEncoding.SubjectPublicKeyInfo(point));

        for (int i = 0; i < 64; i++)
        {
            byte[] digest = SHA256.HashData(BitConverter.GetBytes(i));
            byte[] der = key.SignHash(digest, DSASignatureFormat.Rfc3279DerSequence);
            byte[] p1363 = PhoneKeyEncoding.P1363FromDer(der);
            Assert.Equal(64, p1363.Length);
            Assert.True(key.VerifyHash(digest, p1363, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }

        Assert.Throws<ArgumentException>(() => PhoneKeyEncoding.SubjectPublicKeyInfo(point.AsSpan(1)));
        Assert.ThrowsAny<Exception>(() => PhoneKeyEncoding.P1363FromDer([0x30, 0x03, 0x02, 0x01, 0x01]));
    }

    public void Dispose() => Directory.Delete(home, recursive: true);

    private RecordEnvelope Device(TestEstate estate, string platform, string custody, string? presence)
    {
        var store = new FileKeyStore(home);
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        string extra = presence is null ? "" : $$""","presence":"{{presence}}","purpose":"signing" """;
        return estate.Add("device", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","name":"Phone","platform":"{{platform}}","custody":"{{custody}}","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["local-network"]{{extra}}}""", id: id, signer: new StoreSigner(store, id));
    }
}
