// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

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
}
