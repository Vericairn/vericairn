// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.Data.Sqlite;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Objects;

/// <summary>Objects as rows of a SQLite database: a single file holding many small objects, verified on read.</summary>
public sealed class SqliteObjectStore : IObjectStore
{
    private readonly SqliteConnection connection;

    /// <summary>Opens (creating when needed) the database file.</summary>
    public SqliteObjectStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode = WAL;");
        Execute("CREATE TABLE IF NOT EXISTS objects (hash TEXT PRIMARY KEY, size INTEGER NOT NULL, bytes BLOB NOT NULL);");
    }

    /// <inheritdoc />
    public string Kind => "sqlite";

    /// <inheritdoc />
    public string Put(ReadOnlySpan<byte> bytes)
    {
        string hash = ContentHash.Sha256(bytes);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO objects (hash, size, bytes) VALUES ($hash, $size, $bytes);";
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$size", bytes.Length);
        command.Parameters.AddWithValue("$bytes", bytes.ToArray());
        command.ExecuteNonQuery();
        return hash;
    }

    /// <inheritdoc />
    public bool Has(string hash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM objects WHERE hash = $hash;";
        command.Parameters.AddWithValue("$hash", Check(hash));
        return command.ExecuteScalar() is not null;
    }

    /// <inheritdoc />
    public byte[]? Read(string hash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT bytes FROM objects WHERE hash = $hash;";
        command.Parameters.AddWithValue("$hash", Check(hash));
        if (command.ExecuteScalar() is not byte[] bytes)
        {
            return null;
        }

        string actual = ContentHash.Sha256(bytes);
        return actual == hash ? bytes : throw ObjectCorruptedException.For(hash, actual);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List()
    {
        var hashes = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT hash FROM objects ORDER BY hash;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            hashes.Add(reader.GetString(0));
        }

        return hashes;
    }

    /// <inheritdoc />
    public bool Remove(string hash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM objects WHERE hash = $hash;";
        command.Parameters.AddWithValue("$hash", Check(hash));
        return command.ExecuteNonQuery() > 0;
    }

    /// <inheritdoc />
    public void Dispose() => connection.Dispose();

    private static string Check(string hash) => ContentHash.IsValid(hash) ? hash : throw new ArgumentException("Not a content hash.", nameof(hash));

    private void Execute(string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
