// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A client certificate judged against a device key in a form Vericairn does not use (<see cref="UnusableKeyTests"/>), or
/// dated at either end of the calendar, gets an answer, never an exception, and the answer for a certificate the device's key did not
/// issue is the same refusal whatever the key or the dates; a runtime identity proof under such a key verifies nothing.
/// </summary>
public sealed class UnusableKeyIssuerTests
{
    [Theory]
    [MemberData(nameof(UnusableKeyTests.Kinds), MemberType = typeof(UnusableKeyTests))]
    public void ACertificateIsNotIssuedByAKeyInAFormVericairnDoesNotUse(string kind)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using X509Certificate2 certificate = new CertificateRequest("CN=device", key, HashAlgorithmName.SHA256).CreateSelfSigned(now.AddMinutes(-1), now.AddMinutes(10));
        Assert.True(RequestAuthenticator.IssuedBy(certificate, RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo()), now));

        // The same key's point written another way, or a key on another curve: refused, on every platform.
        Assert.False(RequestAuthenticator.IssuedBy(certificate, UnusableKeyTests.Key(kind, key), now));
    }

    [Theory]
    [MemberData(nameof(UnusableKeyTests.Kinds), MemberType = typeof(UnusableKeyTests))]
    public void ARuntimeIdentityProofUnderAKeyInAFormVericairnDoesNotUseVerifiesNothing(string kind)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string identity = UnusableKeyTests.Key(kind, key);
        const string Estate = "01M7AGWQYC20Y92FJ6C4WKE97C", Certificate = "sha256:aa", Client = "sha256:bb", Nonce = "nonce";
        string signature = Base64Url.EncodeToString(key.SignHash(RuntimeIdentityScheme.Digest(Estate, Certificate, Client, Nonce), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.False(RuntimeIdentityScheme.Verify(new RuntimeIdentityProof(Estate, identity, Certificate, Client, Nonce, signature), identity, Estate, Certificate, Client, Nonce));
    }

    [Theory]
    [InlineData("from the first day of the calendar")]
    [InlineData("to the last day of the calendar")]
    [InlineData("from the first to the last day")]
    public void ACertificateDatedAtAnEndOfTheCalendarIsJudgedLikeAnyOther(string dates)
    {
        // Run in more than one time zone (TZ=UTC and a zone east of it, besides the machine's own): local times of such dates fall
        // outside the calendar in some zones and not others, and the answer never depends on that.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (DateTimeOffset from, DateTimeOffset to) = dates switch
        {
            "from the first day of the calendar" => (new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), now.AddMinutes(10)),
            "to the last day of the calendar" => (now.AddMinutes(-1), new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            _ => (new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero)),
        };
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string deviceKey = RecordSigning.EncodePublicKey(device.ExportSubjectPublicKeyInfo());
        using X509Certificate2 issued = new CertificateRequest("CN=device", device, HashAlgorithmName.SHA256).CreateSelfSigned(from, to);
        using X509Certificate2 forged = new CertificateRequest("CN=device", stranger, HashAlgorithmName.SHA256).CreateSelfSigned(from, to);

        // Within its dates, the device's own certificate is issued by its key; another key's is refused, as for any device.
        Assert.True(RequestAuthenticator.IssuedBy(issued, deviceKey, now));
        Assert.False(RequestAuthenticator.IssuedBy(forged, deviceKey, now));

        // Outside its dates it is refused; judged at either end of the calendar itself nothing overflows, and after the last instant
        // every certificate has ended.
        if (from.Year > 1) Assert.False(RequestAuthenticator.IssuedBy(issued, deviceKey, from.AddDays(-1)));
        if (to.Year < 9999) Assert.False(RequestAuthenticator.IssuedBy(issued, deviceKey, to.AddDays(1)));
        _ = RequestAuthenticator.IssuedBy(issued, deviceKey, DateTimeOffset.MinValue);
        Assert.False(RequestAuthenticator.IssuedBy(issued, deviceKey, DateTimeOffset.MaxValue));
    }
}
