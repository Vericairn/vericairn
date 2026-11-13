// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A client certificate judged against a device key in a form Vericairn does not use (<see cref="UnusableKeyTests"/>), or
/// dated at either end of the calendar, gets an answer, never an exception, and the answer for a certificate the device's key did not
/// issue is the same refusal whatever the key or the dates; a runtime identity proof under such a key verifies nothing, and the
/// runtime identity reads a key's form by the same rules as the records.
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

        // The same signature verifies under the key in the form Vericairn makes, so only the form refuses it below.
        string named = RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo());
        Assert.True(RuntimeIdentityScheme.Verify(new RuntimeIdentityProof(Estate, named, Certificate, Client, Nonce, signature), named, Estate, Certificate, Client, Nonce));

        // The same key's point written another way, or a key on another curve: refused, on every platform.
        Assert.False(RuntimeIdentityScheme.Verify(new RuntimeIdentityProof(Estate, identity, Certificate, Client, Nonce, signature), identity, Estate, Certificate, Client, Nonce));
    }

    [Fact]
    public void TheRuntimeIdentityReadsAKeysFormByTheSameRulesAsTheRecords()
    {
        // The runtime identity's assembly references no other Vericairn assembly, so it keeps its own copy of the records' check of a
        // key's form. Over every key the records' tests generate, and named P-256 keys of the shapes each part of the rule refuses, the
        // two give the same answer, so a change to either copy in any part of the rule is found here.
        Func<byte[], bool> records = FormCheck(typeof(RecordSigning)), identity = FormCheck(typeof(RuntimeIdentityScheme));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(false);
        byte[] x = parameters.Q.X!, y = parameters.Q.Y!, spki = key.ExportSubjectPublicKeyInfo();
        string p256 = ECCurve.NamedCurves.nistP256.Oid.Value!;
        byte[][] otherShapes =
        [
            // Points of other lengths than 65 bytes: padded, shortened, followed by a byte, or one coordinate alone.
            .. ((byte[][])[[4, 0, .. x, 0, .. y], [4, .. x[1..], .. y[1..]], [4, .. x, .. y, 0], [4, .. x]]).Select(point => UnusableKeyTests.Named(p256, point)),

            // Hybrid points: 65 bytes, first 6 or 7 rather than 4.
            UnusableKeyTests.Named(p256, [6, .. x, .. y]),
            UnusableKeyTests.Named(p256, [7, .. x, .. y]),

            // An element after the curve in the algorithm, and an element after the point in the key.
            WithAnotherElement([4, .. x, .. y], inTheAlgorithm: true),
            WithAnotherElement([4, .. x, .. y], inTheAlgorithm: false),

            // The key Vericairn makes, in encodings BER allows and DER does not: its length in the long form, or left open and ended by
            // an end mark.
            [0x30, 0x81, .. spki[1..]],
            [0x30, 0x80, .. spki[2..], 0, 0],
        ];
        int accepted = 0, refused = 0;
        var differences = new List<string>();
        foreach (byte[] candidate in UnusableKeyTests.Candidates().Concat(otherShapes))
        {
            bool answer = records(candidate);
            if (answer) accepted++;
            else refused++;
            if (identity(candidate) != answer) differences.Add($"{Base64Url.EncodeToString(candidate)}: {answer} for the records, {!answer} for the runtime identity");
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{accepted + refused} keys compared: {accepted} in the form, {refused} not.");
        Assert.Empty(differences);
        Assert.True(accepted >= 100 && refused >= 1500, $"{accepted} keys in the form and {refused} not");

        // A key Vericairn makes is in the form; a key written in another form is refused by the form alone, whatever a platform's
        // import would make of it. A point off the curve is in the form, and the import refuses it.
        Assert.True(identity(spki));
        Assert.All(otherShapes, candidate => Assert.False(identity(candidate)));
        foreach (string kind in (string[])["explicit parameters", "characteristic two", "secp256k1", "brainpoolP256r1", "prime192v1", "sect163k1"])
        {
            Assert.False(identity(Base64Url.DecodeFromChars(UnusableKeyTests.Key(kind, key))), kind);
        }
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

        // The first day is the calendar's own, except on Windows: it counts a certificate's dates from 1601-01-01T00:00:00Z and cannot
        // load a certificate dated earlier, so there the first day is the earliest it can load one with.
        DateTimeOffset first = OperatingSystem.IsWindows() ? new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero) : new(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        (DateTimeOffset from, DateTimeOffset to) = dates switch
        {
            "from the first day of the calendar" => (first, now.AddMinutes(10)),
            "to the last day of the calendar" => (now.AddMinutes(-1), new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            _ => (first, new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero)),
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

    /// <summary>
    /// The named P-256 SubjectPublicKeyInfo of a point with one more element, a null, after the curve in the algorithm or after the point.
    /// </summary>
    private static byte[] WithAnotherElement(byte[] point, bool inTheAlgorithm)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.10045.2.1");
                writer.WriteObjectIdentifier(ECCurve.NamedCurves.nistP256.Oid.Value!);
                if (inTheAlgorithm) writer.WriteNull();
            }

            writer.WriteBitString(point);
            if (!inTheAlgorithm) writer.WriteNull();
        }

        return writer.Encode();
    }

    /// <summary>A type's own check of a key's form, which each keeps private.</summary>
    private static Func<byte[], bool> FormCheck(Type type) =>
        (type.GetMethod("IsNamedP256", BindingFlags.Static | BindingFlags.NonPublic, [typeof(byte[])]) ?? throw new MissingMethodException(type.FullName, "IsNamedP256"))
            .CreateDelegate<Func<byte[], bool>>();
}
