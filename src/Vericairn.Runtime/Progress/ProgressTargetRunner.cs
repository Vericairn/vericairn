// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Progress;

/// <summary>
/// From 0.9.4 (WATCH-06): one outward effect on a progress target before it is dispatched: the target, the action (create, replace or
/// comment), the form, the exact text, and every record whose text the text carries, which the effect is charged with under
/// design/holds-reads-sends.md § 7.
/// </summary>
internal sealed record ProgressEffect(string Target, string Action, string Form, string Text, IReadOnlyList<RecordReference> Carried);

/// <summary>
/// From 0.9.4 (WATCH-06): what one refresh or comment did: created, replaced, unchanged or commented; would-create, would-replace or
/// would-comment in a dry run; refused (nothing was sent, as <paramref name="Detail"/> says), stopped (held for a person before
/// dispatch) or uncertain (whether the adapter sent it or the service accepted it is unknown).
/// </summary>
internal sealed record ProgressOutcome(string Target, string Action, string? Url, string? Form, IReadOnlyList<RecordReference> Carried, string? Detail = null)
{
    /// <summary>The text placed or, in a dry run, that would be placed; null when nothing was composed.</summary>
    public string? Text { get; init; }

    /// <summary>The keys of the work orders the public form left out.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];

    /// <summary>The event or wait key of a comment.</summary>
    public string? CommentKey { get; init; }

    /// <summary>True when the target refused the effect or its outcome is uncertain.</summary>
    public bool Failed => Action is "refused" or "uncertain";
}

