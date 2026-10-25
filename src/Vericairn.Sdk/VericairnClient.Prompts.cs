// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public PromptsResponse Prompts(PromptQuery query) => Get<PromptsResponse>(PromptPath(query))!;
    /// <inheritdoc />
    public WriteOutcome PromptSave(PromptSaveRequest request) => Post<PromptSaveRequest, WriteOutcome>("/v1/prompts", request)!;
    /// <inheritdoc />
    public WriteOutcome PromptPreference(PromptPreferenceRequest request) => Post<PromptPreferenceRequest, WriteOutcome>("/v1/prompts/preferences", request)!;
    /// <inheritdoc />
    public WriteOutcome PromptCopy(PromptCopyRequest request) => Post<PromptCopyRequest, WriteOutcome>("/v1/prompts/copy", request)!;
    /// <inheritdoc />
    public PromptUseResponse PromptUse(PromptUseRequest request) => Post<PromptUseRequest, PromptUseResponse>("/v1/prompts/use", request)!;
    /// <inheritdoc />
    public Task<PromptsResponse> PromptsAsync(PromptQuery query, CancellationToken cancellationToken = default) =>
        RunOperationAsync<PromptsResponse>(nameof(Prompts), async token => (await GetAsync<PromptsResponse>(PromptPath(query), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Prompts)), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptSaveAsync(PromptSaveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(PromptSave), async token => (await PostAsync<PromptSaveRequest, WriteOutcome>("/v1/prompts", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PromptSave)), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptPreferenceAsync(PromptPreferenceRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(PromptPreference), async token => (await PostAsync<PromptPreferenceRequest, WriteOutcome>("/v1/prompts/preferences", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PromptPreference)), cancellationToken);
    /// <inheritdoc />
    public Task<WriteOutcome> PromptCopyAsync(PromptCopyRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<WriteOutcome>(nameof(PromptCopy), async token => (await PostAsync<PromptCopyRequest, WriteOutcome>("/v1/prompts/copy", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PromptCopy)), cancellationToken);
    /// <inheritdoc />
    public Task<PromptUseResponse> PromptUseAsync(PromptUseRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<PromptUseResponse>(nameof(PromptUse), async token => (await PostAsync<PromptUseRequest, PromptUseResponse>("/v1/prompts/use", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PromptUse)), cancellationToken);

    // A missing query is the unfiltered one, as an empty query string is to the route.
    private static string PromptPath(PromptQuery? query)
    {
        query ??= new PromptQuery();
        var values = new Dictionary<string, string?> { ["person"] = query.Person, ["category"] = query.Category, ["role"] = query.Role,
            ["favorites"] = query.Favorites ? "true" : "false", ["includeUnavailable"] = query.IncludeUnavailable ? "true" : "false" };
        return "/v1/prompts?" + string.Join('&', values.Where(p => p.Value is not null).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!)));
    }
}
