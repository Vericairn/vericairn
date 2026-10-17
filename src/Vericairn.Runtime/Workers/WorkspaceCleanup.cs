// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Runtime.Workers;

/// <summary>Deletes an owned, inactive workspace or disposable Git directory without changing link targets.</summary>
internal static class WorkspaceCleanup
{
    internal static void Delete(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix unlink does not require a file's write bit. The framework removes directory links themselves,
            // without descending into them; chmod would needlessly change the targets of file or hard links.
            Directory.Delete(path, recursive: true);
            return;
        }

        DeleteWindows(Path.GetFullPath(path));
    }

    private static void DeleteWindows(string path)
    {
        FileAttributes attributes;
        // Hold each ancestor without write/delete sharing during enumeration and recursion. Opening the leaf reparse point
        // itself and setting attributes by handle prevents a link swap from redirecting attribute changes outside it.
        string extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        using (SafeFileHandle entry = CreateFileW(extended, 0x10180, 1, 0, 3, 0x02200000, 0)) // DELETE | READ/WRITE_ATTRIBUTES, BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        {
            if (entry.IsInvalid) throw new IOException("The workspace entry could not be opened safely for cleanup.", new Win32Exception(Marshal.GetLastPInvokeError()));
            attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string child in Directory.EnumerateFileSystemEntries(path)) DeleteWindows(child);
                }

                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    if (!GetFileInformationByHandleEx(entry, 1, out FileStandard info, Marshal.SizeOf<FileStandard>()))
                        throw new IOException("The workspace entry's link count could not be verified for cleanup.", new Win32Exception(Marshal.GetLastPInvokeError()));
                    if ((attributes & FileAttributes.Directory) == 0 && info.Links > 1)
                    {
                        // Attributes belong to the shared file, not this name. Unlink a read-only hard link without
                        // changing the other names' attributes (FILE_DISPOSITION_IGNORE_READONLY_ATTRIBUTE).
                        uint flags = 0x11; // DELETE | IGNORE_READONLY_ATTRIBUTE
                        if (!SetFileInformationByHandle(entry, 21, ref flags, sizeof(uint)))
                            throw new IOException("The read-only hard link could not be removed without changing its other names.", new Win32Exception(Marshal.GetLastPInvokeError()));
                        return;
                    }
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }

        // The parent is still pinned. Non-recursive deletion removes a link itself even if the leaf changed after close.
        if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
        else File.Delete(path);
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling keeps the project free of unsafe code.
#pragma warning disable CA2101 // Explicit Unicode entry point.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int kind, out FileStandard info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref uint flags, int size);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandard { public long AllocationSize, EndOfFile; public uint Links; public byte DeletePending, Directory; }
}
