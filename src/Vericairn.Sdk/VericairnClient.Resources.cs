// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public ResourcesResponse Resources() => Get<ResourcesResponse>("/v1/resources")!;
    /// <inheritdoc />
    public ResourceResponse ResourceRead(ResourceRequest request) => Post<ResourceRequest, ResourceResponse>("/v1/resources/read", request)!;
    /// <inheritdoc />
    public Task<ResourcesResponse> ResourcesAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync<ResourcesResponse>(nameof(Resources), async token => (await GetAsync<ResourcesResponse>("/v1/resources", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Resources)), cancellationToken);
    /// <inheritdoc />
    public Task<ResourceResponse> ResourceReadAsync(ResourceRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<ResourceResponse>(nameof(ResourceRead), async token => (await PostAsync<ResourceRequest, ResourceResponse>("/v1/resources/read", request, cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(ResourceRead)), cancellationToken);
}
