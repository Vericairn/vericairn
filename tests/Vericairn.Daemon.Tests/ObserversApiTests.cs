// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

/// <summary>OPS-20 through the daemon's routes: a declared observer that falls silent is the operator's attention until it reports again.</summary>
[Collection("daemon")]
public sealed class ObserversApiTests
{
    public ObserversApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task TheRoutesDeclareReportAndListAndASilentObserverIsTheOperatorsAttention()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        var clock = new Clock(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        host.Service.PolicyClock = clock;
        using var client = new Sdk.VericairnClient(host.Endpoint);

        ObserversResponse declared = await client.ObserverDeclareAsync(new(estate.OperatorId, "status", "status-refresh", 600, 120, "Refreshes the status discussion."), TestContext.Current.CancellationToken);
        Assert.True(declared.Ok, declared.Message);
        Assert.Equal("denied", client.ObserverDeclare(new(estate.BuilderId, "other", "other", 600)).Problem!.Code);

        clock.Now = clock.Now.AddMinutes(13);
        AttentionNotice silent = Assert.Single(client.AttentionGet(estate.OperatorId).Items, n => n.SubjectType == "observer");
        Assert.Equal(("failed check", "silent", "status"), (silent.Kind, silent.State, silent.SubjectId));
        Assert.DoesNotContain(client.AttentionGet(estate.BuilderId).Items, n => n.SubjectType == "observer");

        ObserversResponse reported = await client.ObserverReportAsync(new(estate.BuilderId, "status", "passed"), TestContext.Current.CancellationToken);
        Assert.True(reported.Ok, reported.Message);
        ObserverRow row = Assert.Single((await client.ObserversAsync(TestContext.Current.CancellationToken)).Observers);
        Assert.Equal(("observing", "passed"), (row.State, row.LastReport!.Outcome));
        Assert.DoesNotContain(client.AttentionGet(estate.OperatorId).Items, n => n.SubjectType == "observer");

        Assert.True(client.ObserverWithdraw(new(estate.OperatorId, "status", "Replaced by the progress target.")).Ok);
        Assert.Empty(client.Observers().Observers);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
