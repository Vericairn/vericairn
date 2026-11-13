// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// The keys recovered from an ECDSA P-256 signature (FIX-121), judged by the platform: the arithmetic against the platform's own keys and
/// key agreement, every recovered key against <see cref="ECDsa.VerifyHash(byte[], byte[])"/>, signatures constructed to verify under
/// two and four keys, high-s twins, digests at the order's edges, malformed signatures and keys; and the answer the key-closing check
/// reads from recovered keys, which equals verifying under the key with <see cref="RecordSigning.Verify"/> in every case, and still does
/// when the recovery is wrong.
/// </summary>
public sealed class P256KeyRecoveryTests
{
    private static readonly BigInteger P = Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger N = Hex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
    private static readonly BigInteger CurveB = Hex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");
    private static readonly BigInteger Gx = Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
    private static readonly BigInteger Gy = Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
    private static readonly byte[] G = [.. Bytes(Gx), .. Bytes(Gy)];

    [Fact]
    public void ScalarMultiplicationGivesThePlatformsKeysAndKeyAgreement()
    {
        for (int i = 0; i < 16; i++)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ECParameters parameters = key.ExportParameters(true);
            Assert.Equal(Point(parameters), P256KeyRecovery.MultiplyGenerator(parameters.D!));
        }

        // The edges: 1·G, 2·G by the affine doubling formula, (n - 1)·G = -G, and n·G and 0·G at infinity.
        Assert.Equal(G, P256KeyRecovery.MultiplyGenerator(Bytes(BigInteger.One)));
        BigInteger lambda = Mod(3 * (Gx * Gx - 1) * BigInteger.ModPow(2 * Gy, P - 2, P));
        BigInteger x2 = Mod(lambda * lambda - 2 * Gx);
        byte[] doubled = [.. Bytes(x2), .. Bytes(Mod(lambda * (Gx - x2) - Gy))];
        Assert.Equal(doubled, P256KeyRecovery.MultiplyGenerator(Bytes(2)));
        byte[] negated = [.. Bytes(Gx), .. Bytes(P - Gy)];
        Assert.Equal(negated, P256KeyRecovery.MultiplyGenerator(Bytes(N - 1)));
        Assert.Equal(negated, P256KeyRecovery.Multiply(G, Bytes(N - 1)));
        Assert.Null(P256KeyRecovery.MultiplyGenerator(Bytes(N)));
        Assert.Null(P256KeyRecovery.MultiplyGenerator(Bytes(BigInteger.Zero)));
        Assert.Null(P256KeyRecovery.Multiply(G, Bytes(N)));

        // A variable point: the x of d·Q is the raw secret of key agreement between d and Q, which the platform hashes.
        for (int i = 0; i < 8; i++)
        {
            using var alice = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var bob = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[]? shared = P256KeyRecovery.Multiply(Point(bob.ExportParameters(false)), alice.ExportParameters(true).D!);
            Assert.NotNull(shared);
            Assert.Equal(alice.DeriveKeyFromHash(bob.PublicKey, HashAlgorithmName.SHA256), SHA256.HashData(shared.AsSpan(0, 32)));
        }

