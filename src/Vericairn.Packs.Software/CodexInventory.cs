// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>The deliberately narrow Codex CLI qualification, never a claim about another build or its defaults.</summary>
internal static class CodexInventory
{
    // The shipped baselines, oldest first, each with its own effective feature inventory. A desktop app updates its bundled
    // Codex in place, so the version current at release preparation ships beside the earlier one. Another exact version needs
    // a human qualification and is judged against the newest baseline's inventory.
    private sealed record Baseline(string Version, string[] Disabled, string[] Passive);
    // Include native discovery and model-conditional utilities even when this turn's mode hides their schemas.
    private static readonly string[] Tools = ["exec_command", "write_stdin", "apply_patch", "view_image", "update_plan", "request_user_input",
        "request_user_input_async", "send_message_to_user_async", "clock.curr_time", "tool_search",
        "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource", "create_goal", "get_goal", "update_goal"];
    private static readonly string[] Enabled = ["goals", "shell_tool", "unified_exec", "view_image", "code_mode_host"];
    private static readonly string[] Disabled159 =
    [
        "apps", "plugins", "remote_plugin", "hooks", "multi_agent", "multi_agent_v2", "browser_use", "browser_use_external",
        "browser_use_full_cdp_access", "computer_use", "in_app_browser", "image_generation", "code_mode", "code_mode_only",
        "memories", "workspace_dependencies", "skill_search", "skill_mcp_dependency_install", "tool_suggest", "sleep_tool", "shell_snapshot",
    ];
    // These reported switches do not add tools or discover extensions in this exact version. Other enabled switches refuse.
    private static readonly string[] Passive159 =
    [
        "auth_elicitation", "collaboration_modes", "compaction_image_budget", "content_item_kinds", "daemon_auto_start", "enable_request_compression",
        "fast_mode", "guardian_approval", "guardian_reuse_parent_compaction", "in_app_chat", "in_app_dictation", "in_app_local_automation", "in_app_updates",
        "item_ids", "mentions_v2", "plugin_sharing", "realtime_conversation", "resize_all_images", "sqlite", "steer", "system_proxy_fallback",
        "terminal_resize_reflow", "tool_call_mcp_elicitation", "tool_search_always_defer_mcp_tools", "tui_app_server", "unbounded_connection_retries",
        "unified_exec_tty", "unified_exec_zsh_fork", "worktrees", "write_stdin_approval",
    ];
    // 0.163 enables more by default. Its automatic access-program selection is switched off; API-key catalogs and access
    // programs, desktop-only annotation and voice, interrupt handling and Ultra Fast selection add no tool or extension.
    private static readonly string[] Disabled163 = [.. Disabled159, "cli_daybreak"];
    private static readonly string[] Passive163 = [.. Passive159, "api_key_cyber_access_programs", "api_key_model_discovery", "browser_annotation_api",
        "code_mode_interrupt", "in_app_voice", "instant_interrupt", "ultrafast_mode"];
    private static readonly Baseline[] Baselines =
    [
        new("0.159.0-alpha.12.1", Disabled159, Passive159),
        new("0.163.0-alpha.10.2", Disabled163, Passive163),
        // The npm release a pinned install can use reports the same effective inventory as the desktop app's build.
        new("0.163.0", Disabled163, Passive163),
    ];
    // An older baseline ignores a switch it does not know, so every run carries every baseline's disabled switches.
    private static readonly string[] Disabled = Baselines.SelectMany(b => b.Disabled).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The newest shipped baseline.</summary>
    internal static string Version => Baselines[^1].Version;

    /// <summary>Every shipped baseline, oldest first.</summary>
    internal static IReadOnlyList<string> Versions { get; } = Baselines.Select(b => b.Version).ToArray();

    internal static IEnumerable<string> Settings => Enabled.Select(n => "features." + n + "=true").Concat(Disabled.Select(n => "features." + n + "=false"))
        .Concat(["web_search=\"disabled\"", "skills.bundled.enabled=false"]);

