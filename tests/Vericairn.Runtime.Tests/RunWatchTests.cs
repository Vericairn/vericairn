// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class RunWatchTests
{
    [Fact]
    public void BurnUsesAMinimumSampleTheLauncherReferenceHistoryAndCap()
    {
        var watch = new RunWatch(new RunWatchSettings(), 1_000_000, null);
        Assert.Empty(watch.Usage(null, TimeSpan.FromSeconds(60)));
        Assert.Empty(watch.Usage(100_000, TimeSpan.FromSeconds(30)));
        RunSignal warning = Assert.Single(watch.Usage(100_000, TimeSpan.FromSeconds(60)));
        Assert.Equal("warning", warning.Severity);
        Assert.Contains("goal history median unavailable", warning.Summary, StringComparison.Ordinal);
        Assert.Contains("against token cap 1000000", warning.Summary, StringComparison.Ordinal);
        RunSignal critical = Assert.Single(watch.Usage(400_000, TimeSpan.FromSeconds(120)));
        Assert.Equal("critical", critical.Severity);
        Assert.Equal("burn-rate", critical.Code);

        var normalForGoal = new RunWatch(new RunWatchSettings(), 1_000_000, 100_000);
        Assert.Empty(normalForGoal.Usage(100_000, TimeSpan.FromSeconds(60)));
        var small = new RunWatch(new RunWatchSettings(NormalTokensPerMinute: 1), 1_000_000, null);
        Assert.Empty(small.Usage(9_999, TimeSpan.FromSeconds(60)));
        Assert.Empty(watch.Usage(1, TimeSpan.FromSeconds(121))); // A decreasing counter is not a rate sample.
    }

    [Fact]
    public void ARecentBurstIsNotHiddenByAnEarlierQuietPeriodAndExtremeCountsDoNotOverflow()
    {
        var watch = new RunWatch(new RunWatchSettings(), long.MaxValue, null);
        Assert.Empty(watch.Usage(1, TimeSpan.FromSeconds(3600)));
        Assert.Equal("critical", Assert.Single(watch.Usage(long.MaxValue, TimeSpan.FromSeconds(3660))).Severity);
        Assert.Empty(watch.Usage(long.MaxValue, TimeSpan.FromSeconds(3720)));
    }

    [Fact]
    public void LoopsIncludeAlternatingActionsAndAgeOutOfABoundedWindow()
    {
        var watch = new RunWatch(new RunWatchSettings(LoopWarning: 3, LoopStop: 4, LoopWindow: 6), null, null);
        HarnessAction a = Action("same");
        HarnessAction b = Action("other");
        Assert.Empty(watch.Action(a));
        Assert.Empty(watch.Action(b));
        Assert.Empty(watch.Action(a));
        Assert.Empty(watch.Action(b));
        RunSignal warning = Assert.Single(watch.Action(a));
        Assert.Equal("warning", warning.Severity);
        Assert.Equal(3, warning.Count);
        Assert.Equal("critical", Assert.Single(watch.Action(a)).Severity);
        for (int i = 0; i < 6; i++)
        {
            Assert.Empty(watch.Action(Action("different-" + i)));
        }

        Assert.Empty(watch.Action(a));
    }

    private static HarnessAction Action(string argument) => new(Guid.NewGuid().ToString("N"), "Read", JsonSerializer.SerializeToElement(new { file_path = argument }));
}
