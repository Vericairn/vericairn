// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vericairn.Records.Signing;

/// <summary>
/// Per-user state readable by its owner alone on Linux and macOS, whichever process creates it: directories 0700 and files 0600 from the
/// moment they exist, and narrowed when an earlier build, another tool or a copy left them wider. State that belongs to another user is
/// refused. State of this user's own on a filesystem that keeps no Unix modes (FAT, exFAT, NTFS through FUSE, some network mounts) is
/// used as it is, since permissions mean nothing there, and reported once. Windows keeps the per-user permissions of the profile the
/// state lives in, and nothing changes there.
/// </summary>
internal static class OwnerOnlyState
{
    internal const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // A SQLite database's own files: the database, its write-ahead log, its shared-memory index and a rollback journal if one remains.
    private static readonly string[] DatabaseFiles = ["", "-wal", "-shm", "-journal"];

    /// <summary>
    /// Creates a directory, or keeps the one there, owner-only, and returns its full path. When it lies under <paramref name="home"/>, the
    /// home and every directory from it down to this one are created or narrowed too, so nothing under the home is reachable by another
    /// user. Directories above the home are not this user's state and are created as the system would, never narrowed; a directory
    /// between the home and this one that belongs to someone else is left as it is.
    /// </summary>
    internal static string CreateDirectory(string directory, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return directory;
        }

        string top = home is not null && Within(Path.GetFullPath(home), directory) ? Path.GetFullPath(home) : directory;
        if (Path.GetDirectoryName(top) is string above)
        {
            Directory.CreateDirectory(above);
        }

