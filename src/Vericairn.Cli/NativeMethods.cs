// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;

namespace Vericairn.Cli;

/// <summary>
/// The one native call the command line makes. On Windows a started process inherits every inheritable handle of its parent,
/// including the parent's own standard streams when its caller made them inheritable; a daemon started from 'runtime start'
/// would then hold its caller's output open for as long as it serves. Clearing the inherit flag on this process's standard
/// handles before starting the daemon keeps them out of it. Elsewhere a started process receives only the streams it is given.
/// </summary>
internal static class NativeMethods
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint HandleFlagInherit = 0x00000001;

    /// <summary>Keeps this process's standard handles from being inherited by the processes it starts (Windows only; a no-op elsewhere).</summary>
    public static void KeepStandardHandlesFromChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (int which in new[] { StandardInput, StandardOutput, StandardError })
        {
            IntPtr handle = GetStdHandle(which);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);
}
