// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public OperatorActListResponse OperatorActItems(string? principal = null) => inner.OperatorActItemsAsync(principal, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorActListResponse> OperatorActItemsAsync(string? principal = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorActItemsAsync(principal, token), cancellationToken);

    /// <inheritdoc />
    public OperatorActResponse OperatorAct(OperatorActRequest request) => inner.OperatorActAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorActResponse> OperatorActAsync(OperatorActRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorActAsync(request, token), cancellationToken);
}
