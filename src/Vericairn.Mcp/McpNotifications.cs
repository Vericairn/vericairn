// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

/// <summary>An event a session's source delivered, with the subscribed resources (by their URIs) it concerns.</summary>
/// <param name="Envelope">The envelope, which the source delivered only after the audience check admitted it for the session's principal.</param>
/// <param name="Subscriptions">The URIs of the session's subscriptions whose filters it passes.</param>
public sealed record McpEvent(EventEnvelope Envelope, IReadOnlyCollection<string> Subscriptions);

/// <summary>
/// One round of a session's events (MCP-05): what its principal may read since the round before, a reset when the journal could not
/// honor where the session stood, or a refusal that ends delivery. A withheld event is in no round, and nothing in a round tells one
/// from none.
/// </summary>
/// <param name="Events">The events delivered, oldest first.</param>
/// <param name="Reset">True when the session's position could not be honored: every subscribed resource may have changed.</param>
/// <param name="Refusal">Why the session may no longer read events; nothing is delivered after it.</param>
public sealed record McpEventRound(IReadOnlyList<McpEvent> Events, bool Reset = false, string? Refusal = null)
{
    /// <summary>A round that delivered nothing.</summary>
    public static McpEventRound Empty { get; } = new([]);
}

/// <summary>
/// Where an MCP session's events come from (MCP-05): the event journal, read for the session's principal with the audience check of
/// <c>GET /v1/events</c> at delivery, followed from a cursor of the session's own. Over standard input and output it is the estate
/// API; over Streamable HTTP, the daemon's journal read for the session's device. A source never reads records to find a change.
/// </summary>
public interface IMcpEventSource
{
    /// <summary>Whether this connection's runtime keeps an event journal it may read: the daemon does, an embedded runtime does not.</summary>
    Task<bool> AvailableAsync(CancellationToken cancellationToken);

    /// <summary>Keeps a subscription's filter under its URI, from now on: what was observed before it is not its change.</summary>
    Task AddAsync(string key, EventFilter filter, CancellationToken cancellationToken);

    /// <summary>Forgets a subscription's filter.</summary>
    void Remove(string key);

    /// <summary>
    /// Reads the session's next round, waiting at most <paramref name="wait"/> for an event its principal may read; a withheld event
    /// neither ends the wait nor appears.
    /// </summary>
    Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken cancellationToken);
}

/// <summary>The vocabulary MCP-05 adds: the notifications, what a session follows, and the operator queue it never carries.</summary>
public static class McpEvents
{
    /// <summary>The notification a subscribed resource's change yields.</summary>
    public const string UpdatedMethod = "notifications/resources/updated";

    /// <summary>The notification a write yields, since it changes what resources/list returns.</summary>
    public const string ListChangedMethod = "notifications/resources/list_changed";

    /// <summary>The kinds of event a session's resources follow: records, asks and flow steps.</summary>
    public static ImmutableArray<string> FollowedKinds { get; } = ["record", "ask", "step"];

    /// <summary>The record types of the operator queue: their events never reach MCP (design/mcp-network.md § 8).</summary>
    public static IReadOnlySet<string> OperatorRequestTypes { get; } = new[] { "operator-request", "operator-request-close" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether an event is an operator-request event, which never reaches MCP on any path: a request event, or one whose subject or any
    /// source is an operator request or its closing, such as an operator request's record or the attention it raises.
    /// </summary>
    public static bool OperatorRequest(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Kind == "request" || OperatorRequestTypes.Contains(envelope.Subject.Type) || envelope.Sources.Any(s => OperatorRequestTypes.Contains(s.Type));
    }

    /// <summary><c>notifications/resources/updated</c> for a URI, exactly as the client subscribed to it.</summary>
    public static JsonObject Updated(string uri) => new()
    {
        ["jsonrpc"] = "2.0", ["method"] = UpdatedMethod, ["params"] = new JsonObject { ["uri"] = uri },
    };

    /// <summary><c>notifications/resources/list_changed</c>.</summary>
    public static JsonObject ListChanged() => new() { ["jsonrpc"] = "2.0", ["method"] = ListChangedMethod };

    /// <summary>Whether an envelope passes a filter that names no subjects, judged as the runtime judges it.</summary>
    internal static bool Passes(EventFilter filter, EventEnvelope envelope) =>
        (filter.Kinds is not { Count: > 0 } kinds || kinds.Contains(envelope.Kind, StringComparer.Ordinal))
        && (filter.Types is not { Count: > 0 } types || types.Contains(envelope.Subject.Type, StringComparer.Ordinal))
        && (filter.To is not { Count: > 0 } to || envelope.To is not null && to.Contains(envelope.To, StringComparer.Ordinal))
        && (filter.Categories is not { Count: > 0 } categories || envelope.Category is not null && categories.Contains(envelope.Category, StringComparer.Ordinal));

    /// <summary>Whether a failure of a read is one to retry later, rather than a fault of this process.</summary>
    internal static bool Transient(Exception exception) =>
        exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException or JsonException or OperationCanceledException or IClientOperationFailure;
}

/// <summary>
/// An MCP session's resource subscriptions (MCP-05, design/events-and-presence.md § 7): which URIs it subscribed to, each with the
/// event filter its resource's changes pass, and the notifications its events yield. A subscribed resource yields
/// <c>notifications/resources/updated</c> at most once a second per URI, and writes yield <c>notifications/resources/list_changed</c>
/// at most every five seconds; a change inside either interval is sent when it ends, once. Everything is derived from the rounds its
/// source reads from the event journal, never by reading records, and an operator-request event yields nothing. The transport sends
/// what it returns: over standard input and output as lines between responses (<see cref="FollowAsync"/>), over Streamable HTTP on
/// the session's server stream, which drives it.
/// </summary>
public sealed class McpResourceWatch
{
    /// <summary>The most resources one session subscribes to.</summary>
    public const int MaxSubscriptions = EventVocabulary.MaxFilterValues;

