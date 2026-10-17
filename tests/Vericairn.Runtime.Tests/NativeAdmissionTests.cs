// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class NativeAdmissionTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void LaunchedHarnessSeesEndOfInput(string harness)
    {
        using var fixture = new NativeRun(harness);
        fixture.Probe.Behavior("stdin", "eof");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);
        Assert.Equal("eof", File.ReadAllText(fixture.Probe.Report + ".stdin"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexMaxIsAdmittedButAHarnessRefusalSettlesAsFailure(bool refuseAtLaunch)
    {
        using var fixture = new NativeRun("codex", budget: """{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        var store = new WorkerConfigurationStore();
        string path = Path.Combine(VericairnHome.Path(), "worker", "codex.json");
        Assert.False(File.Exists(path));
        try
        {
            store.Write(fixture.Probe.Launcher, new("gpt-6-astra", "max"));
            fixture.Run.Service.WorkerOptions = fixture.Run.Service.WorkerOptions with
            {
                Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal),
            };
            if (refuseAtLaunch) fixture.Probe.Behavior("refuseEffort", "max");
            RunResponse started = fixture.Start();
            Assert.True(started.Ok, started.Message);
            RunRow settled = fixture.Run.Settled(started.Run);
            Assert.Equal(refuseAtLaunch ? "known-failure" : "observed-success", settled.Status);
            string[] arguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(fixture.Probe.Report))!;
            Assert.Contains("model_reasoning_effort=\"max\"", arguments);
            JsonElement requested = settled.Execution!.Value.GetProperty("requested");
            Assert.Equal("gpt-6-astra", requested.GetProperty("model").GetString());
            Assert.Equal("max", requested.GetProperty("effort").GetString());
            var records = AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!;
            Assert.Equal(1000, records.Intent.Body.GetProperty("cap").GetProperty("tokens").GetInt64());
            if (refuseAtLaunch)
            {
                Assert.Contains("exited with code 2", records.Latest.Body.GetProperty("observed").GetProperty("summary").GetString(), StringComparison.Ordinal);
                Assert.Contains("effort", settled.ExecutionObserved!.Value.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
                Assert.Equal(1000, settled.Charged.GetProperty("tokens").GetInt64());
            }
            Assert.Equal(0, fixture.Run.Service.Check().Errors);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingLocalConfigurationDuringPreparationRequiresFreshAdmission(bool afterIntent)
    {
        using var fixture = new NativeRun("codex");
        var store = new WorkerConfigurationStore();
        string path = Path.Combine(VericairnHome.Path(), "worker", "codex.json");
        Assert.False(File.Exists(path));
        try
        {
            store.Write(fixture.Probe.Launcher, new("model-a", "high"));
            fixture.Run.Service.WorkerOptions = fixture.Run.Service.WorkerOptions with
            {
                Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal),
            };
            void Change(LaunchContext _) => store.Write(fixture.Probe.Launcher, new("model-b", "high"));
            fixture.Run.Service.Providers.Register(new HookedLauncher(fixture.Probe.Launcher,
                beforeCommand: afterIntent ? Change : null, probed: afterIntent ? null : Change));
            RunResponse refused = fixture.Start();
            Assert.False(refused.Ok);
            Assert.Equal(afterIntent ? "known-failure" : "not-started", refused.Status);
            Assert.False(File.Exists(fixture.Probe.Report));
            if (afterIntent) Assert.Empty(fixture.Run.Row(refused.Run).Charged.EnumerateObject());
            else Assert.Empty(fixture.Run.Service.Runs().Runs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("codex", false, false)]
    [InlineData("claude", false, false)]
    [InlineData("codex", true, true)]
    [InlineData("claude", true, true)]
    [InlineData("codex", true, false)]
    [InlineData("claude", true, false)]
    public void OnlyAnAdmittedNativeInventorySuppliesTheDefaultEnvironmentPolicy(string harness, bool newer, bool qualified)
    {
        using var fixture = new NativeRun(harness, newer: newer, qualify: qualified);
        string launcher = fixture.Probe.Launcher.Launcher;
        WorkerOptions options = fixture.Run.Service.WorkerOptions;
        fixture.Run.Service.WorkerOptions = options with
        {
            Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal)
            {
                [launcher] = options.Configurations[launcher] with { EnvironmentPolicy = null },
            },
        };
        RunResponse started = fixture.Start();
        if (newer && !qualified)
        {
            Assert.False(started.Ok);
            Assert.Empty(fixture.Run.Service.Runs().Runs);
            Assert.False(File.Exists(fixture.Probe.Report));
            return;
        }
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        JsonElement execution = settled.Execution!.Value;
        Assert.Equal("qualified-" + launcher, execution.GetProperty("environment").GetProperty("policy").GetString());
        Assert.Equal(qualified ? "qualification" : "baseline", execution.GetProperty("harnessAdmission").GetProperty("kind").GetString());
        if (qualified) Assert.Equal(fixture.Qualification!.Hash, execution.GetProperty("harnessAdmission").GetProperty("hash").GetString());
    }

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
