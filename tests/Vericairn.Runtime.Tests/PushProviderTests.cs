// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class PushProviderTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Basis = new('c', 40);
    private static readonly string Moved = new('d', 40);
    private static readonly PushPlan Plan = new("work/change", "main", Head, Basis, Basis, Basis);

    [Theory]
    [InlineData(0, 0, "passed")]
    [InlineData(1, 2, "findings")]
    [InlineData(1, 0, "incomplete")]
    [InlineData(2, 0, "incomplete")]
    public void ScannerUsesRedactionAnExactRangeAndOnlyACountReport(int exit, int count, string verdict)
    {
        var scanner = new GitleaksScanner(info =>
        {
            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.24.2");
            Assert.Contains("--redact=100", info.ArgumentList);
            Assert.Contains("--ignore-gitleaks-allow", info.ArgumentList);
            Assert.Contains("--log-opts=" + Plan.Range + " --full-history --no-renames -m --no-ext-diff --no-textconv --text", info.ArgumentList);
            Assert.Contains("--report-format=template", info.ArgumentList);
            string template = info.ArgumentList.Single(a => a.StartsWith("--report-template=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Equal("{{ len . }}", File.ReadAllText(template));
            string configuration = info.ArgumentList.Single(a => a.StartsWith("--config=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Equal("[extend]\nuseDefault = true\n", File.ReadAllText(configuration));
            string ignored = info.ArgumentList.Single(a => a.StartsWith("--gitleaks-ignore-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Empty(File.ReadAllText(ignored));
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, count.ToString(CultureInfo.InvariantCulture));
            return new(ProcessOutcome.Exited, true, exit, "untrusted scanner output", "untrusted scanner diagnostics");
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal(verdict, scan.Verdict);
        Assert.Equal(Plan.Range, scan.Range);
        Assert.Equal("8.24.2", scan.Version);
        Assert.DoesNotContain("untrusted", JsonSerializer.Serialize(scan), StringComparison.Ordinal);
        Assert.Equal(verdict == "passed", scan.Passed(Plan));
    }

    [Theory]
    [InlineData("NotStarted")]
    [InlineData("TimedOut")]
    [InlineData("OutputLimitExceeded")]
    [InlineData("Canceled")]
    [InlineData("Failed")]
    public void MissingTimedOutAndIncompleteScannersNeverPass(string outcome)
    {
        var scanner = new GitleaksScanner(info => info.FileName == "git" ? Success("2") : info.ArgumentList[0] == "version" ? Success("8.24.2")
            : new(Enum.Parse<ProcessOutcome>(outcome), outcome != "NotStarted", null, string.Empty, string.Empty));
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.False(scan.Passed(Plan));
        Assert.Null(scan.FindingCount);
    }

    [Fact]
    public void AScannerMissingBeforeItsVersionDoesNotRunAScan()
    {
        int calls = 0;
        var scanner = new GitleaksScanner(_ => { calls++; return new(ProcessOutcome.NotStarted, false, null, string.Empty, string.Empty); });
        Assert.Equal("unavailable", scanner.Scan(Path.GetTempPath(), Plan).Verdict);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AnUnreadableRangeIsRefusedEvenWhenTheScannerWouldReportZeroFindings()
    {
        int scans = 0;
        var scanner = new GitleaksScanner(info =>
        {
            if (info.FileName == "git") return new(ProcessOutcome.Exited, true, 128, string.Empty, "unreadable range");
            if (info.ArgumentList[0] == "version") return Success("8.24.2");
            scans++;
            return Success();
        });
        Assert.Equal("incomplete", scanner.Scan(Path.GetTempPath(), Plan).Verdict);
        Assert.Equal(0, scans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PushUsesExactlyTheScannedCommitAndAnExplicitRemoteLease(bool newBranch)
    {
        var scripted = new RecordedGit { NewBranch = newBranch };
        var adapter = new GitPushAdapter(scripted.Run);
        PushPlan plan = adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head);
        Assert.Equal(Basis, plan.Base);
        Assert.Equal(newBranch ? null : Basis, plan.RemoteHead);
        adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, new("scanner", "1.0.0", plan.Range, "passed", 0));
        string[] push = scripted.Calls.Single(c => c.Contains("push"));
        Assert.Contains(Head + ":refs/heads/work/change", push);
        Assert.Contains("--force-with-lease=refs/heads/work/change:" + (newBranch ? string.Empty : Basis), push);
        Assert.Contains("--no-follow-tags", push);
        Assert.Contains("--recurse-submodules=no", push);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("remote")]
    [InlineData("target")]
    [InlineData("scan")]
    public void NothingIsPushedIfHeadRemoteBaseOrScanChanges(string moved)
    {
        var scripted = new RecordedGit();
        var adapter = new GitPushAdapter(scripted.Run);
        PushPlan plan = adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head);
        scripted.Moved = moved;
        var scan = new SecretScan("scanner", "1.0.0", moved == "scan" ? Basis + ".." + Moved : plan.Range, "passed", 0);
        Assert.Throws<ProviderRefusedException>(() => adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, scan));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("push"));
    }

    [Fact]
    public void NonFastForwardUpdatesAreRefusedBeforeScanning()
    {
        var scripted = new RecordedGit { Divergent = true };
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head));
    }

    [Fact]
    public void AShallowCloneCannotClaimThatEveryCommitWasScanned()
    {
        var scripted = new RecordedGit { Shallow = true };
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("ls-remote") || c.Contains("push"));
    }

    private static ProcessResult Success(string output = "") => new(ProcessOutcome.Exited, true, 0, output, string.Empty);

    private sealed class RecordedGit
    {
        internal List<string[]> Calls { get; } = [];
        internal bool NewBranch { get; set; }
        internal bool Divergent { get; set; }
        internal bool Shallow { get; set; }
        internal string? Moved { get; set; }
        internal ProcessResult Run(ProcessStartInfo info)
        {
            Assert.Equal("git", info.FileName);
            string[] args = info.ArgumentList.ToArray();
            Calls.Add(args);
            if (args.Contains("check-ref-format")) return Success();
            if (args.Contains("--is-shallow-repository")) return Success(Shallow ? "true" : "false");
            if (args.Contains("rev-parse")) return Success(Moved == "head" ? PushProviderTests.Moved : Head);
            if (args.Contains("ls-remote"))
            {
                bool target = args[^1] == "refs/heads/main";
                if (!target && NewBranch) return Success();
                return Success((Moved == (target ? "target" : "remote") ? PushProviderTests.Moved : Basis) + "\t" + args[^1] + "\n");
            }
            if (args.Contains("merge-base")) return Divergent ? new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty) : Success(Basis);
            if (args.Contains("push")) return Success();
            throw new InvalidOperationException("Unscripted git invocation.");
        }
    }
}
