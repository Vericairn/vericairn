// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The rules of a goal that its records and the runtime share (the estate's design/goals.md §§ 3 to 5): the work it is made of and
/// who authored its steps, its evaluation, who activates it and within whose authority, and who accepts it. The record types
/// validate with them and the runtime refuses with them before it writes, so both give the same reasons. A rule judged for a record
/// already written reads only the records before it, by time and then by identifier, so a later record never unmakes it.
/// </summary>
public static class GoalRules
{
    /// <summary>An evaluation's verdicts on a condition (design/goals.md § 5).</summary>
    public static readonly ImmutableArray<string> EvaluationVerdicts = ["met", "not-yet", "impossible"];

    /// <summary>The statuses after which a goal never moves again.</summary>
    public static readonly ImmutableArray<string> Ended = ["impossible", "abandoned"];

    /// <summary>
    /// Who accepts a goal, the one place the rule lives (decision 01M507XV4VBFYWMRN8WCXMXHX4): its sponsor when the sponsor is a
    /// person, or the principal that sponsor delegates the acceptance of an ordinary goal to, while it holds the 'accept-work' scope;
    /// and an operator, only an operator, when an agent or a service sponsors it, so that no agent accepts a goal it sponsors. A goal
    /// that includes a reserved action is never accepted by delegation. Who decides for the goal, the sponsor or, for a goal an agent
    /// sponsors, an operator, also activates, resumes and ends it. Never the evaluator or a step's author: <see cref="AcceptanceRefusals"/>.
    /// </summary>
    public static Accepter AccepterOf(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string? sponsor = EstateModel.BodyString(goal, "sponsor");
        if (sponsor is null || !IsPerson(model, sponsor))
        {
            string kind = sponsor is not null && model.CurrentPrincipal(sponsor) is RecordEnvelope principal ? EstateModel.BodyString(principal, "kind") ?? "unknown" : "unknown";
            return new Accepter(null, $"an operator, and only an operator, since its sponsor is {(kind == "service" ? "a service" : kind == "agent" ? "an agent" : "not a person")}");
        }

        string described = $"its sponsor, {Name(model, sponsor)} ({sponsor})";
        string? named = EstateModel.BodyString(goal, "delegate");
        if (named is null || named == sponsor)
        {
            return new Accepter(sponsor, described);
        }

        IReadOnlyList<string> reserved = ReservedActions(model, goal, before);
        return reserved.Count == 0
            ? new Accepter(sponsor, $"{described}, or {Name(model, named)} ({named}), to whom the sponsor delegated it, while that principal holds the 'accept-work' scope", named)
            : new Accepter(sponsor, $"{described}: the goal includes a reserved action ({string.Join("; ", reserved)}), so its acceptance is not delegated");
    }

    /// <summary>
    /// The reserved actions the records show a goal includes, each in words: a completion condition about a release, whose acceptance
    /// and publication are the operator's; a completion condition about a milestone work order, or a milestone among its work, whose
    /// acceptance is the operator's. A goal that includes one is never accepted by delegation (design/goals.md § 5).
    /// </summary>
    public static IReadOnlyList<string> ReservedActions(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var reserved = new List<string>();
        int index = 0;
        foreach (JsonElement condition in goal.Body.TryGetProperty("completion", out JsonElement completion) && completion.ValueKind == JsonValueKind.Array ? completion.EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            index++;
            if (condition.ValueKind != JsonValueKind.Object || !condition.TryGetProperty("record", out JsonElement predicate) || predicate.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? type = predicate.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
            JsonElement fields = predicate.TryGetProperty("fields", out JsonElement f) && f.ValueKind == JsonValueKind.Object ? f : default;
            string? kind = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("kind", out JsonElement k) ? k.GetString() : null;
            string? key = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("key", out JsonElement kk) ? kk.GetString() : null;
            if (type == "release")
            {
                reserved.Add($"condition {index} is about a release, whose acceptance and publication are the operator's");
            }
            else if (type == "work-order" && (kind == "milestone" || (key is not null && model.Current("work-order").Any(o => EstateModel.BodyString(o, "key") == key && EstateModel.BodyString(o, "kind") == "milestone"))))
            {
                reserved.Add($"condition {index} is about a milestone, whose acceptance is the operator's");
            }
        }

        foreach (RecordEnvelope order in Work(model, goal, before).Where(o => EstateModel.BodyString(o, "kind") == "milestone"))
        {
            reserved.Add($"work order {EstateModel.BodyString(order, "key")} is a milestone, whose acceptance is the operator's");
        }

        return reserved;
    }

    /// <summary>True when the principal is a person: a human principal.</summary>
    public static bool IsPerson(EstateModel model, string? principal)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.CurrentPrincipal(principal) is RecordEnvelope record && EstateModel.BodyString(record, "kind") == "human";
    }

