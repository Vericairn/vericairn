// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;

namespace Vericairn.Workbench;

/// <summary>Entry point; the Avalonia lifetime owns the window.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.Arguments = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by the headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
