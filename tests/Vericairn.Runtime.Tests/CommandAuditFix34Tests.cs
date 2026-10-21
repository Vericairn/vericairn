// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The 0.8.0 review found command forms that FIX-24 left without any signal: a path word with a partial quoted prefix, an
/// unexpanded variable or an unrecognised shell in command position, and a recursive copy of the home's estates directory,
/// which only warned.
/// </summary>
public class CommandAuditFix34Tests
{
    [Theory]
    [InlineData("touch ''../outside")]
    [InlineData("touch \"\"../outside")]
    [InlineData("touch ''\"../outside\"")]
    [InlineData("touch ..''/outside")]
    [InlineData("touch '.'./outside")]
    [InlineData("touch \\../outside")]
    [InlineData("echo x > ''../outside")]
    [InlineData("cp source ''\"$HOME\"/copy")]
    public void PartlyQuotedPathsResolveAsTheShellWouldBeforeTheWorkspaceTest(string command) =>
        Critical(Inspect(command, EnvironmentForAudit()), "scope");

    [Theory]
    [InlineData("cat ''\"$HOME\"/notes")]
    [InlineData("cat ''../notes")]
    [InlineData("cat \"\"\"$HOME/notes\"")]
    public void PartlyQuotedReadsOutsideTheWorkspaceWarn(string command) =>
        Assert.Contains(Inspect(command, EnvironmentForAudit()), s => s.Code == "scope-read" && s.Severity == "warning");

    [Theory]
    [InlineData("cp -r ''\"$VERICAIRN_HOME\" copy")]
    [InlineData("tar -cf copy.tar ''\"$HOME\"")]
    [InlineData("zip -r copy.zip \"\"$VERICAIRN_HOME")]
    public void PartlyQuotedHomeCopiesAreCritical(string command) => Critical(Inspect(command, EnvironmentForAudit()), "credentials");

    [Theory]
    [InlineData("touch ''inside")]
    [InlineData("touch 'a file' \"another file\" ''")]
    [InlineData("touch in''side/'x y'")]
    [InlineData("cat \"notes\"'.md'")]
    public void PartlyQuotedPathsInsideTheWorkspacePass(string command) => Assert.Empty(Inspect(command, EnvironmentForAudit()));

    [Theory]
    [InlineData("$RUNNER -c 'touch inside'")]
    [InlineData("${RUNNER} build")]
    [InlineData("\"$TOOL\" --version")]
    [InlineData("env $TOOL run")]
    [InlineData("fish -c 'touch inside'")]
    [InlineData("csh -c 'touch inside'")]
    [InlineData("tcsh -c 'touch inside'")]
    [InlineData("nu -c 'touch inside'")]
    [InlineData("xonsh -c 'touch inside'")]
    public void AnUninspectableCommandPositionWarns(string command) =>
        Assert.Contains(Inspect(command, EnvironmentForAudit()), s => s.Code == "audit-unverified" && s.Severity == "warning");

    [Theory]
    [InlineData("dash -c 'touch ../outside'")]
    [InlineData("ksh -c 'touch ../outside'")]
    [InlineData("mksh -c 'touch ../outside'")]
    [InlineData("ash -c 'touch ../outside'")]
    [InlineData("yash -c 'touch ../outside'")]
    [InlineData("/bin/dash -c 'touch ../outside'")]
    [InlineData("$SHELL -c 'touch ../outside'")]
    [InlineData("\"$SHELL\" -c 'touch ../outside'")]
    [InlineData("''sh -c 'touch ../outside'")]
    [InlineData("$RUNNER -c 'touch ../outside'")]
    [InlineData("fish -c 'touch ../outside'")]
    [InlineData("eval 'touch ../outside'")]
    [InlineData("eval touch \"../outside\"")]
    [InlineData("eval \"cd ..; touch x\"")]
    public void InnerCommandsOfOtherShellsAreInspected(string command) => Critical(Inspect(command, EnvironmentForAudit()), "scope");

