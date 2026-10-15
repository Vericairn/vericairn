// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Records.Signing;

/// <summary>
/// The key of the operator's recovery device. It is made in memory and written once to a file the operator names, as an encrypted
/// PKCS#8 key (PBES2: AES-256-CBC with a PBKDF2-SHA256 key from the passphrase), and no key store ever holds it. It is opened only
/// for a recovery, in memory, and signs only device records, admissions and revocations.
/// </summary>
public static class RecoveryKey
{
    /// <summary>The fewest characters a recovery passphrase has.</summary>
    public const int MinimumPassphrase = 12;

    private static readonly PbeParameters Encryption = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    /// <summary>Writes the key to a new file readable by its owner only, with a plain-text note naming the device; never overwrites a file.</summary>
    public static void Write(string path, ECDsa key, string passphrase, string deviceId, string estate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(passphrase);
        string text = $"Vericairn recovery key of device {deviceId} in estate {estate}.\n"
            + "Keep it offline and its passphrase apart from it. With the passphrase it admits and revokes the operator's devices.\n\n"
            + key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, Encryption) + "\n";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        stream.Write(bytes);
    }

    /// <summary>Opens a recovery key file with its passphrase; throws <see cref="CryptographicException"/> when the passphrase does not open it.</summary>
    public static ECDsa Open(string path, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(passphrase);
        string text = File.ReadAllText(path);
        var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(text, passphrase);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

/// <summary>Signs with a key held in memory, in a device's name: the recovery device's key, which no key store holds.</summary>
public sealed class KeySigner : IRecordSigner
{
    private readonly ECDsa key;

    /// <summary>Creates a signer for the key, naming the device.</summary>
    public KeySigner(ECDsa key, string deviceId)
    {
        this.key = key ?? throw new ArgumentNullException(nameof(key));
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
    }

    /// <inheritdoc />
    public string DeviceId { get; }

    /// <inheritdoc />
    public RecordSignature Sign(ReadOnlySpan<byte> digest) =>
        new(RecordSigning.Algorithm, DeviceId, Base64Url.EncodeToString(key.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
}
