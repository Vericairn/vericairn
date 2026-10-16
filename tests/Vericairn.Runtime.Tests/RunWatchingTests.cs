// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunWatchingTests
{
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
        string record = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetRawText();
        Assert.DoesNotContain(".aws/credentials", record, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", record, StringComparison.Ordinal);
        Assert.DoesNotContain("I claim", record, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
        run.Reopen();
        Assert.Equal(3, run.Row(started.Run).Signals!.Count);
    }
}
