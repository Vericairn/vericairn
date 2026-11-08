// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// A network MCP session's events (MCP-05): the event journal read for the session's device, its principal and the profile it came
/// over, with the audience check of <c>GET /v1/events</c> at delivery. The session's server stream reads it while open, from where the
/// session stands: the journal's head at its first subscription, then wherever its last stream ended, so a client that reconnects
/// within the journal's seven days misses nothing. Each subscription is a selector over the same reads, passing only what was observed
/// after it was made. A device no longer admitted with its key and profile, or a principal no longer known, reads nothing more.
/// </summary>
internal sealed class McpSessionEvents(EstateService service, string device, string publicKey, string profile, string principal) : IMcpEventSource
{
    private static readonly EventFilter Followed = new(Kinds: McpEvents.FollowedKinds);

    private readonly object gate = new();
    private readonly Dictionary<string, EventSelector> selectors = new(StringComparer.Ordinal);
    private string? cursor;
    private EventSubscription? open;

    /// <inheritdoc />
    public Task<bool> AvailableAsync(CancellationToken cancellationToken) => Task.FromResult(service.EventsEnabled);

    /// <inheritdoc />
    public Task AddAsync(string key, EventFilter filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // The session's position starts at its first subscription when no stream holds it yet, so a stream opened after it hears
        // what changed in between. It is taken before the selector, so nothing the selector passes lies before it.
        bool positioned;
        lock (gate)
        {
            positioned = cursor is not null || open is not null;
        }

        string? head = positioned ? null : Head();
        (EventSelector? selector, Problem? problem) = service.OpenEventSelector(filter);
        if (selector is null)
        {
            throw problem?.Code == "invalid" ? new ArgumentException(problem.Message, nameof(filter)) : new InvalidOperationException(problem?.Message ?? "The event journal is unavailable.");
        }

        lock (gate)
        {
            selectors[key] = selector;
            if (cursor is null && open is null)
            {
                cursor = head;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        lock (gate)
        {
            selectors.Remove(key);
        }
    }

    /// <summary>
    /// Opens the session's stream subscription where the session stands, bound to its device, key and profile; a refusal says why it
    /// may not read events now. Its <see cref="EventSubscription.Reset"/> is true when that position could not be honored.
    /// </summary>
    public (EventSubscription? Subscription, Problem? Problem) Open()
    {
        string? from;
        lock (gate)
        {
            from = cursor;
        }

        (EventSubscription? subscription, Problem? problem) = service.OpenEvents(from, Followed, principal, device, publicKey, profile);
        if (subscription is not null)
        {
            lock (gate)
            {
                open = subscription;
            }
        }

        return (subscription, problem);
    }

    /// <summary>Keeps where a closing stream stood, for the session's next stream.</summary>
    public void Close(EventSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (gate)
        {
            if (ReferenceEquals(open, subscription))
            {
                cursor = subscription.Cursor;
                open = null;
            }
        }
    }

    /// <summary>
    /// Reads the open stream's next entries with every subscription's selector: the events the audience check admits, each with the
    /// subscriptions it concerns; the batch's refusal or the journal's close is the caller's to act on.
    /// </summary>
    public (EventBatch Batch, McpEventRound Round) Read(EventSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        string[] keys;
        EventSelector[] chosen;
        lock (gate)
        {
            keys = [.. selectors.Keys];
            chosen = [.. keys.Select(k => selectors[k])];
        }

        EventBatch batch = subscription.Read(256, chosen);
        if (batch.Closed || batch.Refusal is not null)
        {
            return (batch, new McpEventRound([], Refusal: batch.Refusal ?? "The runtime is stopping."));
        }

        var events = new List<McpEvent>(batch.Events.Count);
        for (int i = 0; i < batch.Events.Count; i++)
        {
            IReadOnlyList<int> passed = batch.Selected.Count > i ? batch.Selected[i] : [];
            events.Add(new McpEvent(batch.Events[i], [.. passed.Select(index => keys[index])]));
        }

        return (batch, new McpEventRound(events, batch.Reset));
    }

    /// <inheritdoc />
    public Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        EventSubscription? subscription;
        lock (gate)
        {
            subscription = open;
        }

        // The server stream waits on the journal itself; without one open there is nothing to read.
        return Task.FromResult(subscription is null ? McpEventRound.Empty : Read(subscription).Round);
    }

    private string Head()
    {
        (EventSubscription? at, Problem? problem) = service.OpenEvents(null, Followed, principal, device, publicKey, profile);
        return at?.Cursor ?? throw new InvalidOperationException(problem?.Message ?? "The event journal is unavailable.");
    }
}
