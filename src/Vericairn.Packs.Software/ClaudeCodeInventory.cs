// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Claude Code's explicit built-in tool set and source isolation for one qualified version.</summary>
internal static class ClaudeCodeInventory
{
    internal const string Version = "2.1.296";
    internal const string ToolList = "Bash,Read,Write,Edit,Glob,Grep,TaskOutput,TaskStop,EndConversation";
    // /goal is a built-in evaluator hook: disableAllHooks would also disable the requested run.
    internal const string Settings = "{\"hooks\":{},\"enabledPlugins\":{}}";

    internal static RunPreflight Establish(LaunchContext context)
    {
        IReadOnlyList<RunComponent> instructions = HarnessConfiguration.Inspect(context, codex: false);
        _ = HarnessInventory.Home(context, "CLAUDE_CONFIG_DIR", "claude", ".credentials.json");
        HarnessInventory.McpFile(context);
        IReadOnlyList<string> command = new ClaudeCodeLauncher().Command(context);
        string binding = HarnessInventory.Binding(context, command);
        string version = HarnessInventory.Version(context, "claude", Version + " (Claude Code)", Version);
        string help = HarnessInventory.Probe(context, "claude", "--help");
        HarnessInventory.Flags("claude", help, "--print", "--output-format", "--verbose", "--permission-mode", "--mcp-config",
            "--strict-mcp-config", "--setting-sources", "--settings", "--tools", "--model", "--effort", "--max-budget-usd");
        _ = HarnessInventory.Home(context, "CLAUDE_CONFIG_DIR", "claude", ".credentials.json");
        string configuration = HarnessInventory.Digest(new
        {
            qualification = "run-02-claude-code-v1", version, tools = ToolList, settings = Settings, settingSources = "none",
            strictMcp = true, home = "run-only-login", goal = "built-in-evaluator", help = HarnessInventory.Digest(help),
            loginPresent = File.Exists(Path.Combine(context.Environment["CLAUDE_CONFIG_DIR"], ".credentials.json")),
        });
        List<RunComponent> components = HarnessInventory.Components(context, "claude", "claude-code", version, configuration, ToolList.Split(','), "CLAUDE_CONFIG_DIR", "anthropic-inference");
        components.Add(new("claude-code-setting-sources-none", "configuration", "none", HarnessInventory.Digest("none"), configuration, [], ["local"], ["claude-code-configuration"]));
        components.Add(new("claude-code-goal-evaluator", "hook", version, HarnessInventory.Digest(new { version, command = "goal", settings = Settings }), configuration,
            [], ["anthropic-inference"], ["claude-code"]));
        HarnessInventory.Recheck(context, command, binding, instructions, codex: false);
        return new([.. components, .. instructions], true, true, version, LocalBindingHash: binding);
    }
}
