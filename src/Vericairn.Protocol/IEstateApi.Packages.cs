// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Record public registry state for every declared package or an original account-holder intent.</summary>
    PackagesResponse PackagesObserve(PackagesObserveRequest request);

    /// <summary>Record intent and return the account holder's unlisting step; never change the registry.</summary>
    PackagesResponse PackagesUnlist(PackagesUnlistRequest request);

    /// <summary>Record exact deprecation intent and return the account holder's step; never change the registry.</summary>
    PackagesResponse PackagesDeprecate(PackagesDeprecateRequest request);

    /// <summary>The cancellable package observation. A stopped client does not undo stored observations.</summary>
    Task<PackagesResponse> PackagesObserveAsync(PackagesObserveRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PackagesObserve), () => PackagesObserve(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable intent submission; reconcile its identity if the receipt is interrupted.</summary>
    Task<PackagesResponse> PackagesUnlistAsync(PackagesUnlistRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PackagesUnlist), () => PackagesUnlist(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable intent submission; reconcile its identity if the receipt is interrupted.</summary>
    Task<PackagesResponse> PackagesDeprecateAsync(PackagesDeprecateRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PackagesDeprecate), () => PackagesDeprecate(request), true, this as IClientOperationLifetime, cancellationToken);
}
