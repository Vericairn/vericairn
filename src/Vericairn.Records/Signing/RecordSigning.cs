// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
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

    /// <summary>True when the text is a usable P-256 public key.</summary>
    public static bool IsPublicKey(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded) || !Base64Url.IsValid(encoded))
        {
            return false;
        }

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
    }

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
    /// A public key imported once, which verifies signatures as <see cref="RecordSigning.Verify"/> does, since that is how it verifies: nothing
    /// when the text is not a usable P-256 key, when the signature is not 64 bytes, or when the platform rejects the key or the signature.
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
            catch (CryptographicException)
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
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose() => key?.Dispose();
    }
}
