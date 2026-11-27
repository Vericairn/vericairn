// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private DesktopHandoffRequest? handoffForm;
    private IReadOnlyList<RemoteHandoffRow> handoffs = [];
    private string handoffMessage = "";
    private bool handoffAttempted;
    private bool handoffRetry;
    private bool handoffsCurrent;

    /// <summary>Desktop tasks in the last read for the selected runtime and person.</summary>
    public IReadOnlyList<RemoteHandoffRow> Handoffs { get => handoffs; private set => Set(ref handoffs, value); }

    /// <summary>When the desktop task list was observed, or why it is unavailable.</summary>
    public string HandoffFreshness => Selected?.Observation?.HandoffsAsOf is { } at
        ? $"{(handoffsCurrent && Selected.Current ? "Observed" : "Cached")} {at}"
        : "Desktop tasks have not been observed. Refresh Handoffs to read them.";

    /// <summary>The outcome of preparing or reading the handoff.</summary>
    public string HandoffMessage { get => handoffMessage; private set => Set(ref handoffMessage, value); }

    /// <summary>Whether an exact subject is open for handoff preparation.</summary>
    public bool HasHandoffForm => ShowHome && handoffForm is not null;

    /// <summary>Estate, node and pinned record for the desktop task.</summary>
    public string HandoffSubject => handoffForm is { Subject: { } subject }
        ? $"{Node?.Estate} · {Node?.Runtime}\n{subject.Type} {subject.Id} at {subject.Hash}" : "";

    /// <summary>The next task the desktop should show.</summary>
    public string HandoffTask
    {
        get => handoffForm?.Task ?? "";
        set
        {
            if (handoffForm is null || handoffAttempted) return;
            handoffForm = handoffForm with { Task = value ?? "" };
            Raise(nameof(HandoffTask));
            NotifyHandoffCommands();
        }
    }

    /// <summary>The reason to continue on the desktop.</summary>
    public string HandoffReason
    {
        get => handoffForm?.Reason ?? "";
        set
        {
            if (handoffForm is null || handoffAttempted) return;
            handoffForm = handoffForm with { Reason = value ?? "" };
            Raise(nameof(HandoffReason));
            NotifyHandoffCommands();
        }
    }

    /// <summary>Prepared but unsigned words to carry to Workbench.</summary>
    public string HandoffDraft
    {
        get => handoffForm?.Draft ?? "";
        set
        {
            if (handoffForm is null || handoffAttempted) return;
            handoffForm = handoffForm with { Draft = value };
            Raise(nameof(HandoffDraft));
        }
    }

    /// <summary>Editable until a submission is attempted.</summary>
    public bool HandoffEditable => !Busy && !handoffAttempted;

    /// <summary>The action available for the displayed request.</summary>
    public string PrepareHandoffLabel => handoffAttempted ? "Check original preparation" : "Prepare for Workbench";

    /// <summary>Start a handoff for the exact work detail.</summary>
    public AsyncCommand ComposeWorkHandoffCommand { get; private set; } = null!;
    /// <summary>Start a handoff preserving the decision's exact original draft and evidence references.</summary>
    public AsyncCommand ComposeDecisionHandoffCommand { get; private set; } = null!;
    /// <summary>Start a handoff for an exact policy notice.</summary>
    public AsyncCommand ComposePolicyHandoffCommand { get; private set; } = null!;
    /// <summary>Start a handoff for the exact product selected from the runtime's list.</summary>
    public AsyncCommand ComposeProductHandoffCommand { get; private set; } = null!;
    /// <summary>Save the prepared desktop task with the serving runtime.</summary>
    public AsyncCommand PrepareHandoffCommand { get; private set; } = null!;
    /// <summary>Return without preparing or resending the task.</summary>
    public AsyncCommand CloseHandoffCommand { get; private set; } = null!;
    /// <summary>Read prepared tasks and their independently observed desktop opening state.</summary>
    public AsyncCommand RefreshHandoffsCommand { get; private set; } = null!;

    private void InitializeHandoffCommands()
    {
        ComposeWorkHandoffCommand = Command(ComposeWorkHandoffAsync, () => !Busy && WorkDetail is not null);
        ComposeDecisionHandoffCommand = Command(ComposeDecisionHandoffAsync, () => !Busy && Decision is not null);
        ComposePolicyHandoffCommand = Command(ComposePolicyHandoffAsync, () => !Busy && PolicyDetail is not null);
        ComposeProductHandoffCommand = Command(ComposeProductHandoffAsync, () => !Busy && SelectedProduct?.Hash is not null);
        PrepareHandoffCommand = Command(PrepareHandoffAsync, () => !Busy && (!handoffAttempted || handoffRetry) && Selected?.Current == true &&
            !string.IsNullOrWhiteSpace(HandoffTask) && !string.IsNullOrWhiteSpace(HandoffReason));
        CloseHandoffCommand = Command(() => { SetHandoff(null); RestorePendingHandoffMessage(); return Task.CompletedTask; }, () => !Busy && HasHandoffForm);
        RefreshHandoffsCommand = Command(RefreshHandoffsAsync, () => !Busy && Selected?.Current == true);
    }

    private void NotifyHandoffCommands()
    {
        ComposeWorkHandoffCommand?.NotifyCanExecuteChanged();
        ComposeDecisionHandoffCommand?.NotifyCanExecuteChanged();
        ComposePolicyHandoffCommand?.NotifyCanExecuteChanged();
        ComposeProductHandoffCommand?.NotifyCanExecuteChanged();
        PrepareHandoffCommand?.NotifyCanExecuteChanged();
        CloseHandoffCommand?.NotifyCanExecuteChanged();
        RefreshHandoffsCommand?.NotifyCanExecuteChanged();
        Raise(nameof(HandoffEditable));
        Raise(nameof(PrepareHandoffLabel));
    }

    private void SetHandoff(DesktopHandoffRequest? request, bool retained = false)
    {
        handoffForm = request;
        handoffAttempted = retained;
        handoffRetry = retained;
        Raise(nameof(HasHandoffForm));
        Raise(nameof(HasDecisionReview));
        Raise(nameof(HasPolicyDetail));
        Raise(nameof(ShowAreas));
        Raise(nameof(HandoffSubject));
        Raise(nameof(HandoffTask));
        Raise(nameof(HandoffReason));
        Raise(nameof(HandoffDraft));
        NotifyHandoffCommands();
    }

    private void ComposeHandoff(AttentionReference subject, string defaultTask, string reason, string? draft = null, IReadOnlyList<AttentionReference>? evidence = null)
    {
        if (Selected is not { } estate || string.IsNullOrWhiteSpace(subject.Hash) || subject.Hash == "unknown") return;
        try
        {
            DesktopHandoffRequest? original = connections.LoadPendingHandoffs(estate.Connection)
                .FirstOrDefault(item => item.Subject.Type == subject.Type && item.Subject.Id == subject.Id);
            if (original is not null)
            {
                SetHandoff(original, retained: true);
                HandoffMessage = "The original preparation is still uncertain. Refresh Handoffs or check this exact request again.";
                return;
            }
            HandoffMessage = estate.Current ? "" : "Cached subject. Reconnect and refresh before preparing the task.";
            SetHandoff(new(estate.Connection.Principal, Guid.NewGuid().ToString("N"), subject, defaultTask, reason, draft, evidence));
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            SetHandoff(null);
            HandoffMessage = "The original handoff preparations could not be read from this phone.";
        }
    }

    private Task ComposeWorkHandoffAsync()
    {
        if (WorkDetail is not { } detail) return Task.CompletedTask;
        JsonElement order = RemoteWorkDetail.Property(detail.Work.Json, "workOrder");
        var workOrder = new AttentionReference("work-order", RemoteWorkDetail.Text(order, "id"), RemoteWorkDetail.Text(order, "hash"));
        JsonElement assignment = RemoteWorkDetail.Rows(detail.Work.Json, "assignments")
            .FirstOrDefault(a => RemoteWorkDetail.Text(a, "id") == detail.Assignment);
        AttentionReference subject = detail.Assignment is string id && assignment.ValueKind == JsonValueKind.Object
            ? new("assignment", id, RemoteWorkDetail.Text(assignment, "hash")) : workOrder;
        ComposeHandoff(subject, $"Continue {detail.Title} in Workbench.", "Review the complete current work and evidence on the desktop.", evidence: subject == workOrder ? [] : [workOrder]);
        return Task.CompletedTask;
    }

    private Task ComposeDecisionHandoffAsync()
    {
        if (Decision is not { } decision) return Task.CompletedTask;
        RecordResponse subject = decision.Original.Subject;
        ComposeHandoff(new(subject.Type, subject.Id, subject.Hash), "Review this decision in Workbench.",
            "The required review needs the desktop.", decision.Statement,
            decision.Original.Material.Where(r => r.Id != subject.Id).Select(r => new AttentionReference(r.Type, r.Id, r.Hash)).ToList());
        return Task.CompletedTask;
    }

    private Task ComposePolicyHandoffAsync()
    {
        if (PolicyDetail is { Notice.Policy.Finding: { } finding } detail)
        {
            PolicyRecordReference policy = finding.Policy;
            ComposeHandoff(new(policy.Type, policy.Id, policy.Hash), $"Review {finding.PolicyKey}/{finding.Rule} in Workbench.",
                "Review this exact policy condition and its evidence on the desktop.", draft: detail.DesktopRoute, evidence: finding.Evidence
                    .Select(r => new AttentionReference(r.Type, r.Id, r.Hash)).ToList());
        }
        return Task.CompletedTask;
    }

    private ProductItem? selectedProduct;

    /// <summary>A product to carry to the desktop.</summary>
    public ProductItem? SelectedProduct
    {
        get => selectedProduct;
        set { if (Set(ref selectedProduct, value)) NotifyHandoffCommands(); }
    }

    private Task ComposeProductHandoffAsync()
    {
        if (SelectedProduct is { } product)
            ComposeHandoff(new("product", product.Id, product.Hash), $"Inspect {product.Name} in Workbench.",
                "Continue reviewing the product and its evidence on the desktop.");
        return Task.CompletedTask;
    }

    private void RestorePendingHandoffMessage()
    {
        try
        {
            HandoffMessage = Selected is { } estate && connections.LoadPendingHandoffs(estate.Connection).Count > 0
                ? "A preparation is still uncertain. Refresh Handoffs to check for the original task."
                : "";
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            HandoffMessage = "The original handoff preparations could not be read from this phone.";
        }
    }

    private async Task PrepareHandoffAsync()
    {
        if (Selected is not { Current: true } estate || handoffForm is not { } request || (handoffAttempted && !handoffRetry)) return;
        bool wasRetained = handoffAttempted;
        Busy = true;
        try
        {
            if (await CheckRuntimeAsync(estate).ConfigureAwait(true) is not { } api)
            {
                if (Selected == estate) HandoffMessage = "Reconnect before preparing this desktop task. Nothing was sent.";
                return;
            }
            if (await api.RecordAsync(request.Subject.Id).ConfigureAwait(true) is not { } exact ||
                exact.Type != request.Subject.Type || exact.Hash != request.Subject.Hash ||
                Selected != estate || handoffForm != request)
            {
                if (Selected == estate) HandoffMessage = "The exact subject could not be confirmed. Nothing was sent.";
                return;
            }
            try
            {
                connections.KeepPendingHandoff(estate.Connection, request);
            }
            catch (Exception exception) when (InterventionReadFailure(exception))
            {
                HandoffMessage = "This phone could not retain the original handoff preparation. Nothing was sent. " + exception.Message;
                return;
            }
            handoffAttempted = true;
            handoffRetry = false;
            DesktopHandoffResponse response = await api.PrepareDesktopHandoffAsync(request).ConfigureAwait(true);
            if (Selected != estate || handoffForm != request) return;
            if (response.Handoff is not { } handoff || !MatchesPreparation(handoff, request))
            {
                HandoffMessage = response.Problem?.Message ?? "Preparation was not confirmed. Refresh Handoffs to see whether the task was kept.";
                if (!wasRetained && response.Problem is { Code: not "unavailable" } &&
                    ForgetPendingHandoff(estate, request.RequestId)) handoffAttempted = false;
                return;
            }

            if (!ForgetPendingHandoff(estate, request.RequestId)) return;
            ReplaceHandoff(estate, handoff);
            ShowPreparedHandoff(handoff);
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected != estate) return;
            if (ClientFailure.FromException(exception) is { Code: "refused" } failure)
            {
                HandoffMessage = "The runtime refused the handoff. " + failure.Message;
                if (!wasRetained && ForgetPendingHandoff(estate, request.RequestId)) handoffAttempted = false;
            }
            else
                HandoffMessage = handoffAttempted ? "Preparation is uncertain. Refresh Handoffs and look for the original task." :
                    "The exact subject could not be read. Nothing was sent.";
        }
        finally { Busy = false; }
    }

    private async Task RefreshHandoffsAsync()
    {
        if (Selected is not { Current: true, Observation: { } observation } estate) return;
        Busy = true;
        handoffsCurrent = false;
        try
        {
            if (await CheckRuntimeAsync(estate, allowPartial: true).ConfigureAwait(true) is not { } api)
            {
                if (Selected == estate) HandoffMessage = "Reconnect to see the current desktop task list.";
                return;
            }
            DesktopHandoffsResponse response = await api.DesktopHandoffsAsync(estate.Connection.Principal).ConfigureAwait(true);
            if (response.Problem is { } problem)
            {
                if (Selected == estate) HandoffMessage = problem.Message;
                return;
            }
            estate.Observation = observation with { DesktopHandoffs = response.Handoffs, HandoffsAsOf = Now };
            Keep(estate);
            if (Selected != estate) return;
            HandoffMessage = response.Handoffs.Count == 0 ? "No desktop tasks were found." : "";
            handoffsCurrent = true;
            DesktopHandoff? openedForm = null;
            bool unobserved = false;
            foreach (DesktopHandoffRequest original in connections.LoadPendingHandoffs(estate.Connection))
            {
                if (response.Handoffs.FirstOrDefault(handoff => MatchesPreparation(handoff, original)) is not { } kept)
                {
                    unobserved = true;
                    continue;
                }
                if (!ForgetPendingHandoff(estate, original.RequestId)) return;
                if (handoffForm?.RequestId == original.RequestId) openedForm = kept;
            }
            if (openedForm is not null) ShowPreparedHandoff(openedForm);
            if (unobserved) HandoffMessage = "An original preparation was not observed. Its outcome is still uncertain.";
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            if (Selected == estate) HandoffMessage = "The desktop task list could not be refreshed. Any retained tasks are cached.";
        }
        finally
        {
            if (Selected == estate) ShowHandoffs();
            Busy = false;
        }
    }

    private bool ForgetPendingHandoff(RemoteEstate estate, string id)
    {
        try
        {
            connections.ForgetPendingHandoff(estate.Connection, id);
            return true;
        }
        catch (Exception exception) when (InterventionReadFailure(exception))
        {
            HandoffMessage = "The outcome was observed. This phone could not clear its retained preparation. Refresh Handoffs to check it again.";
            return false;
        }
    }

    private static bool MatchesPreparation(DesktopHandoff handoff, DesktopHandoffRequest request) =>
        handoff.Id == request.RequestId && handoff.By == request.By && handoff.Subject == request.Subject &&
        handoff.Task == request.Task && handoff.Reason == request.Reason && handoff.Draft == request.Draft &&
        handoff.Evidence.SequenceEqual(request.Evidence ?? []);

    private void ShowPreparedHandoff(DesktopHandoff handoff)
    {
        SetHandoff(null);
        SetDecision(null);
        SetPolicyDetail(null);
        SelectedArea = 4;
        HandoffMessage = $"Prepared at {handoff.PreparedAt}. Open this task in Workbench.";
    }

    private void ReplaceHandoff(RemoteEstate estate, DesktopHandoff handoff)
    {
        if (estate.Observation is not { } observation) return;
        estate.Observation = observation with
        {
            DesktopHandoffs = [handoff, .. (observation.DesktopHandoffs ?? []).Where(h => h.Id != handoff.Id)],
        };
        Keep(estate);
        ShowHandoffs();
    }

    private void ShowHandoffs()
    {
        Handoffs = Selected?.Observation?.DesktopHandoffs?.Select(h => new RemoteHandoffRow(h.Task,
            $"{h.Subject.Type} {h.Subject.Id} at {h.Subject.Hash} ({h.SubjectState})\nEstate {h.Estate} · node {h.Node}\n{h.Reason}",
            $"Prepared {h.PreparedAt} · " + (h.OpenedAt is { } opened ? $"Opened in Workbench {opened}." : "No Workbench opening observed.") +
            (h.SubjectStatus is { } status ? $"\nLatest observed subject state: {status}." : "\nDownstream state has not been observed."),
            h.Draft ?? "")).ToList() ?? [];
        Raise(nameof(HandoffFreshness));
        NotifyHandoffCommands();
    }
}

/// <summary>A desktop task's exact context, and separately observed preparation and opening.</summary>
public sealed record RemoteHandoffRow(string Task, string Context, string State, string Draft);
