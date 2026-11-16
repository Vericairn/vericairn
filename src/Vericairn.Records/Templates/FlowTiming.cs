// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>
/// A step's timing contract, its <c>when</c> (the estate's design/flow.md § 7, decision 01M75X387N0J4FRWQV80M406CY; from 0.9.4, PROC-18):
/// how it starts, at most one of <c>instant</c> (the default, as soon as what it waits on is done and its preconditions hold), <c>delay</c>
/// (a duration after the previous step's close, a named step's close, an output's time or the run's activation), <c>at</c> (an instant, or
/// a time an output's record gives), <c>schedule</c> (days of the week and times of day in a named time zone), <c>event</c> (an event kind
/// and a record predicate, the records read again before acting), <c>watch</c> (a repeated observation until the done predicates hold or a
/// limit passes) and <c>due</c> (a delay after a named step's close, as before 0.9.4); and, combined with any of them, a <c>window</c> of
/// the hours and days it may start in, quiet hours among them, a <c>zone</c>, a <c>deadline</c> after its due time and the rule for a
/// missed deadline: <c>late</c> runs it late, <c>skip</c> skips an optional step and <c>attention</c>, the default, asks the sponsor and
/// runs it once, late, when answered. The flow marks a timing no venture changes <c>fixed</c>, such as an embargo. Every due time is
/// computed from records and the runtime's clock, never an author's claim.
/// </summary>
public sealed partial record FlowTiming
{
    /// <summary>How a step starts: the keys of which a <c>when</c> names at most one, instant when it names none.</summary>
    public static readonly ImmutableArray<string> Starts = ["instant", "delay", "at", "schedule", "event", "watch", "due"];

    /// <summary>The rules for a missed deadline: run late, skip an optional step, or ask the sponsor (the default).</summary>
    public static readonly ImmutableArray<string> MissedRules = ["late", "skip", "attention"];

    /// <summary>The event kinds an event timing names, the event journal's (vericairn.event/1); an event is a hint, and the records decide.</summary>
    public static readonly ImmutableArray<string> EventKinds = ["record", "attention", "ask", "step", "request", "due", "run", "presence"];

    /// <summary>The days of the week a schedule or a window names, Monday first.</summary>
    public static readonly ImmutableArray<string> Days = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>The time zone a run reads local times in when neither its step nor its configuration names one.</summary>
    public const string Utc = "UTC";

    /// <summary>The configuration option that names a venture's time zone, an IANA name.</summary>
    public const string ZoneOption = "zone";

    /// <summary>The prefix of the configuration option that sets one step's timing: when.&lt;key&gt;, a timing as JSON text.</summary>
    public const string WhenOption = "when.";

    /// <summary>A delay's point that is the close of the steps a step waits on.</summary>
    public const string Close = "close";

    /// <summary>A delay's point that is the run's activation.</summary>
    public const string Activation = "activation";

    /// <summary>How long a search for a schedule's occurrence or a window's opening looks ahead.</summary>
    private const int Horizon = 400;

    /// <summary>The timing of a step that declares none: instant.</summary>
    public static FlowTiming Instant { get; } = new();

    /// <summary>One of <see cref="Starts"/>.</summary>
    public string Start { get; init; } = "instant";

    /// <summary>A delay's or due time's duration.</summary>
    public TimeSpan? After { get; init; }

    /// <summary>A delay's point: close, activation, a step key or step.item; a due time's step.</summary>
    public string? From { get; init; }

    /// <summary>An at timing's instant, or local date and time in the zone.</summary>
    public string? Time { get; init; }

    /// <summary>An at timing's output, step.item, whose record gives the time.</summary>
    public string? Output { get; init; }

    /// <summary>The field of that output's record, a dotted path, that holds the time.</summary>
    public string? Field { get; init; }

    /// <summary>A schedule's days of the week; every day when it names none.</summary>
    public IReadOnlyList<DayOfWeek> ScheduleDays { get; init; } = [];

