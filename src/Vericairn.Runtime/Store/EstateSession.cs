// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Protocol;
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
    // The model a one-shot command's opening judged, kept while the records and the manifest stamp unchanged (FIX-148).
    private Kept? kept;
    // Raised by every drop, write and taking of the write lock, and read and compared with kept only under keptSync: a load keeps the
    // model it judged only while the generation it read before its stamp is unchanged, so a write on another thread while it judged
    // leaves nothing kept (FIX-148).
    private long generation;
    private readonly object keptSync = new();

    internal long ParsedFiles => parseCache.ParsedFiles;
    internal bool IsReadOnly => readOnly;

    /// <summary>The Vericairn home this session keeps its machine-local state under.</summary>
    internal string Home => home;

    // The response envelope must come from the very bytes whose model was just judged, not a second full store read/sync.
    internal RecordFile? LoadedFile(string path) => loadedFiles?.ByPath.GetValueOrDefault(path);

    private EstateSession(string root, EstateManifest manifest, LiveStore live, IObjectStore objects, JudgmentContext? judgments)
    {
        Root = root;
        Manifest = manifest;
        Live = live;
        Objects = new NotingObjectStore(objects, Drop);
        Judgments = judgments ?? new JudgmentContext();
    }

    /// <summary>
    /// From 0.9.4 (FIX-148): what this runtime has spent judging the estate (judgments, parses, verifications and key imports), counted
    /// where each happens, and the signature tables its judgments read: the process's, unless a test gave fresh ones.
    /// </summary>
    internal JudgmentContext Judgments { get; }

    /// <summary>
    /// From 0.9.4 (FIX-148; design/performance.md § 3.2): whether <see cref="Load"/> keeps the model it judged for later loads, as a
    /// one-shot command's runtime does, while the stamp of the records and the manifest (<see cref="Stamp"/>) is unchanged and nothing has
    /// been written here since. A load while this session holds the working tree's write lock is always judged afresh, whatever its
    /// caller, and taking the lock, storing or removing a record and storing or removing an object drop the kept model, including one a
    /// load on another thread was still judging when they happened. Long-lived hosts (the daemon, mcp serve, Workbench, a runtime
    /// supervising a worker run) leave it off and judge at every load.
    /// </summary>
    internal bool KeepsJudgment
    {
        get => keepsJudgment;
        set
        {
            keepsJudgment = value;
            if (!value) Drop();
        }
    }

    private bool keepsJudgment;

    /// <summary>
    /// Forgets the kept model and raises the generation, so the next load judges afresh and no load already judging keeps what it
    /// judged: anything this runtime writes to the working tree calls it.
    /// </summary>
    internal void Drop()
    {
        lock (keptSync)
        {
            generation++;
            kept = null;
        }
    }

    private long Generation()
    {
        lock (keptSync) return generation;
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
        EstateSession writable = Open(Root, false, home, Judgments);
        Live.Dispose();
        Objects.Dispose();
        Live = writable.Live;
        Objects = new NotingObjectStore(((NotingObjectStore)writable.Objects).Inner, Drop);
        Drop();
        writers = writable.writers;
        writeLock = writable.writeLock;
        readOnly = false;
    }

    /// <summary>
    /// From 0.9.5 (FIX-174): takes the estate's writer lock alone, as a cache rebuild does, so that while the hold lasts no other runtime on
    /// this machine has the estate open for writing, and none opens it: every writable session holds the lock shared for its lifetime. Null,
    /// and this session as it was, when another runtime holds the lock: a daemon, an MCP server or a command in flight. A writable session
    /// gives up its own share of the lock for the hold and takes it again when the hold ends, or here when the hold is refused; see
    /// <see cref="RetakeShare"/>.
    /// </summary>
    internal WritersHeldAlone? HoldWritersAlone()
    {
        // The lock's directory is made before the share is given up, so that only the two opens fall between giving it up and holding alone.
        OwnerOnlyState.CreateDirectory(Path.GetDirectoryName(LiveStoreRecovery.WriterLockPath(Live.Path))!, home);
        // A writable session holds a share for its lifetime and takes it back after the hold, even one that could not take it back after an
        // earlier hold.
        bool writable = !readOnly;
        FileStream? share = writers;
        writers = null;
        share?.Dispose();
        if (writable) WriterLockGap?.Invoke(Live.Path);
        FileStream? alone = LiveStoreRecovery.ExclusiveWriterLock(Live.Path, home);
        if (alone is null)
        {
            if (writable) RetakeShare();
            return null;
        }

        return new WritersHeldAlone(this, alone, writable);
    }

    /// <summary>From 0.9.5 (FIX-174): how long this writable session keeps trying to take its share of the writer lock back after a hold.</summary>
    internal TimeSpan RetakeFor { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// For tests: called with the live store's path while a writable session holds no share of the writer lock, between giving it up and
    /// holding the lock alone, and between releasing it and taking its share back.
    /// </summary>
    internal static Action<string>? WriterLockGap { get; set; }

    /// <summary>For tests: called with the live store's path each time taking the share back is refused, before the next try.</summary>
    internal static Action<string>? RetakeRefused { get; set; }

    /// <summary>
    /// From 0.9.5 (FIX-174): takes this writable session's share of the writer lock back after it gave it up for a hold. Another command can
    /// take the lock alone in the moment between, such as a cache rebuild or another upgrade's restore point; the session then tries again
    /// until that command releases it, for at most <see cref="RetakeFor"/>, since a writable session without its share would let a rebuild
    /// or another upgrade go ahead as if the estate were closed. Throws an <see cref="IOException"/> with
    /// <see cref="LiveStoreRecovery.ShareNotRetaken"/> when the share cannot be taken back.
    /// </summary>
    private void RetakeShare()
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                writers = LiveStoreRecovery.ShareWriterLock(Live.Path);
                return;
            }
            catch (IOException exception) when (exception.Message == LiveStoreRecovery.WriterLockHeldAlone)
            {
                if (waited.Elapsed >= RetakeFor) throw new IOException(LiveStoreRecovery.ShareNotRetaken, exception);
                RetakeRefused?.Invoke(Live.Path);
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>From 0.9.5 (FIX-174): the estate's writer lock held alone by <see cref="HoldWritersAlone"/>, released on disposal.</summary>
    internal sealed class WritersHeldAlone : IDisposable
    {
        private readonly EstateSession session;
        private readonly bool shared;
        private FileStream? alone;

        internal WritersHeldAlone(EstateSession session, FileStream alone, bool shared)
        {
            this.session = session;
            this.alone = alone;
            this.shared = shared;
        }

        /// <summary>
        /// Releases the lock and, for a writable session, takes its share again, waiting while another command that took the lock alone in
        /// between still holds it; throws when the share cannot be taken back (<see cref="RetakeShare"/>).
        /// </summary>
        public void Dispose()
        {
            if (alone is null) return;
            alone.Dispose();
            alone = null;
            if (!shared) return;
            WriterLockGap?.Invoke(session.Live.Path);
            session.RetakeShare();
        }
    }

    /// <summary>Where the live store of an estate opened from a root lives: under the per-user state, keyed by estate id and root path.</summary>
    public static string LiveStorePath(string root, string estateId, string profile) => EstateLocations.LiveStorePath(root, estateId, profile);

    /// <summary>Opens the estate at a root; the manifest decides the profile.</summary>
    public static EstateSession Open(string root) => Open(root, false);

    internal static EstateSession Open(string root, bool readOnly) => Open(root, readOnly, VericairnHome.Path());

    /// <summary>Opens the estate with its machine-local state under a given Vericairn home.</summary>
    internal static EstateSession Open(string root, bool readOnly, string home) => Open(root, readOnly, home, null);

    /// <summary>From 0.9.4 (FIX-148): opens the estate counting its judgments in a given context, such as a test's with fresh signature tables.</summary>
    internal static EstateSession Open(string root, bool readOnly, string home, JudgmentContext? judgments)
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
        // The home and every directory down to the live store's are owner-only whichever process opens the estate first, a daemon or a
        // command without one. A read-only opening creates and changes nothing here.
        if (!readOnly) OwnerOnlyState.CreateDirectory(Path.GetDirectoryName(databasePath)!, home);
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
            return new EstateSession(root, manifest, live, objects, judgments)
            {
                readOnly = readOnly, writers = writerLock, home = home,
                writeLock = readOnly ? null : WorkingTreeLock.For(root, manifest.Profile, databasePath, home),
            };
        }
        catch { live?.Dispose(); writerLock?.Dispose(); throw; }
    }

    // Submission checks read the canonical source without refreshing or touching the live cache.
    internal IReadOnlyList<RecordFile> ReadFiles() => Profile == "repository-synced" ? EstateLoader.EnumerateRecordFiles(Root) : Live.Files();

    internal EstateModel Read() => EstateLoader.Load(Root, ReadFiles(), null, objects: ObjectReader, context: Judgments);

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
    internal EstateModel LoadFiles(IReadOnlyList<RecordFile> files) => EstateLoader.Load(Root, files, null, objects: ObjectReader, context: Judgments);

    /// <summary>A file set judged with every record's roles and grants read at its own time, for adoption's check alone (design/signed-history.md § 10).</summary>
    internal EstateModel LoadFilesAtRecordTime(IReadOnlyList<RecordFile> files) => EstateLoader.LoadAtRecordTime(Root, files, ObjectReader, Judgments);

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
            return (EstateLoader.LoadIdentities(Root, files, null, Judgments), files.ToDictionary(f => f.Path, StringComparer.Ordinal));
        }

        if (Profile == "repository-synced") Live.SyncFromDirectory(Root);
        LiveStore.FileSnapshot snapshot = Live.Snapshot();
        if (!ReferenceEquals(snapshot, loadedFiles))
        {
            parseCache.Retain(snapshot.ByPath);
            loadedFiles = snapshot;
        }

        return (EstateLoader.LoadIdentities(Root, snapshot.Files, parseCache, Judgments), snapshot.ByPath);
    }

    /// <summary>
    /// A cheap account of the canonical records and the manifest as they are now: equal stamps mean that records read when the stamp was
    /// taken are still the canonical ones. Under the local-only profile it is the live store's snapshot, which a write here and another
    /// connection's commit replace, so the caller holds the lock this session is used under; under the repository-synced profile it is
    /// <see cref="TreeStamp"/>. Neither refreshes the live cache or its metadata.
    /// </summary>
    internal CanonicalStamp Stamp() => Profile == "repository-synced" ? TreeStamp() : new(Live.Snapshot().Generation, string.Empty, FileStamp(ManifestPath));

    /// <summary>
    /// The repository-synced profile's stamp, taken from the working tree alone, so without the live store or its lock: every record
    /// file's path, size and modification time, as the live store's sync compares them, and the manifest's. A tree changing while it is
    /// stamped gives a stamp equal to no other.
    /// </summary>
    internal CanonicalStamp TreeStamp() => new(0, FilesStamp(Path.Combine(Root, EstateLayout.RecordsDirectory)), FileStamp(ManifestPath));

    /// <summary>The canonical record files as they are now, read as <see cref="Read"/> reads them; the caller holds this session's lock.</summary>
    internal IReadOnlyList<RecordFile> CanonicalFiles() => Profile == "repository-synced" ? EstateLoader.EnumerateRecordFiles(Root) : Live.Snapshot().Files;

    /// <summary>What <see cref="Stamp"/> gives: the live store's snapshot generation, or a digest of the record files, and the manifest's size and time.</summary>
    internal readonly record struct CanonicalStamp(long Snapshot, string Files, string Manifest);

    private string ManifestPath => Path.Combine(Root, EstateLayout.ManifestPath);

    private static string FileStamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent";
    }

    private static string FilesStamp(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return "absent";
            var files = new List<(string Path, long Length, long Written)>();
            foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                files.Add((file.FullName, file.Length, file.LastWriteTimeUtc.Ticks));
            }

            files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> numbers = stackalloc byte[17];
            foreach ((string path, long length, long written) in files)
            {
                digest.AppendData(Encoding.UTF8.GetBytes(path));
                numbers[0] = 0;
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(numbers[1..], length);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(numbers[9..], written);
                digest.AppendData(numbers);
            }

            return Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file removed or replaced while the tree was walked: the records are changing, so this stamp matches nothing.
            return "changing:" + Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// How many times <see cref="Load"/> has been called on this session; from 0.9.4 a one-shot command's load may return the kept model,
    /// so the judgments themselves are counted in <see cref="Judgments"/>.
    /// </summary>
    internal long Loads { get; private set; }

    /// <summary>
    /// Loads and validates the estate. Repository-synced: refreshes the live cache unless opened read-only. From 0.9.4 (FIX-148) a
    /// session that keeps its judgment (<see cref="KeepsJudgment"/>) returns the model it kept while the stamp is unchanged, nothing was
    /// written here since and it does not hold the write lock; under the lock every load is judged afresh. A load keeps what it judged
    /// only if nothing was written, dropped or locked here between its stamp and its keeping, from any thread.
    /// </summary>
    public EstateModel Load() => LoadWithFiles().Model;

    /// <summary>
    /// From 0.9.4 (FIX-148): the model <see cref="Load"/> gives and the exact record files it was judged from, one snapshot, so a read that
    /// needs both parses nothing again. The files are not copied and must not be changed.
    /// </summary>
    internal (EstateModel Model, IReadOnlyList<RecordFile> Files) LoadWithFiles()
    {
        Loads++;
        if (readOnly) return ReadOnlyJudgment();

        // Read before the lock is asked of: a writer that takes the lock after this raises the generation once it holds it, so either
        // this load finds the lock held or its keeping finds the generation changed.
        long started = Generation();
        // The lock is asked of this session itself, so a load under it is fresh from any caller (design/performance.md § 3.2).
        bool keeping = keepsJudgment && writeLock?.Entered != true;
        CanonicalStamp stamp = default;
        if (keeping)
        {
            // Taken before the records are read: a change while they are read gives a stamp no later load matches.
            stamp = Stamp();
            Kept? reused = null;
            lock (keptSync)
            {
                if (kept is { } held && held.Stamp == stamp && held.Generation == started) reused = held;
            }

            if (reused is not null)
            {
                loadedFiles = reused.Files;
                Loaded?.Invoke(reused.Model);
                return (reused.Model, reused.Files.Files);
            }
        }

        lock (keptSync) kept = null;
        if (writeLock?.Entered == true) LockedJudgments++;
        // What a client operation stopped at its request limit names as being judged (FIX-152).
        using IDisposable stage = ClientOperationActivity.Stage(ReadingStage);
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
        ClientOperationActivity.Report(JudgingStage(files.Files.Count));
        EstateModel model = EstateLoader.Load(Root, files.Files, parseCache, objects: ObjectReader, context: Judgments);
        Live.SetMeta("lastLoadedAt", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        if (keeping)
        {
            lock (keptSync)
            {
                // Kept only if nothing wrote, dropped or took the lock here since the generation was read: a write by another thread while
                // this load judged leaves the model unkept, so the next load judges afresh (FIX-148).
                if (generation == started && writeLock?.Entered != true) kept = new Kept(stamp, started, files, model);
            }
        }

        Judged?.Invoke(model);
        Loaded?.Invoke(model);
        return (model, files.Files);
    }

    /// <summary>
    /// From 0.9.4 (FIX-152): a judgment of the estate made now, as an unkept <see cref="Load"/> makes it, through the session's parse cache,
    /// and the exact files it was judged from; it neither returns nor replaces the kept model, so its caller may add to its findings.
    /// The files are not copied and must not be changed.
    /// </summary>
    internal (EstateModel Model, IReadOnlyList<RecordFile> Files) JudgeAfresh()
    {
        if (readOnly) return ReadOnlyJudgment();
        using IDisposable stage = ClientOperationActivity.Stage(ReadingStage);
        if (Profile == "repository-synced") Live.SyncFromDirectory(Root);
        LiveStore.FileSnapshot files = Live.Snapshot();
        if (!ReferenceEquals(files, loadedFiles))
        {
            parseCache.Retain(files.ByPath);
            loadedFiles = files;
            Drop();
        }

        ClientOperationActivity.Report(JudgingStage(files.Files.Count));
        return (EstateLoader.Load(Root, files.Files, parseCache, objects: ObjectReader, context: Judgments), files.Files);
    }

    private (EstateModel Model, IReadOnlyList<RecordFile> Files) ReadOnlyJudgment()
    {
        using IDisposable stage = ClientOperationActivity.Stage(ReadingStage);
        IReadOnlyList<RecordFile> read = ReadFiles();
        ClientOperationActivity.Report(JudgingStage(read.Count));
        return (EstateLoader.Load(Root, read, null, objects: ObjectReader, context: Judgments), read);
    }

    // The stages of a judgment as a client operation stopped at its request limit names them (FIX-152).
    private const string ReadingStage = "judging the estate: reading its record files";

    private static string JudgingStage(int files) =>
        string.Create(CultureInfo.InvariantCulture, $"judging the estate: verifying and validating its {files:N0} record files");

    /// <summary>Told each model <see cref="Load"/> judges, once it is judged: what cost tests count the passes over (FIX-136).</summary>
    internal Action<EstateModel>? Judged { get; set; }

    /// <summary>From 0.9.4 (FIX-148): told each model <see cref="Load"/> returns, judged now or kept.</summary>
    internal Action<EstateModel>? Loaded { get; set; }

    /// <summary>From 0.9.4 (FIX-148): how many of this session's loads judged afresh while it held the write lock, as every such load does.</summary>
    internal long LockedJudgments { get; private set; }

    private sealed record Kept(CanonicalStamp Stamp, long Generation, LiveStore.FileSnapshot Files, EstateModel Model);

    /// <summary>
    /// The working tree's write lock (FIX-99), held by this session's writes; null for a read-only session. A runtime holds it from
    /// judging records against the tree's current files until they are stored.
    /// </summary>
    internal WorkingTreeLock? WriteLock => writeLock;

    /// <summary>
    /// Takes the working tree's write lock, or enters it again on the thread that holds it. Throws an <see cref="IOException"/> naming
    /// the holder when another writer keeps it past the wait.
    /// </summary>
    internal WorkingTreeLock.Hold HoldWriteLock()
    {
        WorkingTreeLock held = writeLock ?? throw new InvalidOperationException("This session is read-only.");
        // A writer's judgment reads the tree afresh, and whatever it writes leaves no kept model behind (FIX-148): the drop once the lock is
        // held raises the generation after the lock shows as entered, so a load on another thread that found it free keeps nothing.
        Drop();
        WorkingTreeLock.Hold hold = held.Enter();
        Drop();
        return hold;
    }

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
        Writes++;
        Drop();
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

    /// <summary>
    /// How many times this session has begun to store or remove a record since it opened: while it is unchanged, nothing this session did
    /// has made a model it loaded out of date.
    /// </summary>
    internal long Writes { get; private set; }

    /// <summary>Told the id of each record this session stores, once it is stored: what the event journal calls written here (0.9.2).</summary>
    internal Action<string>? Written { get; set; }

    /// <summary>Removes a record that was just written and turned out to make the estate invalid.</summary>
    public void Discard(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (readOnly) throw new InvalidOperationException("This session is read-only.");
        using WorkingTreeLock.Hold hold = HoldWriteLock();
        Writes++;
        Drop();
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

    /// <summary>Whether a record's file is stored where the profile keeps records: the working tree, or the live store.</summary>
    internal bool Holds(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Profile == "repository-synced"
            ? File.Exists(Path.Combine(Root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            : Live.Has(record.RelativePath);
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

    /// <summary>The object store as the session hands it out: storing or removing an object drops the kept model (FIX-148).</summary>
    private sealed class NotingObjectStore(IObjectStore inner, Action wrote) : IObjectStore, IBoundedObjectReads
    {
        internal IObjectStore Inner => inner;

        public string Kind => inner.Kind;

        public string Put(ReadOnlySpan<byte> bytes)
        {
            wrote();
            try { return inner.Put(bytes); }
            finally { wrote(); }
        }

        public bool Has(string hash) => inner.Has(hash);

        public byte[]? Read(string hash) => inner.Read(hash);

        public IReadOnlyList<string> List() => inner.List();

        public bool Remove(string hash)
        {
            wrote();
            try { return inner.Remove(hash); }
            finally { wrote(); }
        }

        public void Dispose() => inner.Dispose();

        // The stores this session opens read bounded; another store is read whole, once, as it always was.
        public long? Length(string hash) => inner is IBoundedObjectReads bounded ? bounded.Length(hash) : inner.Read(hash)?.LongLength;

        public bool ReadExactly(string hash, Span<byte> destination)
        {
            if (inner is IBoundedObjectReads bounded) return bounded.ReadExactly(hash, destination);
            byte[]? bytes = inner.Read(hash);
            if (bytes is null || bytes.Length != destination.Length) return false;
            bytes.CopyTo(destination);
            return true;
        }
    }
}
