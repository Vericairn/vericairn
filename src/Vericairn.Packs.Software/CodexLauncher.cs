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
    /// <summary>The launcher id recorded as the provider of every run it launches.</summary>
    public const string LauncherId = "codex";

    /// <summary>The feature Codex's /goal needs.</summary>
    public const string GoalsFeature = "goals";

    /// <inheritdoc />
    public string Launcher => LauncherId;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "codex-cli",
        IAgentLauncher.Contract,
        "implemented",
        ["verify-goals", "launch", "observe-usage"],
        [
            "Runs codex exec --json --cd <workspace> --sandbox workspace-write -c mcp_servers.vericairn.command=... -c mcp_servers.vericairn.args=[...] \"/goal <condition>\": Codex's own /goal, with the vericairn MCP server configured for the run.",
            "Needs Codex's goals feature: before a run is recorded it reads codex features list, and refuses, saying why, when goals is not enabled or its state cannot be read.",
            "Signs in with Codex's own login under its home; no API key or other credential passes to it, so a run that relies on one in the environment does not sign in.",
            "Adds up the tokens of each completed turn; it reads no price, so a cap in currency is charged in full.",
            "The MCP servers of the user's own Codex configuration stay configured beside vericairn.",
            "Implemented against codex --help, codex exec --help and a stand-in process that speaks its events; not yet qualified against a live Codex.",
        ],
        [new ProviderSetting("command", "The Codex executable, or a JSON array of it and its leading arguments.", false, "codex")]);

    /// <inheritdoc />
    public IReadOnlyList<string> EnvironmentNames => ["CODEX_HOME"];

    /// <summary>Verifies, before anything is recorded, that Codex's goals feature is enabled: what codex features list says it is, with the run's own environment.</summary>
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        if (HarnessProcess.Problem(settings, "codex") is string problem)
        {
            return problem;
        }

        int code;
        string output;
        string error;
        try
        {
            (code, output, error) = HarnessProcess.Run([.. HarnessProcess.Command(settings, "codex"), "features", "list"], environment, TimeSpan.FromSeconds(30));
        }
        catch (ProviderRefusedException exception)
        {
            return $"Codex's goals feature could not be verified: {exception.Message}";
        }

        if (code != 0)
        {
            string said = (error.Trim().Length > 0 ? error : output).Trim();
            return $"Codex's goals feature could not be verified: codex features list exited {code.ToString(CultureInfo.InvariantCulture)}{(said.Length == 0 ? "." : ": " + (said.Length > 300 ? said[..300] + "…" : said))}";
        }

        string[]? goals = output.Split('\n').Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(columns => columns.Length >= 2 && columns[0] == GoalsFeature);
        if (goals is null)
        {
            return "Codex lists no goals feature in codex features list, so its /goal cannot be verified; a Codex whose /goal is a feature named goals is needed.";
        }

        return goals[^1] == "true" ? null : $"Codex's goals feature is not enabled here (codex features list shows goals {goals[^1]}); enable it with codex features enable goals, then start the run again.";
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var command = new List<string>(HarnessProcess.Command(context.Settings, "codex")) { "exec", "--json", "--cd", context.Workspace, "--sandbox", "workspace-write" };
        command.Add("-c");
        command.Add("mcp_servers.vericairn.command=" + Toml(context.McpServer[0]));
        command.Add("-c");
        command.Add("mcp_servers.vericairn.args=[" + string.Join(", ", context.McpServer.Skip(1).Select(Toml)) + "]");
        foreach ((string name, string value) in context.McpEnvironment.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            command.Add("-c");
            command.Add($"mcp_servers.vericairn.env.{name}={Toml(value)}");
        }

        command.Add("/goal " + context.Condition);
        return command;
    }

    /// <summary>Each completed turn reports the tokens it spent, its input and output tokens, added to the run's tally.</summary>
    public HarnessSignal? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.TrimStart().StartsWith('{') || !line.Contains("turn.completed", StringComparison.Ordinal))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.GetString() != "turn.completed" || !root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long tokens = 0;
        foreach (string field in new[] { "input_tokens", "output_tokens" })
        {
            if (usage.TryGetProperty(field, out JsonElement count) && count.TryGetInt64(out long value))
            {
                tokens += value;
            }
        }

        return new HarnessSignal(TokensAdded: tokens);
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
