// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// Encoded keys in forms Vericairn does not use: a P-256 key whose curve parameters are written out in full rather than named,
/// a named-curve P-256 key whose point is off the curve, parameters over a characteristic-two field, and keys on named curves other
/// than P-256. Platforms and the framework's own reader refuse such keys in different ways, some of them by an error other than a
/// cryptographic one; each reads as every unusable key reads: not a usable key, and one that verifies nothing, on every platform.
/// </summary>
public sealed class UnusableKeyTests
{
    private const string EcPublicKey = "1.2.840.10045.2.1";
    private const string NamedP256 = "1.2.840.10045.3.1.7";
    private static readonly BigInteger P = Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger N = Hex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
    private static readonly BigInteger CurveB = Hex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");
    private static readonly BigInteger Gx = Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
    private static readonly BigInteger Gy = Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

    /// <summary>Named curves other than P-256, each by its object identifier and its generator, an uncompressed point on it.</summary>
    private static readonly Dictionary<string, (string Oid, string Point)> OtherCurves = new(StringComparer.Ordinal)
    {
        ["secp256k1"] = ("1.3.132.0.10", "0479BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8"),
        ["brainpoolP256r1"] = ("1.3.36.3.3.2.8.1.1.7", "048BD2AEB9CB7E57CB2C4B482FFC81B7AFB9DE27E1E3BD23C23A4453BD9ACE3262547EF835C3DAC4FD97F8461A14611DC9C27745132DED8E545C1D54C72F046997"),
        ["prime192v1"] = ("1.2.840.10045.3.1.1", "04188DA80EB03090F67CBF20EB43A18800F4FF0AFD82FF101207192B95FFC8DA78631011ED6B24CDD573F977A11E794811"),
        ["sect163k1"] = ("1.3.132.0.1", "0402FE13C0537BBC11ACAA07D793DE4E6D5E5C94EEE80289070FB05D38FF58321F2E800536D538CCDAA3D9"),
    };

    /// <summary>The kinds of key, by name.</summary>
    public static TheoryData<string> Kinds => ["explicit parameters", "off the curve", "characteristic two", "secp256k1", "brainpoolP256r1", "prime192v1", "sect163k1"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AKeyInAFormVericairnDoesNotUseIsNotUsableAndVerifiesNothing(string kind)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(kind));
        string signature = Base64Url.EncodeToString(signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        string key = Key(kind, signer);

        // No answer throws, and every platform gives the same one: the signer's own point written with its parameters in full is refused
        // as surely as a point off the curve or a key on another curve.
        Assert.False(RecordSigning.IsPublicKey(key));
        Assert.Null(RecordSigning.PublicPoint(key));
        Assert.False(RecordSigning.Verify(key, digest, signature));
        using (RecordSigning.ImportedKey imported = RecordSigning.ImportedKey.Import(key))
        {
            Assert.False(imported.Verify(digest, signature));
        }

        // The key-closing check answers for it as verifying under it answers.
        string signing = RecordSigning.EncodePublicKey(signer.ExportSubjectPublicKeyInfo());
        Assert.False(new RecoveredKeys().VerifiesUnder(key, digest, signature, [signing]) ?? false);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ADeviceRecordCarryingSuchAKeyIsAFindingInTheLoadAndTheChecksRunOverIt(string kind)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string key = Key(kind, signer);

        // The text scan of the record alone answers: a key that is not usable is not a validated encoding, so its text stays scanned.
        var record = new JsonObject
        {
            ["type"] = "device",
            ["body"] = new JsonObject { ["publicKey"] = key },
        };
        _ = EstateChecks.Content("records/device/2026-11/test.json", Encoding.UTF8.GetBytes(record.ToJsonString()));

        // An estate holding the record unsigned: the load answers with its findings, and the repository checks read the file.
        using var estate = new TestEstate();
        RecordEnvelope device = estate.Add("device", estate.OperatorId, Device(estate.OperatorId, key));
        EstateModel model = estate.Load();
        _ = EstateChecks.Run(model);

        // The record is invalid, unsigned, and its key is one of its findings: not a usable key, or longer than a device record takes.
        Assert.Contains(model.Findings, f => f.Code == "device.unsigned" && f.Location == device.ExpectedPath);
        Assert.Contains(model.Findings, f => f.Code is "device.public-key" or "body.length" && f.Location == device.ExpectedPath + "#body.publicKey");
    }

