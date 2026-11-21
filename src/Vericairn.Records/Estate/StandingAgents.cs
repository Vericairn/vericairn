// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Standing agents (from 0.9.4, GOAL-07; the estate's design/standing-agents.md §§ 4 to 7 and 10, design/flow.md § 5): a stewardship goal
/// whose plan carries <c>standing</c>, naming a flow with triggers, the subject it binds, its triggers, its caps per UTC-anchored period,
/// an until time and optionally the runtime that dispatches it. Triggers make occurrences due, each with a key stable across machines,
/// restarts and redelivery; each occurrence is carried as one assignment of the goal's one ordinary work order, which names the key. What
/// is due is a pure function of the records, the pinned flow and an explicit UTC time, repeated before dispatch; a key holds at most one
/// assignment, whoever records it, and the period caps, the concurrency and the failure pause hold every run.
/// </summary>
internal static class StandingAgents
{
    /// <summary>The trigger kinds a standing plan declares (design/standing-agents.md § 6).</summary>
    public static readonly ImmutableArray<string> TriggerKinds = ["schedule", "duty", "event", "condition", "request"];

    /// <summary>The units of a schedule trigger and of a period.</summary>
    public static readonly ImmutableArray<string> Units = ["days", "months"];

    /// <summary>Consecutive failed runs that pause a standing agent with an ask when its goal's limits set none (§ 10).</summary>
    public const int DefaultConsecutiveFailures = 3;

    /// <summary>The results awaiting review that defer a standing agent's dispatch when its plan sets none (§ 10).</summary>
    public const int DefaultAwaitingReview = 3;

    /// <summary>The days until a standing agent returns to its sponsor when its proposal names no until time (§ 5).</summary>
    public const int DefaultDays = 30;

    /// <summary>The most occurrences one trigger shows, oldest first; an event burst past it waits for the next evaluation.</summary>
    public const int MaximumOccurrences = 200;

    /// <summary>The words that open the renewal ask, once for each goal revision before its until time.</summary>
    public const string RenewalAsk = "Renew standing agent";

    /// <summary>How long before its until time a standing agent asks its sponsor to renew it, at most half of its term.</summary>
    public static readonly TimeSpan RenewalLead = TimeSpan.FromDays(3);

    /// <summary>The words that open the ask a period cap raises, once in each period.</summary>
    public const string PeriodAsk = "A period cap of standing agent";

    /// <summary>A trigger of a standing plan: its key, kind, declaration and the goal revision its declaration originates from.</summary>
    /// <param name="Key">The trigger's key within the plan.</param>
    /// <param name="Kind">schedule, duty, event, condition or request.</param>
    /// <param name="Declaration">The trigger as the plan declares it.</param>
    /// <param name="Revision">The earliest goal revision from which the declaration is unchanged up to the goal's current one, part of every key.</param>
    public sealed record Trigger(string Key, string Kind, JsonElement Declaration, string Revision);

    /// <summary>A duty occurrence the runtime's policy evaluation owes, which a duty trigger carries under the duty's own identity.</summary>
    /// <param name="Identity">The duty occurrence's identity: the policy, the subject and the schedule item.</param>
    /// <param name="Policy">The policy identity.</param>
    /// <param name="Rule">The rule key.</param>
    /// <param name="DueAt">When it fell due.</param>
    /// <param name="Deadline">Its grace deadline.</param>
    /// <param name="Origin">The policy revision the occurrence originates from.</param>
    /// <param name="Item">The schedule item.</param>
    public sealed record DutyDue(string Identity, string Policy, string Rule, DateTimeOffset DueAt, DateTimeOffset Deadline, RecordReference Origin, JsonElement Item);

    /// <summary>One occurrence: its key, trigger, item and when it fell due, with the records that made it due.</summary>
    /// <param name="Key">The occurrence key.</param>
    /// <param name="Trigger">The trigger's key.</param>
    /// <param name="Kind">The trigger's kind.</param>
    /// <param name="Revision">The trigger's revision.</param>
    /// <param name="Item">The item, as the assignment carrying it records it.</param>
    /// <param name="DueAt">When it fell due.</param>
    /// <param name="Records">The records that made it due, quoted to its brief as untrusted content.</param>
    /// <param name="Summary">The occurrence in words.</param>
    public sealed record Occurrence(string Key, string Trigger, string Kind, string Revision, JsonElement Item, DateTimeOffset DueAt, IReadOnlyList<RecordReference> Records, string Summary);

    /// <summary>An occurrence's state as of a time.</summary>
    /// <param name="Occurrence">The occurrence.</param>
    /// <param name="State">due, deferred, running, assigned, settled, disposed or held.</param>
    /// <param name="Reason">Why, in words.</param>
    /// <param name="Assignment">The assignment that holds its key, when one does.</param>
    /// <param name="Run">Its latest run, when it has one.</param>
    /// <param name="Result">Its latest result, when it has one.</param>
    public sealed record OccurrenceState(Occurrence Occurrence, string State, string Reason, RecordEnvelope? Assignment, AgentRuns.Run? Run, RecordEnvelope? Result);

    /// <summary>A UTC-anchored period and what a standing agent's runs spent in it.</summary>
    /// <param name="Unit">days or months.</param>
    /// <param name="Every">How many units it spans.</param>
    /// <param name="Start">Its start.</param>
    /// <param name="End">Its end, exclusive.</param>
    /// <param name="Caps">Its caps: runs, wallMinutes and, where granted, tokens and currency (minor units).</param>
    /// <param name="Spent">What its runs were charged, in the same units, runs counted.</param>
    /// <param name="Currency">The currency unit, when it caps currency.</param>
    public sealed record Period(string Unit, int Every, DateTimeOffset Start, DateTimeOffset End, IReadOnlyDictionary<string, long> Caps, IReadOnlyDictionary<string, long> Spent, string? Currency)
    {
        /// <summary>The caps it has reached, in words; empty while every cap leaves something.</summary>
        public IReadOnlyList<string> Reached => Caps.Where(c => Spent.GetValueOrDefault(c.Key) >= c.Value)
            .Select(c => $"{c.Key} {Spent.GetValueOrDefault(c.Key).ToString(CultureInfo.InvariantCulture)} of {c.Value.ToString(CultureInfo.InvariantCulture)}").ToList();
    }

    /// <summary>A standing agent as of a time: its state, its occurrences, its period and what pauses it.</summary>
    /// <param name="Goal">The goal's current revision.</param>
    /// <param name="Status">The goal's status.</param>
    /// <param name="State">active, paused, lapsed, held, unavailable, stopped, or the goal's status otherwise.</param>
    /// <param name="Reason">Why, in words.</param>
    /// <param name="AsOf">The time it is evaluated at.</param>
    /// <param name="Flow">The flow, when it resolves here.</param>
    /// <param name="Step">The flow's work step each occurrence carries.</param>
    /// <param name="Unavailable">Why the flow does not resolve here.</param>
    /// <param name="Order">The goal's one work order.</param>
    /// <param name="Until">When it returns to its sponsor.</param>
    /// <param name="RunsOn">The runtime that dispatches it, by its identity's public-key hash, when the plan names one.</param>
    /// <param name="Period">The current period.</param>
    /// <param name="InFlight">Its runs in flight.</param>
    /// <param name="Concurrency">How many of its runs may be in flight at once.</param>
    /// <param name="Failures">Its consecutive failed runs that count toward the pause.</param>
    /// <param name="FailureLimit">The consecutive failed runs that pause it.</param>
    /// <param name="FailureTrigger">The latest of those failures, which the pause's ask names.</param>
    /// <param name="Triggers">Its triggers.</param>
    /// <param name="Next">When each trigger is next due, by key; null when it waits for a record or a request.</param>
    /// <param name="Occurrences">Its occurrences, by when they fell due.</param>
    public sealed record Agent(RecordEnvelope Goal, string Status, string State, string Reason, DateTimeOffset AsOf, FlowDefinition? Flow, FlowStep? Step, string? Unavailable, RecordEnvelope? Order,
        DateTimeOffset? Until, string? RunsOn, Period? Period, int InFlight, int Concurrency, int Failures, int FailureLimit, RecordEnvelope? FailureTrigger,
        IReadOnlyList<Trigger> Triggers, IReadOnlyDictionary<string, DateTimeOffset?> Next, IReadOnlyList<OccurrenceState> Occurrences)
    {
        /// <summary>Its occurrences whose result awaits review.</summary>
        public int Awaiting { get; init; }

        /// <summary>The results awaiting review that defer its dispatch.</summary>
        public int AwaitingCap { get; init; } = DefaultAwaitingReview;

        /// <summary>From GOAL-08: its open outputs, possibilities still open, asks still waiting and publication requests not closed.</summary>
        public int OpenOutputs { get; init; }

        /// <summary>From GOAL-08: the open outputs that defer its dispatch.</summary>
        public int OpenCap { get; init; } = StandingOutputs.DefaultOpen;

        /// <summary>The occurrences that may be carried now, oldest first.</summary>
        public IEnumerable<OccurrenceState> Due => Occurrences.Where(o => o.State == "due");

        /// <summary>The earliest time a schedule trigger is next due, or null.</summary>
        public DateTimeOffset? NextAt => Next.Values.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty().Min() is { } min && min != default ? min : null;
    }

    /// <summary>A goal's plan.standing, or undefined when its plan carries none.</summary>
    public static JsonElement Plan(RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return TJson.Object(TJson.Object(goal.Body, "plan"), "standing") is { ValueKind: JsonValueKind.Object } standing ? standing : default;
    }

    /// <summary>True when a goal is a standing agent: its plan carries standing.</summary>
    public static bool IsStanding(RecordEnvelope goal) => Plan(goal).ValueKind == JsonValueKind.Object;

    /// <summary>The exact flow pin a standing plan names, or null.</summary>
    public static TemplatePin? Pin(RecordEnvelope goal)
    {
        JsonElement pin = TJson.Object(Plan(goal), "pin");
        return TJson.Text(pin, "id") is { } id && TJson.Text(pin, "version") is { } version && TJson.Text(pin, "hash") is { } hash ? new(id, version, hash) : null;
    }

    /// <summary>The occurrence key an assignment names, or null.</summary>
    public static string? KeyOf(RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return TJson.Text(TJson.Object(assignment.Body, "occurrence"), "key");
    }

    /// <summary>The standing agent a work order is the work of, as of a record when one is given; null when it is the work of none.</summary>
    public static RecordEnvelope? StandingOf(EstateModel model, RecordEnvelope order, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return Routing.GoalsOf(model, order, before).FirstOrDefault(IsStanding);
    }

    /// <summary>Each loaded model's resolved standing flows, by exact pin.</summary>
    private static readonly ConditionalWeakTable<EstateModel, Dictionary<TemplatePin, (FlowDefinition? Flow, string? Unavailable)>> Resolved = [];

