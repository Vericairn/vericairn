// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Store;

/// <summary>A writer that could not take the working tree's write lock in time; the message names the holder as it recorded itself.</summary>
internal sealed class WorkingTreeLockException : IOException
{
    internal WorkingTreeLockException(string message) : base(message)
    {
    }
}

/// <summary>
/// One writer at a time per working tree (FIX-99). Every writable session, whatever its Vericairn home, takes this exclusive lock in
/// the working tree's own <c>.vericairn</c> directory around judging and storing records, so a second writer judges against the
/// records the first wrote. A local-only estate keeps its records in the live store under the home, so its lock is beside that store. It is held by an open handle shared with no one, which the operating system releases when the process
/// ends, so a crash leaves no stale lock. A waiting runtime cannot open a file held this way on every platform, so the holder
/// records itself beside the lock: process id, host, a digest of its home and the time it took the lock, never a path. It is
/// reentrant on the thread that holds it, and it is separate from the per-home <c>live.lock</c>, which guards a cache rebuild.
/// </summary>
internal sealed class WorkingTreeLock : IDisposable
{
    internal const string DirectoryName = "write-lock";
    internal const string LockName = "write.lock";
    internal const string HolderName = "holder.json";

    /// <summary>How long a writer waits for another writer before it is refused.</summary>
    internal static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim threads = new(1, 1);
    private readonly object sync = new();
    private readonly string directory;
    private readonly string homeId;
    private FileStream? handle;
    private int owner;
    private int depth;

    private WorkingTreeLock(string directory, string home)
    {
        this.directory = directory;
        homeId = HomeId(home);
    }

    /// <summary>
    /// The lock of the store that holds an estate's records: a repository-synced estate's working tree, in its <c>.vericairn</c>
    /// directory; a local-only estate's live store, whose directory under the home holds the records, beside it.
    /// </summary>
    internal static WorkingTreeLock For(string root, string profile, string liveStorePath, string home) => new(profile == "local-only"
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(liveStorePath))!, DirectoryName)
        : Path.Combine(Path.GetFullPath(root), ".vericairn", DirectoryName), home);

    /// <summary>How long this writer waits; tests shorten it.</summary>
    internal TimeSpan Wait { get; set; } = DefaultWait;

    /// <summary>A test hook, called once when the lock is found held and this writer starts waiting.</summary>
    internal Action? Waiting { get; set; }

    /// <summary>Whether the calling thread holds the lock.</summary>
    internal bool Held
    {
        get { lock (sync) return depth > 0 && owner == Environment.CurrentManagedThreadId; }
    }

    internal string LockPath => Path.Combine(directory, LockName);

    internal string HolderPath => Path.Combine(directory, HolderName);

    /// <summary>The digest by which a holder names its home: the first 16 hex digits of the SHA-256 of the home's full path.</summary>
    internal static string HomeId(string home) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(home))))[..16];

    /// <summary>Takes the lock, or enters it again on the thread that holds it; throws <see cref="WorkingTreeLockException"/> when it stays held past the wait.</summary>
    internal Hold Enter()
    {
        int thread = Environment.CurrentManagedThreadId;
        lock (sync)
        {
            if (depth > 0 && owner == thread)
            {
                depth++;
                return new Hold(this);
            }
        }

        DateTime deadline = DateTime.UtcNow + Wait;
        bool waited = false;
        if (!threads.Wait(0))
        {
            Signal(ref waited);
            if (!threads.Wait(Remaining(deadline))) throw Busy();
        }

        try
        {
            FileStream stream = OpenExclusive(deadline, ref waited);
            lock (sync)
            {
                handle = stream;
                owner = thread;
                depth = 1;
            }

            Publish();
            return new Hold(this);
        }
        catch
        {
            threads.Release();
            throw;
        }
    }

    private FileStream OpenExclusive(DateTime deadline, ref bool waited)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // The directory ignores itself even in estates whose root .gitignore does not yet name .vericairn/.
            string ignore = Path.Combine(directory, ".gitignore");
            if (!File.Exists(ignore))
            {
                try
                {
                    using var stream = new FileStream(ignore, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    stream.Write("*\n"u8);
                }
                catch (IOException) when (File.Exists(ignore))
                {
                    // Another writer created it at the same moment, with the same content; on Windows its open handle is a sharing violation.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }

        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
                throw Unavailable(exception);
            }
            catch (IOException)
            {
                // Held by another writer: a sharing violation on Windows, an exclusive advisory lock elsewhere.
                Signal(ref waited);
                TimeSpan remaining = Remaining(deadline);
                if (remaining <= TimeSpan.Zero) throw Busy();
                Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private void Signal(ref bool waited)
    {
        if (waited) return;
        waited = true;
        Waiting?.Invoke();
    }

    private static TimeSpan Remaining(DateTime deadline)
    {
        TimeSpan remaining = deadline - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // The holder's description is diagnosis, not the lock: a failure to publish or read it never changes who holds the lock.
    private void Publish()
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "vericairn.write-lock/1",
            ["pid"] = Environment.ProcessId,
            ["host"] = Environment.MachineName,
            ["home"] = homeId,
            ["since"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow),
        });
        string temporary = Path.Combine(directory, Ulid.NewUlid().Value + ".tmp");
        try
        {
            File.WriteAllText(temporary, json + "\n");
            File.Move(temporary, HolderPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private static WorkingTreeLockException Unavailable(Exception exception) =>
        new($"Not written: this working tree's write lock could not be taken ({exception.Message.Trim().TrimEnd('.')}). Make its .vericairn directory writable, then try again.");

    private WorkingTreeLockException Busy()
    {
        string holder = "its holder did not record itself";
        try
        {
            using var stream = new FileStream(HolderPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out JsonElement value) ? value.ToString() : null;
            holder = string.Create(CultureInfo.InvariantCulture,
                $"held by process {Text("pid") ?? "unknown"} on host {Text("host") ?? "unknown"}, Vericairn home {Text("home") ?? "unknown"}, since {Text("since") ?? "an unknown time"}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
        }

        return new WorkingTreeLockException($"Not written: another writer has this working tree's write lock ({holder}) and did not release it within {Wait.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds. One writer at a time writes a working tree: let that command finish, or stop the daemon that serves this tree, then try again.");
    }

    private void Exit()
    {
        FileStream? released = null;
        lock (sync)
        {
            if (depth == 0) return;
            if (--depth > 0) return;
            released = handle;
            handle = null;
            owner = 0;
        }

        try { File.Delete(HolderPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        released?.Dispose();
        try { threads.Release(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Releases a handle still held when its session closes.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            handle?.Dispose();
            handle = null;
            depth = 0;
            owner = 0;
        }

        threads.Dispose();
    }

    /// <summary>One entry into the lock; disposing it leaves that entry, and the last one releases the lock.</summary>
    internal sealed class Hold(WorkingTreeLock owner) : IDisposable
    {
        private WorkingTreeLock? lockOwner = owner;

        public void Dispose() => Interlocked.Exchange(ref lockOwner, null)?.Exit();
    }
}