    private readonly IMcpEventSource source;
    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private TaskCompletionSource subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool listPending;
    private DateTimeOffset? listSent;
    private bool? declared;

    /// <summary>A session's subscriptions over its event source; <paramref name="time"/> spaces the notifications.</summary>
    public McpResourceWatch(IMcpEventSource source, TimeProvider? time = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>The least time between two updates of one URI.</summary>
    public static TimeSpan UpdateInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>The least time between two list changes.</summary>
    public static TimeSpan ListInterval { get; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a follower's read waits for an event before it reads again.</summary>
    private static TimeSpan FollowWait { get; } = TimeSpan.FromSeconds(EventVocabulary.DefaultWaitSeconds);

    /// <summary>The URIs subscribed to now.</summary>
    public IReadOnlyList<string> Subscribed
    {
        get { lock (gate) return [.. entries.Keys.Order(StringComparer.Ordinal)]; }
    }

    /// <summary>
    /// Whether this connection can subscribe: its runtime keeps an event journal it may read. The first answer holds for the session,
    /// so what initialization declared is what <c>resources/subscribe</c> later finds.
    /// </summary>
    public async Task<bool> AvailableAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (declared is bool known)
            {
                return known;
            }
        }

        bool answer = await source.AvailableAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            declared ??= answer;
            return declared.Value;
        }
    }