    /// <summary>
    /// True when the principal may grant a goal's budget, which is spending, at a time: the operator holds every reserved action, and
    /// any other principal needs a grant of the 'spending' scope that is in force then.
    /// </summary>
    public static bool HoldsSpending(EstateModel model, string principal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.HasRole(principal, Roles.Operator) || model.HasScope(principal, Scopes.Spending, at);
    }

    /// <summary>
    /// Why a principal may not activate or resume a goal at a time, which grants its budget: who decides for the goal does, its
    /// sponsor when a person, who needs the authority to spend, and an operator for a goal an agent sponsors; an agent never does (an
    /// agent's proposed goal is proposed, not authorized or funded). Empty when it may.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> ActivationRefusals(EstateModel model, RecordEnvelope goal, string by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        Accepter decider = AccepterOf(model, goal);
        if (!decider.Decides(model, by))
        {
            return decider.Principal is not null
                ? [("goal-status.sponsor", "Only the goal's sponsor records it as active.")]
                : [("goal-status.operator", "A goal an agent sponsors is activated by an operator, who grants its budget: an agent may propose a goal, never activate or fund one.")];
        }

        return HoldsSpending(model, by, at)
            ? []
            : [("goal-status.authority", "Activating a goal grants its budget, which is spending: its sponsor holds the operator role or a grant of the 'spending' scope in force.")];
    }

    /// <summary>
    /// Who may record a revision of an existing goal, the one place the rule lives (decision 01M50A8KHJT1MSSS84KZKHKRHB, which answers
    /// ask 01M509A2SX02VQJ64C83V1ZGB7): who decides for the goal, as for its activation, its end and its acceptance. That is its sponsor
    /// when the sponsor is a person, and an operator, only an operator, when an agent or a service sponsors it. Anyone else proposes the
    /// change as an ask to the sponsor, and anyone may still propose a new goal. <paramref name="goal"/> is the revision a revision
    /// supersedes; its sponsor decides, whatever the revision names.
    /// </summary>
    public static bool MayRevise(EstateModel model, RecordEnvelope goal, string? by)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return AccepterOf(model, goal).Decides(model, by);
    }

    /// <summary>Why a principal may not record a revision of a goal (<see cref="MayRevise"/>), in words; empty when it may.</summary>
    public static IReadOnlyList<(string Code, string Message)> RevisionRefusals(EstateModel model, RecordEnvelope goal, string? by)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (MayRevise(model, goal, by))
        {
            return [];
        }

        Accepter decider = AccepterOf(model, goal);
        return decider.Principal is not null
            ? [("goal.revision", $"A goal is revised only by its sponsor, {Name(model, decider.Principal)} ({decider.Principal}); anyone else proposes the change as an ask to the sponsor (decision 01M50A8KHJT1MSSS84KZKHKRHB).")]
            : [("goal.revision", "A goal an agent sponsors is revised only by an operator; anyone else proposes the change as an ask (decision 01M50A8KHJT1MSSS84KZKHKRHB).")];
    }

    /// <summary>
    /// True when the principal may end a goal as impossible or abandoned: its sponsor, or who decides for it, an operator for a goal
    /// an agent sponsors.
    /// </summary>
    public static bool MayEnd(EstateModel model, RecordEnvelope goal, string by)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return by == EstateModel.BodyString(goal, "sponsor") || AccepterOf(model, goal).Decides(model, by);
    }

    /// <summary>
    /// The work orders a goal is made of, each at its latest revision: those its plan gives, and those of which a revision names any
    /// revision of the goal among its inputs, which is how a planner proposes work for it. Ordered by key and identifier.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Work(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        IReadOnlyList<string> chain = model.Chain(goal);
        var named = new List<RecordEnvelope>();
        if (goal.Body.TryGetProperty("plan", out JsonElement plan) && plan.ValueKind == JsonValueKind.Object && plan.TryGetProperty("workOrders", out JsonElement given) && given.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement id in given.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
            {
                if (model.ById.TryGetValue(id.GetString()!, out RecordEnvelope? order) && order.Type == "work-order")
                {
                    named.Add(order);
                }
            }
        }

        named.AddRange(model.OfType("work-order").Where(o => (before is null || Before(o, before)) && Inputs(o).Any(chain.Contains)));

        // A work order is one whatever its revisions: the first record of its chain names it, and its latest revision stands for it.
        var orders = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        foreach (RecordEnvelope order in named)
        {
            orders[model.Chain(order)[0]] = model.Latest(order);
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

    /// <summary>
    /// The goal-status records of the exact goal revision that count, oldest first; those before a record when one is given. A status
    /// counts when its author may record it: an activation or a resumption no rule refuses, an end recorded by the sponsor, and any
    /// other status. So an activation an agent wrote around the runtime activates nothing.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Statuses(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return Ordered(model.About(goal.Id, "goal-status").Where(s => (before is null || Before(s, before)) && EstateModel.BodyString(s, "status") switch
        {
            "active" => ActivationRefusals(model, goal, s.By, s.AtTime).Count == 0,
            "impossible" or "abandoned" => MayEnd(model, goal, s.By),
            _ => true,
        }));
    }

    /// <summary>
    /// The evaluations of the exact goal revision that count, oldest first; those before a record when one is given. An evaluation is a
    /// review pinned to the goal's hash; it counts when it gives one known verdict for each completion condition and its author had
    /// authored none of the goal's steps when it was written.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Evaluations(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        int conditions = goal.Body.TryGetProperty("completion", out JsonElement completion) && completion.ValueKind == JsonValueKind.Array ? completion.GetArrayLength() : 0;
        return Ordered(model.About(goal.Id, "review").Where(r => r.Subject?.Hash == goal.ComputedHash && (before is null || Before(r, before))
            && Verdicts(r) is { } verdicts && verdicts.Count == conditions && verdicts.All(v => EvaluationVerdicts.IndexOf(v) >= 0)
            && !StepAuthors(model, goal, r).Contains(r.By)));
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

    /// <summary>
    /// Why an acceptance of the exact goal revision by a principal, relying on the reviews in its basis, is refused; empty when it is
    /// not. The goal is accepted by whom <see cref="AccepterOf"/> names, after its sponsor activated it and before it ended, relying on
    /// its current evaluation, which finds every condition met, and never by that evaluation's author or by a step's author.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> AcceptanceRefusals(EstateModel model, RecordEnvelope goal, string by, IReadOnlyCollection<string> basis, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(basis);
        var refusals = new List<(string Code, string Message)>();
        Accepter accepter = AccepterOf(model, goal, before);
        if (!accepter.Allows(model, by, before?.AtTime ?? DateTimeOffset.UtcNow))
        {
            refusals.Add(("acceptance.policy", $"A goal is accepted by {accepter.Description}."));
        }

        IReadOnlyList<RecordEnvelope> statuses = Statuses(model, goal, before);
        string? last = statuses.Count == 0 ? null : EstateModel.BodyString(statuses[^1], "status");
        if (!statuses.Any(s => EstateModel.BodyString(s, "status") == "active"))
        {
            refusals.Add(("acceptance.activation", "The goal's exact revision was never activated; a goal is accepted after its sponsor activated it."));
        }
        else if (last is not null && Ended.IndexOf(last) >= 0)
        {
            refusals.Add(("acceptance.activation", $"The goal ended as {last}; an ended goal is not accepted."));
        }

        RecordEnvelope? evaluation = CurrentEvaluation(model, goal, before);
        if (evaluation is null)
        {
            refusals.Add(("acceptance.evaluation", "No evaluation of the goal's exact revision is recorded; a goal is accepted on an evaluation, by a principal who authored none of its steps, that finds every condition met."));
        }
        else
        {
            IReadOnlyList<string> verdicts = Verdicts(evaluation);
            List<string> unmet = verdicts.Select((v, i) => (v, i)).Where(x => x.v != "met").Select(x => $"condition {x.i + 1} is {x.v}").ToList();
            if (verdicts.Count == 0 || unmet.Count > 0)
            {
                refusals.Add(("acceptance.evaluation", $"The current evaluation, {evaluation.Id}, does not find every condition met{(unmet.Count == 0 ? string.Empty : ": " + string.Join(", ", unmet))}."));
            }

            if (!basis.Contains(evaluation.Id))
            {
                refusals.Add(("acceptance.evaluation", $"The acceptance relies on the current evaluation of the goal, {evaluation.Id}, and names it in its basis."));
            }

            if (evaluation.By == by)
            {
                refusals.Add(("acceptance.independence", "A goal is never accepted by its evaluator."));
            }
        }

        if (StepAuthors(model, goal, before).Contains(by))
        {
            refusals.Add(("acceptance.independence", "A goal is never accepted by the author of one of its steps."));
        }

        return refusals;
    }

    /// <summary>The ids of the reviews an acceptance names in its basis.</summary>
    public static IReadOnlyList<string> Basis(RecordEnvelope acceptance)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        return acceptance.Body.TryGetProperty("basis", out JsonElement basis) && basis.ValueKind == JsonValueKind.Array
            ? basis.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.String).Select(b => b.GetString()!).ToList()
            : [];
    }

    /// <summary>
    /// The acceptance that achieved the exact goal revision: the first acceptance of it that no rule refuses, before a record when one
    /// is given; null when none did.
    /// </summary>
    public static RecordEnvelope? Achievement(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        return Ordered(model.About(goal.Id, "acceptance").Where(a => a.Subject?.Hash == goal.ComputedHash && (before is null || Before(a, before))))
            .FirstOrDefault(a => AcceptanceRefusals(model, goal, a.By, Basis(a), a).Count == 0);
    }

    /// <summary>
    /// The goal's status: achieved once an acceptance no rule refuses is recorded, otherwise the latest status of its exact revision,
    /// and proposed before any; a revision starts again from proposed. Judged from the records before a record when one is given.
    /// </summary>
    public static string Status(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        if (Achievement(model, goal, before) is not null)
        {
            return "achieved";
        }

        IReadOnlyList<RecordEnvelope> statuses = Statuses(model, goal, before);
        return statuses.Count == 0 ? "proposed" : EstateModel.BodyString(statuses[^1], "status") ?? "proposed";
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

    private static string Name(EstateModel model, string principal) =>
        model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal : principal;

    /// <summary>Who decides for a goal and who accepts it: its sponsor, or any operator when <paramref name="Principal"/> is null, and the delegate.</summary>
    /// <param name="Principal">The sponsor, a person, who decides for the goal and accepts it; null when any operator does.</param>
    /// <param name="Description">Who accepts it, in words.</param>
    /// <param name="Delegate">The principal the sponsor delegated the acceptance of an ordinary goal to, when the delegation stands.</param>
    public sealed record Accepter(string? Principal, string Description, string? Delegate = null)
    {
        /// <summary>True when the principal decides for the goal, activating, resuming and ending it: its sponsor, or an operator when an agent sponsors it.</summary>
        public bool Decides(EstateModel model, string? by)
        {
            ArgumentNullException.ThrowIfNull(model);
            return by is not null && (Principal is null ? model.HasRole(by, Roles.Operator) : by == Principal);
        }

        /// <summary>True when the principal may accept the goal at a time: who decides for it, or the delegate while it holds the 'accept-work' scope.</summary>
        public bool Allows(EstateModel model, string? by, DateTimeOffset at)
        {
            ArgumentNullException.ThrowIfNull(model);
            return Decides(model, by) || (by is not null && by == Delegate && model.HasScope(by, Scopes.AcceptWork, at));
        }
    }
}
