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
                InterventionMessage = "The runtime did not report an eligible current target." + InterventionHandoff;
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
            if (Selected == estate)
                InterventionMessage = ClientFailure.FromException(exception) is { Code: "refused" } refusal
                    ? refusal.Message + InterventionHandoff
                    : "The exact target could not be read. Refresh the connection and review again.";
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
                "Prevents new dispatch for this goal. Runs already in flight continue. An authorized person can resume the goal.")
            {
                ReviewedSubject = goalHash.Length == 0 ? null : new("goal", id, goalHash),
            };
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
            ReviewedSubject = run.OperationRevision is { } revision ? new("operation", revision, operationHash) : null,
        };
    }

    private async Task SubmitInterventionAsync()
    {
        if (Selected is not { Current: true } estate || intervention is not { } review || InterventionTarget is not { } run) return;
        Busy = true;
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api ||
                await ReadInterventionAsync(api, review.Operation, run.Run).ConfigureAwait(true) != review ||
                Selected != estate || intervention != review)
            {
                if (Selected == estate) InterventionMessage = "The reviewed target changed or could not be confirmed. Review it again. Nothing was sent.";
                return;
            }

            // A missing record can be followed by a late arrival. Competing plans for the same review
            // keep one request identity and the same pin, which the runtime checks at the write boundary.
            RemoteInterventionReceipt? retry = Interventions.LastOrDefault(r => r.State == "not-delivered" && r.Request.SameReviewedOperation(review));
            RemoteInterventionReceipt receipt = retry ?? new(review, Guid.NewGuid().ToString("N"), "not-delivered", "");
            if (await SendSignedInterventionAsync(estate, api, review, receipt, retry is not null).ConfigureAwait(true) is not { } result) return;
            if (KeepIntervention(estate, result) && Selected == estate) InterventionMessage = result.Observation;
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate) InterventionMessage = "The live review could not be refreshed. Nothing was sent.";
        }
        finally
        {
            if (Selected == estate) SetIntervention(null);
            Busy = false;
        }
    }

    private async Task ReconcileInterventionAsync()
    {
        if (Selected is not { Current: true } estate || SelectedIntervention is not { } receipt) return;
        Busy = true;
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api) return;
            RemoteInterventionReceipt observed = await ObserveInterventionAsync(api, estate.Connection.Principal, receipt).ConfigureAwait(true);
            if (KeepIntervention(estate, observed) && Selected == estate) InterventionMessage = observed.Observation;
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate) InterventionMessage = "The runtime did not answer this reconciliation. The last observation is retained.";
        }
        finally { Busy = false; }
    }

    private static async Task<RemoteInterventionReceipt> ObserveInterventionAsync(IEstateApi api, string principal, RemoteInterventionReceipt receipt)
    {
        RecordResponse? record = await FindInterventionRecordAsync(api, principal, receipt).ConfigureAwait(true);
        if (record is null)
            return receipt.State is "pending" or "uncertain"
                ? receipt with { State = "not-delivered", Observation = "The runtime holds no signed record for this request. Review the current target before trying again." }
                : receipt;

        receipt = receipt with { Record = record.Id, RecordHash = record.Hash };
        if (receipt.Request.Operation == PauseGoal)
        {
            GoalResponse? goal = await api.GoalAsync(receipt.Request.Target).ConfigureAwait(true);
            string current = goal is null ? "" : $" The goal is currently {RemoteWorkDetail.Text(goal.Json, "status")}.";
            return receipt with
            {
                State = "completed",
                Observation = $"The runtime recorded a paused goal in {record.Id} at {record.Hash}.{current} Check the run separately for any run stopped.",
            };
        }

        RunRow? run = (await api.RunsAsync().ConfigureAwait(true)).Runs.SingleOrDefault(r => r.Run == receipt.Request.Target);
        if (run?.Stop is { } facts)
        {
            if (facts.Reason == receipt.Marker)
                return receipt with { State = facts.State, Observation = StopObservation(facts) };
            if (receipt.Pending || receipt.State == "not-delivered")
            {
                bool predecessor = facts.Reason == receipt.Request.ExpectedStopReason && facts.ContainedAt is null;
                return receipt with
                {
                    State = predecessor ? "recorded" : "superseded",
                    Observation = $"The runtime holds the signed record {record.Id}. A different worker stop was observed.\n" + StopObservation(facts),
                };
            }
        }

        return receipt.State is "pending" or "uncertain" or "not-delivered"
            ? receipt with { State = "recorded", Observation = $"The runtime holds the signed record {record.Id}. No worker stop was observed." }
            : receipt;
    }

    private static async Task<RecordResponse?> FindInterventionRecordAsync(IEstateApi api, string principal, RemoteInterventionReceipt receipt)
    {
        bool pause = receipt.Request.Operation == PauseGoal;
        string type = pause ? "goal-status" : "run-stop-request";
        if (receipt.Record is { } id && await api.RecordAsync(id).ConfigureAwait(true) is { } exact)
        {
            if (exact.Id != id || (receipt.RecordHash is { } hash && exact.Hash != hash) || !Matches(exact))
                throw new InvalidDataException("The runtime returned a different record for the prepared intervention.");
            return exact;
        }

        // Old receipts can lack a prepared id. A retry can also race an earlier plan for the same request.
        return (await api.RecordsAsync(type).ConfigureAwait(true)).FirstOrDefault(Matches);

        bool Matches(RecordResponse record)
        {
            if (record.Type != type || RemoteWorkDetail.Text(record.Envelope, "by") != principal) return false;
            JsonElement body = RemoteWorkDetail.Property(record.Envelope, "body");
            if (!pause)
                return RemoteWorkDetail.Text(body, "run") == receipt.Request.Target &&
                    RemoteWorkDetail.Text(body, "operation") == (receipt.Request.Operation == ContainRun ? "contain" : "cancel") &&
                    RemoteWorkDetail.Text(body, "requestId") == receipt.Id;
            JsonElement subject = RemoteWorkDetail.Property(record.Envelope, "subject");
            return RemoteWorkDetail.Text(subject, "id") == receipt.Request.Target &&
                RemoteWorkDetail.Text(body, "status") == "paused" && RemoteWorkDetail.Text(body, "note") == receipt.Marker;
        }
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
            if (Selected == estate) ShowInterventionStorageProblem();
            return false;
        }
    }

    private void ShowInterventionStorageProblem()
    {
        InterventionStorageProblem = "The original intervention could not be kept. Recover app storage before sending.";
        NotifyInterventionCommands();
    }

    private static bool InterventionReadFailure(Exception exception) =>
        ClientFailure.FromException(exception) is not null || exception is NotSupportedException or JsonException or IOException or InvalidDataException or UnauthorizedAccessException;
}
