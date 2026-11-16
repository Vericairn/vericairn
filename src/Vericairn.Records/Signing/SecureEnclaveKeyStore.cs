// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Vericairn.Records.Signing;

/// <summary>The names and the helper of the macOS Secure Enclave custody, readable on every platform.</summary>
public static class SecureEnclaveCustody
{
    /// <summary>The custody kind recorded in device records, from record format 2.</summary>
    public const string Kind = "macos-secure-enclave";

    /// <summary>The helper's file name, beside the command line.</summary>
    public const string HelperName = "vericairn-enclave";

    /// <summary>Overrides where the helper is, for development builds and tests; from 0.9.4 a release build ignores it (ID-16).</summary>
    public const string HelperVariable = "VERICAIRN_ENCLAVE_HELPER";

    /// <summary>
    /// The helper's path: in a development build the override, else beside the running executable; from 0.9.4 a release build always
    /// uses the helper beside the running executable (design/presence-sessions.md § 11). Null when absent.
    /// </summary>
    public static string? HelperPath() => HelperPath(PresenceSessionBuild.Current);

    /// <summary>The helper's path for a build: a release build ignores <see cref="HelperVariable"/>.</summary>
    internal static string? HelperPath(PresenceSessionBuild build) => HelperPath(build, Environment.GetEnvironmentVariable(HelperVariable));

    /// <summary>The helper's path for a build and what <see cref="HelperVariable"/> names: a release build ignores the variable.</summary>
    internal static string? HelperPath(PresenceSessionBuild build, string? variable)
    {
        ArgumentNullException.ThrowIfNull(build);
        string? configured = build.Release ? null : variable;
        string path = string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, HelperName) : configured;
        return File.Exists(path) ? path : null;
    }
}

