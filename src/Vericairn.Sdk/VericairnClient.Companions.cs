// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public CompanionsResponse Companions() => Get<CompanionsResponse>("/v1/companions")!;
    /// <inheritdoc />
    public CompanionResponse CompanionRender(CompanionRequest request) => Get<CompanionResponse>(CompanionPath("render", request))!;
    /// <inheritdoc />
    public CompanionResponse CompanionSources(CompanionRequest request) => Get<CompanionResponse>(CompanionPath("sources", request))!;
    /// <inheritdoc />
    public CompanionComparison CompanionCompare(CompanionCompareRequest request) => Get<CompanionComparison>(ComparePath(request))!;

    /// <inheritdoc />
    public Task<CompanionsResponse> CompanionsAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(Companions), async token => (await GetAsync<CompanionsResponse>("/v1/companions", cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(Companions)), cancellationToken);

    /// <inheritdoc />
    public Task<CompanionResponse> CompanionRenderAsync(CompanionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(CompanionRender), async token => (await GetAsync<CompanionResponse>(CompanionPath("render", request), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(CompanionRender)), cancellationToken);

    /// <inheritdoc />
    public Task<CompanionResponse> CompanionSourcesAsync(CompanionRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(CompanionSources), async token => (await GetAsync<CompanionResponse>(CompanionPath("sources", request), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(CompanionSources)), cancellationToken);

    /// <inheritdoc />
    public Task<CompanionComparison> CompanionCompareAsync(CompanionCompareRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(CompanionCompare), async token => (await GetAsync<CompanionComparison>(ComparePath(request), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(CompanionCompare)), cancellationToken);

    // As for every request the client sends, the runtime validates the request; a missing one reaches it as an empty query.
    private static string ComparePath(CompanionCompareRequest? request) =>
        "/v1/companions/compare" + CompanionQuery(new Dictionary<string, string?> { ["selection"] = request?.Selection, ["commit"] = request?.Commit, ["audience"] = request?.Audience });

    private static string CompanionPath(string operation, CompanionRequest? request) => "/v1/companions/" + operation + CompanionQuery(new Dictionary<string, string?>
    {
        ["generator"] = request?.Generator, ["selection"] = request?.Selection, ["revision"] = request?.Revision, ["snapshot"] = request?.Snapshot,
        ["page"] = request?.Page.ToString(CultureInfo.InvariantCulture), ["field"] = request?.Field, ["index"] = request?.Index,
        ["audience"] = request?.Audience, ["contract"] = request?.Contract.ToString(CultureInfo.InvariantCulture),
        ["declaration"] = request?.Declaration, ["commit"] = request?.Commit,
    });

    private static string CompanionQuery(Dictionary<string, string?> values) => "?" + string.Join('&', values.Where(p => p.Value is not null)
        .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!)));
}
