// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The next-eligible view (the estate's design/goals.md § 7, decision 18). Its records-only part is the planned work orders whose
/// 'after' work orders are all accepted and whose 'blockedBy' holds nothing open, the same on every machine for the same records. Its
/// routed part (NextEligible.Routes.cs) takes each such step of a goal to the principal the goal's roster names, from this machine, with
/// whether the goal's budget allows it. Both are read-only queries: the view never dispatches anything.
/// </summary>
public static partial class NextEligible
{
    /// <summary>The operation states that hold nothing: the effect was observed, or the intent was canceled before any dispatch.</summary>
    private static readonly string[] SettledOperations = ["observed-success", "canceled"];

    /// <summary>The work-order states that hold nothing as a blocker: accepted, or retired and never to be done.</summary>
    private static readonly string[] ClosedWorkOrders = ["accepted", "retired"];

    /// <summary>
    /// One relation of a planned work order to a record that can hold it: a work order it starts after (<c>after</c>), which holds it until
    /// it is accepted; or a blocker (<c>blockedBy</c>): an ask, which holds it while it is open or notified; a work order, until it is
    /// accepted or retired; an operation, until its effect is observed or it is canceled; a decision, while an obligation it leaves has
    /// no discharge. A record the estate does not hold holds it, since nothing shows it free.
    /// </summary>
    /// <param name="Kind">after or blockedBy.</param>
    /// <param name="Type">The type named: work-order, ask, operation or decision.</param>
    /// <param name="Id">The id named.</param>
    /// <param name="Holder">The latest revision of the record named; null when the estate holds no such record.</param>
    /// <param name="Key">A work order's key.</param>
    /// <param name="State">The state that decides it: a work order's or an ask's derived state, an operation's status, a decision's obligations open, obligations discharged or no obligations, or absent.</param>
    /// <param name="Holds">Whether it holds the work order now.</param>
    /// <param name="Basis">The records the state rests on beyond the holder: a work order's status or acceptance, the decisions answering an ask, the discharges of a decision's obligations.</param>
    /// <param name="Open">A decision's obligations without a discharge, by key.</param>
    public sealed record Relation(string Kind, string Type, string Id, RecordEnvelope? Holder, string? Key, string State, bool Holds, IReadOnlyList<RecordEnvelope> Basis, IReadOnlyList<string> Open);

    /// <summary>A planned work order with its relations; it is eligible when none of them holds it.</summary>
    /// <param name="Order">The work order's current revision.</param>
    /// <param name="Relations">Its after and blockedBy relations, in the order the record names them.</param>
    public sealed record Item(RecordEnvelope Order, IReadOnlyList<Relation> Relations)
    {
        /// <summary>True when nothing holds it.</summary>
        public bool Eligible => !Relations.Any(r => r.Holds);
    }

    /// <summary>Every planned work order, by key, with what relates it to the records that can hold it; the eligible ones have nothing holding them.</summary>
    public static IReadOnlyList<Item> Derive(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Dictionary<string, string> status = EstateStatus.WorkItems(model).ToDictionary(i => i.Id, i => i.Status, StringComparer.Ordinal);
        return model.Current("work-order")
            .Where(o => status.GetValueOrDefault(o.Id) == "planned")
            .OrderBy(o => EstateModel.BodyString(o, "key"), StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new Item(o, Relations(model, o, status)))
            .ToList();
    }

    /// <summary>The planned work orders nothing holds, by key.</summary>
    public static IReadOnlyList<Item> Eligible(EstateModel model) => Derive(model).Where(i => i.Eligible).ToList();

    private static List<Relation> Relations(EstateModel model, RecordEnvelope order, IReadOnlyDictionary<string, string> status)
    {
        var relations = new List<Relation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (order.Body.TryGetProperty("after", out JsonElement after) && after.ValueKind == JsonValueKind.Array)
        {
            foreach (string id in after.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!))
            {
                Relation relation = WorkOrder(model, "after", id, status, ["accepted"]);
                if (seen.Add("after " + (relation.Holder?.Id ?? id)))
                {
                    relations.Add(relation);
                }
            }
        }

