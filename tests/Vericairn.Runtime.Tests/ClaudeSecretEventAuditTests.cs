// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class ClaudeSecretEventAuditTests
{
    [Theory]
    [InlineData("control")]
    [InlineData("cloud")]
    [InlineData("generic")]
    [InlineData("setup")]
    public void SecretTextCannotHideUsageOrCriticalSignals(string shape)
    {
        using var fixture = new NativeRun("claude", recordFormat: 3, qualify: true, setupToken: FakeHarness.NewClaudeSetupToken());
        fixture.Probe.Behavior("secretAudit", shape);

        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Contains(settled.Signals!, signal => signal.Code == "tool-inventory" && signal.Severity == "critical");
        Assert.Contains(settled.Signals!, signal => signal.Code == "credentials" && signal.Severity == "critical");
        AgentRuns.Run recorded = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!;
        Assert.Equal(1_000_000, recorded.Latest.Body.GetProperty("usage").GetProperty("tokens").GetInt64());
        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        bool secretRetained = SecretShapes.Secret().IsMatch(File.ReadAllText(directory.OutputLog));
        Assert.False(secretRetained);
    }

    [Fact]
    public void MalformedSecretBearingEventLeavesTheAuditUnverified()
    {
        using var fixture = new NativeRun("claude", recordFormat: 3, qualify: true, setupToken: FakeHarness.NewClaudeSetupToken());
        fixture.Probe.Behavior("secretAudit", "malformed");

        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Contains(settled.Signals!, signal => signal.Code == "audit-unverified" && signal.Severity == "warning");
        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        Assert.Null(directory.Read()!.Tokens);
        bool secretRetained = SecretShapes.Secret().IsMatch(File.ReadAllText(directory.OutputLog));
        Assert.False(secretRetained);
    }
}
