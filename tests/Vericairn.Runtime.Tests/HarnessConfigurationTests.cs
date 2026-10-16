// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class HarnessConfigurationTests
{
    [Theory]
    [InlineData("claude", ".claude/settings.json")]
    [InlineData("claude", ".claude/settings.local.json")]
    [InlineData("claude", ".claude/hooks/hooks.json")]
    [InlineData("claude", ".claude/skills/custom/SKILL.md")]
    [InlineData("claude", ".claude/agents/custom.md")]
    [InlineData("claude", ".claude/commands/custom.md")]
    [InlineData("claude", ".claude/plugins/plugin.json")]
    [InlineData("claude", ".mcp.json")]
    [InlineData("claude", "nested/.claude/settings.json")]
    [InlineData("codex", ".codex/config.toml")]
    [InlineData("codex", ".codex/hooks.json")]
    [InlineData("codex", ".agents/skills/custom/SKILL.md")]
    [InlineData("codex", ".agents/plugins/marketplace.json")]
    [InlineData("codex", "nested/.codex/config.toml")]
    public void ProjectConfigurationIsRefusedAndNamedWithoutAProbeOrDispatch(string harness, string relative)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        Write(Path.Combine(context.Workspace, relative), "unadmitted");
        fixture.Behavior("probeFailure", "--version");
        string message = Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message;
        Assert.Contains(relative.Split('/').Last(), message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("claude", ".claude/commands/custom.md")]
    [InlineData("codex", ".codex/config.toml")]
    public void AncestorConfigurationIsAlsoRefused(string harness, string relative)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        Write(Path.Combine(fixture.Root, relative), "unadmitted");
        Assert.Contains("ancestor", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void InstructionFilesAreRecordedAsContentAndNeverAsTools(string harness)
    {
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        Write(Path.Combine(context.Workspace, "AGENTS.md"), "Instruction content.");
        Write(Path.Combine(context.Workspace, "nested", "CLAUDE.md"), "More content.");
        Write(Path.Combine(context.Workspace, harness == "claude" ? ".claude" : ".codex", "CLAUDE.md"), "Only an instruction file.");
        RunPreflight preflight = fixture.Launcher.Preflight(context);
        RunComponent[] instructions = preflight.Components.Where(c => c.Id.StartsWith("instruction-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, instructions.Length);
        Assert.All(instructions, c => { Assert.Equal("configuration", c.Kind); Assert.Equal("content", c.Version); });
        Assert.Contains(instructions, c => c.Id.Contains("AGENTS.md", StringComparison.Ordinal));
        Assert.Contains(instructions, c => c.Id.Contains("CLAUDE.md", StringComparison.Ordinal));
        Write(Path.Combine(context.Workspace, "AGENTS.md"), "Changed content.");
        var second = fixture.Launcher.Preflight(context);
        Assert.NotEqual(preflight.Components.Single(c => c.Id.Contains("AGENTS.md", StringComparison.Ordinal)).Hash,
            second.Components.Single(c => c.Id.Contains("AGENTS.md", StringComparison.Ordinal)).Hash);
    }

    [Theory]
    [InlineData("claude", ".claude")]
    [InlineData("codex", ".codex")]
    public void LinkedConfigurationCannotHideFromInspection(string harness, string name)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating directory links can require a Windows privilege.");
        using var fixture = new NativeProbe(harness);
        LaunchContext context = fixture.Prepare();
        Directory.CreateSymbolicLink(Path.Combine(context.Workspace, name), Path.Combine(fixture.Root, "missing"));
        Assert.Contains(name, Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexRefusesUserSkillsOutsideCodexHome()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        Write(Path.Combine(context.Environment["HOME"], ".agents", "skills", "custom", "SKILL.md"), "not in CODEX_HOME");
        Assert.Contains("user", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexInspectsTheCommonWorktreeConfiguration()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        string clone = Path.Combine(fixture.Root, "clone");
        string metadata = Path.Combine(clone, ".git", "worktrees", "run");
        Write(Path.Combine(metadata, "commondir"), "../..\n");
        Write(Path.Combine(context.Workspace, ".git"), "gitdir: " + metadata + "\n");
        Write(Path.Combine(clone, ".codex", "config.toml"), "unadmitted");
        Assert.Contains("common-worktree", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
