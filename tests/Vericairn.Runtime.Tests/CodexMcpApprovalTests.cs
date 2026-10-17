// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CodexMcpApprovalTests
{
    [Fact]
    public void OnlyTheEstateServerIsPreapprovedInTheRunHomeAndLaunchOverride()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        string configuration = File.ReadAllText(Path.Combine(context.Environment["CODEX_HOME"], "config.toml"));
        IReadOnlyList<string> command = fixture.Launcher.Command(context);
        string server = Assert.Single(configuration.Split('\n'), line => line.Contains("default_tools_approval_mode", StringComparison.Ordinal));
        Assert.StartsWith("mcp_servers={vericairn={", server, StringComparison.Ordinal);
        Assert.Contains("default_tools_approval_mode=\"approve\"", server, StringComparison.Ordinal);
        Assert.Equal(server, Assert.Single(command, argument => argument.Contains("default_tools_approval_mode", StringComparison.Ordinal)));
        Assert.DoesNotContain("approval_policy", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain(command, argument => argument.Contains("approval_policy", StringComparison.Ordinal));
        Assert.DoesNotContain("enabled_tools", configuration, StringComparison.Ordinal); // No tool discovery occurs in the probe.
        Assert.True(fixture.Launcher.Preflight(context).Complete);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheProbeAcceptsTheExactApprovalWhenReportedOrOmittedByTheBaseline(bool reported)
    {
        using var fixture = new NativeProbe("codex");
        if (reported) fixture.Behavior("mcpApproval", "approve");
        Assert.True(fixture.Launcher.Preflight(fixture.Prepare()).Complete);
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("prompt")]
    [InlineData("writes")]
    [InlineData("APPROVE")]
    [InlineData("")]
    public void TheProbeRefusesAnyOtherReportedApproval(string mode)
    {
        using var fixture = new NativeProbe("codex");
        fixture.Behavior("mcpApproval", mode);
        Assert.Contains("MCP", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(fixture.Prepare())).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-approved-server")]
    [InlineData("extra-approved-server")]
    [InlineData("null-approval")]
    [InlineData("duplicate-approval")]
    [InlineData("per-tool-approval")]
    [InlineData("approval-policy")]
    [InlineData("approval-reviewer")]
    public void TheProbeRefusesApprovalOnOtherServersAndAdditionalControls(string defect)
    {
        using var fixture = new NativeProbe("codex");
        fixture.Behavior("mcpApproval", "approve");
        fixture.Behavior("mcp", defect);
        Assert.Contains("MCP", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(fixture.Prepare())).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approval_policy=\"on-request\"\n")]
    [InlineData("approvals_reviewer=\"auto_review\"\n")]
    [InlineData("mcp_servers.vericairn.tools.work_attempt.approval_mode=\"approve\"\n")]
    [InlineData("mcp_servers.other.default_tools_approval_mode=\"approve\"\n")]
    public void ApprovalChangesHiddenByTheListReportStillRefuseThroughTheExactHomeCheck(string added)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        string path = Path.Combine(context.Environment["CODEX_HOME"], "config.toml");
        File.WriteAllText(path, added + File.ReadAllText(path));
        Assert.Contains("isolated configuration", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report + ".probed"));
    }
}
