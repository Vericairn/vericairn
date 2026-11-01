// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public AttentionResponse AttentionGet(string? principal = null) => Get<AttentionResponse>(AttentionPath(principal))!;
    /// <inheritdoc />
    public AttentionAcknowledgeResponse AttentionAcknowledge(AttentionAcknowledgeRequest request) =>
        Post<AttentionAcknowledgeRequest, AttentionAcknowledgeResponse>("/v1/attention/acknowledge", request)!;
    /// <inheritdoc />
    public Task<AttentionResponse> AttentionGetAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(AttentionGet), async token => (await GetAsync<AttentionResponse>(AttentionPath(principal), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(AttentionGet)), cancellationToken);
    /// <inheritdoc />
    public Task<AttentionAcknowledgeResponse> AttentionAcknowledgeAsync(AttentionAcknowledgeRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(AttentionAcknowledge), async token => (await PostAsync<AttentionAcknowledgeRequest, AttentionAcknowledgeResponse>("/v1/attention/acknowledge", request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(AttentionAcknowledge)), cancellationToken);

    private static string AttentionPath(string? principal) => "/v1/attention" + (principal is null ? "" : "?principal=" + Uri.EscapeDataString(principal));
}
