// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class QualifiedWatchTests
{
    [Theory]
    [InlineData("codex", false)]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("claude", true)]
    public void QualifiedRunsKeepRun01SelectionAndInventoryAndWatch01ToolChecks(string harness, bool ambient)
    {
        using var fixture = new NativeRun(harness, newer: true, qualify: true);
        fixture.Probe.Behavior("actions", ambient ? "ambient" : "admitted");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal(ambient ? "known-failure" : "observed-success", settled.Status);
        if (ambient)
        {
            Assert.Contains(settled.Signals!, s => s.Code is "tool-inventory" or "execution" && s.Severity == "critical");
            Assert.NotNull(settled.Stop!.ContainedAt);
        }
        else
        {
            Assert.Equal("attempt-missing", Assert.Single(settled.Signals!).Code);
            Assert.Empty(settled.ExecutionObserved!.Value.GetProperty("discrepancies").EnumerateArray());
        }
        JsonElement execution = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!.Intent.Body.GetProperty("execution");
        JsonElement environment = execution.GetProperty("environment");
        JsonElement inventory = environment.GetProperty("inventory");
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(inventory)), environment.GetProperty("inventoryHash").GetString());
        Assert.Equal("model-a", execution.GetProperty("requested").GetProperty("model").GetString());
        Assert.Equal("disabled", execution.GetProperty("adapter").GetString());
        Assert.NotEmpty(execution.GetProperty("provenance").EnumerateArray());
        Assert.Contains(inventory.EnumerateArray(), c => c.GetProperty("kind").GetString() == "mcp" && c.GetProperty("id").GetString() == "vericairn");
        Assert.All(inventory.EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "tool"), c =>
            Assert.Equal(harness == "codex" ? "0.160.0" : "2.1.297", c.GetProperty("version").GetString()));
        Assert.Equal(fixture.Qualification!.Id, execution.GetProperty("harnessAdmission").GetProperty("record").GetString());
        Assert.DoesNotContain(fixture.Probe.Root, execution.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }
}
