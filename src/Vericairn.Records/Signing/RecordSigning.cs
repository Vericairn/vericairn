// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Signing;

/// <summary>One signature on a record: which device signed the record's content hash and with which algorithm.</summary>
/// <param name="Algorithm">Signature algorithm name.</param>
/// <param name="Device">Device record id whose key signed.</param>
/// <param name="Signature">Base64url signature bytes.</param>
public sealed record RecordSignature(string Algorithm, string Device, string Signature);

/// <summary>Something that can sign a record's content hash on behalf of a device.</summary>
public interface IRecordSigner
{
    /// <summary>The device record id the signature will name.</summary>
    string DeviceId { get; }

    /// <summary>Signs the digest and returns the signature.</summary>
    RecordSignature Sign(ReadOnlySpan<byte> digest);
}

/// <summary>Signs with a key held in a device key store.</summary>
public sealed class StoreSigner : IRecordSigner
{
    private readonly IDeviceKeyStore store;
    private readonly string keyId;

    /// <summary>Creates a signer for a device whose key the store holds.</summary>
    public StoreSigner(IDeviceKeyStore store, string deviceId)
        : this(store, deviceId, deviceId)
    {
    }

    /// <summary>Creates a signer that signs with the key held under <paramref name="keyId"/> and names the device's identity: a rotation's new key, before it takes the device's place.</summary>
    public StoreSigner(IDeviceKeyStore store, string keyId, string deviceId)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.keyId = keyId ?? throw new ArgumentNullException(nameof(keyId));
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
    }

    /// <inheritdoc />
    public string DeviceId { get; }

    /// <summary>From 0.9.3: the key it signs with, the device's identity except for a rotation's new key before it takes the device's place.</summary>
    public string KeyId => keyId;

    /// <summary>From 0.9.3: the custody kind of the store holding the key, as device records name it.</summary>
    public string Custody => store.Kind;

    /// <inheritdoc />
    public RecordSignature Sign(ReadOnlySpan<byte> digest) =>
        new(RecordSigning.Algorithm, DeviceId, Base64Url.EncodeToString(store.Sign(keyId, digest)));

    /// <summary>
    /// From 0.9.3 (FIX-124): signs as <see cref="Sign(ReadOnlySpan{byte})"/> does, passing the store the reason its presence prompt shows
    /// (<see cref="PresenceReason"/>); a store that asks nobody ignores it.
    /// </summary>
    public RecordSignature Sign(ReadOnlySpan<byte> digest, string reason) =>
        new(RecordSigning.Algorithm, DeviceId, Base64Url.EncodeToString(store.Sign(keyId, digest, PresenceReason.Text(reason))));
}

/// <summary>The signature scheme of the record formats and its verification; format 2 keeps format 1's.</summary>
public static class RecordSigning
{
    /// <summary>ECDSA over the NIST P-256 curve with SHA-256 digests, IEEE P1363 fixed-field encoding (64 bytes).</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>Signature length in bytes.</summary>
    public const int SignatureBytes = 64;

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    private const string NamedP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>The 32 digest bytes behind a content hash such as sha256:ab12....</summary>
    public static byte[] Digest(string contentHash)
    {
        if (!ContentHash.IsValid(contentHash))
        {
            throw new ArgumentException("Not a content hash.", nameof(contentHash));
        }

        return Convert.FromHexString(contentHash.AsSpan(ContentHash.Prefix.Length));
    }

    /// <summary>Encodes a public key for a device record.</summary>
    public static string EncodePublicKey(byte[] subjectPublicKeyInfo) => Base64Url.EncodeToString(subjectPublicKeyInfo);

