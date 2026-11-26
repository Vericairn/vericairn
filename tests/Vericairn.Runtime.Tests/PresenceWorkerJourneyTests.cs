// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class PresenceWorkerJourneyTests
{
    [Fact]
    [Trait("Journey", "J135")]
    public void OperatorCloseAsksThreeChecksWhileTheSameUserHasAnUnsettledWorkerRun()
    {
        using var worker = new RunEstate(recordFormat: 3);
        using var closing = new OperatorCloseFixture();
        closing.Add("STEP-1");
        closing.Add("STEP-2");
        closing.Add("STEP-3");
        var custody = new RecordingCustody(VericairnHome.Path(), SecureEnclaveCustody.Kind);
        var helpers = new List<PresenceHelperStandIn>();
        closing.Api.KeyStores = [custody];
        closing.Api.PresenceRequired = device => device == closing.Device;
        closing.Api.HostsEmbedded = true;
        closing.Api.PresenceHost = StandInSessions.Host(helpers, agentRun: new PresenceSessionHost().SameUserAgentRun);
        string report = worker.Report("in-flight-presence");
        RunResponse running = worker.Start("claude-code", "ack-stop", report, wallMinutes: 10);
        Assert.True(running.Ok, running.Message);

        try
        {
            worker.WaitUntil(() => worker.Row(running.Run).Observed && File.Exists(report));
            Assert.True(CustodyGuard.UnsettledAgentRuns());
            OperatorCloseResponse closed = closing.Api.OperatorClose(closing.Request(), TestContext.Current.CancellationToken);
            Assert.True(closed.Ok, closed.Problem?.Message);
            Assert.Equal(3, closed.Written.Count);
            Assert.Equal(3, custody.Reasons.Count);
            Assert.Empty(helpers);
        }
        finally
        {
            worker.Service.RunStop(running.Run, new RunStopRequest(worker.Estate.OperatorId, "End the fixture worker.", 5));
            worker.Settled(running.Run);
        }
    }
}
