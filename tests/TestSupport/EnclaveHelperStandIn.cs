// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;

namespace Vericairn.TestSupport;

/// <summary>
/// From 0.9.5 (FIX-169): a stand-in for the vericairn-enclave helper, on macOS, of an earlier release's protocol or of this one's. It is a
/// shell script that answers as that release's helper answers: a command it does not serve, <c>version</c> included before 0.9.5, with
/// its usage line on standard error and exit code 2, as the helper's <c>fail(2, usage)</c> does. It has no key access: it reaches no
/// Secure Enclave, holds no key and refuses every command that would use one, except that a stand-in of this release's protocol answers a
/// signature with 64 zero bytes, so a test can see that a signature reached it. It records each command it was asked for, in order.
/// </summary>
internal sealed class EnclaveHelperStandIn : IDisposable
{
    /// <summary>The usage line of the helpers of 0.8.0 to 0.9.3, which serve no sign-record (src/Vericairn.Enclave/vericairn-enclave.swift at v0.9.3).</summary>
    internal const string UsageBefore094 = "usage: vericairn-enclave available | create <blob> [--presence] | public <blob> | sign <blob> <digest-hex> [reason]";

    /// <summary>The usage line of 0.9.4's helper, which serves sign-record and session and no version (the same file at v0.9.4).</summary>
    internal const string Usage094 = "usage: vericairn-enclave available | create <blob> [--presence] | public <blob> | sign <blob> <digest-hex> [reason] | sign-record <blob> [reason] | session <blob>";

    private readonly string directory;
    private readonly string log;

    [SupportedOSPlatform("macos")]
    private EnclaveHelperStandIn(string usage, IReadOnlyList<string> serves, string? version)
    {
        directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vericairn-enclave-stand-in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        log = System.IO.Path.Combine(directory, "asked.log");
        Path = System.IO.Path.Combine(directory, "vericairn-enclave");
        string signature = Convert.ToBase64String(new byte[64]);
        var script = new System.Text.StringBuilder();
        script.Append("#!/bin/sh\n");
        script.Append("printf '%s\\n' \"$1\" >> '").Append(log).Append("'\n");
        script.Append("case \"$1\" in\n");
        script.Append("  available) echo available; exit 0 ;;\n");
        if (version is not null)
        {
            script.Append("  version) printf '%s\\n' '{\"commands\":[").Append(string.Join(',', serves.Order(StringComparer.Ordinal).Select(c => "\"" + c + "\"")))
                .Append("],\"version\":\"").Append(version).Append("\"}'; exit 0 ;;\n");
            script.Append("  sign|sign-record) echo ").Append(signature).Append("; exit 0 ;;\n");
        }

        foreach (string command in serves.Where(c => c is not ("available" or "version") && (version is null || c is not ("sign" or "sign-record"))))
            script.Append("  ").Append(command).Append(") echo 'The stand-in holds no key.' >&2; exit 4 ;;\n");
        script.Append("esac\n");
        script.Append("echo '").Append(usage).Append("' >&2\n");
        script.Append("exit 2\n");
        File.WriteAllText(Path, script.ToString());
        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>The stand-in's path, to run as the helper.</summary>
    internal string Path { get; }

    /// <summary>The commands it was asked for, in order.</summary>
    internal IReadOnlyList<string> Asked => File.Exists(log) ? File.ReadAllLines(log) : [];

    /// <summary>A helper of 0.8.0 to 0.9.3: no version and no sign-record.</summary>
    [SupportedOSPlatform("macos")]
    internal static EnclaveHelperStandIn Before094() => new(UsageBefore094, ["available", "create", "public", "sign"], null);

    /// <summary>0.9.4's helper: sign-record and session, and no version.</summary>
    [SupportedOSPlatform("macos")]
    internal static EnclaveHelperStandIn Of094() => new(Usage094, ["available", "create", "public", "sign", "sign-record", "session"], null);

    /// <summary>A helper that answers version with the version and commands given, as one from 0.9.5 on does.</summary>
    [SupportedOSPlatform("macos")]
    internal static EnclaveHelperStandIn Reporting(string version, params string[] commands) =>
        new("usage: vericairn-enclave " + string.Join(" | ", commands), commands.Contains("version") ? commands : [.. commands, "version"], version);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
