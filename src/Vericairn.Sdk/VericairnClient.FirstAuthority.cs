// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private const string FirstAuthorityPath = "/v1/estate/first-authority";

    /// <inheritdoc />
    public (FirstAuthorityResponse? FirstAuthority, Problem? Problem) FirstAuthority(FirstAuthorityRequest request) =>
        PostOrProblem<FirstAuthorityRequest, FirstAuthorityResponse>(FirstAuthorityPath, request);

    /// <inheritdoc />
    public Task<(FirstAuthorityResponse? FirstAuthority, Problem? Problem)> FirstAuthorityAsync(FirstAuthorityRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<(FirstAuthorityResponse? FirstAuthority, Problem? Problem)>(nameof(FirstAuthority),
            async token => await PostOrProblemAsync<FirstAuthorityRequest, FirstAuthorityResponse>(FirstAuthorityPath, request, cancellationToken: token).ConfigureAwait(false),
            request is { Check: false } && (request.Device is not null || request.Admission is not null), OperationTimeout?.Invoke(nameof(FirstAuthority)), cancellationToken);
}
