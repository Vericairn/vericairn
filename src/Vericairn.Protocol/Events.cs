// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Protocol;

/// <summary>
/// What an event concerns, or one of its sources: a type and an id and, for a record, its hash. A subject that is not a record, such
/// as a finding, is named by an opaque digest, so no path or command reaches an envelope.
/// </summary>
/// <param name="Type">A record type, or the kind of a derived subject (finding, reconciliation, step).</param>
/// <param name="Id">A record id, or an opaque sha256 digest for a subject that is not a record.</param>
/// <param name="Hash">The record's content hash; null for a subject that is not a record.</param>
public sealed record EventReference(string Type, string Id, string? Hash = null);

/// <summary>
/// The envelope <c>vericairn.event/1</c>: the runtime's notice that a record was written or a derived state changed. It is a hint and
/// never authority; it carries identities, hashes, kinds, states and categories only, never a body, title, summary, path, secret or
/// command, and a consumer reads its subject through its own checked read.
/// </summary>
/// <param name="Format">Always <c>vericairn.event/1</c>.</param>
/// <param name="Key">A hash of the kind, subject, exact sources, state and recipient: stable when the same change is derived again.</param>
/// <param name="Cursor">The opaque position after this event, sealed for the reader; resume from it.</param>
/// <param name="Kind">record, attention, ask, step, request, due, run or presence.</param>
/// <param name="Change">written or found for a record; raised, changed, acknowledged or cleared for a derived state.</param>
/// <param name="At">When this runtime observed the change (RFC 3339 UTC).</param>
/// <param name="Subject">What changed.</param>
/// <param name="Sources">At most 16 records the change rests on.</param>
/// <param name="To">The principal concerned, when one is.</param>
/// <param name="Category">One of the six attention kinds, when the change is attention.</param>
public sealed record EventEnvelope(string Format, string Key, string Cursor, string Kind, string Change, string At, EventReference Subject,
    IReadOnlyList<EventReference> Sources, string? To = null, string? Category = null);

/// <summary>
/// What a subscriber narrows its events to. Every non-empty list narrows, and a value in any one of a list's entries matches; empty
/// or null lists admit everything the reader may read. A filter only narrows: it never widens what the audience check admits.
/// </summary>
/// <param name="Kinds">Event kinds.</param>
/// <param name="Types">Subject types, such as record types.</param>
/// <param name="Subjects">Record ids; a goal matches its work orders, steps and runs.</param>
/// <param name="To">Principals concerned.</param>
/// <param name="Categories">Attention categories.</param>
public sealed record EventFilter(IReadOnlyList<string>? Kinds = null, IReadOnlyList<string>? Types = null, IReadOnlyList<string>? Subjects = null,
    IReadOnlyList<string>? To = null, IReadOnlyList<string>? Categories = null);

/// <summary>
/// A long poll's answer: the events after the cursor the reader may read, oldest first, and the cursor to continue from. With reset,
/// the cursor given could not be honored (too old, foreign or from another epoch): the events list is empty, and the reader re-reads
/// the state it follows and continues from the new cursor. A problem means nothing was read.
/// </summary>
/// <param name="Events">The events delivered.</param>
/// <param name="Cursor">The position to continue from, sealed for the reader.</param>
/// <param name="Reset">True when the reader must re-read state before continuing.</param>
/// <param name="Problem">Why nothing was read, such as a runtime that keeps no journal.</param>
public sealed record EventsResponse(IReadOnlyList<EventEnvelope> Events, string Cursor, bool Reset = false, Problem? Problem = null);

/// <summary>
/// From 0.9.2 (MCP-05): one of several narrower interests a follower reads the journal for, such as an MCP session's subscribed
/// resource. It only narrows: an event passes it only when the read delivers that event at all.
/// </summary>
/// <param name="Filter">What the interest follows, as a filter names it.</param>
/// <param name="Since">A cursor the reader holds: only events after it pass. A cursor that cannot be honored bounds nothing.</param>
public sealed record EventSelection(EventFilter Filter, string? Since = null);

/// <summary>
/// From 0.9.2 (MCP-05): a long poll of the journal that also says, for each event delivered, which of several selections it passes,
/// so one read serves a follower with many interests. It answers as <c>GET /v1/events</c> does as a long poll, and never streams.
/// </summary>
/// <param name="After">The cursor to continue from; the journal's head when omitted.</param>
/// <param name="Filter">What the read delivers at all.</param>
/// <param name="Selections">At most 32 narrower interests, each judged on the events delivered.</param>
/// <param name="Principal">On the local socket, the principal read for, by default the operator; a device reads only for its own.</param>
/// <param name="Wait">Seconds to wait for an event, 0 to 25, by default 20.</param>
public sealed record EventSelectRequest(string? After = null, EventFilter? Filter = null, IReadOnlyList<EventSelection>? Selections = null,
    string? Principal = null, int? Wait = null);

