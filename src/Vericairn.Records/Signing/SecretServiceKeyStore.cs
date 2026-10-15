// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Vericairn.Records.Signing;

/// <summary>The names of the Linux secret-service custody, readable on every platform.</summary>
public static class SecretServiceCustody
{
    /// <summary>The custody kind recorded in device records; a record format 1 kind.</summary>
    public const string Kind = "linux-secret-service";

    /// <summary>The libsecret command the store runs.</summary>
    public const string Tool = "secret-tool";
}

/// <summary>
/// Linux custody in the secret service (GNOME Keyring, KWallet, KeePassXC and the like), through the libsecret <c>secret-tool</c>
/// command. The key is kept encrypted at rest under the user's keyring, so a copied home directory or backup holds no usable key;
/// while the keyring is unlocked any process of the same user can read it, which the custody kind records. A marker file under the
/// home's keys directory says which devices the secret service holds, so asking never opens the keyring; only signing does.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SecretServiceKeyStore : IDeviceKeyStore
{
    private const string Application = "vericairn";
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WithPerson = TimeSpan.FromMinutes(5);
    private readonly string directory;

    /// <summary>Creates a store whose markers live under the given home directory.</summary>
    public SecretServiceKeyStore(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        directory = Path.Combine(home, "keys");
    }

    /// <inheritdoc />
    public string Kind => SecretServiceCustody.Kind;

    /// <summary>True when secret-tool is installed and a secret service answers it without an error.</summary>
    public static bool Available()
    {
        if (ToolPath() is not string tool)
        {
            return false;
        }

        (int code, string _, string error) = Run(tool, Quick, null, "search", "application", Application, "probe", "availability");
        return code is 0 or 1 && error.Length == 0;
    }

    /// <inheritdoc />
    public bool Has(string deviceId) => File.Exists(Marker(deviceId));

    /// <inheritdoc />
    public byte[] Create(string deviceId)
    {
        if (Has(deviceId))
        {
            throw new InvalidOperationException($"A key for device {deviceId} already exists; keys are never overwritten.");
        }

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Store(deviceId, key.ExportPkcs8PrivateKey());
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
        string marker = Marker(deviceId);
        if (File.Exists(marker))
        {
            Run(RequiredTool(), Quick, null, "clear", "application", Application, "device", deviceId);
            File.Delete(marker);
        }
    }

    /// <inheritdoc />
    public void Promote(string keyId, string deviceId)
    {
        using ECDsa key = Load(keyId) ?? throw new InvalidOperationException($"This machine holds no key {keyId} to promote.");
        if (Has(deviceId))
        {
            Remove(deviceId);
        }

        Store(deviceId, key.ExportPkcs8PrivateKey());
        Remove(keyId);
    }

    private void Store(string deviceId, byte[] pkcs8)
    {
        string marker = Marker(deviceId);
        try
        {
            (int code, string _, string error) = Run(RequiredTool(), WithPerson, Convert.ToBase64String(pkcs8), "store", "--label", $"Vericairn device key {deviceId}", "application", Application, "device", deviceId);
            if (code != 0)
            {
                throw new InvalidOperationException($"The secret service did not store the key: {error}");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllBytes(marker, []);
        File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private ECDsa? Load(string deviceId)
    {
        if (!Has(deviceId))
        {
            return null;
        }

        (int code, string output, string error) = Run(RequiredTool(), WithPerson, null, "lookup", "application", Application, "device", deviceId);
        if (code != 0 || output.Length == 0)
        {
            throw new InvalidOperationException($"The secret service did not return the key of device {deviceId}: {(error.Length == 0 ? "it holds none" : error)}");
        }

        byte[] pkcs8 = Convert.FromBase64String(output);
        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private string Marker(string deviceId) =>
        Identity.Ulid.IsValid(deviceId) ? Path.Combine(directory, deviceId + ".secret") : throw new ArgumentException("A device id is a ULID.", nameof(deviceId));

    private static string? ToolPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, SecretServiceCustody.Tool)).FirstOrDefault(File.Exists);

    private static string RequiredTool() => ToolPath() ?? throw new PlatformNotSupportedException($"Secret-service custody needs {SecretServiceCustody.Tool} (libsecret) and a running secret service.");

    private static (int Code, string Output, string Error) Run(string tool, TimeSpan timeout, string? input, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{SecretServiceCustody.Tool} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.Write(input);
        }

        process.StandardInput.Close();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return (4, string.Empty, $"{SecretServiceCustody.Tool} did not answer within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        return (process.ExitCode, output.Result.Trim(), error.Result.Trim());
    }
}
