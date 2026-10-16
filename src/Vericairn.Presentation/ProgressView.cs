// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;

namespace Vericairn.Presentation;

/// <summary>One line of the progress summary: what it is about, and what the report says of it in the state language.</summary>
/// <param name="Heading">The section of the report.</param>
/// <param name="Detail">Its summary, counted from the report beside each count's denominator.</param>
public sealed record ProgressRow(string Heading, string Detail);

/// <summary>
/// The progress report's summary as a surface shows it beside attention: one row per section of the report the runtime renders
/// (<c>GET /v1/progress</c>), worded in the state language. It reads the report's own figures and states and derives none: each row
/// counts the entries the report lists, and a report the runtime did not give is shown as unavailable, never as empty or zero.
/// </summary>
public static class ProgressView
{
    /// <summary>The summary rows of a report in its machine form; one row saying the report is unavailable when there is none.</summary>
    public static IReadOnlyList<ProgressRow> Summary(JsonElement report)
    {
        if (report.ValueKind != JsonValueKind.Object || Str(report, "format") != "vericairn.progress/1")
        {
            return [new ProgressRow("Progress", "unavailable: the runtime gave no progress report")];
        }

        return [Standing(report), WorkOrders(report), Releases(report), Next(report), Eligible(report), Operator(report),
            .. Items(report, "agentClaims").Select(c => new ProgressRow("Agent claim · " + Str(c, "phase"), $"{Str(c, "note")} (attempt {Str(c, "attempt")}; not a worker observation)")),
            .. Items(report, "runSignals").SelectMany(r => Items(r, "signals").Select(s => new ProgressRow($"Worker · {Str(s, "severity")} {Str(s, "code")}",
                $"{Str(s, "summary")} (run {Str(r, "run")}; {(Str(s, "attention") is string ask ? "ask " + ask : "attention recording pending")})")))];
    }

    private static ProgressRow Standing(JsonElement report)
    {
        JsonElement standing = Obj(report, "standing");
        JsonElement pin = Obj(standing, "pin");
        JsonElement format = Obj(standing, "recordFormat");
        JsonElement check = Obj(standing, "check");
        string pinned = pin.ValueKind == JsonValueKind.Object ? "pinned to " + Str(pin, "describe") : "pin unknown: the manifest did not load";
        string recordFormat = format.ValueKind == JsonValueKind.Object ? "record format " + Int(format, "format").ToString(CultureInfo.InvariantCulture) : "record format unknown";
        string checkText = $"check: {Count(Int(check, "errors"), StateLanguage.Label("finding-severity", "error"))}, {Count(Int(check, "warnings"), StateLanguage.Label("finding-severity", "warning"))}";
        return new ProgressRow("Where the estate stands", $"{pinned} · {recordFormat} · {checkText}");
    }

    private static ProgressRow WorkOrders(JsonElement report)
    {
        JsonElement orders = Obj(report, "workOrders");
        List<string> states = Items(orders, "states").Select(s => $"{Int(s, "count").ToString(CultureInfo.InvariantCulture)} {StateLanguage.Label("work-order", Str(s, "state"))}").ToList();
        return new ProgressRow("Work orders", $"{Int(orders, "total").ToString(CultureInfo.InvariantCulture)} current{(states.Count == 0 ? string.Empty : ": " + string.Join("; ", states))}");
    }

    private static ProgressRow Releases(JsonElement report)
    {
        JsonElement releases = Obj(report, "releases");
        List<JsonElement> published = Items(releases, "published");
        string recorded = Int(releases, "recorded").ToString(CultureInfo.InvariantCulture);
        if (published.Count == 0)
        {
            return new ProgressRow("Published releases", $"none of the {recorded} recorded");
        }

        JsonElement latest = published[^1];
        string date = Str(latest, "publishedAt") is { Length: >= 10 } at ? at[..10] : "?";
        return new ProgressRow("Published releases", $"{published.Count.ToString(CultureInfo.InvariantCulture)} of the {recorded} recorded; the latest {Str(latest, "version")}, {StateLanguage.Label("release", "published")} {date}");
    }

    private static ProgressRow Next(JsonElement report)
    {
        List<string> releases = Items(Obj(report, "next"), "releases").Select(r =>
        {
            JsonElement order = Obj(r, "workOrder");
            return $"{Str(order, "key")} {StateLanguage.Label("work-order", Str(order, "status"))}, {Int(r, "accepted").ToString(CultureInfo.InvariantCulture)} of {Count(Items(r, "carries").Count, "work order")} it carries accepted";
        }).ToList();
        return new ProgressRow("What comes next", releases.Count == 0 ? "no release work order waits for its acceptance" : string.Join("; ", releases));
    }

    private static ProgressRow Eligible(JsonElement report)
    {
        JsonElement eligible = Obj(report, "eligible");
        return new ProgressRow("Next eligible work", $"{Items(eligible, "eligible").Count.ToString(CultureInfo.InvariantCulture)} of {Count(Int(eligible, "planned"), "planned work order")}; {Items(eligible, "held").Count.ToString(CultureInfo.InvariantCulture)} held by an after or a blocker");
    }

    private static ProgressRow Operator(JsonElement report)
    {
        JsonElement waiting = Obj(report, "operator");
        List<JsonElement> asks = Items(waiting, "asks");
        List<string> states = asks.GroupBy(a => Str(a, "state") ?? "?", StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count().ToString(CultureInfo.InvariantCulture)} {StateLanguage.Label("ask", g.Key)}").ToList();
        JsonElement acceptances = Obj(waiting, "acceptances");
        int due = Items(acceptances, "milestoneWorkOrders").Count + Items(acceptances, "milestoneResults").Count + Items(acceptances, "releaseCandidates").Count + Items(acceptances, "goals").Count;
        return new ProgressRow("Waits on the operator", $"asks: {(states.Count == 0 ? "none" : string.Join("; ", states))}; acceptances only the operator gives: {due.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string Count(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");

    private static List<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];

    private static JsonElement Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
