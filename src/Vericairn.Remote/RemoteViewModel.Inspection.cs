// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private AttentionItem? selectedAttention;
    private WorkOrderRow? selectedWork;
    private AssignmentItem? selectedAssignment;
    private RemoteWorkDetail? workDetail;
    private string detailMessage = string.Empty;
    private string communication = string.Empty;
    private string observationAge = "age unknown";
    private bool detailCurrent;
    private int selectedArea;

    /// <summary>The selected home area; opening a work subject moves to Work.</summary>
    public int SelectedArea { get => selectedArea; set => Set(ref selectedArea, value); }

    /// <summary>The selected notice; selection writes nothing.</summary>
    public AttentionItem? SelectedAttention { get => selectedAttention; set { if (Set(ref selectedAttention, value)) NotifyInspectionCommands(); } }
    /// <summary>The selected work order.</summary>
    public WorkOrderRow? SelectedWork { get => selectedWork; set { if (Set(ref selectedWork, value)) NotifyInspectionCommands(); } }
    /// <summary>The selected assignment.</summary>
    public AssignmentItem? SelectedAssignment { get => selectedAssignment; set { if (Set(ref selectedAssignment, value)) NotifyInspectionCommands(); } }
    /// <summary>The current or explicitly cached detail.</summary>
    public RemoteWorkDetail? WorkDetail { get => workDetail; private set { if (Set(ref workDetail, value)) { Raise(nameof(HasWorkDetail)); Raise(nameof(ShowWorkList)); } } }
    /// <summary>Whether a detail has been observed.</summary>
    public bool HasWorkDetail => WorkDetail is not null;
    /// <summary>The work list is replaced by the opened detail until the reader goes back.</summary>
    public bool ShowWorkList => !HasWorkDetail;
    /// <summary>The result of inspection or acknowledgment.</summary>
    public string DetailMessage { get => detailMessage; private set => Set(ref detailMessage, value); }
    /// <summary>The person's effective communication settings, inheritance, conflicts and preview, read-only.</summary>
    public string Communication { get => communication; private set => Set(ref communication, value); }
    /// <summary>The age of the home observation; refreshing a connection never renews old content.</summary>
    public string ObservationAge { get => observationAge; private set => Set(ref observationAge, value); }
    /// <summary>The work detail's own observation time, separate from the home refresh.</summary>
    public string DetailFreshness => WorkDetail is null ? "not observed" : $"{(detailCurrent && Selected?.Current == true ? "observed" : "cached")} {WorkDetail.AsOf}; {RemoteFreshness.Age(WorkDetail.AsOf, clock)}";
    /// <summary>Each run's state and independently dated worker observation.</summary>
    public string RunObservations => WorkDetail is null ? "not observed" : WorkDetail.Runs.Count == 0 ? "No runs observed." : string.Join("\n\n", WorkDetail.Runs.Select(r =>
        $"Run {r.Run}: {StateLanguage.Label("operation", r.Status)}\nWorker observation: {(detailCurrent && Selected?.Current == true ? "observed" : "cached")}; heartbeat {r.Heartbeat ?? "not observed"}; " +
        (r.Heartbeat is null ? "age unknown" : RemoteFreshness.Age(r.Heartbeat, clock)) + $"; worker {(r.Observed ? "reported current at this observation" : "not observed current")}.\n" +
        (r.Liveness is null ? "No event-stream observation." : $"Last worker event {r.Liveness.LastEventAt}; {RemoteFreshness.Age(r.Liveness.LastEventAt, clock)}; stall limit {r.Liveness.StallSeconds} seconds.") +
        (r.Stop is null ? "" : "\n" + StopObservation(r.Stop)) +
        (r.Signals is null ? "" : "\n" + string.Join("\n", r.Signals.Select(s => s.Summary)))));
    /// <summary>Every contributor claim beside the worker's observation, with the source's freshness and the cache's age.</summary>
    public string ProgressClaims => WorkDetail is null ? "not observed" : WorkDetail.Progress.Count == 0 ? "No agent progress claim observed." : string.Join("\n\n", WorkDetail.Progress.Select(p =>
        $"{(detailCurrent && Selected?.Current == true ? "Observed" : "Cached")} reporting status as of {p.AsOf}; {RemoteFreshness.Age(p.AsOf, clock)}\n" +
        ProgressClaimPresentation.Claim(p) + (p.Claim is null ? "" : "; " + RemoteFreshness.Age(p.Claim.At, clock) + "\nPhase: " + p.Claim.Phase + "; " + p.Claim.Note) +
        "\nWorker observation at that time: " + ProgressClaimPresentation.Liveness(p) + (p.Liveness is null ? "" : "; " + RemoteFreshness.Age(p.Liveness.LastEventAt, clock))));

    /// <summary>Opens the selected notice's subject, or explains where to act.</summary>
    public AsyncCommand OpenAttentionCommand { get; private set; } = null!;
    /// <summary>Acknowledges only the current selected notice, when online.</summary>
    public AsyncCommand AcknowledgeCommand { get; private set; } = null!;
    /// <summary>Opens the selected work order.</summary>
    public AsyncCommand OpenWorkCommand { get; private set; } = null!;
    /// <summary>Opens the selected assignment.</summary>
    public AsyncCommand OpenAssignmentCommand { get; private set; } = null!;
    /// <summary>Returns to the work list without sending a request.</summary>
    public AsyncCommand BackToWorkCommand { get; private set; } = null!;

    private void InitializeInspectionCommands()
    {
        OpenAttentionCommand = new(() => OpenDetailAsync(SelectedAttention!.SubjectId, SelectedAttention.SubjectType), () => !Busy && SelectedAttention is not null);
        AcknowledgeCommand = new(AcknowledgeAsync, () => !Busy && Selected?.Current == true && SelectedAttention?.Notice is { AcknowledgedAt: null });
        OpenWorkCommand = new(() => OpenDetailAsync(SelectedWork!.Id, "work-order"), () => !Busy && SelectedWork is not null);
        OpenAssignmentCommand = new(() => OpenDetailAsync(SelectedAssignment!.Id, "assignment"), () => !Busy && SelectedAssignment is not null);
        BackToWorkCommand = new(() =>
        {
            if (Selected is { } estate) estate.DetailKey = null;
            WorkDetail = null;
            DetailMessage = string.Empty;
            RaiseDetailFreshness();
            return Task.CompletedTask;
        }, () => !Busy && HasWorkDetail);
    }

    private void NotifyInspectionCommands()
    {
        OpenAttentionCommand?.NotifyCanExecuteChanged();
        AcknowledgeCommand?.NotifyCanExecuteChanged();
        OpenWorkCommand?.NotifyCanExecuteChanged();
        OpenAssignmentCommand?.NotifyCanExecuteChanged();
        BackToWorkCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Advances displayed ages locally. This sends no request, including while offline.</summary>
    public void RefreshFreshness()
    {
        if (Selected?.Observation is not { } observation) return;
        ObservationAge = RemoteFreshness.Age(observation.AsOf, clock);
        // Keep the selected list row and scroll position stable while the shared observation age advances.
        Communication = $"{(Selected.Current ? "Observed" : "Cached")} {observation.AsOf}; {ObservationAge}\n" +
            CommunicationPresentation.Describe(observation.Communication) + "\nPreview\n" + CommunicationPresentation.Preview(observation.Communication);
        RaiseDetailFreshness();
    }

    private void RaiseDetailFreshness()
    {
        Raise(nameof(DetailFreshness)); Raise(nameof(RunObservations)); Raise(nameof(ProgressClaims));
    }

    private static string StopObservation(RunStop stop) => $"Stop {stop.State}: requested {stop.RequestedAt} by {stop.RequestedBy}; {stop.Reason ?? "no reason recorded"}"
        + (stop.TransmittedAt is null ? "" : $"\nTransmitted {stop.TransmittedAt}: {stop.Transmission}")
        + (stop.AcknowledgedAt is null ? "" : $"\nAcknowledged by the worker {stop.AcknowledgedAt}: {stop.Acknowledgment}")
        + (stop.EscalatedAt is null ? "" : $"\nEscalated {stop.EscalatedAt}: {stop.Escalation}")
        + (stop.ContainedAt is null ? "\nContainment not observed." : $"\nContainment observed {stop.ContainedAt}");

    private async Task AcknowledgeAsync()
    {
        if (Selected is not { Current: true, Client: { } api, Observation: { } observation } estate || SelectedAttention?.Notice is not { } notice) return;
        Busy = true;
        try
        {
            AttentionAcknowledgeResponse response = await api.AttentionAcknowledgeAsync(new(estate.Connection.Principal, notice.Kind, notice.SubjectType, notice.SubjectId, notice.Revision)).ConfigureAwait(true);
            if (response is { Ok: true, Item: { } acknowledged })
            {
                estate.Observation = observation with { Attention = observation.Attention with { Items = observation.Attention.Items.Select(i => i.Key == acknowledged.Key ? acknowledged : i).ToList() } };
                Keep(estate);
                if (Selected == estate)
                {
                    Show(estate);
                    SelectedAttention = Attention.FirstOrDefault(i => i.Notice?.Key == acknowledged.Key);
                    DetailMessage = "Acknowledged. This notice remains visible; its work has not changed.";
                }
            }
            else if (Selected == estate) DetailMessage = response.Problem?.Message ?? "Acknowledgment was not confirmed. Refresh attention.";
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null)
        {
            estate.Current = false;
            if (Selected == estate) { Show(estate); DetailMessage = "Acknowledgment was not confirmed. Nothing is queued; refresh to observe its state."; }
        }
        finally { Busy = false; }
    }

    private async Task OpenDetailAsync(string? id, string? type)
    {
        if (Selected is not { Observation: { } observation } estate || id is null) return;
        detailCurrent = false;
        estate.DetailKey = id;
        WorkDetail = observation.Details.GetValueOrDefault(id);
        RaiseDetailFreshness();
        if (type is not ("work-order" or "assignment" or "attempt" or "result" or "operation"))
        {
            DetailMessage = "Open this subject in Workbench or the command line. " + (SelectedAttention?.NextAction ?? "Inspect the exact record there.");
            return;
        }
        if (!estate.Current || estate.Client is not { } api)
        {
            Show(estate);
            if (WorkDetail is not null) SelectedArea = 1;
            DetailMessage = WorkDetail is null ? "This detail was not cached. Reconnect and refresh to inspect it; nothing was sent." : "Showing the last cached detail. Nothing was sent.";
            return;
        }
        Busy = true;
        try
        {
            string observedAt = Now;
            string? assignment = null;
            RecordResponse? pinnedAssignment = null;
            string order = id;
            string? kind = type;
            for (int depth = 0; kind != "work-order" && depth < 5; depth++)
            {
                if (kind is not ("assignment" or "attempt" or "result" or "operation")) break;
                RecordResponse? record = await api.RecordAsync(order).ConfigureAwait(true);
                if (record is null) break;
                if (kind == "assignment") { assignment = record.Id; pinnedAssignment = record; }
                if (kind == "operation")
                {
                    JsonElement target = RemoteWorkDetail.Property(RemoteWorkDetail.Property(record.Envelope, "body"), "target");
                    order = RemoteWorkDetail.Text(target, "assignment"); kind = "assignment";
                }
                else
                {
                    JsonElement subject = RemoteWorkDetail.Property(record.Envelope, "subject");
                    order = RemoteWorkDetail.Text(subject, "id"); kind = RemoteWorkDetail.Text(subject, "type");
                }
            }
            if (kind != "work-order")
            {
                if (Selected == estate) { WorkDetail = null; DetailMessage = "Open this subject in Workbench or the command line. " + (SelectedAttention?.NextAction ?? "Inspect the exact record there."); }
                return;
            }
            ObserveResponse? observed = await api.ObserveAsync(order).ConfigureAwait(true);
            if (observed is null) { if (Selected == estate) DetailMessage = "The runtime did not return this work detail. Refresh attention to inspect what changed."; return; }
            if (pinnedAssignment is not null) observed = await IncludePinnedAssignmentAsync(api, observed, pinnedAssignment).ConfigureAwait(true);
            List<JsonElement> assigned = RemoteWorkDetail.Rows(observed.Json, "assignments").Where(a => assignment is null || RemoteWorkDetail.Text(a, "id") == assignment).ToList();
            var assignmentIds = assigned.Select(a => RemoteWorkDetail.Text(a, "id")).ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<RunRow> runs = (await api.RunsAsync().ConfigureAwait(true)).Runs.Where(r => r.Assignment is not null && assignmentIds.Contains(r.Assignment)).ToList();
            var claims = new List<RequestedProgress>();
            foreach (JsonElement attempt in assigned.SelectMany(a => RemoteWorkDetail.Rows(a, "attempts")))
                if (await api.WorkProgressGetAsync(RemoteWorkDetail.Text(attempt, "id")).ConfigureAwait(true) is { } claim) claims.Add(claim);
            foreach (RunRow run in runs)
                if (!claims.Any(p => p.Run == run.Run) && await api.WorkProgressGetAsync(run.Run).ConfigureAwait(true) is { } claim) claims.Add(claim);
            var detail = new RemoteWorkDetail(order, assignment, observedAt, observed, runs, claims);
            var details = new Dictionary<string, RemoteWorkDetail>(observation.Details, StringComparer.Ordinal) { [id] = detail };
            estate.Observation = observation with { Details = details };
            Keep(estate);
            if (Selected == estate) { Show(estate); detailCurrent = true; SelectedArea = 1; RaiseDetailFreshness(); DetailMessage = "Work detail observed. Results and progress are claims to inspect."; }
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null || exception is NotSupportedException)
        {
            estate.Current = false;
            if (Selected == estate) { Show(estate); DetailMessage = "The runtime did not answer this detail read. Retained observations are cached; refresh to reconnect."; }
        }
        finally { Busy = false; }
    }
}
