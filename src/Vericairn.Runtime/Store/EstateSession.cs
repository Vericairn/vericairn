// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Objects;

namespace Vericairn.Runtime.Store;

/// <summary>
/// An opened estate on this machine: its root, its storage profile, its live store and its object store.
/// Under the repository-synced profile the record files in the repository are canonical and the live store is a
/// derived copy refreshed on every load; under the local-only profile the live store is canonical and no record
/// file exists in the root directory.
/// </summary>
public sealed class EstateSession : IDisposable
{
    private readonly RecordParseCache parseCache = new();
    private bool readOnly;
    private LiveStore.FileSnapshot? loadedFiles;
    // Held shared while writable, so a cache rebuild can see that this runtime has the estate open (FIX-93).
    private FileStream? writers;
    // Taken around each write, so one writer at a time writes the working tree, whatever its home (FIX-99).
    private WorkingTreeLock? writeLock;
    private string home = VericairnHome.Path();

    internal long ParsedFiles => parseCache.ParsedFiles;
    internal bool IsReadOnly => readOnly;

    // The response envelope must come from the very bytes whose model was just judged, not a second full store read/sync.
    internal RecordFile? LoadedFile(string path) => loadedFiles?.ByPath.GetValueOrDefault(path);

    private EstateSession(string root, EstateManifest manifest, LiveStore live, IObjectStore objects)
    {
        Root = root;
        Manifest = manifest;
        Live = live;
        Objects = objects;
    }

    /// <summary>Repository root (repository-synced) or estate directory holding the manifest (local-only).</summary>
    public string Root { get; }

    /// <summary>The manifest.</summary>
    public EstateManifest Manifest { get; }

    /// <summary>Storage profile.</summary>
    public string Profile => Manifest.Profile;

    /// <summary>The live store on this machine.</summary>
    public LiveStore Live { get; private set; }

    /// <summary>The object store: the repository's objects/ directory, or the live database.</summary>
    public IObjectStore Objects { get; private set; }

    // Only the deferred upgrade host calls this, after verification, migration staging and record judgment.
    // Ordinary read-only sessions are never promoted by their read operations.
    internal void EnableWrites()
    {
        if (!readOnly) return;
        EstateSession writable = Open(Root, false, home);
        Live.Dispose();
        Objects.Dispose();
        Live = writable.Live;
        Objects = writable.Objects;
        writers = writable.writers;
        writeLock = writable.writeLock;
        readOnly = false;
    }

    /// <summary>Where the live store of an estate opened from a root lives: under the per-user state, keyed by estate id and root path.</summary>
    public static string LiveStorePath(string root, string estateId, string profile) => EstateLocations.LiveStorePath(root, estateId, profile);

    /// <summary>Opens the estate at a root; the manifest decides the profile.</summary>
    public static EstateSession Open(string root) => Open(root, false);

    internal static EstateSession Open(string root, bool readOnly) => Open(root, readOnly, VericairnHome.Path());

    /// <summary>Opens the estate with its machine-local state under a given Vericairn home.</summary>
    internal static EstateSession Open(string root, bool readOnly, string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        root = Path.GetFullPath(root);
        home = Path.GetFullPath(home);
        var findings = new FindingList();
        EstateManifest? manifest = EstateManifest.Load(root, findings);
        if (manifest is null)
        {
            throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        }

        string databasePath = EstateLocations.LiveStorePath(root, manifest.Estate, manifest.Profile, home);
        // The writer lock is taken before the store opens, so no session opens a cache while it is being rebuilt.
        FileStream? writerLock = readOnly ? null : LiveStoreRecovery.ShareWriterLock(databasePath);
        LiveStore? live = null;
        try
        {
            live = new LiveStore(databasePath, readOnly, transient: readOnly && manifest.Profile == "repository-synced", manifest.Profile);
            if (!readOnly)
            {
                live.SetMeta("estate", manifest.Estate);
                live.SetMeta("profile", manifest.Profile);
                live.SetMeta("root", root);
            }

            IObjectStore objects = manifest.Profile == "local-only"
                ? new SqliteObjectStore(Path.Combine(Path.GetDirectoryName(live.Path)!, "objects.db"), readOnly)
                : new FileObjectStore(Path.Combine(root, EstateLayout.ObjectsDirectory));
            return new EstateSession(root, manifest, live, objects)
            {
                readOnly = readOnly, writers = writerLock, home = home,
                writeLock = readOnly ? null : WorkingTreeLock.For(root, manifest.Profile, databasePath, home),
            };
        }
        catch { live?.Dispose(); writerLock?.Dispose(); throw; }
    }

    // Submission checks read the canonical source without refreshing or touching the live cache.
    internal IReadOnlyList<RecordFile> ReadFiles() => Profile == "repository-synced" ? EstateLoader.EnumerateRecordFiles(Root) : Live.Files();

    internal EstateModel Read() => EstateLoader.Load(Root, ReadFiles(), ObjectReader);

