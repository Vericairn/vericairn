// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Only aggregate facts leave setup; command text, environment and process output never do.</summary>
internal sealed record WorkspaceSetupResult(string Status, int? ExitCode, long DurationMilliseconds)
{
    internal bool Success => Status == "exited" && ExitCode == 0;
    internal string Summary => string.Create(CultureInfo.InvariantCulture,
        $"Workspace setup: {Status}; exit code {(ExitCode is int code ? code.ToString(CultureInfo.InvariantCulture) : "unavailable")}; duration {DurationMilliseconds} ms.");
}

internal static class WorkspaceSetup
{
    internal static WorkspaceSetupResult Run(WorkspaceSetupConfiguration configuration, string workspace, IReadOnlyDictionary<string, string> environment)
    {
        var info = new ProcessStartInfo(configuration.Command[0])
        {
            WorkingDirectory = workspace, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in configuration.Command.Skip(1)) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            // Setup belongs to the person, outside the harness sandbox. Runtime bindings and custody helpers do not pass.
            if (!name.StartsWith("VERICAIRN_", StringComparison.OrdinalIgnoreCase)
                && !WorkerEnvironment.McpServer.Contains(name, StringComparer.OrdinalIgnoreCase)) info.Environment[name] = value;
        }

        var watch = Stopwatch.StartNew();
        ContainedProcess process;
        try { process = ContainedProcess.Start(info); }
        catch (Exception exception) when (exception is Win32Exception or IOException or ProviderRefusedException or RunLaunchException)
        {
            return new("not-started", null, watch.ElapsedMilliseconds);
        }

        Task output = Drain(process.StandardOutput);
        Task error = Drain(process.StandardError);
        string status = "unverified";
        int? exitCode = null;
        bool contained = false;
        try
        {
            while (watch.Elapsed < TimeSpan.FromSeconds(configuration.TimeoutSeconds))
            {
                if (process.WaitForExit(TimeSpan.Zero) && process.BoundaryExited() && output.IsCompleted && error.IsCompleted)
                {
                    contained = true;
                    Task.WhenAll(output, error).GetAwaiter().GetResult();
                    status = "exited";
                    exitCode = process.ExitCode;
                    break;
                }
                Thread.Sleep(10);
            }
            if (!contained) status = "timed-out";
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status = "unverified";
        }
        finally
        {
            if (!contained)
            {
                try
                {
                    process.Signal(force: true);
                    var cleanup = Stopwatch.StartNew();
                    do
                    {
                        contained = process.WaitForExit(TimeSpan.Zero) && process.BoundaryExited() && output.IsCompleted && error.IsCompleted;
                        if (contained) break;
                        Thread.Sleep(10);
                    } while (cleanup.Elapsed < TimeSpan.FromSeconds(1));
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException) { }
            }
            if (contained) process.Close();
            else process.Detach();
        }
        return new(status, exitCode, watch.ElapsedMilliseconds);

        static Task Drain(StreamReader reader) => Task.Factory.StartNew(() => reader.BaseStream.CopyTo(Stream.Null),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
