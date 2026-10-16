// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

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
    public void Behavior(string key, string value)
    {
        JsonObject behavior = File.Exists(Report + ".behavior") ? JsonNode.Parse(File.ReadAllText(Report + ".behavior"))!.AsObject() : [];
        behavior[key] = value;
        File.WriteAllText(Report + ".behavior", behavior.ToJsonString());
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);

}