    /// <summary>
    /// The estate's objects by content hash, for reading history checkpoint inventories: null when an object is absent, unreadable or not
    /// the bytes its hash names.
    /// </summary>
    internal Func<string, byte[]?> ObjectReader => Profile == "repository-synced" ? EstateLoader.FileObjects(Root) : hash =>
    {
        // The repository's objects are read as the loader reads them, refusing links and anything over the object limit; a local-only
        // store's objects are held in its database, under the same limit.
        try
        {
            byte[]? bytes = Objects.Read(hash);
            return bytes is null || bytes.LongLength > EstateLayout.MaxObjectBytes ? null : bytes;
        }
        catch (Exception exception) when (exception is ObjectCorruptedException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    };

    /// <summary>Loads and validates a given set of this estate's record files, with its objects.</summary>
    internal EstateModel LoadFiles(IReadOnlyList<RecordFile> files) => EstateLoader.Load(Root, files, ObjectReader);

    /// <summary>
    /// The estate's records as identities only, with the exact record files they were parsed from, from one snapshot: ids, hashes,
    /// bodies, supersession and the manifest, never a judgment (no validation finding, authority, signature or admission). Parsing
    /// reuses the session's parse cache; nothing judged is kept between calls. The files are not copied and must not be changed.
    /// </summary>
    internal (EstateModel Model, IReadOnlyDictionary<string, RecordFile> Files) LoadIdentities()
    {
        if (readOnly)
        {
            IReadOnlyList<RecordFile> files = ReadFiles();
            return (EstateLoader.LoadIdentities(Root, files, null), files.ToDictionary(f => f.Path, StringComparer.Ordinal));
        }

        if (Profile == "repository-synced") Live.SyncFromDirectory(Root);
        LiveStore.FileSnapshot snapshot = Live.Snapshot();
        if (!ReferenceEquals(snapshot, loadedFiles))
        {
            parseCache.Retain(snapshot.ByPath);
            loadedFiles = snapshot;
        }

        return (EstateLoader.LoadIdentities(Root, snapshot.Files, parseCache), snapshot.ByPath);
    }

    /// <summary>Loads and validates the estate. Repository-synced: refreshes the live cache unless opened read-only.</summary>
    public EstateModel Load()
    {
        if (readOnly) return Read();
        if (Profile == "repository-synced")
        {
            Live.SyncFromDirectory(Root);
        }

        LiveStore.FileSnapshot files = Live.Snapshot();
        if (!ReferenceEquals(files, loadedFiles))
        {
            parseCache.Retain(files.ByPath);
            loadedFiles = files;
        }
        EstateModel model = EstateLoader.Load(Root, files.Files, parseCache, objects: ObjectReader);
        Live.SetMeta("lastLoadedAt", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        return model;
    }

    /// <summary>
    /// The working tree's write lock (FIX-99), held by this session's writes; null for a read-only session. A runtime holds it from
    /// judging records against the tree's current files until they are stored.
    /// </summary>
    internal WorkingTreeLock? WriteLock => writeLock;

    /// <summary>
    /// Takes the working tree's write lock, or enters it again on the thread that holds it. Throws an <see cref="IOException"/> naming
    /// the holder when another writer keeps it past the wait.
    /// </summary>
    internal WorkingTreeLock.Hold HoldWriteLock() =>
        (writeLock ?? throw new InvalidOperationException("This session is read-only.")).Enter();

    /// <summary>
    /// Stores a new record where the profile keeps records; never overwrites. Returns the record's path. From 0.9.2 it stores under the
    /// working tree's write lock, taking it when the caller does not hold it, and throws an <see cref="IOException"/> naming the holder
    /// when another writer keeps it past the wait.
    /// </summary>
    public string Write(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (readOnly) throw new InvalidOperationException("This session is read-only.");
        using WorkingTreeLock.Hold hold = HoldWriteLock();
        if (Profile == "repository-synced")
        {
            RecordAuthor.Write(Root, record);
        }
        else
        {
            if (Live.Has(record.RelativePath))
            {
                throw new IOException($"{record.RelativePath} already exists; records are never overwritten.");
            }

            Live.Put(record.RelativePath, record.Bytes);
        }

        Written?.Invoke(record.Envelope.Id);
        return record.RelativePath;
    }

    /// <summary>Told the id of each record this session stores, once it is stored: what the event journal calls written here (0.9.2).</summary>
    internal Action<string>? Written { get; set; }

    /// <summary>Removes a record that was just written and turned out to make the estate invalid.</summary>
    public void Discard(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (readOnly) throw new InvalidOperationException("This session is read-only.");
        using WorkingTreeLock.Hold hold = HoldWriteLock();
        if (Profile == "repository-synced")
        {
            string full = Path.Combine(Root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }

        Live.Remove(record.RelativePath);
    }

    /// <summary>Every record file the estate holds, in path order (from the repository or the live store).</summary>
    public IReadOnlyList<RecordFile> Files()
    {
        if (readOnly) return ReadFiles();
        if (Profile == "repository-synced")
        {
            Live.SyncFromDirectory(Root);
        }

        return Live.Files();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Objects.Dispose();
        Live.Dispose();
        writeLock?.Dispose();
        writers?.Dispose();
    }
}