    /// <summary>The flow a standing plan names, resolved with its bases, or why it cannot be here; a reader without it shows the goal and dispatches none of it.</summary>
    public static (FlowDefinition? Flow, string? Unavailable) Resolve(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (Pin(goal) is not TemplatePin pin)
        {
            return (null, "the goal's plan names no standing flow");
        }

        Dictionary<TemplatePin, (FlowDefinition? Flow, string? Unavailable)> flows = Resolved.GetValue(model, _ => []);
        lock (flows)
        {
            if (flows.TryGetValue(pin, out (FlowDefinition? Flow, string? Unavailable) known))
            {
                return known;
            }
        }

        (FlowDefinition? Flow, string? Unavailable) resolved;
        try
        {
            resolved = (FlowDefinition.Resolve(model, pin), null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            resolved = (null, $"flow {pin.Id} {pin.Version} at {pin.Hash} does not resolve here: {error.Message}");
        }

        lock (flows)
        {
            flows[pin] = resolved;
        }

        return resolved;
    }

    /// <summary>
    /// The step each occurrence carries: the flow's one routed step, of kind work. A standing flow in this release carries an occurrence
    /// as one assignment; null when the flow has no such single step.
    /// </summary>
    public static FlowStep? WorkStep(FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        List<FlowStep> routed = flow.Steps.Where(s => s.IsRouted).ToList();
        return routed is [{ Kind: "work" } only] ? only : null;
    }

    /// <summary>The goal's until time, or null.</summary>
    public static DateTimeOffset? Until(RecordEnvelope goal) => Time(TJson.Text(Plan(goal), "until"));

    /// <summary>The consecutive failed runs that pause a standing agent: its goal's limits.consecutiveFailures, or three.</summary>
    public static int FailureLimit(RecordEnvelope goal) =>
        TJson.Object(goal.Body, "limits") is { ValueKind: JsonValueKind.Object } limits && limits.TryGetProperty("consecutiveFailures", out JsonElement limit) && limit.ValueKind == JsonValueKind.Number && limit.TryGetInt32(out int allowed) && allowed > 0
            ? allowed : DefaultConsecutiveFailures;

    /// <summary>
    /// The triggers of a goal's current revision, each with the earliest revision from which its declaration is unchanged, so a renewal
    /// that keeps a trigger keeps its keys and nothing is carried twice, while a changed trigger starts its own keys.
    /// </summary>
    public static IReadOnlyList<Trigger> Triggers(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        List<RecordEnvelope> revisions = model.ById.ContainsKey(goal.Id)
            ? model.Chain(goal).TakeWhile(id => id != goal.Id).Select(id => model.ById[id]).Append(goal).ToList()
            : [goal];
        var triggers = new List<Trigger>();
        foreach (JsonElement declared in TJson.Array(Plan(goal), "triggers"))
        {
            if (TJson.Text(declared, "key") is not string key || TJson.Text(declared, "kind") is not string kind)
            {
                continue;
            }

            string canonical = CanonicalJson.Serialize(declared);
            string origin = goal.Id;
            for (int i = revisions.Count - 2; i >= 0; i--)
            {
                JsonElement earlier = TJson.Array(Plan(revisions[i]), "triggers").FirstOrDefault(t => TJson.Text(t, "key") == key);
                if (earlier.ValueKind != JsonValueKind.Object || CanonicalJson.Serialize(earlier) != canonical)
                {
                    break;
                }

                origin = revisions[i].Id;
            }

            triggers.Add(new Trigger(key, kind, declared, origin));
        }

        return triggers;
    }

    /// <summary>An occurrence key: the goal's identity, the trigger, its revision and the item; a duty's keeps the duty occurrence's own identity.</summary>
    public static string Key(string goal, Trigger trigger, string item)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Kind == "duty" ? "duty/" + item : $"{goal}/{trigger.Key}/{trigger.Revision}/{item}";
    }

    /// <summary>The item's part of a key, from the item an assignment records; null when the item does not fit its kind.</summary>
    public static string? ItemKey(string kind, JsonElement item) => kind switch
    {
        "schedule" => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("interval", out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out long number) ? "interval-" + number.ToString(CultureInfo.InvariantCulture) : null,
        "event" => TJson.Object(item, "record") is { ValueKind: JsonValueKind.Object } record && TJson.Text(record, "id") is string id && TJson.Text(record, "hash") is string hash ? $"record-{id}@{hash}" : null,
        "condition" => TJson.Text(item, "failing") is string failing ? "failing-" + failing : null,
        "request" => TJson.Text(item, "at") is string at && TJson.Text(item, "by") is string by ? $"request-{at}-{by}" : null,
        "duty" => TJson.Text(item, "occurrence"),
        _ => null,
    };

    /// <summary>The UTC-anchored period that holds a time: calendar months, or days counted from 1970-01-01, every so many of them.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) PeriodAt(string unit, int every, DateTimeOffset at)
    {
        DateTimeOffset utc = at.ToUniversalTime();
        every = Math.Max(every, 1);
        if (unit == "days")
        {
            long day = (long)Math.Floor((utc - DateTimeOffset.UnixEpoch).TotalDays);
            long first = day - (((day % every) + every) % every);
            DateTimeOffset start = DateTimeOffset.UnixEpoch.AddDays(first);
            return (start, start.AddDays(every));
        }

        int month = (utc.Year * 12) + utc.Month - 1;
        int anchored = month - (month % every);
        var begin = new DateTimeOffset(anchored / 12, (anchored % 12) + 1, 1, 0, 0, 0, TimeSpan.Zero);
        return (begin, begin.AddMonths(every));
    }

    /// <summary>A schedule trigger's tick: the anchor plus the interval number times its every, in days of 86,400 seconds or calendar months, the last day of a shorter month standing for the anchor's.</summary>
    public static DateTimeOffset? Tick(JsonElement trigger, long number)
    {
        if (number < 0 || Time(TJson.Text(trigger, "firstAt")) is not { } anchor || !trigger.TryGetProperty("every", out JsonElement e) || e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long every) || every <= 0)
        {
            return null;
        }

        try
        {
            long count = checked(number * every);
            return TJson.Text(trigger, "unit") switch
            {
                "months" when count <= 120000 => anchor.AddMonths((int)count),
                "days" when count <= 3652059 => anchor.AddDays(count),
                _ => null,
            };
        }
        catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>The latest tick of a schedule trigger due at or before a time, or -1 when none is.</summary>
    public static long LatestTick(JsonElement trigger, DateTimeOffset asOf)
    {
        if (Tick(trigger, 0) is not { } first || first > asOf)
        {
            return -1;
        }

        long every = trigger.GetProperty("every").GetInt64();
        long estimate = TJson.Text(trigger, "unit") == "days"
            ? (long)Math.Floor((asOf - first).TotalDays / every)
            : (((asOf.Year - first.Year) * 12) + asOf.Month - first.Month) / every;
        long number = Math.Max(estimate + 1, 0);
        while (number > 0 && (Tick(trigger, number) is not { } due || due > asOf))
        {
            number--;
        }

        return number;
    }

    /// <summary>
    /// A standing agent as of an explicit UTC time: its triggers evaluated from the records, each occurrence's state from the assignment
    /// that holds its key, and the gates of § 10 (until, the failure pause, the period's caps and the concurrency) over what is due. A
    /// duty trigger reads the duty occurrences the runtime's policy evaluation owes; without them its occurrences are those already carried.
    /// </summary>
    public static Agent Derive(EstateModel model, RecordEnvelope goal, DateTimeOffset asOf, IReadOnlyList<DutyDue>? duties = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        RecordEnvelope current = model.Latest(goal);
        string identity = model.Chain(current)[0];
        string status = GoalRules.Status(model, current);
        (FlowDefinition? flow, string? unavailable) = Resolve(model, current);
        FlowStep? step = flow is null ? null : WorkStep(flow);
        RecordEnvelope? order = FlowRuns.Work(model, current);
        DateTimeOffset? until = Until(current);
        JsonElement plan = Plan(current);
        IReadOnlyList<Trigger> triggers = Triggers(model, current);
        List<RecordEnvelope> assignments = order is null ? [] : Routing.Assignments(model, order).Where(a => KeyOf(a) is not null).ToList();
        Dictionary<string, List<RecordEnvelope>> byKey = assignments.GroupBy(a => KeyOf(a)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        List<AgentRuns.Run> runs = AgentRuns.All(model).Where(r => r.Goal is not null && model.Chain(current).Contains(r.Goal)).ToList();
        HashSet<string> own = OwnAuthors(model, current, assignments, step);

        var occurrences = new List<Occurrence>();
        var next = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Trigger trigger in triggers)
        {
            List<Occurrence> found = trigger.Kind switch
            {
                "schedule" => Schedule(identity, trigger, asOf, next),
                "event" => Events(model, current, identity, trigger, asOf, own),
                "condition" => Condition(model, identity, trigger, asOf),
                "duty" => Duties(trigger, duties, asOf),
                _ => [],
            };
            if (!next.ContainsKey(trigger.Key))
            {
                next[trigger.Key] = null;
            }

            foreach (Occurrence occurrence in found.Where(o => seen.Add(o.Key)))
            {
                occurrences.Add(occurrence);
            }
        }

        // The occurrences already carried, each from the item its assignment records, whatever the triggers would evaluate now.
        foreach ((string key, List<RecordEnvelope> holders) in byKey.Where(k => !seen.Contains(k.Key)))
        {
            JsonElement named = TJson.Object(holders[0].Body, "occurrence");
            string kind = triggers.FirstOrDefault(t => t.Key == TJson.Text(named, "trigger"))?.Kind ?? (key.StartsWith("duty/", StringComparison.Ordinal) ? "duty" : "?");
            JsonElement item = TJson.Object(named, "item").Clone();
            DateTimeOffset due = Time(TJson.Text(item, "dueAt")) ?? Time(TJson.Text(item, "at")) ?? holders[0].AtTime;
            occurrences.Add(new Occurrence(key, TJson.Text(named, "trigger") ?? "?", kind, TJson.Text(named, "revision") ?? "?", item, due, [], $"carried by assignment {holders[0].Id}"));
            seen.Add(key);
        }

        occurrences = occurrences.OrderBy(o => o.DueAt).ThenBy(o => o.Key, StringComparer.Ordinal).ToList();

        // The gates over what is due: the goal's status, until, the failure pause, the period's caps and the concurrency.
        AgentRuns.Ledger ledger = AgentRuns.Of(model, current);
        Period? period = PeriodOf(model, current, runs, asOf);
        (int failures, RecordEnvelope? failure) = Failures(model, current, runs);
        int awaitingCap = AwaitingReviewCap(current);
        int awaiting = AwaitingCount(model, order, null);
        int openCap = StandingOutputs.CapsOf(current).Open;
        int open = StandingOutputs.OpenCount(model, current);
        int limit = FailureLimit(current);
        int inFlight = runs.Count(r => r.InFlight);
        (string state, string reason) = status switch
        {
            "active" when flow is null => ("unavailable", $"the goal is shown and none of it is dispatched here: its {unavailable}"),
            "active" when step is null => ("unavailable", $"flow {flow!.Pin.Id} declares no single routed work step, which each occurrence carries as one assignment"),
            "active" when order is null => ("unavailable", "its work order is missing"),
            "active" when until is { } u && asOf >= u => ("lapsed", $"its until time {RecordParser.FormatTimestamp(u)} has passed: it returns to its sponsor, and a renewal is a goal revision its sponsor activates"),
            "active" when failures >= limit => ("held", $"{failures.ToString(CultureInfo.InvariantCulture)} of its runs failed in a row, its limit of {limit.ToString(CultureInfo.InvariantCulture)}: it is paused until its sponsor answers the ask about it"),
            "active" => ("active", "its occurrences are carried one per goal step, within its caps"),
            "abandoned" => ("stopped", "the goal is abandoned: nothing of it is dispatched again"),
            "paused" => ("paused", "the goal is paused: nothing new is dispatched, and duty occurrences stay owed"),
            "proposed" => ("proposed", "the goal is proposed: nothing is dispatched until its sponsor activates it"),
            _ => (status, $"the goal is {status}: nothing of it is dispatched"),
        };

        var states = new List<OccurrenceState>();
        foreach (Occurrence occurrence in occurrences)
        {
            states.Add(StateOf(model, ledger, occurrence, byKey.GetValueOrDefault(occurrence.Key), runs, state, reason, period, inFlight, awaiting, awaitingCap, open, openCap));
        }

        return new Agent(current, status, state, reason, asOf, flow, step, unavailable, order, until, TJson.Text(plan, "runsOn"), period, inFlight, ledger.Concurrency, failures, limit, failure, triggers, next, states)
        {
            Awaiting = awaiting,
            AwaitingCap = awaitingCap,
            OpenOutputs = open,
            OpenCap = openCap,
        };
    }

