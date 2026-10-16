// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunWatchingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CriticalSignalsContainThroughTheExistingStopPathEvenWhenAttentionRecordingFails(bool permanent)
    {
        var clock = new WorkerClock();
        int recordings = 0;
        using var run = new RunEstate(options: new WorkerOptions
        {
            Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(20), Grace = TimeSpan.FromSeconds(1),
            BeforeAttentionRecord = () =>
            {
                if (Interlocked.Increment(ref recordings) <= 2 || permanent)
                {
                    throw new IOException("Synthetic attention-store failure.");
                }
            },
        });
        RunResponse started = run.Start("codex", "watch-codex-critical-hold", run.Report("contain"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
        Assert.Null(run.Row(started.Run).Stop!.ContainedAt);
        if (!permanent)
        {
            WaitUntil(() => run.Row(started.Run).Signals?.All(s => s.Attention is not null) == true);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal("worker", settled.Stop!.RequestedBy);
        Assert.NotNull(settled.Stop.EscalatedAt);
        Assert.NotNull(settled.Stop.ContainedAt);
        Assert.All(settled.Signals!, s => Assert.Equal(permanent, s.Attention is null));
        Assert.Contains("Worker observations of agent runs", run.Service.Progress().Text, StringComparison.Ordinal);
        if (permanent)
        {
            Assert.Empty(run.Service.Records("ask"));
            Assert.Contains("attention recording pending", run.Service.Progress().Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(settled.Signals!.Count, run.Service.Records("ask").Count);
        }

        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AWarningRaisesOneAttentionItemWhileTheRunContinues()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("codex", "watch-codex-warning-hold", run.Report("warning"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => run.Row(started.Run).Signals is { Count: > 0 } signals && signals[0].Attention is not null);
        RunRow live = run.Row(started.Run);
        RunSignal signal = Assert.Single(live.Signals!);
        Assert.Equal("warning", signal.Severity);
        Assert.Null(live.Stop);
        Assert.Equal("dispatched", live.Status);
        Assert.Single(run.Service.Records("ask"));
        Assert.Equal(signal.Attention, run.Service.Progress().Json.GetProperty("runSignals")[0].GetProperty("signals")[0].GetProperty("attention").GetString());
        Assert.True(run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "End the stand-in test.", 1)).Ok);
        Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        Assert.Single(run.Service.Records("ask"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("watch-claude-unverified-path")]
    [InlineData("watch-claude-unverified-workdir")]
    [InlineData("watch-claude-unverified-filesystem")]
    public void InvalidInvocationPathsProduceAnUnverifiedWarningWithoutStopping(string scenario)
    {
        using var run = new RunEstate(brief: RunEstate.Brief.Replace("the widget", "**", StringComparison.Ordinal));
        AssertUnverifiedRunContinues(run, scenario);
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unsupported")]
    [InlineData("timeout")]
    [InlineData("regex-timeout")]
    public void AuditExceptionsProduceAnUnverifiedWarningWithoutStopping(string failure)
    {
        Exception exception = failure switch
        {
            "argument" => new ArgumentException("Synthetic audit failure."),
            "io" => new IOException("Synthetic audit failure."),
            "access" => new UnauthorizedAccessException("Synthetic audit failure."),
            "unsupported" => new NotSupportedException("Synthetic audit failure."),
            "timeout" => new TimeoutException("Synthetic audit failure."),
            _ => new System.Text.RegularExpressions.RegexMatchTimeoutException(),
        };
        using var run = new RunEstate();
        HookedLauncher.Install(run, readActions: _ => throw exception);
        AssertUnverifiedRunContinues(run, "watch-claude-unverified-check");
    }

    private static void AssertUnverifiedRunContinues(RunEstate run, string scenario)
    {
        string report = run.Report("unverified");
        RunResponse started = run.Start("claude-code", scenario, report);
        Assert.True(started.Ok, started.Message);
        try
        {
            WaitUntil(() => run.Row(started.Run).Signals is { Count: > 0 });
            RunRow live = run.Row(started.Run);
            RunSignal signal = Assert.Single(live.Signals!);
            Assert.Equal("audit-unverified", signal.Code);
            Assert.Equal("warning", signal.Severity);
            Assert.Null(live.Stop);
            Assert.Equal("dispatched", live.Status);
            Assert.DoesNotContain("Synthetic audit failure", signal.Summary, StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllText(report + ".finish", "release");
        }

        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.NotNull(Assert.Single(settled.Signals!).Attention);
        Assert.Single(run.Service.Records("ask"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AgentProgressClaimsDoNotResetWorkerObservedSilence()
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(options: new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(20), Grace = TimeSpan.FromSeconds(1) });
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository", "Work."))).Id;
        RunResponse started = run.Start("generic", "silent-ignore", run.Report("claims"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "60" });
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("claims")));
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(run.Service.WorkProgress(new WorkProgressRequest(attempt, run.Estate.BuilderId, "build", "I claim to be active.")).Ok);
        WaitUntil(() => run.Row(started.Run).Liveness?.Attention is not null);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(run.Service.WorkProgress(new WorkProgressRequest(attempt, run.Estate.BuilderId, "build", "Still claiming activity.")).Ok);
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void RepeatedLifecycleEventsCountOncePerInvocationAndProduceLoopSignals()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("codex", "watch-codex-loop", run.Report("loop"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal([5, 10], settled.Signals!.Where(s => s.Code == "loop").Select(s => s.Count));
        Assert.Equal(["warning", "critical"], settled.Signals!.Where(s => s.Code == "loop").Select(s => s.Severity));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ObservedUsageProducesRateSignalsAgainstTheCapWithoutWallClockSleeps()
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(budget: """{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000000}""", options: new WorkerOptions
        {
            Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(20), Grace = TimeSpan.FromSeconds(1),
            AfterEvent = () => clock.Advance(TimeSpan.FromSeconds(60)),
        });
        RunResponse started = run.Start("codex", "watch-codex-burn", run.Report("burn"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(["warning", "critical"], settled.Signals!.Where(s => s.Code == "burn-rate").Select(s => s.Severity));
        Assert.Equal(400000, settled.Usage!.Value.GetProperty("tokens").GetInt64());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ClaudeMessageUsageIsProvisionalAndRepeatedSnapshotsAreNotChargedTwice()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "watch-claude-usage", run.Report("usage"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(125, settled.Usage!.Value.GetProperty("tokens").GetInt64());
        Assert.Empty(settled.Signals!);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheNextRunsIntentPinsItsLaunchersReferenceAndTheGoalsReportedHistory()
    {
        using var run = new RunEstate();
        RunResponse first = run.Start("codex", "codex", run.Report("history-first"), branch: "work/first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal("observed-success", run.Settled(first.Run).Status);
        RunResponse next = run.Start("codex", "codex", run.Report("history-next"), branch: "work/next");
        Assert.True(next.Ok, next.Message);
        run.Settled(next.Run);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), next.Run)!;
        Assert.Equal("1400", recorded.Target("watchHistoryTokensPerMinute"));
        Assert.Contains("\"normalTokensPerMinute\":30000", recorded.Target("watch"), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("claude-code", "watch-claude-audit")]
    [InlineData("codex", "watch-codex-audit")]
    public void TheWorkerRecordsEventStreamObservationsInsteadOfTheAgentsClaims(string launcher, string scenario)
    {
        using var run = new RunEstate(brief: RunEstate.Brief.Replace("the widget", "src/", StringComparison.Ordinal));
        RunResponse started = run.Start(launcher, scenario, run.Report("audit"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(["credentials", "network", "scope"], settled.Signals!.Select(s => s.Code).Order(StringComparer.Ordinal));
        Assert.All(settled.Signals!, s => Assert.Equal(1, s.Count));
        Assert.All(settled.Signals!, s => Assert.NotNull(s.Attention));
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal("worker", settled.Stop!.RequestedBy);
        Assert.NotNull(settled.Stop.ContainedAt);
        string record = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetRawText();
        Assert.DoesNotContain(".aws/credentials", record, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", record, StringComparison.Ordinal);
        Assert.DoesNotContain("I claim", record, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
        run.Reopen();
        Assert.Equal(3, run.Row(started.Run).Signals!.Count);
    }

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(20)), "The worker observation did not arrive.");
}
