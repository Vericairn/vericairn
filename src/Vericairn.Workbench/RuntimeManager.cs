// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;

namespace Vericairn.Workbench;

/// <summary>What a runtime operation observed: the command's exit and what it printed, never a claim beyond that.</summary>
public sealed record RuntimeOutcome(bool Ok, string Output);

/// <summary>Starts, stops and plans the daemon for an estate; Workbench is the runtime's manager, never its host.</summary>
public interface IRuntimeManager
{
    /// <summary>Starts a daemon for the estate and waits until it answers.</summary>
    Task<RuntimeOutcome> StartAsync(string root);

    /// <summary>Asks the daemon to stop.</summary>
    Task<RuntimeOutcome> StopAsync(string root);

    /// <summary>The per-user service unit and commands, without installing anything.</summary>
    Task<RuntimeOutcome> InstallPlanAsync(string root);

    /// <summary>Installs the daemon as a per-user service that starts at login; the plan says what this does.</summary>
    Task<RuntimeOutcome> InstallAsync(string root);

    /// <summary>Removes the per-user service installed before.</summary>
    Task<RuntimeOutcome> UninstallAsync(string root);
}

/// <summary>Drives the vericairn command line found beside this executable, named by VERICAIRN_CLI, or on the path.</summary>
public sealed class CliRuntimeManager : IRuntimeManager
{
    /// <summary>Where the command line was found, for the target context.</summary>
    public static string Locate()
    {
        string? configured = Environment.GetEnvironmentVariable("VERICAIRN_CLI");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        string name = OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn";
        string sibling = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(sibling) ? sibling : name;
    }

    /// <inheritdoc />
    public Task<RuntimeOutcome> StartAsync(string root) => RunAsync(root, "runtime", "start");

    /// <inheritdoc />
    public Task<RuntimeOutcome> StopAsync(string root) => RunAsync(root, "runtime", "stop");

    /// <inheritdoc />
    public Task<RuntimeOutcome> InstallPlanAsync(string root) => RunAsync(root, "runtime", "install", "--plan");

    /// <inheritdoc />
    public Task<RuntimeOutcome> InstallAsync(string root) => RunAsync(root, "runtime", "install");

    /// <inheritdoc />
    public Task<RuntimeOutcome> UninstallAsync(string root) => RunAsync(root, "runtime", "uninstall");

    private static async Task<RuntimeOutcome> RunAsync(string root, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(Locate()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--estate");
            start.ArgumentList.Add(root);
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);
            if (process is null)
            {
                return new RuntimeOutcome(false, "The command line could not be started.");
            }

            // The command's exit is what is awaited, never the end of its output: a service it starts may hold that output
            // open for as long as it runs. What the command wrote is collected while it runs and for a moment after it exits.
            var collected = new StringBuilder();
            var gate = new object();
            Task readOutput = Collect(process.StandardOutput, collected, gate);
            Task readError = Collect(process.StandardError, collected, gate);
            await process.WaitForExitAsync().ConfigureAwait(true);
            await Task.WhenAny(Task.WhenAll(readOutput, readError), Task.Delay(OutputGrace)).ConfigureAwait(true);
            lock (gate)
            {
                return new RuntimeOutcome(process.ExitCode == 0, collected.ToString().Trim());
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new RuntimeOutcome(false, exception.Message);
        }
    }

    /// <summary>How long output is still collected after the command exits.</summary>
    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    private static async Task Collect(StreamReader reader, StringBuilder sink, object gate)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                lock (gate)
                {
                    sink.Append(buffer, 0, read);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process was disposed while a service still held the stream open; what was read is kept.
        }
    }
}
