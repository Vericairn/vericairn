// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Adapters.Git;

internal static partial class AcquisitionWorkingTree
{
    /// <summary>Creates missing ordinary directories without writing through a redirected parent.</summary>
    internal static void CreateDirectories(string root, string path)
    {
        string[] names = RelativeNames(root, path, allowRoot: true);
        using OpenedDirectory directory = OpenRoot(root);
        Create(directory, 0);

        void Create(OpenedDirectory parent, int index)
        {
            if (index == names.Length) return;
            parent.EnsureDirectory(names[index]);
            using OpenedDirectory child = parent.OpenDirectory(names[index]);
            Create(child, index + 1);
        }
    }

    /// <summary>Writes a new file without overwriting a name another process already owns.</summary>
    internal static void WriteNew(string root, string path, Stream bytes) =>
        AtParent(root, path, (parent, leaf) => parent.WriteNew(leaf, bytes));

    /// <summary>Publishes an existing regular staging file by an atomic move beneath the same selected root.</summary>
    internal static void Move(string root, string from, string to, bool overwrite)
    {
        string[] sourceNames = RelativeNames(root, from, allowRoot: false);
        string[] targetNames = RelativeNames(root, to, allowRoot: false);
        using OpenedDirectory directory = OpenRoot(root);
        AtParent(directory, sourceNames, (source, oldLeaf) =>
            AtParent(directory, targetNames, (target, newLeaf) => source.Move(oldLeaf, target, newLeaf, overwrite)));
    }

    /// <summary>Removes a staging file from the selected root. An already-moved file is absent.</summary>
    internal static void RemoveFile(string root, string path) =>
        AtParent(root, path, (parent, leaf) => parent.Remove(leaf));

    private static void AtParent(string root, string path, Action<OpenedDirectory, string> action)
    {
        string[] names = RelativeNames(root, path, allowRoot: false);
        using OpenedDirectory directory = OpenRoot(root);
        AtParent(directory, names, action);
    }

    private static void AtParent(OpenedDirectory directory, string[] names, Action<OpenedDirectory, string> action)
    {
        Enter(directory, 0);

        void Enter(OpenedDirectory parent, int index)
        {
            if (index == names.Length - 1)
            {
                action(parent, names[index]);
                return;
            }

            using OpenedDirectory child = parent.OpenDirectory(names[index]);
            Enter(child, index + 1);
        }
    }

    private sealed partial class OpenedDirectory
    {
        internal void EnsureDirectory(string leaf)
        {
            // Existence here does not establish safety. OpenDirectory must still pin the child without following it.
            bool created = OperatingSystem.IsWindows()
                ? CreateDirectoryW(WorkspaceScopeAudit.ExtendedWindowsPath(Path.Combine(WindowsPath, leaf)), 0)
                : mkdirat(Descriptor, leaf, 0x1c0) == 0;
            if (!created && Marshal.GetLastPInvokeError() != (OperatingSystem.IsWindows() ? 183 : 17))
                throw NativeFailure("the publication directory could not be created safely.");
        }

        internal void WriteNew(string leaf, Stream bytes)
        {
            using SafeFileHandle file = CreateExclusive(leaf);
            try
            {
                using var stream = new FileStream(file, FileAccess.Write);
                bytes.CopyTo(stream);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                file.Dispose();
                try { Remove(leaf); }
                catch (IOException) { /* Keep the original write failure. */ }
                throw;
            }
        }

        // O_EXCL and CREATE_NEW prevent opening preexisting hard links, symbolic links or special files for writing.
        private SafeFileHandle CreateExclusive(string leaf)
        {
            SafeFileHandle file;
            if (OperatingSystem.IsWindows())
            {
                file = CreateFileW(WorkspaceScopeAudit.ExtendedWindowsPath(Path.Combine(WindowsPath, leaf)),
                    0x40000000, 0, 0, 1, 0x00200000, 0); // GENERIC_WRITE, CREATE_NEW, OPEN_REPARSE_POINT.
            }
            else
            {
                int exclusive = OperatingSystem.IsMacOS() ? 0x200 | 0x800 : 0x40 | 0x80;
                int descriptor = OpenCreateAt(Descriptor, leaf, UnixOpenFlags(directory: false) | exclusive | 1, 0x180);
                file = new SafeFileHandle(descriptor, ownsHandle: true);
                // POSIX openat carries its creation mode as a variadic argument. Set the mode through the returned descriptor
                // before writing any data, including on platforms whose native variadic ABI differs from a fixed signature.
                if (!file.IsInvalid && fchmod(file, 0x180) != 0)
                {
                    IOException error = NativeFailure("the staging file's private permissions could not be set.");
                    file.Dispose();
                    _ = unlinkat(Descriptor, leaf, 0);
                    throw error;
                }
            }
            if (!file.IsInvalid) return file;
            file.Dispose();
            throw NativeFailure("the staging file could not be created exclusively.");
        }

        internal void Move(string oldLeaf, OpenedDirectory target, string newLeaf, bool overwrite)
        {
            // Move replaces directory entries, never follows a target file. Refuse preexisting linked or special entries.
            ValidateRegular(oldLeaf);
            if (overwrite && target.HasEntry(newLeaf)) target.ValidateRegular(newLeaf);
            bool moved;
            if (OperatingSystem.IsWindows())
            {
                moved = MoveFileExW(WorkspaceScopeAudit.ExtendedWindowsPath(Path.Combine(WindowsPath, oldLeaf)),
                    WorkspaceScopeAudit.ExtendedWindowsPath(Path.Combine(target.WindowsPath, newLeaf)), overwrite ? 1u : 0u);
            }
            else if (overwrite)
            {
                moved = renameat(Descriptor, oldLeaf, target.Descriptor, newLeaf) == 0;
            }
            else if (OperatingSystem.IsMacOS())
            {
                moved = renameatx_np(Descriptor, oldLeaf, target.Descriptor, newLeaf, 4) == 0; // RENAME_EXCL
            }
            else
            {
                moved = renameat2(Descriptor, oldLeaf, target.Descriptor, newLeaf, 1) == 0; // RENAME_NOREPLACE
            }
            if (!moved) throw NativeFailure("the staging file could not be published atomically.");
        }

        internal void Remove(string leaf)
        {
            if (!HasEntry(leaf)) return;
            ValidateRegular(leaf);
            bool removed = OperatingSystem.IsWindows()
                ? DeleteFileW(WorkspaceScopeAudit.ExtendedWindowsPath(Path.Combine(WindowsPath, leaf)))
                : unlinkat(Descriptor, leaf, 0) == 0;
            if (!removed && Marshal.GetLastPInvokeError() != 2) throw NativeFailure("the staging file could not be removed.");
        }

        private void ValidateRegular(string leaf)
        {
            using SafeFileHandle file = OpenFile(leaf, Classify(leaf), out _);
        }
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenCreateAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int fchmod(SafeFileHandle file, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int mkdirat(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int renameat(int source, [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        int destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string to);
    [DllImport("libc", SetLastError = true)] private static extern int renameat2(int source, [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        int destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string to, uint flags);
    [DllImport("libc", SetLastError = true)] private static extern int renameatx_np(int source, [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        int destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string to, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, nint security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string source, string destination, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string path);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