    [Fact]
    public void IsPublicKeyAnswersAsThePlatformsImportDidWhereverTheImportAnswered()
    {
        // The key check before 0.9.3: the platform's import alone, a cryptographic error read as a refusal and any other error escaping.
        static bool? Before(string encoded, out string? escaped)
        {
            escaped = null;
            if (string.IsNullOrEmpty(encoded) || !Base64Url.IsValid(encoded)) return false;
            try
            {
                using ECDsa key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(encoded), out _);
                return key.ExportParameters(false).Curve.Oid?.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (Exception exception)
            {
                escaped = exception.GetType().Name;
                return null;
            }
        }

        int compared = 0, usable = 0;
        var escapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var differences = new List<string>();
        foreach (byte[] candidate in Candidates())
        {
            string encoded = Base64Url.EncodeToString(candidate);
            bool now = RecordSigning.IsPublicKey(encoded);
            if (Before(encoded, out string? escaped) is not bool before)
            {
                escapes[escaped!] = escapes.GetValueOrDefault(escaped!) + 1;
                Assert.False(now, encoded);
                continue;
            }

            compared++;
            if (now) usable++;
            if (now != before) differences.Add($"{encoded}: {before} before, {now} now");
        }

        // The one narrowing: a named P-256 point of another length than 65 bytes, its coordinates padded or shortened, is refused now.
        // macOS and Windows refuse such a point too; a platform that read padded coordinates as the key would have accepted it.
        int narrowed = 0;
        for (int i = 0; i < 8; i++)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ECParameters parameters = key.ExportParameters(false);
            byte[] x = parameters.Q.X!, y = parameters.Q.Y!;
            foreach (byte[] point in (byte[][])[[4, 0, .. x, 0, .. y], [4, .. x[1..], .. y[1..]]])
            {
                string encoded = Base64Url.EncodeToString(Named(NamedP256, point));
                Assert.False(RecordSigning.IsPublicKey(encoded));
                if (Before(encoded, out _) == true) narrowed++;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{compared} keys compared, {usable} usable; the import raised another error for {string.Join(", ", escapes.Select(e => $"{e.Value} ({e.Key})"))}; {narrowed} of 16 padded or shortened points the import accepted.");
        Assert.Empty(differences);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()) Assert.Equal(0, narrowed);
        Assert.True(usable >= 20, $"{usable} usable keys among those compared");
        Assert.True(compared >= 1500 && compared + escapes.Values.Sum() >= 3000, $"{compared} keys compared, {escapes.Values.Sum()} not");
    }