    /// <summary>The principals whose records never trigger the agent itself: the contributors of its occurrences and the agents its role names.</summary>
    private static HashSet<string> OwnAuthors(EstateModel model, RecordEnvelope goal, IReadOnlyList<RecordEnvelope> assignments, FlowStep? step)
    {
        var own = new HashSet<string>(assignments.Select(a => EstateModel.BodyString(a, "contributor")).OfType<string>(), StringComparer.Ordinal);
        if (step is not null)
        {
            own.UnionWith(Routing.Principals(model, goal, step.Role).Principals.Where(p => !GoalRules.IsPerson(model, p)));
        }

        return own;
    }

    /// <summary>A schedule trigger's occurrence: its latest tick due by the time, earlier unclaimed ticks coalescing into it, and when it is next due.</summary>
    private static List<Occurrence> Schedule(string goal, Trigger trigger, DateTimeOffset asOf, Dictionary<string, DateTimeOffset?> next)
    {
        long latest = LatestTick(trigger.Declaration, asOf);
        next[trigger.Key] = Tick(trigger.Declaration, latest + 1);
        if (latest < 0 || Tick(trigger.Declaration, latest) is not { } due)
        {
            return [];
        }

        JsonElement item = JsonSerializer.SerializeToElement(new { interval = latest, dueAt = RecordParser.FormatTimestamp(due) });
        return [new Occurrence(Key(goal, trigger, ItemKey("schedule", item)!), trigger.Key, "schedule", trigger.Revision, item, due, [], $"interval {latest.ToString(CultureInfo.InvariantCulture)} of schedule {trigger.Key}, due at {RecordParser.FormatTimestamp(due)}")];
    }

