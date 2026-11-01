// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Vericairn.Remote;

/// <summary>The phone application: one view, a client of the runtimes this phone is paired with; never a host of a runtime.</summary>
public partial class RemoteApp : Application
{
    /// <summary>The phone's connections, which the platform head supplies before the application starts: its storage and key store.</summary>
    public static Func<IRemoteConnections>? Connections { get; set; }

    /// <summary>The phone's text-size setting as a scale of the default, which the platform head reads before the application starts.</summary>
    public static double TextScale { get; set; } = 1;

    /// <summary>Scales Remote's text sizes, kept within what its layouts were checked at (0.8 to 3 times the default).</summary>
    public static void ApplyTextScale(IResourceDictionary resources, double scale)
    {
        ArgumentNullException.ThrowIfNull(resources);
        double clamped = double.IsFinite(scale) ? Math.Clamp(scale, 0.8, 3) : 1;
        resources["ControlContentThemeFontSize"] = 14 * clamped;
        resources["RemoteSmallFontSize"] = 12 * clamped;
        resources["RemoteHeadingFontSize"] = 18 * clamped;
    }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            IRemoteConnections connections = (Connections ?? throw new InvalidOperationException("The platform head supplies the phone's connections."))();
            ApplyTextScale(Resources, TextScale);
            var model = new RemoteViewModel(connections);
            single.MainView = new RemoteView { DataContext = model };
            // Opening refreshes the selected estate before anything is relied on; a restored screen restores no permission.
            if (model.Selected is not null) Dispatcher.UIThread.Post(async () => await model.RefreshCommand.ExecuteAsync().ConfigureAwait(true));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
