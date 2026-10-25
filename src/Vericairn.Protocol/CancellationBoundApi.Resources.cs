// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public ResourcesResponse Resources() => inner.ResourcesAsync(cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public ResourceResponse ResourceRead(ResourceRequest request) => inner.ResourceReadAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<ResourcesResponse> ResourcesAsync(CancellationToken cancellationToken = default) => ForwardAsync(inner.ResourcesAsync, cancellationToken);
    /// <inheritdoc />
    public Task<ResourceResponse> ResourceReadAsync(ResourceRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.ResourceReadAsync(request, token), cancellationToken);
}
