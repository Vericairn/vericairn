// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>
/// The Codex launcher (vericairn.launcher.agent/1): Codex's own /goal with the same condition as Claude Code's, through codex exec with
/// its JSON events, in the workspace-write sandbox of the workspace, with the vericairn MCP server configured on its command line.
/// Codex's /goal needs its goals feature, so before a run is recorded the launcher reads codex features list and refuses, saying why,
/// when the feature is not enabled or cannot be verified. Every flag is one codex --help and codex exec --help list.
/// </summary>
public sealed class CodexLauncher : IAgentLauncher
{
    /// <inheritdoc />
    public RunCapabilities ExecutionCapabilities { get; } = new(RunSelection.Scopes, ["none", "minimal", "low", "medium", "high", "xhigh"], ["openai"], ["openai-inference"], ["worker-user"], ProviderDefault: true, ClosedEnvironment: true);

    /// <inheritdoc />
    public RunPreflight Preflight(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CodexInventory.Establish(context);
    }

    /// <inheritdoc />
    public HarnessExecution? ReadExecution(string line) => HarnessSelection.Read(line, "thread.started");

    /// <inheritdoc />
    public IReadOnlyList<HarnessAction> ReadActions(string line) => HarnessActions.Codex(line);

    /// <inheritdoc />
    public RunWatchSettings WatchDefaults => new(NormalTokensPerMinute: 30000);

    /// <summary>The launcher id recorded as the provider of every run it launches.</summary>
    public const string LauncherId = "codex";

    /// <summary>The feature Codex's /goal needs.</summary>
    public const string GoalsFeature = "goals";

    /// <inheritdoc />
    public string Launcher => LauncherId;

    /// <inheritdoc />
    public string Limitations => "Codex receives native rollout budget tracking at its token threshold below the reservation; tracking is not a hard limit. There is no native currency cap; unreported currency is charged in full.";

