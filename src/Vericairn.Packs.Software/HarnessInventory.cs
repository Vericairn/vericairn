// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Evidence shared by the two version-qualified native launchers; no ambient defaults are admission evidence.</summary>
internal static class HarnessInventory
{
    public static void CommandPrefix(LaunchContext context, string executable)
    {
        if (HarnessProcess.Command(context.Settings, executable).Skip(1).Any(a => a.StartsWith('-')))
        {
            throw new ProviderRefusedException("Native qualification does not admit leading harness options in the command setting; the launcher supplies every option.");
        }
    }

    public static bool OnlyProperties(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().All(p => names.Contains(p.Name, StringComparer.Ordinal))
        && value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();

    public static string Probe(LaunchContext context, string executable, params string[] arguments)
    {
        (int code, string output, _) = HarnessProcess.Run([.. HarnessProcess.Command(context.Settings, executable), .. arguments],
            context.Environment, TimeSpan.FromSeconds(30), context.Workspace);
        if (code != 0 || string.IsNullOrWhiteSpace(output) || output.Length > 1024 * 1024)
        {
            // Probe output can contain local configuration and credentials. Never repeat it in a refusal or record.
            throw new ProviderRefusedException($"The {executable} {string.Join(' ', arguments.Where(a => a.StartsWith("--", StringComparison.Ordinal)))} inventory probe failed or returned no bounded evidence.");
        }
        return output.Trim();
    }

    public static string Version(LaunchContext context, string executable, string expected, string version, bool qualifying = false)
    {
        string output = Probe(context, executable, "--version");
        if (!qualifying && output != expected)
        {
            throw new ProviderRefusedException($"The {executable} harness version is not qualified; only {version} is qualified by this launcher.");
        }
        string prefix = executable == "codex" ? "codex-cli " : "";
        string suffix = executable == "claude" ? " (Claude Code)" : "";
        if (!output.StartsWith(prefix, StringComparison.Ordinal) || !output.EndsWith(suffix, StringComparison.Ordinal)
            || output.Length <= prefix.Length + suffix.Length || !HarnessQualificationType.ExactVersion(output[prefix.Length..(output.Length - suffix.Length)]))
        {
            throw new ProviderRefusedException("The harness version probe did not report one exact bounded version; raw output was discarded.");
        }
        return output[prefix.Length..(output.Length - suffix.Length)];
    }

    public static string Binary(LaunchContext context, IReadOnlyList<string> command) => Digest(new
    {
        executable = ContentHash.Sha256File(Executable(command, context.Environment)),
        entries = EntryFiles(context, command).Select(ContentHash.Sha256File).ToArray(),
    });

    // The exact local transport has already been checked. Normalize only its run-specific paths, principal and bindings;
    // retain code identities and environment names, so qualification can apply to a new run without storing any values.
    public static string McpEvidence(LaunchContext context, string method) => Digest(new
    {
        method, server = "vericairn", transport = "stdio", binary = Binary(context, context.McpServer),
        environment = context.McpEnvironment.Keys.Order(StringComparer.Ordinal).ToArray(), authority = "acting-principal", count = 1,
    });

    public static string Platform => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

    public static void Flags(string executable, string help, params string[] required)
    {
        // Match complete option tokens on option lines; a prefix or a mention in prose is not a flag declaration.
        HashSet<string> flags = help.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('-'))
            .SelectMany(l => l.Split([' ', '\t', ',', '='], StringSplitOptions.RemoveEmptyEntries).TakeWhile(t => t.StartsWith('-')))
            .ToHashSet(StringComparer.Ordinal);
        foreach (string flag in required)
        {
            if (!flags.Contains(flag)) throw new ProviderRefusedException($"The {executable} help does not declare required isolation or launch flag {flag}; this version cannot be qualified.");
        }
    }