    /// <summary>
    /// True when the text is a usable P-256 public key: a SubjectPublicKeyInfo naming P-256 (<see cref="IsNamedP256"/>) that the
    /// platform imports. A key in any other form is refused before the platform or the framework's reader sees it, and one the platform
    /// refuses to import is not usable however it refuses (<see cref="Refused"/>).
    /// </summary>
    public static bool IsPublicKey(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded) || !Base64Url.IsValid(encoded))
        {
            return false;
        }

        byte[] subjectPublicKeyInfo = Base64Url.DecodeFromChars(encoded);
        if (!IsNamedP256(subjectPublicKeyInfo))
        {
            return false;
        }

        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return key.ExportParameters(false).Curve.Oid?.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
        }
        catch (Exception exception) when (Refused(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the bytes begin with the only form of SubjectPublicKeyInfo a usable key takes, read as DER: the algorithm
    /// id-ecPublicKey with the named curve P-256 as its only parameter, and an uncompressed point of 65 bytes. Every key Vericairn
    /// makes has this form. Read before any import, so a key in another form (explicit curve parameters, another named curve, a
    /// compressed point, another algorithm) is refused the same way on every platform, without asking the platform or the framework's
    /// own reader, whose answers and errors for such keys differ. Bytes after the structure, and the bit string's count of unused bits,
    /// are not read, as the platform's import does not read them, so such a key reads as it did before. The runtime identity
    /// (Vericairn.Protocol's RuntimeIdentityScheme) keeps its own copy of this check, written to the same rules; a test holds the two
    /// to the same answers, so a change here is made there too.
    /// </summary>
    private static bool IsNamedP256(byte[] subjectPublicKeyInfo)
    {
        try
        {
            AsnReader info = new AsnReader(subjectPublicKeyInfo, AsnEncodingRules.DER).ReadSequence();
            AsnReader algorithm = info.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcPublicKeyOid || algorithm.ReadObjectIdentifier() != NamedP256Oid)
            {
                return false;
            }

            algorithm.ThrowIfNotEmpty();
            byte[] point = info.ReadBitString(out _);
            info.ThrowIfNotEmpty();
            return point.Length == 65 && point[0] == 0x04;
        }
        catch (Exception exception) when (Refused(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether an exception from reading, importing or verifying a key or signature a record supplies is a refusal of it: any exception
    /// but the fatal ones (out of memory, stack overflow, access violation). Platforms refuse with a cryptographic error, or with the
    /// unsupported-platform error some give for keys they do not take, and the framework's reader may refuse with another error first;
    /// whichever it is, the key is not usable or the signature does not verify, so every platform answers alike and none escapes.
    /// </summary>
    private static bool Refused(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    /// <summary>True when the signature text is a base64url 64-byte value.</summary>
    public static bool IsSignature(string? encoded) =>
        !string.IsNullOrEmpty(encoded) && Base64Url.IsValid(encoded) && Base64Url.GetMaxDecodedLength(encoded.Length) >= SignatureBytes && Base64Url.DecodeFromChars(encoded).Length == SignatureBytes;

    /// <summary>Verifies a signature over a digest with an encoded public key.</summary>
    public static bool Verify(string encodedPublicKey, ReadOnlySpan<byte> digest, string encodedSignature)
    {
        using ImportedKey key = ImportedKey.Import(encodedPublicKey);
        return key.Verify(digest, encodedSignature);
    }

    /// <summary>
    /// The point of an encoded public key as 64 bytes X‖Y, read through the same import <see cref="Verify"/> verifies with, so a key it
    /// refuses has none; null when the text is not a usable P-256 key or the platform refuses its import (<see cref="Refused"/>).
    /// </summary>
    internal static byte[]? PublicPoint(string encodedPublicKey)
    {
        if (!IsPublicKey(encodedPublicKey)) return null;
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(encodedPublicKey), out _);
            ECPoint q = key.ExportParameters(false).Q;
            return q.X is { Length: 32 } x && q.Y is { Length: 32 } y ? [.. x, .. y] : null;
        }
        catch (Exception exception) when (Refused(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a 64-byte signature over a digest verifies with the P-256 key at a point X‖Y, as the platform verifies it; false when the
    /// platform refuses the point or the signature (<see cref="Refused"/>), so every platform answers alike.
    /// </summary>
    internal static bool VerifyPoint(ReadOnlySpan<byte> point, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (point.Length != 64 || signature.Length != SignatureBytes) return false;
        try
        {
            using ECDsa key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[..32].ToArray(), Y = point[32..].ToArray() } });
            return key.VerifyHash(digest, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (Refused(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// A public key imported once, which verifies signatures as <see cref="RecordSigning.Verify"/> does, since that is how it verifies: nothing
    /// when the text is not a usable P-256 key, when the signature is not 64 bytes, or when the platform refuses the key or the signature
    /// (<see cref="Refused"/>).
    /// </summary>
    internal sealed class ImportedKey : IDisposable
    {
        private readonly ECDsa? key;

        private ImportedKey(ECDsa? key) => this.key = key;

        /// <summary>Imports an encoded public key; one that is not usable verifies nothing.</summary>
        internal static ImportedKey Import(string encodedPublicKey)
        {
            if (!IsPublicKey(encodedPublicKey))
            {
                return new(null);
            }

            ECDsa? key = null;
            try
            {
                key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(encodedPublicKey), out _);
                return new(key);
            }
            catch (Exception exception) when (Refused(exception))
            {
                key?.Dispose();
                return new(null);
            }
        }

        /// <summary>Verifies a signature over a digest.</summary>
        internal bool Verify(ReadOnlySpan<byte> digest, string encodedSignature)
        {
            if (key is null || !IsSignature(encodedSignature))
            {
                return false;
            }

            try
            {
                return key.VerifyHash(digest, Base64Url.DecodeFromChars(encodedSignature), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception exception) when (Refused(exception))
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose() => key?.Dispose();
    }
}
