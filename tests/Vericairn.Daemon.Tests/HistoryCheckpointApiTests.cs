// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// <c>POST /v1/history/checkpoints</c> through the daemon (OPS-07): the SDK sees the plan the embedded runtime gives, and a refused
/// adoption comes back as the runtime's problem, with nothing written.
/// </summary>
[Collection("daemon")]
public class HistoryCheckpointApiTests
{
    public HistoryCheckpointApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheDaemonServesTheAdoptionCheckAndRefusesAnAgentsAdoption()
    {
        using var f = new HistoryFixture();
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Root)!);

        (HistoryCheckpointResponse? embedded, _) = host.Service.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        (HistoryCheckpointResponse? served, Problem? problem) = client.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        Assert.Null(problem);
        Assert.False(served!.Applied);
        Assert.Equal(embedded!.Witnessed, served.Witnessed);
        Assert.Equal(embedded.Anchored.Select(a => a.Id), served.Anchored.Select(a => a.Id));
        Assert.NotEmpty(served.Anchored);

        (HistoryCheckpointResponse? agents, Problem? refused) = client.HistoryCheckpoint(new HistoryCheckpointRequest(f.Estate.BuilderId, Adopt: true));
        Assert.Null(agents);
        Assert.Equal("refused", refused!.Code);
        Assert.Contains("never an agent", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Service.Session.Load().Records, r => r.Type == "history-checkpoint");
    }
}
