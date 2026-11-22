// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public OperatorAnswerCandidatesResponse OperatorAnswerCandidates(string? principal = null) => inner.OperatorAnswerCandidatesAsync(principal, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorAnswerCandidatesResponse> OperatorAnswerCandidatesAsync(string? principal = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorAnswerCandidatesAsync(principal, token), cancellationToken);

    /// <inheritdoc />
    public OperatorAnswerResponse OperatorAnswer(OperatorAnswerRequest request) => inner.OperatorAnswerAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<OperatorAnswerResponse> OperatorAnswerAsync(OperatorAnswerRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.OperatorAnswerAsync(request, token), cancellationToken);
}
