// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public AttentionResponse AttentionGet(string? principal = null) => inner.AttentionGetAsync(principal, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public AttentionAcknowledgeResponse AttentionAcknowledge(AttentionAcknowledgeRequest request) => inner.AttentionAcknowledgeAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<AttentionResponse> AttentionGetAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.AttentionGetAsync(principal, token), cancellationToken);
    /// <inheritdoc />
    public Task<AttentionAcknowledgeResponse> AttentionAcknowledgeAsync(AttentionAcknowledgeRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.AttentionAcknowledgeAsync(request, token), cancellationToken);
}
