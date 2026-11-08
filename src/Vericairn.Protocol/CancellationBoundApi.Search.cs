// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public SearchResponse Search(SearchRequest request) => inner.SearchAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.SearchAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public SearchIndexResponse SearchIndex(SearchIndexRequest request) => inner.SearchIndexAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<SearchIndexResponse> SearchIndexAsync(SearchIndexRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.SearchIndexAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public SearchReplayResponse SearchReplay(SearchReplayRequest request) => inner.SearchReplayAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<SearchReplayResponse> SearchReplayAsync(SearchReplayRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.SearchReplayAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public ContextResponse Context(ContextRequest request) => inner.ContextAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ContextResponse> ContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.ContextAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public BriefResponse? BriefContext(BriefContextRequest request) => inner.BriefContextAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<BriefResponse?> BriefContextAsync(BriefContextRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.BriefContextAsync(request, token), cancellationToken);
}
