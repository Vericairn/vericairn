// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        else if (command && e.Key == Key.G)
        {
            FocusRegion(RecordIdBox);
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

    private bool EnterOnFocusedRegion()
    {
        if (AttentionList.IsKeyboardFocusWithin && AttentionList.SelectedItem is AttentionItem item)
        {
            shell.OpenAttention(item);
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
