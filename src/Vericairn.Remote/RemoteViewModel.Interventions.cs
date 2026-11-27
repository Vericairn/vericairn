// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private const string PauseGoal = "Pause goal";
    private const string CancelRun = "Cancel run";
    private const string ContainRun = "Contain run";
    private RemoteInterventionTarget? interventionTarget;
    private RemoteIntervention? intervention;
    private string interventionMessage = "";
    private IReadOnlyList<RemoteInterventionReceipt> interventions = [];
    private RemoteInterventionReceipt? selectedIntervention;
    private string interventionStorageProblem = "";

    /// <summary>Exact runs from the opened work detail.</summary>
    public IReadOnlyList<RemoteInterventionTarget> InterventionTargets =>
        WorkDetail?.Runs.Select(r => new RemoteInterventionTarget(r.Run, StateLanguage.Label("operation", r.Status))).ToList() ?? [];

    /// <summary>Changes the run under review without sending anything.</summary>
    public RemoteInterventionTarget? InterventionTarget
    {
        get => interventionTarget;
        set
        {
            if (Set(ref interventionTarget, value)) SetIntervention(null);
        }
    }

    /// <summary>The reviewed target and expected effect.</summary>
    public RemoteIntervention? Intervention => intervention;

    /// <summary>Whether there is an operation to review before submission.</summary>
    public bool HasIntervention => intervention is not null;

    /// <summary>The latest refusal, request receipt or observation.</summary>
    public string InterventionMessage { get => interventionMessage; private set => Set(ref interventionMessage, value); }

    /// <summary>The connection's original intervention requests, including requests whose outcome remains unknown.</summary>
    public IReadOnlyList<RemoteInterventionReceipt> Interventions
    {
        get => interventions;
        private set => Set(ref interventions, value);
    }

    /// <summary>An original request to inspect or reconcile.</summary>
    public RemoteInterventionReceipt? SelectedIntervention
    {
        get => selectedIntervention;
        set { if (Set(ref selectedIntervention, value)) NotifyInterventionCommands(); }
    }

    /// <summary>Why app storage cannot retain original requests, when unavailable.</summary>
    public string InterventionStorageProblem { get => interventionStorageProblem; private set => Set(ref interventionStorageProblem, value); }

    /// <summary>Read the run's goal and review a pause of future dispatch.</summary>
    public AsyncCommand ReviewPauseCommand { get; private set; } = null!;
    /// <summary>Read the run and review a graceful stop with the runtime's grace period.</summary>
    public AsyncCommand ReviewCancelCommand { get; private set; } = null!;
    /// <summary>Read the run and review a stop with no additional grace before escalation.</summary>
    public AsyncCommand ReviewContainCommand { get; private set; } = null!;
    /// <summary>Send the reviewed request once after a live reread.</summary>
    public AsyncCommand SubmitInterventionCommand { get; private set; } = null!;
    /// <summary>Read the original outcome without resending.</summary>
    public AsyncCommand ReconcileInterventionCommand { get; private set; } = null!;

    private void InitializeInterventionCommands()
    {
        bool CanReview() => !Busy && Selected?.Current == true && InterventionTarget is not null && InterventionStorageProblem.Length == 0;
        ReviewPauseCommand = Command(() => ReviewInterventionAsync(PauseGoal), CanReview);
        ReviewCancelCommand = Command(() => ReviewInterventionAsync(CancelRun), CanReview);
        ReviewContainCommand = Command(() => ReviewInterventionAsync(ContainRun), CanReview);
        SubmitInterventionCommand = Command(SubmitInterventionAsync, () => CanReview() && HasIntervention);
        ReconcileInterventionCommand = Command(ReconcileInterventionAsync, () => !Busy && Selected?.Current == true && SelectedIntervention is not null);
    }

    private void NotifyInterventionCommands()
    {
        ReviewPauseCommand?.NotifyCanExecuteChanged();
        ReviewCancelCommand?.NotifyCanExecuteChanged();
        ReviewContainCommand?.NotifyCanExecuteChanged();
        SubmitInterventionCommand?.NotifyCanExecuteChanged();
        ReconcileInterventionCommand?.NotifyCanExecuteChanged();
    }

    private void SetIntervention(RemoteIntervention? value)
    {
        if (Set(ref intervention, value, nameof(Intervention))) Raise(nameof(HasIntervention));
        NotifyInterventionCommands();
    }

    private void RestoreInterventions()
    {
        InterventionStorageProblem = "";
        InterventionMessage = "";
        InterventionTarget = null;
        SelectedIntervention = null;
        Interventions = [];
        if (Selected is not { } estate) return;
        try { Interventions = connections.LoadInterventions(estate.Connection); }
        catch (Exception exception) when (StorageFailure(exception))
        {
            InterventionStorageProblem = "The original interventions could not be read. Recover app storage before sending.";
        }
        NotifyInterventionCommands();
    }

    private async Task<IEstateApi?> CheckRuntimeAsync(RemoteEstate estate, bool allowPartial = false)
    {
        ConnectionStatus status = await connections.ProbeAsync(estate.Connection, CancellationToken.None).ConfigureAwait(true);
        estate.Connection = status.Connection;
        if (Selected != estate) return null;
        ShowNode(estate, status.State, status.Message);
        if (status.State == ConnectionStates.Current || (allowPartial && status.State == ConnectionStates.Partial))
            return estate.Client ??= connections.Connect(estate.Connection);
        estate.Current = false;
        return null;
    }

    private async Task ReviewInterventionAsync(string operation)
    {
        if (Selected is not { Current: true } estate || InterventionTarget is not { } target) return;
        Busy = true;
        SetIntervention(null);
        InterventionMessage = "";
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api)
            {
                if (Selected == estate) InterventionMessage = "Reconnect to the runtime before reviewing an intervention.";
                return;
            }
            RemoteIntervention? review = await ReadInterventionAsync(api, operation, target.Run).ConfigureAwait(true);
            if (Selected != estate || InterventionTarget != target) return;
            if (review is null)
            {
                InterventionMessage = "The runtime did not report an eligible current target. Inspect the run or continue in Workbench.";
                return;
            }
            if (Interventions.FirstOrDefault(r => r.Blocks(review)) is { } prior)
            {
                SelectedIntervention = prior;
                InterventionMessage = "This operation has an outstanding request. Reconcile it before acting again.";
                return;
            }
            SetIntervention(review);
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate) InterventionMessage = "The exact target could not be read. Refresh the connection and review again.";
        }
        finally { Busy = false; }
    }

    private async Task<RemoteIntervention?> ReadInterventionAsync(IEstateApi api, string operation, string runId)
    {
        RunRow? run = (await api.RunsAsync().ConfigureAwait(true)).Runs.SingleOrDefault(r => r.Run == runId);
        if (run is null) return null;
        string state = $"Run {run.Run}; {StateLanguage.Label("operation", run.Status)}.";
        if (operation == PauseGoal)
        {
            if (run.Goal is not string goalId || await api.GoalAsync(goalId).ConfigureAwait(true) is not { ActiveStatusHash: { } activeHash } goal)
                return null;
            JsonElement basis = RemoteWorkDetail.Property(goal.Json, "goal");
            string id = RemoteWorkDetail.Text(basis, "id");
            if (id != goalId || RemoteWorkDetail.Text(goal.Json, "status") != "active") return null;
            string goalHash = RemoteWorkDetail.Text(basis, "hash");
            return new(PauseGoal, id, activeHash, state + $" Active goal revision {goalHash}; status {activeHash}.",
                "Prevents new dispatch for this goal. Runs already in flight continue. An authorized person can resume the goal.");
        }

        if (run.Status is not ("not-started" or "dispatched" or "uncertain") || run.OperationHash is not string operationHash)
            return null;
        string? priorStop = null;
        if (run.Stop is { } stop)
        {
            if (operation != ContainRun || stop is not { Reason: string reason, EscalatedAt: null, ContainedAt: null } ||
                !Interventions.Any(r => r.Request.Operation == CancelRun && r.Request.Target == runId && r.Marker == reason))
                return null;

            priorStop = reason;
            state += $" The runtime reported an earlier cancel at {stop.RequestedAt} as {stop.State}.";
        }
        return new(operation, runId, operationHash, state + $" Operation revision {operationHash}.", operation == ContainRun
            ? "Requests containment of this run's launch boundary. Its worker may escalate without an additional grace period. The stop cannot be undone once observed."
            : "Requests termination of this run. Its worker gives the harness the configured grace period before escalating. The stop cannot be undone once observed.")
        {
            ExpectedStopReason = priorStop,
        };
    }

    private async Task SubmitInterventionAsync()
    {
        if (Selected is not { Current: true } estate || intervention is not { } review || InterventionTarget is not { } run) return;
        Busy = true;
        RemoteInterventionReceipt? receipt = null;
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api ||
                await ReadInterventionAsync(api, review.Operation, run.Run).ConfigureAwait(true) != review ||
                Selected != estate || intervention != review)
            {
                if (Selected == estate) InterventionMessage = "The reviewed target changed or could not be confirmed. Review it again. Nothing was sent.";
                return;
            }
            receipt = new(review, Guid.NewGuid().ToString("N"), "uncertain", "Submission reserved on this phone. The runtime's receipt and effect have yet to be observed.");
            if (!KeepIntervention(estate, receipt)) return;
            receipt = await SendInterventionAsync(api, estate.Connection.Principal, receipt).ConfigureAwait(true);
            KeepIntervention(estate, receipt);
            if (Selected == estate) InterventionMessage = receipt.Observation;
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            string message = receipt is null ? "The live review could not be refreshed. Nothing was sent." :
                ClientFailure.FromException(exception)?.Code == "refused" ? "The runtime refused the intervention. " + ClientFailure.FromException(exception)!.Message :
                "The runtime outcome is uncertain. Reconcile this original request before taking further action.";
            if (receipt is not null)
                KeepIntervention(estate, receipt with { State = ClientFailure.FromException(exception)?.Code == "refused" ? "rejected" : "uncertain", Observation = message });
            if (Selected == estate) InterventionMessage = message;
        }
        finally
        {
            if (Selected == estate) SetIntervention(null);
            Busy = false;
        }
    }

    private static async Task<RemoteInterventionReceipt> SendInterventionAsync(IEstateApi api, string principal, RemoteInterventionReceipt receipt)
    {
        RemoteIntervention review = receipt.Request;
        if (review.Operation == PauseGoal)
        {
            WriteOutcome written = await api.GoalPauseAsync(review.Target,
                new(principal, receipt.Marker) { ExpectedActiveStatusHash = review.Revision }).ConfigureAwait(true);
            if (written.Written is { } record)
                return receipt with
                {
                    State = "completed", Record = record.Id,
                    Observation = $"The runtime recorded a paused goal in {record.Id} at {record.Hash}. This does not report any run stopped." +
                        (written.Problem is { } recovery ? "\n" + recovery.Message : ""),
                };
            return written.Problem is { } problem
                ? receipt with { State = "rejected", Observation = problem.Message + string.Concat(problem.Findings.Select(f => $"\n{f.Code}: {f.Message}")) }
                : receipt with { State = "uncertain", Observation = "The runtime returned no pause record. Reconcile the original request." };
        }

        RunStopResponse stopped = await api.RunStopAsync(review.Target,
            new(principal, receipt.Marker, review.Operation == ContainRun ? 0 : null)
            {
                ExpectedOperationHash = review.Revision,
                ExpectedStopReason = review.ExpectedStopReason,
            }).ConfigureAwait(true);
        if (!stopped.Ok && stopped.Problem?.Code is not (null or "unavailable"))
            return receipt with { State = "rejected", Observation = stopped.Message };
        if (stopped.Run == review.Target && stopped.Stop is { } facts)
            return ObserveRunStop(receipt, facts);
        return receipt with
        {
            State = "uncertain",
            Observation = stopped.Message,
        };
    }

    private static RemoteInterventionReceipt ObserveRunStop(RemoteInterventionReceipt receipt, RunStop facts)
    {
        if (facts.Reason == receipt.Marker)
            return receipt with { State = facts.State, Observation = StopObservation(facts) };
        if (!receipt.Pending) return receipt;
        return receipt with
        {
            State = facts.Reason == receipt.Request.ExpectedStopReason && facts.ContainedAt is null ? "uncertain" : "superseded",
            Observation = "A different stop request was observed. This original request has no matching receipt.\n" + StopObservation(facts),
        };
    }

    private async Task ReconcileInterventionAsync()
    {
        if (Selected is not { Current: true } estate || SelectedIntervention is not { } receipt) return;
        Busy = true;
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api) return;
            RemoteInterventionReceipt observed = receipt.Request.Operation == PauseGoal
                ? await ObservePauseAsync(api, receipt).ConfigureAwait(true)
                : await ObserveStopAsync(api, receipt).ConfigureAwait(true);
            KeepIntervention(estate, observed);
            if (Selected == estate) InterventionMessage = observed.Observation;
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate) InterventionMessage = "The runtime did not answer this reconciliation. The last observation is retained.";
        }
        finally { Busy = false; }
    }

    private static async Task<RemoteInterventionReceipt> ObservePauseAsync(IEstateApi api, RemoteInterventionReceipt receipt)
    {
        GoalResponse? goal = await api.GoalAsync(receipt.Request.Target).ConfigureAwait(true);
        if (goal is null) return receipt;
        string currentId = RemoteWorkDetail.Text(RemoteWorkDetail.Property(goal.Json, "goal"), "id");
        string state = RemoteWorkDetail.Text(goal.Json, "status");
        IReadOnlyList<RecordResponse> statuses = await api.RecordsAsync("goal-status").ConfigureAwait(true);
        RecordResponse? match = statuses.FirstOrDefault(record =>
        {
            JsonElement body = RemoteWorkDetail.Property(record.Envelope, "body");
            JsonElement subject = RemoteWorkDetail.Property(record.Envelope, "subject");
            return record.Type == "goal-status" && RemoteWorkDetail.Text(subject, "id") == receipt.Request.Target &&
                RemoteWorkDetail.Text(body, "status") == "paused" && RemoteWorkDetail.Text(body, "note") == receipt.Marker;
        });
        string current = $"Current goal revision {currentId} was observed as {state}.";
        return match is null
            ? receipt with { Observation = $"No matching pause record was found for the original request. {current}" }
            : receipt with { State = "completed", Record = match.Id, Observation = $"The runtime recorded this pause in {match.Id} at {match.Hash}. {current}" };
    }

    private static async Task<RemoteInterventionReceipt> ObserveStopAsync(IEstateApi api, RemoteInterventionReceipt receipt)
    {
        RunRow? run = (await api.RunsAsync().ConfigureAwait(true)).Runs.SingleOrDefault(r => r.Run == receipt.Request.Target);
        return run?.Stop is { } facts ? ObserveRunStop(receipt, facts) :
            receipt with { Observation = run is null ? "The runtime did not return the original run." :
                $"The run was observed as {StateLanguage.Label("operation", run.Status)}. No new stop facts were observed." };
    }

    private bool KeepIntervention(RemoteEstate estate, RemoteInterventionReceipt receipt)
    {
        try
        {
            connections.SaveIntervention(estate.Connection, receipt);
            if (Selected == estate)
            {
                Interventions = connections.LoadInterventions(estate.Connection);
                SelectedIntervention = Interventions.Single(r => r.Key == receipt.Key);
            }
            return true;
        }
        catch (Exception exception) when (StorageFailure(exception))
        {
            if (Selected == estate)
            {
                InterventionStorageProblem = "The original intervention could not be kept. Recover app storage before sending.";
                NotifyInterventionCommands();
            }
            return false;
        }
    }

    private static bool InterventionReadFailure(Exception exception) =>
        ClientFailure.FromException(exception) is not null || exception is NotSupportedException or JsonException or IOException or UnauthorizedAccessException;
}
