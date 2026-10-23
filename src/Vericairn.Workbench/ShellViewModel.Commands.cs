// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Presentation;

namespace Vericairn.Workbench;

public sealed partial class ShellViewModel
{
    private CancellationTokenSource? currentRequest;
    private bool isBusy;
    private string requestState = "idle";
    private ClientFailure? lastFailure;
    private readonly List<string> uncertainOutcomes = [];

    /// <summary>Whether an application command is waiting for the runtime. Editing and cancellation remain available.</summary>
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) { RefreshCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } } }

    /// <summary>idle, busy, cancelled, timed-out, unavailable or refused; separate from the runtime's last observed state.</summary>
    public string RequestState { get => requestState; private set => Set(ref requestState, value); }

    /// <summary>The last interrupted observation, retaining its cause and whether an effect is uncertain.</summary>
    public ClientFailure? LastFailure { get => lastFailure; private set => Set(ref lastFailure, value); }

    /// <summary>Persistent uncertainty from this session. A successful read never silently clears an unknown effect.</summary>
    public string UncertaintyNotice => string.Join("\n", uncertainOutcomes);

    /// <summary>Whether an effect still needs reconciliation.</summary>
    public bool HasUncertainOutcomes => uncertainOutcomes.Count != 0;

    /// <summary>The active application command, including commands started by selection or keyboard events.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>Refreshes through the asynchronous API and is disabled while another command is running.</summary>
    public AsyncCommand RefreshCommand { get; }

    /// <summary>Cancels the current observation. This never claims to undo a runtime effect.</summary>
    public AsyncCommand CancelCommand { get; }

    /// <summary>Requests cancellation of the active application command.</summary>
    public void CancelCurrentRequest() => currentRequest?.Cancel();

    private Task<bool> RunCommandAsync(string label, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
        RunCommandAsync<bool>(label, async token => { await action(token).ConfigureAwait(true); return true; }, cancellationToken);

    private Task<T?> RunCommandAsync<T>(string label, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return Task.FromResult<T?>(default);
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        currentRequest = source;
        IsBusy = true;
        RequestState = "busy";
        LastFailure = null;
        Status = label + "…";
        Task<T?> pending = ExecuteAsync();
        PendingOperation = pending;
        return pending;

        async Task<T?> ExecuteAsync()
        {
            try
            {
                source.Token.ThrowIfCancellationRequested();
                return await action(source.Token).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
            {
                ClientFailure failure = ClientFailure.FromException(exception) ?? new("unavailable", "The runtime did not answer: " + exception.Message, false, 1);
                LastFailure = failure;
                RequestState = failure.Code;
                Status = failure.Message;
                Freshness = "cached";
                foreach (HandoffDocument page in Documents.OfType<HandoffDocument>()) page.Unavailable();
                InvalidateCompanions();
                if (failure.OutcomeUnknown)
                {
                    uncertainOutcomes.Add(EstateName + ": " + label + ". " + failure.Message);
                    Raise(nameof(UncertaintyNotice));
                    Raise(nameof(HasUncertainOutcomes));
                }
                Log(label + ": " + failure.Message);
                return default;
            }
            finally
            {
                currentRequest = null;
                source.Dispose();
                if (RequestState == "busy") RequestState = "idle";
                IsBusy = false;
                Raise(nameof(HasEstate));
            }
        }
    }

    private Task<bool> SelectRegionAsync(Region region) => RunCommandAsync("Open " + region.Name, token => ShowRegionAsync(region, token));

    private Task<bool> RefreshActingAsync() => RunCommandAsync("Read acting principal", async token =>
    {
        await DescribeActingAsync(token).ConfigureAwait(true);
        await RefreshCommunicationAsync(token).ConfigureAwait(true);
    });

    private Task<bool> RefreshCommunicationCommandAsync() => RunCommandAsync("Read communication settings", token => RefreshCommunicationAsync(token));

    /// <summary>Open Estate through a cancellable application command.</summary>
    public Task OpenEstateAsync(string root, CancellationToken cancellationToken = default) => RunCommandAsync("Open Estate", token => OpenEstateCoreAsync(root, token), cancellationToken);

    /// <summary>Refresh through a cancellable application command.</summary>
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Refresh with caller cancellation.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) => RunCommandAsync("Refresh", token => RefreshCoreAsync(token), cancellationToken);

    /// <summary>Start Runtime through a cancellable application command.</summary>
    public Task StartRuntimeAsync() => StartRuntimeAsync(CancellationToken.None);

    /// <summary>Start Runtime with caller cancellation.</summary>
    public Task StartRuntimeAsync(CancellationToken cancellationToken) => RunCommandAsync("Start Runtime", token => StartRuntimeCommandCoreAsync(token), cancellationToken);

    /// <summary>Stop Runtime through a cancellable application command.</summary>
    public Task StopRuntimeAsync() => StopRuntimeAsync(CancellationToken.None);

    /// <summary>Stop Runtime with caller cancellation.</summary>
    public Task StopRuntimeAsync(CancellationToken cancellationToken) => RunCommandAsync("Stop Runtime", token => StopRuntimeCoreAsync(token), cancellationToken);

    /// <summary>Show Install Plan through a cancellable application command.</summary>
    public Task ShowInstallPlanAsync() => ShowInstallPlanAsync(CancellationToken.None);

    /// <summary>Show Install Plan with caller cancellation.</summary>
    public Task ShowInstallPlanAsync(CancellationToken cancellationToken) => RunCommandAsync("Show Install Plan", token => ShowInstallPlanCoreAsync(token), cancellationToken);

    /// <summary>Install Runtime through a cancellable application command.</summary>
    public Task InstallRuntimeAsync() => InstallRuntimeAsync(CancellationToken.None);

    /// <summary>Install Runtime with caller cancellation.</summary>
    public Task InstallRuntimeAsync(CancellationToken cancellationToken) => RunCommandAsync("Install Runtime", token => InstallRuntimeCoreAsync(token), cancellationToken);

    /// <summary>Uninstall Runtime through a cancellable application command.</summary>
    public Task UninstallRuntimeAsync() => UninstallRuntimeAsync(CancellationToken.None);

    /// <summary>Uninstall Runtime with caller cancellation.</summary>
    public Task UninstallRuntimeAsync(CancellationToken cancellationToken) => RunCommandAsync("Uninstall Runtime", token => UninstallRuntimeCoreAsync(token), cancellationToken);

    /// <summary>Open Runtime through a cancellable application command.</summary>
    public Task<RuntimeDocument?> OpenRuntimeAsync() => OpenRuntimeAsync(CancellationToken.None);

    /// <summary>Open Runtime with caller cancellation.</summary>
    public Task<RuntimeDocument?> OpenRuntimeAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Runtime", token => OpenRuntimeCoreAsync(token), cancellationToken);

    /// <summary>Open Progress through a cancellable application command.</summary>
    public Task<ProgressDocument?> OpenProgressAsync() => OpenProgressAsync(CancellationToken.None);

    /// <summary>Open Progress with caller cancellation.</summary>
    public Task<ProgressDocument?> OpenProgressAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Progress", token => OpenProgressCoreAsync(token), cancellationToken);

    /// <summary>Refresh Progress through a cancellable application command.</summary>
    public Task RefreshProgressAsync() => RefreshProgressAsync(CancellationToken.None);

    /// <summary>Refresh Progress with caller cancellation.</summary>
    public Task RefreshProgressAsync(CancellationToken cancellationToken) => RunCommandAsync("Refresh Progress", token => RefreshProgressCoreAsync(token), cancellationToken);

    /// <summary>Open Handoff through a cancellable application command.</summary>
    public Task<HandoffDocument?> OpenHandoffAsync() => OpenHandoffAsync(CancellationToken.None);

    /// <summary>Open Handoff with caller cancellation.</summary>
    public Task<HandoffDocument?> OpenHandoffAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Handoff", token => OpenHandoffCoreAsync(token), cancellationToken);

    /// <summary>Observe Estate through a cancellable application command.</summary>
    public Task ObserveEstateAsync() => ObserveEstateAsync(CancellationToken.None);

    /// <summary>Observe Estate with caller cancellation.</summary>
    public Task ObserveEstateAsync(CancellationToken cancellationToken) => RunCommandAsync("Observe Estate", token => ObserveEstateCoreAsync(token), cancellationToken);

    /// <summary>Open Goals through a cancellable application command.</summary>
    public Task<GoalsWorkspace?> OpenGoalsAsync(string? goal = null, CancellationToken cancellationToken = default) => RunCommandAsync("Open Goals", token => OpenGoalsCoreAsync(goal, token), cancellationToken);

    /// <summary>Open Living Documents through a cancellable application command.</summary>
    public Task<LivingDocumentsWorkspace?> OpenLivingDocumentsAsync() => OpenLivingDocumentsAsync(CancellationToken.None);

    /// <summary>Open Living Documents with caller cancellation.</summary>
    public Task<LivingDocumentsWorkspace?> OpenLivingDocumentsAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Living Documents", token => OpenLivingDocumentsCoreAsync(token), cancellationToken);

    /// <summary>Open Release through a cancellable application command.</summary>
    public Task<ReleaseWorkspace?> OpenReleaseAsync() => OpenReleaseAsync(CancellationToken.None);

    /// <summary>Open Release with caller cancellation.</summary>
    public Task<ReleaseWorkspace?> OpenReleaseAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Release", token => OpenReleaseCoreAsync(token), cancellationToken);

    /// <summary>Open Attention through a cancellable application command.</summary>
    public Task OpenAttentionAsync(AttentionItem item, CancellationToken cancellationToken = default) => RunCommandAsync("Open Attention", token => OpenAttentionCoreAsync(item, token), cancellationToken);

    /// <summary>Open Work Order through a cancellable application command.</summary>
    public Task OpenWorkOrderAsync(string keyOrId, CancellationToken cancellationToken = default) => RunCommandAsync("Open Work Order", token => OpenWorkOrderCoreAsync(keyOrId, token), cancellationToken);

    /// <summary>Open Assignment through a cancellable application command.</summary>
    public Task<AssignmentDocument?> OpenAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default) => RunCommandAsync("Open Assignment", token => OpenAssignmentCoreAsync(assignmentId, token), cancellationToken);

    /// <summary>Open Composer through a cancellable application command.</summary>
    public Task<AssignmentComposer?> OpenComposerAsync(string workOrderKey, CancellationToken cancellationToken = default) => RunCommandAsync("Open Composer", token => OpenComposerCoreAsync(workOrderKey, token), cancellationToken);

    /// <summary>Open Review through a cancellable application command.</summary>
    public Task<ReviewWorkspace?> OpenReviewAsync(string recordId, CancellationToken cancellationToken = default) => RunCommandAsync("Open Review", token => OpenReviewCoreAsync(recordId, token), cancellationToken);

    /// <summary>Open Products through a cancellable application command.</summary>
    public Task OpenProductsAsync() => OpenProductsAsync(CancellationToken.None);

    /// <summary>Open Products with caller cancellation.</summary>
    public Task OpenProductsAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Products", token => OpenProductsCoreAsync(token), cancellationToken);

    /// <summary>Open Comparison through a cancellable application command.</summary>
    public Task OpenComparisonAsync(string rightId, string? leftId = null, CancellationToken cancellationToken = default) => RunCommandAsync("Open Comparison", token => OpenComparisonCoreAsync(rightId, leftId, token), cancellationToken);

    /// <summary>Open Record through a cancellable application command.</summary>
    public Task OpenRecordAsync(string id, CancellationToken cancellationToken = default) => RunCommandAsync("Open Record", token => OpenRecordCoreAsync(id, token), cancellationToken);

    /// <summary>Open Communication through a cancellable application command.</summary>
    public Task<CommunicationDocument?> OpenCommunicationAsync() => OpenCommunicationAsync(CancellationToken.None);

    /// <summary>Open Communication with caller cancellation.</summary>
    public Task<CommunicationDocument?> OpenCommunicationAsync(CancellationToken cancellationToken) => RunCommandAsync("Open Communication", token => OpenCommunicationCoreAsync(token), cancellationToken);

    private Task DocumentCommandAsync(Document? page, string label, Func<CancellationToken, Task> action, bool refresh = true,
        bool submission = false, CancellationToken cancellationToken = default)
    {
        if (page is null) return Task.CompletedTask;
        return RunCommandAsync(label, async token =>
        {
            if (submission && page.UncertainEffect is ClientFailure unresolved)
            {
                LastFailure = unresolved;
                RequestState = unresolved.Code;
                Status = unresolved.Message;
                return;
            }
            try { await action(token).ConfigureAwait(true); }
            catch (Exception exception) when (ClientFailure.FromException(exception) is ClientFailure)
            {
                ClientFailure failure = ClientFailure.FromException(exception)!;
                if (failure.OutcomeUnknown) page.UncertainEffect = failure;
                switch (page)
                {
                    case AssignmentComposer composer: composer.Status = failure.Message; break;
                    case ReviewWorkspace review: review.Status = failure.Message; break;
                    case ReleaseWorkspace release: release.Status = failure.Message; break;
                    case LivingDocumentsWorkspace document: document.Status = failure.Message; break;
                    case CommunicationDocument communicationPage: communicationPage.Note = failure.Message; break;
                }
                throw;
            }
            string outcome = page switch
            {
                AssignmentComposer composer => composer.Status,
                ReviewWorkspace review => review.Status,
                ReleaseWorkspace release => release.Status,
                LivingDocumentsWorkspace document => document.Status,
                CommunicationDocument communicationPage => communicationPage.Note,
                HandoffDocument handoff => handoff.Note,
                _ => string.Empty,
            };
            Status = outcome;
            if (refresh) await RefreshCoreAsync(token).ConfigureAwait(true);
            Status = outcome;
        }, cancellationToken);
    }

    private IEstateApi AvailableApi => Api ?? throw new HttpRequestException("The runtime is unavailable.");

    /// <summary>Sends an assignment and refreshes the views, preserving the composer's result.</summary>
    public Task SubmitAssignmentAsync(AssignmentComposer? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Send assignment for " + page?.WorkOrderKey, token => page!.SubmitAsync(AvailableApi, ActingPrincipalId, token), cancellationToken: cancellationToken, submission: true);

    /// <summary>Sends a review after rechecking its pinned subject.</summary>
    public Task SubmitReviewAsync(ReviewWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Send review", token => page!.SubmitReviewAsync(AvailableApi, token), cancellationToken: cancellationToken, submission: true);

    /// <summary>Sends an acceptance after rechecking its pinned subject.</summary>
    public Task SubmitAcceptanceAsync(ReviewWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Send acceptance", token => page!.SubmitAcceptanceAsync(AvailableApi, token), cancellationToken: cancellationToken, submission: true);

    /// <summary>Rechecks a prepared subject without sending its draft.</summary>
    public Task CheckPreparedAsync(ReviewWorkspace? page, CancellationToken cancellationToken = default) =>
        RunCommandAsync("Check prepared subject", async token =>
        {
            if (page is null) return;
            await page.CheckPreparedAsync(AvailableApi, token).ConfigureAwait(true);
            Status = page.PreparedState;
        }, cancellationToken);

    /// <summary>Checks a release form without submitting or requiring signing presence.</summary>
    public Task CheckReleaseAsync(ReleaseWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Check release " + page?.Version, token => page!.CheckAsync(AvailableApi, ActingPrincipalId, token), refresh: false, cancellationToken: cancellationToken);

    /// <summary>Submits a release intent; an interrupted response stays uncertain.</summary>
    public Task SubmitReleaseAsync(ReleaseWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Submit release " + page?.Version, token => page!.SubmitAsync(AvailableApi, ActingPrincipalId, token), cancellationToken: cancellationToken, submission: true);

    /// <summary>Observes the original release identity; it does not resubmit a release.</summary>
    public Task ObserveReleaseAsync(ReleaseWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Observe release " + page?.Version, token => page!.ObserveAsync(AvailableApi, ActingPrincipalId, token), cancellationToken: cancellationToken);

    /// <summary>Submits a living document's review through the runtime.</summary>
    public Task SubmitDocumentReviewAsync(LivingDocumentsWorkspace? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Review document", token => page!.SubmitAsync(AvailableApi, ActingPrincipalId, token), cancellationToken: cancellationToken, submission: true);

    /// <summary>Reads communication settings without changing a profile.</summary>
    public Task RefreshCommunicationDocumentAsync(CommunicationDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Read communication settings", token => page!.RefreshAsync(token), false, cancellationToken: cancellationToken);

    /// <summary>Saves the profile draft with its expected prior revision.</summary>
    public Task SaveCommunicationAsync(CommunicationDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Save communication settings", token => page!.SaveAsync(token), false, cancellationToken: cancellationToken, submission: true);

    /// <summary>Resets the profile to inheritance.</summary>
    public Task ResetCommunicationAsync(CommunicationDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Reset communication settings", token => page!.ResetAsync(token), false, cancellationToken: cancellationToken, submission: true);

    /// <summary>Prepares a hand-over while retaining the previous package until a complete response arrives.</summary>
    public Task PrepareHandoffAsync(HandoffDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Prepare hand-over", token => page!.PrepareAsync(token), false, cancellationToken: cancellationToken);

    /// <summary>Compares the retained package without replacing its original basis.</summary>
    public Task CompareHandoffAsync(HandoffDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Compare hand-over", token => page!.CompareAsync(token), false, cancellationToken: cancellationToken);

    /// <summary>Reads and validates a saved hand-over through the runtime.</summary>
    public Task LoadHandoffAsync(HandoffDocument? page, string path, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Load hand-over", async token =>
            await page!.LoadAsync(await File.ReadAllTextAsync(path, token).ConfigureAwait(true), token).ConfigureAwait(true), false, cancellationToken: cancellationToken);
    /// <summary>Saves the retained package without blocking the UI while writing its bytes.</summary>
    public Task SaveHandoffAsync(HandoffDocument? page, string path, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Save hand-over", async token => await page!.SaveAsync(path, token).ConfigureAwait(true), false, cancellationToken: cancellationToken);

}
