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
        var watch = Stopwatch.StartNew();
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("silent"), TimeSpan.FromMilliseconds(300), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(result.Started);
        Assert.Null(result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
    }

    [Fact]
    public void BothPipesAreDrainedWhileALargeStandardErrorIsWritten()
    {
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("stderr"), TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);
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
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command(scenario), TimeSpan.FromSeconds(3), outputLimit: 4096, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.OutputLimitExceeded, result.Outcome);
        Assert.True(result.Started);
        Assert.InRange(result.StandardOutput.Length, 0, 4096);
        Assert.InRange(result.StandardError.Length, 0, 4096);
    }

    [Fact]
    public void ANonzeroExitRetainsBothStreamsAndItsExitCode()
    {
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("nonzero"), TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);
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
        else
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        }

        var watch = Stopwatch.StartNew();
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("silent"), TimeSpan.FromSeconds(10), cancellationToken: cancellation.Token);
        Assert.Equal(ProcessOutcome.Canceled, result.Outcome);
        Assert.Equal(!beforeStart, result.Started);
        Assert.Null(result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
    }

    [Fact]
    public void AMissingExecutableHasNoPossibleEffect()
    {
        var info = new ProcessStartInfo("vericairn-missing-" + Guid.NewGuid().ToString("N")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        ProcessResult result = BoundedProcess.Run(info, TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.NotStarted, result.Outcome);
        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void AnExitedRootDoesNotLeaveADescendantHoldingThePipes()
    {
        var watch = Stopwatch.StartNew();
        ProcessResult result = BoundedProcess.Run(ProcessHarness.Command("descendant"), TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        int childId = int.Parse(result.StandardOutput.Trim(), CultureInfo.InvariantCulture);
        try
        {
            using Process child = Process.GetProcessById(childId);
            Assert.True(child.WaitForExit(2000), "The descendant must be terminated even after its parent exits.");
        }
        catch (ArgumentException)
        {
            // It was terminated and reaped already.
        }
    }
}
