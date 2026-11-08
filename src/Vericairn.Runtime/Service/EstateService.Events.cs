// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Events;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The event journal and its derivation (WATCH-05, design/events-and-presence.md §§ 5 and 6). The daemon enables the journal; an
/// embedded runtime keeps none. A record first observed by this runtime, written through it or found after a pull, raises a record
/// event; each derived projection a reader consumes (attention per principal, asks, flow steps, operator requests, runs) is derived
/// again after writes, at most once a second, and on a clock tick, and compared with its last derivation. An event is a hint and
/// never authority: it carries no content, and a reader reads its subject through its own checked read.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>How long an unused per-principal projection is kept; longer than the journal's retention, so no live cursor outlives it.</summary>
    private static readonly TimeSpan RegistrationLifetime = TimeSpan.FromDays(8);

    private static readonly string[] GlobalProjections = ["ask", "request", "step", "run"];

    private readonly ConcurrentDictionary<string, byte> writtenHere = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<EventSubscription, byte> listening = new();
    private readonly SemaphoreSlim eventWork = new(0, 1);
    private EventJournal? journal;
    private TimeProvider eventClock = TimeProvider.System;
    private EventAudience audience = EventAudience.Empty;

    /// <summary>
    /// A narrower read rule than today's, for tests of the audience check: false withholds every event whose subject, source or matched
    /// subject a principal may not read. Today an admitted principal reads every record, so the runtime sets none.
    /// </summary>
    internal Func<string, EventReference, bool>? EventReadRule { get; set; }

    /// <summary>Told after each derivation pass commits; tests wait on it rather than on time.</summary>
    internal Action? EventsDerived { get; set; }

    /// <summary>A test's hook at the start of each derivation pass, where it injects a fault.</summary>
    internal Action? EventPassStarting { get; set; }

    /// <summary>True when this runtime keeps an event journal: the daemon enables it.</summary>
    public bool EventsEnabled => journal is not null;

    /// <summary>
    /// Keeps the event journal in a directory, the live store's own, which the daemon holds owner-only: at most seven days and at most
    /// <paramref name="capacity"/> events (100,000 by default). A journal that is missing or was not closed cleanly starts a new epoch.
    /// Called once, by the daemon, before it serves; <paramref name="time"/> stamps events and judges their age.
    /// </summary>
    public void EnableEvents(string directory, TimeProvider? time = null, int capacity = 100_000) =>
        EnableEvents(directory, time, capacity, EventJournal.DefaultRetention);

    internal void EnableEvents(string directory, TimeProvider? time, int capacity, TimeSpan retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (gate)
        {
            if (journal is not null || session.IsReadOnly)
            {
                return;
            }

            eventClock = time ?? TimeProvider.System;
            journal = EventJournal.Open(Path.Combine(directory, EventJournal.FileName), eventClock, capacity, retention);
            session.Written = id =>
            {
                writtenHere.TryAdd(id, 0);
                MarkEventsDirty();
            };
            audience = EventAudience.From(session.Load());
        }
    }

    /// <summary>
    /// Derives every consumed projection now and appends what changed: the records first observed, then the derived changes they cause.
    /// A projection is consumed when a listening reader's filter admits its kind, and each principal's attention when one of its
    /// readers admits attention; one no reader consumes keeps its last derivation, which the next derivation compares against, so a
    /// reader that consumes it later still hears every change since. The pump calls this; it holds the estate's gate while it reads.
    /// </summary>
    public void DeriveEvents()
    {
        if (journal is null)
        {
            return;
        }

        EventSubscription[] readers = [.. listening.Keys];
        bool Consumed(string kind) => readers.Any(s => s.Admits(kind));
        lock (gate)
        {
            EstateModel model = session.Load();
            var pass = new EventPass { Asks = Consumed("ask"), Requests = Consumed("request"), Steps = Consumed("step"), Runs = Consumed("run") };
            foreach (EventSubscription subscription in readers.Where(s => s.Admits("attention")))
            {
                pass.Attention.TryAdd(subscription.Principal, null);
            }

            DeriveEventsLocked(model, pass);
        }
    }

    /// <summary>
    /// The derivation pump: after a write it derives within a second, never more than once a second, and on each tick it derives what
    /// time changed (due states, overdue progress) and what was found after a pull; only while a reader listens. A failed pass is told
    /// to the log and tried again on the next signal; it never stops the runtime.
    /// </summary>
    public async Task RunEventDerivationAsync(TimeSpan tick, Action<string>? log, CancellationToken cancellationToken)
    {
        long last = 0;
        TimeSpan took = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // A pass holds the estate's gate, so passes are spaced to hold it at most a fifth of the time, whatever the estate's size.
                await eventWork.WaitAsync(Max(tick, took * 5), cancellationToken).ConfigureAwait(false);
                if (listening.IsEmpty)
                {
                    continue;
                }

                TimeSpan spacing = Max(TimeSpan.FromSeconds(1), took * 4);
                TimeSpan since = Stopwatch.GetElapsedTime(last);
                if (last != 0 && since < spacing)
                {
                    await Task.Delay(spacing - since, cancellationToken).ConfigureAwait(false);
                }

                long started = Stopwatch.GetTimestamp();
                DeriveEvents();
                last = Stopwatch.GetTimestamp();
                took = Stopwatch.GetElapsedTime(started, last);
            }
            catch (Exception exception) when (exception is OperationCanceledException && cancellationToken.IsCancellationRequested
                || exception is ObjectDisposedException && (closeRequested || journal is not { Closed: false }))
            {
                // The runtime or its journal is closing. Anything else disposed is an ordinary failed pass, below.
                break;
            }
            catch (Exception exception) when (!Fatal(exception))
            {
                // Whatever a projection throws, the pump goes on: the next signal or tick tries again, and the daemon still stops cleanly.
                log?.Invoke($"the event journal could not derive what changed: {exception.GetType().Name}: {exception.Message}");
                last = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>
    /// Opens a reader's subscription: the principal it reads for, its filter and where it starts. On the local socket no device is
    /// given and the operating-system user reads for the named principal, by default the operator; a device reads only for its own
    /// principal and must be admitted now with the key that authenticated it (and the profile it came over). Without a cursor the
    /// subscription starts at the journal's head; a cursor this epoch did not seal for this principal, or one the bound no longer
    /// holds, starts it at the head with reset.
    /// </summary>
    public (EventSubscription? Subscription, Problem? Problem) OpenEvents(string? after, EventFilter? filter, string? principal,
        string? device = null, string? devicePublicKey = null, string? profile = null)
    {
        EventJournal? events = journal;
        if (events is null)
        {
            return (null, EventsUnavailable);
        }

        if (EventMatcher.Create(filter, out EventMatcher? matcher) is Problem invalid)
        {
            return (null, invalid);
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if (EventReader(model, ref principal, device, devicePublicKey, profile) is Problem denied)
            {
                return (null, denied);
            }

            matcher = matcher!.Rooted(model);

            // The records first observed are appended before the reader starts, so a resumed reader has every record change at once
            // and a new one starts after them. A new registration's first derivation raises every current item of the principal's
            // attention; for a reader starting at the head those land before it, and for a resumed reader after its cursor.
            var pass = new EventPass();
            if (matcher.Admits("attention") && events.Baseline(AttentionProjection(principal!)) is null)
            {
                pass.Attention[principal!] = null;
            }

            try
            {
                DeriveEventsLocked(model, pass);
            }
            catch (Exception exception) when (!Fatal(exception))
            {
                return (null, new Problem("unavailable", "The event journal cannot be read now: " + exception.Message, []));
            }

            bool reset = false;
            long position;
            if (after is null)
            {
                position = events.Head;
            }
            else if (!events.TryOpen(after, principal!, out position))
            {
                reset = true;
                position = events.Head;
            }

            return (new EventSubscription(this, events, principal!, device, devicePublicKey, profile, matcher, position, reset), null);
        }
    }

    /// <summary>
    /// From 0.9.2 (MCP-05): validates and roots a filter once, for a subscription's reads to judge each event it delivers by
    /// (<see cref="EventSubscription.Read(int, IReadOnlyList{EventSelector})"/>). The records first observed are appended before the
    /// selector is opened, so it passes only what was observed after: an MCP session hears what changed after it subscribed.
    /// </summary>
    public (EventSelector? Selector, Problem? Problem) OpenEventSelector(EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EventJournal? events = journal;
        if (events is null)
        {
            return (null, EventsUnavailable);
        }

        if (EventMatcher.Create(filter, out EventMatcher? matcher) is Problem invalid)
        {
            return (null, invalid);
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            try
            {
                DeriveEventsLocked(model, new EventPass());
            }
            catch (Exception exception) when (!Fatal(exception))
            {
                return (null, new Problem("unavailable", "The event journal cannot be read now: " + exception.Message, []));
            }

            return (new EventSelector(matcher!.Rooted(model), events.Head), null);
        }
    }

    /// <inheritdoc />
    public EventsResponse Events(string? after = null, EventFilter? filter = null, string? principal = null, int? wait = null)
    {
        RequestDeviceScope? request = requestDevice.Value is { Closed: false } scope ? scope : null;
        (EventSubscription? subscription, Problem? problem) = OpenEvents(after, filter, principal, request?.Device, request?.PublicKey, request?.Profile);
        if (subscription is null)
        {
            return new EventsResponse([], string.Empty, Problem: problem);
        }

        if (subscription.Reset)
        {
            return new EventsResponse([], subscription.Cursor, Reset: true);
        }

        EventBatch batch = subscription.Read();
        return batch.Refusal is not null ? new EventsResponse([], string.Empty, Problem: new Problem("denied", batch.Refusal, []))
            : new EventsResponse(batch.Events, subscription.Cursor, batch.Reset);
    }

    /// <inheritdoc />
    public EventEnvelope? EventGet(string key, string? principal = null)
    {
        RequestDeviceScope? request = requestDevice.Value is { Closed: false } scope ? scope : null;
        return EventByKeyFor(key, principal, request?.Device, request?.PublicKey, request?.Profile).Event;
    }

    /// <summary>
    /// The newest event with a key that the reader may read, sealed for it. An unknown key, a key the bound dropped and a key whose
    /// events the reader may not read are answered alike, with neither an event nor a problem; a problem says only that the reader
    /// itself may not read events (a device not admitted now, an unknown principal) or that no journal is kept.
    /// </summary>
    public (EventEnvelope? Event, Problem? Problem) EventByKeyFor(string key, string? principal, string? device = null, string? devicePublicKey = null, string? profile = null)
    {
        EventJournal? events = journal;
        if (events is null)
        {
            return (null, EventsUnavailable);
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if (EventReader(model, ref principal, device, devicePublicKey, profile) is Problem denied)
            {
                return (null, denied);
            }
        }

        if (string.IsNullOrEmpty(key) || key.Length != 71 || !key.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return (null, null);
        }

        foreach (JournalEntry entry in events.WithKey(key, 64))
        {
            if (EventReadable(principal!, entry))
            {
                return (Envelope(events, entry, principal!), null);
            }
        }

        return (null, null);
    }

    /// <summary>Why a reader may no longer read events, judged on the newest audience a pass published; null while it may.</summary>
    internal string? EventRefusal(string principal, string? device, string? devicePublicKey, string? profile)
    {
        EventAudience current = Volatile.Read(ref audience);
        if (!current.Principals.Contains(principal))
        {
            return $"Principal {principal} is not known here now.";
        }

        if (device is null)
        {
            return null;
        }

        if (!current.Devices.TryGetValue(device, out DeviceStanding? standing) || standing.Admission != "admitted")
        {
            return $"Device {device} is not admitted now; its streams are closed.";
        }

        if (!string.Equals(standing.PublicKey, devicePublicKey, StringComparison.Ordinal))
        {
            return $"Device {device} no longer holds the key that opened this stream.";
        }

        if (!string.Equals(standing.Principal, principal, StringComparison.Ordinal))
        {
            return $"Device {device} no longer acts for {principal}.";
        }

        return profile is not null && !standing.Profiles.Contains(profile, StringComparer.Ordinal)
            ? $"Device {device} is no longer permitted the {profile} profile."
            : null;
    }

    /// <summary>
    /// The audience check at delivery: per-principal state reaches only its principal, and the reader must be able to read the subject
    /// and every source under the matching read's rule. Today an admitted principal reads every record; a narrower rule binds here.
    /// </summary>
    internal bool EventReadable(string principal, JournalEntry entry)
    {
        if (entry.Private && !string.Equals(entry.To, principal, StringComparison.Ordinal))
        {
            return false;
        }

        if (!CanReadEventReference(principal, entry.Subject))
        {
            return false;
        }

        foreach (EventReference source in entry.Sources)
        {
            if (!CanReadEventReference(principal, source))
            {
                return false;
            }
        }

        return true;
    }

    internal bool CanReadEventReference(string principal, EventReference reference) => EventReadRule?.Invoke(principal, reference) ?? true;

    internal static EventEnvelope Envelope(EventJournal events, JournalEntry entry, string principal) =>
        new(EventVocabulary.Format, entry.Key, events.Seal(entry.Position, principal), entry.Kind, entry.Change, entry.At, entry.Subject, entry.Sources, entry.To, entry.Category);

    /// <summary>Keeps a subscription among the listeners while a stream or a long poll waits; the pump derives only while one listens.</summary>
    internal IDisposable ListenEvents(EventSubscription subscription)
    {
        listening.TryAdd(subscription, 0);
        MarkEventsDirty();
        return new EventListener(this, subscription);
    }

    /// <summary>How many subscriptions listen now.</summary>
    internal int EventListeners => listening.Count;

    /// <summary>Asks the pump for a pass; it runs within a second while a reader listens.</summary>
    internal void MarkEventsDirty()
    {
        try
        {
            if (eventWork.CurrentCount == 0)
            {
                eventWork.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // A pass is already asked for.
        }
        catch (ObjectDisposedException)
        {
            // The runtime is closing.
        }
    }

    /// <summary>
    /// The cursor a state read returns, after a pass over what that read shows, so a stream from it misses nothing the read did not
    /// show; null when the runtime keeps no journal or the journal cannot be written now. The caller holds the gate.
    /// </summary>
    private string? EventsCursorLocked(EstateModel model, string? principal, EventPass pass)
    {
        if (journal is not { } events)
        {
            return null;
        }

        principal ??= RequestPrincipal(model) ?? (model.CurrentEstate is { } charter ? EstateModel.BodyString(charter, "operator") : null);
        if (principal is null || model.CurrentPrincipal(principal) is null)
        {
            return null;
        }

        principal = model.Chain(model.ById[principal])[0];

        try
        {
            DeriveEventsLocked(model, pass);
            return events.Seal(events.Head, principal);
        }
        catch (Exception exception) when (!Fatal(exception))
        {
            // A journal that cannot derive never fails the read it would complement: the read answers without a cursor.
            return null;
        }
    }

    private static TimeSpan Max(TimeSpan one, TimeSpan other) => one > other ? one : other;

    /// <summary>Failures no catch should hide: the process cannot go on after them.</summary>
    private static bool Fatal(Exception exception) => exception is OutOfMemoryException or StackOverflowException or AccessViolationException or InsufficientExecutionStackException;

    private static readonly Problem EventsUnavailable = new("unavailable", "This runtime keeps no event journal: the daemon keeps it. Start the runtime with 'vericairn runtime start' and read events through it.", []);

    private static string AttentionProjection(string principal) => "attention:" + principal;

    /// <summary>Resolves and checks the principal a reader reads for. The caller holds the gate.</summary>
    private static Problem? EventReader(EstateModel model, ref string? principal, string? device, string? devicePublicKey, string? profile)
    {
        if (device is not null)
        {
            DeviceStanding? standing = Standing(model, device);
            if (standing is null || standing.Admission != "admitted" || !string.Equals(standing.PublicKey, devicePublicKey, StringComparison.Ordinal)
                || profile is not null && !standing.Profiles.Contains(profile, StringComparer.Ordinal))
            {
                return new Problem("denied", $"Device {device} is not admitted now with the key that signed this request.", []);
            }

            if (principal is not null && !string.Equals(principal, standing.Principal, StringComparison.Ordinal))
            {
                return new Problem("denied", "Events belong to the requesting device's principal.", []);
            }

            principal = standing.Principal;
        }

        principal ??= model.CurrentEstate is { } charter ? EstateModel.BodyString(charter, "operator") : null;
        if (principal is null || model.CurrentPrincipal(principal) is null)
        {
            return new Problem("denied", "Events require a known principal.", []);
        }

        // A principal is named by its first record's id, whichever revision's id the reader gave.
        principal = model.Chain(model.ById[principal])[0];
        return null;
    }

    /// <summary>
    /// One derivation pass over a model loaded under the gate: publishes the audience first, so no event it appends can reach a reader
    /// the records no longer admit, then compares each projection the pass covers with its last derivation and appends the changes,
    /// record events before derived ones. The first pass of an epoch only observes, raising nothing. The caller holds the gate.
    /// </summary>
    private void DeriveEventsLocked(EstateModel model, EventPass pass)
    {
        EventPassStarting?.Invoke();
        EventJournal events = journal!;
        Volatile.Write(ref audience, EventAudience.From(model));
        string at = RecordParser.FormatTimestamp(eventClock.GetUtcNow());
        bool initialize = !events.Initialized;
        var relations = new EventRelations(model);
        var appended = new List<PendingEvent>();
        var observedNow = new List<string>();
        foreach (RecordEnvelope record in model.Records.Where(r => !events.Observed(r.Id)).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            observedNow.Add(record.Id);
            if (!initialize)
            {
                appended.Add(RecordEvent(model, record, writtenHere.ContainsKey(record.Id) ? "written" : "found", relations));
            }
        }

        foreach (string id in observedNow)
        {
            writtenHere.TryRemove(id, out _);
        }

        // A write judged invalid after it was stored is discarded under the same gate: its id never reaches a model, so it is forgotten.
        foreach (string id in writtenHere.Keys.Where(id => !model.ById.ContainsKey(id)))
        {
            writtenHere.TryRemove(id, out _);
        }

        var derived = new List<(string Item, PendingEvent Event)>();
        var updates = new List<ProjectionUpdate>();
        void Compare(string projection, Dictionary<string, EventItem> items, IReadOnlyDictionary<string, EventItem>? baseline, bool silent, Func<string, bool>? within = null)
        {
            baseline ??= new Dictionary<string, EventItem>(StringComparer.Ordinal);
            var upserts = new Dictionary<string, EventItem>(StringComparer.Ordinal);
            var removed = new List<string>();
            foreach ((string key, EventItem item) in items)
            {
                if (baseline.TryGetValue(key, out EventItem? before) && before.Fingerprint == item.Fingerprint)
                {
                    continue;
                }

                upserts[key] = item;
                if (!silent)
                {
                    string change = before is null ? "raised" : before.State + ":acknowledged" == item.State ? "acknowledged" : "changed";
                    derived.Add((projection + "\n" + key, new PendingEvent(item.Key(item.State), item.Kind, change, item.Subject, item.Sources, item.To, item.Category, item.Private, item.Related)));
                }
            }

            foreach ((string key, EventItem before) in baseline)
            {
                if ((within is null || within(key)) && !items.ContainsKey(key))
                {
                    removed.Add(key);
                    if (!silent)
                    {
                        derived.Add((projection + "\n" + key, new PendingEvent(before.Key("cleared"), before.Kind, "cleared", before.Subject, before.Sources, before.To, before.Category, before.Private, before.Related)));
                    }
                }
            }

            if (upserts.Count > 0 || removed.Count > 0)
            {
                updates.Add(new ProjectionUpdate(projection, upserts, removed));
            }
        }

        // An epoch's first pass observes every global projection, so that each later derivation has something to compare against.
        bool full = initialize;
        if (full || pass.Asks)
        {
            Compare("ask", AskItems(model, relations), events.Baseline("ask"), initialize);
        }

        if (full || pass.Requests)
        {
            Compare("request", RequestItems(model, relations), events.Baseline("request"), initialize);
        }

        if (full || pass.Steps)
        {
            Compare("step", StepItems(model, relations, null), events.Baseline("step"), initialize);
        }
        else if (pass.StepsOfGoal is string goal)
        {
            string prefix = goal + "/";
            Compare("step", StepItems(model, relations, goal), events.Baseline("step"), initialize, key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        if (full || pass.Runs)
        {
            Compare("run", RunItems(model, relations), events.Baseline("run"), initialize);
        }

        var touched = new List<string>();
        foreach ((string named, AttentionResponse? computed) in pass.Attention)
        {
            if (model.CurrentPrincipal(named) is null)
            {
                continue;
            }

            string principal = model.Chain(model.ById[named])[0];

            string projection = AttentionProjection(principal);
            // A projection registering now starts empty, so its first derivation raises every current item, even in an epoch's first pass.
            Compare(projection, AttentionItems(model, principal, computed ?? AttentionLocked(model, principal), relations), events.Baseline(projection), silent: false);
            touched.Add(projection);
        }

        long now = eventClock.GetUtcNow().ToUnixTimeSeconds();
        var dropped = events.Registrations()
            .Where(r => now - r.Value > (long)RegistrationLifetime.TotalSeconds && !touched.Contains(r.Key, StringComparer.Ordinal)
                && !listening.Keys.Any(s => AttentionProjection(s.Principal) == r.Key))
            .Select(r => r.Key).ToList();

        // Derived changes to one item in one pass collapse to the last; record events never merge. An item is a projection's own
        // identity, so two items on one subject (attention of two kinds about one record, say) stay two events.
        var collapsed = derived.Select((e, index) => (e, index))
            .GroupBy(x => x.e.Item, StringComparer.Ordinal)
            .Select(g => g.Last()).OrderBy(x => x.index).Select(x => x.e.Event);
        appended.AddRange(collapsed);
        events.Commit(at, appended, observedNow, updates, touched, dropped, initialize: !events.Initialized);
        EventsDerived?.Invoke();
    }

    private PendingEvent RecordEvent(EstateModel model, RecordEnvelope record, string change, EventRelations relations)
    {
        var sources = new List<EventReference>();
        if (record.Subject is { } subject)
        {
            sources.Add(Reference(model, subject.Type, subject.Id, subject.Hash));
        }

        if (record.Supersedes is string prior && model.ById.TryGetValue(prior, out RecordEnvelope? previous))
        {
            sources.Add(new EventReference(previous.Type, previous.Id, previous.ComputedHash));
        }

        var item = new EventItem("record", new EventReference(record.Type, record.Id, record.ComputedHash), sources, record.ComputedHash, null, null, false, relations.Of(record));
        return new PendingEvent(item.Key(item.State), "record", change, item.Subject, item.Sources, null, null, false, item.Related);
    }

    private Dictionary<string, EventItem> AttentionItems(EstateModel model, string principal, AttentionResponse attention, EventRelations relations)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
        foreach (AttentionNotice notice in attention.Items)
        {
            EventReference subject = Reference(model, notice.SubjectType, notice.SubjectId, null);
            var sources = new List<EventReference>();
            if (notice.Target is { } target)
            {
                sources.Add(Reference(model, target.Type, target.Id, target.Hash));
            }

            var related = new List<string>(relations.Of(model.ById.GetValueOrDefault(notice.SubjectId)));
            if (notice.Target is { } pointed)
            {
                related.AddRange(relations.Of(model.ById.GetValueOrDefault(pointed.Id)));
            }

            string state = notice.Revision + (notice.AcknowledgedAt is null ? string.Empty : ":acknowledged");
            items[notice.Key] = new EventItem("attention", subject, Bounded(sources), state, principal, Category(notice.Kind), true, related.Distinct(StringComparer.Ordinal).ToList());
        }

        return items;
    }

    private Dictionary<string, EventItem> AskItems(EstateModel model, EventRelations relations)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
        foreach (RecordEnvelope ask in model.Current("ask"))
        {
            var sources = new List<EventReference>();
            if (ask.Subject is { } subject)
            {
                sources.Add(Reference(model, subject.Type, subject.Id, subject.Hash));
            }

            if (EstateModel.BodyString(ask, "goal") is string goal && model.ById.TryGetValue(goal, out RecordEnvelope? goalRecord))
            {
                sources.Add(new EventReference(goalRecord.Type, goalRecord.Id, goalRecord.ComputedHash));
            }

            items[model.Chain(ask)[0]] = new EventItem("ask", new EventReference(ask.Type, ask.Id, ask.ComputedHash), sources, global::Vericairn.Records.Estate.Asks.State(model, ask).State,
                EstateModel.BodyString(ask, "to"), "decision-needed", false, relations.Of(ask));
        }

        return items;
    }

    private Dictionary<string, EventItem> RequestItems(EstateModel model, EventRelations relations)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);

        // A request's state is its delivery as every surface shows it: open while it waits on its operator, and for a run's reserved
        // step settled once the step is done or withdrawn once the step no longer waits on it (design/release-procedure.md § 6).
        global::Vericairn.Records.Estate.OperatorRequests.Waiting queue = global::Vericairn.Records.Estate.OperatorRequests.WaitingOf(model, null, eventClock.GetUtcNow());
        var delivered = queue.Open.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> held = queue.Withdrawn.ToDictionary(h => h.Request.Id, h => h.State, StringComparer.Ordinal);
        foreach (RecordEnvelope request in model.Current("operator-request"))
        {
            var sources = new List<EventReference>();
            if (request.Subject is { } subject)
            {
                sources.Add(Reference(model, subject.Type, subject.Id, subject.Hash));
            }

            string state = global::Vericairn.Records.Estate.OperatorRequests.Closures(model, request).Any() ? "closed"
                : held.TryGetValue(request.Id, out string? undelivered) ? undelivered
                : delivered.Contains(request.Id) ? "open" : "closed";
            items[model.Chain(request)[0]] = new EventItem("request", new EventReference(request.Type, request.Id, request.ComputedHash), sources, state,
                EstateModel.BodyString(request, "to"), state == "open" ? "decision-needed" : "information", false, relations.Of(request));
        }

        return items;
    }

    private Dictionary<string, EventItem> StepItems(EstateModel model, EventRelations relations, string? only)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
        DateTimeOffset at = eventClock.GetUtcNow();
        foreach (RecordEnvelope goal in model.Current("goal"))
        {
            string root = model.Chain(goal)[0];
            if (only is not null && root != only || !FlowRuns.IsRun(goal))
            {
                continue;
            }

            FlowPosition.Position position;
            try
            {
                position = FlowPosition.Derive(model, goal, at);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException or FormatException or ArgumentException)
            {
                continue;
            }

            foreach (FlowPosition.StepInstance step in position.Steps)
            {
                var sources = new List<EventReference> { new(goal.Type, goal.Id, goal.ComputedHash) };
                foreach (RecordEnvelope? record in new[] { position.Work, step.Assignment, step.Result, step.Review })
                {
                    if (record is not null)
                    {
                        sources.Add(new EventReference(record.Type, record.Id, record.ComputedHash));
                    }
                }

                var related = new List<string> { root };
                related.AddRange(relations.Of(position.Work));
                related.AddRange(relations.Of(step.Assignment));
                // A step's name can carry an item a goal configures, so the step is named only by an opaque digest.
                string key = root + "/" + step.Name;
                items[key] = new EventItem("step", new EventReference("step", journal!.Name("step", key)), Bounded(sources), step.State + (step.Waiting ? ":waiting" : string.Empty),
                    step.Principal, step.State is "blocked" or "stale" ? "blocked" : null, false, related.Distinct(StringComparer.Ordinal).ToList());
            }
        }

        return items;
    }

    private Dictionary<string, EventItem> RunItems(EstateModel model, EventRelations relations)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
        foreach (RunRow run in Runs(model).Runs)
        {
            RecordEnvelope? intent = model.ById.GetValueOrDefault(run.Run);
            EventReference subject = intent is null ? new EventReference("operation", run.Run) : new EventReference(intent.Type, intent.Id, intent.ComputedHash);
            var sources = new List<EventReference>();
            foreach (string? id in new[] { run.Assignment, run.Goal })
            {
                if (id is not null && model.ById.TryGetValue(id, out RecordEnvelope? record))
                {
                    sources.Add(new EventReference(record.Type, record.Id, record.ComputedHash));
                }
            }

            bool stalled = run.Liveness?.AttentionAt is not null || run.Signals?.Any(s => s.Severity == "critical") == true;
            bool overdue = run.RequestedProgress?.Overdue == true;
            string state = Digest(JsonSerializer.Serialize(new
            {
                run.Status,
                stop = run.Stop is not null,
                stall = run.Liveness?.AttentionAt,
                stopped = run.Liveness?.StoppedAt,
                signals = run.Signals?.Select(s => s.Code + ":" + s.Count + ":" + (s.Attention ?? string.Empty)).Order(StringComparer.Ordinal).ToArray(),
                overdue,
                closed = run.RequestedProgress?.Closed,
            }, ProtocolJson.Options));
            string? category = stalled ? "incident" : run.Status == "uncertain" ? "uncertain-effect" : overdue ? "blocked" : null;
            var related = new List<string>(relations.Of(intent));
            if (run.Goal is not null)
            {
                related.Add(run.Goal);
            }

            related.AddRange(relations.Of(run.Assignment is null ? null : model.ById.GetValueOrDefault(run.Assignment)));
            items[run.Run] = new EventItem("run", subject, sources, state, run.Agent, category, false, related.Distinct(StringComparer.Ordinal).ToList());
        }

        return items;
    }

    /// <summary>
    /// A reference an envelope may carry: a record by type, id and hash; any other subject by its kind and a name keyed on this
    /// machine, since its identity can be a path or a command that a reader could otherwise confirm by guessing.
    /// </summary>
    private EventReference Reference(EstateModel model, string type, string id, string? hash) =>
        model.ById.TryGetValue(id, out RecordEnvelope? record)
            ? new EventReference(record.Type, record.Id, hash ?? record.ComputedHash)
            : new EventReference(SafeType(type), journal!.Name(type, id));

    private static string SafeType(string type) =>
        type.Length is > 0 and <= 64 && type.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ? type : "subject";

    /// <summary>A digest of a derived state; it never reaches an envelope except inside a key.</summary>
    private static string Digest(string state) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static List<EventReference> Bounded(List<EventReference> sources) =>
        sources.Distinct().Take(EventVocabulary.MaxSources).ToList();

    /// <summary>
    /// The attention category of a notice's kind, one of the six attention kinds of the cohesion contract § 5. A kind this build does
    /// not name is information.
    /// </summary>
    internal static string Category(string kind) => kind switch
    {
        "decision needed" or "required-decision" or "operator-request" => "decision-needed",
        "blocker" or "blocked" or "progress-overdue" or "exhausted-budget" => "blocked",
        "uncertain-effect" or "uncertain effect" => "uncertain-effect",
        "failed check" or "failed-check" or "unknown-check" => "failed-check",
        "incident" => "incident",
        _ => "information",
    };

    /// <summary>What one derivation pass covers.</summary>
    private sealed class EventPass
    {
        /// <summary>The asks, as an asks read shows them.</summary>
        public bool Asks { get; init; }

        /// <summary>The operator requests.</summary>
        public bool Requests { get; init; }

        /// <summary>Every goal's flow steps.</summary>
        public bool Steps { get; init; }

        /// <summary>The runs, as a progress read shows them.</summary>
        public bool Runs { get; init; }

        /// <summary>One goal's flow steps, as a goal read shows them.</summary>
        public string? StepsOfGoal { get; init; }

        /// <summary>The principals whose attention is derived, with the attention a read already computed.</summary>
        public Dictionary<string, AttentionResponse?> Attention { get; } = new(StringComparer.Ordinal);
    }

    private sealed class EventListener(EstateService service, EventSubscription subscription) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                service.listening.TryRemove(subscription, out _);
            }
        }
    }
}

