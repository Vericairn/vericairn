// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>List current resources under estate read access. This operation writes nothing.</summary>
    ResourcesResponse Resources() => new(null, [], new("unsupported", "This runtime does not offer resources.", []));

    /// <summary>Read exact resource bytes through the matching read operation, rechecking access.</summary>
    ResourceResponse ResourceRead(ResourceRequest request) => new(null, null, null, Problem: new("unsupported", "This runtime does not offer resources.", []));

    /// <summary>The cancellable resource catalogue read.</summary>
    Task<ResourcesResponse> ResourcesAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Resources), Resources, false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable resource read.</summary>
    Task<ResourceResponse> ResourceReadAsync(ResourceRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(ResourceRead), () => ResourceRead(request), false, this as IClientOperationLifetime, cancellationToken);
}
