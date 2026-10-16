// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>The worker's isolated filesystem observer, hosted by the command line so a deadline can terminate even blocked I/O.</summary>
public static class WorkspaceScopeProbe
{
    internal const string Marker = "--vericairn-worker-scope-audit";

    /// <summary>Handles the private worker subprocess invocation; null for an ordinary command line. Writes only a scope snapshot.</summary>
    public static int? TryRun(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (2 or 5) || arguments[0] != Marker)
        {
            return null;
        }

        Console.Out.Write(JsonSerializer.Serialize(arguments.Count == 2
            ? WorkspaceScopeAudit.CaptureLocal(arguments[1], WorkspaceScopeAudit.Deadline)
            : WorkspaceScopeAudit.CaptureLocal(arguments[1], WorkspaceScopeAudit.Deadline, arguments[2], arguments[3], arguments[4])));
        return 0;
    }

    internal static WorkspaceSnapshot Capture(string workspace, TimeSpan deadline, IReadOnlyList<string>? command = null, string? branch = null, string? baseline = null, string? gitDirectory = null)
    {
        if (deadline <= TimeSpan.Zero)
        {
            return Incomplete();
        }

        command ??= WorkerOptions.Default.VericairnCommand();
        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in command.Skip(1).Concat([Marker, workspace]))
        {
            info.ArgumentList.Add(argument);
        }

        if (branch is not null)
        {
            foreach (string argument in new[] { branch, baseline ?? string.Empty, gitDirectory ?? string.Empty })
            {
                info.ArgumentList.Add(argument);
            }
        }

        info.Environment.Clear();
        foreach ((string name, string value) in WorkerEnvironment.Build(WorkerEnvironment.Current(), []))
        {
            info.Environment[name] = value;
        }

        try
        {
            using Process process = Process.Start(info) ?? throw new IOException("The scope observer could not start.");
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(deadline))
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(5000);
                return Incomplete();
            }

            // Bound pipe reads even if an observer or one of its Git children does not close them.
            string json = output.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _ = error.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return process.ExitCode == 0 ? JsonSerializer.Deserialize<WorkspaceSnapshot>(json) ?? Incomplete() : Incomplete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or JsonException or TimeoutException)
        {
            return Incomplete();
        }
    }

    private static WorkspaceSnapshot Incomplete() => new(new Dictionary<string, string>(StringComparer.Ordinal), "The isolated scope audit could not complete within its deadline; assignment scope is unverified.");
}
