// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunExecutionWatchTests
{
    [Fact]
    public void ExecutionDiscrepanciesCannotHideTheOriginalCommandAudit()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "execution-audit", run.Report("audit"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Contains(settled.Signals!, s => s.Code == "credentials" && s.Severity == "critical");
        Assert.Contains(settled.Signals!, s => s.Code == "network");
        Assert.Contains(settled.Signals!, s => s.Code == "execution" && s.Severity == "critical");
        Assert.Contains(settled.ExecutionObserved!.Value.GetProperty("discrepancies").EnumerateArray(), d => d.GetString() == "model");
        Assert.Equal("known-failure", settled.Status);
        Assert.NotNull(settled.Stop);
        Assert.All(settled.Signals!, s => Assert.NotNull(s.Attention));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnUnadmittedToolIsAuditedAndSignalsTheExistingStopPath()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "execution-tool", run.Report("tool"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Contains(settled.Signals!, s => s.Code == "tool-inventory" && s.Severity == "critical");
        Assert.Contains(settled.Signals!, s => s.Code == "credentials");
        Assert.Equal("known-failure", settled.Status);
        Assert.NotNull(settled.Execution);
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