/// <summary>
/// From 0.9.4 (WATCH-06): keeps a configured progress target's text equal to the estate's progress report, through the adapter its
/// configuration chooses, refreshing and commenting only on the events its triggers name. The runtime composes every text and checks
/// it against the disclosure rules before the adapter sees it; the adapter only places it. Every effect is offered to <see cref="Stop"/>
/// first, with what it is charged with: this build sets no stop, since deriving an effect's exposure and stopping it for a person is
/// GOAL-15's (design/holds-reads-sends.md § 7), which supplies one here.
/// </summary>
internal sealed class ProgressTargetRunner(Func<EstateModel> load, ProviderRegistry registry, ProgressTargetStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>
    /// Asked before each effect is dispatched: a reason to hold it for a person, or null to dispatch it. An effect it holds is reported
    /// stopped and nothing is sent. GOAL-15 supplies the stop for an effect whose exposure holds and reads; none is set before it.
    /// </summary>
    internal Func<ProgressEffect, string?>? Stop { get; init; }

    /// <summary>The configured target of a name with its adapter and settings, or why it cannot be used.</summary>
    internal (Target? Target, string? Problem) Resolve(string name)
    {
        ProgressTargetConfiguration? configuration = store.Find(name);
        if (configuration is null) return (null, $"No progress target is named '{name}' on this machine; set one with 'vericairn progress target set'.");
        IProgressTargetAdapter? adapter = registry.ProgressTarget(configuration.Adapter);
        return configuration.Problem(adapter) is string problem ? (null, problem) : (new Target(configuration, adapter!), null);
    }

    /// <summary>The event filter a follower of a target reads with: only the kinds its triggers name.</summary>
    internal static EventFilter Filter(ProgressTargetConfiguration target) =>
        new([.. target.Refresh.Concat(target.Comment).Select(t => t.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

    /// <summary>Brings a target's text equal to the estate's progress now; in a dry run, locates and composes, and sends nothing.</summary>
    internal ProgressOutcome Refresh(string name, bool dryRun = false)
    {
        (Target? target, string? problem) = Resolve(name);
        return target is null ? new ProgressOutcome(name, "refused", null, null, [], problem) : Refresh(target, load(), dryRun);
    }

    /// <summary>
    /// Follows the event journal for a target until the stream ends or is cancelled: once the stream is open the target is refreshed, so
    /// no change is missed, and again after a reset; each batch of events read together refreshes it at most once and adds a comment
    /// for each event or new wait a comment trigger names. The cursor is kept after each batch, so a follower started again resumes there.
    /// </summary>
    internal async Task FollowAsync(string name, Func<string?, EventFilter, IAsyncEnumerable<EventStreamItem>> open, Action<ProgressOutcome> told, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(told);
        (Target? target, string? problem) = Resolve(name);
        if (target is null) throw new InvalidOperationException(problem);
        ProgressTargetState state = RecoverComments(name, told);
        var channel = Channel.CreateUnbounded<EventStreamItem>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var pumpStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pump = Task.Run(async () =>
        {
            try
            {
                await foreach (EventStreamItem item in open(state.Cursor, Filter(target.Configuration)).WithCancellation(pumpStop.Token).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(item, pumpStop.Token).ConfigureAwait(false);
                }

                channel.Writer.Complete();
            }
            catch (Exception exception)
            {
                channel.Writer.Complete(exception);
            }
        }, CancellationToken.None);

        try
        {
            bool first = true;
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var events = new List<EventEnvelope>();
                bool reset = false;
                string? cursor = null;
                while (channel.Reader.TryRead(out EventStreamItem? item))
                {
                    cursor = item.Cursor;
                    if (item.Event is EventEnvelope envelope) events.Add(envelope);
                    else if (item.Reset) reset = true;
                }

                // A target configured afresh while following is read again at each batch.
                (Target? current, string? changed) = Resolve(name);
                if (current is null)
                {
                    told(new ProgressOutcome(name, "refused", null, null, [], changed));
                    continue;
                }

                foreach (ProgressOutcome outcome in Take(current, events, first || reset, cursor)) told(outcome);
                first = false;
            }
        }
        finally
        {
            await pumpStop.CancelAsync().ConfigureAwait(false);
            await pump.ConfigureAwait(false);
        }
    }

    private ProgressTargetState RecoverComments(string name, Action<ProgressOutcome> told)
    {
        var recovered = new List<ProgressOutcome>();
        ProgressTargetState recoveredState = store.Update(name, state =>
        {
            ImmutableSortedDictionary<string, ProgressTargetComment> comments = state.Comments;
            foreach ((string key, ProgressTargetComment entry) in state.Comments)
            {
                if (entry.Outcome is not null) continue;
                comments = comments.SetItem(key, entry with { Outcome = "uncertain" });
                recovered.Add(new ProgressOutcome(name, "uncertain", entry.PlaceUrl, entry.Form, [],
                    $"The follower stopped before saving the outcome for comment {key}. Check the original destination. The follower will keep the entry without retrying.")
                    { CommentKey = key });
            }
            return comments == state.Comments ? state : state with { Comments = comments };
        });

        foreach (ProgressOutcome outcome in recovered) told(outcome);
        return recoveredState;
    }

    /// <summary>
    /// One batch of events for a target: whether a refresh trigger fires, and the comment each comment trigger adds, read from the
    /// records. With <paramref name="look"/> (the stream opened or was reset) the target is refreshed whatever the events, and the
    /// waits are compared with those last seen.
    /// </summary>
    internal IReadOnlyList<ProgressOutcome> Take(Target target, IReadOnlyList<EventEnvelope> events, bool look, string? cursor = null)
    {
        ProgressTargetConfiguration configuration = target.Configuration;
        List<(EventEnvelope Event, ProgressTrigger Trigger)> refreshing = [.. events.SelectMany(e => configuration.Refresh.Where(t => t.Admits(e)).Select(t => (e, t)))];
        List<(EventEnvelope Event, ProgressTrigger Trigger)> commenting = [.. events.SelectMany(e => configuration.Comment.Where(t => t.Admits(e)).Select(t => (e, t)))];
        if (!look && refreshing.Count == 0 && commenting.Count == 0)
        {
            if (cursor is not null) store.Update(configuration.Name, state => state with { Cursor = cursor });
            return [];
        }

        EstateModel model = load();
        DateTimeOffset at = time.GetUtcNow();
        ProgressTargetState state = store.State(configuration.Name);
        // What waits is read only for a target whose triggers ask about it, when one of them is woken or the target looks afresh.
        bool waiting = configuration.Refresh.Concat(configuration.Comment).Any(t => t.When == "waiting");
        bool waking = waiting && (look || refreshing.Concat(commenting).Any(p => p.Trigger.When == "waiting"));
        IReadOnlyList<Wait>? waits = waking ? Waits(model, at) : null;
        HashSet<string>? before = state.Waiting is { } seen ? [.. seen] : null;
        // A first look comments on nothing that already waited: with nothing seen before, nothing is new.
        List<Wait> fresh = waits is null || before is null ? [] : [.. waits.Where(w => !before.Contains(w.Key))];

        bool refresh = look || refreshing.Any(p => Fires(model, p.Trigger, p.Event, fresh));
        var comments = new List<(string Key, Remark Remark)>();
        var commented = new HashSet<string>(StringComparer.Ordinal);
        // Each wait that began since the target last looked, once, for a waiting comment trigger an event woke or a fresh look (a
        // reset, or following started again) wakes; then each event a comment trigger names whose condition the records show.
        foreach (ProgressTrigger trigger in configuration.Comment.Where(t => t.When == "waiting" && (look || commenting.Any(p => p.Trigger.Equals(t)))))
        {
            foreach (Wait wait in fresh.Where(w => w.Kind == trigger.Kind && Matches(model, w.Principal, trigger.To)
                && !state.Comments.ContainsKey(w.Key) && commented.Add(w.Key)))
                comments.Add((wait.Key, WaitComment(model, wait)));
        }

        foreach ((EventEnvelope envelope, ProgressTrigger trigger) in commenting.Where(p => p.Trigger.When != "waiting"))
        {
            if (!state.Comments.ContainsKey(envelope.Key) && Fires(model, trigger, envelope, fresh) && commented.Add(envelope.Key))
                comments.Add((envelope.Key, EventComment(model, trigger, envelope)));
        }

        var outcomes = new List<ProgressOutcome>();
        // A refresh only where a refresh trigger fires; the text is replaced before any comment of the same batch is added.
        if (refresh) outcomes.Add(Refresh(target, model, dryRun: false));
        foreach ((string key, Remark remark) in comments)
        {
            if (Comment(target, model, key, remark, at) is ProgressOutcome outcome) outcomes.Add(outcome);
        }

        if (waits is not null || cursor is not null)
            store.Update(configuration.Name, current => current with
            {
                Cursor = cursor ?? current.Cursor,
                Waiting = waits is null ? current.Waiting : [.. waits.Select(w => w.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            });
        return outcomes;
    }

    private ProgressOutcome Refresh(Target target, EstateModel model, bool dryRun)
    {
        ProgressTargetConfiguration configuration = target.Configuration;
        string name = configuration.Name;
        ProgressTargetState state = store.State(name);
        ProgressTargetSpec spec = Spec(target, model, state.PlaceId);
        ProgressTargetDestination destination;
        try
        {
            destination = target.Adapter.Locate(spec);
        }
        catch (ProviderRefusedException refused)
        {
            return new ProgressOutcome(name, "refused", null, null, [], refused.Message);
        }

        (string form, string? note) = Form(configuration, destination);
        DateTimeOffset at = time.GetUtcNow();
        // The text's heading is the place's title; the charter is charged when its name gives it.
        ProgressPayload payload = ProgressReport.RenderTarget(model, form, configuration.Title, spec.Marker, at);
        var composed = new ProgressOutcome(name, string.Empty, destination.Place?.Url, form, payload.Carried, note) { Text = payload.Text, LeftOut = payload.LeftOut };
        if (ProgressReport.TargetRefusal(model, form, payload.Text, at) is string reason)
            return composed with { Action = "refused", Detail = $"The {form} form would carry {reason}; nothing was sent." };

        ProgressTargetPlace? place = destination.Place;
        if (place is not null && Same(place.Body, payload.Text))
        {
            Keep(name, place);
            return composed with { Action = "unchanged" };
        }

        string action = place is null ? "create" : "replace";
        if (dryRun) return composed with { Action = "would-" + action };
        if (Stop?.Invoke(new ProgressEffect(name, action, form, payload.Text, payload.Carried)) is string stop)
            return composed with { Action = "stopped", Detail = stop };

        try
        {
            ProgressTargetPlace placed = place is null ? target.Adapter.Create(spec, payload.Text) : target.Adapter.Replace(spec, place, payload.Text);
            Keep(name, placed);
            return composed with { Action = place is null ? "created" : "replaced", Url = placed.Url };
        }
        catch (ProviderRefusedException refused)
        {
            return composed with { Action = "refused", Detail = refused.Message };
        }
        catch (ProviderUncertainException uncertain) when (place is null)
        {
            // A creation that may have happened is reconciled by locating it before anything is created again.
            try
            {
                if (target.Adapter.Locate(spec).Place is ProgressTargetPlace found)
                {
                    Keep(name, found);
                    return composed with { Action = "created", Url = found.Url, Detail = "The creation was confirmed by locating it." };
                }
            }
            catch (ProviderRefusedException)
            {
            }

            return composed with { Action = "uncertain", Detail = uncertain.Message + " The next refresh locates it before creating another." };
        }
        catch (ProviderUncertainException uncertain)
        {
            return composed with { Action = "uncertain", Detail = uncertain.Message + " The next refresh compares the text and replaces it again." };
        }
    }

    private ProgressOutcome? Comment(Target target, EstateModel model, string key, Remark remark, DateTimeOffset at)
    {
        string name = target.Configuration.Name;
        ProgressTargetSpec spec = Spec(target, model, store.State(name).PlaceId);
        ProgressTargetDestination destination;
        try
        {
            destination = target.Adapter.Locate(spec);
        }
        catch (ProviderRefusedException refused)
        {
            return new ProgressOutcome(name, "refused", null, null, [], refused.Message) { CommentKey = key };
        }

        // A comment takes the form its destination allows now, as the text does.
        string form = Form(target.Configuration, destination).Form;
        string body = form == ProgressReport.PublicForm ? remark.Public : remark.Text;
        IReadOnlyList<RecordReference> charged = form == ProgressReport.PublicForm ? remark.PublicCarried : remark.Carried;
        var composed = new ProgressOutcome(name, string.Empty, destination.Place?.Url, form, charged) { Text = body, CommentKey = key };
        if (ProgressReport.TargetRefusal(model, form, body, at) is string reason)
            return composed with { Action = "refused", Detail = $"The {form} form's comment would carry {reason}; nothing was sent." };
        if (destination.Place is not ProgressTargetPlace place) return composed with { Action = "refused", Detail = "The target has no place to comment on yet; refresh it first." };
        Keep(name, place);
        if (Stop?.Invoke(new ProgressEffect(name, "comment", form, body, charged)) is string stop) return composed with { Action = "stopped", Detail = stop };

        string payloadHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var pending = new ProgressTargetComment(target.Configuration.Adapter, payloadHash, form, place.Id, place.Url);
        bool reserved = false;
        store.Update(name, state =>
        {
            if (state.Comments.ContainsKey(key)) return state;
            reserved = true;
            return state with { Comments = state.Comments.Add(key, pending) };
        });
        if (!reserved) return null;

        ProgressOutcome outcome;
        try
        {
            string url = target.Adapter.Comment(spec, place, body);
            outcome = composed with { Action = "commented", Url = url };
        }
        catch (ProviderRefusedException refused)
        {
            outcome = composed with { Action = "refused", Detail = refused.Message };
        }
        catch (ProviderUncertainException uncertain)
        {
            outcome = composed with { Action = "uncertain", Detail = uncertain.Message + " Check the original destination. The follower will keep the entry without retrying." };
        }

        var finished = pending with { Outcome = outcome.Action, Url = outcome.Action == "commented" ? outcome.Url : null };
        store.Update(name, state => state.Comments.ContainsKey(key) ? state with { Comments = state.Comments.SetItem(key, finished) } : state);
        return outcome;
    }

    private void Keep(string name, ProgressTargetPlace place)
    {
        store.Update(name, state => state.PlaceId == place.Id && state.PlaceUrl == place.Url
            ? state : state with { PlaceId = place.Id, PlaceUrl = place.Url });
    }

    /// <summary>
    /// The form a target is given: private only when it is configured private and its adapter reports its destination readable by its
    /// own members alone; otherwise public, with the reason when the configuration asked for private.
    /// </summary>
    internal static (string Form, string? Note) Form(ProgressTargetConfiguration configuration, ProgressTargetDestination destination) =>
        configuration.Form == ProgressReport.PrivateForm && !destination.Private
            ? (ProgressReport.PublicForm, "Its destination is readable beyond its own members, or its adapter cannot tell, so it takes the public form.")
            : (configuration.Form, null);

    /// <summary>The adapter's view of a target: its settings, title and the marker that ends every text.</summary>
    internal static ProgressTargetSpec Spec(Target target, EstateModel model, string? known)
    {
        ProgressTargetConfiguration configuration = target.Configuration;
        RecordEnvelope? charter = model.CurrentEstate;
        string title = configuration.Title ?? "Progress of " + (charter is null ? "the estate" : EstateModel.BodyString(charter, "name"));
        // The marker names the target and a digest of the estate's identity, so two estates' targets in one place never meet.
        string estate = charter is null ? "none" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(model.Chain(charter)[0])))[..16];
        return new ProgressTargetSpec(configuration.Name, configuration.Effective(target.Adapter), title, $"vericairn progress-target {configuration.Name} {estate}", known);
    }

    private static bool Same(string? placed, string text) =>
        placed is not null && placed.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == text.TrimEnd();

    // Whether a trigger's condition holds for an event, read from the records.
    private static bool Fires(EstateModel model, ProgressTrigger trigger, EventEnvelope envelope, IReadOnlyList<Wait> fresh)
    {
        if (trigger.When == "waiting") return fresh.Any(w => w.Kind == trigger.Kind && Matches(model, w.Principal, trigger.To));
        if (trigger.To is not null && !Matches(model, envelope.To, trigger.To)) return false;
        RecordEnvelope? record = model.ById.GetValueOrDefault(envelope.Subject.Id);
        return trigger.When switch
        {
            "state-changed" => record is not null && EstateModel.BodyString(record, "status") is string status
                && (record.Supersedes is not string prior || !model.ById.TryGetValue(prior, out RecordEnvelope? previous) || EstateModel.BodyString(previous, "status") != status),
            "merged" => record is { Type: "operation" } && EstateModel.BodyString(record, "action") == "change-merge" && EstateModel.BodyString(record, "status") == "observed-success",
            _ => true,
        };
    }

    // Whether a principal is the one a trigger's to names.
    private static bool Matches(EstateModel model, string? principal, string? to)
    {
        if (to is null) return true;
        if (principal is null) return false;
        return to switch
        {
            "operator" => model.HasRole(principal, Records.Types.Roles.Operator),
            "person" => Kind(model, principal) == "human",
            "agent" or "service" => Kind(model, principal) == to,
            _ => principal == to,
        };
    }

    private static string? Kind(EstateModel model, string principal) => model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "kind") : null;

    /// <summary>
    /// Everything that waits on a principal now: each run's stops that reach their person (due or delivered), each open operator request
    /// and each ask still waiting, by a key that stays the same while it waits.
    /// </summary>
    internal static IReadOnlyList<Wait> Waits(EstateModel model, DateTimeOffset at)
    {
        var waits = new List<Wait>();
        foreach (FlowPosition.Position position in FlowStops.Runs(model, at))
        {
            string root = model.Chain(position.Goal)[0];
            foreach (FlowStops.RunStop stop in position.Stops.Where(s => s.Principal is not null && s.Delivery is "due" or "delivered"))
                waits.Add(new Wait("step", $"step:{root}/{stop.Name}/{stop.Kind}", stop.Principal, position.Goal, stop.Record) { Stop = stop });
        }

        foreach (RecordEnvelope request in OperatorRequests.WaitingOf(model, null, at).Open)
            waits.Add(new Wait("request", "request:" + model.Chain(request)[0], EstateModel.BodyString(request, "to"), request, null));
        foreach (RecordEnvelope ask in model.Current("ask").Where(a => Asks.IsWaiting(Asks.State(model, a).State)))
            waits.Add(new Wait("ask", "ask:" + model.Chain(ask)[0], EstateModel.BodyString(ask, "to"), ask, null));
        return waits;
    }

    // A comment for something that now waits: in the private form who it waits on and what, from the records; in the public form only
    // that something waits, and on whom by role.
    private static Remark WaitComment(EstateModel model, Wait wait)
    {
        RecordEnvelope? principal = model.CurrentPrincipal(wait.Principal);
        string who = principal is null ? wait.Principal ?? "someone" : EstateModel.BodyString(principal, "name") ?? wait.Principal!;
        string whom = wait.Principal is not null && model.HasRole(wait.Principal, Records.Types.Roles.Operator) ? "the operator" : "a person";
        var carried = new List<RecordReference>();
        void Carry(RecordEnvelope? record)
        {
            if (record is not null && carried.All(r => r.Id != record.Id)) carried.Add(new RecordReference(record.Type, record.Id, record.ComputedHash));
        }

        Carry(wait.Subject);
        Carry(principal);
        Carry(wait.Delivered);
        string text = wait.Kind switch
        {
            "step" => $"Waiting on {who}: {wait.Stop!.Title}, step {wait.Stop.Name} of the run {EstateModel.BodyString(wait.Subject, "title")}. {wait.Stop.Reason}",
            "request" => $"An operator request waits on {who}: {EstateModel.BodyString(wait.Subject, "action")} Recommendation: {EstateModel.BodyString(wait.Subject, "recommendation")}",
            _ => $"An ask waits on {who}: {EstateModel.BodyString(wait.Subject, "question")} Recommendation: {EstateModel.BodyString(wait.Subject, "recommendation")}",
        };
        string open = wait.Kind switch
        {
            "step" => $"A step of a run now waits on {whom}.",
            "request" => "An operator request now waits on the operator.",
            _ => $"An ask now waits on {whom}.",
        };
        return new Remark(text, carried, open, []);
    }

    // A comment for an event a comment trigger names: a release's state as the records give it, else what the event says, read from
    // its subject; in the public form a release's version and state, and otherwise only that the progress changed.
    private static Remark EventComment(EstateModel model, ProgressTrigger trigger, EventEnvelope envelope)
    {
        RecordEnvelope? record = model.ById.GetValueOrDefault(envelope.Subject.Id);
        if (record is { Type: "release" })
        {
            string identity = record.Subject is null ? string.Empty : LivingDocuments.ProductIdentity(model, record.Subject.Id);
            RecordEnvelope? product = model.ById.TryGetValue(identity, out RecordEnvelope? first) ? model.Latest(first) : null;
            string release = ((product is null ? string.Empty : EstateModel.BodyString(product, "name") + " ") + EstateModel.BodyString(record, "version")).Trim();
            string line = $"{release} is now {EstateModel.BodyString(record, "status")}.";
            List<RecordReference> carried = [new(record.Type, record.Id, record.ComputedHash)];
            if (product is not null) carried.Add(new RecordReference(product.Type, product.Id, product.ComputedHash));
            return new Remark(line, carried, line, carried);
        }

        string what = record is null ? $"{envelope.Subject.Type} {envelope.Subject.Id}"
            : record.Type == "work-order" ? $"work order {EstateModel.BodyString(record, "key")} {EstateModel.BodyString(record, "title")}"
            : $"{record.Type} {record.Id}";
        return new Remark($"{envelope.Kind} {envelope.Change}: {what}.", record is null ? [] : [new RecordReference(record.Type, record.Id, record.ComputedHash)],
            "The estate's progress changed; the text above is current.", []);
    }

    /// <summary>A comment in both forms, each with every record whose text it carries.</summary>
    private sealed record Remark(string Text, IReadOnlyList<RecordReference> Carried, string Public, IReadOnlyList<RecordReference> PublicCarried);

    /// <summary>A configured target with the adapter its configuration chose.</summary>
    internal sealed record Target(ProgressTargetConfiguration Configuration, IProgressTargetAdapter Adapter);

    /// <summary>Something that waits on a principal, by a key that stays the same while it waits.</summary>
    internal sealed record Wait(string Kind, string Key, string? Principal, RecordEnvelope Subject, RecordEnvelope? Delivered)
    {
        /// <summary>The run's stop, for a step.</summary>
        public FlowStops.RunStop? Stop { get; init; }
    }
}
