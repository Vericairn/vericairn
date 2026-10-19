// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Installed coverage, declared destinations and the current authorized record snapshot.</summary>
    CompanionsResponse Companions() => throw new NotSupportedException("Companion reads are unavailable.");
    /// <summary>Render one explicit permitted selection; reads never grant authority.</summary>
    CompanionResponse CompanionRender(CompanionRequest request) => throw new NotSupportedException("Companion reads are unavailable.");
    /// <summary>Inspect the exact selected source basis without generating.</summary>
    CompanionResponse CompanionSources(CompanionRequest request) => throw new NotSupportedException("Companion reads are unavailable.");
    /// <summary>Compare every generated member and byte without repairing or clearing authored review.</summary>
    CompanionComparison CompanionCompare(CompanionCompareRequest request) => throw new NotSupportedException("Companion reads are unavailable.");

    /// <summary>The cancellable coverage read.</summary>
    Task<CompanionsResponse> CompanionsAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Companions), Companions, false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable rendition read.</summary>
    Task<CompanionResponse> CompanionRenderAsync(CompanionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(CompanionRender), () => CompanionRender(request), false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable source-detail read.</summary>
    Task<CompanionResponse> CompanionSourcesAsync(CompanionRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(CompanionSources), () => CompanionSources(request), false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable fresh comparison.</summary>
    Task<CompanionComparison> CompanionCompareAsync(CompanionCompareRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(CompanionCompare), () => CompanionCompare(request), false, this as IClientOperationLifetime, cancellationToken);
}
