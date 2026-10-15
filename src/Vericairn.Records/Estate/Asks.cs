// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The state status derives for an ask (record format 2, the estate's design/goals.md § 8): answered once a current decision names
/// any revision of it in its answers, unless its asker withdrew it; otherwise the state its asker recorded, open or notified or
/// withdrawn. Waiting never answers an ask. The goal predicates, the progress report and every surface read this one derivation.
/// What waits on a principal is delivered as the cohesion contract asks (design/cohesion-contract.md § 5): its waiting asks oldest
/// first, at most <see cref="PerDelivery"/> in one delivery, each with its recommendation first and the consequence of leaving it
/// unanswered, and the rest waiting in order behind them. Reading what waits answers nothing.
/// </summary>
public static class Asks
{
    /// <summary>The states that still wait on the principal the ask is put to: open, and notified, whose delivery stays unknown.</summary>
    public static readonly string[] Waiting = ["open", "notified"];

    /// <summary>How many asks one delivery carries to a principal: at most three, the rest waiting in order.</summary>
    public const int PerDelivery = 3;

    /// <summary>The format of what waits, in its machine form.</summary>
    public const string Format = "vericairn.asks/1";

    /// <summary>What waits on one principal: its waiting asks, oldest first, the first <see cref="PerDelivery"/> of them delivered.</summary>
    /// <param name="Principal">The principal asked.</param>
    /// <param name="Asks">Its waiting asks, oldest first, each at its current revision.</param>
    public sealed record Delivery(string Principal, IReadOnlyList<RecordEnvelope> Asks)
    {
        /// <summary>The asks this delivery carries.</summary>
        public IReadOnlyList<RecordEnvelope> Delivered => Asks.Take(PerDelivery).ToList();

        /// <summary>The asks that wait, in order, behind those delivered.</summary>
        public IReadOnlyList<RecordEnvelope> Queued => Asks.Skip(PerDelivery).ToList();
    }

