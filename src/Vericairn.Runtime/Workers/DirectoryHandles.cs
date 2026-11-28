// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Runtime.Workers;

internal static class DirectoryHandles
{
    private const uint FileListDirectory = 0x00000001;
    private const uint FileReadAttributes = 0x00000080;
    private const uint DeleteAccess = 0x00010000;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfo = 9;

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

    internal static SafeFileHandle OpenWindows(string path, SafeFileHandle? parent = null)
    {
        // FILE_LIST_DIRECTORY makes the no-delete-sharing request effective. Attribute access alone does not.
        const uint access = FileListDirectory | FileReadAttributes;
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        SafeFileHandle handle = parent is null
            ? CreateFileW(path, access, ShareReadWrite, 0, openExisting, backupSemantics | OpenReparsePoint, 0)
            : OpenWindowsChild(parent, path, access);
        try
        {
            if (handle.IsInvalid) throw Failure();
            VerifyWindowsEntry(handle, directory: true);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static bool UnlinkWindows(SafeFileHandle parent, string name)
    {
        using SafeFileHandle handle = OpenWindowsChild(parent, name, DeleteAccess | FileReadAttributes, allowMissing: true);
        if (handle.IsInvalid) return false;
        VerifyWindowsEntry(handle, directory: false);
        byte delete = 1; // FILE_DISPOSITION_INFO.DeleteFile is a BOOLEAN.
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, in delete, sizeof(byte)))
            throw new IOException("The local file could not be removed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return true;
    }

    private static SafeFileHandle OpenWindowsChild(SafeFileHandle parent, string name, uint access, bool allowMissing = false)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.AsSpan().ContainsAny("\\/:\0"))
            throw new ArgumentException("A single file name is required.", nameof(name));

        // NtOpenFile interprets a single entry name relative to RootDirectory without reopening its path.
        const uint caseInsensitive = 0x00000040;
        const uint synchronousIoNonAlert = 0x00000020;
        ushort nameBytes = checked((ushort)(name.Length * sizeof(char)));
        nint buffer = Marshal.StringToHGlobalUni(name);
        nint objectName = 0;
        bool parentRef = false;
        try
        {
            var unicode = new UnicodeString { Length = nameBytes, MaximumLength = checked((ushort)(nameBytes + sizeof(char))), Buffer = buffer };
            objectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, objectName, fDeleteOld: false);
            parent.DangerousAddRef(ref parentRef);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(),
                ObjectName = objectName, Attributes = caseInsensitive,
            };
            int status = NtOpenFile(out SafeFileHandle handle, access | SynchronizeAccess, in attributes, out _,
                ShareReadWrite, OpenReparsePoint | synchronousIoNonAlert);
            if (status >= 0) return handle;
            handle.Dispose();
            const int objectNameNotFound = unchecked((int)0xc0000034);
            const int noSuchFile = unchecked((int)0xc000000f);
            if (allowMissing && status is objectNameNotFound or noSuchFile)
                return new SafeFileHandle(0, ownsHandle: true);
            throw new IOException("The local entry could not be opened without following links.", new Win32Exception(RtlNtStatusToDosError(status)));
        }
        finally
        {
            if (parentRef) parent.DangerousRelease();
            Marshal.FreeHGlobal(objectName);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void VerifyWindowsEntry(SafeFileHandle handle, bool directory)
    {
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out AttributeTag tag, Marshal.SizeOf<AttributeTag>())
            || (tag.Attributes & FileAttributes.ReparsePoint) != 0
            || ((tag.Attributes & FileAttributes.Directory) != 0) != directory)
            throw new IOException(directory ? "The local directory is a reparse point or could not be verified."
                : "The local file is a reparse point or could not be verified.");
    }

    private static IOException Failure() => new("The local directory could not be opened without following links.", new Win32Exception(Marshal.GetLastPInvokeError()));

#pragma warning disable SYSLIB1054 // Runtime marshalling keeps the project free of unsafe code.
#pragma warning disable CA2101 // UTF-8 on Unix, explicit Unicode entry points on Windows.
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)] internal static extern int OpenAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] internal static extern int UnlinkAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag tag, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, in byte disposition, int size);
    [DllImport("ntdll.dll")] private static extern int NtOpenFile(out SafeFileHandle handle, uint access, in ObjectAttributes attributes, out IoStatusBlock ioStatus, uint sharing, uint options);
    [DllImport("ntdll.dll")] private static extern int RtlNtStatusToDosError(int status);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public FileAttributes Attributes; public uint Tag; }
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public nint Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes
    {
        public int Length;
        public nint RootDirectory, ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public nint Status; public nuint Information; }
}