    /// <summary>
    /// Encoded keys of every form: named P-256 keys, alone and followed by other bytes; the same points compressed, written with
    /// explicit parameters, off the curve, out of the field, at infinity, padded or with the bit string's unused bits set; keys on other
    /// named curves and for another algorithm; parameters over a characteristic-two field; every truncation of several of them and
    /// every position of them changed four ways; and bytes that are no key.
    /// </summary>
    public static IEnumerable<byte[]> Candidates()
    {
        var bases = new List<byte[]>();
        for (int i = 0; i < 8; i++)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] spki = key.ExportSubjectPublicKeyInfo();
            ECParameters parameters = key.ExportParameters(false);
            byte[] point = [.. parameters.Q.X!, .. parameters.Q.Y!];
            bases.Add(spki);
            bases.Add([.. spki, 0]);
            bases.Add([.. spki, .. spki]);
            bases.Add([.. spki, .. RandomNumberGenerator.GetBytes(10)]);
            bases.Add(Named(NamedP256, [(byte)(point[63] % 2 == 0 ? 2 : 3), .. point[..32]]));
            bases.Add(Base64Url.DecodeFromChars(ExplicitParameters(point)));
            bases.Add(Base64Url.DecodeFromChars(OffCurve(point)));
            bases.Add(Named("1.3.132.1.12", [4, .. point]));
            bases.Add(Spki(EcPublicKey, w => w.WriteNull(), [4, .. point], 0));
            bases.Add(Spki(EcPublicKey, null, [4, .. point], 0));
            if (point[63] % 2 == 0) bases.Add(Spki(EcPublicKey, w => w.WriteObjectIdentifier(NamedP256), [4, .. point], 1));
        }

        bases.Add(Named(NamedP256, [4, .. Bytes(Gx), .. Bytes(Gy)]));
        bases.Add(Named(NamedP256, [4, .. Bytes(P), .. Bytes(Gy)]));
        bases.Add(Named(NamedP256, [4, .. new byte[64]]));
        bases.Add(Named(NamedP256, [0]));
        bases.Add(Named(NamedP256, []));
        foreach (ECCurve curve in (ECCurve[])[ECCurve.NamedCurves.nistP384, ECCurve.NamedCurves.nistP521])
        {
            using var key = ECDsa.Create(curve);
            bases.Add(key.ExportSubjectPublicKeyInfo());
        }

        foreach ((string oid, string point) in OtherCurves.Values) bases.Add(Named(oid, Convert.FromHexString(point)));
        foreach (int m in (int[])[128, 163, 1, 7, 8, 64]) bases.Add(CharacteristicTwo(m));

        foreach (byte[] candidate in bases) yield return candidate;

        // Every truncation, and every byte changed four ways, of a named key, a key with explicit parameters, a characteristic-two key
        // and a key on another curve.
        using var mutated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters own = mutated.ExportParameters(false);
        byte[][] sources =
        [
            mutated.ExportSubjectPublicKeyInfo(),
            Base64Url.DecodeFromChars(ExplicitParameters([.. own.Q.X!, .. own.Q.Y!])),
            CharacteristicTwo(128),
            Named(OtherCurves["secp256k1"].Oid, Convert.FromHexString(OtherCurves["secp256k1"].Point)),
        ];
        foreach (byte[] source in sources)
        {
            for (int length = 0; length < source.Length; length++) yield return source[..length];
            for (int at = 0; at < source.Length; at++)
            {
                foreach (byte value in (byte[])[(byte)(source[at] ^ 0x01), (byte)(source[at] ^ 0x80), 0x00, 0xFF])
                {
                    if (value == source[at]) continue;
                    byte[] changed = (byte[])source.Clone();
                    changed[at] = value;
                    yield return changed;
                }
            }
        }

        // Bytes that are no key.
        yield return [];
        yield return Base64Url.DecodeFromChars("AAAA");
        for (int length = 1; length <= 300; length += 7) yield return RandomNumberGenerator.GetBytes(length);
    }

    /// <summary>A device record's body for a principal, carrying the encoded key.</summary>
    public static string Device(string principal, string publicKey) =>
        $$"""{"principal":"{{principal}}","name":"Unusable key","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"]}""";

    /// <summary>
    /// An encoded key of the kind named: a P-256 key's point with its curve parameters written out in full, or moved off the curve;
    /// parameters over a characteristic-two field; or the generator of another named curve.
    /// </summary>
    public static string Key(string kind, ECDsa key)
    {
        ECParameters parameters = key.ExportParameters(false);
        byte[] point = [.. parameters.Q.X!, .. parameters.Q.Y!];
        return kind switch
        {
            "explicit parameters" => ExplicitParameters(point),
            "off the curve" => OffCurve(point),
            "characteristic two" => Base64Url.EncodeToString(CharacteristicTwo(128)),
            _ when OtherCurves.TryGetValue(kind, out (string Oid, string Point) curve) => Base64Url.EncodeToString(Named(curve.Oid, Convert.FromHexString(curve.Point))),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// The SubjectPublicKeyInfo of a point under P-256's parameters written out in full (SEC 1 specifiedCurve) rather than named by
    /// the curve's object identifier.
    /// </summary>
    internal static string ExplicitParameters(byte[] point)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKey);
                using (writer.PushSequence())
                {
                    writer.WriteInteger(1);
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.10045.1.1");
                        writer.WriteIntegerUnsigned(Bytes(P));
                    }

                    using (writer.PushSequence())
                    {
                        writer.WriteOctetString(Bytes(P - 3));
                        writer.WriteOctetString(Bytes(CurveB));
                    }

                    writer.WriteOctetString([4, .. Bytes(Gx), .. Bytes(Gy)]);
                    writer.WriteIntegerUnsigned(Bytes(N));
                    writer.WriteInteger(1);
                }
            }

            writer.WriteBitString([4, .. point]);
        }

        return Base64Url.EncodeToString(writer.Encode());
    }

    /// <summary>The named-curve SubjectPublicKeyInfo of a point moved off the curve: its x with y + 1, which is neither y nor -y.</summary>
    internal static string OffCurve(byte[] point)
    {
        byte[] prefix = Convert.FromHexString("3059301306072A8648CE3D020106082A8648CE3D030107034200");
        BigInteger y = new(point.AsSpan(32), isUnsigned: true, isBigEndian: true);
        return Base64Url.EncodeToString([.. prefix, 4, .. point[..32], .. Bytes((y + 1) % P)]);
    }

    /// <summary>
    /// The SubjectPublicKeyInfo of sect163k1's generator under explicit parameters over a characteristic-two field of degree
    /// <paramref name="m"/> with a pentanomial basis (SEC 1 specifiedCurve), the field elements written at sect163k1's width whatever
    /// the degree says.
    /// </summary>
    internal static byte[] CharacteristicTwo(int m)
    {
        byte[] one = [.. new byte[20], 1];
        byte[] generator = Convert.FromHexString(OtherCurves["sect163k1"].Point);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKey);
                using (writer.PushSequence())
                {
                    writer.WriteInteger(1);
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.10045.1.2");
                        using (writer.PushSequence())
                        {
                            writer.WriteInteger(m);
                            writer.WriteObjectIdentifier("1.2.840.10045.1.2.3.3");
                            using (writer.PushSequence())
                            {
                                writer.WriteInteger(3);
                                writer.WriteInteger(6);
                                writer.WriteInteger(7);
                            }
                        }
                    }

                    using (writer.PushSequence())
                    {
                        writer.WriteOctetString(one);
                        writer.WriteOctetString(one);
                    }

                    writer.WriteOctetString(generator);
                    writer.WriteIntegerUnsigned(Convert.FromHexString("04000000000000000000020108A2E0CC0D99F8A5EF"));
                    writer.WriteInteger(2);
                }
            }

            writer.WriteBitString(generator);
        }

        return writer.Encode();
    }

    /// <summary>The SubjectPublicKeyInfo of an elliptic-curve point under a named curve.</summary>
    public static byte[] Named(string curve, byte[] point) => Spki(EcPublicKey, w => w.WriteObjectIdentifier(curve), point, 0);

    private static byte[] Spki(string algorithm, Action<AsnWriter>? parameters, byte[] key, int unusedBits)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(algorithm);
                parameters?.Invoke(writer);
            }

            writer.WriteBitString(key, unusedBits);
        }

        return writer.Encode();
    }

    private static BigInteger Hex(string hex) => BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static byte[] Bytes(BigInteger value)
    {
        var bytes = new byte[32];
        value.TryWriteBytes(bytes.AsSpan(32 - value.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);
        return bytes;
    }
}
