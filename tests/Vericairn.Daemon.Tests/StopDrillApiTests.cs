// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Daemon.Tests;

/// <summary>OPS-21 through the daemon's route: the runtime's own worker runs the drill over its local socket and records the verification.</summary>
[Collection("daemon")]
public sealed class StopDrillApiTests
{
    public StopDrillApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task TheRuntimesWorkerRunsTheDrillAndRecordsItsVerification()
    {
        using var estate = new TestEstate(2);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        host.Service.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(50), Environment = WorkerEnvironment.Current(), Vericairn = FakeHarness.ScopeCommand() };
        string device = host.Service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator's laptop", "file")).Written!.Id;
        Assert.True(host.Service.DeviceAdmit(new AdmitRequest(device, estate.OperatorId, "The first authority.")).Ok);
        using var client = new Sdk.VericairnClient(host.Endpoint);

        StopDrillResponse drill = await client.StopDrillAsync(new(estate.OperatorId, GraceSeconds: 0), TestContext.Current.CancellationToken);
        Assert.True(drill.Ok, drill.Message);
        Assert.True(drill.Passed, drill.Message);
        Assert.NotNull(drill.StopToContainedMilliseconds);
        Assert.NotNull(drill.StopToLastWriteMilliseconds);
        Assert.Equal("stop-drill", client.Record(drill.Verification!.Id)!.Envelope.GetProperty("body").GetProperty("method").GetString());
    }
}
