// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public CompanionsResponse Companions() => CompanionsAsync().GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionResponse CompanionRender(CompanionRequest request) => CompanionRenderAsync(request).GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionResponse CompanionSources(CompanionRequest request) => CompanionSourcesAsync(request).GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionComparison CompanionCompare(CompanionCompareRequest request) => CompanionCompareAsync(request).GetAwaiter().GetResult();

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
        RunOperationAsync(nameof(CompanionCompare), async token => (await GetAsync<CompanionComparison>("/v1/companions/compare" + CompanionQuery(new Dictionary<string, string?>
            { ["selection"] = request.Selection, ["commit"] = request.Commit, ["audience"] = request.Audience }), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(CompanionCompare)), cancellationToken);

    private static string CompanionPath(string operation, CompanionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return "/v1/companions/" + operation + CompanionQuery(new Dictionary<string, string?>
        {
            ["generator"] = request.Generator, ["selection"] = request.Selection, ["revision"] = request.Revision, ["snapshot"] = request.Snapshot,
            ["page"] = request.Page.ToString(CultureInfo.InvariantCulture), ["field"] = request.Field, ["index"] = request.Index,
            ["audience"] = request.Audience, ["contract"] = request.Contract.ToString(CultureInfo.InvariantCulture),
            ["declaration"] = request.Declaration, ["commit"] = request.Commit,
        });
    }

    private static string CompanionQuery(Dictionary<string, string?> values) => "?" + string.Join('&', values.Where(p => p.Value is not null)
        .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!)));
}
