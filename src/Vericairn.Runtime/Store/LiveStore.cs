// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.Data.Sqlite;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Store;

/// <summary>
/// The runtime's live store for one estate on one machine: a SQLite database that holds the record files
/// (the canonical copy under the local-only profile, a derived copy under the repository-synced profile),
/// never committed and never opened over a shared filesystem.
/// </summary>
public sealed class LiveStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SqliteReadSnapshot? readSnapshot;
    private FileSnapshot? snapshot;
    private long snapshotVersion;

    // Deterministic work counters for first-screen regression tests, independent of machine timing.
    internal long FilesRead { get; private set; }

    /// <summary>Opens (creating when needed) the live store database.</summary>
    public LiveStore(string databasePath) : this(databasePath, false) { }

    internal LiveStore(string databasePath, bool readOnly, bool transient = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Path = System.IO.Path.GetFullPath(databasePath);
        if (!readOnly) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        readSnapshot = readOnly && !transient ? new SqliteReadSnapshot(Path) : null;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = transient ? ":memory:" : readSnapshot?.Path ?? Path, Mode = transient ? SqliteOpenMode.Memory : readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        try { connection.Open(); }
        catch { connection.Dispose(); readSnapshot?.Dispose(); throw; }
        if (readOnly && !transient) return;
        Execute("PRAGMA journal_mode = WAL;");
        Execute("CREATE TABLE IF NOT EXISTS files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL, bytes BLOB NOT NULL);");
        Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
    }

    /// <summary>Database file path.</summary>
    public string Path { get; }

    /// <summary>A metadata value, or null.</summary>
    public string? Meta(string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Sets a metadata value.</summary>
    public void SetMeta(string key, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>Every record file held, in path order.</summary>
    public IReadOnlyList<RecordFile> Files()
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

        return files;
    }

    // Private original bytes for the loader. Public Files() still returns independent buffers. Local writes invalidate this
    // snapshot; SQLite's data_version also catches another connection changing the canonical local-only store.
    internal FileSnapshot Snapshot()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        long version = (long)command.ExecuteScalar()!;
        if (snapshot is null || snapshotVersion != version)
        {
            IReadOnlyList<RecordFile> files = Files();
            snapshot = new FileSnapshot(files, files.ToDictionary(f => f.Path, StringComparer.Ordinal));
            snapshotVersion = version;
        }
        return snapshot;
    }

    internal sealed record FileSnapshot(IReadOnlyList<RecordFile> Files, IReadOnlyDictionary<string, RecordFile> ByPath);

    /// <summary>Number of record files held.</summary>
    public long Count()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM files;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>True when a path is held.</summary>
    public bool Has(string path)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM files WHERE path = $path;";
        command.Parameters.AddWithValue("$path", path);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Stores or replaces a record file.</summary>
    public void Put(string path, byte[] bytes, long mtime = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO files (path, size, mtime, bytes) VALUES ($path, $size, $mtime, $bytes) ON CONFLICT(path) DO UPDATE SET size = excluded.size, mtime = excluded.mtime, bytes = excluded.bytes;";
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$size", bytes.LongLength);
        command.Parameters.AddWithValue("$mtime", mtime);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.ExecuteNonQuery();
        snapshot = null;
    }

    /// <summary>Removes a record file; true when it was held.</summary>
    public bool Remove(string path)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM files WHERE path = $path;";
        command.Parameters.AddWithValue("$path", path);
        bool removed = command.ExecuteNonQuery() > 0;
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

    private void Execute(string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
