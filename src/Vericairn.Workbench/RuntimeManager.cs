// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

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

            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
            string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            return new RuntimeOutcome(process.ExitCode == 0, (output + error).Trim());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new RuntimeOutcome(false, exception.Message);
        }
    }
}
