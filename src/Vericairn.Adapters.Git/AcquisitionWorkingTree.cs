// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>Copies only ordinary files into a private directory for Git to inspect.</summary>
internal static partial class AcquisitionWorkingTree
{
    internal const long MaximumWorkingFileBytes = 64L * 1024 * 1024;
    private const long MaximumWorkingBytes = 512L * 1024 * 1024;
    // Untracked content is copied only within a budget of its own, so build outputs never use up the budget tracked files need;
    // past it an untracked file keeps only its name.
    private const long MaximumUntrackedBytes = 64L * 1024 * 1024;
    private const long MaximumObjectBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 100_000;
    private const int MaximumDepth = 128;
    private const int DirectoryMode = 0x4000;
    private const int RegularMode = 0x8000;
    private const int FileTypeMask = 0xf000;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>Takes a bounded snapshot without giving Git a path to the original tree.</summary>
    internal static void Copy(string root, string destination, Func<string, bool>? include = null, bool workingTree = true)
        => CopyFrom(root, root, destination, include, workingTree);

    /// <summary>
    /// Copies a directory reached through the selected acquisition boundary. A file <paramref name="withholdable"/> names (an
    /// untracked one, whose name is all Git's status reads) that is not a single-link regular file, or would exceed a limit, is
    /// written as an empty placeholder of the same name and added to <paramref name="withheld"/> instead of refusing the copy.
    /// </summary>
    internal static void CopyFrom(string root, string source, string destination, Func<string, bool>? include = null, bool workingTree = false,
        Func<string, bool>? withholdable = null, ISet<string>? withheld = null)
    {
        var watch = Stopwatch.StartNew();
        int entries = 0;
        long totalBytes = 0;
        long untrackedBytes = 0;
        long maximumBytes = workingTree ? MaximumWorkingBytes : MaximumObjectBytes;
        long maximumFileBytes = workingTree ? MaximumWorkingFileBytes : MaximumObjectBytes;
        byte[] buffer = new byte[64 * 1024];
        string current = ".";
        string kind = workingTree ? "working tree" : "Git metadata";
        try
        {
            string[] names = RelativeNames(root, source, allowRoot: true);
            using OpenedDirectory start = OpenRoot(root);
            Directory.CreateDirectory(destination);
            Enter(start, names, 0);
            CheckTime();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            string detail = error is UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException
                ? "the entry is unavailable." : error.Message;
            if (!workingTree && error is HardLinkedFileException)
                detail += " Its files are shared through hard links with another local clone, made from it or it from another: remove that clone, or replace this one with an independent clone made with git clone --no-hardlinks, and retry.";
            throw new ProviderRefusedException($"Could not read the {kind} at {Display(current)}: {detail}", error);
        }

        void Enter(OpenedDirectory directory, string[] names, int index)
        {
            if (index == names.Length)
            {
                Visit(directory, destination, string.Empty, 0);
                return;
            }

            current = string.Join('/', names.AsSpan(0, index + 1).ToArray());
            CheckTime();
            using OpenedDirectory child = directory.OpenDirectory(names[index]);
            Enter(child, names, index + 1);
        }

        void Visit(OpenedDirectory directory, string target, string prefix, int depth)
        {
            CheckTime();
            if (depth >= MaximumDepth) throw new IOException("the directory depth exceeds the acquisition limit.");
            if (workingTree && depth > 0)
            {
                current = prefix + ".git";
                // Detect a populated submodule or nested repository before copying any of its children.
                if (directory.HasEntry(".git")) throw new IOException("nested Git administration or a submodule is outside this working-tree read.");
            }
            foreach (string leaf in directory.Names())
            {
                current = prefix + leaf;
                CheckTime();
                if (++entries > MaximumEntries) throw new IOException("the entry count exceeds the acquisition limit.");
                if (workingTree && string.Equals(leaf, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    if (depth != 0) throw new IOException("nested Git administration or a submodule is outside this working-tree read.");
                    continue;
                }
                if (!SafeName(leaf)) throw new IOException("the file name cannot be resolved safely.");
                if (include?.Invoke(current) == false) continue;

                string next = Path.Combine(target, leaf);
                bool mayWithhold = withholdable?.Invoke(current) == true;
                Entry info;
                try
                {
                    info = directory.Classify(leaf);
                }
                catch (IOException) when (mayWithhold)
                {
                    // A link or reparse point is never followed; an untracked one keeps only its name.
                    Withhold(next);
                    continue;
                }

                if (info.Type == DirectoryMode)
                {
                    using var child = directory.OpenDirectory(leaf);
                    Directory.CreateDirectory(next);
                    Visit(child, next, current + "/", depth + 1);
                    continue;
                }

                if (mayWithhold && (info.Type != RegularMode || info.Links != 1))
                {
                    // Never opened: a special file, link or hard-linked file that is untracked keeps only its name.
                    Withhold(next);
                    continue;
                }

                SafeFileHandle file;
                int mode;
                try
                {
                    file = directory.OpenFile(leaf, info, out mode);
                }
                catch (IOException) when (mayWithhold)
                {
                    // Windows reports a file's link count only once it is open: an untracked hard-linked file keeps only its name.
                    Withhold(next);
                    continue;
                }

                using (file)
                {
                using var source = new FileStream(file, FileAccess.Read, bufferSize: 1);
                long budget = mayWithhold ? MaximumUntrackedBytes - untrackedBytes : maximumBytes - totalBytes;
                if (source.Length > maximumFileBytes || source.Length > budget)
                {
                    if (!mayWithhold) throw new IOException("the file size exceeds the acquisition limit.");
                    Withhold(next);
                    continue;
                }

                long copied = 0;
                bool grew = false;
                using (var output = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    while (true)
                    {
                        CheckTime();
                        int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, Math.Min(maximumFileBytes - copied, budget - copied) + 1));
                        if (read == 0) break;
                        copied += read;
                        if (copied > maximumFileBytes || copied > budget)
                        {
                            if (!mayWithhold) throw new IOException("the file grew beyond the acquisition limit.");
                            grew = true;
                            break;
                        }

                        output.Write(buffer, 0, read);
                    }
                }

                if (grew)
                {
                    File.Delete(next);
                    Withhold(next);
                    continue;
                }

                if (mayWithhold) untrackedBytes += copied;
                else totalBytes += copied;
                }
                CheckTime();
                if (!OperatingSystem.IsWindows() && (mode & 0x40) != 0)
                    File.SetUnixFileMode(next, File.GetUnixFileMode(next) | UnixFileMode.UserExecute);
            }
        }

