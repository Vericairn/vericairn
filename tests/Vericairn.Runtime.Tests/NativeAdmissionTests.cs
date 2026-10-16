// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class NativeAdmissionTests
{
    [Theory]
    [InlineData("codex", false)]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("claude", true)]
    public void WatchUsesTheNativeInventoryForBuiltinsAndExactMcpServers(string harness, bool ambient)
    {
        using var fixture = new NativeRun(harness);
        fixture.Probe.Behavior("actions", ambient ? "ambient" : "admitted");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal(ambient ? "known-failure" : "observed-success", settled.Status);
        if (ambient)
        {
            // Either the report or the invocation can arrive first and trigger containment.
            Assert.Contains(settled.Signals!, s => s.Code is "tool-inventory" or "execution" && s.Severity == "critical");
            Assert.NotNull(settled.Stop!.ContainedAt);
        }
        else
        {
            Assert.Empty(settled.Signals!);
            Assert.Empty(settled.ExecutionObserved!.Value.GetProperty("discrepancies").EnumerateArray());
        }
        var custody = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(custody.ContextFile));
        Assert.Equal(retained.RootElement.GetProperty("context").GetProperty("hash").GetString(), settled.Execution!.Value.GetProperty("contextPackage").GetString());
        Assert.Empty(fixture.Run.Service.Objects());
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void NativeInventoriesAreAdmittedWithTheirIdentityAndRecordedBeforeDispatch(string harness)
    {
        using var fixture = new NativeRun(harness);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        fixture.Run.Settled(started.Run);
        var intent = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!.Intent;
        JsonElement execution = intent.Body.GetProperty("execution");
        JsonElement environment = execution.GetProperty("environment");
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(environment.GetProperty("inventory"))), environment.GetProperty("inventoryHash").GetString());
        Assert.Equal(harness == "codex" ? "0.159.0-alpha.12.1" : "2.1.296", execution.GetProperty("harnessVersion").GetString());
        Assert.Equal("native-test-policy", environment.GetProperty("policy").GetString());
        Assert.DoesNotContain(fixture.Probe.Root, intent.Body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("localBinding", intent.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fixture.Probe.Report));
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex", "instruction")]
    [InlineData("claude", "instruction")]
    [InlineData("codex", "mcp")]
    [InlineData("claude", "mcp")]
    [InlineData("codex", "home")]
    [InlineData("claude", "home")]
    [InlineData("codex", "help")]
    [InlineData("claude", "help")]
    [InlineData("codex", "binding")]
    [InlineData("claude", "binding")]
    public void NativeInventoryChangesBetweenAdmissionAndDispatchRefuseWithoutACharge(string harness, string change)
    {
        using var fixture = new NativeRun(harness, (probe, context) =>
        {
            switch (change)
            {
                case "instruction": File.WriteAllText(Path.Combine(context.Workspace, "AGENTS.md"), "new content"); break;
                case "mcp": File.WriteAllText(context.McpConfiguration, "{\"mcpServers\":{}}"); break;
                case "home": File.WriteAllText(Path.Combine(context.Environment[harness == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"], "hooks.json"), "{}"); break;
                case "help": probe.Behavior("helpSuffix", "Changed help evidence."); break;
                case "binding": ((IDictionary<string, string>)context.Environment)["LANG"] = "changed-binding"; break;
            }
        });
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok, refused.Message);
        Assert.Equal("known-failure", refused.Status);
        Assert.False(File.Exists(fixture.Probe.Report));
        Assert.Empty(fixture.Run.Row(refused.Run).Charged.EnumerateObject());
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }
}