        // The mode given to CreateDirectory applies to the last directory it creates alone, so each one is created in turn.
        string current = top;
        string relative = Path.GetRelativePath(top, directory);
        string[] steps = relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = -1; i < steps.Length; i++)
        {
            current = i < 0 ? top : Path.Combine(current, steps[i]);
            Directory.CreateDirectory(current, DirectoryMode);
            // A directory above the one asked for that is not this user's to change is left; the directory below it is still made owner-only.
            Narrow(current, DirectoryMode, strict: i == steps.Length - 1);
        }

        return directory;
    }

    /// <summary>
    /// Creates a SQLite database file, empty and owner-only, before SQLite opens it, and narrows the one there with its -wal, -shm and
    /// -journal files: SQLite gives the files it creates beside a database the database file's own mode, so they are owner-only too.
    /// </summary>
    internal static void PrepareDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!File.Exists(path))
        {
            try
            {
                using var stream = new FileStream(path, new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = FileMode });
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                // Created by another opening at the same moment, and narrowed below; or not a file at all, which SQLite reports.
            }
        }

        foreach (string suffix in DatabaseFiles)
        {
            Narrow(path + suffix, FileMode, strict: true);
        }
    }

    /// <summary>Writes a file owner-only from its first byte, replacing a leftover of the same name rather than reusing its mode.</summary>
    internal static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(path);
        var options = new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileMode;
        }

        using var stream = new FileStream(path, options);
        stream.Write(bytes);
    }

    /// <summary>
    /// Told, once per process, of the first path found whose filesystem cannot restrict its permissions to its owner, and of no path
    /// under one already told; the command line writes it to its standard error, which a detached daemon writes to its log.
    /// </summary>
    internal static Action<string>? Report { get; set; }

    /// <summary>The paths of this user's state found in this process on a filesystem that cannot restrict their permissions.</summary>
    internal static IReadOnlyCollection<string> Unrestricted => [.. unrestricted.Keys];

    /// <summary>What is said of a path whose filesystem cannot restrict its permissions to its owner.</summary>
    internal static string Warning(string path) =>
        $"{path} is on a filesystem that cannot restrict its permissions to its owner, so other users of this machine may be able to read the Vericairn state kept there.";

    /// <summary>
    /// What is wrong with an existing directory or file of this user's state, with the path to name, or null when it is owner-only or
    /// absent: a file is named by its directory, as a command reports it. It is narrowed on the way, as any writable opening narrows
    /// it. Only <c>doctor</c> asks, and nothing is reported or refused.
    /// </summary>
    internal static (string Subject, string Problem)? Restriction(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows()) return null;
        path = Path.GetFullPath(path);
        bool directory = Directory.Exists(path);
        string subject = directory ? path : Path.GetDirectoryName(path)!;
        try
        {
            return Restrict(path, directory ? DirectoryMode : FileMode) switch
            {
                Outcome.OtherOwner => (path, $"{path} belongs to another user of this machine; the Vericairn state kept there is refused."),
                Outcome.NoModes => (subject, Warning(subject)),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (path, $"{path} could not be checked: {exception.Message}");
        }
    }

    // Tests stand in for a filesystem that keeps no Unix modes; null outside tests.
    internal static Action<string, UnixFileMode>? ModeSetter { get; set; }

    private static readonly ConcurrentDictionary<string, byte> unrestricted = new(StringComparer.Ordinal);

    private enum Outcome { Absent, OwnerOnly, OtherOwner, NoModes, Unchangeable }

    // State that must be owner-only: refused when another user owns it, used and reported once when its filesystem keeps no modes.
    // Above it, a directory that is not this user's to change is left as it is. One gone meanwhile (SQLite removes its log) is skipped.
    [UnsupportedOSPlatform("windows")]
    private static void Narrow(string path, UnixFileMode mode, bool strict)
    {
        switch (Restrict(path, mode))
        {
            case Outcome.OtherOwner when strict:
                throw new IOException($"{path} belongs to another user of this machine, who can read and change the Vericairn state kept there, so it is not used. Keep the Vericairn home in a directory you own, or set VERICAIRN_HOME to one.");
            case Outcome.Unchangeable when strict:
                throw new IOException($"{path} holds this user's Vericairn state and must be readable by its owner alone, and its permissions could not be changed. Keep the Vericairn home in a directory you own on a local filesystem, or set VERICAIRN_HOME to one.");
            case Outcome.NoModes:
                // A file is told of by its directory, so a store and its siblings on one filesystem are said once.
                Record(mode == DirectoryMode ? path : Path.GetDirectoryName(path)!);
                break;
        }
    }

    // Changed only when it differs, so an owner-only file keeps its change time. The owner is read first: a path of another user's is
    // never changed. A change refused, or one that does not hold, on this user's own path is the filesystem's: it keeps no Unix modes.
    [UnsupportedOSPlatform("windows")]
    private static Outcome Restrict(string path, UnixFileMode mode)
    {
        try
        {
            uint? owner = OwnerOf(path), user = CurrentUser();
            if (owner is uint found && user is uint self && found != self) return Outcome.OtherOwner;
            if (File.GetUnixFileMode(path) == mode) return Outcome.OwnerOnly;
            try
            {
                (ModeSetter ?? File.SetUnixFileMode)(path, mode);
                if (File.GetUnixFileMode(path) == mode) return Outcome.OwnerOnly;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException and not FileNotFoundException and not DirectoryNotFoundException)
            {
                // Refused; whether by the filesystem or for want of ownership only a known owner says, below.
            }

            return owner is not null && user is not null ? Outcome.NoModes : Outcome.Unchangeable;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Outcome.Absent;
        }
    }

    private static void Record(string path)
    {
        // Under a path already told of, the filesystem is the same one: nothing new to say.
        bool covered = unrestricted.Keys.Any(known => path == known || path.StartsWith(known + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        if (unrestricted.TryAdd(path, 0) && !covered) Report?.Invoke(path);
    }

    // The owner's user id, or null when it cannot be read. The runtime's own native library reads it, as .NET reads a file's status;
    // its layout starts with flags, mode, owner and group, and the mode it gives is checked against .NET's before the owner is trusted.
    [UnsupportedOSPlatform("windows")]
    private static uint? OwnerOf(string path)
    {
        try
        {
            var status = new byte[256];
            if (Stat(path, status) != 0)
            {
                return File.Exists(path) || Directory.Exists(path) ? null : throw new FileNotFoundException(path);
            }

            int mode = BinaryPrimitives.ReadInt32LittleEndian(status.AsSpan(4));
            return (mode & 0xFFF) == (int)File.GetUnixFileMode(path) ? BinaryPrimitives.ReadUInt32LittleEndian(status.AsSpan(8)) : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static uint? CurrentUser()
    {
        try
        {
            return GetEUid();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project.
#pragma warning disable CA2101 // The path is marshalled explicitly as UTF-8.
    [DllImport("libSystem.Native", EntryPoint = "SystemNative_Stat", SetLastError = true)]
    private static extern int Stat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] status);

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetEUid")]
    private static extern uint GetEUid();
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    private static bool Within(string home, string directory)
    {
        string relative = Path.GetRelativePath(home, directory);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
