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
/// every connection the identity signs the fingerprint of the certificate in use. The file beside the live store names the key and
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
    /// The identity kept for the live store in a directory, or a new one when there is none or its key is no longer held here; a
    /// new identity means every paired device pairs again, which <paramref name="log"/> is told. A new key goes into the given store,
    /// else into the strongest custody this machine offers, never asking for presence, since the runtime signs unattended.
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

    /// <summary>The identity's proof for a client's nonce on a connection that presented a certificate.</summary>
    public RuntimeIdentityProof Prove(string estate, string certificate, string nonce)
    {
        byte[] signature = store.Sign(key, RuntimeIdentityScheme.Digest(estate, certificate, nonce));
        return new RuntimeIdentityProof(estate, PublicKey, certificate, nonce, Base64Url.EncodeToString(signature));
    }

    private static RuntimeIdentity Create(string directory, IDeviceKeyStore? store)
    {
        Directory.CreateDirectory(directory);
        IDeviceKeyStore target = store ?? DeviceKeyStores.Default(presence: false);
        string id = Ulid.NewUlid().Value;
        string publicKey = RecordSigning.EncodePublicKey(target.Create(id));
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

    private static RuntimeIdentity? Read(string path, IDeviceKeyStore? store)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            string? id = node?["key"]?.GetValue<string>();
            string? custody = node?["custody"]?.GetValue<string>();
            string? publicKey = node?["publicKey"]?.GetValue<string>();
            if (id is null || custody is null || publicKey is null || !Ulid.IsValid(id))
            {
                return null;
            }

            IDeviceKeyStore? held = store is not null && store.Kind == custody ? store : Named(custody);
            return held is not null && held.Has(id) && held.PublicKey(id) is byte[] spki && RecordSigning.EncodePublicKey(spki) == publicKey
                ? new RuntimeIdentity(held, id, publicKey)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
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
