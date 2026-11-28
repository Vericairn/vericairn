// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Remote;

/// <summary>A worker's own observation or the absence of one for an in-flight run.</summary>
public sealed record RemoteWorkerNode(string Run, string State, string Observation);

public sealed partial class RemoteViewModel
{
    private bool nodesCurrent;
    private IReadOnlyList<RemoteWorkerNode> workers = [];
    private string workersMessage = "";

    /// <summary>The connection transport shipped by this build.</summary>
    public static string NodePath => "Direct local-network connection";

    /// <summary>Which in-flight work the selected runtime can observe.</summary>
    public IReadOnlyList<RemoteWorkerNode> Workers { get => workers; private set => Set(ref workers, value); }

    /// <summary>Work impact and freshness of the worker read, which are independent of the phone connection.</summary>
    public string WorkersMessage { get => workersMessage; private set => Set(ref workersMessage, value); }

    /// <summary>Read the connected runtime's live worker observations.</summary>
    public AsyncCommand CheckWorkersCommand { get; private set; } = null!;

    private void InitializeNodeCommands() =>
        CheckWorkersCommand = Command(CheckWorkersAsync, () => !Busy && Selected?.Current == true);

    private void ShowWorkers()
    {
        if (Selected?.Observation is not { WorkerRuns: { } runs, WorkersAsOf: { } at } || Selected is not { } estate)
        {
            Workers = [];
            WorkersMessage = "Worker observations are unavailable. Check workers to read this runtime's view of in-flight work.";
        }
        else
        {
            bool current = nodesCurrent && estate.Current;
            Workers = runs.Select(run => new RemoteWorkerNode(run.Run, StateLanguage.Label("operation", run.Status),
                (run.Observed ? "Worker reported current at the time of this read." : "No current worker was observed by this runtime.") +
                "\nHeartbeat " + (run.Heartbeat ?? "not observed") +
                (run.Stop is { } stop ? "\n" + StopObservation(stop) : "") +
                (run.Signals is null ? "" : "\n" + string.Join("\n", run.Signals.Select(s => s.Summary))))).ToList();
            WorkersMessage = $"{(current ? "Observed" : "Cached")} {at}. " +
                (Workers.Count == 0 ? "No in-flight runs were reported by this runtime at that time." :
                    $"{Workers.Count} in-flight work {(Workers.Count == 1 ? "item" : "items")}. Worker observations are the runtime's own.");
        }
        CheckWorkersCommand?.NotifyCanExecuteChanged();
    }

    private async Task CheckWorkersAsync()
    {
        if (Selected is not { Current: true, Observation: { } observation } estate) return;
        Busy = true;
        nodesCurrent = false;
        try
        {
            if (await CheckRuntimeAsync(estate, allowPartial: true).ConfigureAwait(true) is not { } api)
            {
                if (Selected == estate) ShowWorkers();
                return;
            }

            IReadOnlyList<RunRow> runs = (await api.RunsAsync().ConfigureAwait(true)).Runs.Where(r =>
                r.Status is "not-started" or "dispatched" or "uncertain").ToList();
            estate.Observation = observation with { WorkerRuns = runs, WorkersAsOf = Now };
            Keep(estate);
            if (Selected == estate)
            {
                nodesCurrent = true;
                ShowWorkers();
            }
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate)
            {
                ShowWorkers();
                WorkersMessage = "Worker observation could not be refreshed. " + WorkersMessage;
            }
        }
        finally { Busy = false; }
    }
}
