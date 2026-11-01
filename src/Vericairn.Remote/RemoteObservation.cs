// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Protocol;
using Vericairn.Presentation;

namespace Vericairn.Remote;

/// <summary>The last successful home observation, and independently dated work details. Restoring this grants no current access.</summary>
public sealed record RemoteObservation(StatusResponse Status, string AsOf, IReadOnlyList<ProductItem> Products,
    AttentionResponse Attention, CommunicationProfile Communication, IReadOnlyDictionary<string, RemoteWorkDetail> Details);

/// <summary>A complete observed work detail. The worker's observations remain separate from the contributor's claims.</summary>
public sealed record RemoteWorkDetail(string Id, string? Assignment, string AsOf, ObserveResponse Work,
    IReadOnlyList<RunRow> Runs, IReadOnlyList<RequestedProgress> Progress)
{
    /// <summary>The work's title.</summary>
    [JsonIgnore]
    public string Title => Text(Body, "key") + " " + Text(Body, "title");
    /// <summary>The work's goal and exit question, from its record.</summary>
    [JsonIgnore]
    public string Goal => Text(Body, "intent") + "\nExit question: " + Text(Body, "exitQuestion");
    /// <summary>The work scope and each complete admitted assignment brief.</summary>
    [JsonIgnore]
    public string Scope => "Current work scope\n" + Describe(Property(Body, "scope")) + "\n" + string.Join("\n", Assignments.Select(a =>
        "Assignment " + Text(a, "id") + " at " + Text(a, "hash") + "; pinned work-order " + Text(a, "workOrderHash") +
        (Property(a, "historical").ValueKind == JsonValueKind.True ? "\nHistorical assignment; its exact admitted brief follows." : "") +
        "\nAdmitted brief\n" + Describe(Property(a, "brief"))));
    /// <summary>Every attempt and its recorded start, note and result, including limitations and unresolved effects.</summary>
    [JsonIgnore]
    public string History => string.Join("\n\n", Assignments.Select(a => "Assignment " + Text(a, "id") + "; contributor " + Text(a, "contributor") + "\n" +
        (Rows(a, "attempts").Any() ? string.Join("\n\n", Rows(a, "attempts").Select(t => "Attempt " + Text(t, "id") + " at " + Text(t, "hash") +
            "; started " + Text(t, "startedAt") + "\n" + Text(t, "note") + "\n" +
            (Property(t, "result").ValueKind == JsonValueKind.Object ? "Result " + Describe(Property(t, "result")) : "No result observed."))) : "No attempts observed.")));
    /// <summary>The outcome of each assignment's latest result, without treating a claim as acceptance.</summary>
    [JsonIgnore]
    public string LatestResult => string.Join("\n", Assignments.Select(a =>
    {
        JsonElement last = Rows(a, "attempts").LastOrDefault(t => Property(t, "result").ValueKind == JsonValueKind.Object);
        JsonElement result = Property(last, "result");
        return "Assignment " + Text(a, "id") + ": " + (result.ValueKind == JsonValueKind.Object
            ? StateLanguage.Label("result-outcome", Text(Property(result, "body"), "outcome")) + "; " + Text(result, "state") + "; result " + Text(result, "id")
            : "no result observed");
    }));

    private JsonElement Body => Property(Property(Work.Json, "workOrder"), "body");
    private IEnumerable<JsonElement> Assignments => Rows(Work.Json, "assignments").Where(a => Assignment is null || Text(a, "id") == Assignment);

    internal static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found) ? found : default;
    internal static string Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString()! : "unknown";
    internal static JsonElement.ArrayEnumerator Rows(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.Array } rows ? rows.EnumerateArray() : default;

    // Retain every brief/result field, including future additions, without abbreviating evidence to fit a phone.
    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => string.Join("\n", value.EnumerateObject().Select(p => Words(p.Name) + ": " + Describe(p.Value))),
        JsonValueKind.Array => value.GetArrayLength() == 0 ? "none" : string.Join("\n", value.EnumerateArray().Select(Describe)),
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Null or JsonValueKind.Undefined => "not observed",
        _ => value.GetRawText(),
    };

    private static string Words(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value) { if (char.IsUpper(c)) result.Append(' '); result.Append(char.ToLowerInvariant(c)); }
        return result.ToString();
    }
}

internal static class RemoteFreshness
{
    internal static string Age(string at, TimeProvider clock)
    {
        if (!DateTimeOffset.TryParse(at.Replace(" UTC", "Z", StringComparison.Ordinal), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset observed)) return "age unknown";
        double seconds = (clock.GetUtcNow() - observed).TotalSeconds;
        if (seconds < 0) return "age unknown; observation is ahead of this phone's clock";
        return ((long)seconds).ToString(CultureInfo.InvariantCulture) + " seconds old";
    }
}
