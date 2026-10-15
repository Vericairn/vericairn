// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The rules of a goal that its records and the runtime share (the estate's design/goals.md §§ 3 to 5): the work it is made of and
/// who authored its steps, and its evaluation. The record types
/// validate with them and the runtime refuses with them before it writes, so both give the same reasons. A rule judged for a record
/// already written reads only the records before it, by time and then by identifier, so a later record never unmakes it.
/// </summary>
public static class GoalRules
{
    /// <summary>An evaluation's verdicts on a condition (design/goals.md § 5).</summary>
    public static readonly string[] EvaluationVerdicts = ["met", "not-yet", "impossible"];

    /// <summary>
    /// The work orders a goal is made of: those its plan gives, at their latest revisions, and the current ones that name any revision
    /// of the goal among their inputs, which is how a planner proposes work for it. Ordered by key and identifier.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Work(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        IReadOnlyList<string> chain = model.Chain(goal);
        var orders = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        if (goal.Body.TryGetProperty("plan", out JsonElement plan) && plan.ValueKind == JsonValueKind.Object && plan.TryGetProperty("workOrders", out JsonElement given) && given.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement id in given.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
            {
                if (model.ById.TryGetValue(id.GetString()!, out RecordEnvelope? order) && order.Type == "work-order")
                {
                    RecordEnvelope latest = model.Latest(order);
                    orders[latest.Id] = latest;
                }
            }
        }

        foreach (RecordEnvelope order in model.Current("work-order").Where(o => (before is null || Before(o, before)) && Inputs(o).Any(chain.Contains)))
        {
            orders[order.Id] = order;
        }

        return orders.Values.OrderBy(o => EstateModel.BodyString(o, "key"), StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The authors of a goal's steps: for every assignment of its work, the contributor it delegates to and every principal that
    /// recorded an attempt or a result under it. Reviewing a result is not authoring a step, so a reviewer may still evaluate.
    /// </summary>
    public static IReadOnlySet<string> StepAuthors(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var authors = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope order in Work(model, goal, before))
        {
            IReadOnlyList<string> chain = model.Chain(order);
            foreach (RecordEnvelope assignment in model.OfType("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id) && (before is null || Before(a, before))))
            {
                if (EstateModel.BodyString(assignment, "contributor") is string contributor)
                {
                    authors.Add(contributor);
                }

                foreach (RecordEnvelope attempt in model.About(assignment.Id, "attempt").Where(t => before is null || Before(t, before)))
                {
                    authors.Add(attempt.By);
                    foreach (RecordEnvelope result in model.About(attempt.Id, "result").Where(r => before is null || Before(r, before)))
                    {
                        authors.Add(result.By);
                    }
                }
            }
        }

        return authors;
    }

    /// <summary>The evaluations of the exact goal revision, the reviews pinned to its hash, oldest first; those before a record when one is given.</summary>
    public static IReadOnlyList<RecordEnvelope> Evaluations(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return Ordered(model.About(goal.Id, "review").Where(r => r.Subject?.Hash == goal.ComputedHash && (before is null || Before(r, before))));
    }

    /// <summary>The current evaluation of the exact goal revision: the latest one, before a record when one is given; null when there is none.</summary>
    public static RecordEnvelope? CurrentEvaluation(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        IReadOnlyList<RecordEnvelope> evaluations = Evaluations(model, goal, before);
        return evaluations.Count == 0 ? null : evaluations[^1];
    }

    /// <summary>An evaluation's verdict on each condition, in the goal's order.</summary>
    public static IReadOnlyList<string> Verdicts(RecordEnvelope evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return evaluation.Body.TryGetProperty("conditions", out JsonElement conditions) && conditions.ValueKind == JsonValueKind.Array
            ? conditions.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("verdict", out JsonElement v) ? v.GetString() ?? "?" : "?").ToList()
            : [];
    }

    /// <summary>Record order: by authoring time, then by identifier within one second.</summary>
    public static bool Before(RecordEnvelope a, RecordEnvelope b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int byTime = string.CompareOrdinal(a.At, b.At);
        return byTime < 0 || (byTime == 0 && string.CompareOrdinal(a.Id, b.Id) < 0);
    }

    private static List<RecordEnvelope> Ordered(IEnumerable<RecordEnvelope> records) =>
        records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

    private static IEnumerable<string> Inputs(RecordEnvelope order) =>
        order.Body.TryGetProperty("inputs", out JsonElement inputs) && inputs.ValueKind == JsonValueKind.Array
            ? inputs.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String).Select(i => i.GetProperty("id").GetString()!)
            : [];
}
