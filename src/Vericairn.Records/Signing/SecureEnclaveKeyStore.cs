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

    /// <summary>Overrides where the helper is, for development builds and tests.</summary>
    public const string HelperVariable = "VERICAIRN_ENCLAVE_HELPER";

    /// <summary>The helper's path: the override, else beside the running executable; null when absent.</summary>
    public static string? HelperPath()
    {
        string? configured = Environment.GetEnvironmentVariable(HelperVariable);
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
    private static readonly TimeSpan WithPerson = TimeSpan.FromMinutes(5);
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
    public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("The digest is 32 bytes.", nameof(digest));
        }

        string hex = Convert.ToHexString(digest).ToLowerInvariant();
        (int code, string output) = Run(helper, WithPerson, "sign", Blob(deviceId), hex, "sign a Vericairn record");
        byte[] signature = code == 0 ? Convert.FromBase64String(output) : throw new InvalidOperationException(output);
        return signature.Length == RecordSigning.SignatureBytes ? signature : throw new InvalidOperationException("The Secure Enclave returned a signature of an unexpected size.");
    }

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

    private static (int Code, string Output) Run(string helper, TimeSpan timeout, params string[] arguments)
    {
        var start = new ProcessStartInfo(helper) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"The {SecureEnclaveCustody.HelperName} helper did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return (4, $"The {SecureEnclaveCustody.HelperName} helper did not answer within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        return process.ExitCode == 0 ? (0, output.Result.Trim()) : (process.ExitCode, error.Result.Trim());
    }
}
