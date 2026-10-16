// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class NativePreflightTests
{
    [Theory]
    [InlineData("tool_search")]
    [InlineData("request_user_input_async")]
    [InlineData("send_message_to_user_async")]
    [InlineData("clock.curr_time")]
    public void CodexInventoriesBuiltInDiscoveryAndModelConditionalUtilities(string tool)
    {
        using var fixture = new NativeProbe("codex");
        RunPreflight inventory = fixture.Launcher.Preflight(fixture.Prepare());
        Assert.Contains(inventory.Components, c => c.Kind == "tool" && c.Id == tool && c.Version == "0.159.0-alpha.12.1");
    }

    [Fact]
    public void CodexSuppressesBundledSkillInstallationInBothConfigurationAndLaunchOverrides()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        Assert.Contains("skills.bundled.enabled=false", fixture.Launcher.Command(context));
        Assert.Contains("skills.bundled.enabled = false", File.ReadAllText(Path.Combine(context.Environment["CODEX_HOME"], "config.toml")), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Launcher.Preflight(context).Components, c => c.Kind == "skill");
    }

    [Fact]
    public void CodexEstablishesItsVersionToolsIsolatedHomeAndEffectiveMcp()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext prepared = fixture.Prepare();
        RunPreflight inventory = fixture.Launcher.Preflight(prepared);
        Assert.True(fixture.Launcher.ExecutionCapabilities.ClosedEnvironment);
        Assert.True(inventory.Complete);
        Assert.True(inventory.InheritsEnvironment);
        Assert.Equal("0.159.0-alpha.12.1", inventory.HarnessVersion);
        Assert.Contains(inventory.Components, c => c.Kind == "harness" && c.Id == "codex");
        Assert.Contains(inventory.Components, c => c.Kind == "tool" && c.Id == "exec_command");
        Assert.Contains(inventory.Components, c => c.Kind == "tool" && c.Id == "create_goal");
        Assert.Contains(inventory.Components, c => c.Kind == "mcp" && c.Id == "vericairn");
        Assert.DoesNotContain(inventory.Components, c => c.Kind is "skill" or "plugin" or "hook");
        string home = prepared.Environment["CODEX_HOME"];
        Assert.NotEqual(fixture.Login, home);
        Assert.Equal("synthetic-login", File.ReadAllText(Path.Combine(home, "auth.json")));
        Assert.False(File.Exists(Path.Combine(home, "ambient.txt")));
        var admitted = RunEnvironment.Admit(new("native-test", inventory.Components), inventory, prepared.Environment,
            ["openai-inference", "estate", "local"], fixture.Launcher.EnvironmentNames);
        string json = admitted.ToJsonString();
        Assert.DoesNotContain(fixture.Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-login", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ambient", json, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("version", "codex-cli 0.159.0-alpha.12.2", "version")]
    [InlineData("version", "codex-cli unknown", "version")]
    [InlineData("missingFlag", "--config", "--config")]
    [InlineData("missingFlag", "--json", "--json")]
    [InlineData("missingFlag", "--sandbox", "--sandbox")]
    [InlineData("missingFlag", "--ignore-rules", "--ignore-rules")]
    [InlineData("goals", "false", "goals")]
    [InlineData("goals", "missing", "goals")]
    [InlineData("goals", "duplicate", "feature")]
    [InlineData("probeFailure", "--help", "probe")]
    [InlineData("probeFailure", "--version", "probe")]
    [InlineData("probeFailure", "features", "probe")]
    public void CodexRefusesMissingQualificationEvidence(string key, string value, string reason)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior(key, value);
        Assert.Contains(reason, Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("ambient")]
    [InlineData("disabled-ambient")]
    [InlineData("command")]
    [InlineData("args")]
    [InlineData("env")]
    [InlineData("env_vars")]
    [InlineData("cwd")]
    [InlineData("malformed")]
    public void CodexRequiresExactlyItsEffectiveMcpTransport(string change)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("mcp", change);
        Assert.Contains("MCP", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("config.toml")]
    [InlineData("hooks.json")]
    [InlineData("skills/extra/SKILL.md")]
    public void CodexRefusesChangesToThePreparedConfiguration(string relative)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        string path = Path.Combine(context.Environment["CODEX_HOME"], relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "unadmitted configuration");
        Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context));
    }
}

