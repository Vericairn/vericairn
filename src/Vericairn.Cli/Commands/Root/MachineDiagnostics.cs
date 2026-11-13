// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    /// <summary>One probe's finding: its state (present, missing, unavailable or not-applicable), its value and the rest of its report line.</summary>
    internal sealed record Probe(string State, string? Value, string? Detail);

    public int Doctor(string root, bool json = false)
    {
        string version = CommandContext.VersionText();
        string? git = Tool("git", "--version");
        string? dotnet = Tool("dotnet", "--version");
        bool estate = IsEstate(root);
        string full = Path.GetFullPath(root);
        long? format = estate ? EstateManifest.Load(root, new FindingList())?.RecordFormat : null;
        string custody = format is < 2 ? DeviceKeyStores.Default(enclave: false).Kind : DeviceKeyStores.Default().Kind;
        string custodyDetail = format is < 2
            ? $"the default for this estate, at record format {format}, which cannot name the Secure Enclave; {DeviceKeyStores.Default().Kind} once it is at format 2"
            : "default on this platform";
        Probe enclave = !OperatingSystem.IsMacOS() ? new("not-applicable", null, null)
            : SecureEnclaveKeyStore.Available()
                ? new("present", "secure-enclave", "Secure Enclave custody available (record format 2): a person's device asks for Touch ID or the login password at every signature unless it is created with --presence false; an agent's never asks")
                : new("unavailable", null, SecureEnclaveCustody.HelperPath() is null ? $"no {SecureEnclaveCustody.HelperName} helper beside the command line" : "no Secure Enclave available to this process");
        string records = string.Join(", ", RecordFormats.Reads);
        string writes = $"writes an estate's own format, at most {RecordFormats.Current}";
        (string? sqlite, string? searchProblem) = Vericairn.Runtime.Search.SearchEngine.Probe();
        string search = $"keyword index {Records.Search.SearchSelections.KeywordMethod} on SQLite {sqlite} with FTS5";
        List<string> state = StateProblems(root).ToList();
        DaemonEndpoint? endpoint = estate ? DaemonHost.Discover(root) : null;
        bool serving = endpoint is not null && context.IsAlive(endpoint);
        string? daemonDetail = serving
            ? $"serves this estate since {endpoint!.StartedAt} ({endpoint.Kind}{(endpoint.Loopback is null ? string.Empty : ", loopback " + endpoint.Loopback.Port)})"
            : null;

        if (json)
        {
            // The same facts as the report, under stable names; a release notice still goes to standard error.
            var result = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["version"] = new Probe("present", version, null),
                ["runtime"] = new Probe("present", $".NET {Environment.Version}", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"),
                ["git"] = git is null ? new Probe("missing", null, "not found on PATH") : new Probe("present", git, null),
                ["dotnet"] = dotnet is null ? new Probe("missing", null, "not found on PATH (needed only to build from source)") : new Probe("present", dotnet, null),
                ["estate"] = estate ? new Probe("present", full, "manifest present") : new Probe("missing", full, $"not an estate root (no {EstateLayout.ManifestPath})"),
                ["custody"] = new Probe("present", custody, $"{custodyDetail}; per-user state at {VericairnHome.Path()}"),
                ["enclave"] = enclave,
                ["records"] = new Probe("present", records, writes),
                ["search"] = searchProblem is null ? new Probe("present", search, null) : new Probe("unavailable", null, searchProblem),
                ["daemon"] = !estate ? new Probe("not-applicable", null, null)
                    : serving ? new Probe("present", endpoint!.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), daemonDetail)
                    : new Probe("missing", null, "commands run the embedded runtime"),
                ["stateWarnings"] = state,
            };
            context.Output.WriteLine(JsonSerializer.Serialize(result, ProtocolJson.Options));
            return Clean;
        }

        context.Output.WriteLine($"vericairn {version}");
        context.Output.WriteLine($"runtime   .NET {Environment.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        context.Output.WriteLine($"git       {git ?? "not found on PATH"}");
        context.Output.WriteLine($"dotnet    {dotnet ?? "not found on PATH (needed only to build from source)"}");
        context.Output.WriteLine(estate ? $"estate    {full} (manifest present)" : $"estate    {full} is not an estate root (no {EstateLayout.ManifestPath})");
        context.Output.WriteLine($"custody   {custody} ({custodyDetail}); per-user state at {VericairnHome.Path()}");
        if (OperatingSystem.IsMacOS())
        {
            context.Output.WriteLine(enclave.State == "present" ? "enclave   " + enclave.Detail : $"enclave   Secure Enclave custody unavailable ({enclave.Detail})");
        }
        context.Output.WriteLine($"records   reads record formats {records}; {writes}");
        context.Output.WriteLine(searchProblem is null ? $"search    {search}" : $"search    keyword index unavailable: {searchProblem}");
        foreach (string problem in state)
        {
            context.Output.WriteLine($"state     {problem}");
        }

        if (estate)
        {
            context.Output.WriteLine(serving ? $"daemon    process {endpoint!.ProcessId} {daemonDetail}" : "daemon    none; commands run the embedded runtime");
        }

        return Clean;
    }

    // The per-user state, the estate's live store directory and its database, when they exist: one that cannot be made owner-only is
    // named, once for its filesystem. Nothing is said when they are owner-only.
    private static IEnumerable<string> StateProblems(string root)
    {
        var paths = new List<string> { VericairnHome.Path() };
        if (IsEstate(root) && EstateManifest.Load(root, new FindingList()) is { } manifest)
        {
            string database = EstateLocations.LiveStorePath(Path.GetFullPath(root), manifest.Estate, manifest.Profile);
            paths.Add(Path.GetDirectoryName(database)!);
            paths.Add(database);
        }

        var named = new List<string>();
        foreach (string path in paths.Where(p => Directory.Exists(p) || File.Exists(p)))
        {
            if (OwnerOnlyState.Restriction(path) is not (string subject, string problem)
                || named.Any(known => subject == known || subject.StartsWith(known + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                continue;
            }

            named.Add(subject);
            yield return problem;
        }
    }

    private static string? Tool(string tool, string argument)
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
