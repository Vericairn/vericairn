// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>What a setup command leaves for the run: its status, exit code and duration, never its command, environment or output.</summary>
/// <param name="Status">exited, timed-out, not-started, or unobserved when its exit or the end of its processes could not be observed.</param>
/// <param name="ExitCode">Its exit code, once it exited.</param>
/// <param name="DurationMilliseconds">How long it ran.</param>
internal sealed record WorkspaceSetupResult(string Status, int? ExitCode, long DurationMilliseconds)
{
    internal bool Succeeded => Status == "exited" && ExitCode == 0;

    internal string Facts => string.Create(CultureInfo.InvariantCulture,
        $"status {Status}, exit code {(ExitCode is int code ? code.ToString(CultureInfo.InvariantCulture) : "none")}, {DurationMilliseconds} ms");
}

/// <summary>
/// Runs a configured setup command in a workspace before dispatch: as the person, outside the harness's sandbox, inside its own process
/// group or job with a deadline. Nothing it starts outlives it, so the checks that follow see the workspace as it left it. What it runs
/// can be the work branch's own content, so it starts with a harness's filtered environment and the variables it names, never the
/// worker's whole environment (FIX-96).
/// </summary>
internal static class WorkspaceSetup
{
    private static readonly TimeSpan Cleanup = TimeSpan.FromSeconds(5);

    internal static WorkspaceSetupResult Run(WorkspaceSetupConfiguration configuration, string workspace, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var info = new ProcessStartInfo(configuration.Command[0])
        {
            WorkingDirectory = workspace, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (string argument in configuration.Command.Skip(1)) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach ((string name, string value) in Environment(environment, configuration.Variables ?? [])) info.Environment[name] = value;

        var watch = Stopwatch.StartNew();
        ContainedProcess process;
        try { process = ContainedProcess.Start(info); }
        catch (RunLaunchException)
        {
            return new("unobserved", null, watch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or ProviderRefusedException)
        {
            return new("not-started", null, watch.ElapsedMilliseconds);
        }

        // Output is drained and discarded: it can name paths and secrets of this machine.
        Task output = Drain(process.StandardOutput);
        Task error = Drain(process.StandardError);
        string status = "unobserved";
        int? exitCode = null;
        long duration;
        try
        {
            TimeSpan remaining = TimeSpan.FromSeconds(configuration.TimeoutSeconds) - watch.Elapsed;
            if (process.WaitForExit(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
            {
                status = "exited";
                exitCode = process.ExitCode;
            }
            else
            {
                status = "timed-out";
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            status = "unobserved";
        }
        finally
        {
            duration = watch.ElapsedMilliseconds;
            bool contained = false;
            try
            {
                // What it left running, or all of it at its deadline, ends now: the group or job survives the root's exit.
                // A group already empty is not signalled; macOS refuses a signal to a group of exited members.
                try
                {
                    if (!process.BoundaryExited()) process.Signal(force: true);
                }
                catch (Win32Exception)
                {
                    // It may have emptied meanwhile; only the observation below decides.
                }

                var cleanup = Stopwatch.StartNew();
                do
                {
                    contained = process.WaitForExit(TimeSpan.Zero) && process.BoundaryExited() && output.IsCompleted && error.IsCompleted;
                    if (contained) break;
                    Thread.Sleep(10);
                }
                while (cleanup.Elapsed < Cleanup);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                contained = false;
            }

            if (contained)
            {
                process.Close();
            }
            else
            {
                // Never release a process that may still change the workspace; the checks are not reached.
                process.Detach();
                status = "unobserved";
                exitCode = null;
            }
        }

        return new(status, exitCode, duration);
    }

    /// <summary>
    /// What a harness would receive of the person's environment, by its allowlist and with its credential and loader filters, plus the
    /// variables the configuration names, as the person holds them; never a variable that would hand the command Vericairn's home or
    /// a run's bindings.
    /// </summary>
    internal static Dictionary<string, string> Environment(IReadOnlyDictionary<string, string> source, IReadOnlyCollection<string> named)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(named);
        var names = new HashSet<string>(named, WorkerEnvironment.Names);
        var environment = new Dictionary<string, string>(WorkerEnvironment.Names);
        foreach ((string name, string value) in WorkerEnvironment.Build(source).Concat(source.Where(variable => names.Contains(variable.Key))))
        {
            if (!name.StartsWith("VERICAIRN_", StringComparison.OrdinalIgnoreCase)) environment[name] = value;
        }

        return environment;
    }

    private static Task Drain(StreamReader reader) => Task.Factory.StartNew(() =>
    {
        try { reader.BaseStream.CopyTo(Stream.Null); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A closed pipe ends the drain.
        }
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
