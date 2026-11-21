// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// A standing agent as every surface shows it (from 0.9.4, GOAL-07; the estate's design/standing-agents.md § 11): its state, its next
/// occurrence, its occurrences with their states and deferrals, its runs and spend in the period, its concurrency and its failure pause.
/// goal due gives it in full; status, progress, the goal view, Workbench and Remote give its summary. Reading it records nothing.
/// </summary>
internal static class StandingView
{
    /// <summary>The format of goal due's machine form.</summary>
    public const string DueFormat = "vericairn.due/1";

    /// <summary>goal due in words: the agent, its triggers and when each is next due, its period, and each occurrence with its state.</summary>
    public static string RenderText(EstateModel model, StandingAgents.Agent agent)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(agent);
        var text = new StringBuilder();
        text.Append("Standing agent ").Append(EstateModel.BodyString(agent.Goal, "title")).Append(" [goal ").Append(agent.Goal.Id).Append("], as of ").Append(Time(agent.AsOf)).Append('\n');
        text.Append("  State: ").Append(agent.State).Append(": ").Append(agent.Reason).Append('\n');
        if (agent.Flow is not null)
        {
            text.Append("  Flow: ").Append(agent.Flow.Pin.Id).Append(' ').Append(agent.Flow.Pin.Version).Append(agent.Step is null ? string.Empty : $", step {agent.Step.Key} in the {agent.Step.Role} role").Append('\n');
        }

        if (agent.Until is { } until)
        {
            text.Append("  Until: ").Append(Time(until)).Append(", when it returns to its sponsor").Append('\n');
        }

        if (agent.RunsOn is string runsOn)
        {
            text.Append("  Dispatched unattended only by the runtime ").Append(runsOn).Append('\n');
        }

        if (agent.Period is StandingAgents.Period period)
        {
            text.Append("  Period: ").Append(Time(period.Start)).Append(" to ").Append(Time(period.End)).Append(": ")
                .Append(string.Join(", ", period.Caps.Select(c => $"{c.Key} {period.Spent.GetValueOrDefault(c.Key).ToString(CultureInfo.InvariantCulture)} of {c.Value.ToString(CultureInfo.InvariantCulture)}{(c.Key == "currency" ? " minor units of " + period.Currency : string.Empty)}"))).Append('\n');
        }