    /// <summary>
    /// Subscribes to a URI whose changes pass <paramref name="filter"/>; subscribing again to a URI already subscribed changes nothing.
    /// A session holds at most <see cref="MaxSubscriptions"/>.
    /// </summary>
    public async Task SubscribeAsync(string uri, EventFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        ArgumentNullException.ThrowIfNull(filter);
        lock (gate)
        {
            if (entries.ContainsKey(uri))
            {
                return;
            }

            if (entries.Count >= MaxSubscriptions)
            {
                throw new McpResourceException(-32602, $"A session subscribes to at most {MaxSubscriptions} resources; unsubscribe from one first.");
            }

            entries[uri] = new Entry();
        }

        try
        {
            await source.AddAsync(uri, filter, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (gate)
            {
                entries.Remove(uri);
            }

            throw;
        }

        lock (gate)
        {
            if (entries.ContainsKey(uri))
            {
                subscribed.TrySetResult();
            }
        }
    }

    /// <summary>Ends a subscription; true when the URI was subscribed. No notification for it follows.</summary>
    public bool Unsubscribe(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        bool removed;
        lock (gate)
        {
            removed = entries.Remove(uri);
            if (entries.Count == 0 && subscribed.Task.IsCompleted)
            {
                subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        source.Remove(uri);
        return removed;
    }

    /// <summary>
    /// Takes in one round and returns the notifications due now: an update for each subscribed URI an event concerns (or every URI, on a
    /// reset) and a list change for any record event, each once its interval since its last has passed; the rest wait for
    /// <see cref="Due"/>. A refusal yields nothing, and an operator-request event concerns nothing.
    /// </summary>
    public IReadOnlyList<JsonObject> Apply(McpEventRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        lock (gate)
        {
            if (round.Refusal is not null)
            {
                return [];
            }

            if (round.Reset)
            {
                listPending = true;
                foreach (Entry entry in entries.Values)
                {
                    entry.Pending = true;
                }
            }

            foreach (McpEvent item in round.Events)
            {
                if (McpEvents.OperatorRequest(item.Envelope))
                {
                    continue;
                }

                listPending |= item.Envelope.Kind == "record";
                foreach (string uri in item.Subscriptions)
                {
                    if (entries.TryGetValue(uri, out Entry? entry))
                    {
                        entry.Pending = true;
                    }
                }
            }

            return DueLocked(time.GetUtcNow());
        }
    }

    /// <summary>The notifications that waited for their interval and are due now.</summary>
    public IReadOnlyList<JsonObject> Due()
    {
        lock (gate)
        {
            return DueLocked(time.GetUtcNow());
        }
    }

    /// <summary>How long until a waiting notification is due; null when none waits.</summary>
    public TimeSpan? NextDue()
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset? next = null;
            foreach (Entry entry in entries.Values.Where(e => e.Pending))
            {
                next = Earliest(next, entry.Sent is DateTimeOffset sent ? sent + UpdateInterval : now);
            }

            if (listPending)
            {
                next = Earliest(next, listSent is DateTimeOffset sent ? sent + ListInterval : now);
            }

            return next is DateTimeOffset due ? (due > now ? due - now : TimeSpan.Zero) : null;
        }
    }

    /// <summary>
    /// Follows the session's events and sends their notifications until cancelled, for a transport that writes them between its
    /// responses (standard input and output). It reads only while the session holds a subscription, which only a connection whose
    /// runtime keeps a journal can make, so an idle session holds none of the runtime's stream slots; a failed read is retried later,
    /// and a refusal ends following.
    /// </summary>
    public async Task FollowAsync(Func<JsonObject, Task> send, TextWriter? log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        int failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            Task idle;
            lock (gate)
            {
                idle = entries.Count > 0 ? Task.CompletedTask : subscribed.Task;
            }

            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
            long started = time.GetTimestamp();
            Task<McpEventRound> reading = source.ReadAsync(FollowWait, cancellationToken);
            while (!reading.IsCompleted)
            {
                using var pause = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task waited = NextDue() is TimeSpan due ? Task.WhenAny(reading, Task.Delay(due, time, pause.Token)) : Task.WhenAny(reading);
                await waited.ConfigureAwait(false);
                await pause.CancelAsync().ConfigureAwait(false);
                foreach (JsonObject notification in Due())
                {
                    await send(notification).ConfigureAwait(false);
                }
            }

            McpEventRound round;
            try
            {
                round = await reading.ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && McpEvents.Transient(exception))
            {
                failures++;
                if (log is not null)
                {
                    string why = exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ? "the runtime is busy"
                        : ClientFailure.FromException(exception)?.Message ?? exception.Message;
                    await log.WriteLineAsync($"vericairn mcp: events could not be read now ({why}); retrying").ConfigureAwait(false);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures, 5)))), time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (round.Refusal is not null)
            {
                if (log is not null)
                {
                    await log.WriteLineAsync("vericairn mcp: events stopped: " + round.Refusal).ConfigureAwait(false);
                }

                return;
            }

            foreach (JsonObject notification in Apply(round))
            {
                await send(notification).ConfigureAwait(false);
            }