    /// <summary>A schedule's times of day, in the zone.</summary>
    public IReadOnlyList<TimeOnly> Times { get; init; } = [];

    /// <summary>An event timing's event kind.</summary>
    public string? EventKind { get; init; }

    /// <summary>An event timing's record predicate.</summary>
    public JsonElement Predicate { get; init; }

    /// <summary>A watch's interval.</summary>
    public TimeSpan? Every { get; init; }

    /// <summary>A watch's limit.</summary>
    public TimeSpan? Limit { get; init; }

    /// <summary>The spans of a window a step may start in; any time when it names none.</summary>
    public IReadOnlyList<Span> Open { get; init; } = [];

    /// <summary>The quiet spans of a window, in which a step never starts.</summary>
    public IReadOnlyList<Span> Quiet { get; init; } = [];

    /// <summary>The time zone the step's local times are read in, overriding the run's; null for the run's.</summary>
    public string? Zone { get; init; }

    /// <summary>How long after its due time the step may start before it is missed; null for no deadline.</summary>
    public TimeSpan? Deadline { get; init; }

    /// <summary>The rule for a missed deadline, one of <see cref="MissedRules"/>.</summary>
    public string Missed { get; init; } = "attention";

    /// <summary>True when the flow fixes the timing, so that no configuration changes it, such as an embargo.</summary>
    public bool Fixed { get; init; }

    /// <summary>The timing as declared or configured.</summary>
    public JsonElement Element { get; init; }

    /// <summary>True when it names a window.</summary>
    public bool Windowed => Open.Count > 0 || Quiet.Count > 0;

    /// <summary>True when it gates nothing: instant, with no window and no deadline.</summary>
    public bool Untimed => Start == "instant" && !Windowed && Deadline is null;

    /// <summary>The step a delay, a due time or an at timing's output counts from, which the step waits on; null for none.</summary>
    public string? Waited => Start switch
    {
        "due" => From,
        "delay" when From is not null && From is not (Close or Activation) => From.Split('.')[0],
        "at" when Output is not null => Output.Split('.')[0],
        _ => null,
    };

    /// <summary>A span of a window: the days it starts on (every day when none) and its local start and end; an end at or before the start is on the next day.</summary>
    /// <param name="Days">The days it starts on.</param>
    /// <param name="From">Its local start.</param>
    /// <param name="To">Its local end.</param>
    public sealed record Span(IReadOnlyList<DayOfWeek> Days, TimeOnly From, TimeOnly To)
    {
        /// <summary>The span in words.</summary>
        public override string ToString() => (Days.Count == 0 ? "every day" : string.Join(", ", Days.Select(DayName))) + $" {From:HH\\:mm} to {To:HH\\:mm}";
    }

    /// <summary>The step a <c>when</c> counts from, which the step waits on; null for none, and for a <c>when</c> that is undefined.</summary>
    public static string? WaitedOf(JsonElement when) => when.ValueKind == JsonValueKind.Object ? Read(when).Waited : null;

