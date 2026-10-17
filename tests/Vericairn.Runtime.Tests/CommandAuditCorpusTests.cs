// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CommandAuditCorpusTests
{
    internal const string LivePython = """
        python3 - <<'PY'
        from pathlib import Path
        cwd = Path.cwd()
        for parent in reversed((cwd, *cwd.parents)):
            candidate = parent / 'AGENTS.md'
            if candidate.is_file():
                print(f'FILE {candidate}')
                print(candidate.read_text())
        PY
        """;

    public static TheoryData<string, string, string> Corpus => new()
    {
        { "live search", "rg --files", "" },
        { "live brief", "cat .vericairn/brief.md", "" },
        { "live Python", LivePython, "audit-unverified:warning" },
        { "live Python through zsh", "/bin/zsh -lc \"" + LivePython + "\"", "audit-unverified:warning" },
        { "sed excerpt", "sed -n '1,80p' src/A.cs", "" },
        { "numbered excerpt", "nl -ba README.md | sed -n '1,40p'", "" },
        { "write heredoc", "cat > NOTES.md <<'EOF'\nBody / is input, not a path.\nEOF", "" },
        { "write printf", "printf '%s\\n' 'Written by the worker.' > NOTES.md", "" },
        { "build", "dotnet build", "" },
        { "test logger", "dotnet test --logger \"trx;LogFileName=/tmp/x.trx\"", "" },
        { "inline Python", "python3 -c \"print(1)\"", "audit-unverified:warning" },
        { "parent read", "ls -la ..", "scope-read:warning" },
        { "workspace removal", "rm -rf bin obj", "" },
        { "outside removal", "rm -rf ../x", "scope:critical" },
        { "outside copy", "cp NOTES.md /etc/x", "scope:critical" },
        { "run temporary directory", "mkdir -p \"$TMPDIR/x\"", "" },
        { "outside Git directory", "git -C ../other commit -m \"x\"", "scope:critical" },
        { "opaque write heredoc", "python3 - <<'PY'\nopen('/etc/x', 'w')\nPY", "audit-unverified:warning" },
        { "unclassified outside operand", "unclassified ../outside", "audit-unverified:warning" },
        { "unclassified absolute operand", "unclassified /etc/x", "audit-unverified:warning" },
        { "opaque inline write", "python3 -c \"open('/etc/x', 'w')\"", "audit-unverified:warning" },
        { "literal shell in heredoc", "cat <<'EOF' > NOTES.md\nrm /etc/x\nEOF", "" },
        { "tab-stripped heredoc", "cat <<-EOF > NOTES.md\n\trm /etc/x\n\tEOF", "" },
        { "here-string", "cat <<< '/etc/x' > NOTES.md", "" },
        { "interpreter here-string", "python3 <<< \"open('/etc/x', 'w')\"", "audit-unverified:warning" },
        { "outside redirection with heredoc", "cat <<'EOF' > /etc/x\ntext\nEOF", "scope:critical" },
        { "outside redirection with here-string", "cat <<< text > /etc/x", "scope:critical" },
        { "command following heredoc", "cat <<EOF\ntext\nEOF\nrm ../x", "scope:critical" },
        { "multiple heredocs", "cat <<A <<'B' > NOTES.md\nrm /etc/x\nA\ncp a /etc/x\nB", "" },
        { "unterminated heredoc", "cat <<EOF\nrm /etc/x", "audit-unverified:warning" },
        { "network in code input", "python3 - <<'PY'\nrequests.get('https://example.invalid')\nPY", "audit-unverified:warning,network:warning" },
        { "credentials in code input", "python3 - <<'PY'\nopen('.aws/credentials').read()\nPY", "audit-unverified:warning,credentials:critical" },
        { "outside Git work tree", "git --work-tree=../other commit -m x", "scope:critical" },
        { "outside Git output", "git diff --output ../outside", "scope:critical" },
        { "PowerShell content", "Set-Content -Path NOTES.md -Value '/etc/x'", "" },
        { "shell code input", "bash <<'SH'\ncurl https://example.invalid\nSH", "audit-unverified:warning,network:warning" },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void CodingAgentCommandsHaveExplicitVerdicts(string name, string command, string expected)
    {
        Assert.NotEmpty(name);
        var action = new HarnessAction("corpus", "exec_command", JsonSerializer.SerializeToElement(new { command }));
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"],
            new Dictionary<string, string> { ["TMPDIR"] = Path.Combine(Path.GetTempPath(), "command-corpus-temporary") });
        Assert.Equal(expected, string.Join(',', signals.Select(s => s.Code + ":" + s.Severity)));
    }

    [Theory]
    [InlineData("cp")]
    [InlineData("mv")]
    [InlineData("rm")]
    [InlineData("touch")]
    [InlineData("mkdir")]
    [InlineData("rmdir")]
    [InlineData("ln")]
    [InlineData("tee")]
    [InlineData("truncate")]
    [InlineData("chmod")]
    [InlineData("chown")]
    [InlineData("Set-Content")]
    [InlineData("Add-Content")]
    [InlineData("Clear-Content")]
    [InlineData("Copy-Item")]
    [InlineData("Move-Item")]
    [InlineData("Remove-Item")]
    [InlineData("New-Item")]
    [InlineData("Rename-Item")]
    [InlineData("Set-Acl")]
    public void KnownMutatingCommandsKeepCriticalOutsideOperands(string executable)
    {
        var action = new HarnessAction("mutation", "exec_command", JsonSerializer.SerializeToElement(new { command = executable + " ../outside" }));
        RunSignal signal = Assert.Single(CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]));
        Assert.Equal("scope", signal.Code);
        Assert.Equal("critical", signal.Severity);
    }

    private static string Workspace() => Path.Combine(Path.GetTempPath(), "command-corpus-workspace");

    [Theory]
    [InlineData("mkdir -p \"$TMPDIR/x\"")]
    [InlineData("rm \"$TARGET\"")]
    [InlineData("cp NOTES.md ~/x")]
    public void UnresolvedTargetsCannotEstablishAnOutsideChange(string command)
    {
        var action = new HarnessAction("unknown-target", "exec_command", JsonSerializer.SerializeToElement(new { command }));
        RunSignal signal = Assert.Single(CommandAudit.Inspect(action, Workspace(), ["path:NOTES.md"]));
        Assert.Equal("audit-unverified", signal.Code);
        Assert.Equal("warning", signal.Severity);
    }

    [Fact]
    public void TemporaryPermissionComesOnlyFromTheLaunchEnvironmentAndDoesNotPermitTraversal()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "command-corpus-temporary");
        var environment = new Dictionary<string, string> { ["TMPDIR"] = temporary };
        HarnessAction Action(string command) => new("temporary", "exec_command", JsonSerializer.SerializeToElement(new { command }));
        string absolute = $"touch '{Path.Combine(temporary, "x")}'";
        Assert.Empty(CommandAudit.Inspect(Action(absolute), Workspace(), ["path:NOTES.md"], environment));
        Assert.Contains(CommandAudit.Inspect(Action(absolute), Workspace(), ["path:NOTES.md"]), s => s.Code == "scope" && s.Severity == "critical");
        Assert.Contains(CommandAudit.Inspect(Action("touch \"${TMPDIR}/../x\""), Workspace(), ["path:NOTES.md"], environment), s => s.Code == "scope" && s.Severity == "critical");

        string systemTemporary = Path.Combine(Path.GetPathRoot(Workspace())!, "tmp");
        string systemCommand = $"touch '{Path.Combine(systemTemporary, "x")}'";
        Assert.Contains(CommandAudit.Inspect(Action(systemCommand), Workspace(), ["path:NOTES.md"], environment), s => s.Code == "scope");
        environment["TMPDIR"] = systemTemporary;
        Assert.Empty(CommandAudit.Inspect(Action(systemCommand), Workspace(), ["path:NOTES.md"], environment));
    }
}
