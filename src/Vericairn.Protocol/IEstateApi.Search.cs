// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Search the machine-local keyword index; candidates are data, and the selection records the search. Writes no record.</summary>
    SearchResponse Search(SearchRequest request) =>
        new(string.Empty, null, [], false, false, null, new Problem("unsupported", "This API implementation does not support search.", []));

    /// <summary>The cancellable counterpart of <see cref="Search"/>.</summary>
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Search), () => Search(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Bring the machine-local index up to date, or rebuild it. Writes no record.</summary>
    SearchIndexResponse SearchIndex(SearchIndexRequest request) =>
        new(string.Empty, string.Empty, null, false, [], null, null, 0, new Problem("unsupported", "This API implementation does not support search.", []));

    /// <summary>The cancellable counterpart of <see cref="SearchIndex"/>.</summary>
    Task<SearchIndexResponse> SearchIndexAsync(SearchIndexRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(SearchIndex), () => SearchIndex(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Replay recorded selections on exactly their basis, or say why that is not possible here. Never approximates.</summary>
    SearchReplayResponse SearchReplay(SearchReplayRequest request) =>
        new(string.Empty, [], new Problem("unsupported", "This API implementation does not support search.", []));

    /// <summary>The cancellable counterpart of <see cref="SearchReplay"/>.</summary>
    Task<SearchReplayResponse> SearchReplayAsync(SearchReplayRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(SearchReplay), () => SearchReplay(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>A context package with optional searches; without searches it is exactly <see cref="Context(string, long?)"/>.</summary>
    ContextResponse Context(ContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Search is not { Count: > 0 } && request.Principal is null
            ? Context(request.WorkOrder, request.MaxBytes)
            : new ContextResponse("This API implementation does not support search-selected context.\n", null, new Problem("unsupported", "This API implementation does not support search-selected context.", []));
    }

    /// <summary>The cancellable counterpart of <see cref="Context(ContextRequest)"/>.</summary>
    Task<ContextResponse> ContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Context), () => Context(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>A measured agent brief with optional searches; without searches it is exactly <see cref="BriefContext(string, long?, string?, string?, CommunicationValues?)"/>.</summary>
    BriefResponse? BriefContext(BriefContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Search is not { Count: > 0 } && request.Principal is null
            ? BriefContext(request.WorkOrder, request.MaxBytes, request.Recipient, request.Assignment, request.View)
            : new BriefResponse("This API implementation does not support search-selected context.\n", default, Problem: new Problem("unsupported", "This API implementation does not support search-selected context.", []));
    }

    /// <summary>The cancellable counterpart of <see cref="BriefContext(BriefContextRequest)"/>.</summary>
    Task<BriefResponse?> BriefContextAsync(BriefContextRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(BriefContext), () => BriefContext(request), false, this as IClientOperationLifetime, cancellationToken);
}
