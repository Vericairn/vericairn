// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
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
    public ProviderDescriptor Descriptor { get; } = new(
        "claude-code-cli",
        IAgentLauncher.Contract,
        "implemented",
        ["launch", "observe-usage"],
        [
            "Runs claude -p \"/goal <condition>\" --output-format stream-json --verbose --permission-mode auto --mcp-config <the worker's configuration> --strict-mcp-config in the workspace: Claude Code's own /goal, with the vericairn MCP server as its only MCP server.",
            "Signs in with Claude Code's own login under the user's home; no API key or other credential passes to it, so a run that relies on one in the environment does not sign in.",
            "Reads the tokens of the stream's final result; it reads no price, so a cap in currency is charged in full.",
            "Implemented against claude --help and a stand-in process that speaks its stream; not yet qualified against a live Claude Code.",
        ],
        [new ProviderSetting("command", "The Claude Code executable, or a JSON array of it and its leading arguments.", false, "claude")]);

    /// <inheritdoc />
    public IReadOnlyList<string> EnvironmentNames => ["CLAUDE_CONFIG_DIR"];

    /// <inheritdoc />
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => HarnessProcess.Problem(settings, "claude");

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. HarnessProcess.Command(context.Settings, "claude"), "-p", "/goal " + context.Condition, "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", context.McpConfiguration, "--strict-mcp-config"];
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
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.GetString() != "result" || !root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long tokens = 0;
        foreach (string field in new[] { "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens" })
        {
            if (usage.TryGetProperty(field, out JsonElement count) && count.TryGetInt64(out long value))
            {
                tokens += value;
            }
        }

        return new HarnessSignal(Tokens: tokens);
    }
}
