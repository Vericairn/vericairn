// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class MachineDiagnostics(CommandContext context)
{
    public int Doctor(string root)
    {
        context.Output.WriteLine($"vericairn {CommandContext.VersionText()}");
        context.Output.WriteLine($"runtime   .NET {Environment.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        context.Output.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        context.Output.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        context.Output.WriteLine(IsEstate(root) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        long? format = IsEstate(root) ? EstateManifest.Load(root, new FindingList())?.RecordFormat : null;
        context.Output.WriteLine(format is < 2
            ? $"custody   {DeviceKeyStores.Default(enclave: false).Kind} (the default for this estate, at record format {format}, which cannot name the Secure Enclave; {DeviceKeyStores.Default().Kind} once it is at format 2); per-user state at {VericairnHome.Path()}"
            : $"custody   {DeviceKeyStores.Default().Kind} (default on this platform); per-user state at {VericairnHome.Path()}");
        if (OperatingSystem.IsMacOS())
        {
            context.Output.WriteLine(SecureEnclaveKeyStore.Available()
                ? "enclave   Secure Enclave custody available (record format 2): a person's device asks for Touch ID or the login password at every signature unless it is created with --presence false; an agent's never asks"
                : $"enclave   Secure Enclave custody unavailable ({(SecureEnclaveCustody.HelperPath() is null ? $"no {SecureEnclaveCustody.HelperName} helper beside the command line" : "no Secure Enclave available to this process")})");
        }
        context.Output.WriteLine($"records   reads record formats {string.Join(", ", RecordFormats.Reads)}; writes an estate's own format, at most {RecordFormats.Current}");
        if (IsEstate(root))
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            context.Output.WriteLine(endpoint is not null && VericairnClient.IsAlive(endpoint)
                ? $"daemon    process {endpoint.ProcessId} serves this estate since {endpoint.StartedAt} ({endpoint.Kind}{(endpoint.Loopback is null ? string.Empty : ", loopback " + endpoint.Loopback.Port)})"
                : "daemon    none; commands run the embedded runtime");
        }

        return Clean;
    }

    private static string? Probe(string tool, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { argument }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Split('\n')[0] : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
