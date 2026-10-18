// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkerScopeTests
{
    [Theory]
    [InlineData("NOTES.md at the repository's root")]
    [InlineData("the widget")]
    [InlineData("path:../NOTES.md")]
    [InlineData("path:")]
    public void AdmissionRefusesBriefsWithoutAnInterpretablePatternBeforeRecordingOrLaunching(string entry)
    {
        using var run = new RunEstate(brief: Brief(entry));
        string report = run.Report("invalid-scope");
        RunResponse refused = run.Start("codex", "codex", report);
        if (refused.Ok) run.Settled(refused.Run);
        Assert.False(refused.Ok);
        Assert.Contains("permittedChanges", refused.Message, StringComparison.Ordinal);
        Assert.Contains("path:NOTES.md", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(report));
    }

    [Fact]
    public void AdmissionAcceptsProseBesideAnExplicitPattern()
    {
        using var run = new RunEstate(brief: Brief("NOTES.md at the repository's root", "path:NOTES.md"));
        RunResponse started = run.Start("codex", "codex", run.Report("mixed-scope"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        Assert.True(PermittedPaths.Allows("NOTES.md", run.Clone, ["NOTES.md at the repository's root", "path:NOTES.md"]));
        Assert.False(PermittedPaths.Allows("NOTES.md at the repository's root", run.Clone, ["NOTES.md at the repository's root", "path:NOTES.md"]));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AParentDirectoryReadSettlesWithoutAStopAndRetainsItsWarningAndAttention()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        RunResponse started = run.Start("codex", "watch-codex-parent-search", run.Report("parent-search"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "scope-read");
        Assert.Equal("warning", signal.Severity);
        Assert.NotNull(signal.Attention);
        Assert.DoesNotContain(settled.Signals!, s => s.Code == "scope");
        Assert.Equal(0, run.Service.Check().Errors);
        run.Reopen();
        Assert.Contains(run.Row(started.Run).Signals!, s => s == signal);
    }

    [Fact]
    public void AWorkspaceChangeIsCheckedAgainstTheBriefOnlyAtSettlement()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        RunResponse started = run.Start("codex", "watch-codex-workspace-change", run.Report("workspace-change"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.DoesNotContain(settled.Signals!, s => s.Code == "scope");
        Assert.Equal(["README.md"], settled.Scope!.Outside);
        RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "scope-settlement");
        Assert.Equal("warning", signal.Severity);
        Assert.NotNull(signal.Attention);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ANetworkAttemptWarnsAndSettlesWithoutAStop()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        RunResponse started = run.Start("codex", "watch-codex-restore", run.Report("network-warning"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "network");
        Assert.Equal("warning", signal.Severity);
        Assert.NotNull(signal.Attention);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheLiveHeredocWarnsWithoutStoppingAndRetainsAttention()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        string report = run.Report("live-heredoc");
        File.WriteAllText(report + ".command", "/bin/zsh -lc \"" + CommandAuditCorpusTests.LivePython + "\"");
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.DoesNotContain(settled.Signals!, s => s.Code == "scope");
        Assert.NotNull(Assert.Single(settled.Signals!, s => s.Code == "audit-unverified").Attention);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheAuditUsesTheTemporaryDirectoryActuallyGivenToTheHarness()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        string temporary = Path.Combine(run.Scratch, "harness-temporary");
        Directory.CreateDirectory(temporary);
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Environment = new Dictionary<string, string>(run.Service.WorkerOptions.Environment!, WorkerEnvironment.Names) { ["TMPDIR"] = temporary },
        };
        string report = run.Report("temporary-write");
        File.WriteAllText(report + ".command", "mkdir -p \"$TMPDIR/x\"");
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.DoesNotContain(settled.Signals!, s => s.Code is "scope" or "audit-unverified");
        using JsonDocument launched = JsonDocument.Parse(File.ReadLines(report).Last());
        Assert.Equal(temporary, launched.RootElement.GetProperty("environment").GetProperty("TMPDIR").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string Brief(params string[] entries)
    {
        JsonObject brief = JsonNode.Parse(RunEstate.Brief)!.AsObject();
        brief["permittedChanges"] = JsonSerializer.SerializeToNode(entries);
        return brief.ToJsonString();
    }

    [Theory]
    [InlineData("cat $VERICAIRN_HOME/keys/*.p8", "credentials")]
    [InlineData("ls $VERICAIRN_HOME/connections", "credentials")]
    [InlineData("gh auth token", "credentials")]
    [InlineData("gh auth status --show-token", "credentials")]
    [InlineData("env nohup gh auth status -at", "credentials")]
    [InlineData("git -C . credential fill", "credentials")]
    [InlineData("nice git -C . credential-osxkeychain get", "credentials")]
    [InlineData("command git credential-manager get", "credentials")]
    [InlineData("env nohup dd of=../outside", "scope")]
    [InlineData("cd .. && nice sed -i 's/a/b/' outside", "scope")]
    [InlineData("python3 -c \"open('../outside', 'w')\"", "scope")]
    [InlineData("cp -r \"$VERICAIRN_HOME\" copy", "credentials")]
    [InlineData("cp -a \"$VERICAIRN_HOME/..\" copy", "credentials")]
    [InlineData("tar -cf copy.tar -C \"$VERICAIRN_HOME\" .", "credentials")]
    [InlineData("rsync -a \"$VERICAIRN_HOME\" copy", "credentials")]
    [InlineData("zip -r copy.zip \"$VERICAIRN_HOME\"", "credentials")]
    [InlineData("ditto \"$VERICAIRN_HOME\" copy", "credentials")]
    [InlineData("env doas -n -u worker git credential-store --file helper.data get", "credentials")]
    [InlineData("git credential-store --file helper.data store", "credentials")]
    [InlineData("git credential-cache --socket helper.socket erase", "credentials")]
    [InlineData("cd .. && (touch outside)", "scope")]
    [InlineData("cd .. && { touch outside; }", "scope")]
    [InlineData("make install PREFIX=..", "scope")]
    [InlineData("make install DESTDIR=..", "scope")]
    [InlineData("defaults write org.example.app key -bool true", "scope")]
    [InlineData("doas -u worker touch ../outside", "scope")]
    public void Fix20CriticalObservationsStopTheWorker(string command, string code)
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        string report = run.Report("fix20-critical");
        File.WriteAllText(report + ".command", command);
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal("critical", Assert.Single(settled.Signals!, s => s.Code == code).Severity);
        Assert.Equal("worker", settled.Stop!.RequestedBy);
        Assert.NotNull(settled.Stop.ContainedAt);
        using JsonDocument launched = JsonDocument.Parse(File.ReadLines(report).Last());
        Assert.Equal(VericairnHome.Path(), launched.RootElement.GetProperty("environment").GetProperty("VERICAIRN_HOME").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("cat NOTES.md > /dev/null 2>/dev/null", null)]
    [InlineData("cat list | xargs rm", "audit-unverified")]
    [InlineData("osascript -e 'do shell script \"touch /etc/x\"'", "audit-unverified")]
    [InlineData("gh auth status", "network")]
    [InlineData("env git -C . credential reject", null)]
    [InlineData("cp -r source copy", null)]
    [InlineData("tar -cf copy.tar source", null)]
    [InlineData("rsync -a source copy", null)]
    [InlineData("zip -r copy.zip source", null)]
    [InlineData("ditto source copy", null)]
    [InlineData("git credential-store --file get", null)]
    [InlineData("ls -R \"$VERICAIRN_HOME\"", "scope-read")]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/local/live.db\"", "scope-read")]
    [InlineData("cd sub && (touch inside)", null)]
    [InlineData("cd sub && { touch inside; }", null)]
    [InlineData("make install PREFIX=inside", null)]
    [InlineData("make install DESTDIR=inside PREFIX=/usr", null)]
    [InlineData("defaults write ./settings key -bool true", null)]
    [InlineData("cd .. && (doas echo x &>/dev/null)", null)]
    [InlineData("cd .. && { doas echo x >/dev/null; }", null)]
    public void Fix20NoncriticalObservationsDoNotInventAScopeStop(string command, string? warning)
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        string report = run.Report("fix20-warning");
        File.WriteAllText(report + ".command", command);
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.DoesNotContain(settled.Signals!, s => s.Code is "scope" or "credentials");
        if (warning is not null) Assert.Equal("warning", Assert.Single(settled.Signals!, s => s.Code == warning).Severity);
        else Assert.DoesNotContain(settled.Signals!, s => s.Code == "audit-unverified");
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("touch ../outside", true)]
    [InlineData("git credential-store --file helper.data get", true)]
    [InlineData("cp -r \"$VERICAIRN_HOME\" copy", true)]
    [InlineData("touch inside", false)]
    [InlineData("echo x > /dev/null", false)]
    public void ArrayCommandEventsUseTheWorkerStopPath(string body, bool stops)
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        string report = run.Report("array-command");
        File.WriteAllText(report + ".command", JsonSerializer.Serialize(new[] { "sh", "-c", body }));
        RunResponse started = run.Start("codex", "watch-codex-command-array-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(stops ? "known-failure" : "observed-success", settled.Status);
        if (stops)
        {
            Assert.Equal("worker", settled.Stop!.RequestedBy);
            Assert.NotNull(settled.Stop.ContainedAt);
            Assert.Contains(settled.Signals!, s => s.Severity == "critical");
        }
        else
        {
            Assert.Null(settled.Stop);
            Assert.DoesNotContain(settled.Signals!, s => s.Severity == "critical");
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