/// <summary>Native launcher probes run the test executable, never an installed coding agent.</summary>
internal sealed class NativeProbe : IDisposable
{
    public NativeProbe(string harness)
    {
        Root = Path.Combine(Path.GetTempPath(), "vn-" + Ulid.NewUlid().Value);
        Login = Path.Combine(Root, "login");
        Report = Path.Combine(Root, "dispatch.jsonl");
        Directory.CreateDirectory(Login);
        Directory.CreateDirectory(Path.Combine(Root, "workspace"));
        Directory.CreateDirectory(Path.Combine(Root, "run"));
        Directory.CreateDirectory(Path.Combine(Root, "user"));
        File.WriteAllText(Path.Combine(Login, "auth.json"), "synthetic-login");
        File.WriteAllText(Path.Combine(Login, ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");
        File.WriteAllText(Path.Combine(Login, "ambient.txt"), "never imported");
        Launcher = harness == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
        var environment = WorkerEnvironment.Build(WorkerEnvironment.Current(), Launcher.EnvironmentNames);
        environment["HOME"] = Path.Combine(Root, "user");
        environment["USERPROFILE"] = Path.Combine(Root, "user");
        environment[harness == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"] = Login;
        string mcp = Path.Combine(Root, "run", "mcp.json");
        environment["VERICAIRN_MCP_CONFIG"] = mcp;
        var server = FakeHarness.Command("native-server", Path.Combine(Root, "server.jsonl"));
        File.WriteAllText(mcp, JsonSerializer.Serialize(new { mcpServers = new { vericairn = new { command = server[0], args = server.Skip(1), env = new { } } } }));
        Context = new(Path.Combine(Root, "workspace"), "Is it done?", "brief.md", mcp, server,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("native-" + harness, Report) }, environment,
            Execution: JsonSerializer.SerializeToElement(new { requested = new { model = "model-a", effort = "high" } }));
    }

    public string Root { get; }
    public string Login { get; }
    public string Report { get; }
    public IAgentLauncher Launcher { get; }
    public LaunchContext Context { get; }
    public LaunchContext Prepare() => Launcher.Prepare(Context);
    public void Behavior(string key, string value) => File.WriteAllText(Report + ".behavior", new JsonObject { [key] = value }.ToJsonString());
    public void Dispose() => Directory.Delete(Root, recursive: true);

    [ModuleInitializer]
#pragma warning disable CA2255 // Native protocol emulation belongs only to this test executable, not the linked shared stand-in.
    internal static void Intercept()
#pragma warning restore CA2255
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(arguments, FakeHarness.NativeMarker);
        if (at < 0 || at + 2 >= arguments.Length) return;
        int code;
        try { code = Run(arguments[at + 1], arguments[at + 2], arguments[(at + 3)..]) ?? 70; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine("native stand-in: " + exception.Message);
            code = 70;
        }
        Console.Out.Flush();
        Environment.Exit(code);
    }

    internal static int? Run(string scenario, string report, string[] arguments)
    {
        if (!scenario.StartsWith("native-", StringComparison.Ordinal)) return null;
        JsonObject behavior = File.Exists(report + ".behavior") ? JsonNode.Parse(File.ReadAllText(report + ".behavior"))!.AsObject() : [];
        if (behavior["probeFailure"]?.GetValue<string>() is string failure && arguments.Contains(failure)) return 2;
        bool codex = scenario == "native-codex";
        if (arguments.Contains("--version"))
        {
            Console.WriteLine(behavior["version"]?.GetValue<string>() ?? (codex ? "codex-cli 0.159.0-alpha.12.1" : "2.1.296 (Claude Code)"));
            return 0;
        }
        if (arguments.Contains("--help"))
        {
            string[] flags = codex
                ? ["-c", "--config <key=value>", "--enable <FEATURE>", "--disable <FEATURE>", "--json", "--cd <DIR>", "--sandbox <SANDBOX_MODE>", "--model <MODEL>", "--ignore-rules"]
                : ["-p", "--print", "--output-format <format>", "--verbose", "--permission-mode <mode>", "--mcp-config <configs...>", "--strict-mcp-config", "--setting-sources <sources>", "--settings <file-or-json>", "--tools <tools...>", "--model <model>", "--effort <level>", "--max-budget-usd <amount>"];
            Console.WriteLine(string.Join('\n', flags.Where(f => !f.StartsWith(behavior["missingFlag"]?.GetValue<string>() ?? behavior["proseFlag"]?.GetValue<string>() ?? behavior["descriptionFlag"]?.GetValue<string>() ?? "never", StringComparison.Ordinal)).Select(f => "  " + f)));
            if (behavior["proseFlag"]?.GetValue<string>() is string prose) Console.WriteLine("This prose mentions " + prose + " but declares no flag.");
            if (behavior["descriptionFlag"]?.GetValue<string>() is string description) Console.WriteLine("  --unrelated  Documentation mentioning " + description + " without declaring it.");
            if (behavior["helpSuffix"]?.GetValue<string>() is string suffix) Console.WriteLine(suffix);
            return 0;
        }
        if (arguments.Contains("features") && arguments.Contains("list"))
        {
            string? goals = behavior["goals"]?.GetValue<string>();
            if (goals != "missing") Console.WriteLine("goals stable " + (goals == "false" ? "false" : "true"));
            if (goals == "duplicate") Console.WriteLine("goals stable true");
            Console.WriteLine("shell_tool stable true\nunified_exec stable true\nview_image stable true");
            foreach (string name in DisabledCodexFeatures) Console.WriteLine(name + " stable false");
            return 0;
        }
        if (arguments.Contains("mcp") && arguments.Contains("list"))
        {
            string? defect = behavior["mcp"]?.GetValue<string>();
            if (defect == "malformed") { Console.WriteLine("not JSON"); return 0; }
            var config = JsonNode.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("VERICAIRN_MCP_CONFIG")!))!;
            JsonNode server = config["mcpServers"]!["vericairn"]!;
            var transport = new JsonObject { ["type"] = "stdio", ["command"] = server["command"]!.DeepClone(), ["args"] = server["args"]!.DeepClone(),
                ["env"] = server["env"]!.DeepClone(), ["env_vars"] = new JsonArray(), ["cwd"] = null };
            if (defect is "command" or "cwd") transport[defect] = "unexpected";
            if (defect is "args" or "env_vars") transport[defect] = new JsonArray("unexpected");
            if (defect == "env") transport["env"] = new JsonObject { ["EXTRA"] = "unexpected" };
            var list = new JsonArray(new JsonObject { ["name"] = "vericairn", ["enabled"] = true, ["transport"] = transport });
            if (defect == "top-level-control") list[0]!["enabled_tools"] = new JsonArray("unapproved");
            if (defect == "transport-control") transport["unapproved"] = true;
            if (defect == "duplicate-env")
            {
                Console.WriteLine(list.ToJsonString().Replace("\"env\":{}", "\"env\":{},\"env\":{}", StringComparison.Ordinal));
                return 0;
            }
            if (defect == "duplicate-variable")
            {
                Console.WriteLine(list.ToJsonString().Replace("\"SECOND\":\"two\"", "\"FIRST\":\"one\"", StringComparison.Ordinal));
                return 0;
            }
            if (defect is "ambient" or "disabled-ambient") list.Add(new JsonObject { ["name"] = "ambient", ["enabled"] = defect == "ambient" });
            Console.WriteLine(list.ToJsonString());
            return 0;
        }
        File.WriteAllText(report, JsonSerializer.Serialize(arguments));
        if (behavior["actions"]?.GetValue<string>() is string actions)
        {
            string server = actions == "ambient" ? "ambient" : "vericairn";
            string tool = "mcp__" + server + "__work_brief";
            Console.WriteLine(JsonSerializer.Serialize(new { type = codex ? "thread.started" : "system", subtype = "init", tools = new[] { codex ? "exec_command" : "Read", tool } }));
            if (codex)
            {
                Console.WriteLine("""{"type":"item.started","item":{"type":"command_execution","id":"command","command":"echo ready"}}""");
                Console.WriteLine(JsonSerializer.Serialize(new { type = "item.started", item = new { type = "mcp_tool_call", id = "mcp", server, tool = "work_brief", arguments = new { } } }));
            }
            else
            {
                Console.WriteLine("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"read","name":"Read","input":{"file_path":".vericairn/brief.md"}}]}}""");
                Console.WriteLine(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "tool_use", id = "mcp", name = tool, input = new { } } } } }));
            }
        }
        Console.WriteLine("{\"type\":\"result\",\"usage\":{\"input_tokens\":0,\"output_tokens\":0,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}");
        return 0;
    }

    private static readonly string[] DisabledCodexFeatures =
    [
        "apps", "plugins", "remote_plugin", "hooks", "multi_agent", "multi_agent_v2", "browser_use", "browser_use_external",
        "browser_use_full_cdp_access", "computer_use", "in_app_browser", "image_generation", "code_mode", "code_mode_host", "code_mode_only",
        "memories", "workspace_dependencies", "skill_search", "skill_mcp_dependency_install", "tool_suggest", "sleep_tool", "shell_snapshot",
    ];
}
