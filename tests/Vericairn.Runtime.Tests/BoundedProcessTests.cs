// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class BoundedProcessTests
{
    [Fact]
    public void ASilentChildTimesOutFromLaunch()
    {
        using var child = new ProcessHarness.Scenario("silent");
        var clock = new WorkerClock();
        long started = clock.GetTimestamp();
        ProcessResult result = BoundedProcess.Run(child.Command, TimeSpan.FromMilliseconds(300),
            cancellationToken: TestContext.Current.CancellationToken, clock: clock, launched: () => child.WhenReady(() => clock.Advance(TimeSpan.FromMilliseconds(300))));
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(result.Started);
        Assert.Null(result.ExitCode);
        Assert.True(clock.GetElapsedTime(started) < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AnObservedExitAfterTheDeadlineIsStillATimeout()
    {
        var clock = new WorkerClock();
        bool exited = false;
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("nonzero"), TimeSpan.FromDays(1),
            clock: clock, rootExited: () => { exited = true; clock.Advance(TimeSpan.FromDays(1)); },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(exited, "The child must have exited before its deadline is advanced.");
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(result.Started);
        Assert.Null(result.ExitCode);
        Assert.Equal("output", result.StandardOutput);
        Assert.Equal("error", result.StandardError);
    }

    [Fact]
    public void BothPipesAreDrainedWhileALargeStandardErrorIsWritten()
    {
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("stderr"), TestTiming.HangTimeout, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.StandardOutput);
        Assert.Equal(new string('e', 256 * 1024), result.StandardError);
    }

    [Theory]
    [InlineData("output-limit")]
    [InlineData("stderr")]
    public void OutputLimitsTerminateTheChildAndBoundRetainedText(string scenario)
    {
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command(scenario), TestTiming.HangTimeout, outputLimit: 4096, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.OutputLimitExceeded, result.Outcome);
        Assert.True(result.Started);
        Assert.InRange(result.StandardOutput.Length, 0, 4096);
        Assert.InRange(result.StandardError.Length, 0, 4096);
    }

    [Fact]
    public void ANonzeroExitRetainsBothStreamsAndItsExitCode()
    {
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("nonzero"), TestTiming.HangTimeout, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.True(result.Started);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("output", result.StandardOutput);
        Assert.Equal("error", result.StandardError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationReportsWhetherAChildWasStarted(bool beforeStart)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (beforeStart)
        {
            cancellation.Cancel();
        }
        using var child = new ProcessHarness.Scenario("silent");
        var clock = new WorkerClock();
        long started = clock.GetTimestamp();
        ProcessResult result = BoundedProcess.Run(child.Command, TestTiming.HangTimeout, cancellationToken: cancellation.Token,
            clock: clock, launched: () => child.WhenReady(cancellation.Cancel));
        Assert.Equal(ProcessOutcome.Canceled, result.Outcome);
        Assert.Equal(!beforeStart, result.Started);
        Assert.Null(result.ExitCode);
        Assert.True(clock.GetElapsedTime(started) < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AMissingExecutableHasNoPossibleEffect()
    {
        var info = new ProcessStartInfo("vericairn-missing-" + Guid.NewGuid().ToString("N")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        ProcessResult result = BoundedProcess.Run(info, TestTiming.HangTimeout, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.NotStarted, result.Outcome);
        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void AnExitedRootDoesNotLeaveADescendantHoldingThePipes()
    {
        var clock = new WorkerClock();
        long started = clock.GetTimestamp();
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("descendant"), TimeSpan.FromSeconds(2),
            cancellationToken: TestContext.Current.CancellationToken, clock: clock, rootExited: () => clock.Advance(TimeSpan.FromSeconds(2)));
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(clock.GetElapsedTime(started) < TimeSpan.FromSeconds(5));
        int childId = int.Parse(result.StandardOutput.Trim(), CultureInfo.InvariantCulture);
        try
        {
            using Process child = Process.GetProcessById(childId);
            Assert.True(child.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds), "The descendant must be terminated even after its parent exits.");
        }
        catch (ArgumentException)
        {
            // It was terminated and reaped already.
        }
    }
}
