// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public OperatorRequestsResponse OperatorRequests(string? principal = null) => inner.OperatorRequestsAsync(principal, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorRequestsResponse> OperatorRequestsAsync(string? principal = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRequestsAsync(principal, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestWrite(OperatorRequestWriteRequest request) => inner.OperatorRequestWriteAsync(request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> OperatorRequestWriteAsync(OperatorRequestWriteRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRequestWriteAsync(request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestCarry(string ask, OperatorRequestCarryRequest request) => inner.OperatorRequestCarryAsync(ask, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> OperatorRequestCarryAsync(string ask, OperatorRequestCarryRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRequestCarryAsync(ask, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDone(string id, OperatorRequestCloseRequest request) => inner.OperatorRequestDoneAsync(id, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> OperatorRequestDoneAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRequestDoneAsync(id, request, cancellationToken: token), cancellationToken);

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDismiss(string id, OperatorRequestCloseRequest request) => inner.OperatorRequestDismissAsync(id, request, cancellationToken: cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<WriteOutcome> OperatorRequestDismissAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorRequestDismissAsync(id, request, cancellationToken: token), cancellationToken);

}
