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
    /// <inheritdoc />
    public RunCapabilities ExecutionCapabilities { get; } = new(RunSelection.Scopes, ["low", "medium", "high", "max"], ["anthropic"], ["anthropic-inference"], ["worker-user"], ProviderDefault: true, ClosedEnvironment: true);

    /// <inheritdoc />
    public RunPreflight Preflight(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        return ClaudeCodeInventory.Establish(context);
    }

    /// <inheritdoc />
    public RunPreflight Probe(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RefuseManagedSettings();
        return ClaudeCodeInventory.Establish(context, qualifying: true);
    }

    /// <inheritdoc />
    public HarnessExecution? ReadExecution(string line) => HarnessSelection.Read(line, "system", "init");

    /// <inheritdoc />
    public IReadOnlyList<HarnessAction> ReadActions(string line) => HarnessActions.Claude(line);

    /// <inheritdoc />
    public RunWatchSettings WatchDefaults => new(NormalTokensPerMinute: 20000);

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
            "Imports only a provisioned pro or max OAuth login into a fresh run configuration; implicit keychain login, managed accounts and environment credentials are not qualified.",
            "Watches provisional per-message tokens and settles tokens from the stream's complete final result; it reads no price, so a cap in currency is charged in full.",
            "Excludes user, project and local setting sources and repository customizations; preserves and inventories the built-in goal evaluator. Selects nine built-in tools explicitly and refuses detected managed settings.",
            "Shipped baseline: Claude Code 2.1.296. Another exact version needs a current human qualification with matching local probe digests; no version range is inferred. Every required flag is confirmed by help, with strict explicit MCP and two inventory checks. Tested with stand-in executables; no live Claude Code or sign-in qualification.",
        ],
        [new ProviderSetting("stall-seconds", "Event-stream silence limit in seconds; roster and goal limits override it.", false, "600"), new ProviderSetting("command", "The Claude Code executable, or a JSON array with positional entry arguments; leading option flags are not qualified.", false, "claude")]);

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
        HarnessInventory.FreshHome(home);
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
                    if (oauth.TryGetProperty("subscriptionType", out JsonElement subscription) && subscription.GetString() is "team" or "enterprise")
                    {
                        throw new ProviderRefusedException("Claude Code organization logins can load remote managed settings that this launcher cannot verify; use an unmanaged worker login.");
                    }

                    File.WriteAllText(login, JsonSerializer.Serialize(new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["claudeAiOauth"] = oauth }));
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
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
        string directory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode")
            : OperatingSystem.IsMacOS() ? "/Library/Application Support/ClaudeCode" : "/etc/claude-code";
        RefuseManagedPaths([Path.Combine(directory, "managed-settings.json"), Path.Combine(directory, "managed-settings.d"), Path.Combine(directory, "managed-mcp.json")]);
        if (OperatingSystem.IsMacOS())
        {
            RefuseManagedPaths(["/Library/Managed Preferences/com.anthropic.claudecode.plist",
                Path.Combine("/Library/Managed Preferences", Environment.UserName, "com.anthropic.claudecode.plist")]);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? policy = root.OpenSubKey(@"SOFTWARE\Policies\ClaudeCode");
                    if (policy is not null)
                    {
                        throw new ProviderRefusedException("Claude Code managed settings cannot be isolated by this launcher; use an unmanaged worker account.");
                    }
                }
            }
        }
    }

    internal static void RefuseManagedPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                _ = File.GetAttributes(path); // Unlike File.Exists, access denied must not mean absent.
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ProviderRefusedException("Claude Code managed configuration could not be checked; launch was refused.", exception);
            }

            throw new ProviderRefusedException("Claude Code managed configuration cannot be isolated by this launcher; use an unmanaged worker account.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        (string model, string effort) = HarnessSelection.Requested(context);
        var selection = new List<string> { "--model", model };
        if (effort != "provider-default") selection.AddRange(["--effort", effort]);
        var limits = new List<string>();
        if (context.Limits is { Currency: long amount, CurrencyUnit: "USD" })
        {
            limits.AddRange(["--max-budget-usd", (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture)]);
        }

        return [.. HarnessProcess.Command(context.Settings, "claude"), "-p", "/goal " + context.Condition, "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", context.McpConfiguration, "--strict-mcp-config", "--setting-sources", "", "--settings", ClaudeCodeInventory.Settings, "--tools", ClaudeCodeInventory.ToolList, .. selection, .. limits];
    }

    /// <summary>The stream's final result reports the run's usage: its input, cache and output tokens, read as one total.</summary>
    public HarnessSignal? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (type.GetString() == "assistant" && root.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String && message.TryGetProperty("usage", out _))
        {
            long? provisional = HarnessUsage.Message(message);
            return new HarnessSignal(UsageStarted: true, UsageIncomplete: provisional is null, UsageId: id.GetString(), UsageTokens: provisional);
        }

        if (type.GetString() != "result")
        {
            return null;
        }

        long? tokens = HarnessUsage.Total(root, "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens");
        return new HarnessSignal(Tokens: tokens, UsageIncomplete: tokens is null, UsageCompleted: true);
    }
}