    /// <inheritdoc />
    public string? Harness => "Codex";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "codex-cli",
        IAgentLauncher.Contract,
        "implemented",
        ["verify-goals", "launch", "observe-usage"],
        [
            "Runs codex exec --json --cd <workspace> --sandbox workspace-write with a fresh run home and a verified vericairn-only MCP configuration \"/goal <condition>\": Codex's own /goal, with the vericairn MCP server configured for the run.",
            "Needs Codex's goals feature: before a run is recorded it reads codex features list, and refuses, saying why, when goals is not enabled or its state cannot be read.",
            "Signs in with Codex's own login under its home; no API key or other credential passes to it, so a run that relies on one in the environment does not sign in.",
            "Adds up the tokens of each completed turn; it reads no price, so a cap in currency is charged in full.",
            "A run-specific CODEX_HOME imports only the harness login file, never user configuration; an effective MCP-list probe refuses any ambient server or altered transport.",
            "Implemented against codex --help, codex exec --help and a stand-in process that speaks its events; not yet qualified against a live Codex.",
        ],
        [new ProviderSetting("stall-seconds", "Event-stream silence limit in seconds; roster and goal limits override it.", false, "600"), new ProviderSetting("command", "The Codex executable, or a JSON array of it and its leading arguments.", false, "codex")]);

    /// <inheritdoc />
    public IReadOnlyList<string> EnvironmentNames => ["CODEX_HOME"];

    /// <inheritdoc />
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => HarnessProcess.Problem(settings, "codex");

    /// <inheritdoc />
    public LaunchContext Prepare(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context = context with { Workspace = HarnessWorkspace.Canonical(context.Workspace) };
        string home = Path.Combine(Path.GetDirectoryName(context.McpConfiguration)!, "codex");
        HarnessInventory.FreshHome(home);
        Directory.CreateDirectory(home);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(home, "config.toml"), CodexInventory.Configuration(context));
        string? original = context.Environment.GetValueOrDefault("CODEX_HOME");
        if (original is null && (context.Environment.GetValueOrDefault("HOME") ?? context.Environment.GetValueOrDefault("USERPROFILE")) is string user)
        {
            original = Path.Combine(user, ".codex");
        }

        // Import only Codex's explicitly provisioned harness login, never configuration, MCP OAuth stores, plugins or hooks.
        if (original is not null && File.Exists(Path.Combine(original, "auth.json")))
        {
            string login = Path.Combine(home, "auth.json");
            File.Copy(Path.Combine(original, "auth.json"), login, overwrite: false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(login, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        var environment = new Dictionary<string, string>(context.Environment, WorkerEnvironment.Names) { ["CODEX_HOME"] = home };
        LaunchContext prepared = context with { Environment = environment };
        return prepared;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        (string model, string effort) = HarnessSelection.Requested(context);
        var selection = new List<string> { "--model", model };
        if (effort != "provider-default") selection.AddRange(["-c", "model_reasoning_effort=" + Toml(effort)]);
        var limits = new List<string>();
        if (context.Limits?.Tokens is long tokens)
        {
            limits.AddRange(["-c", "features.rollout_budget.enabled=true", "-c", "features.rollout_budget.limit_tokens=" + tokens.ToString(CultureInfo.InvariantCulture)]);
        }

        return [.. HarnessProcess.Command(context.Settings, "codex"), "exec", "--json", "--cd", context.Workspace, "--sandbox", "workspace-write",
            .. Overrides(context), "--ignore-rules",
            .. selection, .. limits, "/goal " + context.Condition];
    }

    // Override one whole projects value: CLI dotted-key parsing does not interpret TOML quotes in a path segment.
    internal static string Projects(LaunchContext context) => "{" + Toml(context.Workspace) + "={trust_level=\"untrusted\"}}";

    internal static IReadOnlyList<string> Overrides(LaunchContext context) => ["-c", "mcp_servers=" + Mcp(context), "-c", "projects=" + Projects(context),
        .. CodexInventory.Settings.SelectMany(s => new[] { "-c", s })];

    internal static void VerifyMcp(LaunchContext context)
    {
        (int code, string output, _) = HarnessProcess.Run([.. HarnessProcess.Command(context.Settings, "codex"),
            .. Overrides(context), "mcp", "list", "--json"], context.Environment, TimeSpan.FromSeconds(30), context.Workspace);
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            if (code != 0 || root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1)
            {
                throw new ProviderRefusedException("Codex's effective MCP configuration is not the run's approved server table; ambient or incompatible managed configuration must be removed before launch.");
            }

            JsonElement server = root[0];
            JsonElement transport = server.GetProperty("transport");
            bool approved = HarnessInventory.OnlyProperties(server, "name", "enabled", "disabled_reason", "auth_status", "transport")
                && HarnessInventory.OnlyProperties(transport, "type", "command", "args", "env", "env_vars", "cwd")
                && (!server.TryGetProperty("disabled_reason", out JsonElement reason) || reason.ValueKind == JsonValueKind.Null)
                && server.GetProperty("name").GetString() == "vericairn" && server.GetProperty("enabled").GetBoolean()
                && transport.GetProperty("type").GetString() == "stdio" && transport.GetProperty("command").GetString() == context.McpServer[0]
                && transport.GetProperty("args").EnumerateArray().Select(a => a.GetString()).SequenceEqual(context.McpServer.Skip(1))
                && transport.GetProperty("env_vars").GetArrayLength() == 0
                && (!transport.TryGetProperty("cwd", out JsonElement cwd) || cwd.ValueKind == JsonValueKind.Null);
            JsonElement env = transport.GetProperty("env");
            approved &= context.McpEnvironment.Count == 0 && env.ValueKind == JsonValueKind.Null
                || env.ValueKind == JsonValueKind.Object && env.EnumerateObject().Count() == context.McpEnvironment.Count
                    && env.EnumerateObject().All(p => context.McpEnvironment.TryGetValue(p.Name, out string? value) && p.Value.GetString() == value);
            if (!approved)
            {
                throw new ProviderRefusedException("Codex's effective MCP transport differs from the run's approved configuration; launch was refused.");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderRefusedException("Codex's effective MCP configuration could not be verified; launch was refused.", exception);
        }
    }

    internal static string Mcp(LaunchContext context) => "{vericairn={command=" + Toml(context.McpServer[0]) + ",args=[" + string.Join(",", context.McpServer.Skip(1).Select(Toml)) + "],env={" + string.Join(",", context.McpEnvironment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => Toml(e.Key) + "=" + Toml(e.Value))) + "}}}";

    /// <summary>Each completed turn reports the tokens it spent, its input and output tokens, added to the run's tally.</summary>
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

        if (type.GetString() == "turn.started")
        {
            return new HarnessSignal(UsageStarted: true);
        }

        if (type.GetString() == "turn.failed")
        {
            return new HarnessSignal(UsageIncomplete: true, UsageCompleted: true);
        }

        if (type.GetString() != "turn.completed")
        {
            return null;
        }

        long? tokens = HarnessUsage.Total(root, "input_tokens", "output_tokens");
        return new HarnessSignal(TokensAdded: tokens, UsageIncomplete: tokens is null, UsageCompleted: true);
    }

    /// <summary>A TOML basic string, which a -c override's value is parsed as.</summary>
    private static string Toml(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            _ = c switch
            {
                '"' => text.Append("\\\""),
                '\\' => text.Append("\\\\"),
                < ' ' or '\u007f' => text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)),
                _ => text.Append(c),
            };
        }

        return text.Append('"').ToString();
    }
}
