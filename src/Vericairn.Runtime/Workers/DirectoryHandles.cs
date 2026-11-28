// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Runtime.Workers;

internal static class DirectoryHandles
{
    internal static SafeFileHandle OpenUnix(string path, SafeFileHandle? parent = null, bool searchOnly = false)
    {
        int noFollow = OperatingSystem.IsMacOS() ? 0x100 : 0x20000;
        int directory = OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;
        int closeOnExec = OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;
        int access = searchOnly ? (OperatingSystem.IsMacOS() ? 0x40000000 : 0x200000) : 0; // O_SEARCH or O_PATH
        // An absolute path ignores the parent descriptor.
        int descriptor = OpenAt(parent is null ? -1 : (int)parent.DangerousGetHandle(), path, access | noFollow | directory | closeOnExec, 0);
        return descriptor >= 0 ? new SafeFileHandle(descriptor, ownsHandle: true) : throw Failure();
    }

    internal static SafeFileHandle OpenWindows(string path)
    {
        // Open the reparse point itself and deny delete sharing to hold the directory in place.
        SafeFileHandle handle = CreateFileW(path, 0x80, 3, 0, 3, 0x02000000 | 0x00200000, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw Failure();
        }

        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTag tag, Marshal.SizeOf<AttributeTag>()) || (tag.Attributes & 0x400) != 0)
        {
            handle.Dispose();
            throw new IOException("The local directory is a reparse point or could not be verified.");
        }
        return handle;
    }

    private static IOException Failure() => new("The local directory could not be opened without following links.", new Win32Exception(Marshal.GetLastPInvokeError()));

#pragma warning disable SYSLIB1054 // Runtime marshalling keeps the project free of unsafe code.
#pragma warning disable CA2101 // UTF-8 on Unix, explicit Unicode entry points on Windows.
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)] internal static extern int OpenAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] internal static extern int UnlinkAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag tag, int size);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes, Tag; }
}
