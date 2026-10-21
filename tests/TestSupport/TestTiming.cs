// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Threading.Channels;

namespace Vericairn.TestSupport;

/// <summary>Ceilings detect a hung test. Only a signal or a controlled clock establishes readiness.</summary>
internal static class TestTiming
{
    internal const int HangTimeoutMilliseconds = 120_000;
    internal static readonly TimeSpan HangTimeout = TimeSpan.FromMilliseconds(HangTimeoutMilliseconds);

    /// <summary>The ceiling for a whole journey of many signed writes and Git commands. Such a journey takes about forty
    /// seconds on a fast machine and more than two minutes on a loaded Windows runner, so the ordinary ceiling would stop a
    /// slow journey rather than a hung one.</summary>
    internal static readonly TimeSpan JourneyHangTimeout = TimeSpan.FromMinutes(10);

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<bool> CompletesAsync(Task task, CancellationToken token)
    {
        try { await task.WaitAsync(HangTimeout, token); return true; }
        catch (TimeoutException) { return false; }
    }

    internal static Task UntilCancelled(CancellationToken token) =>
        new TaskCompletionSource().Task.WaitAsync(token);
}

/// <summary>Observe test-owned files without sleeping or racing a notification against the initial check.</summary>
internal sealed class FileSignal : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly MacFileNotifications? native;
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    internal FileSignal(params string[] directories)
    {
        foreach (string directory in directories)
            Assert.True(Directory.Exists(directory), "The test-owned directory must exist before watching it.");
        if (OperatingSystem.IsMacOS())
        {
            native = new MacFileNotifications(directories, Notify, error => changes.Writer.TryComplete(error));
            return;
        }

        foreach (string directory in directories)
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Changed;
            watcher.Error += (_, args) => changes.Writer.TryComplete(args.GetException());
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    private void Changed(object sender, FileSystemEventArgs args) => changes.Writer.TryWrite(true);
    internal void Notify() => changes.Writer.TryWrite(true);

    internal async Task UntilAsync(Func<bool> condition, string because, CancellationToken cancellationToken = default)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        try
        {
            while (!Holds()) await changes.Reader.ReadAsync(guard.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert.Fail("Timed out waiting for " + because);
        }

        // Windows refuses a read while another process replaces the file; that moment means not yet, so check again.
        bool Holds()
        {
            try
            {
                return condition();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                changes.Writer.TryWrite(true);
                return false;
            }
        }
    }

    internal void Until(Func<bool> condition, string because, CancellationToken cancellationToken = default) =>
        UntilAsync(condition, because, cancellationToken).GetAwaiter().GetResult();

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS()) native?.Dispose();
        foreach (FileSystemWatcher watcher in watchers) watcher.Dispose();
        changes.Writer.TryComplete();
    }
}
