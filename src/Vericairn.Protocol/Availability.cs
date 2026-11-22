// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (GOAL-11; design/events-and-presence.md § 9): a person's declared availability, on their communication setting. People
/// declare; nothing infers it from a connection or from silence.
/// </summary>
/// <param name="State">available, or away.</param>
/// <param name="Until">For away, when the person is back (RFC 3339 UTC).</param>
/// <param name="From">For away, when the absence begins; at once when omitted.</param>
public sealed record CommunicationAvailability(string State, string? Until = null, string? From = null);

/// <summary>
/// From 0.9.4 (GOAL-11; design/events-and-presence.md § 9): weekly windows in an IANA time zone that hold a person's notifications,
/// except those of the categories that break through. Quiet hours change delivery only: views, attention and routing are unaffected.
/// </summary>
/// <param name="Zone">The IANA time zone the windows are read in, such as Europe/Berlin.</param>
/// <param name="Windows">At most 14 weekly windows.</param>
/// <param name="Breakthrough">The attention categories delivered inside a window; incident when omitted.</param>
public sealed record QuietHoursSetting(string Zone, IReadOnlyList<QuietHoursWindow> Windows, IReadOnlyList<string>? Breakthrough = null);

/// <summary>From 0.9.4 (GOAL-11): one weekly quiet window, starting on each of its days; one whose end is not after its start runs past midnight.</summary>
/// <param name="Days">The days it starts on: mon, tue, wed, thu, fri, sat, sun.</param>
/// <param name="Start">When it starts, HH:MM in the setting's zone.</param>
/// <param name="End">When it ends, HH:MM, 24:00 for midnight at the day's end.</param>
public sealed record QuietHoursWindow(IReadOnlyList<string> Days, string Start, string End);

/// <summary>From 0.9.4 (GOAL-11): a person's availability and quiet hours as of a read.</summary>
/// <param name="Away">True while the person is away.</param>
/// <param name="AwayUntil">When the person is back, while away.</param>
/// <param name="Quiet">True while the person's quiet hours hold notifications.</param>
/// <param name="QuietUntil">When the current quiet window ends, while quiet.</param>
public sealed record AvailabilityState(bool Away, string? AwayUntil, bool Quiet, string? QuietUntil);

/// <summary>
/// From 0.9.4 (GOAL-11; design/events-and-presence.md § 9): the rules of availability and quiet hours, shared by the runtime and its
/// clients. A notification of a category that does not break through, due inside a quiet window, is held until the window ends; a
/// reminder due inside one arrives at its start. A zone this machine does not know holds nothing, so nothing is lost to it.
/// </summary>
public static partial class AvailabilityRules
{
    /// <summary>The availability states.</summary>
    public static ImmutableArray<string> States { get; } = ["available", "away"];

    /// <summary>The days a window starts on, Monday first.</summary>
    public static ImmutableArray<string> Days { get; } = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>The categories that break through quiet hours when a setting names none: incident, which includes continuity concerns.</summary>
    public static ImmutableArray<string> DefaultBreakthrough { get; } = ["incident"];

    /// <summary>The most windows one setting holds.</summary>
    public const int MaxWindows = 14;

    /// <summary>Why an availability is malformed; null when it is well formed.</summary>
    public static string? Refusal(CommunicationAvailability? availability)
    {
        if (availability is null)
        {
            return null;
        }

        if (!States.Contains(availability.State))
        {
            return "Availability is available, or away with until and an optional from.";
        }

        if (availability.State == "available")
        {
            return availability.Until is null && availability.From is null ? null : "An available person names no until or from.";
        }

        if (Time(availability.Until) is not DateTimeOffset until)
        {
            return "Away names until, when the person is back, as a whole-second UTC time such as 2026-12-02T09:00:00Z.";
        }

        return availability.From is null ? null
            : Time(availability.From) is not DateTimeOffset from ? "Away's from is a whole-second UTC time such as 2026-12-02T09:00:00Z."
            : from >= until ? "Away's from comes before its until." : null;
    }

    /// <summary>Why quiet hours are malformed; null when they are well formed.</summary>
    public static string? Refusal(QuietHoursSetting? quiet)
    {
        if (quiet is null)
        {
            return null;
        }

        if (quiet.Zone is null || !ZonePattern().IsMatch(quiet.Zone))
        {
            return "Quiet hours name an IANA time zone, such as Europe/Berlin or UTC.";
        }

        if (quiet.Windows is null || quiet.Windows.Count is 0 or > MaxWindows)
        {
            return $"Quiet hours hold 1 to {MaxWindows} weekly windows.";
        }

        foreach (QuietHoursWindow window in quiet.Windows)
        {
            if (window?.Days is null || window.Days.Count == 0 || window.Days.Any(d => !Days.Contains(d)) || window.Days.Distinct(StringComparer.Ordinal).Count() != window.Days.Count)
            {
                return "Each quiet window names the days it starts on, each once: mon, tue, wed, thu, fri, sat or sun.";
            }

            if (Minutes(window.Start) is not int start || start == 24 * 60 || Minutes(window.End) is not int end || start == end)
            {
                return "Each quiet window starts and ends at HH:MM (24:00 ends at midnight), and its start and end differ.";
            }
        }

        return quiet.Breakthrough is { } categories && categories.Any(c => !EventVocabulary.Categories.Contains(c))
            ? "Quiet hours' breakthrough names attention categories: decision-needed, blocked, uncertain-effect, failed-check, incident or information."
            : null;
    }

