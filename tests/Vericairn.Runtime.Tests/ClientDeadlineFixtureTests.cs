// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public sealed class ClientDeadlineFixtureTests
{
    [Fact]
    public async Task ADeadlineBeforeConnectionDoesNotLeaveTheFixtureWaitingForAPeer()
    {
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var accepting = new TaskCompletionSource<Peer>();
        Task operation = Task.FromException(new ClientTimeoutException(false));
        CancellationTokenRegistration registration = default;
        Task<Peer?> observation = SilentPeer.AcceptUntilCompletedAsync(token =>
        {
            registration = token.Register(() => accepting.TrySetCanceled(token));
            return accepting.Task;
        }, operation, cleanup.Token);
        try
        {
            // Force exactly the cold-start ordering: the deadline has fired, and no connection can
            // ever arrive. Neither the reproduction nor the assertion waits for a wall-clock timer.
            Assert.True(observation.IsCompleted, "The fixture still waits for a connection after the operation timed out.");
            Assert.Null(await observation);
            Assert.True(accepting.Task.IsCanceled);
            ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() => operation);
            Assert.False(failure.OutcomeUnknown);
            Assert.Equal("timed-out", ClientFailure.FromException(failure)?.Code);
        }
        finally
        {
            cleanup.Cancel();
            try { (await observation)?.Dispose(); }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { }
            registration.Dispose();
        }
    }

    [Fact]
    public async Task AnAcceptedPeerStaysOpenWhileTheOperationIsStillPending()
    {
        using var peer = new Peer();
        var operation = new TaskCompletionSource();
        using Peer? accepted = await SilentPeer.AcceptUntilCompletedAsync(_ => Task.FromResult(peer),
            operation.Task, TestContext.Current.CancellationToken);

        Assert.Same(peer, accepted);
        Assert.False(peer.Disposed);
        Assert.False(operation.Task.IsCompleted);
    }

    [Fact]
    public async Task AConnectionThatWinsAgainstAcceptCancellationStillHasAnOwner()
    {
        using var peer = new Peer();
        var accepting = new TaskCompletionSource<Peer>();
        CancellationTokenRegistration registration = default;
        try
        {
            using Peer? accepted = await SilentPeer.AcceptUntilCompletedAsync(token =>
            {
                registration = token.Register(() => accepting.SetResult(peer));
                return accepting.Task;
            }, Task.CompletedTask, TestContext.Current.CancellationToken);
            Assert.Same(peer, accepted);
            Assert.False(peer.Disposed);
        }
        finally { registration.Dispose(); }
        Assert.True(peer.Disposed);
    }

    private sealed class Peer : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
