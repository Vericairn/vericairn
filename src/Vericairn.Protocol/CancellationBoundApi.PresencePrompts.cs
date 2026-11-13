// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public PresencePromptsResponse PresencePrompts(PresencePromptsRequest request) => inner.PresencePromptsAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresencePromptsResponse> PresencePromptsAsync(PresencePromptsRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.PresencePromptsAsync(request, token), cancellationToken);
}
