// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>A test that changes the process's umask runs alone, so no other test creates files under it.</summary>
[CollectionDefinition("process umask", DisableParallelization = true)]
public sealed class ProcessUmaskIsolation;

/// <summary>
/// The per-user state that an estate opened in this process creates, with no daemon running: the Vericairn home, every directory down to
/// the live store's, the live store with its -wal and -shm files, the object store and the batch journals are readable by their owner
/// alone whatever the process's umask, and ones an earlier build left wider are narrowed when the estate is opened.
/// </summary>
[Collection("process umask")]
public class LocalStateModeTests
{
    private const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode WideDirectory = OwnerDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode WideFile = OwnerFile | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    [Theory]
    [InlineData("repository-synced")]
    [InlineData("local-only")]
    public void AnEstateOpenedInProcessUnderAPermissiveUmaskKeepsItsStateOwnerOnly(string profile)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = Scratch();
        try
        {
            (string root, PreparedEstate prepared) = Estate(scratch, profile);
            string estateId = prepared.Estate.Envelope.Id;
            string home = Path.Combine(scratch, "state", "home");
            uint previous = umask(0);
            try
            {
                using EstateSession session = EstateSession.Open(root, false, home);
                if (profile == "local-only")
                {
                    // A local-only estate's records live in its live store, so they are stored through the session, as initialization does.
                    session.Write(prepared.Operator);
                    session.Write(prepared.Estate);
                    session.Objects.Put("an object"u8);
                }

                Assert.False(session.Load().Findings.HasErrors);
                BatchJournal journal = BatchJournal.Begin(session, "mode check", null, []);
                string live = Path.GetDirectoryName(session.Live.Path)!;

                foreach (string directory in new[] { home, Path.Combine(home, "estates"), Path.Combine(home, "estates", estateId), live, BatchJournal.Directory(session) })
                    Assert.Equal(OwnerDirectory, File.GetUnixFileMode(directory));

                // The store is open, so SQLite's log and index are there, created beside a database that was owner-only from its first byte.
                string[] files = profile == "local-only" ? ["live.db", "objects.db"] : ["live.db"];
                foreach (string file in files.SelectMany(f => new[] { f, f + "-wal", f + "-shm" }))
                    Assert.Equal(OwnerFile, File.GetUnixFileMode(Path.Combine(live, file)));
                Assert.Equal(OwnerFile, File.GetUnixFileMode(Path.Combine(BatchJournal.Directory(session), journal.Id + ".json")));
                Assert.Equal(WideDirectory | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite, File.GetUnixFileMode(Path.Combine(scratch, "state")));
            }
            finally
            {
                Assert.Equal(0u, umask(previous) & 0x1FF);
            }
        }
        finally
        {
            Delete(scratch);
        }
    }

    [Fact]
    public void OpeningAnEstateNarrowsAHomeAndLiveStoreThatAnEarlierBuildLeftWider()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = Scratch();
        try
        {
            (string root, PreparedEstate prepared) = Estate(scratch, "repository-synced");
            string estateId = prepared.Estate.Envelope.Id;
            string home = Path.Combine(scratch, "home");
            string database;
            using (EstateSession first = EstateSession.Open(root, false, home))
            {
                first.Load();
                database = first.Live.Path;
            }

            string live = Path.GetDirectoryName(database)!;
            string[] directories = [home, Path.Combine(home, "estates"), Path.Combine(home, "estates", estateId), live];
            foreach (string directory in directories) File.SetUnixFileMode(directory, WideDirectory);
            File.SetUnixFileMode(database, WideFile);

            // Another connection keeps the log and index in place, as an earlier build's daemon would, and they take the database's wider mode.
            using var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
            other.Open();
            using (SqliteCommand command = other.CreateCommand())
            {
                command.CommandText = "INSERT INTO meta (key, value) VALUES ('mode-check', 'wide') ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                command.ExecuteNonQuery();
            }

            foreach (string sidecar in new[] { database + "-wal", database + "-shm" })
            {
                Assert.True(File.Exists(sidecar), sidecar);
                File.SetUnixFileMode(sidecar, WideFile);
            }

            using (EstateSession reopened = EstateSession.Open(root, false, home))
            {
                Assert.Equal("wide", reopened.Live.Meta("mode-check"));
                foreach (string directory in directories) Assert.Equal(OwnerDirectory, File.GetUnixFileMode(directory));
                foreach (string file in new[] { database, database + "-wal", database + "-shm" }) Assert.Equal(OwnerFile, File.GetUnixFileMode(file));
            }

            // A read-only opening changes nothing on disk, its modes included.
            File.SetUnixFileMode(live, WideDirectory);
            using (EstateSession.Open(root, true, home))
            {
                Assert.Equal(WideDirectory, File.GetUnixFileMode(live));
            }
        }
        finally
        {
            Delete(scratch);
        }
    }

    [Fact]
    public void CreatingAKeyFileNarrowsTheHomeThatHoldsIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = Scratch();
        try
        {
            string home = Path.Combine(scratch, "home");
            Directory.CreateDirectory(home);
            File.SetUnixFileMode(home, WideDirectory);
            new FileKeyStore(home).Create(Ulid.NewUlid().Value);
            Assert.Equal(OwnerDirectory, File.GetUnixFileMode(home));
            Assert.Equal(OwnerDirectory, File.GetUnixFileMode(Path.Combine(home, "keys")));
        }
        finally
        {
            Delete(scratch);
        }
    }

    [Fact]
    public void AStoreInADirectoryThatBelongsToAnotherUserIsRefused()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Needs a Unix directory that another user owns: /tmp is root's, unless the tests run as root.");
            return;
        }

        // /tmp belongs to root on Linux and macOS; a live store kept directly in it would be readable by root's choosing.
        string database = Path.Combine("/tmp", "vericairn-other-owner-" + Ulid.NewUlid().Value + ".db");
        IOException refused = Assert.Throws<IOException>(() => new LiveStore(database));
        Assert.Contains("belongs to another user of this machine", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(database));
        Assert.Throws<IOException>(() => new Vericairn.Runtime.Objects.SqliteObjectStore(database));
        Assert.False(File.Exists(database));
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("unsupported")]
    [InlineData("ignored")]
    public void StateOnAFilesystemThatKeepsNoUnixModesOpensAndIsReportedOnce(string filesystem)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = Scratch();
        Action<string>? report = OwnerOnlyState.Report;
        var told = new List<string>();
        try
        {
            (string root, _) = Estate(scratch, "repository-synced");
            string home = Path.Combine(scratch, "home");
            Directory.CreateDirectory(home);
            File.SetUnixFileMode(home, WideDirectory);
            OwnerOnlyState.Report = told.Add;
            // As vfat refuses a mode change (EPERM), as some FUSE and network filesystems do not support one, and as exFAT on macOS or
            // WSL's /mnt accept one that does not hold.
            OwnerOnlyState.ModeSetter = filesystem switch
            {
                "refused" => (_, _) => throw new UnauthorizedAccessException("Operation not permitted"),
                "unsupported" => (_, _) => throw new IOException("Operation not supported", 45),
                _ => (_, _) => { },
            };

            string database;
            using (EstateSession session = EstateSession.Open(root, false, home))
            {
                Assert.False(session.Load().Findings.HasErrors);
                database = session.Live.Path;
            }

            Assert.Equal([home], told);
            Assert.Contains(home, OwnerOnlyState.Unrestricted);
            Assert.Equal(WideDirectory, File.GetUnixFileMode(home));

            // A store an earlier build left wider is used as it is too, and nothing more is said: it is on the same filesystem.
            File.SetUnixFileMode(Path.GetDirectoryName(database)!, WideDirectory);
            File.SetUnixFileMode(database, WideFile);
            using (EstateSession reopened = EstateSession.Open(root, false, home))
            {
                Assert.False(reopened.Load().Findings.HasErrors);
            }

            Assert.Equal([home], told);
            Assert.Equal(WideFile, File.GetUnixFileMode(database));
            Assert.Equal((home, OwnerOnlyState.Warning(home)), OwnerOnlyState.Restriction(home));
        }
        finally
        {
            OwnerOnlyState.ModeSetter = null;
            OwnerOnlyState.Report = report;
            Delete(scratch);
        }
    }

    [Fact]
    public void StoreFilesWhoseModesDoNotHoldAreReportedOnceByTheirDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = Scratch();
        Action<string>? report = OwnerOnlyState.Report;
        var told = new List<string>();
        try
        {
            (string root, _) = Estate(scratch, "local-only");
            string home = Path.Combine(scratch, "home");
            string database;
            using (EstateSession first = EstateSession.Open(root, false, home))
            {
                database = first.Live.Path;
            }

            // As FAT32 mounted on macOS shows it: directories 0700 and every file 0700, whatever a mode change asks.
            string live = Path.GetDirectoryName(database)!;
            foreach (string file in new[] { database, Path.Combine(live, "objects.db") }) File.SetUnixFileMode(file, OwnerDirectory);
            OwnerOnlyState.Report = told.Add;
            OwnerOnlyState.ModeSetter = (path, mode) =>
            {
                if (mode == OwnerDirectory && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode);
            };

            using (EstateSession reopened = EstateSession.Open(root, false, home))
            {
                Assert.Equal("local-only", reopened.Profile);
            }

            Assert.Equal([live], told);
            Assert.Equal((live, OwnerOnlyState.Warning(live)), OwnerOnlyState.Restriction(database));
        }
        finally
        {
            OwnerOnlyState.ModeSetter = null;
            OwnerOnlyState.Report = report;
            Delete(scratch);
        }
    }

    private static string Scratch()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-mode-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(scratch);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(scratch, WideDirectory | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
        Directory.CreateDirectory(Path.Combine(scratch, "state"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(scratch, "state"), WideDirectory | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
        return scratch;
    }

    private static (string Root, PreparedEstate Prepared) Estate(string scratch, string profile)
    {
        string root = Path.Combine(scratch, "estate");
        Directory.CreateDirectory(root);
        var initialization = new EstateInitialization("Modes", "modes", "Per-user state modes.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), profile);
        PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
        string manifest = Path.Combine(root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllBytes(manifest, prepared.ManifestBytes);
        if (profile == "repository-synced")
        {
            RecordAuthor.Write(root, prepared.Operator);
            RecordAuthor.Write(root, prepared.Estate);
        }

        return (root, prepared);
    }

    private static void Delete(string scratch)
    {
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a store still closing keeps its files a moment longer.
        }
    }

#pragma warning disable SYSLIB1054
    // mode_t is 16 bits on macOS and 32 on Linux; the permission bits fit either.
    [DllImport("libc")]
    private static extern uint umask(uint mask);
#pragma warning restore SYSLIB1054
}