        void CheckTime()
        {
            if (watch.Elapsed >= Deadline) throw new IOException("the acquisition deadline expired.");
        }

        void Withhold(string next)
        {
            using (new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            withheld?.Add(current);
        }
    }

    /// <summary>Opens a single-link regular file through directories anchored to the specified root.</summary>
    internal static SafeFileHandle OpenRegular(string root, string path)
        => OpenRegularCore(root, path, missingOk: false)!;

    /// <summary>Returns null only for a missing entry. A present link or unreadable entry is refused.</summary>
    internal static SafeFileHandle? TryOpenRegular(string root, string path)
        => OpenRegularCore(root, path, missingOk: true);

    private static SafeFileHandle? OpenRegularCore(string root, string path, bool missingOk)
    {
        string[] names = RelativeNames(root, path, allowRoot: false);
        using OpenedDirectory directory = OpenRoot(root);
        try { return Open(directory, 0); }
        catch (Exception error) when (missingOk && error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The Git entry {Display(string.Join('/', names))} could not be opened safely.", error);
        }

        SafeFileHandle Open(OpenedDirectory parent, int index)
        {
            string name = names[index];
            Entry info = parent.Classify(name);
            if (index == names.Length - 1) return parent.OpenFile(name, info, out _);
            if (info.Type != DirectoryMode) throw new IOException("the parent is not an ordinary directory.");
            using OpenedDirectory child = parent.OpenDirectory(name);
            return Open(child, index + 1);
        }
    }

