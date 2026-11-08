// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public EventsResponse Events(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null) =>
        inner.EventsAsync(after, filter, principal, wait, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<EventsResponse> EventsAsync(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.EventsAsync(after, filter, principal, wait, token), cancellationToken);

    /// <inheritdoc />
    public EventSelectResponse EventsSelect(EventSelectRequest request) => inner.EventsSelectAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<EventSelectResponse> EventsSelectAsync(EventSelectRequest request, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.EventsSelectAsync(request, token), cancellationToken);

    /// <inheritdoc />
    public EventEnvelope? EventGet(string key, string? principal = null) => inner.EventGetAsync(key, principal, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<EventEnvelope?> EventGetAsync(string key, string? principal = null, CancellationToken cancellationToken = default) =>
        ForwardAsync(token => inner.EventGetAsync(key, principal, token), cancellationToken);
}
