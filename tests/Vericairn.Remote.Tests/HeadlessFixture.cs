// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Vericairn.Records.Signing;
using Vericairn.Remote;

[assembly: AssemblyFixture(typeof(Vericairn.Remote.Tests.HeadlessFixture))]

namespace Vericairn.Remote.Tests;

/// <summary>Builds the application on the headless platform; the session's dispatcher thread runs every UI test.</summary>
public static class TestApp
{
    /// <summary>Called by the headless session.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<RemoteApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>One headless Avalonia session for the assembly, with its own Vericairn home so no test touches the person's.</summary>
public sealed class HeadlessFixture : IDisposable
{
    /// <summary>Gives the test process its own Vericairn home before any test runs.</summary>
    public HeadlessFixture() =>
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Directory.CreateTempSubdirectory("vericairn-remote-tests-home-").FullName);

    /// <summary>The session.</summary>
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    /// <summary>Runs an asynchronous function on the UI thread and returns its result, the dispatcher drained.</summary>
    public Task<T> UiAsync<T>(Func<Task<T>> function) => Session.Dispatch(async () =>
    {
        T result = await function().ConfigureAwait(true);
        Dispatcher.UIThread.RunJobs();
        return result;
    }, TestContext.Current.CancellationToken);

    /// <summary>
    /// Nothing to dispose: HeadlessUnitTestSession.Dispose deadlocks with Avalonia 12.1.4 (see Workbench's fixture), and the session's
    /// loop never keeps the process alive.
    /// </summary>
    public void Dispose()
    {
    }
}
