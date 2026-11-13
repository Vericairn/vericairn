// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public (FirstAuthorityResponse? FirstAuthority, Problem? Problem) FirstAuthority(FirstAuthorityRequest request) =>
        inner.FirstAuthorityAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<(FirstAuthorityResponse? FirstAuthority, Problem? Problem)> FirstAuthorityAsync(FirstAuthorityRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.FirstAuthorityAsync(request, token), cancellationToken);
}