    public static string Digest<T>(T value) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(value)));

    public static string[] Arguments(LaunchContext context, string executable) => HarnessProcess.Command(context.Settings, executable).Skip(1)
        .Select(a => Path.IsPathFullyQualified(a) ? "local-binding" : a).ToArray();

    public static string Binding(LaunchContext context, IReadOnlyList<string> command) => Digest(new
    {
        command, workspace = context.Workspace, context.Brief, context.Condition, context.McpConfiguration,
        mcp = context.McpServer, mcpFile = ContentHash.Sha256File(context.McpConfiguration),
        environment = context.Environment.OrderBy(p => p.Key, StringComparer.Ordinal),
        mcpEnvironment = context.McpEnvironment.OrderBy(p => p.Key, StringComparer.Ordinal),
        executable = ContentHash.Sha256File(Executable(command, context.Environment)),
        server = ContentHash.Sha256File(Executable(context.McpServer, context.Environment)),
        entries = EntryFiles(context, command).Concat(EntryFiles(context, context.McpServer)).Select(ContentHash.Sha256File).ToArray(),
    });

    private static IEnumerable<string> EntryFiles(LaunchContext context, IReadOnlyList<string> command) => command.Skip(1)
        .Select(a => Path.IsPathFullyQualified(a) ? a : Path.Combine(context.Workspace, a)).Where(File.Exists);

    public static void Recheck(LaunchContext context, IReadOnlyList<string> command, string binding, IReadOnlyList<RunComponent> instructions, bool codex)
    {
        if (Binding(context, command) != binding || Digest(HarnessConfiguration.Inspect(context, codex)) != Digest(instructions))
        {
            throw new ProviderRefusedException("The local launch bindings or instruction content changed during preflight; no run was dispatched.");
        }
    }

    public static string Executable(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment)
    {
        string name = command[0];
        if (Path.IsPathFullyQualified(name) && File.Exists(name)) return name;
        if (Path.GetFileName(name) != name) throw new ProviderRefusedException("An inventory executable must be absolute or resolved through the admitted PATH.");
        string[] extensions = OperatingSystem.IsWindows() ? (environment.GetValueOrDefault("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';') : [];
        foreach (string directory in (environment.GetValueOrDefault("PATH") ?? "").Split(Path.PathSeparator).Where(Path.IsPathFullyQualified))
        {
            foreach (string suffix in extensions.Prepend(""))
            {
                string candidate = Path.Combine(directory, name + suffix);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new ProviderRefusedException("The inventory executable could not be resolved through the admitted PATH.");
    }

    public static List<RunComponent> Components(LaunchContext context, string executable, string id, string version, string configuration,
        IReadOnlyList<string> tools, string variable, string destination)
    {
        IReadOnlyList<string> command = HarnessProcess.Command(context.Settings, executable);
        var components = new List<RunComponent>();
        string harnessHash = ContentHash.Sha256File(Executable(command, context.Environment));
        string[] needs = context.Environment.Keys.Order(StringComparer.Ordinal).ToArray();
        List<string> dependencies = Dependencies(command, id);
        components.Add(new(id, "harness", version, harnessHash, configuration, needs, [destination, "local"], [id + "-configuration", "vericairn", .. dependencies]));
        components.Add(new(id + "-configuration", "configuration", "run-02-v1", configuration, configuration, [variable], ["local"], []));
        foreach (string tool in tools)
        {
            components.Add(new(tool, "tool", version, Digest(new { harnessHash, tool }), configuration, [], ["local"], [id]));
        }
        string mcpHash = ContentHash.Sha256File(Executable(context.McpServer, context.Environment));
        string mcpConfiguration = Digest(new { server = "vericairn", transport = "stdio", environment = context.McpEnvironment.Keys.Order(StringComparer.Ordinal).ToArray(), authority = "run-principal" });
        components.Add(new("vericairn", "mcp", "vericairn.mcp-v1", mcpHash, mcpConfiguration, context.McpEnvironment.Keys.Order(StringComparer.Ordinal).ToArray(), ["estate"], Dependencies(context.McpServer, "vericairn")));
        return components;

        List<string> Dependencies(IReadOnlyList<string> invocation, string owner)
        {
            var ids = new List<string>();
            // Managed entry assemblies and explicitly supplied scripts are part of the executable, not just its host.
            foreach (string argument in EntryFiles(context, invocation))
            {
                string dependency = owner + "-entry-" + ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string hash = ContentHash.Sha256File(argument);
                components.Add(new(dependency, "dependency", version, hash, Digest("entry"), [], ["local"], []));
                ids.Add(dependency);
            }
            return ids;
        }
    }

    public static void McpFile(LaunchContext context)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(context.McpConfiguration));
            JsonElement root = document.RootElement;
            JsonElement servers = root.GetProperty("mcpServers");
            JsonElement server = servers.GetProperty("vericairn");
            JsonElement env = server.GetProperty("env");
            bool approved = root.EnumerateObject().Count() == 1 && servers.EnumerateObject().Count() == 1 && server.EnumerateObject().Count() == 3
                && server.GetProperty("command").GetString() == context.McpServer[0]
                && server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).SequenceEqual(context.McpServer.Skip(1))
                && OnlyProperties(env, context.McpEnvironment.Keys.ToArray())
                && env.EnumerateObject().Count() == context.McpEnvironment.Count
                && env.EnumerateObject().All(p => context.McpEnvironment.TryGetValue(p.Name, out string? value) && value == p.Value.GetString());
            if (!approved) throw new ProviderRefusedException("The explicit MCP configuration differs from the run's approved server table.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderRefusedException("The explicit MCP configuration could not be established.", exception);
        }
    }

    public static void FreshHome(string home)
    {
        if (Exists(home) && (File.GetAttributes(home).HasFlag(FileAttributes.ReparsePoint) || Directory.EnumerateFileSystemEntries(home).Any()))
        {
            throw new ProviderRefusedException("A native harness needs a fresh, empty run configuration directory.");
        }
    }

    public static string Home(LaunchContext context, string variable, string directory, params string[] allowed)
    {
        string expected = Path.Combine(Path.GetDirectoryName(context.McpConfiguration)!, directory);
        if (!context.Environment.TryGetValue(variable, out string? home) || home != expected || !Directory.Exists(home)
            || File.GetAttributes(home).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ProviderRefusedException($"The run-specific {variable} has not been established.");
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(home))
        {
            string name = Path.GetFileName(entry);
            FileAttributes attributes = File.GetAttributes(entry);
            if (!allowed.Contains(name, StringComparer.Ordinal) || attributes.HasFlag(FileAttributes.ReparsePoint)
                || attributes.HasFlag(FileAttributes.Directory) && name != "tmp")
            {
                throw new ProviderRefusedException($"The run-specific {variable} contains unadmitted configuration: {name}.");
            }
            if (name == "tmp")
            {
                // Codex's read-only probes create empty tmp/arg0 directories. No files or aliases are admitted there.
                foreach (string child in Directory.EnumerateFileSystemEntries(entry))
                {
                    if (Path.GetFileName(child) != "arg0" || File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)
                        || !Directory.Exists(child) || Directory.EnumerateFileSystemEntries(child).Any())
                    {
                        throw new ProviderRefusedException("The Codex probe scratch directory contains unadmitted entries.");
                    }
                }
            }
        }
        return home;
    }

    public static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}