    /// <summary>
    /// An event trigger's occurrences: each record of its type and fields recorded after the trigger's revision was activated and by the
    /// time, never the agent's own; a record an agent wrote counts only for a trigger naming that agent's standing role, and only from an
    /// agent no agent's records trigger, so chains end at depth two.
    /// </summary>
    private static List<Occurrence> Events(EstateModel model, RecordEnvelope goal, string identity, Trigger trigger, DateTimeOffset asOf, HashSet<string> own)
    {
        string? type = TJson.Text(trigger.Declaration, "recordType");
        if (type is null || TriggerActivation(model, goal, trigger) is not { } activated)
        {
            return [];
        }

        IReadOnlyList<string> roles = TJson.Strings(trigger.Declaration, "roles").ToList();
        List<(string Name, string Value)> fields = TJson.Object(trigger.Declaration, "fields") is { ValueKind: JsonValueKind.Object } named
            ? named.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => (p.Name, p.Value.GetString()!)).ToList()
            : [];
        var found = new List<Occurrence>();
        foreach (RecordEnvelope record in model.OfType(type).Where(r => r.AtTime > activated && r.AtTime <= asOf).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            if (own.Contains(record.By) || !fields.All(f => GoalPredicates.FieldString(record, f.Name) == f.Value) || !Admitted(model, record, roles))
            {
                continue;
            }

            JsonElement item = JsonSerializer.SerializeToElement(new { record = new { type = record.Type, id = record.Id, hash = record.ComputedHash } });
            found.Add(new Occurrence(Key(identity, trigger, ItemKey("event", item)!), trigger.Key, "event", trigger.Revision, item, record.AtTime, [record.Reference], $"{record.Type} {record.Id}, recorded by {Routing.NameOf(model, record.By)}"));
            if (found.Count >= MaximumOccurrences)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>Whether a record may trigger: a person's always; an agent's only when the trigger names the agent's standing role and that agent is triggered by no agent's records.</summary>
    private static bool Admitted(EstateModel model, RecordEnvelope record, IReadOnlyList<string> roles)
    {
        if (GoalRules.IsPerson(model, record.By))
        {
            return true;
        }

        foreach (RecordEnvelope other in model.Current("goal").Where(IsStanding))
        {
            if (Resolve(model, other).Flow is not FlowDefinition flow || WorkStep(flow) is not FlowStep step || !roles.Contains(step.Role, StringComparer.Ordinal))
            {
                continue;
            }

            RecordEnvelope? work = FlowRuns.Work(model, other);
            bool carries = work is not null && Routing.Assignments(model, work).Any(a => KeyOf(a) is not null && EstateModel.BodyString(a, "contributor") == record.By);
            bool chained = TJson.Array(Plan(other), "triggers").Any(t => TJson.Text(t, "kind") == "event" && TJson.Strings(t, "roles").Any());
            if (carries && !chained)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>When a trigger began to count records: the first activation of a goal revision from the trigger's revision on; null before one.</summary>
    private static DateTimeOffset? TriggerActivation(EstateModel model, RecordEnvelope goal, Trigger trigger)
    {
        IReadOnlyList<string> chain = model.Chain(goal);
        int from = chain.ToList().IndexOf(trigger.Revision);
        if (from < 0)
        {
            from = chain.Count - 1;
        }

        return chain.Skip(from).Select(id => model.ById[id])
            .Select(r => GoalRules.Statuses(model, r).FirstOrDefault(s => EstateModel.BodyString(s, "status") == "active"))
            .OfType<RecordEnvelope>().Select(s => (DateTimeOffset?)s.AtTime).FirstOrDefault();
    }

    /// <summary>A condition trigger's occurrence: once for each new set of records its predicate read while it fails, keyed by that set's hash.</summary>
    private static List<Occurrence> Condition(EstateModel model, string identity, Trigger trigger, DateTimeOffset asOf)
    {
        JsonElement predicate = TJson.Object(trigger.Declaration, "record");
        if (predicate.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        GoalPredicates.Verdict verdict = GoalPredicates.EvaluatePredicate(model, predicate, asOf);
        if (verdict.Value != "not-met")
        {
            return [];
        }

        string set = string.Join("\n", verdict.Read.Select(r => r.Id + " " + r.Hash).Order(StringComparer.Ordinal));
        string failing = ContentHash.Sha256(Encoding.UTF8.GetBytes(set));
        DateTimeOffset due = verdict.Read.Select(r => model.ById.TryGetValue(r.Id, out RecordEnvelope? record) ? record.AtTime : DateTimeOffset.MinValue).DefaultIfEmpty(asOf).Max();
        if (due == DateTimeOffset.MinValue || due > asOf)
        {
            due = asOf;
        }

        JsonElement item = JsonSerializer.SerializeToElement(new { failing, records = verdict.Read.Count });
        return [new Occurrence(Key(identity, trigger, ItemKey("condition", item)!), trigger.Key, "condition", trigger.Revision, item, due, verdict.Read.Take(20).ToList(), $"condition {trigger.Key} fails: {verdict.Reason}")];
    }

    /// <summary>A duty trigger's occurrences: each occurrence of its policy and rule that the runtime's evaluation owes by the time, missed ones still owed.</summary>
    private static List<Occurrence> Duties(Trigger trigger, IReadOnlyList<DutyDue>? duties, DateTimeOffset asOf)
    {
        string? policy = TJson.Text(trigger.Declaration, "policy");
        string? rule = TJson.Text(trigger.Declaration, "rule");
        return (duties ?? []).Where(d => d.Policy == policy && d.Rule == rule && d.DueAt <= asOf).Take(MaximumOccurrences).Select(d =>
        {
            JsonElement item = JsonSerializer.SerializeToElement(new
            {
                occurrence = d.Identity, policy = new { type = d.Origin.Type, id = d.Origin.Id, hash = d.Origin.Hash }, rule = d.Rule, dueAt = RecordParser.FormatTimestamp(d.DueAt), deadline = RecordParser.FormatTimestamp(d.Deadline), schedule = d.Item,
            });
            return new Occurrence(Key("", trigger, d.Identity), trigger.Key, "duty", trigger.Revision, item, d.DueAt, [d.Origin], $"duty occurrence {d.Identity}, due at {RecordParser.FormatTimestamp(d.DueAt)}, deadline {RecordParser.FormatTimestamp(d.Deadline)}");
        }).ToList();
    }

    /// <summary>A request trigger's occurrence for one caller at one time.</summary>
    public static Occurrence Request(string goal, Trigger trigger, string by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        JsonElement item = JsonSerializer.SerializeToElement(new { at = RecordParser.FormatTimestamp(at), by });
        return new Occurrence(Key(goal, trigger, ItemKey("request", item)!), trigger.Key, "request", trigger.Revision, item, at, [], $"a request by {by} at {RecordParser.FormatTimestamp(at)}");
    }

    /// <summary>An occurrence's state from the assignment that holds its key, its runs and its result, then the agent's gates.</summary>
    private static OccurrenceState StateOf(EstateModel model, AgentRuns.Ledger ledger, Occurrence occurrence, List<RecordEnvelope>? holders, List<AgentRuns.Run> runs, string agent, string agentReason, Period? period, int inFlight, int awaiting, int awaitingCap, int open, int openCap)
    {
        RecordEnvelope? holder = holders is { Count: > 0 } ? holders[^1] : null;
        if (holder is not null)
        {
            IReadOnlyList<string> chain = holders!.SelectMany(model.Chain).ToList();
            AgentRuns.Run? run = runs.LastOrDefault(r => r.Assignment is not null && chain.Contains(r.Assignment));
            // The occurrence's latest result, on whichever assignment of its key recorded it, so a revision or a withdrawal unsettles nothing.
            RecordEnvelope? result = LatestResult(model, occurrence.Key, null);
            string contributor = Routing.NameOf(model, EstateModel.BodyString(holder, "contributor"));
            if (run is { InFlight: true })
            {
                return new(occurrence, "running", $"run {run.Id} of {contributor} on assignment {holder.Id} is in flight ({run.Status})", holder, run, result);
            }

            if (result is not null && EstateModel.BodyString(result, "outcome") is "blocked" or "abandoned")
            {
                if (AgentRuns.Exhausted(model, ledger, holder.Id).FirstOrDefault(r => r.StartsWith("The step has had", StringComparison.Ordinal)) is string used)
                {
                    return new(occurrence, "held", $"its result {result.Id} is {EstateModel.BodyString(result, "outcome")}, assignment {holder.Id} holds it and {used} Its sponsor decides: a person takes it over (vericairn work takeover {holder.Id} --by <person>)", holder, run, result);
                }

                return Gate(new(occurrence, "due", $"its result {result.Id} on assignment {holder.Id} is {EstateModel.BodyString(result, "outcome")}: the assignment goes on with another run, and the failure pause counts it", holder, run, result));
            }

            if (result is not null)
            {
                RecordEnvelope? returned = Returned(model, result);
                if (returned is null)
                {
                    bool disposed = WorkView.ResultState(model, result) == "accepted" || Passed(model, result);
                    return new(occurrence, disposed ? "disposed" : "settled", disposed
                        ? $"its result {result.Id} is reviewed"
                        : $"its result {result.Id} on assignment {holder.Id} waits for review; it reruns only when a review returns it", holder, run, result);
                }

                return Gate(new(occurrence, "due", $"review {returned.Id} returned its result {result.Id}: assignment {holder.Id} goes on with another attempt", holder, run, result));
            }

            if (GoalRules.IsPerson(model, EstateModel.BodyString(holder, "contributor")))
            {
                return new(occurrence, "assigned", $"assigned to {contributor} by assignment {holder.Id}", holder, run, null);
            }

            if (AgentRuns.Exhausted(model, ledger, holder.Id).FirstOrDefault(r => r.StartsWith("The step has had", StringComparison.Ordinal)) is string spent)
            {
                return new(occurrence, "held", $"assignment {holder.Id} holds it and {spent} Its sponsor decides: a person takes it over (vericairn work takeover {holder.Id} --by <person>)", holder, run, null);
            }

            return Gate(new(occurrence, "due", run is null ? $"assignment {holder.Id} holds it and goes on with a run" : $"assignment {holder.Id} holds it and goes on with another run after run {run.Id} ended {run.Status}", holder, run, null));
        }

        // No current assignment holds it, yet a result on one withdrawn since settles it: it stays settled, and nothing carries it again.
        if (Settled(model, occurrence.Key, null) is RecordEnvelope settled)
        {
            bool reviewed = WorkView.ResultState(model, settled) == "accepted" || Passed(model, settled);
            return new(occurrence, reviewed ? "disposed" : "settled", reviewed ? $"its result {settled.Id} is reviewed" : $"its result {settled.Id} waits for review; it reruns only when a review returns it", null, null, settled);
        }

        return Gate(new(occurrence, "due", occurrence.Summary, null, null, null));

        OccurrenceState Gate(OccurrenceState due) => Gated(agent, agentReason, period, inFlight, ledger.Concurrency, awaiting, awaitingCap, due, open, openCap);
    }

    /// <summary>An occurrence the agent's gates defer: anything but an active agent, a period whose caps are reached, or runs in flight at its concurrency.</summary>
    private static OccurrenceState Gated(string agent, string agentReason, Period? period, int inFlight, int concurrency, int awaiting, int awaitingCap, OccurrenceState due, int open = 0, int openCap = int.MaxValue)
    {
        if (agent != "active")
        {
            return due with { State = "deferred", Reason = $"{due.Reason}; deferred: {agentReason}" };
        }

        if (period is not null && period.Reached.Count > 0)
        {
            return due with { State = "deferred", Reason = $"{due.Reason}; deferred: the period from {RecordParser.FormatTimestamp(period.Start)} to {RecordParser.FormatTimestamp(period.End)} has reached its caps ({string.Join(", ", period.Reached)})" };
        }

        if (awaiting >= awaitingCap)
        {
            return due with { State = "deferred", Reason = $"{due.Reason}; deferred: {awaiting.ToString(CultureInfo.InvariantCulture)} of its results await review, its cap of {awaitingCap.ToString(CultureInfo.InvariantCulture)}" };
        }

        // From GOAL-08: open outputs at the plan's cap defer dispatch until another principal disposes of some (§ 10).
        if (open >= openCap)
        {
            return due with { State = "deferred", Reason = $"{due.Reason}; deferred: {open.ToString(CultureInfo.InvariantCulture)} of its outputs are open, its cap of {openCap.ToString(CultureInfo.InvariantCulture)}" };
        }

        if (inFlight >= concurrency)
        {
            return due with { State = "deferred", Reason = $"{due.Reason}; waits: {inFlight.ToString(CultureInfo.InvariantCulture)} of its runs in flight, its concurrency of {concurrency.ToString(CultureInfo.InvariantCulture)}" };
        }

        return due;
    }

    /// <summary>The agent with one more occurrence, a request made now, gated as every due one.</summary>
    public static Agent With(Agent agent, Occurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(occurrence);
        OccurrenceState state = Gated(agent.State, agent.Reason, agent.Period, agent.InFlight, agent.Concurrency, agent.Awaiting, agent.AwaitingCap, new OccurrenceState(occurrence, "due", occurrence.Summary, null, null, null), agent.OpenOutputs, agent.OpenCap);
        return agent with { Occurrences = [.. agent.Occurrences, state] };
    }

    /// <summary>The latest review by another principal than its author that returns a result, unless a later acceptance or passing review stands.</summary>
    private static RecordEnvelope? Returned(EstateModel model, RecordEnvelope result)
    {
        IReadOnlyList<string> chain = model.Chain(result);
        RecordEnvelope? latest = model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && r.By != result.By)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        return latest is not null && EstateModel.BodyString(latest, "outcome") is "findings" or "fail" && WorkView.ResultState(model, result) == "awaiting review" ? latest : null;
    }

    /// <summary>True when the latest review of a result by another principal than its author passes it.</summary>
    private static bool Passed(EstateModel model, RecordEnvelope result)
    {
        IReadOnlyList<string> chain = model.Chain(result);
        RecordEnvelope? latest = model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && r.By != result.By)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        return latest is not null && EstateModel.BodyString(latest, "outcome") == "pass";
    }

    /// <summary>The period that holds a time, its caps and what the goal's runs started in it were charged; null for a plan with no period.</summary>
    public static Period? PeriodOf(EstateModel model, RecordEnvelope goal, IReadOnlyList<AgentRuns.Run> runs, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runs);
        JsonElement spec = TJson.Object(Plan(goal), "period");
        if (spec.ValueKind != JsonValueKind.Object || TJson.Text(spec, "unit") is not string unit)
        {
            return null;
        }

        int every = spec.TryGetProperty("every", out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int n) && n > 0 ? n : 1;
        (DateTimeOffset start, DateTimeOffset end) = PeriodAt(unit, every, at);
        var caps = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string name in new[] { "runs", "wallMinutes", "tokens", "currency" })
        {
            if (AgentRuns.Amount(spec, name) is long cap)
            {
                caps[name] = cap;
            }
        }

        var spent = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (AgentRuns.Run run in runs.Where(r => r.Charged && r.Intent.AtTime >= start && r.Intent.AtTime < end))
        {
            spent["runs"] = spent.GetValueOrDefault("runs") + 1;
            foreach ((string name, long value) in AgentRuns.ChargeOf(run))
            {
                spent[name] = spent.GetValueOrDefault(name) + value;
            }
        }

        return new Period(unit, every, start, end, caps, spent, AgentRuns.CurrencyUnit(spec));
    }

