// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Events;

/// <summary>What one read of a subscription delivered: the events, a refusal that closes it, a reset, or the journal's close.</summary>
/// <param name="Events">The events the reader may read that its filter admits, oldest first.</param>
/// <param name="Refusal">Why the reader may no longer read events; the stream closes and nothing is delivered.</param>
/// <param name="Reset">True when the reader fell behind what the journal keeps and must re-read state before continuing.</param>
/// <param name="Closed">True when the journal closed: the runtime is stopping, and the reader ends and reconnects later.</param>
public sealed record EventBatch(IReadOnlyList<EventEnvelope> Events, string? Refusal = null, bool Reset = false, bool Closed = false)
{
    /// <summary>
    /// From 0.9.2 (MCP-05): for each delivered event, in order, the indices of the selectors it passes among those the read was given;
    /// empty when the read was given none. A selector is judged only on events the audience check already admitted.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Selected { get; init; } = [];
}

/// <summary>
/// From 0.9.2 (MCP-05): a filter validated and rooted once, which a subscription's reads judge each delivered event by, so one reader
/// learns which of several narrower interests (an MCP session's subscribed resources) each of its events also concerns. It passes
/// only events appended after it was opened, and only through ids its reader may read, as a subscription's own filter does.
/// </summary>
public sealed class EventSelector
{
    internal EventSelector(EventMatcher matcher, long since)
    {
        Matcher = matcher;
        Since = since;
    }

    internal EventMatcher Matcher { get; }

    /// <summary>The journal's head when it was opened: it passes later events only.</summary>
    internal long Since { get; }
}

/// <summary>
/// One reader's position in the journal, for a stream or a long poll. Each read passes every entry through the audience check and
/// the reader's filter, and moves past withheld entries without a trace: nothing it returns, no count, cursor or error, tells a
/// withheld entry from none. The cursor it seals reveals no position.
/// </summary>
public sealed class EventSubscription
{
    private readonly EstateService service;
    private readonly EventJournal journal;
    private readonly string? device;
    private readonly string? devicePublicKey;
    private readonly string? profile;
    private readonly EventMatcher matcher;
    private long position;

    internal EventSubscription(EstateService service, EventJournal journal, string principal, string? device, string? devicePublicKey, string? profile,
        EventMatcher matcher, long position, bool reset)
    {
        this.service = service;
        this.journal = journal;
        this.device = device;
        this.devicePublicKey = devicePublicKey;
        this.profile = profile;
        this.matcher = matcher;
        this.position = position;
        Principal = principal;
        Reset = reset;
    }

    /// <summary>The principal this subscription reads for.</summary>
    public string Principal { get; }

    /// <summary>True when the cursor it was opened with could not be honored: the reader re-reads state and continues from <see cref="Cursor"/>.</summary>
    public bool Reset { get; }

    /// <summary>The position after everything read so far, sealed for the reader, with a fresh nonce each time.</summary>
    public string Cursor => journal.Seal(Interlocked.Read(ref position), Principal);

    /// <summary>Whether its filter admits a kind at all: what the pump derives for listening readers.</summary>
    internal bool Admits(string kind) => matcher.Admits(kind);

    /// <summary>Why the reader may no longer read events now, or null.</summary>
    public string? Refusal() => service.EventRefusal(Principal, device, devicePublicKey, profile);

    /// <summary>Completes once the journal holds an entry after this position, which may or may not be one the reader may read.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => journal.WaitAsync(Interlocked.Read(ref position), cancellationToken);

    /// <summary>Keeps this subscription among the listeners the pump derives for, until disposed.</summary>
    public IDisposable Listen() => service.ListenEvents(this);

    /// <summary>
    /// Reads at most <paramref name="max"/> entries after this position. The reader is judged after the entries are read, so no entry a
    /// pass appended after it published a revocation can pass. A reader that fell behind the bound gets reset, at the head.
    /// </summary>
    public EventBatch Read(int max = 256) => Read(max, []);

