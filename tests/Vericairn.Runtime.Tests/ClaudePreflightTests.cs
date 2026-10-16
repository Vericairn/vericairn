// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class ClaudePreflightTests
{
    [Fact]
    public void ClaudeEstablishesItsExplicitToolsAndNoExternalSettingSourcesWithoutDisablingItsGoal()
    {
        using var fixture = new NativeProbe("claude");
        foreach (string name in new[] { "settings.json", "hooks.json", "plugins.json" }) File.WriteAllText(Path.Combine(fixture.Login, name), "ambient");
        LaunchContext context = fixture.Prepare();
        RunPreflight inventory = fixture.Launcher.Preflight(context);
        Assert.True(fixture.Launcher.ExecutionCapabilities.ClosedEnvironment);
        Assert.True(inventory.Complete);
        Assert.True(inventory.InheritsEnvironment);
        Assert.Equal("2.1.296", inventory.HarnessVersion);
        Assert.Contains(inventory.Components, c => c.Id == "claude-code" && c.Kind == "harness");
        Assert.Contains(inventory.Components, c => c.Id == "claude-code-setting-sources-none" && c.Kind == "configuration");
        Assert.Contains(inventory.Components, c => c.Id == "claude-code-goal-evaluator" && c.Kind == "hook");
        Assert.DoesNotContain(inventory.Components, c => c.Kind is "skill" or "plugin");
        List<string> command = fixture.Launcher.Command(context).ToList();
        Assert.Equal("", command[command.IndexOf("--setting-sources") + 1]);
        Assert.Contains("--strict-mcp-config", command);
        Assert.Equal(context.McpConfiguration, command[command.IndexOf("--mcp-config") + 1]);
        string[] tools = command[command.IndexOf("--tools") + 1].Split(',');
        Assert.Equal(["Bash", "Edit", "EndConversation", "Glob", "Grep", "Read", "TaskOutput", "TaskStop", "Write"], tools.Order(StringComparer.Ordinal));
        Assert.Equal(tools.Order(StringComparer.Ordinal), inventory.Components.Where(c => c.Kind == "tool").Select(c => c.Id).Order(StringComparer.Ordinal));
        using JsonDocument settings = JsonDocument.Parse(command[command.IndexOf("--settings") + 1]);
        Assert.False(settings.RootElement.TryGetProperty("disableAllHooks", out var disabled) && disabled.GetBoolean());
        Assert.Empty(settings.RootElement.GetProperty("hooks").EnumerateObject());
        Assert.Empty(settings.RootElement.GetProperty("enabledPlugins").EnumerateObject());
        Assert.Contains("/goal Is it done?", command);
        Assert.Equal(".credentials.json", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(context.Environment["CLAUDE_CONFIG_DIR"]))));
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("--setting-sources")]
    [InlineData("--settings")]
    [InlineData("--strict-mcp-config")]
    [InlineData("--mcp-config")]
    [InlineData("--tools")]
    [InlineData("--print")]
    [InlineData("--output-format")]
    [InlineData("--verbose")]
    [InlineData("--permission-mode")]
    [InlineData("--model")]
    [InlineData("--effort")]
    [InlineData("--max-budget-usd")]
    public void ClaudeRefusesEveryMissingDocumentedFlag(string flag)
    {
        using var fixture = new NativeProbe("claude");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("missingFlag", flag);
        Assert.Contains(flag, Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("2.1.295 (Claude Code)")]
    [InlineData("2.1.297 (Claude Code)")]
    [InlineData("unknown")]
    public void ClaudeRefusesAnUnqualifiedVersion(string version)
    {
        using var fixture = new NativeProbe("claude");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", version);
        Assert.Contains("version", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeRefusesAnAmbientServerInsertedIntoItsExplicitMcpFile()
    {
        using var fixture = new NativeProbe("claude");
        LaunchContext context = fixture.Prepare();
        var configuration = JsonNode.Parse(File.ReadAllText(context.McpConfiguration))!;
        configuration["mcpServers"]!["ambient"] = new JsonObject { ["command"] = "unapproved" };
        File.WriteAllText(context.McpConfiguration, configuration.ToJsonString());
        Assert.Contains("MCP", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("hooks.json")]
    [InlineData("skills/extra/SKILL.md")]
    [InlineData("plugins/installed.json")]
    [InlineData("agents/agent.md")]
    [InlineData("commands/custom.md")]
    public void ClaudeRefusesUnadmittedRunHomeConfiguration(string relative)
    {
        using var fixture = new NativeProbe("claude");
        LaunchContext context = fixture.Prepare();
        string path = Path.Combine(context.Environment["CLAUDE_CONFIG_DIR"], relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "unadmitted");
        Assert.Contains("CLAUDE_CONFIG_DIR", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }
}