    /// <summary>
    /// The goal's consecutive failed runs that count toward the failure pause, judged from the records before a record when one is given,
    /// read so that no record of an agent tied to the goal ends the pause, whatever its date (§ 10; from 0.9.4, FIX-155). Each settled run
    /// has one outcome. It failed when a blocked or abandoned result was recorded on its assignment after it started and before the next run
    /// of that assignment started, any such result whatever a later one or a correction says, or when it settled as a known failure unless
    /// who decides for the goal asked it to stop (<see cref="DeciderStopped"/>). It succeeded when it settled as an observed success with no
    /// such result and no principal tied to the goal recorded its intent or its settlement. Otherwise it counts for nothing. A completed or
    /// partial result, and the attempt it closes, is a claim another principal reviews and never makes a run succeed. A run no tied
    /// principal recorded ends at the next run whose intent no tied principal recorded, so no run a tied principal records takes a result
    /// off another. The outcomes count in the order the runs settled, each at the later of its intent and its settlement. An answered ask of
    /// the pause naming one of the failures cuts every outcome settled before the earlier of the ask and its first answer
    /// (<see cref="Answer"/>); after the cut a success ends the failures before it only while they are fewer than the limit, so once they
    /// reach it the pause holds until such an answer. Returns the failures that count and the latest of them.
    /// </summary>
    private static (int Count, RecordEnvelope? Latest) Failures(EstateModel model, RecordEnvelope goal, IReadOnlyList<AgentRuns.Run> runs, RecordEnvelope? before = null)
    {
        bool Counts(RecordEnvelope record) => before is null || GoalRules.Before(record, before);
        bool Tied(string by) => StandingOutputs.Tied(model, goal, by, before);
        var outcomes = new List<(RecordEnvelope At, RecordEnvelope Record, bool Failed, IReadOnlyList<RecordEnvelope> Records)>();
        foreach (AgentRuns.Run run in runs.Where(r => r.IsSettled && Counts(r.Latest)))
        {
            RecordEnvelope settlement = run.Revisions.First(r => AgentRuns.Settled.Contains(EstateModel.BodyString(r, "status") ?? string.Empty));
            bool tied = Tied(run.Intent.By) || Tied(settlement.By);
            RecordEnvelope? carried = run.Assignment is string id && model.ById.TryGetValue(id, out RecordEnvelope? found) ? model.Latest(found) : null;
            IReadOnlyList<string> chain = carried is null ? [] : model.Chain(carried);
            AgentRuns.Run? next = runs.Where(r => r.Assignment is not null && chain.Contains(r.Assignment) && GoalRules.Before(run.Intent, r.Intent) && (!Tied(r.Intent.By) || (tied && r.IsSettled)))
                .OrderBy(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Intent.Id, StringComparer.Ordinal).FirstOrDefault();

            // Every result of every attempt on the assignment, superseded attempts and results among them, so no revision takes a failure back.
            List<RecordEnvelope> failed = [.. chain.SelectMany(a => model.About(a, "attempt")).SelectMany(attempt => model.About(attempt.Id, "result")).DistinctBy(r => r.Id)
                .Where(r => Counts(r) && GoalRules.Before(run.Intent, r) && (next is null || GoalRules.Before(r, next.Intent)) && EstateModel.BodyString(r, "outcome") is "blocked" or "abandoned")
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
            RecordEnvelope at = GoalRules.Before(run.Intent, settlement) ? settlement : run.Intent;
            if (failed.Count > 0)
            {
                outcomes.Add((at, failed[^1], true, [run.Latest, .. failed]));
            }
            else if (run.Status == "known-failure" && !DeciderStopped(model, goal, run, before))
            {
                outcomes.Add((at, run.Latest, true, [run.Latest]));
            }
            else if (run.Status == "observed-success" && !tied)
            {
                outcomes.Add((at, run.Latest, false, [run.Latest]));
            }
        }

        outcomes = [.. outcomes.OrderBy(o => o.At.At, StringComparer.Ordinal).ThenBy(o => o.At.Id, StringComparer.Ordinal)];

        // The sponsor's answer lets the agent go on. From GOAL-08 only the pause's ask goal step recorded counts, never another principal's
        // ask in its words, and only an answer no agent tied to the goal recorded (Asks.State leaves those out, StandingOutputs.SelfAnswer),
        // whatever date it claims; judged before a record, such as a run's intent, by the ties written before it, so a later tie makes no
        // earlier record wrong. From FIX-155 how far it reaches is read from the ask and its answer, never from where a result falls.
        int cut = -1;
        foreach (RecordEnvelope ask in Routing.AsksOf(model, goal).Where(a => Counts(a) && RuntimeAsk(model, goal, a)
            && (EstateModel.BodyString(model.Latest(a), "question") ?? string.Empty).StartsWith(NextEligible.AttentionQuestion, StringComparison.Ordinal)))
        {
            if (model.Latest(ask).Subject is not RecordReference subject || Answer(model, ask, before) is not RecordEnvelope answer)
            {
                continue;
            }

            RecordEnvelope put = StandingOutputs.FirstOf(model, ask);
            RecordEnvelope bound = GoalRules.Before(put, answer) ? put : answer;
            int last = outcomes.FindLastIndex(o => GoalRules.Before(o.At, bound));
            if (last > cut && outcomes.Take(last + 1).Any(o => o.Failed && o.Records.Any(r => model.Chain(r).Contains(subject.Id))))
            {
                cut = last;
            }
        }

        int count = 0;
        int limit = FailureLimit(goal);
        RecordEnvelope? latest = null;
        foreach ((_, RecordEnvelope record, bool failure, _) in outcomes.Skip(cut + 1))
        {
            if (failure)
            {
                count++;
                latest = record;
            }
            else if (count < limit)
            {
                count = 0;
                latest = null;
            }
        }

        return (count, latest);
    }

    /// <summary>
    /// The first answer of an ask, as <see cref="Asks.State"/> reads it now; before a record, the first answer recorded before it that
    /// counted then (<see cref="StandingOutputs.Counting"/> as of it), while its asker has not withdrawn it. Null while none answers it.
    /// </summary>
    private static RecordEnvelope? Answer(EstateModel model, RecordEnvelope ask, RecordEnvelope? before)
    {
        if (before is null)
        {
            (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, ask);
            return state == "answered" ? answers[0] : null;
        }

        RecordEnvelope latest = model.Latest(ask);
        return EstateModel.BodyString(latest, "state") == "withdrawn" ? null
            : StandingOutputs.Counting(model, latest, model.DecisionsAnswering(model.Chain(latest)), before).Where(d => GoalRules.Before(d, before))
                .OrderBy(d => d.At, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// True when who decides for the goal asked a run to stop, read as the records stood when the stop was first recorded: the sponsor its
    /// goal revision then names, when a person, or a principal holding the operator role then (goal pause --stop-runs, goal stop, an
    /// operator's run stop). A stop anyone else requested, the dispatcher that started the run included, and the worker's for a cap the
    /// run reached, leave its known failure the agent's; and from FIX-155 so does a stop first recorded in a revision a principal tied to
    /// the goal wrote, whoever it names, judged before a record by the ties written before it.
    /// </summary>
    private static bool DeciderStopped(EstateModel model, RecordEnvelope goal, AgentRuns.Run run, RecordEnvelope? before)
    {
        if (TJson.Text(TJson.Object(run.Latest.Body, "stop"), "requestedBy") is not string by || by == "worker"
            || run.Revisions.FirstOrDefault(r => TJson.Text(TJson.Object(r.Body, "stop"), "requestedBy") == by) is not RecordEnvelope stopped
            || StandingOutputs.Tied(model, goal, stopped.By, before))
        {
            return false;
        }

        RecordEnvelope standing = model.Chain(model.Latest(goal)).Where(model.ById.ContainsKey).Select(id => model.ById[id]).LastOrDefault(g => GoalRules.Before(g, stopped)) ?? goal;
        return GoalRules.AccepterOf(model, standing).Decides(model, by, stopped) || model.HasRoleAt(by, Roles.Operator, stopped);
    }

    /// <summary>
    /// Every assignment that names an occurrence key, in any revision, current, withdrawn by a reconciliation or stranded: a result on any
    /// of them is the occurrence's, so nothing withdrawn or revised since unsettles it.
    /// </summary>
    private static IEnumerable<RecordEnvelope> Carriers(EstateModel model, string key) =>
        model.OfType("assignment").Where(a => string.Equals(KeyOf(a), key, StringComparison.Ordinal));

    /// <summary>The latest result recorded on any assignment of an occurrence key, judged from the records before a record when one is given.</summary>
    private static RecordEnvelope? LatestResult(EstateModel model, string key, RecordEnvelope? before)
    {
        bool Counts(RecordEnvelope record) => before is null || GoalRules.Before(record, before);
        return Carriers(model, key).SelectMany(a => WorkView.AttemptsOf(model, a)).SelectMany(attempt => model.About(attempt.Id, "result")).Where(Counts)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>
    /// The result that settles the occurrence an assignment holds, judged from the records before a record when one is given: the latest
    /// result recorded on any assignment of its key, whichever holds it now or was withdrawn, completed or partial, unless a review by
    /// another principal than its author returns it (findings or fail) and no later acceptance stands; null while nothing settles it.
    /// </summary>
    internal static RecordEnvelope? Settled(EstateModel model, RecordEnvelope assignment, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        return KeyOf(assignment) is string key ? Settled(model, key, before) : null;
    }

    /// <summary>The result that settles an occurrence key, as <see cref="Settled(EstateModel, RecordEnvelope, RecordEnvelope?)"/> judges it.</summary>
    internal static RecordEnvelope? Settled(EstateModel model, string key, RecordEnvelope? before = null)
    {
        bool Counts(RecordEnvelope record) => before is null || GoalRules.Before(record, before);
        RecordEnvelope? result = LatestResult(model, key, before);
        if (result is null || EstateModel.BodyString(result, "outcome") is "blocked" or "abandoned")
        {
            // A blocked or abandoned result settles nothing: the occurrence goes on, within its attempts, and the failure pause counts it.
            return null;
        }

        IReadOnlyList<string> chain = model.Chain(result);
        RecordEnvelope? review = model.OfType("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && r.By != result.By && Counts(r))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        bool accepted = chain.SelectMany(id => model.About(id, "acceptance")).Any(Counts);
        return review is not null && EstateModel.BodyString(review, "outcome") is "findings" or "fail" && !accepted ? null : result;
    }

    /// <summary>
    /// The occurrences of a work order whose settling result awaits review, judged from the records before a record when one is given:
    /// each key once, whichever of its assignments carried the result, one withdrawn by a reconciliation included.
    /// </summary>
    private static int AwaitingCount(EstateModel model, RecordEnvelope? order, RecordEnvelope? before)
    {
        if (order is null)
        {
            return 0;
        }

        IReadOnlyList<string> chain = model.Chain(model.Latest(order));
        return model.OfType("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id) && (before is null || GoalRules.Before(a, before)))
            .Select(KeyOf).OfType<string>().Distinct(StringComparer.Ordinal)
            .Count(key => Settled(model, key, before) is RecordEnvelope result && Awaiting(model, result, before));
    }

    /// <summary>True when a settling result awaits review: no review by another principal than its author, and no acceptance, before the record.</summary>
    private static bool Awaiting(EstateModel model, RecordEnvelope result, RecordEnvelope? before)
    {
        bool Counts(RecordEnvelope record) => before is null || GoalRules.Before(record, before);
        IReadOnlyList<string> chain = model.Chain(result);
        return !model.OfType("review").Any(r => r.Subject is not null && chain.Contains(r.Subject.Id) && r.By != result.By && Counts(r))
            && !chain.SelectMany(id => model.About(id, "acceptance")).Any(Counts);
    }

    /// <summary>The results awaiting review that hold a standing agent's dispatch: its plan's awaitingReview, or three.</summary>
    public static int AwaitingReviewCap(RecordEnvelope goal) =>
        TJson.Object(Plan(goal), "awaitingReview") is { ValueKind: JsonValueKind.Number } cap && cap.TryGetInt32(out int value) && value > 0 ? value : DefaultAwaitingReview;

    /// <summary>
    /// True when a standing agent is due to ask for renewal: its until time is within three days, or half of its term since its revision
    /// was activated when that is shorter, and has not passed.
    /// </summary>
    public static bool RenewalDue(EstateModel model, RecordEnvelope goal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (Until(goal) is not { } until || now >= until
            || GoalRules.Statuses(model, goal).FirstOrDefault(s => EstateModel.BodyString(s, "status") == "active") is not RecordEnvelope activation)
        {
            return false;
        }

        TimeSpan lead = TimeSpan.FromTicks(Math.Min(RenewalLead.Ticks, Math.Max((until - activation.AtTime).Ticks / 2, 0)));
        return now >= until - lead;
    }

    /// <summary>
    /// The open ask the failure pause or a period cap raised, when one waits, so goal step records no second: one goal step recorded about
    /// the goal (<see cref="RuntimeAsk"/>), never another principal's ask in the same words.
    /// </summary>
    public static RecordEnvelope? OpenAsk(EstateModel model, RecordEnvelope goal, string prefix, DateTimeOffset? since = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return Routing.AsksOf(model, goal).Where(a => a.Subject is { Type: "goal" } && (EstateModel.BodyString(model.Latest(a), "question") ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)
                && (since is null || a.AtTime >= since) && RuntimeAsk(model, goal, a))
            .LastOrDefault(a => prefix == PeriodAsk || Asks.IsWaiting(Asks.State(model, a).State));
    }

    /// <summary>
    /// From 0.9.4 (GOAL-08): true when an ask of a standing agent's goal is one goal step recorded itself (its renewal ask, a period cap's
    /// ask or its failure pause's ask), recognized by what goal step writes and signs, never by the ask's words alone: its first revision
    /// names no raisedBy, it is put to a person, and its author took the goal's steps as the records stood when it was written, as goal
    /// step's caller must (an operator, the sponsor when a person, a principal holding local-engineering, or a dispatcher whose grant names
    /// the goal). Which of them it is the caller reads from its subject and opening words, which only such an author wrote; an agent that
    /// carries the occurrences and takes no step writes none of them, whatever its words. goal step writes and signs these asks as its
    /// caller, so an agent that carries the occurrences and may itself take the goal's steps (one holding local-engineering) writes one
    /// in their words as it could by calling goal step; telling the runtime's own apart from such an agent's would need a mark no record
    /// field carries yet. Its answer to one counts for nothing either way, since it carries the occurrences (<see cref="StandingOutputs.SelfAnswer"/>);
    /// the author of one that nothing ties to the goal records the person's decision on it.
    /// </summary>
    public static bool RuntimeAsk(EstateModel model, RecordEnvelope goal, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ask);
        RecordEnvelope first = StandingOutputs.FirstOf(model, ask);
        return StandingOutputs.Of(first) is null && GoalRules.IsPerson(model, EstateModel.BodyString(first, "to"))
            && Routing.MayTakeStep(model, model.ById.ContainsKey(goal.Id) ? model.Latest(goal) : goal, first.By, first.AtTime, first);
    }

    /// <summary>
    /// Why a run of a standing agent may not start, judged from the records before a record when one is given (§ 10): every run carries
    /// an occurrence of the goal; none starts once its until time has passed, while its failure pause holds, or once a cap of the period
    /// that holds it is reached.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> RunRefusals(EstateModel model, RecordEnvelope goal, string? assignment, RecordEnvelope? before)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var refusals = new List<(string Code, string Message)>();
        DateTimeOffset at = before?.AtTime ?? DateTimeOffset.UtcNow;
        if (assignment is not null && model.ById.TryGetValue(assignment, out RecordEnvelope? carried) && KeyOf(model.Latest(carried)) is null)
        {
            refusals.Add(("operation.standing", $"Goal {goal.Id} is a standing agent: each of its runs carries one of its occurrences, and assignment {assignment} names none."));
        }

        if (Until(goal) is { } until && at >= until)
        {
            refusals.Add(("operation.standing", $"Standing agent {goal.Id}'s until time {RecordParser.FormatTimestamp(until)} has passed: it returns to its sponsor, and a renewal is a goal revision its sponsor activates."));
        }

        List<AgentRuns.Run> runs = AgentRuns.All(model, before).Where(r => r.Goal is not null && model.Chain(model.Latest(goal)).Contains(r.Goal)).ToList();
        (int failures, _) = Failures(model, goal, runs, before);
        int limit = FailureLimit(goal);
        if (failures >= limit)
        {
            refusals.Add(("operation.standing", $"{failures.ToString(CultureInfo.InvariantCulture)} of standing agent {goal.Id}'s runs failed in a row, its limit of {limit.ToString(CultureInfo.InvariantCulture)}: no run starts until its sponsor answers the ask about it."));
        }

        // A settled occurrence runs again only when a review returns its result, on the assignment that holds it (§ 10).
        if (assignment is not null && model.ById.TryGetValue(assignment, out RecordEnvelope? holder) && Settled(model, model.Latest(holder), before) is RecordEnvelope waiting)
        {
            refusals.Add(("operation.standing", $"Occurrence {KeyOf(model.Latest(holder))} is settled: its result {waiting.Id} awaits review or is reviewed, and it runs again only when a review returns that result."));
        }

        // Results awaiting review hold dispatch at the goal's cap (§ 10).
        int awaiting = AwaitingCount(model, FlowRuns.Work(model, goal), before);
        int cap = AwaitingReviewCap(goal);
        if (awaiting >= cap)
        {
            refusals.Add(("operation.standing", $"{awaiting.ToString(CultureInfo.InvariantCulture)} of standing agent {goal.Id}'s results await review, its cap of {cap.ToString(CultureInfo.InvariantCulture)}: no run starts until they are reviewed."));
        }

        // From GOAL-08: open outputs at the plan's cap hold dispatch (§ 10).
        int open = StandingOutputs.OpenCount(model, goal, before);
        int openCap = StandingOutputs.CapsOf(goal).Open;
        if (open >= openCap)
        {
            refusals.Add(("operation.standing", $"{open.ToString(CultureInfo.InvariantCulture)} of standing agent {goal.Id}'s outputs are open, its cap of {openCap.ToString(CultureInfo.InvariantCulture)}: no run starts until another principal disposes of some."));
        }

        if (PeriodOf(model, goal, runs, at) is { Caps.Count: > 0 } period && period.Caps.TryGetValue("runs", out long allowed) && period.Spent.GetValueOrDefault("runs") >= allowed)
        {
            refusals.Add(("operation.budget", $"Standing agent {goal.Id} has had {period.Spent.GetValueOrDefault("runs").ToString(CultureInfo.InvariantCulture)} runs in the period from {RecordParser.FormatTimestamp(period.Start)}, its cap of {allowed.ToString(CultureInfo.InvariantCulture)}: its occurrences defer to the next period."));
        }

        return refusals;
    }

    /// <summary>
    /// Why a standing agent may not be activated (§§ 5 and 10): its until time has passed, or another active standing agent of the same
    /// role carries the same subject, so one subject never has two agents of one role.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> ActivationRefusals(EstateModel model, RecordEnvelope goal, RecordEnvelope? record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (!IsStanding(goal))
        {
            return [];
        }

        var refusals = new List<(string Code, string Message)>();
        DateTimeOffset at = record?.AtTime ?? DateTimeOffset.UtcNow;
        if (Until(goal) is { } until && at >= until)
        {
            refusals.Add(("goal-status.standing", $"The standing agent's until time {RecordParser.FormatTimestamp(until)} has passed: a renewal is a goal revision with a later until time, activated again."));
        }

        string identity = model.ById.ContainsKey(goal.Id) ? model.Chain(model.Latest(goal))[0] : goal.Id;
        string? role = Resolve(model, goal).Flow is FlowDefinition flow && WorkStep(flow) is FlowStep step ? step.Role : null;
        string subject = CanonicalJson.Serialize(TJson.Object(Plan(goal), "subject"));
        foreach (RecordEnvelope other in model.Current("goal").Where(g => IsStanding(g) && model.Chain(g)[0] != identity))
        {
            RecordEnvelope? revision = record is null ? other : AgentRuns.GoalAt(model, other.Id, record);
            if (revision is null || GoalRules.Status(model, revision, record) != "active" || CanonicalJson.Serialize(TJson.Object(Plan(revision), "subject")) != subject)
            {
                continue;
            }

            TemplatePin? mine = Pin(goal);
            TemplatePin? theirs = Pin(revision);
            string? otherRole = Resolve(model, revision).Flow is FlowDefinition otherFlow && WorkStep(otherFlow) is FlowStep otherStep ? otherStep.Role : null;
            if ((mine is not null && theirs is not null && mine.Id == theirs.Id) || (role is not null && role == otherRole))
            {
                refusals.Add(("goal-status.standing", $"Standing agent {revision.Id} ({EstateModel.BodyString(revision, "title")}) of the same role is active over the same subject: one subject has one agent of a role, so stop or pause it first."));
            }
        }

        return refusals;
    }

    /// <summary>
    /// Validates a goal's plan.standing (design/standing-agents.md §§ 5, 6 and 11): a stewardship goal's, beside no plan.flow, with the
    /// run's one ordinary work order given; the exact pin of a flow that declares the triggers it uses and one routed work step; the
    /// subject the flow declares; its triggers, each with its own key and the fields its kind takes; its period caps over every unit the
    /// budget charges; an until time; and optionally the runtime that dispatches it. A reader without the flow records a warning: it shows
    /// the goal and dispatches none of it.
    /// </summary>
    public static void Validate(ValidationContext context, BodyReader standing, string? kind, string? policy, IReadOnlyList<string>? orders, bool flowPlan)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(standing);
        BodyReader? pin = standing.Nested("pin", true);
        if (pin is not null)
        {
            TemplateRecords.ReadPin(pin);
            pin.NoOtherKeys();
        }

        BodyReader? subject = standing.Nested("subject", true);
        IReadOnlyList<BodyReader>? triggers = standing.NestedArray("triggers", true, allowEmpty: false);
        BodyReader? period = standing.Nested("period", true);
        string? until = standing.Timestamp("until", true);
        standing.Hash("runsOn", false);
        standing.Number("awaitingReview", false, 1, 100);
        StandingOutputs.ValidateCaps(standing.Nested("outputs", false));
        standing.NoOtherKeys();
        if (kind != "stewardship")
        {
            context.Error("standing.kind", "A standing agent is a stewardship goal: kind stewardship.");
        }

        if (flowPlan)
        {
            context.Error("standing.flow", "A standing agent runs its flow once per occurrence: its plan carries standing, never flow beside it.");
        }

        if (policy != "given" || orders is not { Count: 1 })
        {
            context.Error("standing.work", "A standing agent's work is one ordinary work order its sponsor gives: plan.policy given, with exactly one work order; each occurrence is an assignment of it.");
        }
        else if (context.Find(orders[0]) is { Type: "work-order" } order && EstateModel.BodyString(context.Model.Latest(order), "kind") != "ordinary")
        {
            context.Error("standing.work", "A standing agent's work order is ordinary.");
        }

        if (until is not null && Time(until) is { } end && end <= context.Record.AtTime)
        {
            context.Error("standing.until", "A standing agent's until time is after its goal is recorded; it returns to its sponsor then.");
        }

        PeriodCaps(period);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader trigger in triggers ?? [])
        {
            string? key = trigger.Text("key", true, 60, Patterns.Slug, "a trigger key");
            string? kindOf = trigger.Enum("kind", true, [.. TriggerKinds]);
            if (key is not null && !keys.Add(key))
            {
                trigger.Error("standing.trigger", "key", "Each trigger has its own key.");
            }

            TriggerFields(context, trigger, kindOf);
        }

        if (pin is null || subject is null)
        {
            return;
        }

        (FlowDefinition? resolved, string? unavailable) = Resolve(context.Model, context.Record);
        if (resolved is null)
        {
            context.Warning("standing.unavailable", $"The standing agent's {unavailable}. The goal is shown, and none of its occurrences is dispatched on this machine.");
            return;
        }

        if (WorkStep(resolved) is null)
        {
            context.Error("standing.flow", $"Flow {resolved.Pin.Id} carries each occurrence as one assignment: it declares exactly one routed step, of kind work.");
        }

        HashSet<string> declared = resolved.Triggers.Select(t => TJson.Text(t, "kind")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement trigger in TJson.Array(Plan(context.Record), "triggers"))
        {
            if (TJson.Text(trigger, "kind") is string used && !declared.Contains(used))
            {
                context.Error("standing.trigger", $"Flow {resolved.Pin.Id} declares no {used} trigger; a standing plan uses the trigger kinds its flow declares ({(declared.Count == 0 ? "none" : string.Join(", ", declared.Order(StringComparer.Ordinal)))}).");
            }
        }

        foreach (JsonProperty bound in subject.Element.EnumerateObject())
        {
            subject.Allow(bound.Name);
            if (!resolved.SubjectFields.ContainsKey(bound.Name))
            {
                subject.Error("standing.subject", bound.Name, $"Flow {resolved.Pin.Id}'s subject has no field '{bound.Name}'.");
            }
        }

        foreach ((string name, string type) in resolved.SubjectFields)
        {
            FlowRuns.Bind(context, subject, name, type, TJson.Text(TJson.Array(resolved.Subject, "fields").First(f => TJson.Text(f, "name") == name), "recordType"));
        }

        foreach (JsonElement role in TJson.Array(context.Record.Body, "roles"))
        {
            if (resolved.Role(TJson.Text(role, "role")) is { Holder: "person" } person)
            {
                foreach (string named in TJson.Strings(role, "principals").Where(p => context.Principal(p) is { } principal && EstateModel.BodyString(principal, "kind") != "human"))
                {
                    context.Error("standing.holder", $"Role {person.Id} of flow {resolved.Pin.Id} is a person's: {named} is not a person.");
                }
            }
        }

        // The budget is each run's caps; the period states every unit the budget charges, and the same currency.
        JsonElement budget = TJson.Object(context.Record.Body, "budget");
        JsonElement caps = TJson.Object(Plan(context.Record), "period");
        foreach (string unit in AgentRuns.ChargedUnits)
        {
            if ((AgentRuns.Amount(budget, unit) is null) != (AgentRuns.Amount(caps, unit) is null))
            {
                context.Error("standing.period", $"A standing agent's period caps the units its budget caps each run in, and only those: {unit} is in {(AgentRuns.Amount(budget, unit) is null ? "the period and not the budget" : "the budget and not the period")}.");
            }
        }

        if (AgentRuns.CurrencyUnit(budget) is string currency && AgentRuns.CurrencyUnit(caps) is string periodCurrency && currency != periodCurrency)
        {
            context.Error("standing.period", $"The period caps currency in {periodCurrency}, and the budget in {currency}.");
        }
    }

    private static void PeriodCaps(BodyReader? period)
    {
        if (period is null)
        {
            return;
        }

        period.Enum("unit", true, [.. Units]);
        period.Number("every", false, 1, 120);
        period.Number("runs", true, 1, 100_000);
        period.Number("wallMinutes", true, 1, 10_000_000);
        period.Number("tokens", false, 1, 1_000_000_000_000);
        BodyReader? currency = period.Nested("currency", false);
        if (currency is not null)
        {
            currency.Number("amount", true, 0, 1_000_000_000_000);
            currency.Text("unit", true, 3, Patterns.CurrencyUnit, "three upper-case letters such as USD");
            currency.NoOtherKeys();
        }

        period.NoOtherKeys();
    }

    /// <summary>The fields a trigger's kind takes (design/standing-agents.md § 6).</summary>
    private static void TriggerFields(ValidationContext context, BodyReader trigger, string? kind)
    {
        switch (kind)
        {
            case "schedule":
                trigger.Enum("unit", true, [.. Units]);
                trigger.Number("every", true, 1, 1000);
                trigger.Timestamp("firstAt", true);
                break;
            case "duty":
                if (trigger.Ulid("policy", true) is string policy && context.Find(policy) is not { Type: "estate-policy" })
                {
                    trigger.Error("standing.trigger", "policy", $"'policy' names {policy}, which is not an estate policy of this estate.");
                }

                trigger.Text("rule", true, 100);
                break;
            case "event":
                string? type = trigger.Text("recordType", true, 100, Patterns.Slug, "a record type");
                if (type is not null && RecordTypeRegistry.Find(type) is null)
                {
                    trigger.Error("standing.trigger", "recordType", $"'{type}' is not a record type this build reads.");
                }

                BodyReader? fields = trigger.Nested("fields", false);
                if (fields is not null)
                {
                    foreach (JsonProperty field in fields.Element.EnumerateObject())
                    {
                        fields.Allow(field.Name);
                        if (field.Value.ValueKind != JsonValueKind.String)
                        {
                            fields.Error("body.type", field.Name, "An event filter compares a body field with a string value.");
                        }
                    }
                }

                trigger.StringArray("roles", false, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "a role name");
                break;
            case "condition":
                if (trigger.Nested("record", true) is BodyReader predicate)
                {
                    GoalType.Predicate(context, predicate);
                }

                break;
        }

        trigger.NoOtherKeys();
    }

    /// <summary>
    /// Validates an assignment's occurrence (design/standing-agents.md §§ 6, 7 and 10), the duplicate refusal at the record, whoever
    /// records it: every assignment of a standing agent's work order names one occurrence and only those do; its trigger is the goal's,
    /// its key is the one its goal, trigger, revision and item make; a schedule's interval and an event's record are due when it is
    /// recorded; and no earlier assignment names the same key unless this one takes that one's step over, so a key holds one assignment.
    /// </summary>
    public static void ValidateOccurrence(ValidationContext context, RecordEnvelope order, BodyReader? occurrence)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(order);
        RecordEnvelope? goal = StandingOf(context.Model, order, context.Record);
        string key = EstateModel.BodyString(context.Model.Latest(order), "key") ?? order.Id;
        if (goal is null)
        {
            if (occurrence is not null)
            {
                context.Error("assignment.occurrence", $"An occurrence names one of a standing agent's, and work order {key} is the work of none.");
            }

            return;
        }

        if (occurrence is null)
        {
            context.Error("assignment.occurrence", $"Work order {key} is the work of standing agent {goal.Id}: each assignment of it names the occurrence it carries (goal step routes one, work takeover hands one over).");
            return;
        }

        string? named = occurrence.Text("key", true, 600);
        string? triggerKey = occurrence.Text("trigger", true, 60, Patterns.Slug, "a trigger key");
        string? revision = occurrence.Ulid("revision", true);
        BodyReader? item = occurrence.Nested("item", true);
        occurrence.NoOtherKeys();
        if (named is null || triggerKey is null || revision is null || item is null)
        {
            return;
        }

        string identity = context.Model.Chain(goal)[0];
        Trigger? trigger = Triggers(context.Model, goal).FirstOrDefault(t => t.Key == triggerKey);
        RecordEnvelope? handedOver = Routing.TakesOverFrom(context.Model, context.Record);
        RecordEnvelope? revised = context.Record.Supersedes is string prior && context.Find(prior) is { Type: "assignment" } earlierRevision && KeyOf(earlierRevision) is not null ? earlierRevision : null;
        if (revised is not null)
        {
            // A revision keeps the occurrence its assignment carries, exactly: the chain holds that key until the occurrence settles, so a
            // revision never frees it for a second assignment, nor moves the assignment to another occurrence.
            if (CanonicalJson.Serialize(TJson.Object(revised.Body, "occurrence")) != CanonicalJson.Serialize(occurrence.Element))
            {
                context.Error("assignment.occurrence", $"This assignment revises assignment {revised.Id}, which carries occurrence {KeyOf(revised)}: a revision keeps its occurrence exactly, and another occurrence is a new assignment.");
            }
        }
        else if (handedOver is not null)
        {
            // A hand-over carries the occurrence the agent's assignment named, exactly, whatever the goal declares now.
            if (CanonicalJson.Serialize(TJson.Object(handedOver.Body, "occurrence")) != CanonicalJson.Serialize(occurrence.Element))
            {
                context.Error("assignment.occurrence", $"This assignment takes occurrence {KeyOf(handedOver)} over from assignment {handedOver.Id}, and names it exactly as that one does.");
            }
        }
        else if (trigger is null)
        {
            context.Error("assignment.occurrence", $"Standing agent {goal.Id} declares no trigger '{triggerKey}' at its revision current when this assignment is recorded.");
            return;
        }
        else
        {
            string? itemKey = ItemKey(trigger.Kind, item.Element);
            string? expected = itemKey is null ? null : trigger.Kind == "duty" ? "duty/" + itemKey : $"{identity}/{triggerKey}/{revision}/{itemKey}";
            if (expected is null || expected != named)
            {
                context.Error("assignment.occurrence", $"Occurrence key '{named}' is not the one its goal, trigger, revision and item make{(expected is null ? $": the item does not fit a {trigger.Kind} trigger" : $" ('{expected}')")}.");
            }
            else if (trigger.Revision != revision)
            {
                context.Error("assignment.occurrence", $"Trigger {triggerKey}'s revision is {trigger.Revision} when this assignment is recorded, not {revision}.");
            }

            // A new occurrence is carried only while its agent is active and before its until time, as of the record's own time, whatever time
            // its triggers were evaluated at.
            string status = GoalRules.Status(context.Model, goal, context.Record);
            if (status != "active")
            {
                context.Error("assignment.occurrence", $"Standing agent {goal.Id} is {status} when this assignment is recorded: a new occurrence is carried only while it is active.");
            }
            else if (Until(goal) is { } until && context.Record.AtTime >= until)
            {
                context.Error("assignment.occurrence", $"Standing agent {goal.Id}'s until time {RecordParser.FormatTimestamp(until)} has passed when this assignment is recorded: it returns to its sponsor, and a renewal is a goal revision its sponsor activates.");
            }

            Due(context, goal, trigger, item.Element);
        }

        // A key holds one assignment: any other assignment naming it, recorded before or after this one, holds the key, so two callers whose
        // assignments race, or two clones' merged, are refused whichever is stored second; only a person's hand-over of the same occurrence,
        // and an assignment stored without the record it was routed with, which holds nothing, stand beside it.
        bool known = context.Model.ById.ContainsKey(context.Record.Id);
        if (known && (!context.Model.IsCurrent(context.Record) || Routing.Stranded(context.Model, context.Record)))
        {
            // An earlier revision is held by its chain's current one, and a withdrawn or stranded assignment holds nothing.
            return;
        }

        IReadOnlyList<string> own = known ? context.Model.Chain(context.Model.Latest(context.Record)) : [context.Record.Id];
        IReadOnlyList<string> previous = context.Record.Supersedes is string superseded && context.Model.ById.TryGetValue(superseded, out RecordEnvelope? earlier) ? context.Model.Chain(earlier) : [];
        IReadOnlyList<string> taken = handedOver is null ? [] : context.Model.Chain(context.Model.Latest(handedOver));
        foreach (RecordEnvelope other in context.Model.Current("assignment").Where(a => !own.Contains(a.Id) && !previous.Contains(a.Id) && KeyOf(a) == named))
        {
            IReadOnlyList<string> chain = context.Model.Chain(context.Model.Latest(other));
            bool handsOver = Routing.TakesOverFrom(context.Model, other) is RecordEnvelope from && context.Model.Chain(context.Model.Latest(from)).Any(id => own.Contains(id) || previous.Contains(id));
            if (chain.Any(taken.Contains) || handsOver || Routing.Stranded(context.Model, other))
            {
                continue;
            }

            context.Error("standing.duplicate", $"Occurrence {named} is held by assignment {other.Id}: a key holds one assignment, whoever records it. A settled occurrence runs again only when a review returns its result, and the assignment that holds it goes on; where two clones each recorded one, record a reconciliation that keeps one and withdraws the other.");
            break;
        }
    }

