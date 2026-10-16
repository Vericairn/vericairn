// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Vericairn.Presentation;

namespace Vericairn.Workbench;

/// <summary>The shell window: regions, keyboard alternatives for every pointer arrangement, and the handlers that call the view model.</summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel shell;

    /// <summary>The shell over the production connection and runtime manager.</summary>
    public MainWindow() : this(new ShellViewModel(root => new DaemonConnection(root), new CliRuntimeManager(), new Preferences()))
    {
    }

    /// <summary>The shell over a given view model (tests inject their own connection).</summary>
    public MainWindow(ShellViewModel shell)
    {
        this.shell = shell;
        DataContext = shell;
        InitializeComponent();
        // Enter on a list or a box acts before the control's own key handling can swallow it.
        RecordIdBox.AddHandler(KeyDownEvent, OnRecordIdKeyDown, RoutingStrategies.Tunnel);
        EstatePathBox.AddHandler(KeyDownEvent, OnEstatePathKeyDown, RoutingStrategies.Tunnel);
        RecentList.AddHandler(KeyDownEvent, OnRecentKeyDown, RoutingStrategies.Tunnel);
        AttentionList.AddHandler(KeyDownEvent, OnAttentionKeyDown, RoutingStrategies.Tunnel);
        ProgressList.AddHandler(KeyDownEvent, OnProgressKeyDown, RoutingStrategies.Tunnel);
        // Lists inside documents are templated, so Enter for them is caught at the window before any list sees it.
        AddHandler(KeyDownEvent, OnWindowTunnelKeyDown, RoutingStrategies.Tunnel);
        shell.PropertyChanged += OnShellChanged;
        ApplyLayout();
        Closing += OnClosing;
    }

    /// <summary>The view model.</summary>
    public ShellViewModel Shell => shell;

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool handled = true;
        if (e.Key == Key.F5)
        {
            shell.Refresh();
        }
        else if (command && shift && e.Key == Key.L)
        {
            shell.ToggleLeft();
        }
        else if (command && shift && e.Key == Key.R)
        {
            shell.ToggleRight();
        }
        else if (command && shift && e.Key == Key.B)
        {
            shell.ToggleBottom();
        }
        else if (command && shift && e.Key == Key.D0)
        {
            shell.ResetLayout();
        }
        else if (command && e.Key == Key.D1)
        {
            FocusRegion(Navigation);
        }
        else if (command && e.Key == Key.D2)
        {
            FocusRegion(shell.HomeSelected && shell.HasEstate ? AttentionList : DocumentTabs);
        }
        else if (command && e.Key == Key.D3)
        {
            FocusRegion(InspectorText);
        }
        else if (command && e.Key == Key.D4)
        {
            FocusRegion(ActivityList);
        }
        else if (command && e.Key == Key.O)
        {
            FocusRegion(EstatePathBox);
        }
        else if (command && !shift && e.Key == Key.G)
        {
            FocusRegion(RecordIdBox);
        }
        else if (command && e.Key == Key.I)
        {
            shell.OpenInbox();
            FocusRegion(DocumentTabs);
        }
        else if (command && shift && e.Key == Key.E)
        {
            shell.OpenRelease();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.D)
        {
            shell.OpenLivingDocuments();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.P)
        {
            shell.OpenProgress();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.H)
        {
            shell.OpenHandoff();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.G)
        {
            shell.OpenGoals();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.U)
        {
            shell.OpenRuntime();
            FocusDocument();
        }
        else if (command && shift && e.Key == Key.A)
        {
            OnOpenComposer(null, new RoutedEventArgs());
            FocusDocument();
        }
        else if (e.Key == Key.Escape)
        {
            FocusRegion(Navigation);
        }
        else if (e.Key == Key.Enter && ReferenceEquals(e.Source, this))
        {
            // Some platforms deliver keys to the window rather than to the focused element; Enter then acts on the focused region.
            handled = EnterOnFocusedRegion();
        }
        else
        {
            handled = false;
        }

        e.Handled = handled;
        if (!handled)
        {
            base.OnKeyDown(e);
        }
    }

    private void OnWindowTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && EnterOnDocumentList())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && command)
        {
            // Ctrl+Enter sends the open document's request, Ctrl+Shift+Enter its second one, before any text box can take the key.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnSecondarySend(null, new RoutedEventArgs());
            }
            else
            {
                OnPrimarySend(null, new RoutedEventArgs());
            }

            e.Handled = true;
        }
    }

    /// <summary>Focuses the first focusable control inside the open document, so a keyboard user lands in the form rather than on the tab strip.</summary>
    private void FocusDocument(bool retry = true)
    {
        DocumentTabs.UpdateLayout();
        Document? selected = shell.SelectedDocument;
        InputElement? first = selected is null ? null : DocumentTabs.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(i => i is not TabItem && i is not ScrollViewer && i.Focusable && i.IsEffectivelyEnabled && ReferenceEquals(i.DataContext, selected) && i.FindAncestorOfType<TabItem>() is null);
        if (first is not null)
        {
            first.Focus();
        }
        else if (retry)
        {
            // The tab's content is realized by the next layout pass; try once more after it.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FocusDocument(false), Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            FocusRegion(DocumentTabs);
        }
    }

    /// <summary>Enter on a list inside a document: the inbox opens the review workspace; an assignment opens its latest result's review.</summary>
    private bool EnterOnDocumentList()
    {
        if (FocusManager?.GetFocusedElement() is not Visual focused)
        {
            return false;
        }

        ListBox? list = focused as ListBox ?? focused.FindAncestorOfType<ListBox>();
        if (list?.DataContext is ResultInboxDocument && list.SelectedItem is InboxRow row)
        {
            shell.OpenReview(row.ResultId);
            return true;
        }

        if (list?.DataContext is WorkOrderDocument && list.SelectedItem is AssignmentView assignment)
        {
            shell.OpenAssignment(assignment.Id);
            return true;
        }

        if (list?.DataContext is AssignmentDocument && list.SelectedItem is AttemptView attempt && attempt.ResultId is not null)
        {
            shell.OpenReview(attempt.ResultId);
            return true;
        }

        if (list?.DataContext is ReleaseWorkspace && list.SelectedItem is DestinationRow destination)
        {
            if (destination.ReleaseId is not null)
            {
                shell.OpenReview(destination.ReleaseId);
            }
            else
            {
                shell.Status = "No candidate is recorded for this destination yet; observe the run and the draft first.";
            }

            return true;
        }

        if (list?.DataContext is ReleaseWorkspace && list.SelectedItem is ReleaseRowView release)
        {
            shell.OpenReview(release.Id);
            return true;
        }

        return false;
    }

    private bool EnterOnFocusedRegion()
    {
        if (EnterOnDocumentList())
        {
            return true;
        }

        if (AttentionList.IsKeyboardFocusWithin && AttentionList.SelectedItem is AttentionItem item)
        {
            shell.OpenAttention(item);
            return true;
        }

        if (ProgressList.IsKeyboardFocusWithin && ProgressList.SelectedItem is ProgressRow)
        {
            shell.OpenProgress();
            return true;
        }

        if (RecentList.IsKeyboardFocusWithin && RecentList.SelectedItem is string root)
        {
            _ = shell.OpenEstateAsync(root);
            return true;
        }

        if (EstatePathBox.IsKeyboardFocusWithin)
        {
            _ = shell.OpenEstateAsync(EstatePathBox.Text ?? string.Empty);
            return true;
        }

        if (RecordIdBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(RecordIdBox.Text))
        {
            shell.OpenRecord(RecordIdBox.Text.Trim());
            return true;
        }

        return false;
    }

    private static void FocusRegion(Control control)
    {
        if (!control.IsEffectivelyVisible)
        {
            return;
        }

        if (control is SelectingItemsControl items && items.ItemCount > 0)
        {
            if (items.SelectedIndex < 0)
            {
                items.SelectedIndex = 0;
            }

            if (items.ContainerFromIndex(items.SelectedIndex) is Control container && container.Focusable)
            {
                container.Focus();
                return;
            }
        }

        control.Focus();
    }

    private void ApplyLayout()
    {
        ShellLayout layout = shell.Layout;
        Body.ColumnDefinitions[0].Width = layout.LeftVisible ? new GridLength(layout.LeftWidth) : new GridLength(0);
        Body.ColumnDefinitions[1].Width = layout.LeftVisible ? new GridLength(4) : new GridLength(0);
        Body.ColumnDefinitions[4].Width = layout.RightVisible ? new GridLength(layout.RightWidth) : new GridLength(0);
        Body.ColumnDefinitions[3].Width = layout.RightVisible ? new GridLength(4) : new GridLength(0);
        Body.RowDefinitions[2].Height = layout.BottomVisible ? new GridLength(layout.BottomHeight) : new GridLength(0);
        Body.RowDefinitions[1].Height = layout.BottomVisible ? new GridLength(4) : new GridLength(0);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Layout) or nameof(ShellViewModel.LeftVisible) or nameof(ShellViewModel.RightVisible) or nameof(ShellViewModel.BottomVisible))
        {
            ApplyLayout();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        ShellLayout layout = shell.Layout;
        double left = layout.LeftVisible ? Body.ColumnDefinitions[0].ActualWidth : layout.LeftWidth;
        double right = layout.RightVisible ? Body.ColumnDefinitions[4].ActualWidth : layout.RightWidth;
        double bottom = layout.BottomVisible ? Body.RowDefinitions[2].ActualHeight : layout.BottomHeight;
        if (left > 0 && right > 0 && bottom > 0)
        {
            shell.RememberSizes(left, right, bottom);
        }
    }

    private async void OnOpenEstate(object? sender, RoutedEventArgs e) => await shell.OpenEstateAsync(shell.EstatePath).ConfigureAwait(true);

    private async void OnEstatePathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await shell.OpenEstateAsync(EstatePathBox.Text ?? string.Empty).ConfigureAwait(true);
        }
    }

    private async void OnRecentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RecentList.SelectedItem is string root)
        {
            e.Handled = true;
            await shell.OpenEstateAsync(root).ConfigureAwait(true);
        }
    }

    private async void OnRecentOpen(object? sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is string root)
        {
            await shell.OpenEstateAsync(root).ConfigureAwait(true);
        }
    }

    private void OnRecordIdKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(RecordIdBox.Text))
        {
            e.Handled = true;
            shell.OpenRecord(RecordIdBox.Text.Trim());
            FocusRegion(DocumentTabs);
        }
    }

    private void OnAttentionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AttentionList.SelectedItem is AttentionItem item)
        {
            e.Handled = true;
            shell.OpenAttention(item);
        }
    }

    private void OnAttentionOpen(object? sender, RoutedEventArgs e)
    {
        if (AttentionList.SelectedItem is AttentionItem item)
        {
            shell.OpenAttention(item);
        }
    }

    private void OnAttentionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (AttentionList.SelectedItem is AttentionItem item)
        {
            shell.Inspect(item);
        }
    }

    private void OnProgressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProgressList.SelectedItem is ProgressRow)
        {
            e.Handled = true;
            shell.OpenProgress();
            FocusDocument();
        }
    }

    private void OnOpenProgress(object? sender, RoutedEventArgs e)
    {
        shell.OpenProgress();
        FocusDocument();
    }

    private void OnOpenHandoff(object? sender, RoutedEventArgs e)
    {
        shell.OpenHandoff();
        FocusDocument();
    }

    private void OnPrepareHandoff(object? sender, RoutedEventArgs e) => DocumentOf<HandoffDocument>(sender)?.Prepare();

    private void OnResumeHandoff(object? sender, RoutedEventArgs e) => DocumentOf<HandoffDocument>(sender)?.Compare();

    private void OnObserveEstate(object? sender, RoutedEventArgs e) => shell.ObserveEstate();

    private async void OnSaveHandoff(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<HandoffDocument>(sender) is not { HasPackage: true } page)
        {
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save hand-over package in a new file",
            SuggestedFileName = "handoff.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Hand-over package") { Patterns = ["*.json"] }],
        }).ConfigureAwait(true);
        if (file?.TryGetLocalPath() is { } path)
        {
            page.Save(path);
        }
    }

    private async void OnLoadHandoff(object? sender, RoutedEventArgs e)
    {
        HandoffDocument page = DocumentOf<HandoffDocument>(sender) ?? shell.OpenHandoff();
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open retained hand-over package",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Hand-over package") { Patterns = ["*.json"] }],
        }).ConfigureAwait(true);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            try
            {
                page.Load(await File.ReadAllTextAsync(path).ConfigureAwait(true));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                shell.Status = "The package could not be opened: " + exception.Message;
            }
        }
    }

    private void OnOpenGoals(object? sender, RoutedEventArgs e)
    {
        shell.OpenGoals();
        FocusDocument();
    }

    private void OnOpenInbox(object? sender, RoutedEventArgs e)
    {
        shell.OpenInbox();
        FocusRegion(DocumentTabs);
    }

    private void OnOpenProducts(object? sender, RoutedEventArgs e) => shell.OpenProducts();

    private void OnOpenLivingDocuments(object? sender, RoutedEventArgs e)
    {
        shell.OpenLivingDocuments();
        FocusDocument();
    }

    private void OnSubmitDocumentReview(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<LivingDocumentsWorkspace>(sender) is LivingDocumentsWorkspace page && shell.Api is not null)
        {
            page.Submit(shell.Api, shell.ActingPrincipalId);
            shell.Status = page.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnOpenComposer(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<WorkOrderDocument>(sender) is WorkOrderDocument order)
        {
            shell.OpenComposer(order.Key);
        }
        else
        {
            shell.Status = "Open a work order first; the composer assigns the open work order.";
        }
    }

    private void OnCompareOpen(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<WorkOrderDocument>(sender) is WorkOrderDocument order)
        {
            shell.OpenComparison(order.Id);
        }
        else if (DocumentOf<ReviewWorkspace>(sender) is ReviewWorkspace workspace)
        {
            shell.OpenComparison(workspace.SubjectId);
        }
        else if (!string.IsNullOrWhiteSpace(RecordIdBox.Text))
        {
            shell.OpenComparison(RecordIdBox.Text.Trim());
        }
        else
        {
            shell.Status = "Open a work order or a review, or type a record id, to compare it with the revision it supersedes.";
        }
    }

    private void OnOpenSelectedAssignment(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<WorkOrderDocument>(sender) is WorkOrderDocument order)
        {
            if (order.SelectedAssignment is AssignmentView assignment)
            {
                shell.OpenAssignment(assignment.Id);
            }
            else
            {
                shell.Status = "Select an assignment first.";
            }
        }
    }

    private void OnReviewLatestResult(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<AssignmentDocument>(sender) is AssignmentDocument page)
        {
            if (page.SelectedAttempt?.ResultId is string selected)
            {
                shell.OpenReview(selected);
            }
            else if (page.LatestResultId is string latest)
            {
                shell.OpenReview(latest);
            }
            else
            {
                shell.Status = "This assignment has no result yet.";
            }
        }
    }

    private void OnReviewSelectedAssignment(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<WorkOrderDocument>(sender) is WorkOrderDocument order)
        {
            if (order.SelectedAssignment?.LatestResultId is string result)
            {
                shell.OpenReview(result);
            }
            else
            {
                shell.Status = "Select an assignment with a result first.";
            }
        }
    }

    private void OnSubmitAssignment(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<AssignmentComposer>(sender) is AssignmentComposer composer && shell.Api is not null)
        {
            composer.Submit(shell.Api, shell.ActingPrincipalId);
            shell.Status = composer.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnInboxOpen(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ResultInboxDocument>(sender) is ResultInboxDocument inbox && inbox.Selected is InboxRow row)
        {
            shell.OpenReview(row.ResultId);
        }
    }

    private void OnSubmitReview(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReviewWorkspace>(sender) is ReviewWorkspace workspace && shell.Api is not null)
        {
            workspace.SubmitReview(shell.Api);
            shell.Status = workspace.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnSubmitAcceptance(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReviewWorkspace>(sender) is ReviewWorkspace workspace && shell.Api is not null)
        {
            workspace.SubmitAcceptance(shell.Api);
            shell.Status = workspace.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnCheckPrepared(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReviewWorkspace>(sender) is ReviewWorkspace workspace && shell.Api is not null)
        {
            workspace.CheckPrepared(shell.Api);
            shell.Status = workspace.PreparedState;
        }
    }

    private void OnOpenRelease(object? sender, RoutedEventArgs e)
    {
        shell.OpenRelease();
        FocusDocument();
    }

    private void OnOpenRuntime(object? sender, RoutedEventArgs e)
    {
        shell.OpenRuntime();
        FocusDocument();
    }

    private async void OnInstallRuntime(object? sender, RoutedEventArgs e) => await shell.InstallRuntimeAsync().ConfigureAwait(true);

    private async void OnUninstallRuntime(object? sender, RoutedEventArgs e) => await shell.UninstallRuntimeAsync().ConfigureAwait(true);

    private void OnSubmitRelease(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReleaseWorkspace>(sender) is ReleaseWorkspace workspace && shell.Api is not null)
        {
            workspace.Submit(shell.Api, shell.ActingPrincipalId);
            shell.Status = workspace.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnObserveRelease(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReleaseWorkspace>(sender) is ReleaseWorkspace workspace && shell.Api is not null)
        {
            workspace.Observe(shell.Api, shell.ActingPrincipalId);
            shell.Status = workspace.Status.Split('\n')[0];
            shell.Refresh();
        }
    }

    private void OnAcceptCandidate(object? sender, RoutedEventArgs e)
    {
        if (DocumentOf<ReleaseWorkspace>(sender) is ReleaseWorkspace workspace)
        {
            if (workspace.Selected?.ReleaseId is string release)
            {
                shell.OpenReview(release);
            }
            else
            {
                shell.Status = "Select a destination with a recorded candidate first.";
            }
        }
    }

    /// <summary>The open document's request: the composer sends the assignment, the review workspace the review, the release workspace the submission.</summary>
    private void OnPrimarySend(object? sender, RoutedEventArgs e)
    {
        switch (shell.SelectedDocument)
        {
            case AssignmentComposer:
                OnSubmitAssignment(null, e);
                break;
            case ReviewWorkspace:
                OnSubmitReview(null, e);
                break;
            case ReleaseWorkspace:
                OnSubmitRelease(null, e);
                break;
            case LivingDocumentsWorkspace:
                OnSubmitDocumentReview(null, e);
                break;
            default:
                shell.Status = "The open document has nothing to send.";
                break;
        }
    }

    /// <summary>The open document's second request: the review workspace sends the acceptance, the release workspace observes.</summary>
    private void OnSecondarySend(object? sender, RoutedEventArgs e)
    {
        switch (shell.SelectedDocument)
        {
            case ReviewWorkspace:
                OnSubmitAcceptance(null, e);
                break;
            case ReleaseWorkspace:
                OnObserveRelease(null, e);
                break;
            default:
                shell.Status = "The open document has no second request.";
                break;
        }
    }

    /// <summary>The document a handler acts on: the sender's data context when it is inside that document, else the selected document.</summary>
    private T? DocumentOf<T>(object? sender) where T : Document => (sender as Control)?.DataContext as T ?? shell.SelectedDocument as T;

    private void OnFocusOpen(object? sender, RoutedEventArgs e) => FocusRegion(EstatePathBox);

    private void OnRefresh(object? sender, RoutedEventArgs e) => shell.Refresh();

    private async void OnStartRuntime(object? sender, RoutedEventArgs e) => await shell.StartRuntimeAsync().ConfigureAwait(true);

    private async void OnStopRuntime(object? sender, RoutedEventArgs e) => await shell.StopRuntimeAsync().ConfigureAwait(true);

    private async void OnInstallPlan(object? sender, RoutedEventArgs e) => await shell.ShowInstallPlanAsync().ConfigureAwait(true);

    private void OnToggleLeft(object? sender, RoutedEventArgs e) => shell.ToggleLeft();

    private void OnToggleRight(object? sender, RoutedEventArgs e) => shell.ToggleRight();

    private void OnToggleBottom(object? sender, RoutedEventArgs e) => shell.ToggleBottom();

    private void OnResetLayout(object? sender, RoutedEventArgs e) => shell.ResetLayout();

    private void OnFocusNavigation(object? sender, RoutedEventArgs e) => FocusRegion(Navigation);

    private void OnFocusDocuments(object? sender, RoutedEventArgs e) => FocusRegion(DocumentTabs);

    private void OnFocusInspector(object? sender, RoutedEventArgs e) => FocusRegion(InspectorText);

    private void OnFocusActivity(object? sender, RoutedEventArgs e) => FocusRegion(ActivityList);
}