    internal static string Configuration(LaunchContext context) => "mcp_servers=" + CodexLauncher.Mcp(context) + "\nprojects=" + CodexLauncher.Projects(context)
        + "\nsandbox_workspace_write=" + CodexLauncher.Sandbox(context)
        + "\nweb_search=\"disabled\"\nskills.bundled.enabled = false\n[features]\ngoals = true\n" + string.Join('\n', Enabled.Where(n => n != "goals").Select(n => n + " = true").Concat(Disabled.Select(n => n + " = false"))) + "\n";

    internal static RunPreflight Establish(LaunchContext context, bool qualifying = false)
    {
        HarnessInventory.CommandPrefix(context, "codex");
        RefuseManagedConfiguration(context);
        IReadOnlyList<RunComponent> instructions = HarnessConfiguration.Inspect(context, codex: true);
        CheckHome();
        HarnessInventory.McpFile(context);
        IReadOnlyList<string> command = new CodexLauncher().Command(context);
        string binding = HarnessInventory.Binding(context, command);
        string version = HarnessInventory.Version(context, "codex", "codex-cli " + Version, Version, qualifying: true);
        Baseline? shipped = Baselines.FirstOrDefault(b => b.Version == version);
        if (!qualifying && shipped is null)
        {
            throw new ProviderRefusedException($"The codex harness version {version} is not qualified; this launcher ships the baselines {string.Join(" and ", Versions)}.");
        }
        string help = HarnessInventory.Probe(context, "codex", "--help");
        HarnessInventory.Flags("codex", help, "-c", "--config", "--enable", "--disable");
        string execHelp = HarnessInventory.Probe(context, "codex", "exec", "--help");
        HarnessInventory.Flags("codex exec", execHelp, "-c", "--config", "--json", "--cd", "--sandbox", "--model", "--ignore-rules");
        string mcpHelp = HarnessInventory.Probe(context, "codex", "mcp", "list", "--help");
        HarnessInventory.Flags("codex mcp list", mcpHelp, "--json");
        string features = HarnessInventory.Probe(context, "codex", [.. CodexLauncher.Overrides(context), "features", "list"]);
        IReadOnlyDictionary<string, bool> effective = Features(features, version, shipped ?? Baselines[^1], shipped is not null);
        string mcp = CodexLauncher.VerifyMcp(context);
        CheckHome();
        string configuration = HarnessInventory.Digest(new
        {
            qualification = "run-02-codex-v1", version, tools = Tools, features = effective.OrderBy(p => p.Key, StringComparer.Ordinal),
            help = HarnessInventory.Digest(new { help, execHelp, mcpHelp }), home = "run-only-login-and-generated-config", rules = "ignored",
            webSearch = "disabled", projectTrust = "untrusted", bundledSkills = "disabled", estateToolsApproval = CodexLauncher.McpToolsApproval,
            sandbox = "workspace-write", additionalWritableRoot = "workspace/.git",
            arguments = HarnessInventory.Arguments(context, "codex"),
            loginPresent = File.Exists(Path.Combine(context.Environment["CODEX_HOME"], "auth.json")),
        });
        List<RunComponent> components = HarnessInventory.Components(context, "codex", "codex", version, configuration, Tools, "CODEX_HOME", "openai-inference");
        HarnessInventory.Recheck(context, command, binding, instructions, codex: true);
        var probes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = HarnessInventory.Digest("codex-cli " + version),
            ["flags"] = HarnessInventory.Digest(new { help, execHelp, mcpHelp }),
            ["tools"] = HarnessInventory.Digest(new { tools = Tools, features = effective.OrderBy(p => p.Key, StringComparer.Ordinal), report = HarnessInventory.Digest(features), binary = HarnessInventory.Binary(context, HarnessProcess.Command(context.Settings, "codex")) }),
            ["configuration"] = HarnessInventory.Digest(new { configuration, platform = HarnessInventory.Platform }),
            ["login"] = HarnessInventory.Digest(new { boundary = "provisioned-login-only", present = File.Exists(Path.Combine(context.Environment["CODEX_HOME"], "auth.json")) }),
            ["mcp"] = mcp,
        };
        return new([.. components, .. instructions], true, true, version, LocalBindingHash: binding, Probes: new(shipped?.Version ?? Version, probes));

