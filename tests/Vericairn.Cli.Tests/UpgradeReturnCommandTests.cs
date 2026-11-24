// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Cli.Tests;

/// <summary>FIX-145 through the command line: estate upgrade says whether the previous release reads the estate after it, and the way back.</summary>
[Collection("cli")]
public sealed class UpgradeReturnCommandTests
{
    [Fact]
    public void TheUpgradeNamesItsReadabilityItsRestorePointAndTheWayBack()
    {
        Assert.Equal(0, CliHarness.Run("estate", "upgrade", "--help").Code);
        Command upgrade = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Subcommands.Single(c => c.Name == "estate").Subcommands.Single(c => c.Name == "upgrade");
        Assert.False(upgrade.Options.Single(o => o.Name == "--one-way").Required);

        using var f = new EstateLineFixture();
        f.Follow(version: "0.9.3");
        f.Publish("0.9.4");
        using EstateService api = EstateService.Open(f.Estate.Root);
        CliHarness.Outcome check = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "estate", "upgrade", "--to", "0.9.4", "--check");
        Assert.Equal(0, check.Code);
        Assert.Contains("previous release: release 0.9.3 reads the estate after this upgrade", check.Out, StringComparison.Ordinal);
        Assert.Contains("restore point and way back:\n  1. Applying records a restore point before it writes anything", check.Out, StringComparison.Ordinal);

        CliHarness.Outcome applied = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "estate", "upgrade", "--to", "0.9.4", "--by", f.Estate.OperatorId);
        Assert.Equal(0, applied.Code);
        Assert.Matches(@"restore point [0-9A-Z]{26}: no repository commit; local state .+ \(sha256:[0-9a-f]{64}\)", applied.Out);
        Assert.Contains("way back to release 0.9.3:\n  1. Stop the runtime serving the estate on this machine", applied.Out, StringComparison.Ordinal);
        Assert.Contains("  4. Run the previous release: in the estate's directory, ./bootstrap runtime start", applied.Out, StringComparison.Ordinal);

        CliHarness.Outcome json = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "--json", "estate", "upgrade", "--to", "0.9.4", "--check");
        Assert.Equal("readable", json.Json.GetProperty("return").GetProperty("readability").GetString());
        Assert.False(json.Json.GetProperty("return").GetProperty("oneWay").GetBoolean());
    }

    [Fact]
    public void WithNoDaemonTheUpgradeOfARepositorySyncedEstateKeepsItsPopulatedLiveStore()
    {
        // FIX-174 (issue 440): the command line itself, with no daemon serving the estate, so it opens the estate for the upgrade
        // read-only (EstateService.OpenForUpgrade), as the published build did when the restore point left the live store out.
        using var f = new EstateLineFixture();
        string clone = OneWayEstate(f);
        string live = Path.Combine(clone, "live.db");
        IReadOnlyDictionary<string, long> before = RestorePointInspection.Rows(live);

        CliHarness.Outcome applied = CliHarness.Run("--estate", f.Estate.Root, "--json", "estate", "upgrade", "--to", "0.9.4", "--by", f.Estate.OperatorId, "--one-way");
        Assert.True(applied.Code == 0, applied.Err);
        JsonElement point = applied.Json.GetProperty("return").GetProperty("restorePoint");
        string[] files = [.. point.GetProperty("files").EnumerateArray().Select(file => file.GetString()!)];
        Assert.Equal(["live.db", "observers.json"], files);
        Assert.Equal(before, RestorePointInspection.Rows(Path.Combine(point.GetProperty("directory").GetString()!, "state", "live.db")));
        Assert.Equal("release 0.9.4", f.Estate.Load().Manifest!.Describe());
    }

    [Fact]
    public void WithNoDaemonAnExportThatFailsItsCheckRefusesTheUpgradeAndNothingChanges()
    {
        using var f = new EstateLineFixture();
        string clone = OneWayEstate(f);
        string[] estate = Snapshot(f.Estate.Root), state = Snapshot(clone);
        // The export reports success and writes an empty database, as the read-only session's export once wrote no file at all.
        UpgradeReturns.ExportWritten = path =>
        {
            if (path.StartsWith(clone + Path.DirectorySeparatorChar, StringComparison.Ordinal)) RestorePointInspection.Damage(path, "emptied");
        };
        CliHarness.Outcome refused;
        try
        {
            refused = CliHarness.Run("--estate", f.Estate.Root, "estate", "upgrade", "--to", "0.9.4", "--by", f.Estate.OperatorId, "--one-way");
        }
        finally
        {
            UpgradeReturns.ExportWritten = null;
        }

        Assert.NotEqual(0, refused.Code);
        string error = CliHarness.Unfold(refused.Err);
        Assert.Contains("The upgrade's restore point could not be recorded on this machine: its export of live.db holds no table files, where live.db holds", error, StringComparison.Ordinal);
        Assert.Contains("Nothing was applied, and nothing changed.", error, StringComparison.Ordinal);
        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(state, Snapshot(clone));
        Assert.False(Directory.Exists(Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)));
        EstateModel model = f.Estate.Load();
        Assert.Equal("release 0.9.1", model.Manifest!.Describe());
        Assert.Empty(model.OfType("migration"));
    }

    // An estate pinned to 0.9.1 whose upgrade to 0.9.4 is one-way, as in issue 440: the first device's admission names the first authority,
    // which 0.9.1 predates. A writable command before the upgrade left the clone's live store populated, with an observer beside it, and
    // closed; no daemon serves the estate. Returns the clone's directory.
    private static string OneWayEstate(EstateLineFixture f)
    {
        Assert.Equal(0, CliHarness.Run("estate", "upgrade", "--help").Code);
        f.Publish("0.9.4");
        string clone;
        using (EstateService api = EstateService.Open(f.Estate.Root))
        {
            string device = api.DeviceCreate(new DeviceCreateRequest(f.Estate.OperatorId, "Operator's laptop", "file")).Written!.Id;
            Assert.True(api.DeviceAdmit(new AdmitRequest(device, f.Estate.OperatorId, "The first authority.")).Ok);
            Assert.True(api.ObserverDeclare(new(f.Estate.OperatorId, "ci-main", "ci", 3600, 900)).Ok);
            clone = Path.GetDirectoryName(api.Session.Live.Path)!;
        }

        f.Follow(version: "0.9.1");
        Assert.True(File.Exists(Path.Combine(clone, "live.db")));
        Assert.False(File.Exists(Path.Combine(clone, "live.db-wal")));
        Assert.False(File.Exists(Path.Combine(clone, "daemon.json")));
        return clone;
    }

    private static string[] Snapshot(string root) => [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))).Order(StringComparer.Ordinal)];
}
