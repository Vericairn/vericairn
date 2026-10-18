// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

internal static class WorkspacePaths
{
    public static string Canonical(string workspace)
    {
        if (!OperatingSystem.IsWindows())
        {
            nint path = realpath(workspace, 0);
            if (path == 0)
            {
                throw new ProviderRefusedException("The harness workspace could not be canonicalized.");
            }

            try
            {
                return Marshal.PtrToStringUTF8(path)!;
            }
            finally
            {
                free(path);
            }
        }

        using SafeFileHandle directory = CreateFileW(workspace, 0, 7, 0, 3, 0x02000000, 0);
        char[] buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(directory, buffer, (uint)buffer.Length, 0);
        if (directory.IsInvalid || length == 0 || length >= buffer.Length)
        {
            throw new ProviderRefusedException("The harness workspace could not be canonicalized.");
        }

        string result = new(buffer, 0, (int)length);
        return result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + result[8..]
            : result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [DllImport("libc", SetLastError = true)] private static extern nint realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint resolved);
    [DllImport("libc")] private static extern void free(nint pointer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
