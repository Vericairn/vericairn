// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public PresencePromptsResponse PresencePrompts(PresencePromptsRequest request) => Post<PresencePromptsRequest, PresencePromptsResponse>("/v1/presence/prompts", request)!;

    /// <inheritdoc />
    public Task<PresencePromptsResponse> PresencePromptsAsync(PresencePromptsRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(PresencePrompts), async token => (await PostAsync<PresencePromptsRequest, PresencePromptsResponse>("/v1/presence/prompts", request, cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(PresencePrompts)), cancellationToken);
}
