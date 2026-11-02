// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Foundation;
using Security;
using Vericairn.Records.Signing;

namespace Vericairn.Remote.iOS;

/// <summary>
/// Device keys generated inside this iPhone's or iPad's Secure Enclave: P-256, usable only on this device while it is unlocked,
/// never exported, never in a backup. Signing never exposes key bytes; the Secure Enclave signs the digest it is given. The request
/// store's keys sign without asking; the presence store's keys need the owner's presence (Face ID, Touch ID or the passcode) at every
/// signature, enforced by the Secure Enclave's access control, and exist only while the device has a passcode.
/// </summary>
public sealed class SecureEnclaveDeviceKeyStore : IDeviceKeyStore
{
    private readonly string tagPrefix;
    private readonly bool presence;

    /// <summary>The request store: keys that sign every request without asking for presence.</summary>
    public SecureEnclaveDeviceKeyStore()
        : this(presence: false)
    {
    }

    /// <summary>The request store, or with <paramref name="presence"/> the presence store, whose keys are kept apart under their own tags.</summary>
    public SecureEnclaveDeviceKeyStore(bool presence)
    {
        this.presence = presence;
        tagPrefix = presence ? "com.vericairn.remote.presence." : "com.vericairn.remote.device.";
    }

    /// <inheritdoc />
    public string Kind => DeviceKeyStores.IosSecureEnclaveKind;

    /// <inheritdoc />
    public bool Has(string deviceId)
    {
        using SecKey? key = Find(deviceId);
        return key is not null;
    }

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        if (Has(deviceId)) throw new InvalidOperationException($"This device already holds a key for device {deviceId}.");
        using var access = presence
            ? new SecAccessControl(SecAccessible.WhenPasscodeSetThisDeviceOnly, SecAccessControlCreateFlags.PrivateKeyUsage | SecAccessControlCreateFlags.UserPresence)
            : new SecAccessControl(SecAccessible.WhenUnlockedThisDeviceOnly, SecAccessControlCreateFlags.PrivateKeyUsage);
        var parameters = new SecKeyGenerationParameters
        {
            KeyType = SecKeyType.ECSecPrimeRandom,
            KeySizeInBits = 256,
            TokenID = SecTokenID.SecureEnclave,
            PrivateKeyAttrs = new SecKeyParameters { IsPermanent = true, ApplicationTag = Tag(deviceId), AccessControl = access },
        };
        using SecKey key = SecKey.CreateRandomKey(parameters, out NSError? error)
            ?? throw new InvalidOperationException($"The Secure Enclave did not create the key: {error?.LocalizedDescription ?? "no reason given"}.");
        return PublicKeyOf(key);
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId)
    {
        using SecKey? key = Find(deviceId);
        return key is null ? null : PublicKeyOf(key);
    }

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32) throw new ArgumentException("A device signs a 32-byte digest.", nameof(digest));
        // The system's presence prompt blocks the signing thread until the person answers, so it is never the interface's thread.
        if (presence && NSThread.IsMain) throw new InvalidOperationException("A presence key does not sign on the interface's thread.");
        using SecKey key = Find(deviceId) ?? throw new InvalidOperationException($"This device holds no key for device {deviceId}.");
        using NSData data = NSData.FromArray(digest.ToArray());
        using NSData signature = key.CreateSignature(SecKeyAlgorithm.EcdsaSignatureDigestX962Sha256, data, out NSError? error)
            ?? throw new InvalidOperationException($"The Secure Enclave did not sign: {error?.LocalizedDescription ?? "no reason given"}.");
        return PhoneKeyEncoding.P1363FromDer(signature.ToArray());
    }

    /// <inheritdoc />
    public void Remove(string deviceId)
    {
        using var query = new SecRecord(SecKind.Key) { ApplicationTag = Tag(deviceId), KeyClass = SecKeyClass.Private };
        SecStatusCode status = SecKeyChain.Remove(query);
        if (status is not (SecStatusCode.Success or SecStatusCode.ItemNotFound)) throw new InvalidOperationException($"The key of device {deviceId} was not removed: {status}.");
    }

    /// <summary>Rotating a phone's key is not built yet: pair the phone again instead.</summary>
    public void Promote(string keyId, string deviceId) =>
        throw new NotSupportedException("Rotating a phone's device key is not built yet; pair the phone again, and revoke the old device.");

    private NSData Tag(string deviceId) => NSData.FromString(tagPrefix + deviceId, NSStringEncoding.UTF8);

    private SecKey? Find(string deviceId)
    {
        using var query = new SecRecord(SecKind.Key) { ApplicationTag = Tag(deviceId), KeyClass = SecKeyClass.Private };
        return SecKeyChain.QueryAsConcreteType(query, out SecStatusCode status) is SecKey key && status == SecStatusCode.Success ? key : null;
    }

    private static byte[] PublicKeyOf(SecKey privateKey)
    {
        using SecKey publicKey = privateKey.GetPublicKey() ?? throw new InvalidOperationException("The Secure Enclave gave no public key.");
        using NSData point = publicKey.GetExternalRepresentation(out NSError? error)
            ?? throw new InvalidOperationException($"The public key could not be read: {error?.LocalizedDescription ?? "no reason given"}.");
        return PhoneKeyEncoding.SubjectPublicKeyInfo(point.ToArray());
    }
}
