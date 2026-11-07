// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public EventsResponse Events(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null) =>
        Get<EventsResponse>(EventsPath(after, filter, principal, wait))!;

    /// <inheritdoc />
    public Task<EventsResponse> EventsAsync(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(Events), async token => (await GetAsync<EventsResponse>(EventsPath(after, filter, principal, wait), cancellationToken: token).ConfigureAwait(false))!,
            false, OperationTimeout?.Invoke(nameof(Events)), cancellationToken);

    /// <inheritdoc />
    public EventEnvelope? EventGet(string key, string? principal = null) => Get<EventEnvelope>(EventPath(key, principal), allowNotFound: true);

    /// <inheritdoc />
    public Task<EventEnvelope?> EventGetAsync(string key, string? principal = null, CancellationToken cancellationToken = default) =>
        RunOperationAsync(nameof(EventGet), token => GetAsync<EventEnvelope>(EventPath(key, principal), allowNotFound: true, cancellationToken: token),
            false, OperationTimeout?.Invoke(nameof(EventGet)), cancellationToken);

    /// <summary>
    /// Follows one server-sent event stream from a cursor (the journal's head when null) until the runtime ends it or the caller
    /// cancels: each event, each reset (re-read state, then continue from its cursor), and each cursor-only message, the opening one
    /// included, whose cursor is where to resume. A device-signed stream ends after 60 minutes; <see cref="WatchEventsAsync"/> reconnects.
    /// An event is a hint and never authority: read its subject through the API before acting.
    /// </summary>
    public async IAsyncEnumerable<EventStreamItem> StreamEventsAsync(string? after = null, EventFilter? filter = null, string? principal = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The cursor goes in the query, which the request signature covers; Last-Event-ID is for event sources that cannot sign.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(EventsPath(after, filter, principal, null), UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventVocabulary.StreamMediaType));
        using HttpResponseMessage response = await asyncHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            using var reader = new StreamReader(body, Encoding.UTF8);
            string? id = null;
            string? name = null;
            var data = new StringBuilder();
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                if (line.Length > 0)
                {
                    if (line[0] == ':')
                    {
                        continue;
                    }

                    int colon = line.IndexOf(':', StringComparison.Ordinal);
                    string field = colon < 0 ? line : line[..colon];
                    string value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');
                    switch (field)
                    {
                        case "id":
                            id = value;
                            break;
                        case "event":
                            name = value;
                            break;
                        case "data":
                            data.Append(value).Append('\n');
                            break;
                    }

                    continue;
                }

                EventStreamItem? item = Dispatch(id, name, data);
                name = null;
                data.Clear();
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    /// <summary>
    /// Follows the event stream across reconnections, resuming from the newest cursor seen: when the runtime ends a stream (after 60
    /// minutes for a device, or when it stops) and when the connection drops, waiting a little longer after each failure. A refusal (a
    /// device no longer admitted, a principal it may not read for, a malformed filter) ends the watch with its exception. Delivery is at
    /// least once: after a reconnection an event can come again.
    /// </summary>
    public async IAsyncEnumerable<EventStreamItem> WatchEventsAsync(string? after = null, EventFilter? filter = null, string? principal = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = after;
        int failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            IAsyncEnumerator<EventStreamItem> stream = StreamEventsAsync(cursor, filter, principal, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = await stream.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Transient(exception))
                    {
                        failures++;
                        break;
                    }

                    if (!more)
                    {
                        failures = 0;
                        break;
                    }

                    failures = 0;
                    cursor = stream.Current.Cursor;
                    yield return stream.Current;
                }
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, failures == 0 ? 0.2 : Math.Pow(2, Math.Min(failures, 5)))), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Transient(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound } => false,
        HttpRequestException or IOException or ClientUnavailableException or ClientTimeoutException => true,
        _ => false,
    };

    private static EventStreamItem? Dispatch(string? id, string? name, StringBuilder data)
    {
        if (name == "reset")
        {
            return id is null ? null : new EventStreamItem(id, Reset: true);
        }

        if (data.Length == 0)
        {
            return id is null ? null : new EventStreamItem(id);
        }

        EventEnvelope? envelope = JsonSerializer.Deserialize<EventEnvelope>(data.ToString(0, data.Length - 1), ProtocolJson.Options);
        return envelope is null ? null : new EventStreamItem(envelope.Cursor, envelope);
    }

    private static string EventsPath(string? after, EventFilter? filter, string? principal, int? wait)
    {
        var query = new List<string>();
        void Add(string name, string? value)
        {
            if (value is not null)
            {
                query.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value));
            }
        }

        Add("after", after);
        Add("principal", principal);
        Add("wait", wait?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach ((string name, IReadOnlyList<string>? values) in new[] { ("kind", filter?.Kinds), ("type", filter?.Types), ("subject", filter?.Subjects), ("to", filter?.To), ("category", filter?.Categories) })
        {
            foreach (string value in values ?? [])
            {
                Add(name, value);
            }
        }

        return "/v1/events" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query));
    }

    private static string EventPath(string key, string? principal) =>
        "/v1/events/" + Uri.EscapeDataString(key) + (principal is null ? string.Empty : "?principal=" + Uri.EscapeDataString(principal));
}
