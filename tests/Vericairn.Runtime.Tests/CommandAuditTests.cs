// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CommandAuditTests
{
    [Theory]
    [InlineData("curl https://example.invalid", "network")]
    [InlineData("git push origin work", "network")]
    [InlineData("echo ready\ncurl https://example.invalid", "network")]
    [InlineData("bash -lc 'wget https://example.invalid'", "network")]
    [InlineData("python3 -c \"import requests; requests.get('https://example.invalid')\"", "network")]
    [InlineData("cat ~/.ssh/id_ed25519", "credentials")]
    [InlineData("security find-generic-password -w", "credentials")]
    [InlineData("printenv", "credentials")]
    [InlineData("cat .env", "credentials")]
    [InlineData("cat ../outside.txt", "scope")]
    [InlineData("cp src/a.cs docs/outside.cs", "scope")]
    [InlineData("echo value > outside.txt", "scope")]
    [InlineData("cat src/../../outside.txt", "scope")]
    public void RecognizableAttemptsAreFlaggedWithoutRetainingArguments(string command, string code)
    {
        HarnessAction action = Action("Bash", new { command });
        RunSignal signal = Assert.Single(CommandAudit.Inspect(action, Workspace(), ["src/"]), s => s.Code == code);
        Assert.DoesNotContain(command, signal.Summary, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", signal.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(code == "network" ? "warning" : "critical", signal.Severity);
    }

    [Theory]
    [InlineData("cat src/widget.cs")]
    [InlineData("echo 'curl https://example.invalid'")]
    [InlineData("dotnet test --no-restore")]
    [InlineData("cat .vericairn/brief.md")]
    public void HarmlessCommandsAndPermittedPathsDoNotInventAttempts(string command) =>
        Assert.Empty(CommandAudit.Inspect(Action("Bash", new { command }), Workspace(), ["src/"]));

    [Fact]
    public void StructuredToolsAreAuditedAndFingerprintsIgnoreInputPropertyOrder()
    {
        Assert.Contains(CommandAudit.Inspect(Action("Read", new { file_path = "docs/outside.md" }), Workspace(), ["src/"]), s => s.Code == "scope");
        Assert.Empty(CommandAudit.Inspect(Action("Edit", new { file_path = "src/a.cs", new_string = "curl https://example.invalid" }), Workspace(), ["src/"]));
        Assert.Empty(CommandAudit.Inspect(Action("Read", new { file_path = ".vericairn/brief.md" }), Workspace(), ["src/"]));
        Assert.Empty(CommandAudit.Inspect(Action("command_execution", new { cwd = Workspace(), command = "cat src/a.cs" }), Workspace(), ["src/"]));
        Assert.Contains(CommandAudit.Inspect(Action("WebSearch", new { query = "widget" }), Workspace(), ["src/"]), s => s.Code == "network");
        Assert.Equal(CommandAudit.Fingerprint(Action("Edit", new { file_path = "src/a.cs", old_string = "a" })),
            CommandAudit.Fingerprint(Action("Edit", new { old_string = "a", file_path = "src/a.cs" })));
        Assert.NotEqual(CommandAudit.Fingerprint(Action("Read", new { file_path = "src/a.cs" })), CommandAudit.Fingerprint(Action("Read", new { file_path = "src/b.cs" })));
    }

    [Fact]
    public void ClaudeReadsToolUseInputsButNeverAssistantTextOrToolResults()
    {
        var launcher = new ClaudeCodeLauncher();
        const string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"curl https://example.invalid"},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"src/a.cs"}}]}}""";
        HarnessAction action = Assert.Single(launcher.ReadActions(line));
        Assert.Equal("t1", action.Id);
        Assert.Equal("Read", action.Tool);
        Assert.Equal("src/a.cs", action.Input.GetProperty("file_path").GetString());
        Assert.Empty(launcher.ReadActions("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"curl https://example.invalid"}]}}"""));
    }

    [Fact]
    public void CodexReadsLifecycleInputsFileChangesAndMcpCallsButNotCommandOutput()
    {
        var launcher = new CodexLauncher();
        HarnessAction started = Assert.Single(launcher.ReadActions("""{"type":"item.started","item":{"type":"command_execution","id":"c1","command":"cat src/a.cs"}}"""));
        HarnessAction completed = Assert.Single(launcher.ReadActions("""{"type":"item.completed","item":{"type":"command_execution","id":"c1","command":"cat src/a.cs","aggregated_output":"curl https://example.invalid","exit_code":0}}"""));
        Assert.Equal(CommandAudit.Fingerprint(started), CommandAudit.Fingerprint(completed));
        Assert.Empty(CommandAudit.Inspect(completed, Workspace(), ["src/"]));
        HarnessAction change = Assert.Single(launcher.ReadActions("""{"type":"item.completed","item":{"type":"file_change","id":"f1","changes":[{"path":"outside.txt","kind":"add"}]}}"""));
        Assert.Contains(CommandAudit.Inspect(change, Workspace(), ["src/"]), s => s.Code == "scope");
        HarnessAction mcp = Assert.Single(launcher.ReadActions("""{"type":"item.started","item":{"type":"mcp_tool_call","id":"m1","server":"files","tool":"read","arguments":{"path":".aws/credentials"}}}"""));
        Assert.Contains(CommandAudit.Inspect(mcp, Workspace(), ["src/"]), s => s.Code == "credentials");
        Assert.Empty(launcher.ReadActions("""{"type":"item.completed","item":{"type":"agent_message","text":"I ran curl."}}"""));
    }

    [Fact]
    public void PathChecksNormalizeTraversalRespectGlobBoundariesAndRefuseLinks()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "watch-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "src"));
        Directory.CreateDirectory(Path.Combine(workspace, "private"));
        try
        {
            Assert.True(PermittedPaths.Allows("src/sub/../a.cs", workspace, ["path: src/**/*.cs"]));
            Assert.False(PermittedPaths.Allows("src-other/a.cs", workspace, ["src/"]));
            Assert.False(PermittedPaths.Allows("../a.cs", workspace, ["**"]));
            Assert.False(PermittedPaths.Allows("src/$TARGET", workspace, ["src/"]));
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(workspace, "src", "link"), Path.Combine(workspace, "private"));
                Assert.False(PermittedPaths.Allows("src/link/file.txt", workspace, ["src/"]));
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string Workspace() => Path.Combine(Path.GetTempPath(), "watch-audit-workspace");

    private static HarnessAction Action<T>(string tool, T input) => new("invocation", tool, JsonSerializer.SerializeToElement(input));
}
