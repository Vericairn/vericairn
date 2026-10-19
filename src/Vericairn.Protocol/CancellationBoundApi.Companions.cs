// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public CompanionsResponse Companions() => inner.CompanionsAsync(cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionResponse CompanionRender(CompanionRequest request) => inner.CompanionRenderAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionResponse CompanionSources(CompanionRequest request) => inner.CompanionSourcesAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public CompanionComparison CompanionCompare(CompanionCompareRequest request) => inner.CompanionCompareAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<CompanionsResponse> CompanionsAsync(CancellationToken cancellationToken = default) => ForwardAsync(inner.CompanionsAsync, cancellationToken);
    /// <inheritdoc />
    public Task<CompanionResponse> CompanionRenderAsync(CompanionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CompanionRenderAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<CompanionResponse> CompanionSourcesAsync(CompanionRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CompanionSourcesAsync(request, token), cancellationToken);
    /// <inheritdoc />
    public Task<CompanionComparison> CompanionCompareAsync(CompanionCompareRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.CompanionCompareAsync(request, token), cancellationToken);
}