/// <summary>
/// From 0.9.2 (MCP-05): what a selecting long poll delivered: the events, oldest first, and for each the indices of the selections it
/// passes; the cursor to continue from; reset when the cursor given could not be honored; or a problem when nothing was read.
/// </summary>
/// <param name="Events">The events delivered.</param>
/// <param name="Selected">For each event, in order, the indices of the selections it passes.</param>
/// <param name="Cursor">The position to continue from, sealed for the reader.</param>
/// <param name="Reset">True when the reader must re-read state before continuing.</param>
/// <param name="Problem">Why nothing was read.</param>
public sealed record EventSelectResponse(IReadOnlyList<EventEnvelope> Events, IReadOnlyList<IReadOnlyList<int>> Selected, string Cursor, bool Reset = false, Problem? Problem = null);

/// <summary>
/// One message of a server-sent event stream as the SDK reads it: an event, a reset, or only a newer cursor to resume from.
/// </summary>
/// <param name="Cursor">The position after this message.</param>
/// <param name="Event">The event, or null for a reset or a cursor-only message.</param>
/// <param name="Reset">True when the reader must re-read state before continuing from the cursor.</param>
public sealed record EventStreamItem(string Cursor, EventEnvelope? Event = null, bool Reset = false);

/// <summary>The vocabulary of the event envelope and its filters.</summary>
public static class EventVocabulary
{
    /// <summary>The envelope's format.</summary>
    public const string Format = "vericairn.event/1";

    /// <summary>The media type of the server-sent event stream.</summary>
    public const string StreamMediaType = "text/event-stream";

    /// <summary>The most sources one envelope carries.</summary>
    public const int MaxSources = 16;

    /// <summary>The longest a long poll waits, in seconds.</summary>
    public const int MaxWaitSeconds = 25;

    /// <summary>The wait of a long poll that names none, in seconds.</summary>
    public const int DefaultWaitSeconds = 20;

    /// <summary>The most values one filter dimension holds.</summary>
    public const int MaxFilterValues = 32;

    /// <summary>From 0.9.2: the most selections one selecting read names.</summary>
    public const int MaxSelections = 32;

    /// <summary>Every kind of event. due and presence are reserved for reminders and presence leases, which later releases raise.</summary>
    public static ImmutableArray<string> Kinds { get; } = ["record", "attention", "ask", "step", "request", "due", "run", "presence"];

    /// <summary>Every change an event names.</summary>
    public static ImmutableArray<string> Changes { get; } = ["written", "found", "raised", "changed", "acknowledged", "cleared"];

    /// <summary>The six attention kinds (cohesion contract § 5), as categories.</summary>
    public static ImmutableArray<string> Categories { get; } = ["decision-needed", "blocked", "uncertain-effect", "failed-check", "incident", "information"];
}

public partial interface IEstateApi
{
    /// <summary>
    /// Reads the events after a cursor that the reading principal may read, narrowed by a filter, without waiting longer than the
    /// wait given (the runtime's daemon waits; an embedded runtime keeps no journal and answers with a problem). Without a cursor the
    /// read starts at the journal's head. A cursor that cannot be honored answers reset. On the local socket the operating-system
    /// user reads for the named principal, by default the operator; a device reads only for its own principal.
    /// </summary>
    EventsResponse Events(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null) =>
        throw new NotSupportedException("The event journal is unavailable.");

    /// <summary>The newest event with a key that the reading principal may read; null alike for an unknown and an unreadable key.</summary>
    EventEnvelope? EventGet(string key, string? principal = null) => throw new NotSupportedException("The event journal is unavailable.");

    /// <summary>The cancellable read bridge of a long poll.</summary>
    Task<EventsResponse> EventsAsync(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Events), () => Events(after, filter, principal, wait), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>
    /// From 0.9.2 (MCP-05): reads the events after a cursor as <see cref="Events"/> does, and says for each which of the request's
    /// selections it passes. The daemon waits; an embedded runtime that keeps a journal reads once without waiting.
    /// </summary>
    EventSelectResponse EventsSelect(EventSelectRequest request) => throw new NotSupportedException("The event journal is unavailable.");

    /// <summary>The cancellable read bridge of a selecting long poll.</summary>
    Task<EventSelectResponse> EventsSelectAsync(EventSelectRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(EventsSelect), () => EventsSelect(request), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable read bridge of one event by key.</summary>
    Task<EventEnvelope?> EventGetAsync(string key, string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(EventGet), () => EventGet(key, principal), false, this as IClientOperationLifetime, cancellationToken);
}