    /// <summary>Whether a schedule's interval and an event's record are due when the assignment is recorded: never before its time, and an event only from a record of its type and filters.</summary>
    private static void Due(ValidationContext context, RecordEnvelope goal, Trigger trigger, JsonElement item)
    {
        DateTimeOffset at = context.Record.AtTime;
        switch (trigger.Kind)
        {
            case "schedule":
                long number = item.TryGetProperty("interval", out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out long value) ? value : -1;
                if (Tick(trigger.Declaration, number) is not { } due || due > at || Time(TJson.Text(item, "dueAt")) != due)
                {
                    context.Error("assignment.occurrence", $"Interval {number.ToString(CultureInfo.InvariantCulture)} of schedule {trigger.Key} is not due when this assignment is recorded, or its dueAt is not the schedule's: an occurrence is never carried before it is due.");
                }
                else if (LatestTick(trigger.Declaration, at) is long latest && latest != number)
                {
                    context.Error("assignment.occurrence", $"Interval {number.ToString(CultureInfo.InvariantCulture)} of schedule {trigger.Key} coalesced into interval {latest.ToString(CultureInfo.InvariantCulture)} before this assignment was recorded: missed ticks are carried once, as the latest.");
                }

                break;
            case "event":
                JsonElement record = TJson.Object(item, "record");
                RecordEnvelope? named = context.Find(TJson.Text(record, "id"));
                string? type = TJson.Text(trigger.Declaration, "recordType");
                bool fits = named is not null && named.Type == type && named.ComputedHash == TJson.Text(record, "hash") && named.AtTime <= at
                    && (TJson.Object(trigger.Declaration, "fields") is not { ValueKind: JsonValueKind.Object } fields || fields.EnumerateObject().All(f => f.Value.ValueKind == JsonValueKind.String && GoalPredicates.FieldString(named, f.Name) == f.Value.GetString()))
                    && TriggerActivation(context.Model, goal, trigger) is { } activated && named.AtTime > activated;
                if (!fits)
                {
                    context.Error("assignment.occurrence", $"The event's record is not a {type} record of trigger {trigger.Key}'s filters, at its exact hash, recorded after the trigger was activated and before this assignment.");
                }
                else if (named!.By is string author && (OwnAuthors(context.Model, goal, Routing.Assignments(context.Model, FlowRuns.Work(context.Model, goal)!).Where(a => KeyOf(a) is not null).ToList(), Resolve(context.Model, goal).Flow is FlowDefinition flow ? WorkStep(flow) : null).Contains(author)
                    || !Admitted(context.Model, named, [.. TJson.Strings(trigger.Declaration, "roles")])))
                {
                    // Who counts as the agent's own, and which agents' records trigger it, are read as the records stand: the write boundary refuses
                    // a new assignment for them, and assignments already in the estate keep their reading.
                    context.Findings.Info(BoundaryCode, context.Location, $"The event's record {named.Id} is the agent's own, or an agent's that trigger {trigger.Key} does not admit: an agent's records trigger only another agent whose trigger names its role, to depth two, and never the agent itself.");
                }

                break;
            case "condition":
                Condition(context, trigger, item);
                break;
            case "duty":
                Duty(context, trigger, item);
                break;
            case "request":
                if (Time(TJson.Text(item, "at")) is not { } asked || asked > at || TJson.Text(item, "by") != context.Record.By)
                {
                    context.Error("assignment.occurrence", "A request's time is no later than the assignment that carries it, and its requester is who records that assignment.");
                }

                break;
        }
    }

