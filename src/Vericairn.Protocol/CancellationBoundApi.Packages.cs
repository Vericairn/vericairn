// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public PackagesResponse PackagesObserve(PackagesObserveRequest request) => inner.PackagesObserveAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesObserveAsync(PackagesObserveRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PackagesObserveAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public PackagesResponse PackagesUnlist(PackagesUnlistRequest request) => inner.PackagesUnlistAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesUnlistAsync(PackagesUnlistRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PackagesUnlistAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public PackagesResponse PackagesDeprecate(PackagesDeprecateRequest request) => inner.PackagesDeprecateAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesDeprecateAsync(PackagesDeprecateRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.PackagesDeprecateAsync(request, token), cancellationToken);
}
