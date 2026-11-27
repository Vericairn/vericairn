// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Daemon;

/// <summary>
/// The runtime's own identity on this machine, from 0.6.0: a P-256 key held in the strongest custody the machine offers, as a
/// device key is, separate from the runtime certificate and never handed to TLS. A device pins its public key when it pairs; on
/// every connection the identity signs the fingerprint of the certificate in use, so the certificate can be replaced without
/// pairing again, and a replaced certificate is refused by every paired device. The file beside the live store names the key and
/// its custody, never the key itself.
/// </summary>
public sealed class RuntimeIdentity
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "runtime-identity.json";

    private const string Format = "vericairn.runtime-identity/1";

    private readonly IDeviceKeyStore store;
    private readonly string key;

    private RuntimeIdentity(IDeviceKeyStore store, string key, string publicKey)
    {
        this.store = store;
        this.key = key;
        PublicKey = publicKey;
    }

    /// <summary>The identity's public key, base64url SubjectPublicKeyInfo: what a paired device pins.</summary>
    public string PublicKey { get; }

    /// <summary>The identity's fingerprint, sha256:hex of its public key, for comparing by eye.</summary>
    public string Fingerprint => RuntimeIdentityScheme.Fingerprint(PublicKey);

    /// <summary>How the identity's key is held.</summary>
    public string Custody => store.Kind;

    /// <summary>
    /// The identity kept for the live store, or a new one when none is kept or a non-enclave key is no longer held here. A saved
    /// Secure Enclave identity that cannot be read refuses startup. A new identity requires paired devices to pair again, which
    /// <paramref name="log"/> is told when it changes. A new key uses the given store or the strongest available custody and never
    /// asks for presence, since the runtime signs unattended.
    /// </summary>
    public static RuntimeIdentity LoadOrCreate(string directory, IDeviceKeyStore? store = null, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            RuntimeIdentity? existing = Read(path, store);
            if (existing is not null)
            {
                return existing;
            }

            log?.Invoke($"The runtime identity named in {path} is no longer held on this machine; a new identity is made, and every paired device pairs again.");
        }

        return Create(directory, store);
    }

    /// <summary>
    /// Replaces the identity: for a key that may be known to someone else. The old key is removed from custody and a new one made;
    /// no paired device accepts the runtime afterwards until it pairs again.
    /// </summary>
    public RuntimeIdentity Replace(string directory, IDeviceKeyStore? store = null)
    {
        RuntimeIdentity next = Create(directory, store ?? this.store);
        try
        {
            this.store.Remove(key);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            // The replaced key is no longer named anywhere; removing it is tidiness, not the protection.
        }

        return next;
    }

    /// <summary>
    /// Replaces a saved Secure Enclave identity whose key is missing or cannot be opened on this Mac. The caller holds the estate's
    /// writer lock alone. The previous blob, if present, can still be used on the Mac that made it.
    /// </summary>
    internal static RuntimeIdentity ReplaceUnreadable(string directory) =>
        Read(Path.Combine(directory, FileName), store: null, replaceUnreadable: true)!;

    /// <summary>The identity's proof for a client's nonce on a connection that presented a certificate and was presented a client certificate.</summary>
    public RuntimeIdentityProof Prove(string estate, string certificate, string client, string nonce)
    {
        byte[] signature = store.Sign(key, RuntimeIdentityScheme.Digest(estate, certificate, client, nonce));
        return new RuntimeIdentityProof(estate, PublicKey, certificate, client, nonce, Base64Url.EncodeToString(signature));
    }

    private static RuntimeIdentity Create(string directory, IDeviceKeyStore? store, bool confirmKey = false)
    {
        Directory.CreateDirectory(directory);
        IDeviceKeyStore target = store ?? DeviceKeyStores.Default(presence: false);
        string id = Ulid.NewUlid().Value;
        byte[] spki = target.Create(id);
        if (confirmKey && (target.PublicKey(id) is not byte[] held || !spki.AsSpan().SequenceEqual(held)))
        {
            throw new InvalidOperationException("The new Secure Enclave key could not be read back. Check the helper and retry.");
        }

        string publicKey = RecordSigning.EncodePublicKey(spki);
        var node = new JsonObject { ["format"] = Format, ["key"] = id, ["custody"] = target.Kind, ["publicKey"] = publicKey };
        string path = Path.Combine(directory, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, overwrite: true);
        return new RuntimeIdentity(target, id, publicKey);
    }

    private static RuntimeIdentity? Read(string path, IDeviceKeyStore? store, bool replaceUnreadable = false)
    {
        // A failure reading or parsing the file cannot establish that the old identity is gone.
        const string InvalidRecord = "The saved runtime identity record could not be read. Restore it from a backup and retry.";
        JsonObject? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(InvalidRecord);
        }

        if (node is null || node["custody"] is not JsonValue custodyValue || !custodyValue.TryGetValue(out string? custody) ||
            string.IsNullOrWhiteSpace(custody))
        {
            throw new InvalidOperationException(InvalidRecord);
        }

        bool enclave = custody is SecureEnclaveCustody.Kind or "secure-enclave";
        try
        {
            if (replaceUnreadable && !enclave)
            {
                throw new InvalidOperationException("--replace-unreadable requires a saved Secure Enclave runtime identity.");
            }

            string? id = node["key"]?.GetValue<string>();
            string? publicKey = node["publicKey"]?.GetValue<string>();
            if (id is null || publicKey is null || !Ulid.IsValid(id))
            {
                return enclave
                    ? throw new InvalidOperationException("The saved Secure Enclave runtime identity record is incomplete. Restore it from a backup and retry.")
                    : null;
            }

            if (enclave)
            {
                IDeviceKeyStore heldEnclave = store?.Kind == SecureEnclaveCustody.Kind ? store : EnclaveStore();
                return ReadEnclave(id, publicKey, heldEnclave, replaceUnreadable ? Path.GetDirectoryName(path)! : null);
            }

            IDeviceKeyStore? held = store is not null && store.Kind == custody ? store : Named(custody);
            return held is not null && held.Has(id) && held.PublicKey(id) is byte[] spki && RecordSigning.EncodePublicKey(spki) == publicKey
                ? new RuntimeIdentity(held, id, publicKey)
                : null;
        }
        catch (Exception exception) when (!enclave && !replaceUnreadable && exception is not EnclaveHelperMismatchException &&
            exception is (JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException))
        {
            return null;
        }
    }

    private static SecureEnclaveKeyStore EnclaveStore()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new InvalidOperationException("This runtime identity uses macOS Secure Enclave custody. Use the Mac that holds the key.");
        }

        string? helper = SecureEnclaveCustody.HelperPath();
        if (helper is null)
        {
            throw new InvalidOperationException("The Secure Enclave helper is missing. Put the vericairn-enclave from this command line's release archive beside it and retry.");
        }

        return SecureEnclaveKeyStore.Probe(helper) switch
        {
            true => new SecureEnclaveKeyStore(VericairnHome.Path(), helper: helper),
            false => throw SecureEnclaveKeyStore.EnclaveUnavailable(),
            null => throw EnclaveHelperCheck.Refuse(EnclaveHelperReport.FailedAvailability.Description),
        };
    }

    private static RuntimeIdentity ReadEnclave(string id, string publicKey, IDeviceKeyStore store, string? replacementDirectory)
    {
        byte[]? spki = null;
        string? problem;
        try
        {
            spki = store.PublicKey(id);
            problem = spki is null ? "The runtime could not find the runtime identity's key blob. Restore the blob on the Mac that holds the key and retry." : null;
        }
        catch (EnclaveKeyUnavailableException)
        {
            problem = "The Secure Enclave helper could not open the runtime identity key on this Mac (helper exit 4). Use the original Mac that holds the key.";
        }

        if (problem is not null)
        {
            if (replacementDirectory is not null)
            {
                return Create(replacementDirectory, store, confirmKey: true);
            }

            throw new InvalidOperationException(problem + " To choose a new identity here while the runtime is stopped, run 'vericairn runtime rotate --identity --replace-unreadable'. Every paired device must pair again.");
        }

        if (RecordSigning.EncodePublicKey(spki!) != publicKey)
        {
            throw new InvalidOperationException("The Secure Enclave key does not match the saved runtime identity. Restore the matching key blob and identity record from a backup and retry.");
        }

        if (replacementDirectory is not null)
        {
            throw new InvalidOperationException("The saved runtime identity is already readable. Use 'vericairn runtime rotate --identity' to replace it.");
        }

        return new RuntimeIdentity(store, id, publicKey);
    }

    private static IDeviceKeyStore? Named(string custody)
    {
        try
        {
            return DeviceKeyStores.Named(custody);
        }
        catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
