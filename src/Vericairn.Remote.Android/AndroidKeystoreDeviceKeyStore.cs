// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Java.Security.Spec;
using Vericairn.Records.Signing;

namespace Vericairn.Remote.Android;

/// <summary>
/// Device keys generated in Android Keystore: P-256, never exported, hardware-backed where the device provides it. Signing never
/// exposes key bytes; the keystore signs the digest it is given. The request store's keys sign without asking; the presence store's
/// keys need the owner's strong biometric or device credential at every signature (per-use authentication, Android 11 and later),
/// enforced by the keystore, and exist only while the device has a secure lock screen.
/// </summary>
public sealed class AndroidKeystoreDeviceKeyStore : IDeviceKeyStore
{
    private const string Provider = "AndroidKeyStore";
    private readonly string aliasPrefix;
    private readonly bool presence;

    /// <summary>The request store: keys that sign every request without asking for presence.</summary>
    public AndroidKeystoreDeviceKeyStore()
        : this(presence: false)
    {
    }

    /// <summary>The request store, or with <paramref name="presence"/> the presence store, whose keys are kept apart under their own aliases.</summary>
    public AndroidKeystoreDeviceKeyStore(bool presence)
    {
        this.presence = presence;
        aliasPrefix = presence ? "vericairn-presence-" : "vericairn-device-";
    }

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
        KeyGenParameterSpec.Builder builder = new KeyGenParameterSpec.Builder(Alias(deviceId), KeyStorePurpose.Sign)
            .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))!
            .SetDigests(KeyProperties.DigestNone)!;
        if (presence)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30)) throw new PlatformNotSupportedException("A presence key needs Android 11 or later.");
            Presence(builder);
        }

        try
        {
            generator.Initialize(builder.Build()!);
            KeyPair pair = generator.GenerateKeyPair() ?? throw new InvalidOperationException("Android Keystore did not create the key.");
            return pair.Public?.GetEncoded() ?? throw new InvalidOperationException("Android Keystore gave no public key.");
        }
        catch (Java.Lang.Exception exception)
        {
            throw new InvalidOperationException($"Android Keystore did not create the key: {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId) => Store().GetCertificate(Alias(deviceId))?.PublicKey?.GetEncoded();

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32) throw new ArgumentException("A device signs a 32-byte digest.", nameof(digest));
        if (Store().GetKey(Alias(deviceId), null) is not IPrivateKey key) throw new InvalidOperationException($"This device holds no key for device {deviceId}.");
        try
        {
            Signature signature = Signature.GetInstance("NONEwithECDSA") ?? throw new InvalidOperationException("Android Keystore offers no ECDSA signature.");
            signature.InitSign(key);
            if (presence)
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(30)) throw new PlatformNotSupportedException("A presence key needs Android 11 or later.");
                return SignWithPresence(signature, digest.ToArray());
            }

            signature.Update(digest.ToArray());
            return PhoneKeyEncoding.P1363FromDer(signature.Sign() ?? throw new InvalidOperationException("Android Keystore did not sign."));
        }
        catch (Java.Lang.Exception exception)
        {
            throw new InvalidOperationException($"Android Keystore did not sign: {exception.Message}", exception);
        }
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

    // Per-use authentication: every signature needs a strong biometric or the device credential, confirmed for that one operation.
    [SupportedOSPlatform("android30.0")]
    private static void Presence(KeyGenParameterSpec.Builder builder) => builder
        .SetUserAuthenticationRequired(true)!
        .SetUserAuthenticationParameters(0, (int)(KeyPropertiesAuthType.BiometricStrong | KeyPropertiesAuthType.DeviceCredential));

    // The system prompt runs on the main thread and unlocks this one signature; the signing thread waits for its answer.
    [SupportedOSPlatform("android30.0")]
    private static byte[] SignWithPresence(Signature signature, byte[] digest)
    {
        if (Looper.MyLooper() == Looper.MainLooper) throw new InvalidOperationException("A presence key does not sign on the interface's thread.");
        Context context = global::Android.App.Application.Context;
        var outcome = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationSignal();
        using var main = new Handler(Looper.MainLooper!);
        main.Post(() =>
        {
            try
            {
                BiometricPrompt prompt = new BiometricPrompt.Builder(context)
                    .SetTitle("Confirm it is you")!
                    .SetDescription("Remote's presence key signs this exact record only after you confirm your presence.")!
                    .SetAllowedAuthenticators((int)(BiometricManagerAuthenticators.BiometricStrong | BiometricManagerAuthenticators.DeviceCredential))!
                    .Build()!;
                prompt.Authenticate(new BiometricPrompt.CryptoObject(signature), cancel, context.MainExecutor!, new PresenceCallback(outcome, digest));
            }
            catch (Java.Lang.Exception exception)
            {
                outcome.TrySetException(new InvalidOperationException($"The presence prompt could not be shown: {exception.Message}", exception));
            }
        });
        if (!((IAsyncResult)outcome.Task).AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(5)))
        {
            cancel.Cancel();
            throw new TimeoutException("Your presence was not confirmed within five minutes; nothing was signed.");
        }

        return outcome.Task.GetAwaiter().GetResult();
    }

    private string Alias(string deviceId) => aliasPrefix + deviceId;

    private static KeyStore Store()
    {
        KeyStore store = KeyStore.GetInstance(Provider) ?? throw new InvalidOperationException("This device has no Android Keystore.");
        store.Load(null);
        return store;
    }

    /// <summary>Signs once the person is authenticated for this signature; any refusal or error signs nothing.</summary>
    [SupportedOSPlatform("android30.0")]
    private sealed class PresenceCallback(TaskCompletionSource<byte[]> outcome, byte[] digest) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        {
            try
            {
                Signature signature = result?.CryptoObject?.Signature ?? throw new InvalidOperationException("The prompt returned no signature to complete.");
                signature.Update(digest);
                outcome.TrySetResult(PhoneKeyEncoding.P1363FromDer(signature.Sign() ?? throw new InvalidOperationException("Android Keystore did not sign.")));
            }
            catch (Java.Lang.Exception exception)
            {
                outcome.TrySetException(new InvalidOperationException($"Android Keystore did not sign: {exception.Message}", exception));
            }
            catch (InvalidOperationException exception)
            {
                outcome.TrySetException(exception);
            }
        }

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            outcome.TrySetException(new InvalidOperationException($"Your presence was not confirmed ({errorCode}): {errString}. Nothing was signed."));
    }
}