    /// <summary>An ask's derived state and the current decisions that answer it, oldest first.</summary>
    public static (string State, IReadOnlyList<RecordEnvelope> Answers) State(EstateModel model, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        RecordEnvelope latest = model.Latest(ask);
        string recorded = EstateModel.BodyString(latest, "state") ?? "open";
        if (recorded == "withdrawn")
        {
            return (recorded, []);
        }

        IReadOnlyList<string> chain = model.Chain(latest);
        List<RecordEnvelope> answers = model.Current("decision")
            .Where(d => d.Body.TryGetProperty("answers", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array && ids.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.String && chain.Contains(i.GetString()!)))
            .OrderBy(d => d.At, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        return (answers.Count > 0 ? "answered" : recorded, answers);
    }

    /// <summary>True when the state still waits on the principal the ask is put to.</summary>
    public static bool IsWaiting(string state) => Array.IndexOf(Waiting, state) >= 0;

    /// <summary>
    /// What waits on each principal, or on the one named: every current ask still waiting on it, oldest first by the time its first
    /// revision was asked, then by identifier. Principals by identifier; one with nothing waiting is left out.
    /// </summary>
    public static IReadOnlyList<Delivery> Deliveries(EstateModel model, string? to = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("ask")
            .Where(a => IsWaiting(State(model, a).State) && (to is null || EstateModel.BodyString(a, "to") == to))
            .Select(a => (Ask: a, First: model.ById[model.Chain(a)[0]]))
            .GroupBy(a => EstateModel.BodyString(a.Ask, "to") ?? "?", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Delivery(g.Key, g.OrderBy(a => a.First.At, StringComparer.Ordinal).ThenBy(a => a.First.Id, StringComparer.Ordinal).Select(a => a.Ask).ToList()))
            .ToList();
    }

    /// <summary>What waits as text: each principal's delivery, every ask with its recommendation first, and how many wait behind it.</summary>
    public static string RenderText(EstateModel model, string? to = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<Delivery> queues = Deliveries(model, to);
        var text = new StringBuilder();
        if (queues.Count == 0)
        {
            text.Append(to is null ? "Nothing waits on anyone: no ask is open or notified.\n" : $"Nothing waits on {Name(model, to)} [principal {to}]: no ask put to it is open or notified.\n");
            return text.ToString();
        }

        foreach (Delivery queue in queues)
        {
            text.Append("What waits on ").Append(Name(model, queue.Principal)).Append(" [principal ").Append(queue.Principal).Append("]: ").Append(Count(queue.Asks.Count, "ask"))
                .Append(queue.Queued.Count == 0 ? string.Empty : $", {queue.Delivered.Count.ToString(CultureInfo.InvariantCulture)} in this delivery and the rest in order behind them").Append('\n');
            int position = 0;
            foreach (RecordEnvelope ask in queue.Delivered)
            {
                position++;
                text.Append("  ").Append(position.ToString(CultureInfo.InvariantCulture)).Append(". Recommendation: ").Append(EstateModel.BodyString(ask, "recommendation")).Append('\n');
                text.Append("     Asked: ").Append(EstateModel.BodyString(ask, "question")).Append(" [ask ").Append(model.Chain(ask)[0]).Append("], by ").Append(Name(model, ask.By)).Append(", ").Append(State(model, ask).State).Append('\n');
                text.Append("     If unanswered: ").Append(EstateModel.BodyString(ask, "consequence")).Append('\n');
                List<string> options = Options(ask);
                if (options.Count > 0)
                {
                    text.Append("     Options: ").Append(string.Join("; ", options)).Append('\n');
                }

                if (About(model, ask) is string about)
                {
                    text.Append("     About: ").Append(about).Append('\n');
                }
            }

            if (queue.Queued.Count > 0)
            {
                text.Append("  Waiting in order: ").Append(Count(queue.Queued.Count, "more ask")).Append(" [").Append(string.Join(", ", queue.Queued.Select(a => "ask " + model.Chain(a)[0]))).Append("]; each follows as those before it are answered or withdrawn\n");
            }
        }

        text.Append("An ask is answered by a decision of the principal it is put to that names it in its answers; reading it, waiting and dismissing it answer nothing.\n");
        return text.ToString();
    }

    /// <summary>What waits in canonical pretty JSON: each principal's delivery and the asks waiting behind it, the same on every machine for the same records.</summary>
    public static string RenderJson(EstateModel model, string? to = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var json = new JsonObject
        {
            ["format"] = Format,
            ["perDelivery"] = PerDelivery,
            ["to"] = to,
            ["principals"] = new JsonArray(Deliveries(model, to).Select(q => (JsonNode)new JsonObject
            {
                ["id"] = q.Principal,
                ["name"] = Name(model, q.Principal),
                ["kind"] = model.CurrentPrincipal(q.Principal) is RecordEnvelope p ? EstateModel.BodyString(p, "kind") : null,
                ["operator"] = model.HasRole(q.Principal, Types.Roles.Operator),
                ["waiting"] = q.Asks.Count,
                ["delivered"] = new JsonArray(q.Delivered.Select((a, i) => (JsonNode)AskJson(model, a, i + 1)).ToArray()),
                ["queued"] = new JsonArray(q.Queued.Select((a, i) => (JsonNode)new JsonObject { ["id"] = model.Chain(a)[0], ["at"] = model.ById[model.Chain(a)[0]].At, ["position"] = PerDelivery + i + 1 }).ToArray()),
            }).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static JsonObject AskJson(EstateModel model, RecordEnvelope ask, int position)
    {
        RecordEnvelope first = model.ById[model.Chain(ask)[0]];
        string? goal = EstateModel.BodyString(ask, "goal");
        RecordEnvelope? goalRecord = goal is not null && model.ById.TryGetValue(goal, out RecordEnvelope? g) ? model.Latest(g) : null;
        return new JsonObject
        {
            ["id"] = first.Id,
            ["revision"] = ask.Id,
            ["hash"] = ask.ComputedHash,
            ["at"] = first.At,
            ["by"] = ask.By,
            ["byName"] = Name(model, ask.By),
            ["state"] = State(model, ask).State,
            ["position"] = position,
            ["recommendation"] = EstateModel.BodyString(ask, "recommendation"),
            ["question"] = EstateModel.BodyString(ask, "question"),
            ["consequence"] = EstateModel.BodyString(ask, "consequence"),
            ["options"] = new JsonArray(Options(ask).Select(o => (JsonNode)o).ToArray()),
            ["goal"] = goalRecord is null ? null : new JsonObject { ["id"] = goalRecord.Id, ["title"] = EstateModel.BodyString(goalRecord, "title") },
            ["subject"] = ask.Subject is null ? null : new JsonObject { ["type"] = ask.Subject.Type, ["id"] = ask.Subject.Id, ["key"] = ask.Subject.Type == "work-order" && model.ById.TryGetValue(ask.Subject.Id, out RecordEnvelope? order) ? EstateModel.BodyString(model.Latest(order), "key") : null },
        };
    }

    /// <summary>What an ask is about, in words: its goal and its subject.</summary>
    private static string? About(EstateModel model, RecordEnvelope ask)
    {
        var parts = new List<string>();
        if (EstateModel.BodyString(ask, "goal") is string goal && model.ById.TryGetValue(goal, out RecordEnvelope? record))
        {
            parts.Add($"goal {EstateModel.BodyString(model.Latest(record), "title")} [goal {goal}]");
        }

        if (ask.Subject is not null && model.ById.TryGetValue(ask.Subject.Id, out RecordEnvelope? subject))
        {
            parts.Add(subject.Type == "work-order" ? $"work order {EstateModel.BodyString(model.Latest(subject), "key")} [work-order {subject.Id}]" : $"{subject.Type} {subject.Id}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static List<string> Options(RecordEnvelope ask) =>
        ask.Body.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Array ? options.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString()!).ToList() : [];

    private static string Name(EstateModel model, string? principal) =>
        model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal ?? "?" : principal ?? "?";

    private static string Count(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
}
