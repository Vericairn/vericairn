// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using System.Text.Json;

namespace Vericairn.Runtime.Workers;

/// <summary>What a launcher is given to start a coding agent on one assignment; every path is on the machine that runs it.</summary>
/// <param name="Workspace">The prepared workspace: the harness's working directory, a worktree on the run's work branch.</param>
/// <param name="Condition">The condition the harness's own /goal runs with: the work order's exit question with its checks and constraints, on one line.</param>
/// <param name="Brief">The brief exported into the workspace.</param>
/// <param name="McpConfiguration">The MCP configuration the worker wrote outside the workspace: one server, vericairn, serving the estate for the agent's principal.</param>
/// <param name="McpServer">The MCP server's command line: the vericairn command, then --estate and the estate's root, mcp serve, --as and the agent.</param>
/// <param name="McpEnvironment">The variables the MCP server needs to find the agent's key, which its configuration passes it.</param>
/// <param name="Settings">The launcher's settings for this run, as the request names them.</param>
/// <param name="Limits">Operational thresholds below the reserved cap, passed to supported native harness controls.</param>
/// <param name="Execution">The admitted execution selection.</param>
/// <param name="Environment">The environment the harness is started with, built from the allowlist.</param>
public sealed record LaunchContext(string Workspace, string Condition, string Brief, string McpConfiguration, IReadOnlyList<string> McpServer, IReadOnlyDictionary<string, string> McpEnvironment, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Environment, RunLimits? Limits = null, JsonElement? Execution = null);

/// <summary>What one line of a harness's output told the worker; a field left null told nothing.</summary>
/// <param name="Tokens">The tokens the run has spent, as a total that replaces the tally.</param>
/// <param name="TokensAdded">The tokens one turn spent, added to the tally.</param>
/// <param name="Price">What the run has cost, in minor units, as a total; only a launcher whose provider reports price sets it.</param>
/// <param name="PriceUnit">The price's currency, three upper-case letters.</param>
/// <param name="UsageIncomplete">A usage event was missing or invalid; the run must be charged at its token cap.</param>
/// <param name="UsageStarted">A turn began whose usage is in flight.</param>
/// <param name="UsageCompleted">A turn ended.</param>
/// <param name="Acknowledged">The harness acknowledged a stop.</param>
/// <param name="UsageId">A message identity for a provisional usage snapshot.</param>
/// <param name="UsageTokens">The latest provisional token count for that identity, not a second charge.</param>
public sealed record HarnessSignal(long? Tokens = null, long? TokensAdded = null, long? Price = null, string? PriceUnit = null, bool Acknowledged = false, bool UsageIncomplete = false, bool UsageStarted = false, bool UsageCompleted = false, string? UsageId = null, long? UsageTokens = null);

/// <summary>A tool invocation from a harness event, never from an assistant's narrative or a tool's output.</summary>
/// <param name="Id">The harness's invocation identity, used to deduplicate lifecycle events.</param>
/// <param name="Tool">The tool invoked.</param>
/// <param name="Input">Its input, kept on this machine; raw arguments never enter a record.</param>
public sealed record HarnessAction(string Id, string Tool, JsonElement Input);

/// <summary>
/// The coding-agent launcher contract, version 1 (vericairn.launcher.agent/1): how one harness is verified before a run is recorded,
/// the command line that starts it on an assignment, and what its output reports. The worker (vericairn.worker/1) does everything
/// else: it prepares the workspace, records the intent, starts the process with the environment it builds, observes it, stops it
/// and settles the run. A launcher records nothing, selects no other launcher, and never accepts the work.
/// </summary>
public interface IAgentLauncher
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.launcher.agent/1";

    /// <summary>The words that label a generic launcher wherever it is listed or run.</summary>
    const string GenericLabel = "generic process launch is not complete agent support";

    /// <summary>The launcher this implementation serves, recorded as the provider of every run it launches: for example claude-code.</summary>
    string Launcher { get; }

    /// <summary>What this implementation is, with the settings it takes.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>True for a launcher that starts any process rather than a harness it knows: listed and run with <see cref="GenericLabel"/>.</summary>
    bool Generic => false;

    /// <summary>
    /// The harness it starts, as an agent principal's product names it (Claude Code, Codex): routing gives a step to an agent only where
    /// a launcher for its harness is available (design/goals.md § 6). Null for a launcher that names no harness, such as the generic
    /// one, which serves no agent by itself.
    /// </summary>
    string? Harness => null;

    /// <summary>Scopes and execution values supported by this implementation; no declaration means no dispatch.</summary>
    RunCapabilities ExecutionCapabilities => RunCapabilities.Unsupported;

    /// <summary>An explicitly configured default, if the implementation supplies one; never the ambient harness configuration.</summary>
    RunConfiguration? Configuration => null;

    /// <summary>The harness's own configuration variables that pass to it beside the worker's allowlist; never a credential, which never passes.</summary>
    IReadOnlyList<string> EnvironmentNames => [];

    /// <summary>
    /// Why the harness cannot run here, checked before the run is recorded: for example a feature the launch needs that is not enabled,
    /// or that could not be verified. Null when it can run.
    /// </summary>
    string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => null;

    /// <summary>The launcher's default silence limit; a goal may override it.</summary>
    TimeSpan StallLimit => TimeSpan.FromMinutes(10);

    /// <summary>Default command-loop and token-rate thresholds, overridden by recorded goal and roster limits.</summary>
    RunWatchSettings WatchDefaults => new();

    /// <summary>Native spending controls used by this launcher; unlisted units have no native enforcement.</summary>
    string Limitations => "No native token or currency limit; the worker uses soft accounting thresholds only.";

    /// <summary>Builds the run-specific harness configuration and environment outside the workspace before launch.</summary>
    LaunchContext Prepare(LaunchContext context) => context;

    /// <summary>Establishes the complete effective inventory before intent. An unobservable inventory is unsupported.</summary>
    RunPreflight Preflight(LaunchContext context) => throw new ProviderRefusedException("This launcher cannot establish a complete tool, configuration, dependency and route inventory; RUN-01 dispatch is unsupported.");

    /// <summary>Runs the same read-only probes without admitting a version. The worker must establish baseline or recorded qualification before dispatch.</summary>
    RunPreflight Probe(LaunchContext context) => Preflight(context);

    /// <summary>Reported execution facts, without inferring them from usage, prose or the requested values.</summary>
    HarnessExecution? ReadExecution(string line) => null;

    /// <summary>Execution facts from this run's local artifacts after the whole harness boundary exits. Missing or unreadable artifacts report no facts; never infer the requested values.</summary>
    IReadOnlyList<HarnessExecution> ReadExecutionArtifacts(string runDirectory) => [];

    /// <summary>The command line that starts the harness on the assignment: the executable first, then its arguments.</summary>
    IReadOnlyList<string> Command(LaunchContext context);

    /// <summary>What one line of the harness's standard output reports; null when it reports nothing the worker reads.</summary>
    HarnessSignal? Read(string line) => null;

    /// <summary>Tool invocations in a harness event; ordinary messages and tool results return no actions.</summary>
    IReadOnlyList<HarnessAction> ReadActions(string line) => [];
}
