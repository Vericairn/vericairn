// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class QualifiedAdmissionTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void ARecordedQualificationAdmitsExactlyTheNewerVersionAndPinsTheIntent(string harness)
    {
        using var fixture = new NativeAdmissionTests.NativeRun(harness, newer: true, qualify: true);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        fixture.Run.Settled(started.Run);
        JsonElement execution = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!.Intent.Body.GetProperty("execution");
        JsonElement admission = execution.GetProperty("harnessAdmission");
        Assert.Equal("qualification", admission.GetProperty("kind").GetString());
        Assert.Equal(fixture.Qualification!.Id, admission.GetProperty("record").GetString());
        Assert.Equal(fixture.Qualification.Hash, admission.GetProperty("hash").GetString());
        Assert.Equal(execution.GetProperty("harnessVersion").GetString(), admission.GetProperty("version").GetString());
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void TheShippedBaselineIsNamedInTheIntent(string harness)
    {
        using var fixture = new NativeAdmissionTests.NativeRun(harness);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        fixture.Run.Settled(started.Run);
        JsonElement admission = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!.Intent.Body.GetProperty("execution").GetProperty("harnessAdmission");
        Assert.Equal("baseline", admission.GetProperty("kind").GetString());
        Assert.False(admission.TryGetProperty("record", out _));
    }

    [Theory]
    [InlineData("codex", "helpSuffix", "Changed help.")]
    [InlineData("claude", "helpSuffix", "Changed help.")]
    [InlineData("codex", "featuresSuffix", "new_disabled_tool stable false")]
    [InlineData("codex", "mcpAuth", "changed")]
    public void AnySuccessfulProbeDigestDriftRefusesBeforeIntent(string harness, string key, string value)
    {
        using var fixture = new NativeAdmissionTests.NativeRun(harness, newer: true, qualify: true);
        fixture.Probe.Behavior(key, value);
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok);
        Assert.Contains("probe", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("qualification", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Run.Service.Runs().Runs);
        Assert.False(File.Exists(fixture.Probe.Report));
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("claude", true)]
    public void UnqualifiedOrRevokedVersionsRefuse(string harness, bool revoked)
    {
        using var fixture = new NativeAdmissionTests.NativeRun(harness, newer: true, qualify: revoked);
        if (revoked) Revoke(fixture);
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok);
        Assert.Contains("not qualified", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Run.Service.Runs().Runs);
        Assert.False(File.Exists(fixture.Probe.Report));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void RevocationAfterIntentRefusesDispatchWithoutACharge(string harness)
    {
        NativeAdmissionTests.NativeRun? fixture = null;
        using (fixture = new(harness, (_, _) => Revoke(fixture!), newer: true, qualify: true))
        {
            RunResponse refused = fixture.Start();
            Assert.False(refused.Ok);
            Assert.Equal("known-failure", refused.Status);
            Assert.False(File.Exists(fixture.Probe.Report));
            Assert.Empty(fixture.Run.Row(refused.Run).Charged.EnumerateObject());
            Assert.Equal(0, fixture.Run.Service.Check().Errors);
        }
    }

    private static void Revoke(NativeAdmissionTests.NativeRun fixture)
    {
        var original = fixture.Run.Estate.Load().ById[fixture.Qualification!.Id];
        JsonObject body = JsonNode.Parse(original.Body.GetRawText())!.AsObject();
        body["status"] = "revoked";
        RunEstate.Ok(fixture.Run.Service.NewRecord(new("harness-qualification", fixture.Run.Estate.OperatorId,
            JsonSerializer.SerializeToElement(body), Supersedes: original.Id)));
    }
}
