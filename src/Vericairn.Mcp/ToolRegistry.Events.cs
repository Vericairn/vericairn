// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    /// <summary>The most envelopes one events_read returns.</summary>
    public const int EventsReadMaxLimit = 256;

    private const int EventsReadDefaultLimit = 100;

    private const string EventsReadDescription = "From 0.9.2: the events after a cursor that the connected principal may read, for a client that does not show notifications: each a vericairn.event/1 envelope naming only identities, hashes, kinds, states and categories, never content, so read its subject through its own tool. Without after it starts at the journal's head; the cursor it returns continues there. A cursor it cannot honor (too old, from another epoch or sealed for another principal) answers reset with no events, and the state is read again before continuing. Attention reaches only its own principal, and operator-request events are never returned. With wait it waits up to that many seconds for an event. Reads change nothing.";

    private static JsonObject EventsReadArguments()
    {
        static JsonObject List(string description) => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["after"] = new JsonObject { ["type"] = "string", ["description"] = "The cursor to continue from: one events_read returned, or a state read's eventsCursor; omitted, the journal's head." },
                ["kinds"] = List("Event kinds to read: record, attention, ask, step and run; omitted, every kind but request, which is never returned."),
                ["types"] = List("Subject types, such as record types."),
                ["subjects"] = List("Record ids; a goal matches its work orders, steps and runs."),
                ["to"] = List("Principals concerned."),
                ["categories"] = List("Attention categories: decision-needed, blocked, uncertain-effect, failed-check, incident or information."),
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = EventsReadMaxLimit, ["description"] = $"The most envelopes returned, 1 to {EventsReadMaxLimit}; {EventsReadDefaultLimit} when omitted." },
                ["wait"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = EventVocabulary.MaxWaitSeconds, ["description"] = $"Seconds to wait for an event when none is there yet, 0 to {EventVocabulary.MaxWaitSeconds}; 0 when omitted." },
            },
            ["additionalProperties"] = false,
        };
    }

    /// <summary>
    /// events_read (MCP-05): a read of <c>GET /v1/events</c> for the acting principal, as a long poll when it waits, with that read's
    /// audience check and sealed cursors. Operator-request events are refused as a filter and dropped from every answer, and an answer
    /// they alone would fill waits on as if they were withheld; a connection acting for no principal reads no attention.
    /// </summary>
    private async Task<ToolCallResult> EventsReadAsync(IEstateApi api, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!TryStrings(arguments, "kinds", out IReadOnlyList<string>? kinds, out string? problem)
            || !TryStrings(arguments, "types", out IReadOnlyList<string>? types, out problem)
            || !TryStrings(arguments, "subjects", out IReadOnlyList<string>? subjects, out problem)
            || !TryStrings(arguments, "to", out IReadOnlyList<string>? to, out problem)
            || !TryStrings(arguments, "categories", out IReadOnlyList<string>? categories, out problem)
            || !TryWhole(arguments, "limit", EventsReadDefaultLimit, 1, EventsReadMaxLimit, out int limit, out problem)
            || !TryWhole(arguments, "wait", 0, 0, EventVocabulary.MaxWaitSeconds, out int wait, out problem))
        {
            return new ToolCallResult(problem!, true);
        }

        if (kinds?.Contains("request", StringComparer.Ordinal) == true || types?.Any(McpEvents.OperatorRequestTypes.Contains) == true)
        {
            return new ToolCallResult("Operator-request events never reach MCP: the operator queue stays off it. Name other kinds and types.", true);
        }

        if (actingPrincipal is null && kinds?.Contains("attention", StringComparer.Ordinal) == true)
        {
            return new ToolCallResult("Attention reaches only its own principal, and this connection acts for none: serve with --as to read its attention.", true);
        }

        kinds ??= [.. EventVocabulary.Kinds.Where(k => k != "request" && (actingPrincipal is not null || k != "attention"))];
        var filter = new EventFilter(kinds, types, subjects, to, categories);
        string? after = OptionalArg(arguments, "after");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(wait);
        var kept = new List<EventEnvelope>();
        while (true)
        {
            int remaining = (int)Math.Clamp(Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds), 0, EventVocabulary.MaxWaitSeconds);
            EventsResponse read = await api.EventsAsync(after, filter, actingPrincipal, remaining, cancellationToken).ConfigureAwait(false);
            if (read.Problem is { } refused)
            {
                return new ToolCallResult(refused.Message, true);
            }

            if (read.Reset)
            {
                return Json(new EventsResponse([], read.Cursor, Reset: true));
            }

            string cursor = read.Cursor;
            foreach (EventEnvelope envelope in read.Events.Where(e => !McpEvents.OperatorRequest(e)))
            {
                if (kept.Count == limit)
                {
                    // The rest come again from the last envelope returned.
                    cursor = kept[^1].Cursor;
                    break;
                }

                kept.Add(envelope);
            }

            // An answer that only operator-request events would fill is no answer: the wait goes on, as it does for a withheld event.
            if (kept.Count > 0 || read.Events.Count == 0 || remaining == 0)
            {
                return Json(new EventsResponse(kept, cursor));
            }

            after = read.Cursor;
        }
    }

    private static bool TryStrings(JsonObject? arguments, string name, out IReadOnlyList<string>? values, out string? problem)
    {
        values = null;
        problem = null;
        switch (arguments?[name])
        {
            case null:
                return true;
            case JsonArray array when array.All(v => v is JsonValue value && value.TryGetValue(out string? _)):
                values = array.Count == 0 ? null : [.. array.Select(v => v!.GetValue<string>())];
                return true;
            case JsonValue value when value.TryGetValue(out string? single):
                values = [single];
                return true;
            default:
                problem = $"{name} is a list of strings.";
                return false;
        }
    }

    private static bool TryWhole(JsonObject? arguments, string name, int fallback, int minimum, int maximum, out int value, out string? problem)
    {
        value = fallback;
        problem = null;
        JsonNode? given = arguments?[name];
        if (given is null)
        {
            return true;
        }

        bool parsed = false;
        if (given is JsonValue number)
        {
            if (number.TryGetValue(out int whole))
            {
                (value, parsed) = (whole, true);
            }
            else if (number.TryGetValue(out string? text))
            {
                parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }
        }

        if (parsed && value >= minimum && value <= maximum)
        {
            return true;
        }

        problem = $"{name} is a whole number from {minimum} to {maximum}.";
        return false;
    }
}
