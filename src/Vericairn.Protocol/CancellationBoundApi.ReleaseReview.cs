// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewBrief(string version, string? product = null, string? decision = null) =>
        inner.ReleaseReviewBriefAsync(version, product, decision, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewAssign(string version, ReleaseReviewAssignRequest request) =>
        inner.ReleaseReviewAssignAsync(version, request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ReleaseReviewBriefResponse> ReleaseReviewBriefAsync(string version, string? product = null, string? decision = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.ReleaseReviewBriefAsync(version, product, decision, token), cancellationToken);

    /// <inheritdoc />
    public Task<ReleaseReviewBriefResponse> ReleaseReviewAssignAsync(string version, ReleaseReviewAssignRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.ReleaseReviewAssignAsync(version, request, token), cancellationToken);
}