    /// <summary>A timing read from content already checked by <see cref="Check"/>; a <c>when</c> that is undefined is instant.</summary>
    public static FlowTiming Read(JsonElement when)
    {
        if (when.ValueKind != JsonValueKind.Object)
        {
            return Instant;
        }

        string start = Starts.FirstOrDefault(s => s != "instant" && when.TryGetProperty(s, out _)) ?? "instant";
        JsonElement body = TJson.Object(when, start);
        JsonElement window = TJson.Object(when, "window");
        return new FlowTiming
        {
            Start = start,
            After = FlowContract.ParseDuration(TJson.Text(body, "after")),
            From = start == "delay" ? TJson.Text(body, "from") ?? Close : start == "due" ? TJson.Text(body, "from") : null,
            Time = start == "at" ? TJson.Text(body, "time") : null,
            Output = start == "at" ? TJson.Text(body, "output") : null,
            Field = start == "at" ? TJson.Text(body, "field") : null,
            ScheduleDays = start == "schedule" ? DaysOf(body) : [],
            Times = start == "schedule" ? TJson.Strings(body, "times").Select(TimeOf).OfType<TimeOnly>().Order().ToList() : [],
            EventKind = start == "event" ? TJson.Text(body, "kind") : null,
            Predicate = start == "event" ? TJson.Object(body, "predicate") : default,
            Every = start == "watch" ? FlowContract.ParseDuration(TJson.Text(body, "every")) : null,
            Limit = start == "watch" ? FlowContract.ParseDuration(TJson.Text(body, "limit")) : null,
            Open = Spans(window, "open"),
            Quiet = Spans(window, "quiet"),
            Zone = TJson.Text(when, "zone"),
            Deadline = FlowContract.ParseDuration(TJson.Text(when, "deadline")),
            Missed = TJson.Text(when, "missed") ?? "attention",
            Fixed = when.TryGetProperty("fixed", out JsonElement fixedValue) && fixedValue.ValueKind == JsonValueKind.True,
            Element = when.Clone(),
        };
    }

    /// <summary>
    /// A timing a venture's configuration sets as JSON text (the option when.&lt;key&gt;), read as the flow's own is, or null with why
    /// when it is not one: not a JSON object, a timing the step may not take, or one that marks itself fixed, which only a flow does.
    /// </summary>
    internal static (FlowTiming? Timing, IReadOnlyList<string> Problems) Configured(string text, TimingShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return (null, ["it is not a timing as JSON text, such as {\"delay\":{\"after\":\"PT2H\"}}"]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, ["it is not a timing as JSON text, such as {\"delay\":{\"after\":\"PT2H\"}}"]);
            }