    /// <summary>True when an availability says the person is away at a time: from its from, or at once, until its until.</summary>
    public static bool AwayAt(CommunicationAvailability? availability, DateTimeOffset at) =>
        availability is { State: "away" } && Time(availability.Until) is DateTimeOffset until && at < until
        && (availability.From is null || Time(availability.From) is DateTimeOffset from && at >= from);

    /// <summary>
    /// When the quiet window a time falls in ends, or null when no window holds it: outside every window, or in a zone this machine does
    /// not know.
    /// </summary>
    public static DateTimeOffset? QuietUntil(QuietHoursSetting? quiet, DateTimeOffset at) => Window(quiet, at)?.End;

    /// <summary>
    /// When a notification of a category due at a time is delivered: then, when its category breaks through or no window holds it;
    /// otherwise when the window ends, and held notifications arrive together then.
    /// </summary>
    public static DateTimeOffset DeliverAt(QuietHoursSetting? quiet, string category, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(category);
        IReadOnlyList<string> breakthrough = quiet?.Breakthrough ?? DefaultBreakthrough;
        return breakthrough.Contains(category, StringComparer.Ordinal) ? at : Window(quiet, at)?.End ?? at;
    }

    /// <summary>When a reminder due at a time arrives: at the start of the quiet window it falls in, or when due.</summary>
    public static DateTimeOffset ReminderAt(QuietHoursSetting? quiet, DateTimeOffset due) => Window(quiet, due)?.Start ?? due;

    /// <summary>A person's availability and quiet hours as of a time.</summary>
    public static AvailabilityState StateAt(CommunicationAvailability? availability, QuietHoursSetting? quiet, DateTimeOffset at)
    {
        bool away = AwayAt(availability, at);
        DateTimeOffset? until = QuietUntil(quiet, at);
        return new AvailabilityState(away, away ? availability!.Until : null, until is not null, until is DateTimeOffset end ? Format(end) : null);
    }

    /// <summary>The window holding a time, as UTC instants, or null.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End)? Window(QuietHoursSetting? quiet, DateTimeOffset at)
    {
        if (quiet is null || Refusal(quiet) is not null || Zone(quiet.Zone) is not TimeZoneInfo zone)
        {
            return null;
        }

        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        (DateTimeOffset Start, DateTimeOffset End)? found = null;
        // A window starting yesterday may run into today; one starting today may hold the time.
        for (int back = 1; back >= 0; back--)
        {
            DateTime day = local.Date.AddDays(-back);
            string name = Days[((int)day.DayOfWeek + 6) % 7];
            foreach (QuietHoursWindow window in quiet.Windows.Where(w => w.Days.Contains(name, StringComparer.Ordinal)))
            {
                int start = Minutes(window.Start)!.Value;
                int end = Minutes(window.End)!.Value;
                DateTime opens = day.AddMinutes(start);
                DateTime closes = day.AddMinutes(end <= start ? end + 24 * 60 : end);
                if (local >= opens && local < closes && Instant(closes, zone) is DateTimeOffset endsAt && (found is null || endsAt > found.Value.End))
                {
                    // Of overlapping windows, the one that ends last holds the time.
                    found = (Instant(opens, zone), endsAt);
                }
            }
        }

        return found;
    }

    private static DateTimeOffset Instant(DateTime local, TimeZoneInfo zone)
    {
        // A local time a clock change skips is read an hour on; an ambiguous one as its first occurrence.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    private static TimeZoneInfo? Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return null;
        }
    }

    private static int? Minutes(string? text)
    {
        if (text is null || !TimePattern().IsMatch(text))
        {
            return null;
        }

        int hours = int.Parse(text.AsSpan(0, 2), CultureInfo.InvariantCulture);
        int minutes = int.Parse(text.AsSpan(3, 2), CultureInfo.InvariantCulture);
        return hours == 24 && minutes != 0 ? null : hours * 60 + minutes;
    }

    private static DateTimeOffset? Time(string? text) =>
        text is not null && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value)
            ? value : null;

    private static string Format(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\A(?:[01][0-9]|2[0-4]):[0-5][0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_+\-]{0,63}(?:/[A-Za-z0-9_+\-]{1,64}){0,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ZonePattern();
}