        Assert.True(P256KeyRecovery.OnCurve(G));
        Assert.False(P256KeyRecovery.OnCurve([.. Bytes(Gx), .. Bytes(Gy + 1)]));
        Assert.False(P256KeyRecovery.OnCurve([.. Bytes(P), .. Bytes(Gy)]));
    }

    [Fact]
    public void AGenuineSignatureVerifiesUnderItsSignersKeyAndOneOtherAndTheCheckReadsBoth()
    {
        var keys = new RecoveredKeys();
        for (int i = 0; i < 24; i++)
        {
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var unrelated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            // Digests at the order's edges first, where the digest is reduced mod n, then ordinary ones.
            byte[] digest = i switch
            {
                0 => new byte[32],
                1 => Bytes(N - 1),
                2 => Bytes(N),
                3 => Bytes(N + 1),
                4 => Enumerable.Repeat((byte)0xFF, 32).ToArray(),
                _ => SHA256.HashData(BitConverter.GetBytes(i)),
            };
            byte[] signature = signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            byte[] own = Point(signer.ExportParameters(false));
            byte[][] recovered = Assert.IsType<byte[][]>(P256KeyRecovery.Candidates(digest, signature));
            Assert.Equal(2, recovered.Length);
            Assert.Equal(2, P256KeyRecovery.MostKeys(signature));
            Assert.Contains(recovered, k => k.AsSpan().SequenceEqual(own));
            byte[] other = recovered.Single(k => !k.AsSpan().SequenceEqual(own));

            // The platform verifies the signature under the other key too, and its high-s twin under the same two keys.
            string encodedSignature = Base64Url.EncodeToString(signature), twin = Base64Url.EncodeToString(Malleate(signature));
            Assert.True(RecordSigning.Verify(Encode(other), digest, encodedSignature));
            Assert.True(RecordSigning.Verify(Encode(own), digest, twin));
            Assert.Equal(Sorted(recovered), Sorted(P256KeyRecovery.Candidates(digest, Malleate(signature))!));

            string[] candidates = [Encode(own), Encode(other), RecordSigning.EncodePublicKey(unrelated.ExportSubjectPublicKeyInfo())];
            foreach (string sig in (string[])[encodedSignature, twin])
            {
                foreach (string key in candidates)
                {
                    bool? read = keys.VerifiesUnder(key, digest, sig, [Encode(own)]);
                    Assert.NotNull(read);
                    Assert.Equal(RecordSigning.Verify(key, digest, sig), read);
                }
            }
        }

        Assert.Equal(0, keys.Fallbacks);
        Assert.Equal(0, keys.Dropped);
        Assert.Equal(24 * 2 * 3, keys.Recoveries);
    }

    [Fact]
    public void ConstructedSignaturesVerifyUnderExactlyTheKeysRecoveredFromThem()
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("a constructed signature"));
        byte[] s = Bytes(Mod(new BigInteger(SHA256.HashData(Encoding.UTF8.GetBytes("its s")), isUnsigned: true, isBigEndian: true)) % N);
        using var unrelated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string other = RecordSigning.EncodePublicKey(unrelated.ExportSubjectPublicKeyInfo());

        // r below p - n, so that both r and r + n can be x-coordinates: when both are, four keys, all of them the platform verifies under.
        BigInteger both = Smallest(x => IsX(x) && IsX(x + N));
        byte[] four = [.. Bytes(both), .. s];
        byte[][] fourKeys = Assert.IsType<byte[][]>(P256KeyRecovery.Candidates(digest, four));
        Assert.Equal(4, fourKeys.Length);
        Assert.Equal(4, fourKeys.Select(Convert.ToHexString).Distinct().Count());
        Assert.Equal(4, P256KeyRecovery.MostKeys(four));
        Assert.All(fourKeys, k => Assert.True(RecordSigning.VerifyPoint(k, digest, four)));
        Assert.False(RecordSigning.Verify(other, digest, Base64Url.EncodeToString(four)));
        AssertReadAsVerified(digest, four, [.. fourKeys.Select(Encode), other], Encode(fourKeys[0]), trusted: true);

        // Only r, or only r + n, an x-coordinate: two keys of the four possible, so the check verifies under each key as before.
        foreach (BigInteger r in (BigInteger[])[Smallest(x => IsX(x) && !IsX(x + N)), Smallest(x => !IsX(x) && IsX(x + N))])
        {
            byte[] signature = [.. Bytes(r), .. s];
            byte[][] twoKeys = Assert.IsType<byte[][]>(P256KeyRecovery.Candidates(digest, signature));
            Assert.Equal(2, twoKeys.Length);
            Assert.All(twoKeys, k => Assert.True(RecordSigning.VerifyPoint(k, digest, signature)));
            AssertReadAsVerified(digest, signature, [.. twoKeys.Select(Encode), other], Encode(twoKeys[0]), trusted: false);
        }

        // An r of a signature anyone could make, at or above p - n: two keys when r is an x-coordinate, none when it is not.
        BigInteger high = N - 1000;
        while (!IsX(high)) high--;
        byte[] twoKey = [.. Bytes(high), .. s];
        byte[][] pair = Assert.IsType<byte[][]>(P256KeyRecovery.Candidates(digest, twoKey));
        Assert.Equal(2, pair.Length);
        Assert.All(pair, k => Assert.True(RecordSigning.VerifyPoint(k, digest, twoKey)));
        AssertReadAsVerified(digest, twoKey, [.. pair.Select(Encode), other], Encode(pair[1]), trusted: true);
        BigInteger none = N - 1000;
        while (IsX(none)) none--;
        byte[] keyless = [.. Bytes(none), .. s];
        Assert.Empty(Assert.IsType<byte[][]>(P256KeyRecovery.Candidates(digest, keyless)));
        AssertReadAsVerified(digest, keyless, [other, Encode(pair[0])], null, trusted: false);
    }

    [Fact]
    public void SignaturesOutsideTheArithmeticAreVerifiedAsBeforeAndRefusedByThePlatform()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("out of range"));
        byte[] genuine = signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string own = Encode(Point(signer.ExportParameters(false)));
        BigInteger[] outside = [BigInteger.Zero, N, N + 1, (BigInteger.One << 256) - 1];
        var signatures = new List<string>();
        foreach (BigInteger value in outside)
        {
            byte[] badR = [.. Bytes(value), .. genuine[32..]], badS = [.. genuine[..32], .. Bytes(value)];
            Assert.Null(P256KeyRecovery.Candidates(digest, badR));
            Assert.Null(P256KeyRecovery.Candidates(digest, badS));
            signatures.Add(Base64Url.EncodeToString(badR));
            signatures.Add(Base64Url.EncodeToString(badS));
        }

        signatures.AddRange([Base64Url.EncodeToString(genuine.AsSpan(..63)), Base64Url.EncodeToString([.. genuine, 0]), "", "not base64url!"]);
        var keys = new RecoveredKeys();
        foreach (string signature in signatures)
        {
            Assert.False(RecordSigning.Verify(own, digest, signature));
            Assert.Null(keys.VerifiesUnder(own, digest, signature, [own]));
        }

        Assert.Equal(signatures.Count, keys.Fallbacks);
    }

    [Fact]
    public void KeysThatDoNotImportAreVerifiedAsBefore()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var large = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("odd keys"));
        byte[] signature = signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] own = Point(signer.ExportParameters(false));
        byte[] spki = signer.ExportSubjectPublicKeyInfo();
        BigInteger y = new(own.AsSpan(32), isUnsigned: true, isBigEndian: true);
        string[] odd =
        [
            RecordSigning.EncodePublicKey(large.ExportSubjectPublicKeyInfo()),
            Base64Url.EncodeToString([.. Convert.FromHexString("3039301306072A8648CE3D020106082A8648CE3D030107032200"), (byte)(y.IsEven ? 2 : 3), .. own[..32]]),
            Base64Url.EncodeToString(spki.AsSpan(..^1)),
            Base64Url.EncodeToString([.. spki, 0]),
            "AAAA",
            "",
            "not a key",
        ];
        var keys = new RecoveredKeys();
        string sig = Base64Url.EncodeToString(signature), twin = Base64Url.EncodeToString(Malleate(signature));
        foreach (string key in odd)
        {
            foreach (string s in (string[])[sig, twin])
                Assert.Equal(RecordSigning.Verify(key, digest, s), keys.VerifiesUnder(key, digest, s, [Encode(own)]) ?? RecordSigning.Verify(key, digest, s));
        }

        // Those the platform refuses have no point, so the platform's verification answers for them; one with bytes after it reads as the key.
        Assert.Null(keys.Point(odd[0]));
        Assert.Null(keys.Point(odd[2]));
        Assert.Null(keys.Point(odd[4]));
        Assert.Null(keys.Point(odd[5]));
        Assert.Null(keys.Point(odd[6]));
        Assert.Equal(own, keys.Point(odd[3]));
    }

    [Fact]
    public void ARecoveredKeyOffTheCurveIsDroppedAndItsSignatureFallsBackOnEveryPlatform()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("a recovered key off the curve"));
        byte[] signature = signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string sig = Base64Url.EncodeToString(signature);
        byte[] own = Point(signer.ExportParameters(false));
        byte[] other = P256KeyRecovery.Candidates(digest, signature)!.Single(k => !k.AsSpan().SequenceEqual(own));

        // A point whose y is neither y nor -y of a point with its x, and one whose x is p: neither is on the curve, and neither verifies
        // anything when the platform is asked to import it, on any platform.
        byte[] offCurve = [.. own[..32], .. Bytes(Mod(new BigInteger(own.AsSpan(32), isUnsigned: true, isBigEndian: true) + 1))];
        byte[] outOfField = [.. Bytes(P), .. own[32..]];
        foreach (byte[] point in (byte[][])[offCurve, outOfField])
        {
            Assert.False(P256KeyRecovery.OnCurve(point));
            Assert.False(RecordSigning.VerifyPoint(point, digest, signature));
        }

        // Added to the right keys by a recovery, each is dropped before the platform is asked, and the signature falls back.
        foreach (byte[] point in (byte[][])[offCurve, outOfField])
        {
            var keys = new RecoveredKeys((d, s) => [.. P256KeyRecovery.Candidates(d, s)!, point]);
            Assert.Null(keys.Confirmed(digest, sig, [Encode(own)]));
            Assert.Equal(1, keys.Dropped);
            Assert.Equal(1, keys.Fallbacks);
            foreach (string key in (string[])[Encode(own), Encode(other)])
                Assert.Equal(RecordSigning.Verify(key, digest, sig), keys.VerifiesUnder(key, digest, sig, [Encode(own)]) ?? RecordSigning.Verify(key, digest, sig));
        }
    }

    /// <summary>The ways a recovery put in place of the arithmetic is wrong (<see cref="Wrong"/>).</summary>
    internal static readonly string[] WrongRecoveries = ["none", "empty", "first only", "second only", "one corrupted", "one replaced", "repeated", "unrelated"];

    public static TheoryData<string> WrongRecoveryData => new(WrongRecoveries);

    /// <summary>
    /// A wrong recovery: none at all, no key, one of the two keys only, one of them corrupted off the curve or replaced by the generator,
    /// the first twice, or two keys the signature does not verify under.
    /// </summary>
    internal static Func<byte[], byte[], byte[][]?> Wrong(string wrong) => (digest, signature) =>
    {
        byte[][] right = P256KeyRecovery.Candidates(digest, signature)!;
        byte[] corrupted = [.. right[1]];
        corrupted[40] ^= 1;
        return wrong switch
        {
            "none" => null,
            "empty" => [],
            "first only" => [right[0]],
            "second only" => [right[1]],
            "one corrupted" => [right[0], corrupted],
            "one replaced" => [right[0], G],
            "repeated" => [right[0], right[0]],
            _ => [G, P256KeyRecovery.MultiplyGenerator(Bytes(2))!],
        };
    };

    [Theory]
    [MemberData(nameof(WrongRecoveryData))]
    public void AWrongRecoveryFallsBackAndChangesNoAnswer(string wrong)
    {
        var keys = new RecoveredKeys(Wrong(wrong));
        for (int i = 0; i < 6; i++)
        {
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var unrelated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] digest = SHA256.HashData(BitConverter.GetBytes(i + 100));
            byte[] signature = signer.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            byte[] own = Point(signer.ExportParameters(false));
            byte[] other = P256KeyRecovery.Candidates(digest, signature)!.Single(k => !k.AsSpan().SequenceEqual(own));
            string sig = Base64Url.EncodeToString(signature);
            foreach (string key in (string[])[Encode(own), Encode(other), RecordSigning.EncodePublicKey(unrelated.ExportSubjectPublicKeyInfo())])
            {
                bool? read = keys.VerifiesUnder(key, digest, sig, [Encode(own)]);
                Assert.Null(read);
                Assert.Equal(RecordSigning.Verify(key, digest, sig), read ?? RecordSigning.Verify(key, digest, sig));
            }
        }

        Assert.Equal(6 * 3, keys.Fallbacks);
        Assert.Equal(wrong is "one corrupted" or "one replaced" ? 6 * 3 : wrong == "unrelated" ? 6 * 3 * 2 : 0, keys.Dropped);
    }

    /// <summary>
    /// Asserts that the check's reading of each key equals the platform's verification under it, from the recovered keys when they are
    /// certain (<paramref name="trusted"/>) and otherwise by falling back to it.
    /// </summary>
    private static void AssertReadAsVerified(byte[] digest, byte[] signature, string[] keys, string? signing, bool trusted)
    {
        var recovered = new RecoveredKeys();
        string sig = Base64Url.EncodeToString(signature);
        foreach (string key in keys)
        {
            bool? read = recovered.VerifiesUnder(key, digest, sig, signing is null ? [] : [signing]);
            Assert.Equal(trusted, read is not null);
            Assert.Equal(RecordSigning.Verify(key, digest, sig), read ?? RecordSigning.Verify(key, digest, sig));
        }
    }

    /// <summary>The smallest x from 1 with the property, below p - n.</summary>
    private static BigInteger Smallest(Func<BigInteger, bool> property)
    {
        for (BigInteger x = 1; x < 10_000; x++)
        {
            if (property(x)) return x;
        }

        throw new InvalidOperationException("No such x below 10,000.");
    }

    /// <summary>Whether x is the x-coordinate of a point: x³ - 3x + b is a nonzero square mod p (Euler's criterion).</summary>
    private static bool IsX(BigInteger x) => BigInteger.ModPow(Mod(x * x * x - 3 * x + CurveB), (P - 1) / 2, P).IsOne;

    /// <summary>The twin of an IEEE P1363 ECDSA P-256 signature (r, s): (r, n - s), which verifies wherever the signature does.</summary>
    private static byte[] Malleate(byte[] signature) => [.. signature[..32], .. Bytes(N - new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true))];

    private static string[] Sorted(byte[][] keys) => [.. keys.Select(Convert.ToHexString).Order(StringComparer.Ordinal)];

    private static byte[] Point(ECParameters parameters) => [.. parameters.Q.X!, .. parameters.Q.Y!];

    /// <summary>The encoded key of a point, as a device record holds it.</summary>
    private static string Encode(byte[] point)
    {
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[..32], Y = point[32..] } });
        return RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo());
    }

    private static BigInteger Mod(BigInteger value) => (value % P + P) % P;

    private static BigInteger Hex(string hex) => BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static byte[] Bytes(BigInteger value)
    {
        var bytes = new byte[32];
        value.TryWriteBytes(bytes.AsSpan(32 - value.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);
        return bytes;
    }
}
