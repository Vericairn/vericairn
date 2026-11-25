// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-145: an estate upgrade records a restore point and states how to return to the previous release; before applying it says whether the
/// previous release can still read the estate after it, and a one-way upgrade needs explicit confirmation.
/// </summary>
public sealed class UpgradeReturnTests
{
    [Fact]
    public void AReadableUpgradeRecordsARestorePointWhoseStatedStepsReturnTheEstateToThePreviousRelease()
    {
        TestPrerequisites.RequireGit();
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        Git(f.Estate.Root, "init", "-q", "-b", "main");
        // Git for Windows converts line endings on checkout by default; the clone here does so on every platform.
        Git(f.Estate.Root, "config", "core.autocrlf", "true");
        Git(f.Estate.Root, "add", "-A");
        Git(f.Estate.Root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "The estate before the upgrade");
        string commit = Git(f.Estate.Root, "rev-parse", "HEAD");
        byte[] manifestBefore = File.ReadAllBytes(f.ManifestPath);
        string[] filesBefore = Snapshot(f.Estate.Root, exclude: [".git", ".vericairn"]);

        string clone;
        UpgradeReturn back;
        using (EstateService api = EstateService.Open(f.Estate.Root))
        {
            api.Providers = new Providers.ProviderRegistry();
            api.Providers.Register(new GitCommandAdapter());
            Assert.True(api.ObserverDeclare(new(f.Estate.OperatorId, "ci-main", "ci", 3600, 900)).Ok);

            (UpgradeResponse? check, Problem? none) = api.Upgrade(new("0.9.4", null, true));
            Assert.Null(none);
            Assert.Equal(("readable", false), (check!.Return!.Readability, check.Return.OneWay));
            Assert.Empty(check.Return.Reasons);
            Assert.Null(check.Return.RestorePoint);
            Assert.Contains("Applying records a restore point before it writes anything", check.Return.Steps[0], StringComparison.Ordinal);
            clone = Path.GetDirectoryName(api.Session.Live.Path)!;
            Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));

            (UpgradeResponse? applied, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
            Assert.Null(problem);
            Assert.True(applied!.Applied);
            back = applied.Return!;
            UpgradeRestorePoint point = back.RestorePoint!;
            Assert.Equal((commit, true), (point.Commit, point.Clean));
            Assert.Equal(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory, point.Id), point.Directory);
            Assert.Contains("live.db", point.Files);
            Assert.Contains("observers.json", point.Files);

