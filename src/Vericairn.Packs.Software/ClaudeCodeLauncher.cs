// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>
/// The Claude Code launcher (vericairn.launcher.agent/1): Claude Code's own /goal, with the work order's exit question, its checks and
/// its constraints as the condition, in print mode with stream-json output, in auto permission mode within the workspace, and with the
/// vericairn MCP server the worker configured as its only MCP server. Every flag is one claude --help lists.
/// </summary>
public sealed class ClaudeCodeLauncher : IAgentLauncher
{
    /// <summary>The launcher id recorded as the provider of every run it launches.</summary>
    public const string LauncherId = "claude-code";

    /// <inheritdoc />
    public string Launcher => LauncherId;

    /// <inheritdoc />
    public string Limitations => "Claude Code receives --max-budget-usd for USD at the currency threshold below its reservation. This is an estimated-spend soft limit that can overshoot; no native total-token cap is available. Other currencies have no native cap.";

    /// <inheritdoc />
    public string? Harness => "Claude Code";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "claude-code-cli",
        IAgentLauncher.Contract,
        "implemented",
        ["launch", "observe-usage"],
        [
            "Runs claude -p \"/goal <condition>\" --output-format stream-json --verbose --permission-mode auto --mcp-config <the worker's configuration> --strict-mcp-config in the workspace: Claude Code's own /goal, with the vericairn MCP server as its only MCP server.",
            "Imports only Claude Code's provisioned login into a fresh run configuration; no API key or other credential passes to it, so a run that relies on one in the environment does not sign in.",
            "Reads the tokens of the stream's final result; it reads no price, so a cap in currency is charged in full.",
            "Excludes user, project and local settings sources, disables hooks and refuses detected managed settings; remote managed policy is not qualified and managed accounts are unsupported.",
            "Implemented against claude --help and a stand-in process that speaks its stream; not yet qualified against a live Claude Code.",
        ],
        [new ProviderSetting("stall-seconds", "Event-stream silence limit in seconds; the goal limit overrides it.", false, "600"), new ProviderSetting("command", "The Claude Code executable, or a JSON array of it and its leading arguments.", false, "claude")]);

    /// <inheritdoc />
    public IReadOnlyList<string> EnvironmentNames => ["CLAUDE_CONFIG_DIR"];

    /// <inheritdoc />
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => HarnessProcess.Problem(settings, "claude");

    /// <inheritdoc />
    public LaunchContext Prepare(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        string home = Path.Combine(Path.GetDirectoryName(context.McpConfiguration)!, "claude");
        Directory.CreateDirectory(home);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string? original = context.Environment.GetValueOrDefault("CLAUDE_CONFIG_DIR");
        if (original is null && (context.Environment.GetValueOrDefault("HOME") ?? context.Environment.GetValueOrDefault("USERPROFILE")) is string user)
        {
            original = Path.Combine(user, ".claude");
        }

        // Only the explicitly provisioned harness login is imported. Settings, plugins and hook files stay behind.
        if (original is not null && File.Exists(Path.Combine(original, ".credentials.json")))
        {
            string login = Path.Combine(home, ".credentials.json");
            try
            {
                using JsonDocument credentials = JsonDocument.Parse(File.ReadAllText(Path.Combine(original, ".credentials.json")));
                if (credentials.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth))
                {
                    File.WriteAllText(login, JsonSerializer.Serialize(new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["claudeAiOauth"] = oauth }));
                }
            }
            catch (JsonException exception)
            {
                throw new ProviderRefusedException("Claude Code's provisioned login could not be read.", exception);
            }
            if (!OperatingSystem.IsWindows() && File.Exists(login))
            {
                File.SetUnixFileMode(login, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        return context with { Environment = new Dictionary<string, string>(context.Environment, WorkerEnvironment.Names) { ["CLAUDE_CONFIG_DIR"] = home } };
    }

    private static void RefuseManagedSettings()
    {
        string directory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode")
            : OperatingSystem.IsMacOS() ? "/Library/Application Support/ClaudeCode" : "/etc/claude-code";
        if (File.Exists(Path.Combine(directory, "managed-settings.json")) || Directory.Exists(Path.Combine(directory, "managed-settings.d"))
            || (OperatingSystem.IsMacOS() && File.Exists("/Library/Managed Preferences/com.anthropic.claudecode.plist")))
        {
            throw new ProviderRefusedException("Claude Code managed settings cannot be isolated by this launcher; use an unmanaged worker account.");
        }

        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\ClaudeCode");
            using RegistryKey? userPolicy = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Policies\ClaudeCode");
            if (policy is not null || userPolicy is not null)
            {
                throw new ProviderRefusedException("Claude Code managed settings cannot be isolated by this launcher; use an unmanaged worker account.");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var limits = new List<string>();
        if (context.Limits is { Currency: long amount, CurrencyUnit: "USD" })
        {
            limits.AddRange(["--max-budget-usd", (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture)]);
        }

        return [.. HarnessProcess.Command(context.Settings, "claude"), "-p", "/goal " + context.Condition, "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", context.McpConfiguration, "--strict-mcp-config", "--setting-sources", "", "--settings", "{\"disableAllHooks\":true,\"enabledPlugins\":{}}", .. limits];
    }

    /// <summary>The stream's final result reports the run's usage: its input, cache and output tokens, read as one total.</summary>
    public HarnessSignal? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.TrimStart().StartsWith('{') || !line.Contains("\"result\"", StringComparison.Ordinal))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "result")
        {
            return null;
        }

        long? tokens = HarnessUsage.Total(root, "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens");
        return new HarnessSignal(Tokens: tokens, UsageIncomplete: tokens is null);
    }
}