    /// <summary>The code of the occurrence checks the write boundary refuses for a new assignment, read as the records stand.</summary>
    public const string BoundaryCode = "assignment.occurrence-now";

    /// <summary>
    /// A condition's item: its predicate fails when the assignment is recorded, over the set of records its hash and count name, read over
    /// the current records and as status derives states now, which the write boundary holds a new assignment to.
    /// </summary>
    private static void Condition(ValidationContext context, Trigger trigger, JsonElement item)
    {
        JsonElement predicate = TJson.Object(trigger.Declaration, "record");
        GoalPredicates.Verdict verdict = GoalPredicates.EvaluatePredicate(context.Model, predicate, context.Record.AtTime);
        string set = string.Join("\n", verdict.Read.Select(r => r.Id + " " + r.Hash).Order(StringComparer.Ordinal));
        bool fits = verdict.Value == "not-met" && TJson.Text(item, "failing") == ContentHash.Sha256(Encoding.UTF8.GetBytes(set))
            && item.TryGetProperty("records", out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int records) && records == verdict.Read.Count;
        if (fits)
        {
            return;
        }

        // The current records and their derived states change after the assignment, so the write boundary refuses a mismatch and
        // assignments already in the estate keep their reading.
        context.Findings.Info(BoundaryCode, context.Location, $"Condition {trigger.Key} does not fail over the records this occurrence names when the assignment is recorded: its item is the hash and count of the set the predicate reads while it fails ({verdict.Value}: {verdict.Reason}).");
    }

