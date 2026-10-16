// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>Runs production inventory admission and launchers against native-protocol stand-ins only.</summary>
internal sealed class NativeRun : IDisposable
{
    public NativeRun(string harness, Action<NativeProbe, LaunchContext>? beforeDispatch = null, string? budget = null, bool keepOperatorKey = false)
    {
        Probe = new(harness);
        Run = new(budget: budget ?? """{"wallMinutes":60,"attemptsPerStep":3}""", keepOperatorKey: keepOperatorKey);
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
        Run.Service.Providers.Register(new HookedLauncher(Probe.Launcher, beforeCommand: c => beforeDispatch?.Invoke(Probe, c)));
    }

    public NativeProbe Probe { get; }
    public RunEstate Run { get; }
    public RunResponse Start() => Run.Start(Probe.Launcher.Launcher, Probe.Launcher.Launcher == "codex" ? "native-codex" : "native-claude", Probe.Report);
    public void Dispose() { Run.Dispose(); Probe.Dispose(); }
}
