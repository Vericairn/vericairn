// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Vericairn.Workbench;

/// <summary>The application: one main window, a client of the daemon. Arguments: --estate root, --as principal, --probe file [--cell name] [--virtual].</summary>
public partial class App : Application
{
    /// <summary>The command-line arguments, set by Program before the lifetime starts.</summary>
    public static IReadOnlyList<string> Arguments { get; set; } = [];

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            string? estate = Value("--estate");
            string? probe = Value("--probe");
            window.Shell.ActingOverride = Value("--as");
            if (estate is not null)
            {
                window.Opened += async (_, _) => await window.Shell.OpenEstateAsync(estate).ConfigureAwait(true);
            }

            if (probe is not null)
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    File.WriteAllText(probe, Probe.Report(window, Value("--cell") ?? "unnamed cell", Arguments.Contains("--virtual")));
                    desktop.Shutdown();
                }, DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? Value(string option)
    {
        for (int i = 0; i + 1 < Arguments.Count; i++)
        {
            if (Arguments[i] == option)
            {
                return Arguments[i + 1];
            }
        }

        return null;
    }
}
