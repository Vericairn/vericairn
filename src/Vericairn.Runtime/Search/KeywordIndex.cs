// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Search;

/// <summary>
/// The machine-local keyword index of one estate: a SQLite database beside the live store, created owner-only, never committed,
/// exported, shared or served, and derived entirely from the records, pinned documents and knowledge drafts, so deleting it loses
/// nothing. Each audience's passages form their own full-text segment with its own ranking statistics, so a search ranks, counts
/// and excerpts only the segments its reader may read. A read-only runtime keeps the index in memory instead.
/// <para>
/// Several processes may share one index: a worker and its run's MCP server, or a daemon and a command. Each holds a shared lock
/// on the index's lock file while it has the index open, so the files are deleted only when no other process has them open; every
/// update runs under the database's write lock and every operation reads the segments afresh, so one process never answers from
/// another's stale view.
/// </para>
/// </summary>
internal sealed class KeywordIndex : IDisposable
{
    internal const string FileName = "search.db";

    // The storage layout. The method's version and the analyzer's identity are kept beside it; a change to any of them rebuilds the index.
    private const string Layout = "vericairn.search-index/3";

    /// <summary>
    /// The fixed column weights of the method, in column order: text 1, titles and headings 4, whole identifiers 10. Text is the first
    /// column because the engine stores positions in the first column without a column marker, and most positions are text.
    /// </summary>
    internal const string Weights = "1.0, 4.0, 10.0";

    /// <summary>What a selection records as the engine.</summary>
    internal const string Engine = "sqlite fts5 bm25 (k1 1.2, b 0.75); weights body 1, title 4, ident 10; scores x10000 rounded half away from zero";

    private const string Tokenizer = "unicode61 remove_diacritics 0 categories 'L* N* Co M*' tokenchars '-_./:@#+'";
    private const int KindRecord = 0, KindDocument = 1, KindKnowledge = 2;
    private const int StateIndexed = 0, StateUnavailable = 1, StateExcluded = 2;
    private static readonly string[] KindNames = ["record", "document", "knowledge"];

    private readonly SqliteConnection connection;
    private readonly FileStream? openLock;
    private readonly Dictionary<string, (long Id, string Suffix)> segments = new(StringComparer.Ordinal);
    private SqliteTransaction? active;

    private KeywordIndex(SqliteConnection connection, string? path, FileStream? openLock)
    {
        this.connection = connection;
        this.openLock = openLock;
        Path = path;
    }

    /// <summary>The database file, or null when the index is kept in memory.</summary>
    internal string? Path { get; }

    /// <summary>
    /// Opens the index at a path, creating it owner-only, or in memory when the path is null. An index from another layout, method
    /// or analyzer is cleared and rebuilt in place; one failing its integrity check is deleted and rebuilt when no other process has
    /// it open. It is derived, so nothing is lost.
    /// </summary>
    internal static KeywordIndex Open(string? path)
    {
        if (path is null) return Initialize(Connect(":memory:", memory: true), null, null);
        path = System.IO.Path.GetFullPath(path);
        for (int attempt = 0; ; attempt++)
        {
            SqliteConnection? connection = null;
            FileStream? shared = null;
            try
            {
                OwnerOnlyFile(path);
                shared = ShareLock(path);
                connection = Connect(path, memory: false);
                using (SqliteCommand check = connection.CreateCommand())
                {
                    check.CommandText = "PRAGMA quick_check;";
                    if (check.ExecuteScalar() as string != "ok") throw new InvalidDataException("The search index failed its integrity check.");
                }

                return Initialize(connection, path, shared);
            }
            catch (Exception exception) when (attempt == 0 && exception is SqliteException or InvalidDataException)
            {
                connection?.Dispose();
                shared?.Dispose();
                if (!TryDelete(path)) throw new IOException("The search index could not be read, and another process has it open, so it was not replaced.", exception);
            }
            catch
            {
                connection?.Dispose();
                shared?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Deletes an index's files, so the next search rebuilds it, unless another process has the index open: then nothing is deleted
    /// and the answer is false.
    /// </summary>
    internal static bool TryDelete(string path)
    {
        using FileStream? exclusive = ExclusiveLock(path);
        if (exclusive is null) return false;
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(file)) File.Delete(file);
        }

        return true;
    }

    // The lock file beside the index: shared by every process with the index open, exclusive only to delete it. It is never deleted.
    private static string LockPath(string path) => path + ".lock";

    private static FileStream ShareLock(string path) => new(LockPath(path), LockOptions(FileShare.ReadWrite | FileShare.Delete));

    private static FileStream? ExclusiveLock(string path)
    {
        try { return new FileStream(LockPath(path), LockOptions(FileShare.None)); }
        catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException) { return null; }
    }

