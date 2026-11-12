// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Microsoft.Data.Sqlite;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Store;

/// <summary>
/// A live store that SQLite could not use, such as a malformed database: a bounded diagnostic that names the storage profile and
/// its recovery route (FIX-93). Under the repository-synced profile the store is a derived cache that can be rebuilt from the
/// repository's record files; under the local-only profile it is the estate's canonical copy and is never moved or rebuilt.
/// </summary>
internal sealed class LiveStoreException : IOException
{
    private LiveStoreException(string message, string databasePath, string? profile, int sqliteError, Exception inner) : base(message, inner)
    {
        DatabasePath = databasePath;
        Profile = profile;
        SqliteError = sqliteError;
    }

    /// <summary>The live store's database file.</summary>
    internal string DatabasePath { get; }

    /// <summary>The estate's storage profile, when the store was opened for an estate.</summary>
    internal string? Profile { get; }

    /// <summary>SQLite's primary result code.</summary>
    internal int SqliteError { get; }

    internal static LiveStoreException From(string databasePath, string? profile, SqliteException exception)
    {
        int code = exception.SqliteErrorCode & 0xFF;
        string cause = exception.Message.Trim().TrimEnd('.');
        if (cause.Length > 300) cause = cause[..300] + "...";
        string message = code is 5 or 6
            ? $"Another runtime on this machine holds this estate's live store ({cause}). Let that command finish or stop the daemon serving the estate ('vericairn runtime stop'), then try again. Live store: {databasePath}"
            : profile switch
            {
                "repository-synced" => $"This clone's derived cache of a repository-synced estate could not be used ({cause}). The repository's record files are canonical, and this failure did not change them. Rebuild the cache from them with 'vericairn estate cache rebuild' once no daemon or other command has the estate open; it keeps the failed database with its -wal and -shm files for diagnosis. Cache: {databasePath}",
                "local-only" => LiveStoreRecovery.LocalOnly($"This local-only estate's live database could not be used ({cause}).", databasePath),
                _ => $"The live store could not be used ({cause}). Live store: {databasePath}",
            };
        return new LiveStoreException(message, databasePath, profile, code, exception);
    }
}

/// <summary>What a cache rebuild did: the record files it read back and where the failed database and its sidecars were kept.</summary>
internal sealed record LiveStoreRebuilt(string Estate, string Cache, long Records, string? Retained, IReadOnlyList<string> RetainedFiles);

/// <summary>
/// Recovery of a repository-synced estate's derived cache (FIX-93). Every writable session holds the estate's writer lock shared;
/// a rebuild holds it alone, so it refuses while a daemon or another command has the estate open, and no session opens the cache
/// while it is being rebuilt.
/// </summary>
internal static class LiveStoreRecovery
{
    internal const string WriterLockName = "live.lock";
    internal const string RetainedDirectoryName = "failed-cache";

    // A live database's own files: the database, its write-ahead log, its shared-memory index and a rollback journal if one remains.
    private static readonly string[] Sidecars = ["", "-wal", "-shm", "-journal"];

    internal const string WriterPresent = "Another runtime on this machine has this estate open: a daemon or a command in flight. Stop the daemon ('vericairn runtime stop') and let other commands finish, then rebuild. Nothing was moved.";

    internal static string LocalOnly(string lead, string databasePath) =>
        lead + " It is the estate's canonical copy, not a cache, so it is never moved, deleted or rebuilt: keep live.db with its -wal and -shm files together, stop any daemon and command using it, and recover from a backup of that directory or from an earlier 'vericairn estate export' ('vericairn estate restore' restores it as a repository-synced estate). Database: " + databasePath;

    internal static string WriterLockPath(string databasePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, WriterLockName);

    /// <summary>Takes the estate's writer lock shared, as every writable session does for its lifetime; refused while a rebuild holds it.</summary>
    internal static FileStream ShareWriterLock(string databasePath)
    {
        string path = WriterLockPath(databasePath);
        OwnerOnlyState.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new IOException("This estate's derived cache is being rebuilt on this machine. Try again when 'vericairn estate cache rebuild' finishes.", exception);
        }
    }

    private static FileStream? ExclusiveWriterLock(string databasePath)
    {
        string path = WriterLockPath(databasePath);
        OwnerOnlyState.CreateDirectory(Path.GetDirectoryName(path)!, VericairnHome.Path());
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the derived cache of the repository-synced estate at a root from its canonical record files. The failed database
    /// and its sidecars are moved together, unchanged, into a new directory beside the cache. A local-only estate's database is
    /// canonical and is refused, untouched; so is a rebuild while another runtime has the estate open.
    /// </summary>
    internal static LiveStoreRebuilt Rebuild(string root, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        EstateManifest manifest = EstateManifest.Load(root, findings) ?? throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        string database = EstateSession.LiveStorePath(root, manifest.Estate, manifest.Profile);
        if (manifest.Profile != "repository-synced")
            throw new InvalidOperationException(LocalOnly($"Only a repository-synced estate's derived cache is rebuilt; this estate's profile is {manifest.Profile}, and nothing was changed.", database));

        using FileStream writers = ExclusiveWriterLock(database) ?? throw new IOException(WriterPresent);
        string directory = Path.GetDirectoryName(database)!;
        string[] present = Sidecars.Select(s => database + s).Where(File.Exists).ToArray();
        string? retained = null;
        var moved = new List<(string From, string To)>();
        if (present.Length > 0)
        {
            string stamp = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            retained = Path.Combine(directory, RetainedDirectoryName, stamp);
            for (int n = 2; Directory.Exists(retained); n++) retained = Path.Combine(directory, RetainedDirectoryName, stamp + "-" + n.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(retained);
            try
            {
                // The files keep their names, so SQLite still pairs the retained database with its log for diagnosis.
                foreach (string file in present)
                {
                    string target = Path.Combine(retained, Path.GetFileName(file));
                    File.Move(file, target, overwrite: false);
                    moved.Add((file, target));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                foreach ((string from, string to) in Enumerable.Reverse(moved))
                {
                    try { File.Move(to, from, overwrite: false); }
                    catch (Exception restore) when (restore is IOException or UnauthorizedAccessException) { }
                }
                throw new IOException($"The failed cache could not be moved aside ({exception.Message}); nothing was rebuilt. Its files are in {directory}" + (moved.Count > 0 ? $" or {retained}." : "."), exception);
            }
        }

        long records;
        using (var live = new LiveStore(database, readOnly: false, profile: manifest.Profile))
        {
            live.SetMeta("estate", manifest.Estate);
            live.SetMeta("profile", manifest.Profile);
            live.SetMeta("root", root);
            live.SyncFromDirectory(root);
            records = live.Count();
        }

        return new LiveStoreRebuilt(manifest.Estate, database, records, retained, moved.Select(m => Path.GetFileName(m.To)).ToList());
    }
}