        if (order.Body.TryGetProperty("blockedBy", out JsonElement blockers) && blockers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement blocker in blockers.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object))
            {
                string type = blocker.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "?";
                string id = blocker.TryGetProperty("id", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "?";
                Relation relation = type switch
                {
                    "work-order" => WorkOrder(model, "blockedBy", id, status, ClosedWorkOrders),
                    "ask" => Ask(model, id),
                    "operation" => Operation(model, id),
                    "decision" => Decision(model, id),
                    _ => new Relation("blockedBy", type, id, Find(model, id, type), null, "not a blocker", true, [], []),
                };
                if (seen.Add("blockedBy " + type + " " + (relation.Holder?.Id ?? id)))
                {
                    relations.Add(relation);
                }
            }
        }

        return relations;
    }

    private static Relation WorkOrder(EstateModel model, string kind, string id, IReadOnlyDictionary<string, string> status, string[] free)
    {
        if (Find(model, id, "work-order") is not RecordEnvelope holder)
        {
            return new Relation(kind, "work-order", id, null, null, "absent", true, [], []);
        }

        string state = status.GetValueOrDefault(holder.Id) ?? "planned";
        RecordEnvelope? basis = EstateStatus.StatusRecord(model, holder);
        return new Relation(kind, "work-order", id, holder, EstateModel.BodyString(holder, "key"), state, Array.IndexOf(free, state) < 0, basis is null ? [] : [basis], []);
    }

    private static Relation Ask(EstateModel model, string id)
    {
        if (Find(model, id, "ask") is not RecordEnvelope holder)
        {
            return new Relation("blockedBy", "ask", id, null, null, "absent", true, [], []);
        }

        (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, holder);
        return new Relation("blockedBy", "ask", id, holder, null, state, Asks.IsWaiting(state), answers, []);
    }

    private static Relation Operation(EstateModel model, string id)
    {
        if (Find(model, id, "operation") is not RecordEnvelope holder)
        {
            return new Relation("blockedBy", "operation", id, null, null, "absent", true, [], []);
        }

        string state = EstateModel.BodyString(holder, "status") ?? "?";
        return new Relation("blockedBy", "operation", id, holder, null, state, Array.IndexOf(SettledOperations, state) < 0, [], []);
    }

    private static Relation Decision(EstateModel model, string id)
    {
        if (Find(model, id, "decision") is not RecordEnvelope holder)
        {
            return new Relation("blockedBy", "decision", id, null, null, "absent", true, [], []);
        }

        List<string> keys = holder.Body.TryGetProperty("obligations", out JsonElement obligations) && obligations.ValueKind == JsonValueKind.Array
            ? obligations.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object && o.TryGetProperty("key", out JsonElement k) && k.ValueKind == JsonValueKind.String).Select(o => o.GetProperty("key").GetString()!).ToList()
            : [];
        string root = model.Chain(holder)[0];
        List<RecordEnvelope> discharges = model.Current("discharge")
            .Where(d => d.Subject is not null && model.ById.TryGetValue(d.Subject.Id, out RecordEnvelope? subject) && model.Chain(subject)[0] == root && keys.Contains(EstateModel.BodyString(d, "obligation") ?? string.Empty))
            .OrderBy(d => d.At, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        List<string> open = keys.Where(k => !discharges.Any(d => EstateModel.BodyString(d, "obligation") == k)).ToList();
        string state = keys.Count == 0 ? "no obligations" : open.Count > 0 ? "obligations open" : "obligations discharged";
        return new Relation("blockedBy", "decision", id, holder, null, state, open.Count > 0, discharges, open);
    }

    /// <summary>The latest revision of the record an id names, when the estate holds one of that type.</summary>
    private static RecordEnvelope? Find(EstateModel model, string id, string type) =>
        model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == type ? model.Latest(record) : null;
}
