// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Git(f.Estate.Root, "add", "-A");
        Git(f.Estate.Root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "The estate before the upgrade");
        string commit = Git(f.Estate.Root, "rev-parse", "HEAD");
        byte[] manifestBefore = File.ReadAllBytes(f.ManifestPath);

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
            Assert.Contains("stash --include-untracked", back.Steps[1], StringComparison.Ordinal);
            Assert.Contains(Path.Combine(point.Directory, "state"), back.Steps[2], StringComparison.Ordinal);
            Assert.Contains("./bootstrap runtime start", back.Steps[3], StringComparison.Ordinal);
            Assert.Contains("release 0.9.3 reads the estate as the upgrade leaves it", back.Steps[4], StringComparison.Ordinal);

            // Work after the upgrade changes this machine's state; the way back returns it to what it was.
            Assert.True(api.ObserverWithdraw(new(f.Estate.OperatorId, "ci-main", "Moved elsewhere.")).Ok);
        }

        // The stated way back, step by step: the runtime is stopped (disposed here), the files return to the commit, the state is put back.
        Git(f.Estate.Root, "stash", "--include-untracked");
        Assert.Equal(manifestBefore, File.ReadAllBytes(f.ManifestPath));
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

    private static string[] Snapshot(string root) => [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
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