            var findings = new FindingList();
            var reader = new BodyReader(document.RootElement, "when", findings, RecordFormats.Current);
            Check(reader, shape with { Configured = true });
            List<string> problems = findings.Where(f => f.Severity == Severity.Error).Select(f => f.Message).ToList();
            return problems.Count > 0 ? (null, problems) : (Read(document.RootElement), []);
        }
    }

    /// <summary>
    /// What checking a step's timing needs of the flow: the step's kind and whether it is required or declares done predicates; the
    /// outputs of each step declared before it, by key, or null in an extension, whose base is checked when it resolves; a check of a
    /// record predicate; and whether the timing is a venture's configuration rather than the flow's own.
    /// </summary>
    internal sealed record TimingShape(string? Kind, bool Required, bool HasDone, Func<string, IReadOnlyCollection<string>?>? Earlier, Action<BodyReader>? CheckPredicate, bool Configured = false);

    /// <summary>Checks a step's timing (design/flow.md § 7), each problem a finding on its field.</summary>
    internal static void Check(BodyReader when, TimingShape shape)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(shape);
        List<string> starts = Starts.Where(when.Has).ToList();
        if (starts.Count > 1)
        {
            when.Error("flow.when", starts[1], $"A step starts one way: {string.Join(" and ", starts)} are both named; name at most one of {string.Join(", ", Starts)}.");
        }

        if (when.Has("instant") && when.Flag("instant", true) == false)
        {
            when.Error("flow.when", "instant", "instant is true when it is named: the step starts as soon as what it waits on is done and its preconditions hold.");
        }

        if (when.Nested("delay", false) is BodyReader delay)
        {
            Duration(delay, "after", true);
            string? from = delay.Text("from", false, 160, Point(), "close, activation, an earlier step's key, or step.item for an output's time");
            if (from is not null && from is not (Close or Activation))
            {
                Earlier(delay, "from", from, shape, from.Contains('.', StringComparison.Ordinal) ? "an output's time counts from an earlier step's item" : "a delay counts from an earlier step's close");
            }

            delay.NoOtherKeys();
        }

        if (when.Nested("due", false) is BodyReader due)
        {
            Duration(due, "after", true);
            string? from = due.Text("from", true, 100, Patterns.Slug, "a step key");
            if (from is not null)
            {
                Earlier(due, "from", from, shape, "a due time counts from an earlier step's completion");
            }

            due.NoOtherKeys();
        }

        if (when.Nested("at", false) is BodyReader at)
        {
            string? time = at.Text("time", false, 40);
            string? output = at.Text("output", false, 160, Point(), "step.item, an earlier step's item");
            string? field = at.Text("field", false, 200, FieldPath(), "a field of the output's record, a dotted path such as publishedAt or facts.at");
            if ((time is null) == (output is null && !at.Has("output")))
            {
                at.Error("flow.when", "time", "An at timing names its time, an instant or a local date and time, or the output whose record gives it, one of them.");
            }

            if (time is not null && !ParseInstant(time, out _) && !LocalTime(time, out _))
            {
                at.Error("flow.when", "time", $"'{time}' is not an instant such as 2026-11-20T09:00:00Z or 2026-11-20T09:00:00+01:00, nor a local date and time such as 2026-11-20T09:00 in the step's zone.");
            }

            if (output is not null)
            {
                if (!output.Contains('.', StringComparison.Ordinal))
                {
                    at.Error("flow.when", "output", "An at timing's output is step.item, an earlier step's item whose record gives the time.");
                }
                else
                {
                    Earlier(at, "output", output, shape, "an at timing reads its time from an earlier step's item");
                }

                if (field is null && !at.Has("field"))
                {
                    at.Error("flow.when", "field", "An at timing's output names the field of its record that holds the time.");
                }
            }
            else if (at.Has("field"))
            {
                at.Error("flow.when", "field", "Only an at timing that names an output reads a field of its record.");
            }

            at.NoOtherKeys();
        }

        if (when.Nested("schedule", false) is BodyReader schedule)
        {
            DayList(schedule, "days");
            IReadOnlyList<string>? times = schedule.StringArray("times", true, allowEmpty: false, maxItemLength: 5, itemPattern: ClockTime(), patternHint: "a time of day, HH:MM");
            if (times is not null && (times.Count > 24 || times.Distinct(StringComparer.Ordinal).Count() != times.Count))
            {
                schedule.Error("flow.when", "times", "A schedule names each time of day once, at most 24 of them.");
            }

            schedule.NoOtherKeys();
        }

        if (when.Nested("event", false) is BodyReader evented)
        {
            evented.Enum("kind", true, [.. EventKinds]);
            if (evented.Nested("predicate", true) is BodyReader predicate)
            {
                shape.CheckPredicate?.Invoke(predicate);
            }

            evented.NoOtherKeys();
        }

        if (when.Nested("watch", false) is BodyReader watch)
        {
            Duration(watch, "every", true);
            Duration(watch, "limit", true);
            watch.NoOtherKeys();
            if (!shape.HasDone)
            {
                watch.Error("flow.when", "watch", "A watch repeats until the step's done predicates hold; declare them.");
            }

            if (shape.Kind is not null && FlowContract.Routed.IndexOf(shape.Kind) >= 0)
            {
                when.Error("flow.when", "watch", "A watch reads the records until its predicates hold; a work or review step is carried by an assignment instead.");
            }
        }

        if (when.Nested("window", false) is BodyReader window)
        {
            int spans = 0;
            foreach (string name in new[] { "open", "quiet" })
            {
                foreach (BodyReader span in window.NestedArray(name, false, allowEmpty: false) ?? [])
                {
                    spans++;
                    DayList(span, "days");
                    string? from = span.Text("from", true, 5, ClockTime(), "a time of day, HH:MM");
                    string? to = span.Text("to", true, 5, ClockTime(), "a time of day, HH:MM");
                    if (from is not null && from == to)
                    {
                        span.Error("flow.when", "to", "A span of a window ends at another time than it starts; one that ends at or before its start ends on the next day.");
                    }

                    span.NoOtherKeys();
                }
            }

            if (spans == 0)
            {
                window.Error("flow.when", "open", "A window names the spans a step may start in, open, or those it never starts in, quiet, or both.");
            }

            window.NoOtherKeys();
        }

        if (when.Text("zone", false, 64) is string zone && !KnownZone(zone))
        {
            when.Error("flow.zone", "zone", $"'{zone}' is not an IANA time zone, such as Europe/London or America/New_York, that every machine reads alike.");
        }

        Duration(when, "deadline", false);
        string? missed = when.Enum("missed", false, [.. MissedRules]);
        if (missed is not null && !when.Has("deadline"))
        {
            when.Error("flow.when", "missed", "A missed-time rule applies to a deadline; name the deadline it applies to.");
        }

        if (missed == "skip" && shape.Required)
        {
            when.Error("flow.when", "missed", "Only an optional step is skipped when it misses its deadline; a required one runs late, or asks the sponsor.");
        }

        if (when.Has("deadline") && when.Has("watch"))
        {
            when.Error("flow.when", "deadline", "A watch has its limit; it takes no deadline.");
        }

        if (when.Flag("fixed", false) is bool isFixed)
        {
            if (shape.Configured)
            {
                when.Error("flow.when", "fixed", "Only the flow fixes a step's timing; a configuration sets a timing, never a fixed one.");
            }
            else if (!isFixed)
            {
                when.Error("flow.when", "fixed", "fixed is true when it is named: no configuration changes the step's timing.");
            }
        }

        when.NoOtherKeys();
    }

    /// <summary>
    /// The instant a local date and time is in a zone, daylight-saving changes defined: a time a change skips is moved forward by the
    /// change's length, as 02:30 on a night the clocks go from 02:00 to 03:00 is 03:30; a time a change repeats is its first occurrence,
    /// before the clocks go back.
    /// </summary>
    public static DateTimeOffset LocalToInstant(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            // The offsets before and after the gap; the time moves forward by their difference.
            TimeSpan before = zone.GetUtcOffset(unspecified.AddHours(-12));
            TimeSpan after = zone.GetUtcOffset(unspecified.AddHours(12));
            TimeSpan gap = after - before;
            return new DateTimeOffset(unspecified.Add(gap), after);
        }

        if (zone.IsAmbiguousTime(unspecified))
        {
            TimeSpan earlier = zone.GetAmbiguousTimeOffsets(unspecified).Max();
            return new DateTimeOffset(unspecified, earlier);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>A zone by its IANA name, for the names <see cref="KnownZone"/> accepts; Windows reads its rules from the Windows zone that carries it.</summary>
    public static TimeZoneInfo ZoneOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!KnownZone(name))
        {
            throw new InvalidOperationException($"'{name}' is not an IANA time zone that every machine reads alike.");
        }

        if (name == Utc)
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Windows[name]);
        }
    }

    /// <summary>A zone by its IANA name, as <see cref="ZoneOf"/> reads it, or null when this machine has no rules for it.</summary>
    public static TimeZoneInfo? TryZoneOf(string name)
    {
        try
        {
            return ZoneOf(name);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>True for an IANA time zone name every machine reads alike: one the CLDR maps to a Windows zone.</summary>
    public static bool KnownZone(string? name) => name is not null && Windows.ContainsKey(name);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> WindowsZones = new(() => TimeZoneNames.Table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> Windows => WindowsZones.Value;

    /// <summary>The first occurrence of a schedule at or after an instant, in a zone; null when it has none within the horizon.</summary>
    public DateTimeOffset? NextScheduled(DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTime start = TimeZoneInfo.ConvertTime(after, zone).DateTime.Date;
        for (int day = -1; day <= Horizon; day++)
        {
            DateTime date = start.AddDays(day);
            if (ScheduleDays.Count > 0 && !ScheduleDays.Contains(date.DayOfWeek))
            {
                continue;
            }

            foreach (TimeOnly time in Times)
            {
                DateTimeOffset instant = LocalToInstant(date.Add(time.ToTimeSpan()), zone);
                if (instant >= after)
                {
                    return instant;
                }
            }
        }

        return null;
    }

    /// <summary>True when the window lets the step start at an instant: inside an open span, when it names any, and outside every quiet one.</summary>
    public bool InWindow(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return (Open.Count == 0 || Open.Any(s => Inside(s, instant, zone))) && !Quiet.Any(s => Inside(s, instant, zone));
    }

    /// <summary>The first instant at or after one that the window lets the step start at; null when it opens none within the horizon.</summary>
    public DateTimeOffset? NextOpen(DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (InWindow(after, zone))
        {
            return after;
        }

        DateTime start = TimeZoneInfo.ConvertTime(after, zone).DateTime.Date;
        for (int day = -1; day <= Horizon; day++)
        {
            DateTime date = start.AddDays(day);
            List<DateTimeOffset> candidates = Open.Where(s => s.Days.Count == 0 || s.Days.Contains(date.DayOfWeek)).Select(s => LocalToInstant(date.Add(s.From.ToTimeSpan()), zone))
                .Concat(Quiet.Where(s => s.Days.Count == 0 || s.Days.Contains(date.DayOfWeek)).Select(s => End(s, date, zone)))
                .Where(c => c >= after).Order().ToList();
            foreach (DateTimeOffset candidate in candidates)
            {
                if (InWindow(candidate, zone))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>The timing in words, for a position's reasons and the surfaces that show a due time.</summary>
    public string Describe()
    {
        string start = Start switch
        {
            "delay" => $"{Text(After)} after {(From == Close ? "the close of the steps it waits on" : From == Activation ? "the run's activation" : From!.Contains('.', StringComparison.Ordinal) ? $"the time of item {From}" : $"{From}'s close")}",
            "due" => $"{Text(After)} after {From}'s close",
            "at" => Time is not null ? $"at {Time}" : $"at the time field {Field} of item {Output} gives",
            "schedule" => $"on its schedule, {(ScheduleDays.Count == 0 ? "every day" : string.Join(", ", ScheduleDays.Select(DayName)))} at {string.Join(", ", Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)))}",
            "event" => $"on a {EventKind} event whose predicate holds",
            "watch" => $"watched every {Text(Every)}, for at most {Text(Limit)}",
            _ => "instant",
        };
        var parts = new List<string> { start };
        if (Open.Count > 0)
        {
            parts.Add("within " + string.Join("; ", Open));
        }

        if (Quiet.Count > 0)
        {
            parts.Add("outside the quiet hours " + string.Join("; ", Quiet));
        }

        if (Deadline is TimeSpan deadline)
        {
            parts.Add($"with a deadline {Text(deadline)} after it is due, missed {Missed switch { "late" => "running late", "skip" => "skipping it", _ => "asking the sponsor" }}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>A duration as ISO 8601 text, as a flow writes it.</summary>
    internal static string Text(TimeSpan? duration)
    {
        if (duration is not TimeSpan value)
        {
            return "?";
        }

        var text = new System.Text.StringBuilder("P");
        if (value.Days > 0)
        {
            text.Append(value.Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        }

        if (value.Hours > 0 || value.Minutes > 0 || value.Seconds > 0)
        {
            text.Append('T');
            if (value.Hours > 0) text.Append(value.Hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            if (value.Minutes > 0) text.Append(value.Minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            if (value.Seconds > 0) text.Append(value.Seconds.ToString(CultureInfo.InvariantCulture)).Append('S');
        }

        return text.Length == 1 ? "PT0S" : text.ToString();
    }

    /// <summary>An instant with its offset, such as 2026-11-20T09:00:00Z or 2026-11-20T09:00:00+01:00.</summary>
    public static bool ParseInstant(string text, out DateTimeOffset instant)
    {
        instant = default;
        return text is not null && InstantPattern().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant);
    }

    /// <summary>A local date and time without an offset, such as 2026-11-20T09:00, read in a zone.</summary>
    public static bool LocalTime(string text, out DateTime local)
    {
        local = default;
        return text is not null && LocalPattern().IsMatch(text)
            && DateTime.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
    }

    private static bool Inside(Span span, DateTimeOffset instant, TimeZoneInfo zone)
    {
        DateTime date = TimeZoneInfo.ConvertTime(instant, zone).DateTime.Date;
        foreach (DateTime day in new[] { date.AddDays(-1), date })
        {
            if (span.Days.Count > 0 && !span.Days.Contains(day.DayOfWeek))
            {
                continue;
            }

            DateTimeOffset from = LocalToInstant(day.Add(span.From.ToTimeSpan()), zone);
            DateTimeOffset to = End(span, day, zone);
            if (from <= instant && instant < to)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset End(Span span, DateTime day, TimeZoneInfo zone) =>
        LocalToInstant((span.To <= span.From ? day.AddDays(1) : day).Add(span.To.ToTimeSpan()), zone);

    private static List<Span> Spans(JsonElement window, string name) =>
        TJson.Array(window, name).Select(s => TimeOf(TJson.Text(s, "from")) is TimeOnly from && TimeOf(TJson.Text(s, "to")) is TimeOnly to ? new Span(DaysOf(s), from, to) : null).OfType<Span>().ToList();

    private static List<DayOfWeek> DaysOf(JsonElement owner) =>
        TJson.Strings(owner, "days").Select(d => Days.IndexOf(d)).Where(i => i >= 0).Distinct().Order().Select(i => (DayOfWeek)((i + 1) % 7)).ToList();

    private static string DayName(DayOfWeek day) => Days[((int)day + 6) % 7];

    private static TimeOnly? TimeOf(string? text) =>
        text is not null && ClockTime().IsMatch(text) ? new TimeOnly(int.Parse(text[..2], CultureInfo.InvariantCulture), int.Parse(text[3..], CultureInfo.InvariantCulture)) : null;

    private static void DayList(BodyReader owner, string name)
    {
        IReadOnlyList<string>? days = owner.StringArray(name, false, allowEmpty: false, maxItemLength: 20);
        if (days is not null && (days.Any(d => !Days.Contains(d)) || days.Distinct(StringComparer.Ordinal).Count() != days.Count))
        {
            owner.Error("flow.when", name, $"Days are named once each, from {string.Join(", ", Days)}.");
        }
    }

    private static void Duration(BodyReader body, string name, bool required)
    {
        string? text = body.Text(name, required, 40);
        if (text is not null && FlowContract.ParseDuration(text) is null)
        {
            body.Error("flow.duration", name, "A duration is ISO 8601 days, hours, minutes and seconds, such as P7D or PT6H, from one second to ten years.");
        }
    }

    /// <summary>A step a timing counts from, or step.item for an output: declared before the step, with that item when one is named.</summary>
    private static void Earlier(BodyReader owner, string name, string reference, TimingShape shape, string why)
    {
        if (shape.Earlier is null)
        {
            return;
        }

        string[] parts = reference.Split('.');
        IReadOnlyCollection<string>? outputs = shape.Earlier(parts[0]);
        if (outputs is null)
        {
            owner.Error("flow.when", name, $"'{parts[0]}' is not a step declared before this one; {why}.");
        }
        else if (parts.Length == 2 && !outputs.Contains(parts[1]))
        {
            owner.Error("flow.when", name, $"Step '{parts[0]}' declares no item '{parts[1]}'; {why}.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)?$")]
    private static partial Regex Point();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*){0,7}$")]
    private static partial Regex FieldPath();

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex ClockTime();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex InstantPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$")]
    private static partial Regex LocalPattern();
}