/// <summary>Who may read events now, as the last pass found the records: the principals known and every device's standing.</summary>
internal sealed record EventAudience(IReadOnlySet<string> Principals, IReadOnlyDictionary<string, DeviceStanding> Devices)
{
    public static EventAudience Empty { get; } = new(new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, DeviceStanding>(StringComparer.Ordinal));

    public static EventAudience From(EstateModel model)
    {
        var principals = new HashSet<string>(model.OfType("principal").Select(p => model.Chain(p)[0]), StringComparer.Ordinal);
        var devices = new Dictionary<string, DeviceStanding>(StringComparer.Ordinal);
        foreach (RecordEnvelope device in model.OfType("device"))
        {
            string root = model.Chain(device)[0];
            if (!devices.ContainsKey(root) && EstateService.StandingIn(model, root) is DeviceStanding standing)
            {
                devices[root] = standing;
            }
        }

        return new EventAudience(principals, devices);
    }
}

/// <summary>The record ids an event relates to, for a subjects filter: a record's chain, the subjects it rests on, and their goals.</summary>
internal sealed class EventRelations(EstateModel model)
{
    private Dictionary<string, List<string>>? goalsByOrder;

    public IReadOnlyList<string> Of(RecordEnvelope? record)
    {
        if (record is null)
        {
            return [];
        }

        var related = new List<string>();
        RecordEnvelope? current = record;
        for (int depth = 0; current is not null && depth < 8; depth++)
        {
            string root = model.Chain(current)[0];
            related.Add(root);
            if (current.Id != root)
            {
                related.Add(current.Id);
            }

            if (EstateModel.BodyString(current, "goal") is string goal && model.ById.TryGetValue(goal, out RecordEnvelope? goalRecord))
            {
                related.Add(model.Chain(goalRecord)[0]);
            }

            if (current.Type == "work-order" && Goals().TryGetValue(root, out List<string>? goals))
            {
                related.AddRange(goals);
            }

            current = current.Subject is { } subject ? model.ById.GetValueOrDefault(subject.Id) : null;
        }

        return related.Distinct(StringComparer.Ordinal).ToList();
    }

    private Dictionary<string, List<string>> Goals()
    {
        if (goalsByOrder is not null)
        {
            return goalsByOrder;
        }

        goalsByOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (RecordEnvelope goal in model.Current("goal"))
        {
            string goalRoot = model.Chain(goal)[0];
            IReadOnlyList<RecordEnvelope> work;
            try
            {
                work = GoalRules.Work(model, goal);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException)
            {
                continue;
            }

            foreach (RecordEnvelope order in work)
            {
                string orderRoot = model.Chain(order)[0];
                if (!goalsByOrder.TryGetValue(orderRoot, out List<string>? goals))
                {
                    goalsByOrder[orderRoot] = goals = [];
                }

                if (!goals.Contains(goalRoot, StringComparer.Ordinal))
                {
                    goals.Add(goalRoot);
                }
            }
        }

        return goalsByOrder;
    }
}
