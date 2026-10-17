// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Providers;

internal enum ProcessOutcome
{
    Exited,
    NotStarted,
    TimedOut,
    Canceled,
    OutputLimitExceeded,
    Failed,
}

internal sealed record ProcessResult(ProcessOutcome Outcome, bool Started, int? ExitCode, string StandardOutput, string StandardError)
{
    internal string Description => Outcome switch
    {
        ProcessOutcome.NotStarted => "could not be started",
        ProcessOutcome.TimedOut => "did not answer before its deadline",
        ProcessOutcome.Canceled => "was canceled",
        ProcessOutcome.OutputLimitExceeded => "exceeded its output limit",
        _ => "could not be observed to completion",
    };
}

internal static class BoundedProcess
{
    // Each stream retains at most this many characters. Overflow fails the command rather than parsing truncated output.
    internal const int DefaultOutputLimit = 4 * 1024 * 1024;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(1);

    internal static ProcessResult Run(ProcessStartInfo info, TimeSpan timeout, int outputLimit = DefaultOutputLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLimit);
        if (cancellationToken.IsCancellationRequested)
        {
            return new(ProcessOutcome.Canceled, false, null, string.Empty, string.Empty);
        }

        if (string.IsNullOrEmpty(info.WorkingDirectory))
        {
            info.WorkingDirectory = Environment.CurrentDirectory;
        }

        // The deadline includes launch, both drains and exit observation. No stream read starts a fresh allowance.
        var watch = Stopwatch.StartNew();
        ContainedProcess process;
        try
        {
            process = ContainedProcess.Start(info);
        }
        catch (RunLaunchException)
        {
            return new(ProcessOutcome.Failed, true, null, string.Empty, string.Empty);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or ProviderRefusedException)
        {
            return new(ProcessOutcome.NotStarted, false, null, string.Empty, string.Empty);
        }

        var output = new Capture(outputLimit);
        var error = new Capture(outputLimit);
        Task outputTask = Task.CompletedTask;
        Task errorTask = Task.CompletedTask;
        ProcessOutcome outcome = ProcessOutcome.Failed;
        int? exitCode = null;
        try
        {
            // Native pipe handles are synchronous. Dedicated readers keep both pipes draining even if the pool is busy.
            outputTask = Task.Factory.StartNew(() => output.Drain(process.StandardOutput), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            errorTask = Task.Factory.StartNew(() => error.Drain(process.StandardError), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    outcome = ProcessOutcome.Canceled;
                    break;
                }

                if (output.Overflow || error.Overflow)
                {
                    outcome = ProcessOutcome.OutputLimitExceeded;
                    break;
                }

                if (output.Failed || error.Failed)
                {
                    break;
                }

                if (watch.Elapsed >= timeout)
                {
                    outcome = ProcessOutcome.TimedOut;
                    break;
                }

                if (process.WaitForExit(TimeSpan.Zero) && outputTask.IsCompleted && errorTask.IsCompleted)
                {
                    // Recheck capture failures after joining the readers; neither partial nor oversized output is success.
                    outcome = output.Overflow || error.Overflow ? ProcessOutcome.OutputLimitExceeded
                        : output.Failed || error.Failed ? ProcessOutcome.Failed : ProcessOutcome.Exited;
                    exitCode = outcome == ProcessOutcome.Exited ? process.ExitCode : null;
                    break;
                }

                Thread.Sleep(10);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or TaskSchedulerException)
        {
            outcome = ProcessOutcome.Failed;
        }
        finally
        {
            bool exited = false;
            try
            {
                exited = process.WaitForExit(TimeSpan.Zero);
                if (outcome != ProcessOutcome.Exited)
                {
                    // The owned POSIX group / Windows job survives the root's exit, so descendants retaining pipes are killed too.
                    process.Signal(force: true);
                }

                var cleanup = Stopwatch.StartNew();
                do
                {
                    exited = process.WaitForExit(TimeSpan.Zero);
                    if (exited && outputTask.IsCompleted && errorTask.IsCompleted)
                    {
                        break;
                    }

                    Thread.Sleep(10);
                }
                while (cleanup.Elapsed < CleanupTimeout);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
            {
                if (outcome == ProcessOutcome.Exited)
                {
                    outcome = ProcessOutcome.Failed;
                    exitCode = null;
                }
            }
            finally
            {
                if (exited && outputTask.IsCompleted && errorTask.IsCompleted)
                {
                    process.Close();
                }
                else
                {
                    // Do not turn an unresponsive cleanup into an unbounded caller wait or release a still-owned PID.
                    process.Detach();
                }
            }
        }

        return new(outcome, true, exitCode, output.Text, error.Text);
    }

    private sealed class Capture(int limit)
    {
        private readonly object gate = new();
        private readonly StringBuilder text = new();
        private volatile bool overflow;
        private volatile bool failed;

        internal bool Overflow => overflow;
        internal bool Failed => failed;
        internal string Text { get { lock (gate) { return text.ToString(); } } }

        internal void Drain(StreamReader reader)
        {
            char[] buffer = new char[4096];
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) != 0)
                {
                    lock (gate)
                    {
                        int retained = Math.Min(read, limit - text.Length);
                        text.Append(buffer, 0, retained);
                        if (retained != read)
                        {
                            overflow = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                failed = true;
            }
        }
    }
}
