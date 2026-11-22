// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

/// <summary>OPS-21 through the command line: worker drill stops a scratch run and prints both times and the verification it recorded.</summary>
[Collection("cli")]
public sealed class WorkerDrillCommandTests
{
    [Fact]
    public void TheDrillNamesItsPrincipalAndPrintsBothTimesAndTheVerification()
    {
        Assert.Equal(0, CliHarness.Run("worker", "drill", "--help").Code);
        Command drill = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Subcommands.Single(c => c.Name == "worker").Subcommands.Single(c => c.Name == "drill");
        Assert.True(drill.Options.Single(o => o.Name == "--by").Required);
        Assert.False(drill.Options.Single(o => o.Name == "--grace").Required);
        Assert.False(drill.Options.Single(o => o.Name == "--limit").Required);

        using var estate = new TestEstate(2);
        using EstateService api = EstateService.Open(estate.Root);
        api.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(50), Environment = WorkerEnvironment.Current(), Vericairn = FakeHarness.ScopeCommand() };
        string device = api.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator's laptop", "file")).Written!.Id;
        Assert.True(api.DeviceAdmit(new AdmitRequest(device, estate.OperatorId, "The first authority.")).Ok);

        CliHarness.Outcome passed = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "worker", "drill", "--by", estate.OperatorId, "--grace", "0");
        Assert.Equal(0, passed.Code);
        Assert.Contains("records/verification/", passed.Out, StringComparison.Ordinal);
        Assert.Contains(", grace 0 s, limit 60 s", passed.Out, StringComparison.Ordinal);
        Assert.Matches(@"  contained \d+ ms after the stop", passed.Out);
        Assert.Matches(@"  last write \d+ ms after the stop, at ", passed.Out);
        Assert.Contains("  drill passed", passed.Out, StringComparison.Ordinal);

        CliHarness.Outcome late = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "--json", "worker", "drill", "--by", estate.OperatorId, "--grace", "600", "--limit", "1");
        Assert.Equal(1, late.Code);
        Assert.False(late.Json.GetProperty("passed").GetBoolean());
        Assert.True(late.Json.GetProperty("ok").GetBoolean());
        Assert.Equal(1, CliHarness.RunWithApi(api, null, "--estate", estate.Root, "worker", "drill", "--by", estate.BuilderId).Code);
    }
}
