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
    [InlineData("git commit -m \"Add NOTES.md\"")]
    [InlineData("git commit -m \"Written by the worker.\"")]
    [InlineData("git diff README.md")]
    [InlineData("git tag v1.2")]
    [InlineData("git switch work/x")]
    [InlineData("dotnet build src/A/A.csproj")]
    [InlineData("touch README.md")]
    [InlineData("unclassified README.md")]
    [InlineData("echo x > README.md")]
    [InlineData("cat README.md > elsewhere.md")]
    [InlineData("cat README.md | tee elsewhere.md")]
    [InlineData("find . -delete")]
    public void ChangesInsideTheWorkspaceAreLeftToSettlement(string command) =>
        Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));

    [Theory]
    [InlineData("commit")]
    [InlineData("tag")]
    [InlineData("merge")]
    [InlineData("revert")]
    [InlineData("notes add")]
    [InlineData("stash push")]
    public void GitMessagesAreNotFilesystemOperands(string subcommand)
    {
        foreach (string message in new[] { "-m '../README.md'", "--message '../README.md'", "--message='../a file.md'", "-m 'Document .aws/credentials'", "-m push" })
        {
            string command = $"git {subcommand} {message}";
            Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));
        }
    }

    [Theory]
    [InlineData("show")]
    [InlineData("log")]
    [InlineData("diff")]
    [InlineData("blame")]
    [InlineData("cat-file")]
    [InlineData("ls-tree")]
    [InlineData("ls-files")]
    [InlineData("rev-parse")]
    [InlineData("status")]
    public void GitReadSubcommandsDistinguishObjectNamesFromWorkspaceReads(string subcommand)
    {
        Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command = $"git {subcommand} HEAD:README.md" }), Workspace(), ["path:NOTES.md"]));
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command = $"git {subcommand} ../README.md" }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal("scope-read", signal.Code);
        Assert.Equal("warning", signal.Severity);
    }

    [Theory]
    [InlineData("echo x > '{outside}'", "scope")]
    [InlineData("rm ../other/file", "scope")]
    [InlineData("cp NOTES.md ~/x", "audit-unverified")]
    [InlineData("unclassified $TARGET", "audit-unverified")]
    [InlineData("touch ${TARGET}", "audit-unverified")]
    public void OutsideChangesRequireAnIdentifiableTarget(string template, string code)
    {
        string command = template.Replace("{outside}", Path.Combine(Path.GetDirectoryName(Workspace())!, "out"), StringComparison.Ordinal);
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal(code, signal.Code);
        Assert.Equal(code == "scope" ? "critical" : "warning", signal.Severity);
    }

    [Fact]
    public void RestoreRaisesANetworkWarning()
    {
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command = "dotnet restore" }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal("network", signal.Code);
        Assert.Equal("warning", signal.Severity);
    }

    [Theory]
    [InlineData("git -c user.name=Worker commit --message='../a file.md'")]
    [InlineData("git -C . show HEAD:README.md")]
    [InlineData("git commit -m '../README.md'; git show HEAD:README.md")]
    public void GitArgumentClassificationSurvivesGlobalOptionsAndCompoundCommands(string command) =>
        Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));

    [Theory]
    [InlineData("git commit -m '../README.md'; rm ../outside", "scope")]
    [InlineData("git commit -- -m ../outside", "audit-unverified")]
    [InlineData("git diff --output ../outside README.md", "scope")]
    [InlineData("git diff --output=../outside README.md", "scope")]
    [InlineData("git diff --ext-diff ../outside", "audit-unverified")]
    public void GitArgumentExemptionsDoNotHideOutsideChanges(string command, string code)
    {
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal(code, signal.Code);
        Assert.Equal(code == "scope" ? "critical" : "warning", signal.Severity);
    }

    [Theory]
    [InlineData("git credential fill")]
    [InlineData("git -c credential.helper=manager status")]
    [InlineData("git commit -m \"$GITHUB_TOKEN\"")]
    [InlineData("git show HEAD:.ssh/id_ed25519")]
    public void GitCredentialAccessRemainsCritical(string command) =>
        Assert.Equal("critical", Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]), s => s.Code == "credentials").Severity);

    [Fact]
    public void StructuredAndAbsoluteChangesInsideTheWorkspaceAreLeftToSettlement()
    {
        string file = Path.Combine(Workspace(), "README.md");
        Assert.Empty(CommandAudit.Inspect(Action("Write", new { path = file }), Workspace(), ["path:NOTES.md"]));
        Assert.Empty(CommandAudit.Inspect(Action("unknown-tool", new { path = "README.md" }), Workspace(), ["path:NOTES.md"]));
        Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command = $"touch '{file}'" }), Workspace(), ["path:NOTES.md"]));
        Assert.Empty(CommandAudit.Inspect(Action("apply_patch", new { patch = "*** Begin Patch\n*** Update File: README.md\n*** End Patch" }), Workspace(), ["path:NOTES.md"]));
    }

    [Theory]
    [InlineData("cat README.md")]
    [InlineData("head docs/design.md")]
    [InlineData("rg --files --hidden -g AGENTS.md .")]
    [InlineData("pwd\nrg --files --hidden -g AGENTS.md .")]
    [InlineData("find . -name AGENTS.md")]
    [InlineData("Get-Content README.md")]
    [InlineData("cat README.md > NOTES.md")]
    [InlineData("cat < README.md")]
    [InlineData("cat README.md 2>&1")]
    [InlineData("cat README.md; touch NOTES.md")]
    public void ReadsInsideTheWorkspaceDoNotUseChangePermissions(string command) =>
        Assert.Empty(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));

    [Theory]
    [InlineData("cat ../README.md")]
    [InlineData("rg --files --hidden -g AGENTS.md ..")]
    [InlineData("pwd\nrg --files --hidden -g AGENTS.md ..")]
    [InlineData("bash -lc 'cat ../README.md'")]
    public void ReadsOutsideTheWorkspaceWarnWithoutStopping(string command)
    {
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal("scope-read", signal.Code);
        Assert.Equal("warning", signal.Severity);
        Assert.Contains("read outside its workspace", signal.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cat README.md >> ../NOTES.md", "scope")]
    [InlineData("cat README.md; touch ../NOTES.md", "scope")]
    [InlineData("cat README.md | tee ../elsewhere.md", "scope")]
    [InlineData("find .. -delete", "audit-unverified")]
    [InlineData("unclassified ../README.md", "audit-unverified")]
    [InlineData("rg --pre=helper README.md ../source", "audit-unverified")]
    [InlineData("RG --pre=helper README.md ../source", "audit-unverified")]
    public void OutsideChangesAreDistinguishedFromUnclassifiedInvocations(string command, string code)
    {
        RunSignal signal = Assert.Single(CommandAudit.Inspect(Action("exec_command", new { command }), Workspace(), ["path:NOTES.md"]));
        Assert.Equal(code, signal.Code);
        Assert.Equal(code == "scope" ? "critical" : "warning", signal.Severity);
    }

    [Fact]
    public void TheLiveRunParentSearchIsAReadEvenWithAnAbsolutePath()
    {
        string parent = Path.GetDirectoryName(Workspace())!;
        string command = $"pwd\nrg --files --hidden -g AGENTS.md '{parent}'";
        string line = JsonSerializer.Serialize(new { type = "item.started", item = new { type = "command_execution", id = "live-search", command } });
        HarnessAction action = Assert.Single(new CodexLauncher().ReadActions(line));
        RunSignal signal = Assert.Single(CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]));
        Assert.Equal("scope-read", signal.Code);
        Assert.Equal("warning", signal.Severity);
        Assert.DoesNotContain(parent, signal.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*** Update File: ../README.md")]
    [InlineData("*** Add File: ../NOTES.md")]
    [InlineData("*** Move to: ../elsewhere.md")]
    public void PatchPathsKeepCriticalChangeChecks(string header)
    {
        HarnessAction action = Action("apply_patch", new { patch = "*** Begin Patch\n" + header + "\n*** End Patch" });
        Assert.Equal("critical", Assert.Single(CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]), s => s.Code == "scope").Severity);
    }

    [Theory]
    [InlineData("Read", "../README.md", "scope-read", "warning")]
    [InlineData("Write", "../README.md", "scope", "critical")]
    [InlineData("unknown-tool", "../README.md", "audit-unverified", "warning")]
    [InlineData("Read", ".aws/credentials", "credentials", "critical")]
    public void StructuredAccessKeepsItsClassification(string tool, string path, string code, string severity) =>
        Assert.Equal(severity, Assert.Single(CommandAudit.Inspect(Action(tool, new { path }), Workspace(), ["path:NOTES.md"]), s => s.Code == code).Severity);

    [Fact]
    public void AVisibleLiteralWriteInOpaqueCodeKeepsBothVerdicts()
    {
        HarnessAction action = Action("exec_command", new { command = "python3 -c \"open('../outside.txt', 'w').write('x')\"" });
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]);
        Assert.Equal("warning", Assert.Single(signals, s => s.Code == "audit-unverified").Severity);
        Assert.Equal("critical", Assert.Single(signals, s => s.Code == "scope").Severity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadFromAnExplicitWorkingDirectoryUsesTheWorkspaceBoundary(bool outside)
    {
        string cwd = outside ? Path.GetDirectoryName(Workspace())! : Path.Combine(Workspace(), "docs");
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("exec_command", new { cwd, command = "cat README.md" }), Workspace(), ["path:NOTES.md"]);
        if (outside) Assert.Equal("scope-read", Assert.Single(signals).Code);
        else Assert.Empty(signals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnclassifiedWorkingDirectoryUsesTheWorkspaceBoundary(bool outside)
    {
        string cwd = outside ? Path.GetDirectoryName(Workspace())! : Path.Combine(Workspace(), "docs");
        HarnessAction action = Action("exec_command", new { cwd, command = "unclassified" });
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]);
        if (outside) Assert.Equal("audit-unverified", Assert.Single(signals).Code);
        else Assert.Empty(signals);
    }

    [Theory]
    [InlineData("NOTES.md", "NOTES.md")]
    [InlineData("path:NOTES.md", "NOTES.md")]
    [InlineData("path: docs/meeting notes.md", "docs/meeting notes.md")]
    [InlineData("src/**/*.cs", "src/**/*.cs")]
    [InlineData("NOTES.md at the repository's root", null)]
    [InlineData("path:../NOTES.md", null)]
    [InlineData("path:/NOTES.md", null)]
    [InlineData("path:$TARGET", null)]
    [InlineData("path:", null)]
    public void BriefPatternsSeparateProseFromExplicitPaths(string entry, string? expected) =>
        Assert.Equal(expected, PermittedPaths.Pattern(entry));

    [Theory]
    [InlineData("curl https://example.invalid", "network")]
    [InlineData("git push origin work", "network")]
    [InlineData("echo ready\ncurl https://example.invalid", "network")]
    [InlineData("bash -lc 'wget https://example.invalid'", "network")]
    [InlineData("env -i curl https://example.invalid", "network")]
    [InlineData("sudo -u worker curl https://example.invalid", "network")]
    [InlineData("python3 -c \"import requests; requests.get('https://example.invalid')\"", "network")]
    [InlineData("cat ~/.ssh/id_ed25519", "credentials")]
    [InlineData("security find-generic-password -w", "credentials")]
    [InlineData("printenv", "credentials")]
    [InlineData("printf '%s' \"$AWS_SECRET_ACCESS_KEY\"", "credentials")]
    [InlineData("cat .env", "credentials")]
    [InlineData("cat ../outside.txt", "scope-read")]
    [InlineData("cp src/a.cs ../outside.cs", "scope")]
    [InlineData("echo value > ../outside.txt", "scope")]
    [InlineData("cat src/../../outside.txt", "scope-read")]
    public void RecognizableAttemptsAreFlaggedWithoutRetainingArguments(string command, string code)
    {
        HarnessAction action = Action("Bash", new { command });
        RunSignal signal = Assert.Single(CommandAudit.Inspect(action, Workspace(), ["src/"]), s => s.Code == code);
        Assert.DoesNotContain(command, signal.Summary, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", signal.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(code is "scope-read" or "network" ? "warning" : "critical", signal.Severity);
    }

    [Theory]
    [InlineData("cat src/widget.cs")]
    [InlineData("echo 'curl https://example.invalid'")]
    [InlineData("echo 'this > is text' plain")]
    [InlineData("echo '$AWS_SECRET_ACCESS_KEY'")]
    [InlineData("echo '&&' curl")]
    [InlineData("printf '%s' \"\\$AWS_SECRET_ACCESS_KEY\"")]
    [InlineData("dotnet test --no-restore")]
    [InlineData("cat .vericairn/brief.md")]
    public void HarmlessCommandsAndPermittedPathsDoNotInventAttempts(string command) =>
        Assert.Empty(CommandAudit.Inspect(Action("Bash", new { command }), Workspace(), ["src/"]));

    [Fact]
    public void StructuredToolsAreAuditedAndFingerprintsIgnoreInputPropertyOrder()
    {
        Assert.Empty(CommandAudit.Inspect(Action("Read", new { file_path = "docs/outside.md" }), Workspace(), ["src/"]));
        Assert.Equal("warning", Assert.Single(CommandAudit.Inspect(Action("Read", new { file_path = "../outside.md" }), Workspace(), ["src/"])).Severity);
        Assert.Empty(CommandAudit.Inspect(Action("Edit", new { file_path = "src/a.cs", new_string = "curl https://example.invalid" }), Workspace(), ["src/"]));
        Assert.Empty(CommandAudit.Inspect(Action("Read", new { file_path = ".vericairn/brief.md" }), Workspace(), ["src/"]));
        Assert.Empty(CommandAudit.Inspect(Action("command_execution", new { cwd = Workspace(), command = "cat src/a.cs" }), Workspace(), ["src/"]));
        Assert.Contains(CommandAudit.Inspect(Action("WebSearch", new { query = "widget" }), Workspace(), ["src/"]), s => s.Code == "network");
        Assert.Contains(CommandAudit.Inspect(Action("mcp__browser__navigate", new { url = "https://example.invalid" }), Workspace(), ["src/"]), s => s.Code == "network");
        Assert.Equal(CommandAudit.Fingerprint(Action("Edit", new { file_path = "src/a.cs", old_string = "a" })),
            CommandAudit.Fingerprint(Action("Edit", new { old_string = "a", file_path = "src/a.cs" })));
        Assert.NotEqual(CommandAudit.Fingerprint(Action("Read", new { file_path = "src/a.cs" })), CommandAudit.Fingerprint(Action("Read", new { file_path = "src/b.cs" })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CredentialVerdictsSurviveAnUndecidablePathInEitherOrder(bool invalidFirst)
    {
        string[] paths = ["~/.ssh/id_rsa", "src/invalid\0.cs"];
        if (invalidFirst)
        {
            Array.Reverse(paths);
        }

        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("Read", new { paths }), Workspace(), ["src/"]);
        Assert.Equal("critical", Assert.Single(signals, s => s.Code == "credentials").Severity);
        AssertUndecidableCheckWarning(signals);
    }

    [Theory]
    [InlineData("cwd")]
    [InlineData("workdir")]
    [InlineData("working_directory")]
    public void NetworkVerdictsSurviveAnUndecidableWorkingDirectory(string field)
    {
        var input = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["url"] = "https://example.invalid",
            [field] = "invalid\0directory",
            ["file_path"] = "outside.txt",
        };
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("Read", input), Workspace(), ["src/"]);
        Assert.Equal(["audit-unverified", "network"], signals.Select(s => s.Code));
        Assert.Equal("warning", Assert.Single(signals, s => s.Code == "network").Severity);
        AssertUndecidableCheckWarning(signals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeVerdictsSurviveAnUndecidablePathInEitherOrder(bool invalidFirst)
    {
        string[] paths = ["../outside.txt", "src/invalid\0.cs", "src/allowed.cs"];
        if (invalidFirst)
        {
            Array.Reverse(paths);
        }

        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("Edit", new { paths }), Workspace(), ["src/"]);
        Assert.Equal("critical", Assert.Single(signals, s => s.Code == "scope").Severity);
        AssertUndecidableCheckWarning(signals);
    }

    [Fact]
    public void AnAbsolutePathCanStillBeCheckedWhenTheWorkingDirectoryIsUnknown()
    {
        string file = Path.Combine(Path.GetDirectoryName(Workspace())!, "outside.txt");
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("Edit", new { cwd = "invalid\0directory", file_path = file }), Workspace(), ["src/"]);
        Assert.Equal("critical", Assert.Single(signals, s => s.Code == "scope").Severity);
        AssertUndecidableCheckWarning(signals);
    }

    [Theory]
    [InlineData("printenv", "credentials")]
    [InlineData("curl https://example.invalid", "network")]
    [InlineData("cat ../outside.txt", null)]
    [InlineData("cat outside.txt", null)]
    [InlineData("cat .vericairn/brief.md", null)]
    [InlineData("cat src/a.cs", null)]
    public void AReadClassificationTimeoutPreservesIndependentAndConservativeVerdicts(string command, string? code)
    {
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(Action("Bash", new { command }), Workspace(), ["src/"],
            _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException());
        AssertUndecidableCheckWarning(signals);
        if (code is null)
        {
            Assert.Single(signals);
        }
        else
        {
            Assert.Equal(code is "scope-read" or "network" ? "warning" : "critical", Assert.Single(signals, s => s.Code == code).Severity);
        }
    }

    [Fact]
    public void AFingerprintFailureDoesNotDiscardCompletedVerdicts()
    {
        using JsonDocument input = JsonDocument.Parse("""{"url":"https://example.invalid","url":"https://example.invalid"}""");
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(new HarnessAction("invocation", "Read", input.RootElement), Workspace(), ["src/"]);
        Assert.Equal(["audit-unverified", "network"], signals.Select(s => s.Code));
        Assert.All(signals, s => Assert.Null(s.Fingerprint));
        Assert.Contains("invocation check", signals[0].Summary, StringComparison.Ordinal);
    }

    private static void AssertUndecidableCheckWarning(IReadOnlyList<RunSignal> signals)
    {
        RunSignal warning = Assert.Single(signals, s => s.Code == "audit-unverified");
        Assert.Equal("warning", warning.Severity);
        Assert.Contains("invocation check", warning.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("harness event", warning.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid", warning.Summary, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", warning.Fingerprint, StringComparison.Ordinal);
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
        Assert.Equal("exec_command", started.Tool);
        HarnessAction completed = Assert.Single(launcher.ReadActions("""{"type":"item.completed","item":{"type":"command_execution","id":"c1","command":"cat src/a.cs","aggregated_output":"curl https://example.invalid","exit_code":0}}"""));
        Assert.Equal(CommandAudit.Fingerprint(started), CommandAudit.Fingerprint(completed));
        Assert.Empty(CommandAudit.Inspect(completed, Workspace(), ["src/"]));
        HarnessAction change = Assert.Single(launcher.ReadActions("""{"type":"item.completed","item":{"type":"file_change","id":"f1","changes":[{"path":"../outside.txt","kind":"add"}]}}"""));
        Assert.Equal("apply_patch", change.Tool);
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
