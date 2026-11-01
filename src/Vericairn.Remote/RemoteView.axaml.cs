// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.ComponentModel;

namespace Vericairn.Remote;

/// <summary>Remote's one view; its behaviour is <see cref="RemoteViewModel"/>'s.</summary>
public partial class RemoteView : UserControl
{
    private RemoteViewModel? model;
    /// <summary>Creates the view.</summary>
    public RemoteView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= DetailChanged;
            model = DataContext as RemoteViewModel;
            if (model is not null) model.PropertyChanged += DetailChanged;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => (DataContext as RemoteViewModel)?.RefreshFreshness();
        AttachedToVisualTree += (_, _) => timer.Start();
        DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    private void DetailChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RemoteViewModel.WorkDetail))
            Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("WorkScroll")!.Offset = default);
    }
}
