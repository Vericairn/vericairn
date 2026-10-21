// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

public sealed class TestTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFileBarrierObservesReadinessBeforeOrAfterTheWaitStarts(bool alreadyReady)
    {
        using var estate = new TestEstate();
        string path = Path.Combine(estate.Root, "ready");
        if (alreadyReady) File.WriteAllText(path, "ready");
        using var changed = new FileSignal(estate.Root);
        Task waiting = changed.UntilAsync(() => File.Exists(path), "fixture readiness", TestContext.Current.CancellationToken);
        if (!alreadyReady)
        {
            Assert.False(waiting.IsCompleted);
            File.WriteAllText(path, "ready");
        }

        await waiting.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AFileBarrierObservesNestedCreationAndAnAtomicReplacement()
    {
        using var estate = new TestEstate();
        string path = Path.Combine(estate.Root, "nested", "ready");
        using var changed = new FileSignal(estate.Root);
        Task created = changed.UntilAsync(() => File.Exists(path), "nested creation", TestContext.Current.CancellationToken);
        Assert.False(created.IsCompleted);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "before");
        await created.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Task replaced = changed.UntilAsync(() => File.ReadAllText(path) == "after", "atomic replacement", TestContext.Current.CancellationToken);
        Assert.False(replaced.IsCompleted);
        File.WriteAllText(path + ".next", "after");
        File.Move(path + ".next", path, overwrite: true);
        await replaced.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Equal("after", File.ReadAllText(path));
    }

    [Fact]
    public void AManualClockFiresOneShotAndPeriodicTimersOnlyWhenAdvanced()
    {
        var clock = new ClientClock(allowPeriodic: true);
        int once = 0, repeated = 0;
        using ITimer one = clock.CreateTimer(_ => once++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using ITimer periodic = clock.CreateTimer(_ => repeated++, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        clock.FireDueTimers();
        Assert.Equal((0, 0), (once, repeated));
        clock.Elapse(TimeSpan.FromSeconds(1));
        Assert.Equal((1, 0), (once, repeated));
        clock.Elapse(TimeSpan.FromSeconds(1));
        Assert.Equal((1, 1), (once, repeated));
        clock.Elapse(TimeSpan.FromSeconds(2));
        Assert.Equal((1, 2), (once, repeated));
        periodic.Dispose();
        clock.Elapse(TimeSpan.FromDays(1));
        Assert.Equal((1, 2), (once, repeated));
    }

    [Fact]
    public void AMissingOutsidePrerequisiteIsNamedBeforeAnyTestAction()
    {
        Exception failure = Assert.ThrowsAny<Exception>(() => TestPrerequisites.RequireExecutable("vericairn-test-missing-prerequisite"));
        Assert.Contains("Missing prerequisite: vericairn-test-missing-prerequisite", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("skip", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