        text.Append("  Runs in flight: ").Append(agent.InFlight.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(agent.Concurrency.ToString(CultureInfo.InvariantCulture))
            .Append("; failed in a row: ").Append(agent.Failures.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(agent.FailureLimit.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("  Open outputs: ").Append(agent.OpenOutputs.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(agent.OpenCap.ToString(CultureInfo.InvariantCulture))
            .Append(", each a possibility, ask or publication request another principal disposes of").Append('\n');
        text.Append("Triggers\n");
        foreach (StandingAgents.Trigger trigger in agent.Triggers)
        {
            text.Append("  ").Append(trigger.Key).Append(" (").Append(trigger.Kind).Append("): next ")
                .Append(agent.Next.GetValueOrDefault(trigger.Key) is { } next ? Time(next) : trigger.Kind switch { "event" => "on a new record", "condition" => "when its condition fails", "request" => "on request", "duty" => "as the duty falls due", _ => "unknown" })
                .Append('\n');
        }

        text.Append("Occurrences\n");
        if (agent.Occurrences.Count == 0)
        {
            text.Append("  none\n");
        }

        foreach (StandingAgents.OccurrenceState occurrence in agent.Occurrences)
        {
            text.Append("  ").Append(occurrence.State).Append("  ").Append(occurrence.Occurrence.Key).Append(" (").Append(occurrence.Occurrence.Kind).Append(", due ").Append(Time(occurrence.Occurrence.DueAt)).Append("): ").Append(occurrence.Reason).Append('\n');
        }

        if (agent.Due.FirstOrDefault() is { } first)
        {
            text.Append("Next: vericairn goal step ").Append(agent.Goal.Id).Append(" --occurrence ").Append(first.Occurrence.Key).Append(" --by <principal> carries it.\n");
        }

        return text.ToString();
    }

    /// <summary>goal due's machine form, of format vericairn.due/1.</summary>
    public static JsonObject Json(EstateModel model, StandingAgents.Agent agent)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(agent);
        JsonObject json = Summary(model, agent);
        json["format"] = DueFormat;
        json["standing"] = true;
        json["triggers"] = new JsonArray(agent.Triggers.Select(t => (JsonNode)new JsonObject
        {
            ["key"] = t.Key, ["kind"] = t.Kind, ["revision"] = t.Revision, ["next"] = agent.Next.GetValueOrDefault(t.Key) is { } next ? Time(next) : null,
        }).ToArray());
        json["occurrences"] = new JsonArray(agent.Occurrences.Select(o => (JsonNode)Occurrence(o)).ToArray());
        return json;
    }

    /// <summary>The agent's summary, as status, progress, the goal view, Workbench and Remote show it.</summary>
    public static JsonObject Summary(EstateModel model, StandingAgents.Agent agent)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(agent);
        StandingAgents.Period? period = agent.Period;
        return new JsonObject
        {
            ["goal"] = new JsonObject { ["id"] = agent.Goal.Id, ["title"] = EstateModel.BodyString(agent.Goal, "title"), ["status"] = agent.Status },
            ["asOf"] = Time(agent.AsOf),
            ["state"] = agent.State,
            ["reason"] = agent.Reason,
            ["flow"] = agent.Flow is null ? null : new JsonObject { ["id"] = agent.Flow.Pin.Id, ["version"] = agent.Flow.Pin.Version, ["hash"] = agent.Flow.Pin.Hash, ["step"] = agent.Step?.Key, ["role"] = agent.Step?.Role },
            ["until"] = agent.Until is { } until ? Time(until) : null,
            ["runsOn"] = agent.RunsOn,
            ["next"] = agent.NextAt is { } next ? Time(next) : null,
            ["counts"] = new JsonObject
            {
                ["due"] = agent.Occurrences.Count(o => o.State == "due"),
                ["deferred"] = agent.Occurrences.Count(o => o.State == "deferred"),
                ["running"] = agent.Occurrences.Count(o => o.State is "running" or "assigned"),
                ["settled"] = agent.Occurrences.Count(o => o.State == "settled"),
                ["held"] = agent.Occurrences.Count(o => o.State == "held"),
            },
            ["period"] = period is null ? null : new JsonObject
            {
                ["unit"] = period.Unit, ["every"] = period.Every, ["start"] = Time(period.Start), ["end"] = Time(period.End), ["currency"] = period.Currency,
                ["caps"] = new JsonObject(period.Caps.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value))),
                ["spent"] = new JsonObject(period.Caps.Keys.Select(k => KeyValuePair.Create(k, (JsonNode?)period.Spent.GetValueOrDefault(k)))),
                ["reached"] = new JsonArray(period.Reached.Select(r => (JsonNode)r).ToArray()),
            },
            ["concurrency"] = new JsonObject { ["inFlight"] = agent.InFlight, ["cap"] = agent.Concurrency },
            ["failures"] = new JsonObject { ["consecutive"] = agent.Failures, ["limit"] = agent.FailureLimit, ["latest"] = agent.FailureTrigger is null ? null : Reference(agent.FailureTrigger) },
            ["outputs"] = new JsonObject { ["open"] = agent.OpenOutputs, ["cap"] = agent.OpenCap },
        };
    }

    /// <summary>The agent's summary in one line.</summary>
    public static string Line(StandingAgents.Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        string next = agent.NextAt is { } at ? $"next {Time(at)}" : "next on a record or a request";
        string period = agent.Period is StandingAgents.Period p ? $"; this period {p.Spent.GetValueOrDefault("runs").ToString(CultureInfo.InvariantCulture)} of {p.Caps.GetValueOrDefault("runs").ToString(CultureInfo.InvariantCulture)} runs, {p.Spent.GetValueOrDefault("wallMinutes").ToString(CultureInfo.InvariantCulture)} of {p.Caps.GetValueOrDefault("wallMinutes").ToString(CultureInfo.InvariantCulture)} minutes" : string.Empty;
        int due = agent.Occurrences.Count(o => o.State == "due");
        int deferred = agent.Occurrences.Count(o => o.State == "deferred");
        int settled = agent.Occurrences.Count(o => o.State == "settled");
        return $"{EstateModel.BodyString(agent.Goal, "title")} [goal {agent.Goal.Id}]: {agent.State}; {due.ToString(CultureInfo.InvariantCulture)} due, {deferred.ToString(CultureInfo.InvariantCulture)} deferred, {settled.ToString(CultureInfo.InvariantCulture)} awaiting review, {agent.OpenOutputs.ToString(CultureInfo.InvariantCulture)} outputs open; {next}{period}";
    }

    private static JsonObject Occurrence(StandingAgents.OccurrenceState state) => new()
    {
        ["key"] = state.Occurrence.Key,
        ["trigger"] = state.Occurrence.Trigger,
        ["kind"] = state.Occurrence.Kind,
        ["revision"] = state.Occurrence.Revision,
        ["item"] = JsonNode.Parse(state.Occurrence.Item.ValueKind == JsonValueKind.Undefined ? "null" : state.Occurrence.Item.GetRawText()),
        ["dueAt"] = Time(state.Occurrence.DueAt),
        ["state"] = state.State,
        ["reason"] = state.Reason,
        ["assignment"] = state.Assignment?.Id,
        ["run"] = state.Run?.Id,
        ["result"] = state.Result?.Id,
        ["records"] = new JsonArray(state.Occurrence.Records.Select(r => (JsonNode)new JsonObject { ["type"] = r.Type, ["id"] = r.Id, ["hash"] = r.Hash }).ToArray()),
    };

    private static JsonObject Reference(RecordEnvelope record) => new() { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };

    private static string Time(DateTimeOffset at) => RecordParser.FormatTimestamp(at);
}
