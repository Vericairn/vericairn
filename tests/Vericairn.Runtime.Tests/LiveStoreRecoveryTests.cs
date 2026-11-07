// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>FIX-93 (estate report #298): a malformed live store is diagnosed by profile, and only a derived cache is rebuilt.</summary>
public sealed class LiveStoreRecoveryTests
{
    [Fact]
    public void AMalformedRepositorySyncedCacheIsDiagnosedAndRebuiltFromTheRecordFiles()
    {
        using var estate = new TestEstate();
        string database = LiveStoreDamage.Cache(estate.Root);
        using (EstateService.Open(estate.Root)) { }
        Dictionary<string, string> canonical = RecordHashes(estate.Root);
        LiveStoreDamage.Malform(database);

        // Opening reports a bounded diagnostic naming the profile and the route, never the SQLite exception itself.
        LiveStoreException failure = Assert.Throws<LiveStoreException>(() => EstateService.Open(estate.Root));
        Assert.Equal(11, failure.SqliteError);
        Assert.Equal("repository-synced", failure.Profile);
        Assert.Contains("repository-synced", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SQLite Error 11", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'vericairn estate cache rebuild'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(database, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", failure.Message, StringComparison.Ordinal);
        Assert.Throws<LiveStoreException>(() => { using EstateSession session = EstateSession.Open(estate.Root); session.Load(); });
        Assert.Equal(canonical, RecordHashes(estate.Root));

        // The failed database and its sidecars move together, unchanged, and the cache is read back from the record files.
        File.WriteAllBytes(database + "-wal", [1, 2, 3]);
        File.WriteAllBytes(database + "-shm", [4, 5, 6]);
        Dictionary<string, byte[]> failed = Sidecars.ToDictionary(s => "live.db" + s, s => File.ReadAllBytes(database + s));
        LiveStoreRebuilt rebuilt = LiveStoreRecovery.Rebuild(estate.Root, new FixedClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(database)!, "failed-cache", "20261106T120000Z"), rebuilt.Retained);
        Assert.Equal(["live.db", "live.db-wal", "live.db-shm"], rebuilt.RetainedFiles);
        foreach ((string name, byte[] bytes) in failed) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(rebuilt.Retained!, name)));
        Assert.Equal(database, rebuilt.Cache);
        Assert.Equal(canonical.Count, rebuilt.Records);
        Assert.Equal(canonical, RecordHashes(estate.Root));
        using (EstateService reopened = EstateService.Open(estate.Root))
        {
            EstateModel model = reopened.Session.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal(canonical.Count, model.Records.Count);
        }

        // A second rebuild at the same second keeps the first one's files and adds its own directory.
        LiveStoreRebuilt again = LiveStoreRecovery.Rebuild(estate.Root, new FixedClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(rebuilt.Retained + "-2", again.Retained);
        foreach ((string name, byte[] bytes) in failed) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(rebuilt.Retained!, name)));
    }

    [Fact]
    public void ARebuildIsRefusedWhileTheEstateIsOpenAndHoldsOffNewSessionsWhileItRuns()
    {
        using var estate = new TestEstate();
        string database = LiveStoreDamage.Cache(estate.Root);
        string retained = Path.Combine(Path.GetDirectoryName(database)!, LiveStoreRecovery.RetainedDirectoryName);
        using (EstateService.Open(estate.Root))
        {
            IOException refused = Assert.Throws<IOException>(() => LiveStoreRecovery.Rebuild(estate.Root));
            Assert.Equal(LiveStoreRecovery.WriterPresent, refused.Message);
            Assert.True(File.Exists(database));
            Assert.False(Directory.Exists(retained));
        }

        // While a rebuild holds the writer lock, no session opens the cache it is replacing.
        using (new FileStream(LiveStoreRecovery.WriterLockPath(database), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            IOException held = Assert.Throws<IOException>(() => EstateService.Open(estate.Root));
            Assert.Contains("being rebuilt", held.Message, StringComparison.Ordinal);
        }

        // A read-only check takes no writer lock and reads the repository's files, not the cache.
        using (new FileStream(LiveStoreRecovery.WriterLockPath(database), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (EstateService check = EstateService.OpenReadOnly(estate.Root))
            Assert.False(check.Session.Load().Findings.HasErrors);

        LiveStoreRebuilt rebuilt = LiveStoreRecovery.Rebuild(estate.Root);
        Assert.Equal(["live.db"], rebuilt.RetainedFiles);
    }

    [Fact]
    public void ALocalOnlyEstatesCanonicalDatabaseIsNeverMovedOrRebuilt()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-local-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(root);
        try
        {
            var initialization = new EstateInitialization("Local", "local", "Local-only profile.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), "local-only");
            PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
            string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllBytes(manifestPath, prepared.ManifestBytes);
            string database;
            using (EstateSession session = EstateSession.Open(root))
            {
                session.Write(prepared.Operator);
                session.Write(prepared.Estate);
                Assert.False(session.Load().Findings.HasErrors);
                database = session.Live.Path;
            }
            LiveStoreDamage.Malform(database);

            LiveStoreException failure = Assert.Throws<LiveStoreException>(() => EstateService.Open(root));
            Assert.Equal("local-only", failure.Profile);
            Assert.Contains("local-only", failure.Message, StringComparison.Ordinal);
            Assert.Contains("canonical copy, not a cache", failure.Message, StringComparison.Ordinal);
            Assert.Contains("'vericairn estate export'", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("cache rebuild", failure.Message, StringComparison.Ordinal);

            string[] files = Directory.EnumerateFiles(Path.GetDirectoryName(database)!).Order(StringComparer.Ordinal).ToArray();
            Dictionary<string, byte[]> before = files.ToDictionary(f => f, File.ReadAllBytes);
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => LiveStoreRecovery.Rebuild(root));
            Assert.Contains("local-only", refused.Message, StringComparison.Ordinal);
            Assert.Contains("never moved, deleted or rebuilt", refused.Message, StringComparison.Ordinal);
            Assert.Equal(files, Directory.EnumerateFiles(Path.GetDirectoryName(database)!).Order(StringComparer.Ordinal).ToArray());
            foreach ((string file, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(database)!, LiveStoreRecovery.RetainedDirectoryName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(5, "repository-synced", "Another runtime on this machine holds this estate's live store")]
    [InlineData(6, "local-only", "Another runtime on this machine holds this estate's live store")]
    [InlineData(26, "repository-synced", "'vericairn estate cache rebuild'")]
    [InlineData(26, "local-only", "canonical copy, not a cache")]
    [InlineData(11, null, "The live store could not be used")]
    public void TheDiagnosticFollowsTheFailureAndTheProfile(int code, string? profile, string route)
    {
        string database = Path.Combine(Path.GetTempPath(), "vericairn-diagnostic", "live.db");
        LiveStoreException failure = LiveStoreException.From(database, profile, new Microsoft.Data.Sqlite.SqliteException("SQLite Error " + code + ": 'synthetic'.", code));
        Assert.Contains(route, failure.Message, StringComparison.Ordinal);
        Assert.Contains("SQLite Error " + code + ": 'synthetic'", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(database, failure.Message, StringComparison.Ordinal);
        Assert.Equal(code is 5 or 6 || profile != "repository-synced", !failure.Message.Contains("cache rebuild", StringComparison.Ordinal));
    }

    private static readonly string[] Sidecars = ["", "-wal", "-shm"];

    private static Dictionary<string, string> RecordHashes(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, EstateLayout.RecordsDirectory), "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), ContentHash.Sha256File, StringComparer.Ordinal);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>A live store to damage in tests: where an estate's store is, and a page overwritten so SQLite reports it malformed.</summary>
public static class LiveStoreDamage
{
    public static string Cache(string root)
    {
        var findings = new FindingList();
        EstateManifest manifest = EstateManifest.Load(root, findings)!;
        return EstateSession.LiveStorePath(root, manifest.Estate, manifest.Profile);
    }

    // Overwrites the first table page after the schema, so reading the record files fails with SQLITE_CORRUPT (11): malformed.
    public static void Malform(string database)
    {
        Assert.False(File.Exists(database + "-wal") && new FileInfo(database + "-wal").Length > 0, "The cache must be checkpointed before it is damaged.");
        using var stream = new FileStream(database, FileMode.Open, FileAccess.ReadWrite);
        byte[] header = new byte[100];
        stream.ReadExactly(header);
        int pageSize = header[16] << 8 | header[17];
        if (pageSize == 1) pageSize = 65536;
        Assert.True(stream.Length >= 2L * pageSize, "The cache has a table page to damage.");
        stream.Position = pageSize;
        stream.Write(Enumerable.Repeat((byte)0xFF, pageSize).ToArray());
    }
}
