// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService : IClientOperationLifetime
{
    private readonly object lifetimeGate = new();
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
        session.Dispose();
    }

    private sealed class ClientLease(EstateService service) : IDisposable
    {
        private EstateService? owner = service;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseClientOperation();
    }
}