/// <summary>
/// Keys generated inside the macOS Secure Enclave and kept as their enclave-wrapped blobs, through the vericairn-enclave helper
/// beside the command line. A blob signs nothing on any other Mac; a key created with presence asks for Touch ID or the login
/// password at every signature. The helper uses CryptoKit, so neither it nor the command line needs a keychain entitlement.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class SecureEnclaveKeyStore : IDeviceKeyStore
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WithPerson = DeviceKeyStores.PresenceWait;
    private readonly string directory;
    private readonly string helper;

    /// <summary>Creates a store under the given home directory; keys it creates need presence when asked.</summary>
    public SecureEnclaveKeyStore(string home, bool presence = false, string? helper = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        directory = Path.Combine(home, "keys");
        this.helper = helper ?? SecureEnclaveCustody.HelperPath() ?? throw new PlatformNotSupportedException($"The Secure Enclave custody needs the {SecureEnclaveCustody.HelperName} helper beside the command line.");
        Presence = presence;
    }

    /// <summary>Whether keys this store creates ask for their owner's presence at every signature.</summary>
    public bool Presence { get; }

    /// <inheritdoc />
    public string Kind => SecureEnclaveCustody.Kind;

    /// <summary>True when the helper is present and this Mac has a Secure Enclave the process can use.</summary>
    public static bool Available()
    {
        string? path = SecureEnclaveCustody.HelperPath();
        return path is not null && Run(path, Quick, "available").Code == 0;
    }

    /// <inheritdoc />
    public bool Has(string deviceId) => File.Exists(Blob(deviceId));

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        string blob = Blob(deviceId);
        if (File.Exists(blob))
        {
            throw new IOException($"A key for device {deviceId} already exists; keys are never overwritten.");
        }

        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        (int code, string output) = Presence ? Run(helper, Quick, "create", blob, "--presence") : Run(helper, Quick, "create", blob);
        return code == 0 ? Convert.FromBase64String(output) : throw new InvalidOperationException(output);
    }

    /// <inheritdoc />
    public byte[]? PublicKey(string deviceId)
    {
        if (!Has(deviceId))
        {
            return null;
        }

        (int code, string output) = Run(helper, Quick, "public", Blob(deviceId));
        return code == 0 ? Convert.FromBase64String(output) : throw new InvalidOperationException(output);
    }

    /// <inheritdoc />
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest) => Sign(deviceId, digest, PresenceReason.Default);

    /// <inheritdoc />
    /// <remarks>
    /// From 0.9.3 (FIX-124) the helper shows the reason in the system dialog of a key that asks for presence, held to
    /// <see cref="PresenceReason.Text"/> here as well, so no secret or machine path reaches the dialog whatever the caller passed.
    /// </remarks>
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest, string reason)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("The digest is 32 bytes.", nameof(digest));
        }

        string hex = Convert.ToHexString(digest).ToLowerInvariant();
        (int code, string output) = Run(helper, WithPerson, "sign", Blob(deviceId), hex, PresenceReason.Text(reason));
        byte[] signature = code == 0 ? Convert.FromBase64String(output) : throw new InvalidOperationException(output);
        return signature.Length == RecordSigning.SignatureBytes ? signature : throw new InvalidOperationException("The Secure Enclave returned a signature of an unexpected size.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// From 0.9.4 (ID-16) the helper's sign-record reads the record's canonical bytes on its standard input, checks they are in the
    /// compact canonical form, hashes them itself and opens its prompt with "sign Vericairn record &lt;type&gt; &lt;short hash&gt;:", then
    /// the reason (design/presence-sessions.md § 8); the digest-only sign stays for request signatures and pairing proofs.
    /// </remarks>
    public byte[] SignRecord(string deviceId, ReadOnlySpan<byte> content, ReadOnlySpan<byte> digest, string? reason)
    {
        if (digest.Length != 32 || !System.Security.Cryptography.SHA256.HashData(content).AsSpan().SequenceEqual(digest))
        {
            throw new ArgumentException("The digest is the SHA-256 of the record's canonical bytes.", nameof(digest));
        }

        // The helper's own label begins the dialog's text, so the runtime's sentence follows it without its own "sign Vericairn".
        string sentence = PresenceReason.Text(reason);
        const string Repeated = "sign Vericairn ";
        if (sentence.StartsWith(Repeated, StringComparison.Ordinal)) sentence = sentence[Repeated.Length..];
        (int code, string output) = Run(helper, WithPerson, content.ToArray(), "sign-record", Blob(deviceId), sentence);
        byte[] signature = code == 0 ? Convert.FromBase64String(output) : throw new InvalidOperationException(output);
        return signature.Length == RecordSigning.SignatureBytes ? signature : throw new InvalidOperationException("The Secure Enclave returned a signature of an unexpected size.");
    }

    /// <summary>From 0.9.4: the helper this store runs.</summary>
    internal string Helper => helper;

    /// <summary>From 0.9.4: where the key held under an id is kept, the blob a presence session's helper is started with.</summary>
    internal string BlobPath(string keyId) => Blob(keyId);

    /// <inheritdoc />
    public void Remove(string deviceId)
    {
        string blob = Blob(deviceId);
        if (File.Exists(blob))
        {
            File.Delete(blob);
        }
    }

    /// <inheritdoc />
    public void Promote(string keyId, string deviceId)
    {
        string from = Blob(keyId);
        if (!File.Exists(from))
        {
            throw new InvalidOperationException($"This machine holds no key {keyId} to promote.");
        }

        File.Move(from, Blob(deviceId), overwrite: true);
    }

    private string Blob(string deviceId)
    {
        if (!Identity.Ulid.IsValid(deviceId))
        {
            throw new ArgumentException("A device id is a ULID.", nameof(deviceId));
        }

        return Path.Combine(directory, deviceId + ".se");
    }

    private static (int Code, string Output) Run(string helper, TimeSpan timeout, params string[] arguments) => Run(helper, timeout, null, arguments);

    private static (int Code, string Output) Run(string helper, TimeSpan timeout, byte[]? input, params string[] arguments)
    {
        var start = new ProcessStartInfo(helper) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"The {SecureEnclaveCustody.HelperName} helper did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            try
            {
                process.StandardInput.BaseStream.Write(input);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // A helper that refused its arguments exits without reading; its exit says why.
            }
        }

        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return (4, $"The {SecureEnclaveCustody.HelperName} helper did not answer within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        return process.ExitCode == 0 ? (0, output.Result.Trim()) : (process.ExitCode, error.Result.Trim());
    }
}