    [Theory]
    [InlineData("ksh -c 'curl https://example.invalid'")]
    [InlineData("$SHELL -c 'curl https://example.invalid'")]
    [InlineData("''curl https://example.invalid")]
    public void QuotedOrResolvedProgramsKeepTheirNetworkSignal(string command) =>
        Assert.Contains(Inspect(command, EnvironmentForAudit()), s => s.Code == "network");

    public static IEnumerable<object[]> EstateCopyMatrix()
    {
        foreach (string source in new[] { "$VERICAIRN_HOME/estates", "$VERICAIRN_HOME/estates/estate", "$VERICAIRN_HOME/estates/estate/0123456789abcdef", "$VERICAIRN_HOME/estates/estate/local" })
            foreach (string transfer in Transfers) yield return [source, transfer];
    }

    [Theory]
    [MemberData(nameof(EstateCopyMatrix))]
    public void RecursiveCopiesOfTheEstatesDirectoryOrOneEstateAreCritical(string source, string template) =>
        Critical(Inspect(template.Replace("{source}", "\"" + source + "\"", StringComparison.Ordinal), EnvironmentForAudit()), "credentials");

    [Theory]
    [InlineData("ls -R \"$VERICAIRN_HOME/estates\"")]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/local/notes.txt\"")]
    [InlineData("cp \"$VERICAIRN_HOME/estates/estate/local/notes.txt\" copy")]
    [InlineData("cp -r \"$VERICAIRN_HOME-estates\" copy")]
    public void ListingsAndSingleFilesInTheEstatesDirectoryOnlyWarn(string command) =>
        Assert.DoesNotContain(Inspect(command, EnvironmentForAudit()), s => s.Severity == "critical");

    [Theory]
    [InlineData("cp -r source copy")]
    [InlineData("tar -cf copy.tar source")]
    [InlineData("rsync -a source/ copy/")]
    [InlineData("zip -r copy.zip .")]
    [InlineData("ditto source copy")]
    public void RecursiveCopiesInsideAWorkspaceUnderTheEstatesDirectoryStayOrdinary(string command)
    {
        // The worker's default workspace root is the estate's workspaces directory beside its clones' stores.
        Dictionary<string, string> environment = EnvironmentForAudit();
        string workspace = environment["VERICAIRN_HOME"] + "/estates/estate/workspaces/work-w-1";
        IReadOnlyList<RunSignal> signals = CommandAudit.Inspect(new HarnessAction("fix34", "exec_command", JsonSerializer.SerializeToElement(new { command })), workspace, [], environment);
        Assert.DoesNotContain(signals, s => s.Severity == "critical");
        Critical(CommandAudit.Inspect(new HarnessAction("fix34", "exec_command", JsonSerializer.SerializeToElement(new { command = "cp -r ../.. copy" })), workspace, [], environment), "credentials");
    }

    private static readonly string[] Transfers = ["cp -r {source} copy", "cp -a {source} copy", "tar -cf copy.tar {source}", "rsync -a {source}/ copy/",
        "zip -r copy.zip {source}", "ditto {source} copy", "Copy-Item -Recurse -LiteralPath {source} -Destination copy"];

    private static void Critical(IReadOnlyList<RunSignal> signals, string code) => Assert.Equal("critical", Assert.Single(signals, s => s.Code == code).Severity);
    private static string Workspace() => Path.Combine(Path.GetTempPath(), "fix34-audit", "workspace");
    private static Dictionary<string, string> EnvironmentForAudit()
    {
        string home = Path.Combine(Path.GetTempPath(), "fix34-audit", "user space").Replace('\\', '/');
        return new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["HOME"] = home, ["USERPROFILE"] = home, ["VERICAIRN_HOME"] = home + "/state", ["SHELL"] = "/bin/sh",
            ["LOCALAPPDATA"] = home + "/AppData/Local", ["XDG_CONFIG_HOME"] = home + "/config",
        };
    }
    private static IReadOnlyList<RunSignal> Inspect(string command, IReadOnlyDictionary<string, string> environment) =>
        CommandAudit.Inspect(new HarnessAction("fix34", "exec_command", JsonSerializer.SerializeToElement(new { command })), Workspace(), [], environment);
}
