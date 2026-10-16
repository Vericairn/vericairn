// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class NativeRefusalTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void AnAdmittedWrapperCannotChangeItsPositionalMode(string harness)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        RunPreflight initial = fixture.Launcher.Preflight(context);
        List<string> invocation = JsonSerializer.Deserialize<List<string>>(context.Settings["command"])!;
        invocation.Add("different-wrapper-mode");
        LaunchContext different = context with { Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = JsonSerializer.Serialize(invocation) } };
        RunPreflight changed = fixture.Launcher.Preflight(different);
        Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(new("wrapper-policy", initial.Components), changed, different.Environment,
            [harness == "codex" ? "openai-inference" : "anthropic-inference", "estate", "local"], fixture.Launcher.EnvironmentNames));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void RelativeEntryFilesHaveContentIdentitiesToo(string harness)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        string entry = Path.Combine(context.Workspace, "entry.txt");
        File.WriteAllText(entry, "first entry");
        List<string> invocation = JsonSerializer.Deserialize<List<string>>(context.Settings["command"])!;
        invocation.Add("entry.txt");
        context = context with { Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = JsonSerializer.Serialize(invocation) } };
        RunPreflight first = fixture.Launcher.Preflight(context);
        File.WriteAllText(entry, "second entry");
        RunPreflight second = fixture.Launcher.Preflight(context);
        Assert.NotEqual(first.Components.Where(c => c.Kind == "dependency").Select(c => c.Hash), second.Components.Where(c => c.Kind == "dependency").Select(c => c.Hash));
    }

    [Fact]
    public void CodexRefusesSyntheticSystemConfigurationWithoutRunningIt()
    {
        using var fixture = new NativeProbe("codex");
        string path = Path.Combine(fixture.Root, "config.toml");
        CodexInventory.RefuseManagedPaths([path]);
        File.WriteAllText(path, "unadmitted");
        Assert.Contains("config.toml", Assert.Throws<ProviderRefusedException>(() => CodexInventory.RefuseManagedPaths([path])).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Fact]
    public void GenericNeverInheritsANativeVersionQualification()
    {
        using var fixture = new NativeProbe("codex");
        IAgentLauncher generic = new GenericLauncher();
        Assert.False(generic.ExecutionCapabilities.ClosedEnvironment);
        Assert.Contains("inventory", Assert.Throws<ProviderRefusedException>(() => generic.Preflight(fixture.Context)).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("codex", "-c")]
    [InlineData("codex", "--profile=ambient")]
    [InlineData("claude", "--plugin-dir")]
    [InlineData("claude", "--settings=ambient.json")]
    [InlineData("claude", "--agents")]
    public void LeadingHarnessOptionsCannotAddLoadSourcesOutsideTheQualifiedCommand(string harness, string option)
    {
        using var fixture = new NativeProbe(harness);
        var settings = (IDictionary<string, string>)fixture.Context.Settings;
        List<string> command = JsonSerializer.Deserialize<List<string>>(settings["command"])!;
        command.Add(option);
        settings["command"] = JsonSerializer.Serialize(command);
        LaunchContext context = fixture.Prepare();
        Assert.Contains("leading", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("{\"claudeAiOauth\":{\"subscriptionType\":\"team\"}}")]
    [InlineData("{\"claudeAiOauth\":{\"subscriptionType\":\"enterprise\"}}")]
    [InlineData("{\"claudeAiOauth\":{}}")]
    [InlineData("{}")]
    [InlineData("malformed")]
    [InlineData("")]
    public void ClaudeRechecksTheProvisionedUnmanagedLoginWithoutRecordingItsContents(string credential)
    {
        using var fixture = new NativeProbe("claude");
        LaunchContext context = fixture.Prepare();
        string path = Path.Combine(context.Environment["CLAUDE_CONFIG_DIR"], ".credentials.json");
        if (credential.Length == 0) File.Delete(path);
        else File.WriteAllText(path, credential);
        Assert.Contains("login", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("top-level-control")]
    [InlineData("transport-control")]
    [InlineData("duplicate-env")]
    public void CodexRefusesUnaccountedEffectiveMcpControls(string defect)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("mcp", defect);
        Assert.Contains("MCP", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void AFlagMentionInProseDoesNotQualifyTheMissingFlag(string harness)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("proseFlag", harness == "codex" ? "--ignore-rules" : "--strict-mcp-config");
        Assert.Contains("help does not declare", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }
}
