// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vericairn.Remote;

/// <summary>Remote's one view; its behaviour is <see cref="RemoteViewModel"/>'s.</summary>
public partial class RemoteView : UserControl
{
    /// <summary>Creates the view.</summary>
    public RemoteView() => AvaloniaXamlLoader.Load(this);
}
