// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public PackagesResponse PackagesObserve(PackagesObserveRequest request) => Post<PackagesObserveRequest, PackagesResponse>("/v1/packages/observe", request)!;
    /// <inheritdoc />
    public PackagesResponse PackagesUnlist(PackagesUnlistRequest request) => Post<PackagesUnlistRequest, PackagesResponse>("/v1/packages/unlist", request)!;
    /// <inheritdoc />
    public PackagesResponse PackagesDeprecate(PackagesDeprecateRequest request) => Post<PackagesDeprecateRequest, PackagesResponse>("/v1/packages/deprecate", request)!;
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesObserveAsync(PackagesObserveRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<PackagesResponse>(nameof(PackagesObserve), async token => (await PostAsync<PackagesObserveRequest, PackagesResponse>("/v1/packages/observe", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PackagesObserve)), cancellationToken);
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesUnlistAsync(PackagesUnlistRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<PackagesResponse>(nameof(PackagesUnlist), async token => (await PostAsync<PackagesUnlistRequest, PackagesResponse>("/v1/packages/unlist", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PackagesUnlist)), cancellationToken);
    /// <inheritdoc />
    public Task<PackagesResponse> PackagesDeprecateAsync(PackagesDeprecateRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<PackagesResponse>(nameof(PackagesDeprecate), async token => (await PostAsync<PackagesDeprecateRequest, PackagesResponse>("/v1/packages/deprecate", request, cancellationToken: token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(PackagesDeprecate)), cancellationToken);
}
