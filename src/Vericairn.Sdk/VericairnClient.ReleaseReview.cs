// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewBrief(string version, string? product = null, string? decision = null) =>
        Get<ReleaseReviewBriefResponse>(ReviewBriefPath(version, product, decision))!;

    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewAssign(string version, ReleaseReviewAssignRequest request) =>
        Post<ReleaseReviewAssignRequest, ReleaseReviewBriefResponse>(ReviewBriefPath(version), request)!;

    /// <inheritdoc />
    public Task<ReleaseReviewBriefResponse> ReleaseReviewBriefAsync(string version, string? product = null, string? decision = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ReleaseReviewBriefResponse>(nameof(ReleaseReviewBrief), async token =>
            (await GetAsync<ReleaseReviewBriefResponse>(ReviewBriefPath(version, product, decision), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(ReleaseReviewBrief)), cancellationToken);

    /// <inheritdoc />
    public Task<ReleaseReviewBriefResponse> ReleaseReviewAssignAsync(string version, ReleaseReviewAssignRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ReleaseReviewBriefResponse>(nameof(ReleaseReviewAssign), async token =>
            (await PostAsync<ReleaseReviewAssignRequest, ReleaseReviewBriefResponse>(ReviewBriefPath(version), request, cancellationToken: token).ConfigureAwait(false))!,
            true, OperationTimeout?.Invoke(nameof(ReleaseReviewAssign)), cancellationToken);

    private static string ReviewBriefPath(string version, string? product = null, string? decision = null)
    {
        var query = new List<string>();
        if (product is not null) query.Add("product=" + Uri.EscapeDataString(product));
        if (decision is not null) query.Add("decision=" + Uri.EscapeDataString(decision));
        return "/v1/releases/" + Uri.EscapeDataString(version) + "/review-brief" + (query.Count == 0 ? "" : "?" + string.Join('&', query));
    }
}
