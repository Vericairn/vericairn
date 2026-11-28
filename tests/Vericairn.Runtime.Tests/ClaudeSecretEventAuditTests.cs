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
    [InlineData("provision-fragment")]
    public void SecretTextCannotHideUsageOrCriticalSignals(string shape)
    {
        using var fixture = new NativeRun("claude", recordFormat: 3, qualify: true, setupToken: FakeHarness.NewClaudeSetupToken());
        fixture.Probe.Behavior("secretAudit", shape);

        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Contains(settled.Signals!, signal => signal.Code == "tool-inventory" && signal.Severity == "critical");
        RunSignal credentials = Assert.Single(settled.Signals!, signal => signal.Code == "credentials" && signal.Severity == "critical");
        Assert.Equal(shape == "control", credentials.Fingerprint is not null);
        AgentRuns.Run recorded = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!;
        Assert.Equal(1_000_000, recorded.Latest.Body.GetProperty("usage").GetProperty("tokens").GetInt64());
        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        bool secretRetained = SecretShapes.Secret().IsMatch(File.ReadAllText(directory.OutputLog));
        Assert.False(secretRetained);
    }

    [Fact]
    public void WrappedSecretsAreRemovedFromBothStreamsAndTheNextResultIsObserved()
    {
        const int split = 9;
        string token = FakeHarness.NewClaudeSetupToken();
        using var fixture = new NativeRun("claude", recordFormat: 3, qualify: true, setupToken: token);
        fixture.Probe.Behavior("setupToken", "wrapped-output");

        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);
        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string output = File.ReadAllText(directory.OutputLog);
        Assert.False(output.Contains(token[..split], StringComparison.Ordinal));
        Assert.False(output.Contains(token[split..], StringComparison.Ordinal));
        Assert.Equal(0, directory.Read()!.Tokens);
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
