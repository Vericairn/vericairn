// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.TestSupport;

internal static class SilentPeer
{
    // The operation's deadline includes connection setup. Keep an accepted peer silent for the
    // assertion, but also finish when cold setup exhausts that deadline before a connection exists.
    internal static async Task<T?> AcceptUntilCompletedAsync<T>(Func<CancellationToken, Task<T>> accept,
        Task operation, CancellationToken cancellationToken) where T : class, IDisposable
    {
        using var stopAccepting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<T> accepting = accept(stopAccepting.Token);
        T? peer = null;
        bool completed = false;
        try
        {
            await Task.WhenAny(accepting, operation).WaitAsync(TestTiming.HangTimeout, cancellationToken);
            completed = true;
        }
        finally
        {
            stopAccepting.Cancel();
            try { peer = await accepting; }
            catch (OperationCanceledException) when (stopAccepting.IsCancellationRequested) { }
            // A connection can win the race with cleanup. Its socket still needs an owner even
            // when the hang guard or the test's own cancellation ended the wait.
            if (!completed) peer?.Dispose();
        }

        return peer;
    }
}
