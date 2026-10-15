// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Vericairn.Records.Signing;

/// <summary>Holds a device's private signing key so that signing never exposes the key bytes to the caller.</summary>
public interface IDeviceKeyStore
{
    /// <summary>Custody kind recorded in device records: how strongly the key is held on this platform.</summary>
    string Kind { get; }

    /// <summary>True when this machine holds the key of the device.</summary>
    bool Has(string deviceId);

    /// <summary>Creates a new key for the device and returns its public key as SubjectPublicKeyInfo bytes.</summary>
    byte[] Create(string deviceId);

    /// <summary>The device's public key as SubjectPublicKeyInfo bytes, when held.</summary>
    byte[]? PublicKey(string deviceId);

    /// <summary>Signs a 32-byte digest with the device key (IEEE P1363 fixed-field signature, 64 bytes).</summary>
    byte[] Sign(string deviceId, ReadOnlySpan<byte> digest);

    /// <summary>Removes the key; the device record stays and is revoked separately.</summary>
    void Remove(string deviceId);
}

/// <summary>Chooses the key store for this platform.</summary>
public static class DeviceKeyStores
{
    /// <summary>Custody kinds a device record of record format 1 may claim.</summary>
    public static readonly string[] Kinds = ["windows-cng", "macos-keychain", "linux-secret-service", "file-owner-only"];

    /// <summary>Custody kinds a device record of record format 2 may claim: format 1's and the macOS Secure Enclave.</summary>
    public static readonly string[] Format2Kinds = [.. Kinds, SecureEnclaveCustody.Kind];

    /// <summary>Custody kinds that can enforce their owner's presence at every signature.</summary>
    public static readonly string[] PresenceKinds = [SecureEnclaveCustody.Kind];

    /// <summary>The default store for this platform: CNG on Windows (no key file exists), an owner-only file elsewhere.</summary>
    public static IDeviceKeyStore Default() => OperatingSystem.IsWindows() ? new CngKeyStore() : new FileKeyStore(VericairnHome.Path());

    /// <summary>A store by custody name: cng, secure-enclave, or file; presence only where the custody enforces it.</summary>
    public static IDeviceKeyStore Named(string? custody, bool presence = false)
    {
        IDeviceKeyStore store = custody switch
        {
            null or "" or "default" => Default(),
            "file" or "file-owner-only" => new FileKeyStore(VericairnHome.Path()),
            "cng" or "windows-cng" when OperatingSystem.IsWindows() => new CngKeyStore(),
            "cng" or "windows-cng" => throw new PlatformNotSupportedException("CNG custody exists on Windows only."),
            "secure-enclave" or SecureEnclaveCustody.Kind when OperatingSystem.IsMacOS() && SecureEnclaveKeyStore.Available() => new SecureEnclaveKeyStore(VericairnHome.Path(), presence),
            "secure-enclave" or SecureEnclaveCustody.Kind => throw new PlatformNotSupportedException($"Secure Enclave custody needs a Mac with a Secure Enclave and the {SecureEnclaveCustody.HelperName} helper beside the command line."),
            _ => throw new ArgumentException($"Unknown custody '{custody}'. Use file, secure-enclave on a Mac, or cng on Windows.", nameof(custody)),
        };
        if (presence && Array.IndexOf(PresenceKinds, store.Kind) < 0)
        {
            throw new ArgumentException($"{store.Kind} custody cannot enforce its owner's presence, and custody never claims presence it cannot enforce.", nameof(presence));
        }

        return store;
    }

    /// <summary>Every store that could hold keys on this machine, default first.</summary>
    public static IReadOnlyList<IDeviceKeyStore> All()
    {
        if (OperatingSystem.IsWindows())
        {
            return [new CngKeyStore(), new FileKeyStore(VericairnHome.Path())];
        }

        if (OperatingSystem.IsMacOS() && SecureEnclaveCustody.HelperPath() is not null)
        {
            return [new FileKeyStore(VericairnHome.Path()), new SecureEnclaveKeyStore(VericairnHome.Path())];
        }

        return [new FileKeyStore(VericairnHome.Path())];
    }
}

/// <summary>Keys as PKCS#8 files readable by the owner only. The weakest custody: a copied file is usable; recorded as such.</summary>
public sealed class FileKeyStore : IDeviceKeyStore
{
    private readonly string directory;

    /// <summary>Creates a store under the given home directory.</summary>
    public FileKeyStore(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        directory = Path.Combine(home, "keys");
    }

    /// <inheritdoc />
    public string Kind => "file-owner-only";

    /// <inheritdoc />
    public bool Has(string deviceId) => File.Exists(PathOf(deviceId));

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string path = PathOf(deviceId);
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"A key for device {deviceId} already exists here.");
        }

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] secret = key.ExportPkcs8PrivateKey();
        try
        {
            using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(secret);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        return key.ExportSubjectPublicKeyInfo();
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId)
    {
        using ECDsa? key = Load(deviceId);
        return key?.ExportSubjectPublicKeyInfo();
    }

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        using ECDsa key = Load(deviceId) ?? throw new InvalidOperationException($"This machine holds no key for device {deviceId}.");
        return key.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public void Remove(string deviceId)
    {
        string path = PathOf(deviceId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private ECDsa? Load(string deviceId)
    {
        string path = PathOf(deviceId);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] secret = File.ReadAllBytes(path);
        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(secret, out _);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private string PathOf(string deviceId)
    {
        if (!Identity.Ulid.IsValid(deviceId))
        {
            throw new ArgumentException("A device id is a ULID.", nameof(deviceId));
        }

        return Path.Combine(directory, deviceId + ".p8");
    }
}

/// <summary>Keys in the Windows software key storage provider, non-exportable: no key file exists to copy.</summary>
[SupportedOSPlatform("windows")]
public sealed class CngKeyStore : IDeviceKeyStore
{
    private const string Prefix = "vericairn-device-";

    /// <inheritdoc />
    public string Kind => "windows-cng";

    /// <inheritdoc />
    public bool Has(string deviceId) => CngKey.Exists(Name(deviceId), CngProvider.MicrosoftSoftwareKeyStorageProvider);

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        if (Has(deviceId))
        {
            throw new InvalidOperationException($"A key for device {deviceId} already exists here.");
        }

        var parameters = new CngKeyCreationParameters
        {
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
        };
        using CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, Name(deviceId), parameters);
        using var ecdsa = new ECDsaCng(key);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId)
    {
        if (!Has(deviceId))
        {
            return null;
        }

        using CngKey key = CngKey.Open(Name(deviceId), CngProvider.MicrosoftSoftwareKeyStorageProvider);
        using var ecdsa = new ECDsaCng(key);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (!Has(deviceId))
        {
            throw new InvalidOperationException($"This machine holds no key for device {deviceId}.");
        }

        using CngKey key = CngKey.Open(Name(deviceId), CngProvider.MicrosoftSoftwareKeyStorageProvider);
        using var ecdsa = new ECDsaCng(key);
        return ecdsa.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public void Remove(string deviceId)
    {
        if (!Has(deviceId))
        {
            return;
        }

        using CngKey key = CngKey.Open(Name(deviceId), CngProvider.MicrosoftSoftwareKeyStorageProvider);
        key.Delete();
    }

    private static string Name(string deviceId)
    {
        if (!Identity.Ulid.IsValid(deviceId))
        {
            throw new ArgumentException("A device id is a ULID.", nameof(deviceId));
        }

        return Prefix + deviceId;
    }
}
