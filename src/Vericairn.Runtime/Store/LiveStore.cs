// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.Data.Sqlite;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Store;

/// <summary>
/// The runtime's live store for one estate on one machine: a SQLite database that holds the record files
/// (the canonical copy under the local-only profile, a derived copy under the repository-synced profile),
/// never committed and never opened over a shared filesystem. A SQLite failure leaves it as a bounded
/// <see cref="IOException"/> that names the storage profile and its recovery route (FIX-93).
/// </summary>
public sealed class LiveStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SqliteReadSnapshot? readSnapshot;
    private readonly string? profile;
    private FileSnapshot? snapshot;
    private long snapshotVersion;
    // Numbers every snapshot taken in this process, so a snapshot's number says which records it holds whichever store took it.
    private static long snapshots;

    // Deterministic work counters for first-screen regression tests, independent of machine timing.
    internal long FilesRead { get; private set; }

    /// <summary>Opens (creating when needed) the live store database.</summary>
    public LiveStore(string databasePath) : this(databasePath, false) { }

    internal LiveStore(string databasePath, bool readOnly, bool transient = false, string? profile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Path = System.IO.Path.GetFullPath(databasePath);
        this.profile = profile;
        if (!readOnly)
        {
            // Owner-only whichever process creates it, with or without a daemon: the directory, the database and, since SQLite gives
            // them the database's mode, its -wal and -shm files. Ones an earlier build left wider are narrowed here.
            OwnerOnlyState.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            OwnerOnlyState.PrepareDatabase(Path);
        }

        readSnapshot = readOnly && !transient ? new SqliteReadSnapshot(Path) : null;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = transient ? ":memory:" : readSnapshot?.Path ?? Path, Mode = transient ? SqliteOpenMode.Memory : readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        try
        {
            connection.Open();
            if (readOnly && !transient) return;
            Execute("PRAGMA journal_mode = WAL;");
            Execute("CREATE TABLE IF NOT EXISTS files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL, bytes BLOB NOT NULL);");
            Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            readSnapshot?.Dispose();
            throw LiveStoreException.From(Path, profile, exception);
        }
        catch { connection.Dispose(); readSnapshot?.Dispose(); throw; }
    }

    /// <summary>Database file path.</summary>
    public string Path { get; }

    /// <summary>A metadata value, or null.</summary>
    public string? Meta(string key) => Guard(() =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    });

    /// <summary>Every metadata key that starts with the prefix, in key order.</summary>
    internal IReadOnlyList<string> MetaKeys(string prefix) => Guard(() =>
    {
        var keys = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM meta WHERE substr(key, 1, length($prefix)) = $prefix ORDER BY key;";
        command.Parameters.AddWithValue("$prefix", prefix);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) keys.Add(reader.GetString(0));
        return (IReadOnlyList<string>)keys;
    });

    /// <summary>Sets a metadata value.</summary>
    public void SetMeta(string key, string value) => Guard(() =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        return command.ExecuteNonQuery();
    });

    /// <summary>Every record file held, in path order.</summary>
    public IReadOnlyList<RecordFile> Files() => Guard(() =>
    {
        var files = new List<RecordFile>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT path, bytes FROM files ORDER BY path;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new RecordFile(reader.GetString(0), (byte[])reader[1]));
            FilesRead++;
        }

        return (IReadOnlyList<RecordFile>)files;
    });

    // Private original bytes for the loader. Public Files() still returns independent buffers. Local writes invalidate this
    // snapshot; SQLite's data_version also catches another connection changing the canonical local-only store.
    internal FileSnapshot Snapshot() => Guard(() =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        long version = (long)command.ExecuteScalar()!;
        if (snapshot is null || snapshotVersion != version)
        {
            IReadOnlyList<RecordFile> files = Files();
            snapshot = new FileSnapshot(files, files.ToDictionary(f => f.Path, StringComparer.Ordinal), Interlocked.Increment(ref snapshots));
            snapshotVersion = version;
        }
        return snapshot;
    });

    /// <summary>The record files held at one moment, numbered by a generation that is new for every snapshot taken and only for a new one.</summary>
    internal sealed record FileSnapshot(IReadOnlyList<RecordFile> Files, IReadOnlyDictionary<string, RecordFile> ByPath, long Generation);

    /// <summary>Number of record files held.</summary>
    public long Count() => Guard(() =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM files;";
        return (long)(command.ExecuteScalar() ?? 0L);
    });

    /// <summary>True when a path is held.</summary>
    public bool Has(string path) => Guard(() =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM files WHERE path = $path;";
        command.Parameters.AddWithValue("$path", path);
        return command.ExecuteScalar() is not null;
    });

    /// <summary>Stores or replaces a record file.</summary>
    public void Put(string path, byte[] bytes, long mtime = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Guard(() =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO files (path, size, mtime, bytes) VALUES ($path, $size, $mtime, $bytes) ON CONFLICT(path) DO UPDATE SET size = excluded.size, mtime = excluded.mtime, bytes = excluded.bytes;";
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$size", bytes.LongLength);
            command.Parameters.AddWithValue("$mtime", mtime);
            command.Parameters.AddWithValue("$bytes", bytes);
            return command.ExecuteNonQuery();
        });
        snapshot = null;
    }

    /// <summary>Removes a record file; true when it was held.</summary>
    public bool Remove(string path)
    {
        bool removed = Guard(() =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM files WHERE path = $path;";
            command.Parameters.AddWithValue("$path", path);
            return command.ExecuteNonQuery() > 0;
        });
        if (removed) { snapshot = null; }
        return removed;
    }

    /// <summary>
    /// Brings the store in line with the records directory of a repository: new and changed files are read, deleted files
    /// are dropped, unchanged files (same size and modification time) are kept without reading them again.
    /// Returns how many files were read from disk.
    /// </summary>
    public int SyncFromDirectory(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return Guard(() => Sync(root));
    }

    private int Sync(string root)
    {
        string recordsRoot = System.IO.Path.Combine(root, EstateLayout.RecordsDirectory);
        var onDisk = new Dictionary<string, (long Size, long Mtime, string Full)>(StringComparer.Ordinal);
        if (Directory.Exists(recordsRoot))
        {
            foreach (string file in Directory.EnumerateFiles(recordsRoot, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                onDisk[EstateLayout.Relative(root, file)] = (info.Length, info.LastWriteTimeUtc.Ticks, file);
            }
        }

        var held = new Dictionary<string, (long Size, long Mtime)>(StringComparer.Ordinal);
        using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT path, size, mtime FROM files;";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                held[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        int read = 0;
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach ((string path, (long size, long mtime, string full)) in onDisk)
        {
            if (held.TryGetValue(path, out (long Size, long Mtime) entry) && entry.Size == size && entry.Mtime == mtime)
            {
                continue;
            }

            Put(path, File.ReadAllBytes(full), mtime);
            read++;
        }

        foreach (string path in held.Keys.Where(p => !onDisk.ContainsKey(p)))
        {
            Remove(path);
        }

        transaction.Commit();
        return read;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        connection.Dispose();
        readSnapshot?.Dispose();
    }

    // A SQLite failure, such as a malformed database, leaves the store as a bounded diagnostic, never an unhandled exception.
    private T Guard<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (SqliteException exception) { throw LiveStoreException.From(Path, profile, exception); }
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
