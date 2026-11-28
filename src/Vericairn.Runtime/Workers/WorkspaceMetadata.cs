// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Runtime.Workers;

/// <summary>Exports a brief without writing through repository-controlled links, including directory and dangling links.</summary>
internal static class WorkspaceMetadata
{
    public static string Write(string workspace, string brief)
    {
        string own = Path.Combine(workspace, ".vericairn");
        RefuseLinks(workspace, recurse: false);
        if (OperatingSystem.IsWindows())
        {
            // Denying delete sharing holds each directory in place until the files have been replaced. Open the reparse point itself.
            using SafeFileHandle root = DirectoryHandles.OpenWindows(workspace);
            Directory.CreateDirectory(own);
            using SafeFileHandle metadata = DirectoryHandles.OpenWindows(own);
            RefuseLinks(own, recurse: true);
            ReplaceWindows(".gitignore", "*\n");
            ReplaceWindows("brief.md", brief);
        }
        else
        {
            int noFollow = OperatingSystem.IsMacOS() ? 0x100 : 0x20000;
            int closeOnExec = OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;
            using SafeFileHandle root = DirectoryHandles.OpenUnix(workspace);
            if (mkdirat(root, ".vericairn", 0x1c0) != 0 && Marshal.GetLastPInvokeError() != 17) // 0700, EEXIST
            {
                throw Failure();
            }

            using SafeFileHandle metadata = DirectoryHandles.OpenUnix(".vericairn", root);
            RefuseLinks(own, recurse: true);
            ReplaceUnix(".gitignore", "*\n");
            ReplaceUnix("brief.md", brief);

            void ReplaceUnix(string name, string content)
            {
                int parent = (int)metadata.DangerousGetHandle();
                int nonblock = OperatingSystem.IsMacOS() ? 4 : 0x800;
                int existing = DirectoryHandles.OpenAt(parent, name, noFollow | nonblock | closeOnExec, 0);
                if (existing >= 0)
                {
                    using var prior = new SafeFileHandle(existing, ownsHandle: true);
                }
                else if (Marshal.GetLastPInvokeError() != 2)
                {
                    throw Failure();
                }

                string temporary = ".worker-" + Guid.NewGuid().ToString("N");
                int create = OperatingSystem.IsMacOS() ? 0x200 | 0x800 : 0x40 | 0x80;
                try
                {
                    using (SafeFileHandle file = OpenAt(parent, temporary, 1 | create | noFollow | closeOnExec))
                    using (var stream = new FileStream(file, FileAccess.Write))
                    {
                        stream.Write(Encoding.UTF8.GetBytes(content));
                    }

                    if (renameat(metadata, temporary, metadata, name) != 0)
                    {
                        throw Failure();
                    }
                }
                finally
                {
                    _ = DirectoryHandles.UnlinkAt(metadata, temporary, 0);
                }
            }
        }

        return Path.Combine(own, "brief.md");

        void ReplaceWindows(string name, string content)
        {
            string target = Path.Combine(own, name);
            RefuseLinks(target, recurse: false);
            string temporary = Path.Combine(own, ".worker-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(Encoding.UTF8.GetBytes(content));
                }

                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }

    private static void RefuseLinks(string path, bool recurse)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The workspace metadata contains a symbolic link or reparse point; no brief is written through it.");
        }

        if (recurse && (attributes & FileAttributes.Directory) != 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                RefuseLinks(entry, recurse: true);
            }
        }
    }

    private static SafeFileHandle OpenAt(int parent, string name, int flags)
    {
        // openat with an absolute name ignores parent, so no platform-specific AT_FDCWD constant is needed for the root.
        int descriptor = DirectoryHandles.OpenAt(parent, name, flags, 0x180); // 0600 for a newly created file.
        return descriptor >= 0 ? new SafeFileHandle(descriptor, ownsHandle: true) : throw Failure();
    }

    private static IOException Failure() => new("Workspace metadata could not be opened without following links.", new Win32Exception(Marshal.GetLastPInvokeError()));

#pragma warning disable SYSLIB1054 // Runtime marshalling keeps the project free of unsafe code.
#pragma warning disable CA2101 // UTF-8 on Unix, explicit Unicode entry points on Windows.
    [DllImport("libc", SetLastError = true)] private static extern int mkdirat(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int renameat(SafeFileHandle from, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, SafeFileHandle to, [MarshalAs(UnmanagedType.LPUTF8Str)] string target);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
