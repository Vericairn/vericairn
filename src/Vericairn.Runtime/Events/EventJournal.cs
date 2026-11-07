// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Events;

/// <summary>One entry as the journal keeps it: the envelope without its cursor, and what the audience check and filters read.</summary>
/// <param name="Position">Its position in this epoch, from 1.</param>
/// <param name="At">When the runtime observed the change.</param>
/// <param name="Key">The envelope's key.</param>
/// <param name="Kind">The event's kind.</param>
/// <param name="Change">The change it names.</param>
/// <param name="Subject">What changed.</param>
/// <param name="Sources">The records it rests on, at most 16.</param>
/// <param name="To">The principal concerned.</param>
/// <param name="Category">The attention category.</param>
/// <param name="Private">True for per-principal state, which reaches only <paramref name="To"/>.</param>
/// <param name="Related">Record ids a subjects filter matches: the subject, its sources and the goals they belong to. Never sent.</param>
internal sealed record JournalEntry(long Position, string At, string Key, string Kind, string Change, EventReference Subject,
    IReadOnlyList<EventReference> Sources, string? To, string? Category, bool Private, IReadOnlyList<string> Related);

/// <summary>A projection's changed items, as a derivation pass found them: those now derived, and those no longer derived.</summary>
internal sealed record ProjectionUpdate(string Projection, IReadOnlyDictionary<string, EventItem> Upserts, IReadOnlyCollection<string> Removed);

/// <summary>An event a derivation pass appends, before the journal gives it a position.</summary>
internal sealed record PendingEvent(string Key, string Kind, string Change, EventReference Subject, IReadOnlyList<EventReference> Sources,
    string? To, string? Category, bool Private, IReadOnlyList<string> Related);

/// <summary>
/// One runtime's bounded, ordered event journal, kept as owner-only machine state in a SQLite database beside the live store: at most
/// seven days and at most 100,000 events. Positions count from 1 within an epoch. A cursor seals the epoch and a position with a key
/// only this runtime holds, binds the reader's principal and reveals no count: each sealing draws a fresh nonce, so two cursors for
/// one position differ. A journal that is missing, unreadable, from another format, or was not closed cleanly (a crash, or a copy
/// restored from a running machine) starts a new epoch, and every older cursor then answers reset. The journal also keeps what the
/// derivation compares against: the records it has observed and each projection's last derived items.
/// </summary>
internal sealed class EventJournal : IDisposable
{
    internal const string FileName = "events.db";
    internal const int DefaultCapacity = 100_000;
    internal static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

    private const string JournalFormat = "vericairn.event-journal/1";
    private const byte CursorVersion = 1;
    private const int EpochBytes = 16;
    private const int NonceBytes = 16;
    private const int TagBytes = 16;
    private const int SealedBytes = EpochBytes + sizeof(long);
    private const int CursorBytes = 1 + NonceBytes + SealedBytes + TagBytes;

    private readonly object sync = new();
    private readonly SqliteConnection connection;
    private readonly TimeProvider time;
    private readonly int capacity;
    private readonly TimeSpan retention;
    private readonly byte[] epoch;
    private readonly byte[] encryption;
    private readonly byte[] authentication;
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, EventItem>> baselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> used = new(StringComparer.Ordinal);
    private TaskCompletionSource appended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long head;
    private long floor;
    private long pruned;
    private bool disposed;

    private EventJournal(SqliteConnection connection, TimeProvider time, int capacity, TimeSpan retention, byte[] epoch, byte[] secret, string path, bool fresh)
    {
        this.connection = connection;
        this.time = time;
        this.capacity = capacity;
        this.retention = retention;
        this.epoch = epoch;
        encryption = HMACSHA256.HashData(secret, "vericairn.event-cursor/encryption"u8);
        authentication = HMACSHA256.HashData(secret, "vericairn.event-cursor/authentication"u8);
        CryptographicOperations.ZeroMemory(secret);
        Path = path;
        Fresh = fresh;
    }

    /// <summary>The database file.</summary>
    public string Path { get; }

    /// <summary>True when this opening started a new epoch.</summary>
    public bool Fresh { get; }