            // A runtime that answers without waiting is read at most once a second, never in a tight loop.
            if (round.Events.Count == 0 && !round.Reset && time.GetElapsedTime(started) < UpdateInterval)
            {
                await Task.Delay(UpdateInterval, time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private List<JsonObject> DueLocked(DateTimeOffset now)
    {
        var due = new List<JsonObject>();
        foreach ((string uri, Entry entry) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (entry.Pending && (entry.Sent is not DateTimeOffset sent || now - sent >= UpdateInterval))
            {
                entry.Pending = false;
                entry.Sent = now;
                due.Add(McpEvents.Updated(uri));
            }
        }

        if (listPending && (listSent is not DateTimeOffset listed || now - listed >= ListInterval))
        {
            listPending = false;
            listSent = now;
            due.Add(McpEvents.ListChanged());
        }

        return due;
    }

    private static DateTimeOffset Earliest(DateTimeOffset? one, DateTimeOffset other) => one is DateTimeOffset value && value < other ? value : other;

    private sealed class Entry
    {
        public bool Pending { get; set; }

        public DateTimeOffset? Sent { get; set; }
    }
}

/// <summary>
/// A session's events through the estate API (MCP-05), for the server over standard input and output: one selecting long poll of
/// <c>POST /v1/events/select</c> for the session's principal follows the journal from the session's own cursor, and the runtime says
/// for each event which subscriptions it concerns, judging each through what the principal may read. On the local socket these
/// reads take the MCP servers' own slots, never the command line's, and a session beyond them waits its turn. The cursors are the
/// runtime's, sealed for the principal; the operating-system user reads for the acting principal, or the operator when the session
/// acts for none.
/// </summary>
public sealed class McpApiEventSource : IMcpEventSource
{
    private static readonly EventFilter Followed = new(Kinds: McpEvents.FollowedKinds);

    private readonly IEstateApi api;
    private readonly string? principal;
    private readonly object gate = new();
    private readonly Dictionary<string, EventSelection> interests = new(StringComparer.Ordinal);
    private string? cursor;
    private bool? available;

    /// <summary>A source reading through an API for a principal, or for the API's default reader when null.</summary>
    public McpApiEventSource(IEstateApi api, string? principal)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.principal = string.IsNullOrWhiteSpace(principal) ? null : principal;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read on the local socket that does not wait holds no slot, so it is not refused for being busy. A busy runtime (429) and one
    /// briefly unable to read its journal or stopping (503) keep a journal all the same, and are retried; a runtime that keeps none
    /// (the problem <c>no-journal</c>, 501 from a daemon), one without the selecting read, or a reader it refuses, declares none.
    /// </remarks>
    public async Task<bool> AvailableAsync(CancellationToken cancellationToken)
    {
        if (available is bool known)
        {
            return known;
        }

        try
        {
            EventSelectResponse probe = await api.EventsSelectAsync(new EventSelectRequest(Filter: Followed, Principal: principal, Wait: 0), cancellationToken).ConfigureAwait(false);
            available = probe.Problem is null;
        }
        catch (NotSupportedException)
        {
            available = false;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            // Busy or briefly unable to derive: the daemon keeps the journal all the same, and following retries.
            available = true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && McpEvents.Transient(exception))
        {
            available = false;
        }

        return available.Value;
    }

    /// <inheritdoc />
    public async Task AddAsync(string key, EventFilter filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(filter);

        // A read at the journal's head that does not wait checks the filter and the reader, and names where the subscription starts.
        EventSelectResponse opened = await api.EventsSelectAsync(new EventSelectRequest(Filter: Followed, Selections: [new EventSelection(filter)], Principal: principal, Wait: 0),
            cancellationToken).ConfigureAwait(false);
        if (opened.Problem is { } problem)
        {
            throw new McpResourceException(-32603, problem.Message);
        }

        lock (gate)
        {
            interests[key] = new EventSelection(filter, opened.Cursor);
            cursor ??= opened.Cursor;
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        lock (gate)
        {
            interests.Remove(key);
            if (interests.Count == 0)
            {
                cursor = null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        string? from;
        string[] keys;
        EventSelection[] selections;
        lock (gate)
        {
            from = cursor;
            keys = [.. interests.Keys];
            selections = [.. keys.Select(k => interests[k])];
        }

        int seconds = (int)Math.Clamp(Math.Floor(wait.TotalSeconds), 0, EventVocabulary.MaxWaitSeconds);
        EventSelectResponse response = await api.EventsSelectAsync(new EventSelectRequest(from, Followed, selections, principal, seconds), cancellationToken).ConfigureAwait(false);
        if (response.Problem is { Code: "unavailable" } unavailable)
        {
            // The journal cannot be read now: the follower tries again later.
            throw new InvalidOperationException(unavailable.Message);
        }

        if (response.Problem is { } problem)
        {
            return new McpEventRound([], Refusal: problem.Message);
        }

        if (response.Events is null || response.Selected is null || string.IsNullOrEmpty(response.Cursor))
        {
            throw new InvalidOperationException("The runtime's answer to a selecting read named no events or cursor.");
        }

        lock (gate)
        {
            cursor = interests.Count > 0 ? response.Cursor : null;
        }

        if (response.Reset)
        {
            return new McpEventRound([], Reset: true);
        }

        var events = new List<McpEvent>(response.Events.Count);
        for (int i = 0; i < response.Events.Count; i++)
        {
            IReadOnlyList<int> passed = response.Selected.Count > i ? response.Selected[i] : [];
            events.Add(new McpEvent(response.Events[i], [.. passed.Where(index => index >= 0 && index < keys.Length).Select(index => keys[index])]));
        }

        return new McpEventRound(events);
    }
}