    private static string[] RelativeNames(string root, string path, bool allowRoot)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path, root));
        if (allowRoot && relative == ".") return [];
        string[] names = relative.Split(Path.DirectorySeparatorChar);
        return !Path.IsPathRooted(relative) && names.Length < MaximumDepth && names.All(SafeName)
            ? names : throw new IOException($"The Git entry {Display(relative)} is outside the acquisition boundary or has an unsafe name.");
    }

    private static bool SafeName(string name) => name.Length > 0 && name is not ("." or "..")
        && name.IndexOfAny(['/', '\\', ':', '\0', '\ufffd']) < 0
        && (!OperatingSystem.IsWindows() || name[^1] is not ('.' or ' '));

    // Validate the index spelling before any Git command compares paths against a physical working tree.
    internal static bool IsIndexPath(string path) => !Path.IsPathRooted(path) && path.Split('/').All(SafeIndexComponent);

    private static bool SafeIndexComponent(string name)
    {
        if (!SafeName(name)) return false;
        // HFS can treat invisible Unicode format controls as absent in a path component.
        string significant = OperatingSystem.IsMacOS() ? string.Concat(name.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)) : name;
        if (significant is "" or "." or ".." || significant.Equals(".git", StringComparison.OrdinalIgnoreCase)) return false;
        if (!OperatingSystem.IsWindows()) return true;
        string device = significant.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return device is not ("GIT~1" or "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(device.Length == 4 && device[..3] is "COM" or "LPT" && "123456789¹²³".Contains(device[3]));
    }

    private static string Display(string path) => System.Text.Json.JsonSerializer.Serialize(path);

    private static OpenedDirectory OpenRoot(string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string filesystem = Path.GetPathRoot(full)!;
        bool windows = OperatingSystem.IsWindows();
        var current = new OpenedDirectory(filesystem, windows ? OpenWindows(filesystem, directory: true) : OpenUnix(-1, filesystem, directory: true));
        try
        {
            // The caller resolved the selected boundary. Pin its full ancestry so a newly introduced link above it is refused too.
            foreach (string component in full[filesystem.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                OpenedDirectory child = current.OpenDirectory(component, retainParent: windows);
                if (!windows) current.Dispose();
                current = child;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private sealed partial class OpenedDirectory(string path, SafeFileHandle handle, OpenedDirectory? parent = null) : IDisposable
    {
        private int Descriptor => handle.DangerousGetHandle().ToInt32();

        private string WindowsPath => path;

        internal bool HasEntry(string leaf)
        {
            try { _ = Classify(leaf); return true; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
        }

        internal Entry Classify(string leaf)
        {
            if (OperatingSystem.IsWindows())
            {
                FileAttributes attributes = File.GetAttributes(Path.Combine(path, leaf));
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("symbolic links and reparse points are refused.");
                return new Entry((attributes & FileAttributes.Directory) != 0 ? DirectoryMode : RegularMode, 1);
            }

            byte[] stat = new byte[256];
            int flags = OperatingSystem.IsMacOS() ? 0x20 : 0x100; // AT_SYMLINK_NOFOLLOW
            int result = MacIntel ? FstatAtMac(Descriptor, leaf, stat, flags) : fstatat(Descriptor, leaf, stat, flags);
            if (result != 0 && Marshal.GetLastPInvokeError() == 2) throw new FileNotFoundException("the entry is absent.");
            return result == 0 ? Stat(stat) : throw NativeFailure("the file could not be classified without following links.");
        }

        internal OpenedDirectory OpenDirectory(string leaf, bool retainParent = false)
        {
            string child = Path.Combine(path, leaf);
            return new OpenedDirectory(child, OperatingSystem.IsWindows() ? OpenWindows(child, directory: true) : OpenUnix(Descriptor, leaf, directory: true),
                retainParent ? this : null);
        }

        internal SafeFileHandle OpenFile(string leaf, Entry before, out int mode)
        {
            RequireRegular(before);
            SafeFileHandle file = OperatingSystem.IsWindows() ? OpenWindows(Path.Combine(path, leaf), directory: false) : OpenUnix(Descriptor, leaf, directory: false);
            try
            {
                Entry current;
                if (OperatingSystem.IsWindows())
                {
                    if (!GetFileInformationByHandle(file, out WindowsFileInfo info)) throw NativeFailure("the opened file could not be classified.");
                    current = new Entry(RegularMode, info.Links);
                }
                else
                {
                    byte[] stat = new byte[256];
                    int result = MacIntel ? FstatMac(file, stat) : fstat(file, stat);
                    if (result != 0) throw NativeFailure("the opened file could not be classified.");
                    current = Stat(stat);
                }
                RequireRegular(current);
                mode = current.Mode;
                return file;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        internal IEnumerable<string> Names()
        {
            if (OperatingSystem.IsWindows())
            {
                // Ancestor directory handles stay open without delete sharing while their children are opened.
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                    yield return Path.GetFileName(entry);
                yield break;
            }

            // Open "." relative to the pinned directory for enumeration. fdopendir takes ownership of this second descriptor.
            int descriptor = openat(Descriptor, ".", UnixOpenFlags(directory: true));
            if (descriptor < 0) throw NativeFailure("the directory could not be opened for enumeration.");
            nint directory = MacIntel ? FdOpenDirMac(descriptor) : fdopendir(descriptor);
            if (directory == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                _ = close(descriptor);
                throw new IOException("the directory could not be enumerated.", new System.ComponentModel.Win32Exception(error));
            }
            try
            {
                while (true)
                {
                    nint entry = MacIntel ? ReadDirMac(directory) : readdir(directory);
                    if (entry == 0)
                    {
                        if (Marshal.GetLastPInvokeError() != 0) throw NativeFailure("the directory could not be completely enumerated.");
                        break;
                    }
                    // Darwin's 64-bit dirent adds a 16-bit d_namlen before d_type. Both platforms NUL-terminate d_name.
                    string leaf = Marshal.PtrToStringUTF8(entry + (OperatingSystem.IsMacOS() ? 21 : 19))!;
                    if (leaf is not ("." or "..")) yield return leaf;
                }
            }
            finally
            {
                _ = closedir(directory);
            }
        }

        public void Dispose()
        {
            handle.Dispose();
            parent?.Dispose();
        }
    }

    private static bool MacIntel => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    private readonly record struct Entry(int Mode, ulong Links)
    {
        internal int Type => Mode & FileTypeMask;
    }

    private static void RequireRegular(Entry entry)
    {
        if (entry.Type != RegularMode) throw new IOException("the entry is a symbolic link or another kind of non-regular file.");
        if (entry.Links != 1) throw new HardLinkedFileException();
    }

    private sealed class HardLinkedFileException() : IOException("hard links are refused.");

    // Native stat layout on supported 64-bit platforms. Only mode and link count are used.
    private static Entry Stat(byte[] stat)
    {
        if (OperatingSystem.IsMacOS()) return new Entry(BitConverter.ToUInt16(stat, 4), BitConverter.ToUInt16(stat, 6));
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => new Entry(BitConverter.ToUInt16(stat, 16), BitConverter.ToUInt32(stat, 20)),
            Architecture.X64 => new Entry(BitConverter.ToUInt16(stat, 24), BitConverter.ToUInt64(stat, 16)),
            _ => throw new IOException("this architecture cannot safely classify repository files."),
        };
    }

    private static SafeFileHandle OpenUnix(int parent, string name, bool directory)
    {
        int descriptor = openat(parent, name, UnixOpenFlags(directory));
        return descriptor >= 0 ? new SafeFileHandle(descriptor, ownsHandle: true)
            : throw NativeFailure(directory ? "the directory could not be opened without following links." : "the file could not be opened without following links.");
    }

    private static int UnixOpenFlags(bool directory)
    {
        if (OperatingSystem.IsMacOS()) return 0x100 | 0x01000000 | (directory ? 0x100000 : 0x4 | 0x20000);
        if (!OperatingSystem.IsLinux()) throw new IOException("this platform cannot safely classify repository files.");
        bool arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        return (arm64 ? 0x8000 : 0x20000) | 0x80000 | (directory ? (arm64 ? 0x4000 : 0x10000) : 0x800 | 0x100);
        // O_NOFOLLOW | O_CLOEXEC | either O_DIRECTORY or O_NONBLOCK | O_NOCTTY.
    }

    private static SafeFileHandle OpenWindows(string path, bool directory)
    {
        const uint openReparsePoint = 0x00200000, backupSemantics = 0x02000000;
        SafeFileHandle handle = CreateFileW(WorkspaceScopeAudit.ExtendedWindowsPath(path),
            directory ? 0x81u : 0x80000000u, directory ? 3u : 1u, 0, 3, openReparsePoint | (directory ? backupSemantics : 0u), 0);
        if (handle.IsInvalid || GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out WindowsFileInfo info)
            || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != (directory ? FileAttributes.Directory : 0))
        {
            handle.Dispose();
            throw NativeFailure(directory ? "the directory could not be opened without following links." : "the file could not be opened without following links.");
        }
        return handle;
    }

    private static IOException NativeFailure(string detail) =>
        new(detail, new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInfo
    {
        public FileAttributes Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

#pragma warning disable SYSLIB1054 // The project does not use unsafe code.
#pragma warning disable CA2101 // Explicit UTF-8 Unix paths and Unicode Windows paths.
    [DllImport("libc", SetLastError = true)] private static extern int openat(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int close(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern int fstat(SafeFileHandle file, [Out] byte[] stat);
    [DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)] private static extern int FstatMac(SafeFileHandle file, [Out] byte[] stat);
    [DllImport("libc", SetLastError = true)] private static extern int fstatat(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] stat, int flags);
    [DllImport("libc", EntryPoint = "fstatat$INODE64", SetLastError = true)] private static extern int FstatAtMac(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] stat, int flags);
    [DllImport("libc", SetLastError = true)] private static extern nint fdopendir(int descriptor);
    [DllImport("libc", EntryPoint = "fdopendir$INODE64", SetLastError = true)] private static extern nint FdOpenDirMac(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern nint readdir(nint directory);
    [DllImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)] private static extern nint ReadDirMac(nint directory);
    [DllImport("libc")] private static extern int closedir(nint directory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInfo info);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