            // The inventory names every exported file by its hash, and its own hash is the one the migration record names.
            using JsonDocument described = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(point.Directory, "restore-point.json")));
            Assert.Equal(UpgradeReturns.RestorePointFormat, described.RootElement.GetProperty("format").GetString());
            Assert.Equal(("release 0.9.3", "release 0.9.4"), (described.RootElement.GetProperty("fromPin").GetString(), described.RootElement.GetProperty("toPin").GetString()));
            JsonElement inventory = described.RootElement.GetProperty("state");
            foreach (JsonElement file in inventory.EnumerateArray())
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(point.Directory, "state", file.GetProperty("name").GetString()!)))));
            Assert.Equal(point.StateHash, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.SerializeUtf8(inventory))));
            string changes = api.Session.Load().OfType("migration").Single().Body.GetProperty("changes").GetRawText();
            Assert.Contains($"restore point {point.Id}: estate commit {commit} and this machine's local state, inventory {point.StateHash}", changes, StringComparison.Ordinal);
            Assert.Contains("readability by release 0.9.3: readable", changes, StringComparison.Ordinal);
            Assert.DoesNotContain(clone, changes, StringComparison.Ordinal);

            Assert.Contains("runtime stop", back.Steps[0], StringComparison.Ordinal);
            Assert.Contains($"Return the estate's files to commit {commit}", back.Steps[1], StringComparison.Ordinal);
            Assert.Contains("-c core.autocrlf=false stash --include-untracked", back.Steps[1], StringComparison.Ordinal);
            Assert.Contains("-c core.autocrlf=false revert", back.Steps[1], StringComparison.Ordinal);
            Assert.Contains(Path.Combine(point.Directory, "state"), back.Steps[2], StringComparison.Ordinal);
            Assert.Contains("./bootstrap runtime start", back.Steps[3], StringComparison.Ordinal);
            Assert.Contains("release 0.9.3 reads the estate as the upgrade leaves it", back.Steps[4], StringComparison.Ordinal);

            // Work after the upgrade changes this machine's state; the way back returns it to what it was.
            Assert.True(api.ObserverWithdraw(new(f.Estate.OperatorId, "ci-main", "Moved elsewhere.")).Ok);
        }

        // The stated way back, step by step: the runtime is stopped (disposed here), the files return to the commit, the state is put back.
        Git(f.Estate.Root, "-c", "core.autocrlf=false", "stash", "--include-untracked");
        Assert.Equal(manifestBefore, File.ReadAllBytes(f.ManifestPath));
        // Every file of the estate returns with its exact bytes, though the clone converts line endings on checkout.
        Assert.Equal(filesBefore, Snapshot(f.Estate.Root, exclude: [".git", ".vericairn"]));
        string state = Path.Combine(back.RestorePoint!.Directory, "state");
        foreach (string leftover in new[] { "live.db-wal", "live.db-shm" }) File.Delete(Path.Combine(clone, leftover));
        foreach (string file in Directory.EnumerateFiles(state)) File.Copy(file, Path.Combine(clone, Path.GetFileName(file)), overwrite: true);

        using (EstateService returned = EstateService.Open(f.Estate.Root))
        {
            EstateModel model = returned.Session.Load();
            Assert.Equal("release 0.9.3", model.Manifest!.Describe());
            Assert.Empty(model.OfType("migration"));
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal("ci-main", Assert.Single(returned.Observers().Observers).Name);
        }
    }

    [Fact]
    public void AOneWayUpgradeIsNamedBeforeItIsAppliedAndNeedsExplicitConfirmation()
    {
        using var releases = new ReleaseLineFixture(2);
        TestEstate estate = releases.Estate;
        var manifest = new JsonObject
        {
            ["estate"] = estate.Estate.Id, ["format"] = EstateManifest.Format1, ["recordFormat"] = 2,
            ["vericairn"] = new JsonObject { ["mode"] = "release", ["repository"] = "Example/example", ["version"] = "0.8.4", ["artifacts"] = new JsonObject { ["linux-x64"] = "sha256:" + new string('1', 64) } },
        };
        File.WriteAllBytes(Path.Combine(estate.Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)));
        JsonObject body = ReleaseLineFixture.Body("0.9.4");
        body["artifacts"] = new JsonArray(new JsonObject { ["name"] = "vericairn-0.9.4-linux-x64.tar.gz", ["hash"] = "sha256:" + new string('1', 64), ["bytes"] = 10, ["platform"] = "linux-x64" });
        releases.Publish(body);
        string[] before = Snapshot(estate.Root);

        using EstateService api = EstateService.Open(estate.Root);
        (UpgradeResponse? check, Problem? none) = api.Upgrade(new("0.9.4", null, true));
        Assert.Null(none);
        Assert.Equal(("one-way", true), (check!.Return!.Readability, check.Return.OneWay));
        Assert.Equal("the estate moves from record format 2 to 3, and release 0.8.4 reads record formats up to 2", Assert.Single(check.Return.Reasons));

        (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", estate.OperatorId, false));
        Assert.Null(refused);
        Assert.Equal("one-way", problem!.Code);
        Assert.Contains("This upgrade is one-way: the estate moves from record format 2 to 3", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing changed. To apply it, run it again with --one-way.", problem.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(estate.Root));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(api.Session.Live.Path)!, UpgradeReturns.RestorePointsDirectory)));

        (UpgradeResponse? applied, Problem? confirmed) = api.Upgrade(new("0.9.4", estate.OperatorId, false) { OneWay = true });
        Assert.Null(confirmed);
        Assert.True(applied!.Applied);
        Assert.True(applied.Return!.OneWay);
        Assert.NotNull(applied.Return.RestorePoint);
        Assert.True(Directory.Exists(applied.Return.RestorePoint.Directory));
        Assert.Contains("every record written after the upgrade stays behind", applied.Return.Steps[^1], StringComparison.Ordinal);
        Assert.Contains("readability by release 0.8.4: one-way", api.Session.Load().OfType("migration").Single().Body.GetProperty("changes").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestFieldThePreviousReleasePredatesIsOneWayAndASourcePinsReadingIsUnconfirmed()
    {
        using (var f = new EstateLineFixture())
        {
            f.Publish("0.9.4");
            AdmitFirstDevice(f);
            // The device's admission named the first authority in the manifest; a manifest from before names none, so the upgrade adds it.
            f.Follow(version: "0.9.1");
            using EstateService api = EstateService.Open(f.Estate.Root);
            Assert.Null(api.Session.Load().Manifest!.FirstAuthority);
            UpgradeReturn back = api.Upgrade(new("0.9.4", null, true)).Plan!.Return!;
            Assert.Equal("one-way", back.Readability);
            Assert.Equal("release 0.9.1 predates the manifest's first authority, which 0.9.2 first reads", Assert.Single(back.Reasons));
        }

        using (var f = new EstateLineFixture())
        {
            f.Publish("0.9.4");
            AdmitFirstDevice(f);
            f.Follow();
            using EstateService api = EstateService.Open(f.Estate.Root);
            UpgradeReturn back = api.Upgrade(new("0.9.4", null, true)).Plan!.Return!;
            Assert.Equal(("unconfirmed", false), (back.Readability, back.OneWay));
            Assert.Contains("is a source build whose release is unknown, so whether it reads the manifest's first authority, which 0.9.2 first reads, cannot be confirmed", Assert.Single(back.Reasons), StringComparison.Ordinal);
            // Only a known one-way upgrade needs the confirmation; an unconfirmed one is applied, its restore point recorded.
            (UpgradeResponse? applied, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
            Assert.Null(problem);
            Assert.NotNull(applied!.Return!.RestorePoint);
            Assert.Null(applied.Return.RestorePoint.Commit);
        }
    }

    [Fact]
    public void WithNoDaemonARepositorySyncedEstatesRestorePointHoldsItsPopulatedLiveStore()
    {
        // FIX-174 (issue 440): the command line's upgrade with no daemon opens the estate read-only (CommandContext.WithApi takes
        // EstateService.OpenForUpgrade), and that session's repository-synced store is transient, in memory; the live store's export
        // came from it and wrote no file.
        using var f = new EstateLineFixture();
        f.Publish("0.9.4");
        AdmitFirstDevice(f);
        f.Follow(version: "0.9.1");
        string clone = PopulateLiveStore(f);
        string live = Path.Combine(clone, "live.db");
        IReadOnlyDictionary<string, long> before = RestorePointInspection.Rows(live);
        Assert.True(before["files"] > 0 && before["meta"] > 0);

        using EstateService api = EstateService.OpenForUpgrade(f.Estate.Root);
        Assert.True(api.Session.IsReadOnly);
        (UpgradeResponse? applied, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false) { OneWay = true });
        Assert.Null(problem);
        Assert.True(applied!.Applied);
        Assert.True(applied.Return!.OneWay);
        UpgradeRestorePoint point = applied.Return.RestorePoint!;
        Assert.Equal(["live.db", "observers.json"], point.Files);
        // The kept live store passes SQLite's integrity check and holds every row the clone's did.
        Assert.Equal(before, RestorePointInspection.Rows(Path.Combine(point.Directory, "state", "live.db")));
        Assert.Contains("first removing live.db-wal and live.db-shm there.", applied.Return.Steps[2], StringComparison.Ordinal);
        Assert.Contains("its live store included", api.Session.Load().OfType("migration").Single().Body.GetProperty("changes").GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    [InlineData("length")]
    [InlineData("mtime")]
    public void WithNoDaemonAChangeDuringAnImmutableExportRefusesTheUpgrade(string change)
    {
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        string clone = PopulateLiveStore(f);
        string source = Path.Combine(clone, "probe.db");
        CreateDatabase(source, "CREATE TABLE payload (value INTEGER); INSERT INTO payload VALUES (1);");
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        string[] estate = Snapshot(f.Estate.Root);

        UpgradeReturns.ExportWritten = path =>
        {
            if (!path.StartsWith(clone + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.GetFileName(path) != "probe.db") return;
            switch (change)
            {
                case "length":
                    using (var stream = new FileStream(source, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
                    File.SetLastWriteTimeUtc(source, stamp);
                    break;
                case "mtime":
                    File.SetLastWriteTimeUtc(source, stamp.AddSeconds(2));
                    break;
                default:
                    File.WriteAllBytes(source + change, []);
                    break;
            }
        };
        try
        {
            using EstateService api = EstateService.OpenForUpgrade(f.Estate.Root);
            (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
            Assert.Null(refused);
            Assert.Equal("refused", problem!.Code);
            Assert.Contains("probe.db changed while its restore copy was being made", problem.Message, StringComparison.Ordinal);
            Assert.Contains("let other commands using the estate finish, then retry the upgrade", problem.Message, StringComparison.Ordinal);
        }
        finally
        {
            UpgradeReturns.ExportWritten = null;
        }

        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
    }

    [Theory]
    [InlineData("search.db", "move the search index and its sidecars aside and run 'vericairn search index --rebuild'")]
    [InlineData("events.db", "move the event journal and its sidecars aside so the daemon can start a new journal")]
    [InlineData("objects.db", "recover the object store and its sidecars from a known-good backup")]
    [InlineData("extra.db", "move the database and its sidecars aside if it can be rebuilt, or recover them from a known-good backup")]
    public void ADatabaseThatFailsIntegrityNamesItsOwnRemedyOnOneLine(string name, string remedy)
    {
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        string clone = PopulateLiveStore(f);
        // Change the index expression in the schema without rebuilding its entries. SQLite can still read the table, but its integrity
        // verdict names the inconsistent index, whose name includes a newline.
        CreateDatabase(Path.Combine(clone, name), """
            CREATE TABLE payload (value INTEGER);
            CREATE INDEX "probe
            rows" ON payload(value);
            INSERT INTO payload VALUES (1), (2);
            PRAGMA writable_schema=ON;
            UPDATE sqlite_master SET sql = replace(sql, 'payload(value)', 'payload(value + 1)') WHERE type = 'index';
            PRAGMA writable_schema=RESET;
            """);
        string[] estate = Snapshot(f.Estate.Root), state = Snapshot(clone);

        using (EstateService api = EstateService.OpenForUpgrade(f.Estate.Root))
        {
            (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
            Assert.Null(refused);
            Assert.Equal("refused", problem!.Code);
            Assert.Multiple(
                () => Assert.Contains(remedy, problem.Message, StringComparison.Ordinal),
                () => Assert.Contains($"SQLite's integrity check found damage in {name} (row 1 missing from index probe rows)", problem.Message, StringComparison.Ordinal),
                () => Assert.DoesNotContain('\r', problem.Message),
                () => Assert.DoesNotContain('\n', problem.Message),
                () => Assert.DoesNotContain("its export", problem.Message, StringComparison.Ordinal));
        }

        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(state, Snapshot(clone));
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
    }

    [Fact]
    public void AKnownDatabaseWithADamagedHeaderRefusesTheUpgrade()
    {
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        string clone = PopulateLiveStore(f);
        File.WriteAllText(Path.Combine(clone, "search.db"), "Damaged header");

        using EstateService api = EstateService.OpenForUpgrade(f.Estate.Root);
        (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
        Assert.Null(refused);
        Assert.Equal("refused", problem!.Code);
        Assert.Contains("search.db could not be read", problem.Message, StringComparison.Ordinal);
        Assert.Contains("'vericairn search index --rebuild'", problem.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
    }

    [Theory]
    [InlineData("deleted", "its export of live.db wrote no file")]
    [InlineData("emptied", "its export of live.db holds no table files, where live.db holds")]
    [InlineData("short", "rows in table files, where live.db holds")]
    [InlineData("truncated", "SQLite could not check live.db")]
    public void WithNoDaemonAnExportThatFailsItsCheckRefusesTheOneWayUpgradeAndNothingChanges(string damage, string reason)
    {
        using var f = new EstateLineFixture();
        f.Publish("0.9.4");
        AdmitFirstDevice(f);
        f.Follow(version: "0.9.1");
        string clone = PopulateLiveStore(f);
        string[] estate = Snapshot(f.Estate.Root);
        string[] state = Snapshot(clone);

        // The export reports success and its copy is not what the live store holds, as the transient store's export did.
        UpgradeReturns.ExportWritten = path =>
        {
            if (path.StartsWith(clone + Path.DirectorySeparatorChar, StringComparison.Ordinal)) RestorePointInspection.Damage(path, damage);
        };
        try
        {
            using EstateService api = EstateService.OpenForUpgrade(f.Estate.Root);
            (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false) { OneWay = true });
            Assert.Null(refused);
            Assert.Equal("refused", problem!.Code);
            Assert.StartsWith("The upgrade's restore point could not be recorded on this machine: ", problem.Message, StringComparison.Ordinal);
            Assert.Contains(reason, problem.Message, StringComparison.Ordinal);
            Assert.EndsWith("Nothing was applied, and nothing changed.", problem.Message, StringComparison.Ordinal);
        }
        finally
        {
            UpgradeReturns.ExportWritten = null;
        }

        // Nothing changed: the estate's files, the clone's live store and every file beside it, and no restore point is left.
        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(state, Snapshot(clone));
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
        EstateModel model = f.Estate.Load();
        Assert.Equal("release 0.9.1", model.Manifest!.Describe());
        Assert.Empty(model.OfType("migration"));
    }

    [Fact]
    public void WithNoDaemonALiveStoreThatCannotBeReadRefusesTheUpgradeAndNamesItsRebuild()
    {
        using var f = new EstateLineFixture();
        f.Publish("0.9.4");
        AdmitFirstDevice(f);
        f.Follow(version: "0.9.1");
        string clone = PopulateLiveStore(f);
        RestorePointInspection.Damage(Path.Combine(clone, "live.db"), "truncated");
        string[] estate = Snapshot(f.Estate.Root);
        string[] state = Snapshot(clone);

        using (EstateService api = EstateService.OpenForUpgrade(f.Estate.Root))
        {
            (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false) { OneWay = true });
            Assert.Null(refused);
            Assert.Equal("refused", problem!.Code);
            Assert.Contains("The upgrade's restore point could not be recorded on this machine: live.db could not be read (", problem.Message, StringComparison.Ordinal);
            Assert.Contains("run 'vericairn estate cache rebuild' to rebuild this clone's live store from the repository's records", problem.Message, StringComparison.Ordinal);
            Assert.EndsWith("Nothing was applied, and nothing changed.", problem.Message, StringComparison.Ordinal);
        }

        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(state, Snapshot(clone));
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
    }

    [Fact]
    public void ARestorePointCopiesTheEventJournalADaemonHoldsOpenAsOneConsistentFile()
    {
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        using EstateService api = EstateService.Open(f.Estate.Root);
        string clone = Path.GetDirectoryName(api.Session.Live.Path)!;
        // As the daemon serves the estate: its journal beside the live store, held open, its first observation in the write-ahead log,
        // and the search index, whose full-text tables keep their rows in shadow tables.
        api.EnableEvents(clone);
        api.DeriveEvents();
        Assert.Null(api.SearchIndex(new SearchIndexRequest()).Problem);
        string journal = Path.Combine(clone, "events.db");
        string search = Path.Combine(clone, "search.db");
        Assert.True(File.Exists(journal + "-wal") && File.Exists(journal + "-shm") && File.Exists(search));

        (UpgradeResponse? applied, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
        Assert.Null(problem);
        UpgradeRestorePoint point = applied!.Return!.RestorePoint!;
        Assert.Contains("events.db", point.Files);
        Assert.Contains("live.db", point.Files);
        // One file each, read through SQLite, never the -wal and -shm a runtime holds open copied beside it.
        Assert.DoesNotContain(point.Files, name => name.EndsWith("-wal", StringComparison.Ordinal) || name.EndsWith("-shm", StringComparison.Ordinal));
        IReadOnlyDictionary<string, long> kept = RestorePointInspection.Rows(Path.Combine(point.Directory, "state", "events.db"));
        Assert.True(kept["observed"] > 0);
        Assert.Equal(RestorePointInspection.Rows(journal), kept);
        Assert.Contains("search.db", point.Files);
        Assert.Equal(RestorePointInspection.Rows(search), RestorePointInspection.Rows(Path.Combine(point.Directory, "state", "search.db")));
        Assert.Contains("first removing live.db-wal, live.db-shm, events.db-wal, events.db-shm, search.db-wal and search.db-shm there.", applied.Return.Steps[2], StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoDaemonAndNoLiveStoreOnDiskTheRestorePointHoldsNoneAndTheWayBackSaysSo()
    {
        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        using EstateService api = EstateService.OpenForUpgrade(f.Estate.Root);
        string live = api.Session.Live.Path;
        Assert.False(File.Exists(live));
        (UpgradeResponse? applied, Problem? problem) = api.Upgrade(new("0.9.4", f.Estate.OperatorId, false));
        Assert.Null(problem);
        UpgradeRestorePoint point = applied!.Return!.RestorePoint!;
        Assert.Null(point.Commit);
        Assert.DoesNotContain("live.db", point.Files);
        string changes = api.Session.Load().OfType("migration").Single().Body.GetProperty("changes").GetRawText();
        Assert.Contains($"restore point {point.Id}: this machine's local state, inventory {point.StateHash}", changes, StringComparison.Ordinal);
        Assert.DoesNotContain("its live store included", changes, StringComparison.Ordinal);
        Assert.Contains("first removing live.db, live.db-wal and live.db-shm there. This machine kept no live store for the clone before the upgrade, so none is put back; release 0.9.3 builds it again from the repository's records.", applied.Return.Steps[2], StringComparison.Ordinal);
    }

    private static void CreateDatabase(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // Opens the estate writable, as a command or daemon before the upgrade did, so the clone's live store holds its records and
    // metadata, and an observer beside it; closed, it leaves the live store alone on disk. Returns the clone's directory.
    private static string PopulateLiveStore(EstateLineFixture f)
    {
        string clone;
        using (EstateService api = EstateService.Open(f.Estate.Root))
        {
            Assert.False(api.Session.Load().Findings.HasErrors);
            Assert.True(api.ObserverDeclare(new(f.Estate.OperatorId, "ci-main", "ci", 3600, 900)).Ok);
            clone = Path.GetDirectoryName(api.Session.Live.Path)!;
        }

        Assert.True(File.Exists(Path.Combine(clone, "live.db")));
        Assert.False(File.Exists(Path.Combine(clone, "live.db-wal")));
        return clone;
    }

    private static void AdmitFirstDevice(EstateLineFixture f)
    {
        using EstateService api = EstateService.Open(f.Estate.Root);
        string device = api.DeviceCreate(new DeviceCreateRequest(f.Estate.OperatorId, "Operator's laptop", "file")).Written!.Id;
        Assert.True(api.DeviceAdmit(new AdmitRequest(device, f.Estate.OperatorId, "The first authority.")).Ok);
    }

    [Theory]
    [InlineData("0.9.2", "0.9.2", 0)]
    [InlineData("0.9.2-rc.1", "0.9.2", -1)]
    [InlineData("0.10.0", "0.9.2", 1)]
    [InlineData("0.9.1", "0.9.2", -1)]
    [InlineData("1.0.0+0123456", "0.9.2", 1)]
    public void AReleaseReadsWhatItsOwnOrALaterReleaseIntroducedAndAPrereleaseDoesNot(string version, string release, int order) =>
        Assert.Equal(order, Math.Sign(UpgradeReturns.Compare(version, release)));

    // The repository's files, leaving out the directories named (git's own, and the clone's working directory the runtime keeps).
    private static string[] Snapshot(string root, string[]? exclude = null) => [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(file => exclude is null || !exclude.Contains(Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0], StringComparer.Ordinal))
        .Select(file => Path.GetRelativePath(root, file) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))).Order(StringComparer.Ordinal)];

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}

/// <summary>From 0.9.5 (FIX-174): what a restore point's exported databases hold, read from disposable copies of them.</summary>
public static class RestorePointInspection
{
    /// <summary>
    /// Every table's rows in the database, read from a copy of it and its write-ahead log, so nothing is created beside it; the copy must
    /// pass SQLite's integrity check. A virtual table keeps its rows in shadow tables, which are counted instead.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Rows(string database)
    {
        string directory = Directory.CreateTempSubdirectory("vericairn-restore-point-").FullName;
        try
        {
            string copy = Path.Combine(directory, "copy.db");
            File.Copy(database, copy);
            if (File.Exists(database + "-wal")) File.Copy(database + "-wal", copy + "-wal");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", command.ExecuteScalar());
            var tables = new List<string>();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND sql NOT LIKE 'CREATE VIRTUAL TABLE%' ORDER BY name;";
            using (SqliteDataReader reader = command.ExecuteReader())
                while (reader.Read()) tables.Add(reader.GetString(0));
            var rows = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (string table in tables)
            {
                command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
                rows[table] = (long)command.ExecuteScalar()!;
            }

            return rows;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Damages an exported copy as a test forces its export to fail: deleted, emptied, a row short, or truncated.</summary>
    public static void Damage(string copy, string damage)
    {
        switch (damage)
        {
            case "deleted":
                File.Delete(copy);
                break;
            case "emptied":
                File.WriteAllBytes(copy, []);
                break;
            case "short":
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM files WHERE rowid = (SELECT MIN(rowid) FROM files);";
                    Assert.Equal(1, command.ExecuteNonQuery());
                }

                break;
            case "truncated":
                using (var stream = new FileStream(copy, FileMode.Open, FileAccess.Write))
                    stream.SetLength(4096);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(damage), damage, null);
        }
    }
}
