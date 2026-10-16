// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
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

    private sealed class NativeRun : IDisposable
    {
        public NativeRun(string harness, Action<NativeProbe, LaunchContext>? beforeDispatch = null)
        {
            Probe = new(harness);
            Run = new();
            var environment = new Dictionary<string, string>(Probe.Context.Environment, WorkerEnvironment.Names) { ["LANG"] = "C" };
            string[] variables = ["VERICAIRN_RUN", "VERICAIRN_STOP_FILE", "VERICAIRN_BRIEF", "VERICAIRN_CONDITION"];
            foreach (string name in variables) environment[name] = "synthetic-binding";
            var serverEnvironment = WorkerEnvironment.McpServer.Where(environment.ContainsKey).ToDictionary(n => n, n => environment[n], StringComparer.Ordinal);
            IReadOnlyList<string> server = [.. Probe.Context.McpServer, "--estate", Run.Estate.Root, "mcp", "serve", "--as", Run.Estate.BuilderId];
            LaunchContext inspection = Probe.Context with { Environment = environment, McpServer = server, McpEnvironment = serverEnvironment };
            File.WriteAllText(inspection.McpConfiguration, JsonSerializer.Serialize(new { mcpServers = new { vericairn = new { command = server[0], args = server.Skip(1), env = serverEnvironment } } }));
            RunPreflight inventory = Probe.Launcher.Preflight(Probe.Launcher.Prepare(inspection));
            var configuration = new ReportedLauncher(Probe.Launcher).Configuration with { EnvironmentPolicy = new("native-test-policy", inventory.Components) };
            Run.Service.WorkerOptions = Run.Service.WorkerOptions with { Environment = environment, Vericairn = Probe.Context.McpServer,
                Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal) { [Probe.Launcher.Launcher] = configuration } };
            LaunchContext? prepared = null;
            Run.Service.Providers.Register(new HookedLauncher(Probe.Launcher, command: () => beforeDispatch?.Invoke(Probe, prepared!), prepared: c => prepared = c));
        }

        public NativeProbe Probe { get; }
        public RunEstate Run { get; }
        public RunResponse Start() => Run.Start(Probe.Launcher.Launcher, Probe.Launcher.Launcher == "codex" ? "native-codex" : "native-claude", Probe.Report);
        public void Dispose() { Run.Dispose(); Probe.Dispose(); }
    }
}