    /// <summary>The epoch's identity, which no cursor of another epoch carries.</summary>
    public string Epoch => Convert.ToHexStringLower(epoch);

    /// <summary>The newest position, 0 before the first event.</summary>
    public long Head { get { lock (sync) return head; } }

    /// <summary>The oldest position kept; one past the head when nothing is kept.</summary>
    public long Floor { get { lock (sync) return floor; } }

    /// <summary>True once the derivation has taken its first, silent observation of the records and global projections.</summary>
    public bool Initialized { get; private set; }

    /// <summary>
    /// Opens the journal at a path, or starts a new epoch there. The file is created owner-only; the directory holding it is the live
    /// store's, which the daemon keeps owner-only too.
    /// </summary>
    public static EventJournal Open(string path, TimeProvider? time = null, int capacity = DefaultCapacity, TimeSpan? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        time ??= TimeProvider.System;
        try
        {
            return OpenExisting(path, time, capacity, retention ?? DefaultRetention) ?? Create(path, time, capacity, retention ?? DefaultRetention);
        }
        catch (SqliteException)
        {
            // A journal that cannot be read is lost: a new epoch starts, and every older cursor answers reset.
            Delete(path);
            return Create(path, time, capacity, retention ?? DefaultRetention);
        }
    }

    /// <summary>Seals a position for a principal: an opaque cursor that only this epoch opens, and only for that principal.</summary>
    public string Seal(long position, string principal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        Span<byte> cursor = stackalloc byte[CursorBytes];
        cursor[0] = CursorVersion;
        Span<byte> nonce = cursor.Slice(1, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        Span<byte> sealedPart = cursor.Slice(1 + NonceBytes, SealedBytes);
        epoch.CopyTo(sealedPart);
        BinaryPrimitives.WriteInt64BigEndian(sealedPart[EpochBytes..], position);
        Mask(nonce, sealedPart);
        Tag(cursor[..(1 + NonceBytes + SealedBytes)], principal, cursor[(1 + NonceBytes + SealedBytes)..]);
        return Base64Url.EncodeToString(cursor);
    }

    /// <summary>
    /// The position a cursor seals, when this epoch sealed it for this principal and it is still within the journal; false otherwise
    /// (malformed, foreign, another epoch, too old or ahead of the head), which a reader answers with reset and nothing else.
    /// </summary>
    public bool TryOpen(string? cursor, string principal, out long position)
    {
        position = 0;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 128)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != CursorBytes || bytes[0] != CursorVersion)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[TagBytes];
        Tag(bytes.AsSpan(0, 1 + NonceBytes + SealedBytes), principal, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(1 + NonceBytes + SealedBytes)))
        {
            return false;
        }

        Span<byte> sealedPart = bytes.AsSpan(1 + NonceBytes, SealedBytes);
        Mask(bytes.AsSpan(1, NonceBytes), sealedPart);
        if (!CryptographicOperations.FixedTimeEquals(sealedPart[..EpochBytes], epoch))
        {
            return false;
        }

        long opened = BinaryPrimitives.ReadInt64BigEndian(sealedPart[EpochBytes..]);
        lock (sync)
        {
            // Position p means everything up to p was read: the next is p + 1, which must still be kept.
            if (opened < floor - 1 || opened > head)
            {
                return false;
            }
        }

        position = opened;
        return true;
    }

    /// <summary>True when a position is still within the journal: nothing after it has been dropped.</summary>
    public bool Holds(long position)
    {
        lock (sync)
        {
            return position >= floor - 1 && position <= head;
        }
    }

    /// <summary>Completes once the journal holds an event after a position, or when cancelled.</summary>
    public Task WaitAsync(long after, CancellationToken cancellationToken)
    {
        Task signal;
        lock (sync)
        {
            if (head > after || disposed)
            {
                return Task.CompletedTask;
            }

            signal = appended.Task;
        }

        return signal.WaitAsync(cancellationToken);
    }

    /// <summary>The entries after a position, oldest first, at most <paramref name="max"/>.</summary>
    public IReadOnlyList<JournalEntry> After(long position, int max)
    {
        lock (sync)
        {
            if (disposed)
            {
                return [];
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT position, entry FROM events WHERE position > $after ORDER BY position LIMIT $max;";
            command.Parameters.AddWithValue("$after", position);
            command.Parameters.AddWithValue("$max", max);
            return Read(command);
        }
    }

    /// <summary>The entries with a key, newest first, at most <paramref name="max"/>.</summary>
    public IReadOnlyList<JournalEntry> WithKey(string key, int max)
    {
        lock (sync)
        {
            if (disposed)
            {
                return [];
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT position, entry FROM events WHERE key = $key ORDER BY position DESC LIMIT $max;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$max", max);
            return Read(command);
        }
    }

    /// <summary>Whether the derivation has observed a record before.</summary>
    public bool Observed(string id)
    {
        lock (sync)
        {
            return observed.Contains(id);
        }
    }

    /// <summary>A projection's last derived items, or null when the projection is not registered.</summary>
    public IReadOnlyDictionary<string, EventItem>? Baseline(string projection)
    {
        lock (sync)
        {
            return baselines.TryGetValue(projection, out Dictionary<string, EventItem>? items) ? new Dictionary<string, EventItem>(items, StringComparer.Ordinal) : null;
        }
    }

    /// <summary>The per-principal projections registered and when each was last used (Unix seconds).</summary>
    public IReadOnlyDictionary<string, long> Registrations()
    {
        lock (sync)
        {
            return new Dictionary<string, long>(used, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Appends a pass's events and keeps what it compared against, in one transaction: the records newly observed, each projection's
    /// changed items, the per-principal registrations used and those dropped. Then drops what the bound no longer keeps, and wakes every waiting reader
    /// when anything was appended. Returns the head after the pass.
    /// </summary>
    public long Commit(string at, IReadOnlyList<PendingEvent> events, IReadOnlyCollection<string> newlyObserved,
        IReadOnlyList<ProjectionUpdate> updates, IReadOnlyCollection<string> touched, IReadOnlyCollection<string> dropped, bool initialize = false)
    {
        TaskCompletionSource? wake = null;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long stamp = time.GetUtcNow().ToUnixTimeSeconds();

            // A pass that found nothing new writes nothing, beyond a registration's use once an hour and dropping by age once a minute.
            if (events.Count == 0 && newlyObserved.Count == 0 && updates.Count == 0 && dropped.Count == 0 && !initialize
                && touched.All(p => used.TryGetValue(p, out long at) && stamp - at < 3600) && stamp - pruned < 60)
            {
                return head;
            }

            pruned = stamp;
            using SqliteTransaction transaction = connection.BeginTransaction();
            long position = head;
            foreach (PendingEvent pending in events)
            {
                position++;
                var entry = new JournalEntry(position, at, pending.Key, pending.Kind, pending.Change, pending.Subject, pending.Sources, pending.To, pending.Category, pending.Private, pending.Related);
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO events (position, stamp, key, entry) VALUES ($position, $stamp, $key, $entry);";
                insert.Parameters.AddWithValue("$position", position);
                insert.Parameters.AddWithValue("$stamp", stamp);
                insert.Parameters.AddWithValue("$key", pending.Key);
                insert.Parameters.AddWithValue("$entry", JsonSerializer.Serialize(entry, ProtocolJson.Options));
                insert.ExecuteNonQuery();
            }

            foreach (string id in newlyObserved)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO observed (id) VALUES ($id);";
                insert.Parameters.AddWithValue("$id", id);
                insert.ExecuteNonQuery();
            }

            foreach (ProjectionUpdate update in updates)
            {
                foreach ((string item, EventItem value) in update.Upserts)
                {
                    Execute(transaction, "INSERT INTO baselines (projection, item, value) VALUES ($projection, $item, $value) ON CONFLICT(projection, item) DO UPDATE SET value = excluded.value;",
                        ("$projection", update.Projection), ("$item", item), ("$value", JsonSerializer.Serialize(value, ProtocolJson.Options)));
                }

                foreach (string item in update.Removed)
                {
                    Execute(transaction, "DELETE FROM baselines WHERE projection = $projection AND item = $item;", ("$projection", update.Projection), ("$item", item));
                }
            }

            foreach (string projection in touched)
            {
                Execute(transaction, "INSERT INTO projections (projection, used) VALUES ($projection, $used) ON CONFLICT(projection) DO UPDATE SET used = excluded.used;",
                    ("$projection", projection), ("$used", stamp));
            }

            foreach (string projection in dropped)
            {
                Execute(transaction, "DELETE FROM baselines WHERE projection = $projection;", ("$projection", projection));
                Execute(transaction, "DELETE FROM projections WHERE projection = $projection;", ("$projection", projection));
            }

            if (initialize)
            {
                SetMeta(transaction, "initialized", "1");
            }

            SetMeta(transaction, "head", position.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // Only ever a prefix is dropped, so the positions kept stay contiguous even if the clock steps back.
            Execute(transaction, "DELETE FROM events WHERE position <= MAX($bound, (SELECT COALESCE(MAX(position), 0) FROM events WHERE stamp < $oldest));",
                ("$bound", position - capacity), ("$oldest", stamp - (long)retention.TotalSeconds));
            transaction.Commit();

            // Memory follows only once the transaction holds.
            foreach (string id in newlyObserved)
            {
                observed.Add(id);
            }

            foreach (ProjectionUpdate update in updates)
            {
                if (!baselines.TryGetValue(update.Projection, out Dictionary<string, EventItem>? items))
                {
                    baselines[update.Projection] = items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
                }

                foreach ((string item, EventItem value) in update.Upserts)
                {
                    items[item] = value;
                }

                foreach (string item in update.Removed)
                {
                    items.Remove(item);
                }
            }

            foreach (string projection in touched)
            {
                used[projection] = stamp;
                baselines.TryAdd(projection, new Dictionary<string, EventItem>(StringComparer.Ordinal));
            }

            foreach (string projection in dropped)
            {
                baselines.Remove(projection);
                used.Remove(projection);
            }

            if (initialize)
            {
                Initialized = true;
            }

            bool grew = position > head;
            head = position;
            floor = MinimumPosition() ?? head + 1;
            if (grew)
            {
                wake = appended;
                appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        wake?.TrySetResult();
        return Head;
    }

    /// <summary>Closes the journal cleanly, so the next opening continues this epoch; wakes every reader so it can end.</summary>
    public void Dispose()
    {
        TaskCompletionSource wake;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                using SqliteTransaction transaction = connection.BeginTransaction();
                SetMeta(transaction, "clean", "1");
                transaction.Commit();
            }
            catch (SqliteException)
            {
                // Not closed cleanly: the next opening starts a new epoch, which is safe.
            }

            connection.Dispose();
            wake = appended;
        }

        wake.TrySetResult();
    }

    private static EventJournal? OpenExisting(string path, TimeProvider time, int capacity, TimeSpan retention)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        SqliteConnection connection = Connect(path);
        try
        {
            string? Meta(string key)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM meta WHERE key = $key;";
                command.Parameters.AddWithValue("$key", key);
                return command.ExecuteScalar() as string;
            }

            byte[]? epoch = Decode(Meta("epoch"), EpochBytes);
            byte[]? secret = Decode(Meta("secret"), 32);
            if (Meta("format") != JournalFormat || Meta("clean") != "1" || epoch is null || secret is null
                || !long.TryParse(Meta("head"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long head))
            {
                connection.Dispose();
                Delete(path);
                return null;
            }

            var journal = new EventJournal(connection, time, capacity, retention, epoch, secret, path, fresh: false) { Initialized = Meta("initialized") == "1" };
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                // Unclean from now until a clean close: a crash or a copy taken while running starts a new epoch.
                journal.SetMeta(transaction, "clean", "0");
                transaction.Commit();
            }

            journal.head = head;
            journal.Load();
            return journal;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static EventJournal Create(string path, TimeProvider time, int capacity, TimeSpan retention)
    {
        Delete(path);
        SqliteConnection connection = Connect(path);
        try
        {
            using (SqliteCommand create = connection.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    CREATE TABLE events (position INTEGER PRIMARY KEY, stamp INTEGER NOT NULL, key TEXT NOT NULL, entry TEXT NOT NULL);
                    CREATE INDEX events_key ON events (key);
                    CREATE TABLE observed (id TEXT PRIMARY KEY);
                    CREATE TABLE baselines (projection TEXT NOT NULL, item TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (projection, item));
                    CREATE TABLE projections (projection TEXT PRIMARY KEY, used INTEGER NOT NULL);
                    """;
                create.ExecuteNonQuery();
            }

            OwnerOnly(path);
            byte[] epoch = RandomNumberGenerator.GetBytes(EpochBytes);
            byte[] secret = RandomNumberGenerator.GetBytes(32);
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                foreach ((string key, string value) in new[] { ("format", JournalFormat), ("epoch", Base64Url.EncodeToString(epoch)), ("secret", Base64Url.EncodeToString(secret)), ("head", "0"), ("clean", "0"), ("initialized", "0") })
                {
                    using SqliteCommand insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value);";
                    insert.Parameters.AddWithValue("$key", key);
                    insert.Parameters.AddWithValue("$value", value);
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            return new EventJournal(connection, time, capacity, retention, epoch, secret, path, fresh: true) { head = 0, floor = 1 };
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void Load()
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM observed;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                observed.Add(reader.GetString(0));
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT projection, item, value FROM baselines;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (JsonSerializer.Deserialize<EventItem>(reader.GetString(2), ProtocolJson.Options) is not EventItem item)
                {
                    continue;
                }

                string projection = reader.GetString(0);
                if (!baselines.TryGetValue(projection, out Dictionary<string, EventItem>? items))
                {
                    baselines[projection] = items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
                }

                items[reader.GetString(1)] = item;
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT projection, used FROM projections;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                used[reader.GetString(0)] = reader.GetInt64(1);
                if (!baselines.ContainsKey(reader.GetString(0)))
                {
                    baselines[reader.GetString(0)] = new Dictionary<string, EventItem>(StringComparer.Ordinal);
                }
            }
        }

        floor = MinimumPosition() ?? head + 1;
    }

    private long? MinimumPosition()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(position) FROM events;";
        return command.ExecuteScalar() is long minimum ? minimum : null;
    }

    private static List<JournalEntry> Read(SqliteCommand command)
    {
        var entries = new List<JournalEntry>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (JsonSerializer.Deserialize<JournalEntry>(reader.GetString(1), ProtocolJson.Options) is JournalEntry entry)
            {
                entries.Add(entry with { Position = reader.GetInt64(0) });
            }
        }

        return entries;
    }

    private void SetMeta(SqliteTransaction transaction, string key, string value) =>
        Execute(transaction, "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", ("$key", key), ("$value", value));

    private void Execute(SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private void Mask(ReadOnlySpan<byte> nonce, Span<byte> sealedPart)
    {
        Span<byte> input = stackalloc byte[NonceBytes + 1];
        nonce.CopyTo(input);
        input[NonceBytes] = 0x01;
        Span<byte> stream = stackalloc byte[32];
        HMACSHA256.HashData(encryption, input, stream);
        for (int i = 0; i < sealedPart.Length; i++)
        {
            sealedPart[i] ^= stream[i];
        }
    }

    private void Tag(ReadOnlySpan<byte> sealedCursor, string principal, Span<byte> tag)
    {
        byte[] reader = Encoding.UTF8.GetBytes(principal);
        byte[] input = new byte[sealedCursor.Length + 1 + reader.Length];
        sealedCursor.CopyTo(input);
        input[sealedCursor.Length] = 0;
        reader.CopyTo(input, sealedCursor.Length + 1);
        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(authentication, input, full);
        full[..TagBytes].CopyTo(tag);
    }

    private static byte[]? Decode(string? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            byte[] bytes = Base64Url.DecodeFromChars(value);
            return bytes.Length == length ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand wal = connection.CreateCommand();
        // A crash starts a new epoch whatever the last transaction kept, so the journal does not wait on every commit's flush.
        wal.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        wal.ExecuteNonQuery();
        return connection;
    }

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void Delete(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
