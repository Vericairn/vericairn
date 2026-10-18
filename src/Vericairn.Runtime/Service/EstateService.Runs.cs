// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The worker contract, vericairn.worker/1 (the estate's design/goals.md § 7): one assignment carried by a coding agent through a
/// launcher. Prepare a workspace on a work branch, never the shared clone, with the brief exported into it, the MCP server
/// configured for the agent's principal, an environment from the allowlist and the run's cap from its goal's budget; record the
/// intent, an operation of action agent-run, before anything starts; launch; observe heartbeats, usage and the exit, a lost
/// connection never counting as a stop; and settle the operation with the observed effect and usage. The worker accepts nothing:
/// the agent's result is a claim another principal reviews.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>How the worker paces and places the runs this service supervises.</summary>
    public WorkerOptions WorkerOptions { get; set; } = WorkerOptions.Default;

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Launchers() => ProviderRows().Where(r => r.Kind == "launcher").ToList();

    /// <inheritdoc />
    public RunResponse RunStart(RunStartRequest request) => runDispatch.Start(request, null, out _);

    /// <inheritdoc />
    public RunsResponse Runs() => runDispatch.Runs();

    private RunsResponse Runs(EstateModel model, bool includeLocalObservations = true) => runDispatch.Runs(model, includeLocalObservations);

    /// <inheritdoc />
    public RunStopResponse RunStop(string run, RunStopRequest request) => runDispatch.RunStop(run, request);
}
