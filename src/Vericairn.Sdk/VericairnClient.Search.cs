// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public SearchResponse Search(SearchRequest request) => Post<SearchRequest, SearchResponse>("/v1/search", request)!;

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<SearchResponse>(nameof(Search), async token => (await PostAsync<SearchRequest, SearchResponse>("/v1/search", request, token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(Search)), cancellationToken);

    /// <inheritdoc />
    public SearchIndexResponse SearchIndex(SearchIndexRequest request) => Post<SearchIndexRequest, SearchIndexResponse>("/v1/search/index", request)!;

    /// <inheritdoc />
    public Task<SearchIndexResponse> SearchIndexAsync(SearchIndexRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<SearchIndexResponse>(nameof(SearchIndex), async token => (await PostAsync<SearchIndexRequest, SearchIndexResponse>("/v1/search/index", request, token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(SearchIndex)), cancellationToken);

    /// <inheritdoc />
    public SearchReplayResponse SearchReplay(SearchReplayRequest request) => Post<SearchReplayRequest, SearchReplayResponse>("/v1/search/replay", request)!;

    /// <inheritdoc />
    public Task<SearchReplayResponse> SearchReplayAsync(SearchReplayRequest request, CancellationToken cancellationToken = default) =>
        RunOperationAsync<SearchReplayResponse>(nameof(SearchReplay), async token => (await PostAsync<SearchReplayRequest, SearchReplayResponse>("/v1/search/replay", request, token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(SearchReplay)), cancellationToken);

    // Without searches or a principal, a package or brief is the same read as before, on the route every runtime serves.
    /// <inheritdoc />
    public ContextResponse Context(ContextRequest request) => request is { Search: not { Count: > 0 }, Principal: null }
        ? Context(request.WorkOrder, request.MaxBytes)
        : Post<ContextRequest, ContextResponse>("/v1/context/prepare", request)!;

    /// <inheritdoc />
    public Task<ContextResponse> ContextAsync(ContextRequest request, CancellationToken cancellationToken = default) => request is { Search: not { Count: > 0 }, Principal: null }
        ? ContextAsync(request.WorkOrder, request.MaxBytes, cancellationToken)
        : RunOperationAsync<ContextResponse>(nameof(Context), async token => (await PostAsync<ContextRequest, ContextResponse>("/v1/context/prepare", request, token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(Context)), cancellationToken);

    /// <inheritdoc />
    public BriefResponse? BriefContext(BriefContextRequest request)
    {
        if (request is { Search: not { Count: > 0 }, Principal: null })
            return BriefContext(request.WorkOrder, request.MaxBytes, request.Recipient, request.Assignment, request.View);
        using HttpResponseMessage response = http.PostAsJsonAsync(new Uri("/v1/context/brief", UriKind.Relative), request, ProtocolJson.Options).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // An empty 404 means no such work order, unless the daemon lacks the route, which the route check reports.
            CheckLookupRouteAsync("/v1/context/brief", response, CancellationToken.None).GetAwaiter().GetResult();
            return null;
        }

        EnsureSuccess(response);
        return response.Content.ReadFromJsonAsync<BriefResponse>(ProtocolJson.Options).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public Task<BriefResponse?> BriefContextAsync(BriefContextRequest request, CancellationToken cancellationToken = default)
    {
        if (request is { Search: not { Count: > 0 }, Principal: null })
            return BriefContextAsync(request.WorkOrder, request.MaxBytes, request.Recipient, request.Assignment, request.View, cancellationToken);
        return RunOperationAsync<BriefResponse?>(nameof(BriefContext), async token =>
        {
            using HttpResponseMessage response = await asyncHttp.PostAsJsonAsync(new Uri("/v1/context/brief", UriKind.Relative), request, ProtocolJson.Options, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await CheckLookupRouteAsync("/v1/context/brief", response, token).ConfigureAwait(false);
                return null;
            }

            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<BriefResponse>(ProtocolJson.Options, token).ConfigureAwait(false);
        }, false, OperationTimeout?.Invoke(nameof(BriefContext)), cancellationToken);
    }
}
