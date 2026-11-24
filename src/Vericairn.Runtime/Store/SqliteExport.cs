// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Microsoft.Data.Sqlite;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Store;

/// <summary>
/// From 0.9.5 (FIX-174): a consistent, checked copy of a SQLite database as it is on disk, read through a connection of its own whichever
/// connection holds it open, as an upgrade's restore point keeps the live store and every other database beside it. A database that a
/// connection holds, or did not close, has its <c>-wal</c>, <c>-shm</c> or <c>-journal</c> file beside it, and is read through a read-only
/// connection, which reads every page its holder committed in one snapshot and creates nothing that is not there already. One with none
/// of them was closed cleanly and no connection holds it, so its file alone holds every committed page: it is read as an immutable file,
/// which takes no lock and creates nothing beside it. Like a read-only session's disposable snapshot (<see cref="SqliteReadSnapshot"/>),
/// that assumes the documented single-runtime ownership of the state on this machine. The copy is one rollback-journal file, which is
/// checked before it is kept: it exists, passes SQLite's integrity check and holds as many rows in each table as the database did in the
/// snapshot it was copied from.
/// </summary>
internal static class SqliteExport
{
    /// <summary>The files SQLite keeps beside a database while a connection holds it, or after one did not close it.</summary>
    internal static readonly string[] Sidecars = ["-wal", "-shm", "-journal"];

    /// <summary>True when the file begins with SQLite's header, so it is a database whatever its name.</summary>
    internal static bool IsDatabase(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> start = stackalloc byte[16];
            return stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) == start.Length && start.SequenceEqual("SQLite format 3\0"u8);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the database at <paramref name="source"/> to the new file <paramref name="destination"/> and checks the copy; throws a
    /// <see cref="SqliteExportException"/> that names the database by its file name and says what failed. <paramref name="written"/> is
    /// called with the copy's path once it is written and before it is checked.
    /// </summary>
    internal static void Export(string source, string destination, Action<string>? written = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        source = Path.GetFullPath(source);
        string name = Path.GetFileName(source);
        IReadOnlyDictionary<string, long>? held = null;
        try
        {
            using SqliteConnection from = Open(Held(source) ? source : Immutable(source), SqliteOpenMode.ReadOnly);
            // The rows are counted and the pages copied in one read transaction, so the counts are those of the snapshot copied, whatever
            // the database's holder commits meanwhile.
            using SqliteTransaction read = from.BeginTransaction(deferred: true);
            held = Count(from, read);
            // Owner-only from its first byte, as the database it copies is.
            OwnerOnlyState.PrepareDatabase(destination);
            using SqliteConnection to = Open(destination, SqliteOpenMode.ReadWriteCreate);
            from.BackupDatabase(to);
            read.Commit();
            // The copied header still marks the source's write-ahead log; as a rollback-journal database the copy is one file, which a
            // later opening reads without creating anything beside it.
            using SqliteCommand mode = to.CreateCommand();
            mode.CommandText = "PRAGMA journal_mode = DELETE;";
            mode.ExecuteNonQuery();
        }
        catch (SqliteException exception)
        {
            throw held is null
                ? new SqliteExportException($"{name} could not be read for its export ({Cause(exception)})", exception) { Unreadable = true }
                : new SqliteExportException($"{name} could not be copied for its export ({Cause(exception)})", exception);
        }

        written?.Invoke(destination);
        Check(name, destination, held);
    }

    // Any of the files SQLite keeps beside a database it holds means a connection holds it, or did not close it.
    private static bool Held(string source) => Sidecars.Any(sidecar => File.Exists(source + sidecar));

    // An immutable file is read without a lock and without its -wal and -shm, which a database closed cleanly does not have. A network
    // share's path names a host, which SQLite's file names do not take, so it is read as a held database is.
    private static string Immutable(string source)
    {
        var uri = new Uri(source);
        return uri.IsUnc ? source : uri.AbsoluteUri + "?immutable=1";
    }

    private static void Check(string name, string copy, IReadOnlyDictionary<string, long> held)
    {
        if (!File.Exists(copy))
        {
            throw new SqliteExportException($"its export of {name} wrote no file");
        }

        try
        {
            using SqliteConnection connection = Open(copy, SqliteOpenMode.ReadOnly);
            using (SqliteCommand integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                string verdict = integrity.ExecuteScalar() as string ?? "no verdict";
                if (verdict != "ok")
                {
                    throw new SqliteExportException($"its export of {name} fails SQLite's integrity check ({Bounded(verdict)})");
                }
            }

            IReadOnlyDictionary<string, long> copied = Count(connection, null);
            string[] differences = [.. held.Keys.Union(copied.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Where(table => !held.TryGetValue(table, out long rows) || !copied.TryGetValue(table, out long kept) || rows != kept)
                .Select(table => !copied.TryGetValue(table, out long kept) ? $"no table {table}, where {name} holds {held[table]} rows"
                    : !held.TryGetValue(table, out long rows) ? $"a table {table} that {name} does not hold"
                    : $"{kept} rows in table {table}, where {name} holds {rows}")];
            if (differences.Length > 0)
            {
                throw new SqliteExportException($"its export of {name} holds {string.Join("; ", differences)}");
            }
        }
        catch (SqliteException exception)
        {
            throw new SqliteExportException($"its export of {name} is not a database SQLite reads ({Cause(exception)})", exception);
        }
    }

    // Every table's rows, by name. A virtual table, such as the search index's full-text tables, keeps its rows in ordinary shadow tables,
    // which are counted, and is read only through its module, so it is not counted itself.
    private static Dictionary<string, long> Count(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var tables = new List<string>();
        using (SqliteCommand list = connection.CreateCommand())
        {
            list.Transaction = transaction;
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND sql NOT LIKE 'CREATE VIRTUAL TABLE%' ORDER BY name;";
            using SqliteDataReader reader = list.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string table in tables)
        {
            using SqliteCommand count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            counts[table] = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static SqliteConnection Open(string dataSource, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dataSource, Mode = mode, Pooling = false }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string Cause(SqliteException exception) => Bounded(exception.Message.Trim().TrimEnd('.'));

    private static string Bounded(string text) => text.Length > 300 ? text[..300] + "..." : text;
}

/// <summary>From 0.9.5 (FIX-174): a database that could not be exported, or whose copy failed its check; the message says which and why.</summary>
internal sealed class SqliteExportException : IOException
{
    internal SqliteExportException(string message) : base(message) { }

    internal SqliteExportException(string message, Exception inner) : base(message, inner) { }

    /// <summary>True when the database itself could not be read, rather than its copy written or checked.</summary>
    internal bool Unreadable { get; init; }
}
