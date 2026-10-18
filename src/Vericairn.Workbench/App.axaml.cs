// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Vericairn.Workbench;

/// <summary>The application: one main window, a client of the daemon. Arguments: --estate root, --as principal, --open products, inbox, release, runtime, goals or a work-order key, --probe file [--cell name] [--virtual].</summary>
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
            string? open = Value("--open");
            window.Opened += async (_, _) =>
            {
                if (estate is not null)
                {
                    await window.Shell.OpenEstateAsync(estate).ConfigureAwait(true);
                    // --open names a workspace to show after the estate opens, so a measurement can cover it.
                    switch (open)
                    {
                        case "products":
                            await window.Shell.OpenProductsAsync().ConfigureAwait(true);
                            break;
                        case "inbox":
                            window.Shell.OpenInbox();
                            break;
                        case "release":
                            await window.Shell.OpenReleaseAsync().ConfigureAwait(true);
                            break;
                        case "runtime":
                            await window.Shell.OpenRuntimeAsync().ConfigureAwait(true);
                            break;
                        case "goals":
                            await window.Shell.OpenGoalsAsync().ConfigureAwait(true);
                            break;
                        case null:
                            break;
                        default:
                            await window.Shell.OpenWorkOrderAsync(open).ConfigureAwait(true);
                            break;
                    }
                }
                if (probe is not null)
                {
                    string report = await Dispatcher.UIThread.InvokeAsync(() =>
                        Probe.Report(window, Value("--cell") ?? "unnamed cell", Arguments.Contains("--virtual")), DispatcherPriority.Background);
                    await File.WriteAllTextAsync(probe, report).ConfigureAwait(true);
                    desktop.Shutdown();
                }
            };
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
