// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public PromptsResponse Prompts(PromptQuery query) => inner.PromptsAsync(query, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public WriteOutcome PromptSave(PromptSaveRequest request) => inner.PromptSaveAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public WriteOutcome PromptPreference(PromptPreferenceRequest request) => inner.PromptPreferenceAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public WriteOutcome PromptCopy(PromptCopyRequest request) => inner.PromptCopyAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public PromptUseResponse PromptUse(PromptUseRequest request) => inner.PromptUseAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<PromptsResponse> PromptsAsync(PromptQuery query, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PromptsAsync(query, token), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptSaveAsync(PromptSaveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PromptSaveAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptPreferenceAsync(PromptPreferenceRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PromptPreferenceAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptCopyAsync(PromptCopyRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PromptCopyAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<PromptUseResponse> PromptUseAsync(PromptUseRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PromptUseAsync(request, token), cancellationToken);
}