        void CheckHome()
        {
            CheckGitDirectory(context);
            string home = HarnessInventory.Home(context, "CODEX_HOME", "codex", "auth.json", "config.toml", "tmp");
            if (File.ReadAllText(Path.Combine(home, "config.toml")) != Configuration(context))
            {
                throw new ProviderRefusedException("The run-specific CODEX_HOME configuration differs from the launcher's isolated configuration.");
            }
        }
    }

    internal static void CheckGitDirectory(LaunchContext context)
    {
        string git = Path.Combine(context.Workspace, ".git");
        try
        {
            // Qualification probes use an empty workspace. If Git administration exists, it must be owned here:
            // neither a linked-worktree file, a symlink/junction, nor an independent-looking directory with commondir.
            if (!HarnessInventory.Exists(git)) return;
            FileAttributes attributes = File.GetAttributes(git);
            if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)
                || HarnessInventory.Exists(Path.Combine(git, "commondir")))
            {
                throw new ProviderRefusedException("Codex's writable .git root must be the workspace's own repository directory; linked or shared Git administration is refused.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProviderRefusedException("Codex's workspace-owned writable .git root could not be verified.", exception);
        }
    }

    private static void RefuseManagedConfiguration(LaunchContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            if (context.Environment.GetValueOrDefault("ProgramData") is not string shared)
            {
                throw new ProviderRefusedException("Codex's system configuration discovery root could not be established.");
            }
            RefuseManagedPaths([Path.Combine(shared, "Codex"), Path.Combine(shared, "OpenAI", "Codex")]);
        }
        else
        {
            RefuseManagedPaths(["/etc/codex"]);
            if (OperatingSystem.IsMacOS()) RefuseManagedPaths(["/Library/Managed Preferences/com.openai.codex.plist",
                Path.Combine("/Library/Managed Preferences", Environment.UserName, "com.openai.codex.plist")]);
        }
    }

    internal static void RefuseManagedPaths(IEnumerable<string> paths)
    {
        try
        {
            foreach (string path in paths)
            {
                if (HarnessInventory.Exists(path)) throw new ProviderRefusedException("Codex system configuration is not qualified: " + Path.GetFileName(path) + ".");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProviderRefusedException("Codex system configuration could not be inspected; launch was refused.", exception);
        }
    }

    private static Dictionary<string, bool> Features(string output, string version, Baseline baseline, bool shipped)
    {
        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || !bool.TryParse(fields[^1], out bool enabled) || !features.TryAdd(fields[0], enabled))
            {
                throw new ProviderRefusedException("Codex's effective feature inventory is malformed or ambiguous.");
            }
        }
        // A shipped baseline reports exactly its switches. A qualified other version's exact inventory is pinned by its probe digests,
        // so a switch it does not report cannot be on; one it reports must have the qualified value.
        foreach (string feature in Enabled.Concat(baseline.Disabled))
        {
            bool reported = features.TryGetValue(feature, out bool enabled);
            if (!reported && !shipped && !Enabled.Contains(feature, StringComparer.Ordinal)) continue;
            if (!reported || enabled != Enabled.Contains(feature, StringComparer.Ordinal))
            {
                throw new ProviderRefusedException($"Codex's effective {feature} feature is missing or differs from the qualified configuration.");
            }
        }
        string[] unqualified = features.Where(p => p.Value && !Enabled.Contains(p.Key, StringComparer.Ordinal) && !baseline.Passive.Contains(p.Key, StringComparer.Ordinal))
            .Select(p => Printable(p.Key)).Order(StringComparer.Ordinal).ToArray();
        if (unqualified.Length > 0)
        {
            throw new ProviderRefusedException($"Codex {version} enables features this launcher has not qualified: {string.Join(", ", unqualified)}. "
                + $"It was checked against {baseline.Version}'s admitted enabled features: {string.Join(", ", Enabled.Concat(baseline.Passive).Order(StringComparer.Ordinal))}. "
                + $"Run a shipped baseline ({string.Join(" or ", Versions)}), for example a pinned install passed with --setting command=<path>.");
        }
        return features;
    }

    // A feature name comes from the harness's own report; only a plain identifier is repeated in a refusal.
    private static string Printable(string name) => name.Length <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? name : "(an unprintable name)";
}