    /// <summary>
    /// Reads as <see cref="Read(int)"/> does and says, for each event delivered, which of <paramref name="selectors"/> it passes
    /// (<see cref="EventBatch.Selected"/>). Selectors only narrow what this subscription delivers: an event its audience check or its
    /// own filter withholds passes none, and nothing tells it from no event.
    /// </summary>
    public EventBatch Read(int max, IReadOnlyList<EventSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        if (journal.Closed)
        {
            return new EventBatch([], Closed: true);
        }

        long from = Interlocked.Read(ref position);
        IReadOnlyList<JournalEntry> entries = journal.After(from, max);
        if (entries.Count > 0 ? entries[0].Position != from + 1 : !journal.Holds(from))
        {
            Interlocked.Exchange(ref position, journal.Head);
            return new EventBatch([], Reset: true);
        }

        if (Refusal() is string refusal)
        {
            return new EventBatch([], refusal);
        }

        var delivered = new List<EventEnvelope>();
        var selected = new List<IReadOnlyList<int>>();
        foreach (JournalEntry entry in entries)
        {
            Interlocked.Exchange(ref position, entry.Position);
            if (service.EventReadable(Principal, entry) && matcher.Matches(entry, Principal, service))
            {
                delivered.Add(EstateService.Envelope(journal, entry, Principal));
                if (selectors.Count > 0)
                {
                    var passed = new List<int>();
                    for (int i = 0; i < selectors.Count; i++)
                    {
                        if (entry.Position > selectors[i].Since && selectors[i].Matcher.Matches(entry, Principal, service))
                        {
                            passed.Add(i);
                        }
                    }

                    selected.Add(passed);
                }
            }
        }

        return new EventBatch(delivered) { Selected = selectors.Count > 0 ? selected : [] };
    }
}

/// <summary>A validated filter: every non-empty dimension narrows, and a subjects match must itself be readable by the reader.</summary>
internal sealed class EventMatcher
{
    private readonly HashSet<string>? kinds;
    private readonly HashSet<string>? types;
    private readonly HashSet<string>? subjects;
    private readonly HashSet<string>? to;
    private readonly HashSet<string>? categories;

    private EventMatcher(HashSet<string>? kinds, HashSet<string>? types, HashSet<string>? subjects, HashSet<string>? to, HashSet<string>? categories)
    {
        this.kinds = kinds;
        this.types = types;
        this.subjects = subjects;
        this.to = to;
        this.categories = categories;
    }

    /// <summary>Validates a filter: known kinds and categories, at most 32 values a dimension, each a short identifier. Null when valid.</summary>
    public static Problem? Create(EventFilter? filter, out EventMatcher? matcher)
    {
        matcher = null;
        filter ??= new EventFilter();
        var problems = new List<string>();
        HashSet<string>? Set(string name, IReadOnlyList<string>? values, IReadOnlyCollection<string>? known, int length)
        {
            if (values is null || values.Count == 0)
            {
                return null;
            }

            if (values.Count > EventVocabulary.MaxFilterValues)
            {
                problems.Add($"{name} names at most {EventVocabulary.MaxFilterValues} values.");
                return null;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrEmpty(value) || value.Length > length || !value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or ':' or '_' or '.'))
                {
                    problems.Add($"{name} values are identifiers of at most {length} letters, digits, hyphens, colons, underscores and dots.");
                    return null;
                }

                if (known is not null && !known.Contains(value))
                {
                    problems.Add($"{name} '{value}' is not one of {string.Join(", ", known)}.");
                    return null;
                }

                set.Add(value);
            }

            return set;
        }

        var matched = new EventMatcher(
            Set("kind", filter.Kinds, EventVocabulary.Kinds, 32),
            Set("type", filter.Types, null, 64),
            Set("subject", filter.Subjects, null, 128),
            Set("to", filter.To, null, 128),
            Set("category", filter.Categories, EventVocabulary.Categories, 32));
        if (problems.Count > 0)
        {
            return new Problem("invalid", string.Join(" ", problems), []);
        }

        matcher = matched;
        return null;
    }

    /// <summary>Whether the filter admits a kind at all.</summary>
    public bool Admits(string kind) => kinds is null || kinds.Contains(kind);

    /// <summary>The same filter with each subject named by any revision's id also named by its first revision, as events relate them.</summary>
    public EventMatcher Rooted(EstateModel model)
    {
        if (subjects is null)
        {
            return this;
        }

        var rooted = new HashSet<string>(subjects, StringComparer.Ordinal);
        foreach (string subject in subjects)
        {
            if (model.ById.TryGetValue(subject, out RecordEnvelope? record))
            {
                rooted.Add(model.Chain(record)[0]);
            }
        }

        return new EventMatcher(kinds, types, rooted, to, categories);
    }

    /// <summary>Whether an entry passes the filter; a subjects match counts only through an id the reader may read.</summary>
    public bool Matches(JournalEntry entry, string principal, EstateService service) =>
        (kinds is null || kinds.Contains(entry.Kind))
        && (types is null || types.Contains(entry.Subject.Type))
        && (to is null || entry.To is not null && to.Contains(entry.To))
        && (categories is null || entry.Category is not null && categories.Contains(entry.Category))
        && (subjects is null || entry.Related.Any(id => subjects.Contains(id) && service.CanReadEventReference(principal, new EventReference("record", id))));
}
