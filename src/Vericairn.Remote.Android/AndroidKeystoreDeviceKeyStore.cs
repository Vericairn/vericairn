// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Android.Security.Keystore;
using Java.Security;
using Java.Security.Spec;
using Vericairn.Records.Signing;

namespace Vericairn.Remote.Android;

/// <summary>
/// Device keys generated in Android Keystore: P-256, never exported, hardware-backed where the device provides it. Signing never
/// exposes key bytes; the keystore signs the digest it is given.
/// </summary>
public sealed class AndroidKeystoreDeviceKeyStore : IDeviceKeyStore
{
    private const string Provider = "AndroidKeyStore";

    /// <inheritdoc />
    public string Kind => DeviceKeyStores.AndroidKeystoreKind;

    /// <inheritdoc />
    public bool Has(string deviceId) => Store().ContainsAlias(Alias(deviceId));

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        if (Has(deviceId)) throw new InvalidOperationException($"This device already holds a key for device {deviceId}.");
        KeyPairGenerator generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmEc, Provider)
            ?? throw new InvalidOperationException("Android Keystore offers no EC key generator.");
        KeyGenParameterSpec spec = new KeyGenParameterSpec.Builder(Alias(deviceId), KeyStorePurpose.Sign)
            .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))!
            .SetDigests(KeyProperties.DigestNone)!
            .Build()!;
        generator.Initialize(spec);
        KeyPair pair = generator.GenerateKeyPair() ?? throw new InvalidOperationException("Android Keystore did not create the key.");
        return pair.Public?.GetEncoded() ?? throw new InvalidOperationException("Android Keystore gave no public key.");
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId) => Store().GetCertificate(Alias(deviceId))?.PublicKey?.GetEncoded();

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32) throw new ArgumentException("A device signs a 32-byte digest.", nameof(digest));
        if (Store().GetKey(Alias(deviceId), null) is not IPrivateKey key) throw new InvalidOperationException($"This device holds no key for device {deviceId}.");
        Signature signature = Signature.GetInstance("NONEwithECDSA") ?? throw new InvalidOperationException("Android Keystore offers no ECDSA signature.");
        signature.InitSign(key);
        signature.Update(digest.ToArray());
        return PhoneKeyEncoding.P1363FromDer(signature.Sign() ?? throw new InvalidOperationException("Android Keystore did not sign."));
    }

    /// <inheritdoc />
    public void Remove(string deviceId)
    {
        KeyStore store = Store();
        if (store.ContainsAlias(Alias(deviceId))) store.DeleteEntry(Alias(deviceId));
    }

    /// <summary>Rotating a phone's key is not built yet: pair the phone again instead.</summary>
    public void Promote(string keyId, string deviceId) =>
        throw new NotSupportedException("Rotating a phone's device key is not built yet; pair the phone again, and revoke the old device.");

    private static string Alias(string deviceId) => "vericairn-device-" + deviceId;

    private static KeyStore Store()
    {
        KeyStore store = KeyStore.GetInstance(Provider) ?? throw new InvalidOperationException("This device has no Android Keystore.");
        store.Load(null);
        return store;
    }
}