    private static FileStreamOptions LockOptions(FileShare share)
    {
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = share };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    // The database follows the rule all per-user state follows (OwnerOnlyState): created as an empty owner-only file before SQLite
    // opens it, so it is never readable by others, even for a moment, and SQLite gives its -wal and -shm files the same mode; narrowed
    // when an earlier build left it wider; refused when another user owns it; and on a filesystem that keeps no Unix modes used as it
    // is, with one warning, rather than rebuilt at every opening. A link is not trusted, and one others could write to until now is
    // rebuilt once it is owner-only: the index is derived, so nothing is lost.
    private static void OwnerOnlyFile(string path)
    {
        OwnerOnlyState.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var info = new FileInfo(path);
        if (info.Exists && info.LinkTarget is not null)
        {
            if (!TryDelete(path)) throw new IOException("The search index is a link, and another process has it open, so it was not replaced.");
            info.Refresh();
        }

        bool writable = !OperatingSystem.IsWindows() && info.Exists && (info.UnixFileMode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
        OwnerOnlyState.PrepareDatabase(path);
        if (writable && !OperatingSystem.IsWindows() && File.GetUnixFileMode(path) == OwnerOnlyState.FileMode)
        {
            if (!TryDelete(path)) throw new IOException("The search index could be written by other users, and another process has it open, so it was not replaced.");
            OwnerOnlyState.PrepareDatabase(path);
        }

        if (File.Exists(path)) return;
        // Windows keeps its profile's permissions and SQLite opens the index without creating it, so it is created here.
        try
        {
            using var created = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process created it first.
        }
    }

    private static SqliteConnection Connect(string path, bool memory)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, memory ? "PRAGMA foreign_keys = OFF;" : "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 10000;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS segments (id INTEGER PRIMARY KEY, audience TEXT NOT NULL UNIQUE, suffix TEXT NOT NULL UNIQUE, records TEXT, documents TEXT);
        CREATE TABLE IF NOT EXISTS docs (id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE, fpath TEXT NOT NULL, revision TEXT NOT NULL, rev_hash BLOB NOT NULL, byte_hash BLOB NOT NULL, hash_mode TEXT, seg INTEGER, state INTEGER NOT NULL, size INTEGER, mtime INTEGER, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS passages (rowid INTEGER PRIMARY KEY, seg INTEGER NOT NULL, kind INTEGER NOT NULL, rid TEXT, rhash BLOB, doc INTEGER, start INTEGER, length INTEGER, type TEXT, key TEXT, at INTEGER NOT NULL, superseded INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS passages_key ON passages (key) WHERE key IS NOT NULL;
        CREATE INDEX IF NOT EXISTS passages_doc ON passages (doc) WHERE doc IS NOT NULL;
        """;

    private static KeywordIndex Initialize(SqliteConnection connection, string? path, FileStream? openLock)
    {
        try
        {
            var index = new KeywordIndex(connection, path, openLock);
            bool dropped = false;
            // Under the write lock, so two processes opening an old index together clear it once.
            using (Scope scope = index.Begin(write: true))
            {
                if (Scalar(connection, "SELECT value FROM meta WHERE key = 'layout';", tolerateMissing: true) is string layout && layout != Layout)
                {
                    DropEverything(connection);
                    dropped = true;
                }

                Execute(connection, Schema);
                if (index.Meta("layout") != Layout || index.Meta("method") != Records.Search.SearchSelections.KeywordMethod || index.Meta("analyzer") != SearchAnalyzer.Identity)
                {
                    if (index.Meta("layout") is not null) index.Clear();
                    index.SetIdentity();
                }

                scope.Commit();
            }

            if (dropped) index.Vacuum();
            index.LoadSegments();
            return index;
        }
        catch
        {
            connection.Dispose();
            openLock?.Dispose();
            throw;
        }
    }

    private void SetIdentity()
    {
        SetMeta("layout", Layout);
        SetMeta("method", Records.Search.SearchSelections.KeywordMethod);
        SetMeta("analyzer", SearchAnalyzer.Identity);
    }

    /// <summary>A transaction, or nothing when one is already open: reads see one state, and a write holds the write lock from its start.</summary>
    private Scope Begin(bool write)
    {
        if (active is not null) return new Scope(this, null);
        active = connection.BeginTransaction(deferred: !write);
        return new Scope(this, active);
    }

    private sealed class Scope(KeywordIndex owner, SqliteTransaction? transaction) : IDisposable
    {
        internal void Commit() => transaction?.Commit();

        public void Dispose()
        {
            if (transaction is null) return;
            transaction.Dispose();
            owner.active = null;
        }
    }

    // Compaction is opportunistic: another process reading the index defers it to a later update.
    private void Vacuum()
    {
        try { Execute(connection, "VACUUM;"); }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6) { }
    }

    // Another layout's index is derived and disposable: every table and index goes, and the file is compacted before it is rebuilt.
    private static void DropEverything(SqliteConnection connection)
    {
        // Virtual tables first, which drop their own shadow tables, then every remaining table and index.
        foreach (string filter in new[] { "type = 'table' AND sql LIKE 'CREATE VIRTUAL TABLE%'", "type IN ('table', 'index') AND name NOT LIKE 'sqlite!_%' ESCAPE '!'" })
        {
            var names = new List<(string Type, string Name)>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT type, name FROM sqlite_schema WHERE " + filter + ";";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) names.Add((reader.GetString(0), reader.GetString(1)));
            }

            foreach ((string type, string name) in names.OrderBy(n => n.Type == "index" ? 0 : 1))
                Execute(connection, $"DROP {type.ToUpperInvariant()} IF EXISTS \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\";");
        }

    }

    private static object? Scalar(SqliteConnection connection, string sql, bool tolerateMissing)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
        catch (SqliteException) when (tolerateMissing)
        {
            return null;
        }
    }

    private void Clear()
    {
        foreach (string suffix in Segments().Select(s => s.Suffix)) Execute(connection, $"DROP TABLE IF EXISTS f_{suffix};");
        Execute(connection, "DELETE FROM passages; DELETE FROM docs; DELETE FROM segments; DELETE FROM meta;");
        segments.Clear();
    }

    /// <summary>Removes every passage so the next update builds the index afresh.</summary>
    internal void Rebuild()
    {
        using (Scope scope = Begin(write: true))
        {
            LoadSegments();
            Clear();
            SetIdentity();
            scope.Commit();
        }

        Vacuum();
    }

    private void LoadSegments()
    {
        segments.Clear();
        foreach ((long id, string audience, string suffix) in Segments()) segments[audience] = (id, suffix);
    }

    private List<(long Id, string Audience, string Suffix)> Segments()
    {
        var list = new List<(long, string, string)>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, audience, suffix FROM segments ORDER BY id;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) list.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }

    // Called under the write lock with the segments just read, so another process's segment is already known here.
    private (long Id, string Suffix) Segment(string audience)
    {
        if (segments.TryGetValue(audience, out var known)) return known;
        string suffix = SearchAudiences.Suffix(audience);
        Execute(connection, $"CREATE VIRTUAL TABLE IF NOT EXISTS f_{suffix} USING fts5(body, title, ident, content='', contentless_delete=1, tokenize=\"{Tokenizer}\");");
        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO segments (audience, suffix) VALUES ($audience, $suffix) ON CONFLICT(audience) DO NOTHING; SELECT id FROM segments WHERE audience = $audience;";
        insert.Parameters.AddWithValue("$audience", audience);
        insert.Parameters.AddWithValue("$suffix", suffix);
        long id = (long)insert.ExecuteScalar()!;
        segments[audience] = (id, suffix);
        return (id, suffix);
    }

    internal string? Meta(string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    internal void SetMeta(string key, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>One current document pin, as the index sees it.</summary>
    internal sealed record Pin(string Path, string Revision, string RevisionHash, string ByteHash, string? HashMode, string Title, long At, bool Excluded);

    /// <summary>What an update did and whether the index now matches the snapshot it was given.</summary>
    internal sealed record UpdateResult(bool Current, int RecordsAdded, int RecordsRemoved, int DocumentsIndexed, int DocumentsRemoved, IReadOnlyList<string> Unavailable);

    /// <summary>The fingerprint of a snapshot the index last matched completely.</summary>
    internal string? Fingerprint => Meta("fingerprint");

    /// <summary>
    /// Whether the index matches a snapshot: the same fingerprint, and no unavailable document whose working-tree file has
    /// changed since it was found unavailable.
    /// </summary>
    internal bool Matches(string fingerprint, string root)
    {
        if (Fingerprint != fingerprint) return false;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT path, size, mtime FROM docs WHERE state = $state;";
        command.Parameters.AddWithValue("$state", StateUnavailable);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            (long? size, long? mtime) = Stat(root, reader.GetString(0));
            if (size != (reader.IsDBNull(1) ? null : reader.GetInt64(1)) || mtime != (reader.IsDBNull(2) ? null : reader.GetInt64(2))) return false;
        }

        return true;
    }

    /// <summary>
    /// Brings the index to a snapshot incrementally: records added or removed, supersession changes, and pins added, changed or
    /// removed. A deadline commits the progress made and reports the index not current; the next update continues.
    /// </summary>
    internal UpdateResult Update(EstateModel model, IReadOnlyDictionary<string, RecordFile> files, IReadOnlyList<Pin> pins, string fingerprint,
        Func<Pin, byte[]?> read, DateTimeOffset? deadline)
    {
        int added = 0, removed = 0, indexed = 0, dropped = 0;
        var unavailable = new List<string>();
        bool complete = true;
        bool Overdue(int count) => deadline is { } limit && count % 64 == 0 && DateTimeOffset.UtcNow > limit;
        using (Scope scope = Begin(write: true))
        {
            LoadSegments();
            // Records: remove what is gone or changed, add what is new, then correct supersession flags.
            var stored = new Dictionary<string, (long Row, byte[] Hash, bool Superseded, long Segment)>(StringComparer.Ordinal);
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT rowid, rid, rhash, superseded, seg FROM passages WHERE kind = 0;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) stored[reader.GetString(1)] = (reader.GetInt64(0), (byte[])reader[2], reader.GetInt64(3) != 0, reader.GetInt64(4));
            }

            foreach ((string id, var row) in stored.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                if (model.ById.TryGetValue(id, out RecordEnvelope? record) && HashBytes(record.ComputedHash).AsSpan().SequenceEqual(row.Hash)) continue;
                DeletePassage(row.Row, row.Segment);
                stored.Remove(id);
                removed++;
            }

            int work = 0;
            foreach (RecordEnvelope record in model.Records.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                bool superseded = model.SupersededBy.ContainsKey(record.Id);
                if (stored.TryGetValue(record.Id, out var row))
                {
                    if (row.Superseded != superseded) SetSuperseded(row.Row, superseded);
                    continue;
                }

                if (Overdue(++work)) { complete = false; break; }
                if (!files.ContainsKey(record.ExpectedPath)) continue;
                SearchPassage passage = SearchPassages.Record(record);
                InsertPassage(passage, KindRecord, record.Id, HashBytes(record.ComputedHash), null, Epoch(record.At), superseded);
                added++;
            }

            // Documents: a pin's passages are replaced when its revision changes; a pin whose bytes were unavailable is tried again
            // when its working-tree file changes.
            var storedDocs = new Dictionary<string, (long Id, string Revision, byte[] ByteHash, long? Segment, int State, long? Size, long? Mtime)>(StringComparer.Ordinal);
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, path, revision, byte_hash, seg, state, size, mtime FROM docs;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    storedDocs[reader.GetString(1)] = (reader.GetInt64(0), reader.GetString(2), (byte[])reader[3], reader.IsDBNull(4) ? null : reader.GetInt64(4),
                        (int)reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetInt64(7));
            }

            var byPath = pins.ToDictionary(p => p.Path, StringComparer.Ordinal);
            foreach ((string path, var doc) in storedDocs.OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                bool keep = byPath.TryGetValue(path, out Pin? pin) && pin.Revision == doc.Revision && HashBytes(pin.ByteHash).AsSpan().SequenceEqual(doc.ByteHash)
                    && (pin.Excluded == (doc.State == StateExcluded));
                if (keep && doc.State == StateUnavailable && Stat(model.Root, path) != (doc.Size, doc.Mtime)) keep = false;
                if (keep) continue;
                DeleteDocument(doc.Id, doc.Segment);
                storedDocs.Remove(path);
                if (doc.State == StateIndexed) dropped++;
            }

            if (complete)
            {
                foreach (Pin pin in pins.OrderBy(p => p.Path, StringComparer.Ordinal))
                {
                    if (storedDocs.TryGetValue(pin.Path, out var doc))
                    {
                        if (doc.State == StateUnavailable) unavailable.Add(pin.Path);
                        continue;
                    }

                    if (Overdue(++work)) { complete = false; break; }
                    if (pin.Excluded)
                    {
                        InsertDocument(pin, null, StateExcluded, null);
                        continue;
                    }

                    (long? size, long? mtime) stat = Stat(model.Root, pin.Path);
                    byte[]? bytes = read(pin);
                    if (bytes is null)
                    {
                        InsertDocument(pin, null, StateUnavailable, stat);
                        unavailable.Add(pin.Path);
                        continue;
                    }

                    IReadOnlyList<SearchPassage> passages = SearchPassages.Document(pin.Path, pin.Title, bytes);
                    string audience = passages.Count > 0 ? passages[0].Audience : SearchAudiences.Internal;
                    long segment = Segment(audience).Id;
                    long docId = InsertDocument(pin, segment, StateIndexed, stat);
                    foreach (SearchPassage passage in passages)
                        InsertPassage(passage, passage.Kind == "knowledge" ? KindKnowledge : KindDocument, null, null, docId, pin.At, false);
                    indexed++;
                }
            }

            UpdateBases();
            if (complete) SetMeta("fingerprint", fingerprint);
            else SetMeta("fingerprint", "partial");
            scope.Commit();
        }

        Compact();
        return new UpdateResult(complete, added, removed, indexed, dropped, unavailable);
    }

    private static (long? Size, long? Mtime) Stat(string root, string path)
    {
        try
        {
            var info = new FileInfo(System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : (null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }

    private void InsertPassage(SearchPassage passage, int kind, string? record, byte[]? recordHash, long? doc, long at, bool superseded)
    {
        (long segment, string suffix) = Segment(passage.Audience);
        long row;
        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO passages (seg, kind, rid, rhash, doc, start, length, type, key, at, superseded) VALUES ($seg, $kind, $rid, $rhash, $doc, $start, $length, $type, $key, $at, $superseded) RETURNING rowid;";
            insert.Parameters.AddWithValue("$seg", segment);
            insert.Parameters.AddWithValue("$kind", kind);
            insert.Parameters.AddWithValue("$rid", (object?)record ?? DBNull.Value);
            insert.Parameters.AddWithValue("$rhash", (object?)recordHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("$doc", (object?)doc ?? DBNull.Value);
            insert.Parameters.AddWithValue("$start", doc is null ? DBNull.Value : passage.Start);
            insert.Parameters.AddWithValue("$length", doc is null ? DBNull.Value : passage.Length);
            insert.Parameters.AddWithValue("$type", (object?)passage.Type ?? DBNull.Value);
            insert.Parameters.AddWithValue("$key", (object?)passage.Key ?? DBNull.Value);
            insert.Parameters.AddWithValue("$at", at);
            insert.Parameters.AddWithValue("$superseded", superseded ? 1 : 0);
            row = (long)insert.ExecuteScalar()!;
        }

        using SqliteCommand text = connection.CreateCommand();
        text.CommandText = $"INSERT INTO f_{suffix} (rowid, body, title, ident) VALUES ($row, $body, $title, $ident);";
        text.Parameters.AddWithValue("$row", row);
        text.Parameters.AddWithValue("$ident", passage.Ident);
        text.Parameters.AddWithValue("$title", passage.Title);
        text.Parameters.AddWithValue("$body", passage.Body);
        text.ExecuteNonQuery();
    }

    private void DeletePassage(long row, long segment)
    {
        string? suffix = segments.Values.Where(s => s.Id == segment).Select(s => s.Suffix).FirstOrDefault();
        if (suffix is not null)
        {
            using SqliteCommand text = connection.CreateCommand();
            text.CommandText = $"DELETE FROM f_{suffix} WHERE rowid = $row;";
            text.Parameters.AddWithValue("$row", row);
            text.ExecuteNonQuery();
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM passages WHERE rowid = $row;";
        command.Parameters.AddWithValue("$row", row);
        command.ExecuteNonQuery();
    }

    private void SetSuperseded(long row, bool superseded)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE passages SET superseded = $value WHERE rowid = $row;";
        command.Parameters.AddWithValue("$value", superseded ? 1 : 0);
        command.Parameters.AddWithValue("$row", row);
        command.ExecuteNonQuery();
    }

    private long InsertDocument(Pin pin, long? segment, int state, (long? Size, long? Mtime)? stat)
    {
        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO docs (path, fpath, revision, rev_hash, byte_hash, hash_mode, seg, state, size, mtime, at) VALUES ($path, $fpath, $revision, $revHash, $byteHash, $mode, $seg, $state, $size, $mtime, $at) RETURNING id;";
        insert.Parameters.AddWithValue("$path", pin.Path);
        // The path as the analyzer folds it, so an exact path match is exact for any script, not only ASCII.
        insert.Parameters.AddWithValue("$fpath", SearchAnalyzer.Normalize(pin.Path));
        insert.Parameters.AddWithValue("$revision", pin.Revision);
        insert.Parameters.AddWithValue("$revHash", HashBytes(pin.RevisionHash));
        insert.Parameters.AddWithValue("$byteHash", HashBytes(pin.ByteHash));
        insert.Parameters.AddWithValue("$mode", (object?)pin.HashMode ?? DBNull.Value);
        insert.Parameters.AddWithValue("$seg", (object?)segment ?? DBNull.Value);
        insert.Parameters.AddWithValue("$state", state);
        insert.Parameters.AddWithValue("$size", (object?)stat?.Size ?? DBNull.Value);
        insert.Parameters.AddWithValue("$mtime", (object?)stat?.Mtime ?? DBNull.Value);
        insert.Parameters.AddWithValue("$at", pin.At);
        return (long)insert.ExecuteScalar()!;
    }

    private void DeleteDocument(long id, long? segment)
    {
        if (segment is long seg)
        {
            var rows = new List<long>();
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = "SELECT rowid FROM passages WHERE doc = $doc;";
                select.Parameters.AddWithValue("$doc", id);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read()) rows.Add(reader.GetInt64(0));
            }

            foreach (long row in rows) DeletePassage(row, seg);
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM docs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // Each segment's basis: the record-set hash over its records' {id, hash} sorted by id, as estate observations define it, and the
    // document-set hash over its indexed {path, revision, byteHash} sorted by path.
    private void UpdateBases()
    {
        foreach ((string audience, (long id, _)) in segments.ToList())
        {
            var records = new List<(string Id, string Hash)>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT rid, rhash FROM passages WHERE kind = 0 AND seg = $seg;";
                command.Parameters.AddWithValue("$seg", id);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) records.Add((reader.GetString(0), HashText((byte[])reader[1])));
            }

            var documents = new List<(string Path, string Revision, string ByteHash)>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT path, revision, byte_hash FROM docs WHERE seg = $seg AND state = 0;";
                command.Parameters.AddWithValue("$seg", id);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) documents.Add((reader.GetString(0), reader.GetString(1), HashText((byte[])reader[2])));
            }

            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE segments SET records = $records, documents = $documents WHERE id = $id;";
            update.Parameters.AddWithValue("$records", RecordSetHash(records));
            update.Parameters.AddWithValue("$documents", DocumentSetHash(documents));
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
    }

    /// <summary>The record-set hash of estate observations: SHA-256 of the canonical array of {id, hash}, sorted by id.</summary>
    internal static string RecordSetHash(IEnumerable<(string Id, string Hash)> records) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(
        JsonSerializer.SerializeToElement(records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new Dictionary<string, string> { ["hash"] = r.Hash, ["id"] = r.Id }))));

    /// <summary>The document-set hash: SHA-256 of the canonical array of {path, revision, byteHash}, sorted by path.</summary>
    internal static string DocumentSetHash(IEnumerable<(string Path, string Revision, string ByteHash)> documents) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(
        JsonSerializer.SerializeToElement(documents.OrderBy(d => d.Path, StringComparer.Ordinal)
            .Select(d => new Dictionary<string, string> { ["byteHash"] = d.ByteHash, ["path"] = d.Path, ["revision"] = d.Revision }))));

    /// <summary>One segment's basis.</summary>
    internal sealed record SegmentBasis(string Audience, string Records, string Documents, long Passages);

    /// <summary>The bases of the segments a reader may read, in the fixed audience order; a segment with no passages yet has the empty sets' hashes.</summary>
    internal IReadOnlyList<SegmentBasis> Bases(IEnumerable<string> audiences)
    {
        LoadSegments();
        string emptyRecords = RecordSetHash([]);
        string emptyDocuments = DocumentSetHash([]);
        var result = new List<SegmentBasis>();
        foreach (string audience in SearchAudiences.Ordered(audiences))
        {
            if (!segments.TryGetValue(audience, out var segment))
            {
                result.Add(new SegmentBasis(audience, emptyRecords, emptyDocuments, 0));
                continue;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT records, documents, (SELECT COUNT(*) FROM passages WHERE seg = $id) FROM segments WHERE id = $id;";
            command.Parameters.AddWithValue("$id", segment.Id);
            using SqliteDataReader reader = command.ExecuteReader();
            reader.Read();
            result.Add(new SegmentBasis(audience, reader.IsDBNull(0) ? emptyRecords : reader.GetString(0), reader.IsDBNull(1) ? emptyDocuments : reader.GetString(1), reader.GetInt64(2)));
        }

        return result;
    }

    /// <summary>The index revision: the method and the bases of the segments searched.</summary>
    internal static string Revision(IEnumerable<SegmentBasis> bases) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["method"] = Records.Search.SearchSelections.KeywordMethod,
        ["segments"] = bases.Select(b => new Dictionary<string, string> { ["audience"] = b.Audience, ["documents"] = b.Documents, ["records"] = b.Records }).ToArray(),
    })));

    /// <summary>One hit before its source is rechecked: where it is, how it scored and whether it matched an identity exactly.</summary>
    internal sealed record Hit(long Row, string Audience, string Kind, long Score, bool Exact, string? Record, string? RecordHash, string? Path,
        string? Revision, string? RevisionHash, string? ByteHash, string? HashMode, long Start, long Length, string? Type, long At)
    {
        /// <summary>The tie-breaking source identity: kind, then record id or document path and offset.</summary>
        internal string Order => Record is not null ? "r:" + Record : "d:" + Path + "#" + Start.ToString("D12", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Searches the permitted segments only: each segment's matches are scored with that segment's own statistics, filtered before
    /// ranking, and merged by exact identity match first, then rounded score, then source identity. Restricted segments are never
    /// opened, so they change no readable score, rank, count or error.
    /// </summary>
    internal (IReadOnlyList<Hit> Hits, bool More) Search(SearchQuery query, IReadOnlyList<string> audiences, int limit)
    {
        // One read transaction: the segments, matches and passages all come from one state of the index.
        using Scope scope = Begin(write: false);
        LoadSegments();
        string expression = query.Expression();
        var found = new Dictionary<long, (string Audience, long Score, bool Exact, string Order)>();
        string[] exact = query.Exact.ToArray();
        foreach (string audience in SearchAudiences.Ordered(audiences))
        {
            if (!segments.TryGetValue(audience, out var segment)) continue;
            using (SqliteCommand command = connection.CreateCommand())
            {
                var where = new StringBuilder();
                Filters(command, query.Filters, where);
                command.CommandText = $"SELECT f.rowid, bm25(f_{segment.Suffix}, {Weights}), p.rid, d.path, p.start FROM f_{segment.Suffix} AS f JOIN passages AS p ON p.rowid = f.rowid LEFT JOIN docs AS d ON d.id = p.doc WHERE f_{segment.Suffix} MATCH $expression AND p.seg = $seg{where};";
                command.Parameters.AddWithValue("$expression", expression);
                command.Parameters.AddWithValue("$seg", segment.Id);
                try
                {
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                        found[reader.GetInt64(0)] = (audience, Score(reader.GetDouble(1)), false, Order(reader, 2));
                }
                catch (SqliteException exception) when (exception.Message.Contains("fts5", StringComparison.OrdinalIgnoreCase))
                {
                    // The engine only ever sees quoted analyzer output; if it still cannot read it, the query is refused, never the index.
                    throw new SearchQueryRejectedException(exception);
                }
            }

            if (exact.Length == 0) continue;
            using (SqliteCommand command = connection.CreateCommand())
            {
                var where = new StringBuilder();
                Filters(command, query.Filters, where);
                var names = new List<string>();
                for (int i = 0; i < exact.Length; i++)
                {
                    string name = "$e" + i.ToString(CultureInfo.InvariantCulture);
                    command.Parameters.AddWithValue(name, exact[i]);
                    names.Add(name);
                }

                string list = string.Join(", ", names);
                command.CommandText = $"SELECT p.rowid, p.rid, d.path, p.start FROM passages AS p LEFT JOIN docs AS d ON d.id = p.doc WHERE p.seg = $seg AND (lower(p.rid) IN ({list}) OR p.key IN ({list}) OR d.fpath IN ({list})){where};";
                command.Parameters.AddWithValue("$seg", segment.Id);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long row = reader.GetInt64(0);
                    found[row] = found.TryGetValue(row, out var prior) ? prior with { Exact = true } : (audience, 0, true, Order(reader, 1));
                }
            }
        }

        var ordered = found.OrderByDescending(h => h.Value.Exact).ThenByDescending(h => h.Value.Score).ThenBy(h => h.Value.Order, StringComparer.Ordinal).ToList();
        var hits = new List<Hit>();
        foreach (var (row, value) in ordered.Take(limit))
        {
            if (Describe(row, value.Audience, value.Score, value.Exact) is { } hit) hits.Add(hit);
        }

        return (hits, ordered.Count > limit);
    }

    // The same tie-breaking identity as Hit.Order, read from (rid, path, start) columns.
    private static string Order(SqliteDataReader reader, int first) => !reader.IsDBNull(first)
        ? "r:" + reader.GetString(first)
        : "d:" + (reader.IsDBNull(first + 1) ? string.Empty : reader.GetString(first + 1)) + "#" + (reader.IsDBNull(first + 2) ? 0 : reader.GetInt64(first + 2)).ToString("D12", CultureInfo.InvariantCulture);

    private static long Score(double bm25)
    {
        double value = Math.Round(-bm25 * 10000.0, MidpointRounding.AwayFromZero);
        return value <= 0 || double.IsNaN(value) ? 0 : (long)Math.Min(value, CanonicalJson.MaxSafeInteger);
    }

    private static void Filters(SqliteCommand command, SearchQuery.Filter filters, StringBuilder where)
    {
        if (!filters.History) where.Append(" AND p.superseded = 0");
        void In(string column, IReadOnlyList<object> values, string prefix)
        {
            if (values.Count == 0) return;
            var names = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                string name = "$" + prefix + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(name, values[i]);
                names.Add(name);
            }

            where.Append(" AND ").Append(column).Append(" IN (").Append(string.Join(", ", names)).Append(')');
        }

        In("p.type", filters.Types.Cast<object>().ToList(), "t");
        In("p.kind", filters.Kinds.Select(k => (object)Array.IndexOf(KindNames, k)).ToList(), "k");
        In("p.key", filters.Keys.Cast<object>().ToList(), "y");
        if (filters.Paths.Count > 0)
        {
            var clauses = new List<string>();
            for (int i = 0; i < filters.Paths.Count; i++)
            {
                string name = "$p" + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(name, filters.Paths[i]);
                clauses.Add($"substr(CASE WHEN p.kind = 0 THEN 'records/' || p.type || '/' || strftime('%Y-%m', p.at, 'unixepoch') || '/' || p.rid || '.json' ELSE d.path END, 1, length({name})) = {name}");
            }

            where.Append(" AND (").Append(string.Join(" OR ", clauses)).Append(')');
        }

        if (filters.Since is string since)
        {
            command.Parameters.AddWithValue("$since", DayEpoch(since));
            where.Append(" AND p.at >= $since");
        }

        if (filters.Before is string before)
        {
            command.Parameters.AddWithValue("$before", DayEpoch(before));
            where.Append(" AND p.at < $before");
        }
    }

    private Hit? Describe(long row, string audience, long score, bool exact)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT p.kind, p.rid, p.rhash, d.path, d.revision, d.rev_hash, d.byte_hash, d.hash_mode, p.start, p.length, p.type, p.at FROM passages AS p LEFT JOIN docs AS d ON d.id = p.doc WHERE p.rowid = $row;";
        command.Parameters.AddWithValue("$row", row);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        int kind = (int)reader.GetInt64(0);
        if (kind is < 0 or > 2) return null;
        return new Hit(row, audience, KindNames[kind], score, exact,
            reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : HashText((byte[])reader[2]),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : HashText((byte[])reader[5]), reader.IsDBNull(6) ? null : HashText((byte[])reader[6]),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? 0 : reader.GetInt64(8), reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt64(11));
    }

    /// <summary>The latest recorded time among the indexed records, or null when none is indexed.</summary>
    internal string? AsOf(IEnumerable<string> audiences)
    {
        // Only the reader's own segments: a restricted record's time changes nothing another reader sees.
        LoadSegments();
        long? latest = null;
        foreach (string audience in audiences)
        {
            if (!segments.TryGetValue(audience, out var segment)) continue;
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT max(at) FROM passages WHERE kind = 0 AND seg = $seg;";
            command.Parameters.AddWithValue("$seg", segment.Id);
            if (command.ExecuteScalar() is long at && (latest is null || at > latest)) latest = at;
        }

        return latest is long time ? DateTimeOffset.FromUnixTimeSeconds(time).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The audiences of every segment the index holds.</summary>
    internal IReadOnlyList<string> Audiences()
    {
        LoadSegments();
        return segments.Keys.ToList();
    }

    /// <summary>The paths of the documents whose pinned bytes are indexed.</summary>
    internal IReadOnlySet<string> IndexedDocuments() => DocumentPaths(StateIndexed).ToHashSet(StringComparer.Ordinal);

    /// <summary>The paths of pinned documents whose bytes this copy did not hold when last tried.</summary>
    internal IReadOnlyList<string> UnavailableDocuments() => DocumentPaths(StateUnavailable);

    private List<string> DocumentPaths(int state)
    {
        var paths = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM docs WHERE state = $state ORDER BY path;";
        command.Parameters.AddWithValue("$state", state);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) paths.Add(reader.GetString(0));
        return paths;
    }

    /// <summary>The bytes the index takes on disk, or in memory.</summary>
    internal long Size()
    {
        if (Path is null)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            return (long)command.ExecuteScalar()!;
        }

        Checkpoint();
        return new[] { Path, Path + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    }

    // Pages freed by many replaced passages are returned to the file system once they are a tenth of the file.
    private void Compact()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT freelist_count, page_count FROM pragma_freelist_count(), pragma_page_count();";
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetInt64(0) < 256 || reader.GetInt64(0) * 10 < reader.GetInt64(1)) return;
        }

        Vacuum();
    }

    /// <summary>Moves the write-ahead log into the database, so its size is measured where it lives.</summary>
    internal void Checkpoint()
    {
        if (Path is not null) Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    /// <summary>Test seam: overwrites one passage's stored source hash, as a tampered or stale index would hold it.</summary>
    internal void Tamper(string recordOrPath)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE passages SET rhash = zeroblob(32) WHERE rid = $value; UPDATE docs SET byte_hash = zeroblob(32) WHERE path = $value;";
        command.Parameters.AddWithValue("$value", recordOrPath);
        command.ExecuteNonQuery();
    }

    /// <summary>Exercises the full-text module exactly as the index uses it: a segment, a contentless passage, a ranked match and a delete.</summary>
    internal bool SelfTest()
    {
        using Scope scope = Begin(write: true);
        // Three passages, so the matching one's term is rare enough to score above zero.
        InsertPassage(new SearchPassage(SearchAudiences.Internal, "record", 0, 0, "probe", "probe-1", "context-02 01m6hrm89g55req0qrdvpjwqfz", "keyword search", "a probe passage"),
            KindRecord, "01M6HRM89G55REQ0QRDVPJWQFZ", new byte[32], null, 0, false);
        InsertPassage(new SearchPassage(SearchAudiences.Internal, "record", 0, 0, "probe", "probe-2", "", "another", "an unrelated passage"), KindRecord, "01M6HRM89G55REQ0QRDVPJWQG0", new byte[32], null, 0, false);
        InsertPassage(new SearchPassage(SearchAudiences.Internal, "record", 0, 0, "probe", "probe-3", "", "a third", "an unrelated passage"), KindRecord, "01M6HRM89G55REQ0QRDVPJWQG1", new byte[32], null, 0, false);
        (SearchQuery? query, _) = SearchQuery.Parse("CONTEXT-02 keyword");
        (IReadOnlyList<Hit> hits, _) = Search(query!, [SearchAudiences.Internal], 10);
        bool ok = hits.Count == 1 && hits[0].Score > 0 && !hits[0].Exact && hits[0].Record == "01M6HRM89G55REQ0QRDVPJWQFZ";
        DeletePassage(hits.Count == 1 ? hits[0].Row : 0, Segment(SearchAudiences.Internal).Id);
        scope.Dispose();
        LoadSegments();
        return ok;
    }

    /// <summary>The engine's version, for diagnostics.</summary>
    internal string EngineVersion()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        connection.Dispose();
        openLock?.Dispose();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static byte[] HashBytes(string hash) => Convert.FromHexString(hash.AsSpan(ContentHash.Prefix.Length));

    internal static string HashText(byte[] bytes) => ContentHash.Prefix + Convert.ToHexStringLower(bytes);

    internal static long Epoch(string at) => DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToUnixTimeSeconds();

    private static long DayEpoch(string day) => new DateTimeOffset(DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}

/// <summary>Whether this build's SQLite has the full-text module the keyword method needs, exercised exactly as the index uses it.</summary>
internal static class SearchEngine
{
    /// <summary>The SQLite version with FTS5 confirmed, or the reason it is unavailable.</summary>
    internal static (string? Version, string? Problem) Probe()
    {
        try
        {
            using KeywordIndex index = KeywordIndex.Open(null);
            string version = index.EngineVersion();
            return index.SelfTest() ? (version, null) : (version, "the full-text module did not rank its probe passage");
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            return (null, exception.Message);
        }
    }
}

/// <summary>The full-text engine could not read a query's expression: the query is refused and the index is untouched.</summary>
internal sealed class SearchQueryRejectedException(Exception inner) : Exception("The full-text engine could not read the query.", inner);
