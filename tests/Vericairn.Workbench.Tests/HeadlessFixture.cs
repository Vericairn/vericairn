// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Vericairn.Workbench;

[assembly: AssemblyFixture(typeof(Vericairn.Workbench.Tests.HeadlessFixture))]

namespace Vericairn.Workbench.Tests;

/// <summary>Builds the application on the headless platform; the session's dispatcher thread runs every UI test.</summary>
public static class TestApp
{
    /// <summary>Called by the headless session.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>One headless Avalonia session shared by the collection; every test dispatches onto its UI thread.</summary>
public sealed class HeadlessFixture : IDisposable
{
    /// <summary>The session.</summary>
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    /// <summary>Runs an action on the UI thread and drains the dispatcher so layout and input have settled.</summary>
    public Task Ui(Action action) => Session.Dispatch(() => { action(); Dispatcher.UIThread.RunJobs(); }, TestContext.Current.CancellationToken);

    /// <summary>Runs a function on the UI thread.</summary>
    public Task<T> Ui<T>(Func<T> function) => Session.Dispatch(() => { T result = function(); Dispatcher.UIThread.RunJobs(); return result; }, TestContext.Current.CancellationToken);

    /// <summary>Runs an asynchronous action on the UI thread, so that view models bound to controls are driven where the controls live.</summary>
    public Task UiAsync(Func<Task> action) => Session.Dispatch(async () => { await action().ConfigureAwait(true); Dispatcher.UIThread.RunJobs(); return true; }, TestContext.Current.CancellationToken);

    /// <summary>
    /// Nothing to dispose: the session's loop runs on a thread-pool task, which never keeps the process alive, while
    /// HeadlessUnitTestSession.Dispose waits for that loop with cancellation callbacks bound to the dispatcher's
    /// synchronization context and deadlocks (observed with Avalonia 12.1.4 on October 14, 2026). The process ends when the
    /// tests end.
    /// </summary>
    public void Dispose()
    {
    }
}

