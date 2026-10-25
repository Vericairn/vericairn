// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Prepare a candidate's review brief entirely from records; write and launch nothing.</summary>
    ReleaseReviewBriefResponse ReleaseReviewBrief(string version, string? product = null, string? decision = null) =>
        new("", Problem: new("unsupported", "Release review briefs are unavailable.", []));

    /// <summary>Recheck the prepared brief hash and assign its exact content to the recorded Reviewer.</summary>
    ReleaseReviewBriefResponse ReleaseReviewAssign(string version, ReleaseReviewAssignRequest request) =>
        new("", Problem: new("unsupported", "Release review assignments are unavailable.", []));

    /// <summary>The cancellable read bridge for release review preparation.</summary>
    Task<ReleaseReviewBriefResponse> ReleaseReviewBriefAsync(string version, string? product = null, string? decision = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ReleaseReviewBrief), () => ReleaseReviewBrief(version, product, decision), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable write bridge for assigning a prepared release review.</summary>
    Task<ReleaseReviewBriefResponse> ReleaseReviewAssignAsync(string version, ReleaseReviewAssignRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ReleaseReviewAssign), () => ReleaseReviewAssign(version, request), true, this as IClientOperationLifetime, cancellationToken);
}
