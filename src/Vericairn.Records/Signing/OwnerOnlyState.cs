// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;

namespace Vericairn.Records.Signing;

/// <summary>
/// Per-user state readable by its owner alone on Linux and macOS, whichever process creates it: directories 0700 and files 0600 from the
/// moment they exist, and narrowed when an earlier build, another tool or a copy left them wider. Windows keeps the per-user permissions
/// of the profile the state lives in, and nothing changes there.
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

    // Changed only when it differs, so an owner-only file keeps its change time; one gone meanwhile (SQLite removes its log) is skipped.
    // One that must be owner-only and cannot be made so, because another user owns it or its filesystem keeps no Unix modes, is refused.
    [UnsupportedOSPlatform("windows")]
    private static void Narrow(string path, UnixFileMode mode, bool strict)
    {
        try
        {
            if (File.GetUnixFileMode(path) != mode)
            {
                File.SetUnixFileMode(path, mode);
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException exception) when (strict)
        {
            throw new IOException($"{path} holds this user's Vericairn state and must be readable by its owner alone, and its permissions could not be changed: {exception.Message} Keep the Vericairn home on a local filesystem with Unix permissions, owned by this user, or set VERICAIRN_HOME to such a directory.", exception);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool Within(string home, string directory)
    {
        string relative = Path.GetRelativePath(home, directory);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
