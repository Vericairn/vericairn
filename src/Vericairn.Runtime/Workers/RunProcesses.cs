// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// Whether the processes a run's retained observation names are gone, for settling a run whose supervising worker died. Only
/// an observed absence counts: a process that exists and could be the recorded one, or that cannot be inspected, counts as
/// running, so an uninspectable or reused process id can delay a settlement but never cause one.
/// </summary>
internal static class RunProcesses
{
    // The launch is retained truncated to seconds, and Linux derives process start times from a boot time with
    // whole-second resolution. A process that started later than this after the launch cannot be the recorded one.
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(5);

    /// <summary>
    /// True when no process with this id can be the recorded one: none exists, it has exited, or it started after the launch,
    /// so the id was reused. The supervising worker started before the launch and the harness at it.
    /// </summary>
    internal static bool Gone(int processId, DateTimeOffset launchedAt)
    {
        if (processId <= 0) return false;
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.HasExited || process.StartTime.ToUniversalTime() > launchedAt.UtcDateTime + StartTolerance;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the harness's whole containment boundary is gone: on Linux and macOS no member of its process group remains,
    /// including a reparented descendant; on Windows its Job Object closed with the worker's last handle, terminating every
    /// member, so the harness's own absence is the boundary's.
    /// </summary>
    internal static bool BoundaryGone(int processId, DateTimeOffset launchedAt) =>
        Gone(processId, launchedAt) && (OperatingSystem.IsWindows() || ContainedProcess.UnixGroupGone(processId));
}
