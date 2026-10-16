// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunWatchingTests
{
    [Theory]
    [InlineData("claude-code", "watch-claude-audit")]
    [InlineData("codex", "watch-codex-audit")]
    public void TheWorkerRecordsEventStreamObservationsInsteadOfTheAgentsClaims(string launcher, string scenario)
    {
        using var run = new RunEstate(brief: RunEstate.Brief.Replace("the widget", "src/", StringComparison.Ordinal));
        RunResponse started = run.Start(launcher, scenario, run.Report("audit"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(["credentials", "network", "scope"], settled.Signals!.Select(s => s.Code).Order(StringComparer.Ordinal));
        Assert.All(settled.Signals!, s => Assert.Equal(1, s.Count));
        string record = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetRawText();
        Assert.DoesNotContain(".aws/credentials", record, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", record, StringComparison.Ordinal);
        Assert.DoesNotContain("I claim", record, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
        run.Reopen();
        Assert.Equal(3, run.Row(started.Run).Signals!.Count);
    }
}
