// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService : IClientOperationLifetime, IClientOperationClock
{
    private readonly object lifetimeGate = new();

    /// <summary>
    /// From 0.9.4 (FIX-152): the clock this runtime's embedded client operations measure their request limits on; a test's stand-in stops
    /// a read at its limit without waiting. The system's clock in every host.
    /// </summary>
    internal TimeProvider OperationClock { get; set; } = TimeProvider.System;

    TimeProvider IClientOperationClock.OperationClock => OperationClock;
    private int clientOperations;
    private bool closeRequested;
    private bool resourcesClosed;

    /// <inheritdoc />
    public IDisposable RetainClientOperation()
    {
        lock (lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(closeRequested, this);
            clientOperations++;
            return new ClientLease(this);
        }
    }

    private void ReleaseClientOperation()
    {
        lock (lifetimeGate)
        {
            clientOperations--;
            if (closeRequested && clientOperations == 0) CloseResources();
        }
    }

    private void CloseResources()
    {
        if (resourcesClosed) return;
        resourcesClosed = true;
        runDispatch.Dispose();
        keywordIndex?.Dispose();
        // Closed cleanly before the store, so the next start continues this epoch.
        journal?.Dispose();
        session.Dispose();
    }

    private sealed class ClientLease(EstateService service) : IDisposable
    {
        private EstateService? owner = service;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseClientOperation();
    }
}
