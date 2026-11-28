// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FilePath = System.IO.Path;

namespace Vericairn.Records.Signing;

/// <summary>Where this user's Vericairn state lives on this machine: keys, caches, device identity. Never inside an estate repository.</summary>
public static class VericairnHome
{
    /// <summary>Environment variable that overrides the location (tests, continuous integration, unusual machines).</summary>
    public const string EnvironmentVariable = "VERICAIRN_HOME";

    /// <summary>
    /// The home of the scope the calling code runs in, for a process that runs several isolated scopes side by side: the runtime's
    /// tests give each test class its own home this way, since the environment variable is one per process. Null or empty outside a
    /// scope, and always so in the product, which never sets it.
    /// </summary>
    internal static Func<string?>? Scope { get; set; }

    /// <summary>The scope's own home when the calling code runs in one, which a process it starts is given; null otherwise.</summary>
    internal static string? Scoped() => Scope?.Invoke() is { Length: > 0 } scoped ? System.IO.Path.GetFullPath(scoped) : null;

    /// <summary>The per-user state directory for this machine.</summary>
    public static string Path()
    {
        if (Scoped() is string scoped)
        {
            return scoped;
        }

        string? overridden = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return System.IO.Path.GetFullPath(overridden);
        }

        return DefaultPath();
    }

    internal static bool SamePath(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        if (!FilePath.IsPathFullyQualified(left) || !FilePath.IsPathFullyQualified(right)) return false;
        return string.Equals(CanonicalIdentity(left), CanonicalIdentity(right), StringComparison.Ordinal);
    }

    /// <summary>The spelling used to identify a home, including a home whose directory has yet to be created.</summary>
    internal static string CanonicalIdentity(string home)
    {
        string path = FilePath.TrimEndingDirectorySeparator(FilePath.GetFullPath(home));
        var pending = new Stack<string>();
        while (true)
        {
            if (Directory.Exists(path) && ExistingDirectoryPath(path) is string canonical)
            {
                path = canonical;
                break;
            }

            if (FilePath.GetDirectoryName(path) is not string parent) break;
            pending.Push(FilePath.GetFileName(path));
            path = parent;
        }

        // Resolve the existing ancestor without creating a directory for a service plan.
        foreach (string name in pending)
            path = FilePath.Combine(path, name);
        return path;
    }

    private static string? ExistingDirectoryPath(string path)
    {
        if (OperatingSystem.IsWindows()) return WindowsDirectoryPath(path);

        nint pointer = RealPath(path, 0);
        if (pointer == 0) return null;
        try
        {
            string resolved = Marshal.PtrToStringUTF8(pointer)!;
            return OperatingSystem.IsLinux() ? LinuxDirectoryPath(resolved) : resolved;
        }
        finally
        {
            Free(pointer);
        }
    }

    private static string LinuxDirectoryPath(string path)
    {
        if (FilePath.GetDirectoryName(path) is not string parent) return path;

        // Linux realpath resolves links but can preserve a lookup spelling on casefolded filesystems.
        // Use the directory entries' names and let the filesystem decide which spellings mean the same inode.
        parent = LinuxDirectoryPath(parent);
        path = FilePath.Combine(parent, FilePath.GetFileName(path));
        try
        {
            IEnumerable<string> entries = Directory.EnumerateDirectories(parent);
            if (entries.Contains(path, StringComparer.Ordinal) || LStat(path, out UnixFileStatus current) != 0)
                return path;

            return entries.FirstOrDefault(entry => LStat(entry, out UnixFileStatus candidate) == 0
                && candidate.Device == current.Device && candidate.Inode == current.Inode) ?? path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string? WindowsDirectoryPath(string path)
    {
        const uint shareReadWriteDelete = 7;
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        const uint volumeNameGuid = 1;
        using SafeFileHandle directory = CreateFileW(path, 0, shareReadWriteDelete, 0, openExisting, backupSemantics, 0);
        if (directory.IsInvalid) return null;

        var buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(directory, buffer, (uint)buffer.Length, volumeNameGuid);
        // A network share has no volume GUID. Ask for the normalized UNC path.
        if (length == 0)
            length = GetFinalPathNameByHandleW(directory, buffer, (uint)buffer.Length, 0);
        return length == 0 || length >= buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    // The fixed FileStatus layout returned by the .NET Unix shim, also used for status in OwnerOnlyState.
    [StructLayout(LayoutKind.Explicit, Size = 120)]
    private struct UnixFileStatus
    {
        [FieldOffset(88)] public long Device;
        [FieldOffset(104)] public long Inode;
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project.
#pragma warning disable CA2101 // Unix paths are marshalled explicitly as UTF-8.
    [DllImport("libc", EntryPoint = "realpath")]
    private static extern nint RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(nint pointer);

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_LStat")]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UnixFileStatus status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    /// <summary>The platform's home when no explicit home or scope is set.</summary>
    internal static string DefaultPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vericairn");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return System.IO.Path.Combine(home, "Library", "Application Support", "Vericairn");
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return System.IO.Path.Combine(string.IsNullOrWhiteSpace(xdg) ? System.IO.Path.Combine(home, ".config") : xdg, "vericairn");
    }

    /// <summary>The platform name recorded in device records.</summary>
    public static string Platform() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsIOS() ? "ios" : OperatingSystem.IsAndroid() ? "android" : "other";
}