    /// <summary>
    /// A duty's item: the duty occurrence's exact identity, the originating revision of the trigger's policy and its rule, the subject's
    /// identity and the schedule item in canonical form, and the due time and deadline the schedule gives, due when the assignment is
    /// recorded, so one deadline has one key. Whether the policy's evaluation still owes it is the runtime's, before dispatch.
    /// </summary>
    private static void Duty(ValidationContext context, Trigger trigger, JsonElement item)
    {
        string? why = DutyRefusal(context.Model, trigger, item, context.Record.AtTime);
        if (why is not null)
        {
            context.Error("assignment.occurrence", $"The duty occurrence is not one trigger {trigger.Key} makes: {why}.");
        }
    }

    private static string? DutyRefusal(EstateModel model, Trigger trigger, JsonElement item, DateTimeOffset at)
    {
        JsonElement named = TJson.Object(item, "policy");
        if (TJson.Text(named, "type") != "estate-policy" || TJson.Text(named, "id") is not string id || !model.ById.TryGetValue(id, out RecordEnvelope? policy) || policy.Type != "estate-policy"
            || policy.ComputedHash != TJson.Text(named, "hash"))
        {
            return "its policy is not an estate policy at its exact hash";
        }

        if (TJson.Text(trigger.Declaration, "policy") is not string declared || !model.ById.TryGetValue(declared, out RecordEnvelope? declaredPolicy) || PolicyRecords.Identity(model, policy) != PolicyRecords.Identity(model, declaredPolicy))
        {
            return "its policy is not the trigger's";
        }

        string? key = TJson.Text(item, "rule");
        JsonElement rule = DutyRecords.Rule(policy, key);
        if (key != TJson.Text(trigger.Declaration, "rule") || !DutyRecords.IsDuty(rule) || DutyRecords.Origin(model, policy, key).Id != policy.Id)
        {
            return "its rule is not the trigger's recurring duty at its originating revision";
        }

        JsonElement parameters = PolicyRecords.Property(rule, "parameters");
        JsonElement selected = PolicyRecords.Property(parameters, "subject");
        if (PolicyRecords.Text(selected, "id") is not string subjectId || !model.ById.TryGetValue(subjectId, out RecordEnvelope? subject))
        {
            return "its duty's subject is not in this estate";
        }

        // The schedule item is the duty's own, built from the policy's schedule as its evaluation builds it (kind with number, at or
        // publication, nothing else), and the item must be it exactly in canonical form: an extra key or a changed value is another item.
        JsonElement schedule = PolicyRecords.Property(parameters, "schedule");
        JsonElement occurrence = TJson.Object(item, "schedule");

        // A release opens an occurrence only as the duty's evaluation opens one: the first publication of the schedule's product, within
        // the schedule's window, so a later published revision of one release, or another product's, never names a second key.
        if (PolicyRecords.Text(schedule, "kind") == "release" && TJson.Text(TJson.Object(occurrence, "publication"), "id") is string publicationId
            && model.ById.TryGetValue(publicationId, out RecordEnvelope? publication) && DutyRecords.PublicationRefusal(model, policy, key, schedule, publication) is string notOpened)
        {
            return $"its schedule item is not one of the duty's schedule: {notOpened}";
        }

        (JsonElement Item, DateTimeOffset Due)? own = PolicyRecords.Text(schedule, "kind") switch
        {
            "interval" when occurrence.ValueKind == JsonValueKind.Object && occurrence.TryGetProperty("number", out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out long number) && number >= 0
                && DutyRecords.IntervalDue(schedule, number) is { } intervalDue => (JsonSerializer.SerializeToElement(new { kind = "interval", number }), intervalDue),
            "date" when DutyRecords.DateDue(schedule) is { } dateDue => (JsonSerializer.SerializeToElement(new { kind = "date", at = PolicyRecords.Text(schedule, "at") }), dateDue),
            "release" when TJson.Text(TJson.Object(occurrence, "publication"), "id") is string release && model.ById.TryGetValue(release, out RecordEnvelope? published)
                && published.Type == "release" && EstateModel.BodyString(published, "status") == "published"
                => (JsonSerializer.SerializeToElement(new { kind = "release", publication = new { type = published.Type, id = published.Id, hash = published.ComputedHash } }), published.AtTime),
            _ => null,
        };
        if (own is not { } expectedItem || occurrence.ValueKind != JsonValueKind.Object || CanonicalJson.Serialize(occurrence) != CanonicalJson.Serialize(expectedItem.Item))
        {
            return own is null
                ? "its schedule item is not one of the duty's schedule"
                : $"its schedule item is not the duty's own, {CanonicalJson.Serialize(own.Value.Item)}, exactly";
        }

        DateTimeOffset? due = expectedItem.Due;
        string expected = $"{policy.Id}:{PolicyRecords.Identity(model, subject)}:{CanonicalJson.Serialize(expectedItem.Item)}";
        if (TJson.Text(item, "occurrence") != expected)
        {
            return $"its identity is not the duty occurrence's, {expected}";
        }

        long grace = parameters.TryGetProperty("graceDays", out JsonElement days) && days.ValueKind == JsonValueKind.Number && days.TryGetInt64(out long value) ? value : 0;
        if (Time(TJson.Text(item, "dueAt")) != due || Time(TJson.Text(item, "deadline")) != due.Value.AddDays(grace))
        {
            return "its due time or deadline is not the schedule's";
        }

        return due > at ? "it is not due when the assignment is recorded" : null;
    }

    /// <summary>A UTC timestamp, or null.</summary>
    internal static DateTimeOffset? Time(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value) ? value : null;
}
