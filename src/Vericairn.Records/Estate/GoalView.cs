// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// What <c>vericairn goal</c> shows of a goal, derived from the records alone (the estate's design/goals.md §§ 5, 8 and 12): its
/// objective, sponsor and status, the budget its activation grants, each condition's verdict with the records it read beside the
/// current evaluation's verdict, its steps, and what still stands between it and its acceptance. Every client shows the same.
/// </summary>
public static class GoalView
{
    /// <summary>One goal in a list.</summary>
    /// <param name="Id">The current revision's id.</param>
    /// <param name="Hash">The current revision's hash.</param>
    /// <param name="Title">The title.</param>
    /// <param name="Status">proposed, a goal-status, or achieved.</param>
    /// <param name="Sponsor">The sponsor's principal id.</param>
    /// <param name="SponsorName">The sponsor's name.</param>
    public sealed record Row(string Id, string Hash, string Title, string Status, string Sponsor, string? SponsorName);

    /// <summary>The latest revision of the goal an id names, by any revision of it; null when it names no goal.</summary>
    public static RecordEnvelope? Find(EstateModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(id);
        return model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == "goal" ? model.Latest(record) : null;
    }

    /// <summary>The estate's current goals, oldest first.</summary>
    public static IReadOnlyList<Row> Rows(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("goal").OrderBy(g => g.At, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal).Select(g =>
        {
            string sponsor = EstateModel.BodyString(g, "sponsor") ?? "?";
            return new Row(g.Id, g.ComputedHash, EstateModel.BodyString(g, "title") ?? "?", GoalRules.Status(model, g), sponsor, PrincipalName(model, sponsor));
        }).ToList();
    }

    /// <summary>
    /// Why the goal is not ready for its acceptance now, whoever accepts it: it was not activated or it ended, its exact revision has
    /// no current evaluation or one that does not find every condition met, or a record predicate does not hold now. Empty when it
    /// is ready, and for a goal already achieved.
    /// </summary>
    public static IReadOnlyList<string> NotReady(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string status = GoalRules.Status(model, goal);
        var reasons = new List<string>();
        if (status == "achieved")
        {
            return reasons;
        }

        if (status == "proposed")
        {
            reasons.Add("its sponsor has not activated it");
        }
        else if (Array.IndexOf(GoalRules.Ended, status) >= 0)
        {
            reasons.Add($"it ended as {status}");
        }

        RecordEnvelope? evaluation = GoalRules.CurrentEvaluation(model, goal);
        if (evaluation is null)
        {
            reasons.Add("no evaluation of its exact revision is recorded (a principal who authored none of its steps evaluates it)");
        }
        else
        {
            List<string> unmet = GoalRules.Verdicts(evaluation).Select((v, i) => (v, i)).Where(x => x.v != "met").Select(x => $"condition {x.i + 1} {x.v}").ToList();
            if (unmet.Count > 0)
            {
                reasons.Add($"its current evaluation, {evaluation.Id}, finds {string.Join(", ", unmet)}");
            }
        }

        foreach ((JsonElement condition, int index) in Completion(goal).Select((c, i) => (c, i)).Where(x => x.c.TryGetProperty("record", out _)))
        {
            GoalPredicates.Verdict verdict = GoalPredicates.Evaluate(model, condition);
            if (verdict.Value != "met")
            {
                reasons.Add($"condition {index + 1} is {verdict.Value} now");
            }
        }

        return reasons;
    }

    /// <summary>The goal as text, condition by condition.</summary>
    public static string RenderText(EstateModel model, RecordEnvelope goal)
    {
        Snapshot view = Take(model, goal);
        var text = new StringBuilder();
        text.Append("Goal: ").Append(EstateModel.BodyString(goal, "title")).Append('\n');
        text.Append("  goal ").Append(goal.Id).Append(" (revision ").Append(view.Revision.ToString(CultureInfo.InvariantCulture)).Append(") at ").Append(goal.ComputedHash).Append('\n');
        text.Append("  Objective: ").Append(EstateModel.BodyString(goal, "objective")).Append('\n');
        string sponsor = EstateModel.BodyString(goal, "sponsor") ?? "?";
        text.Append("  Sponsor: ").Append(PrincipalName(model, sponsor) ?? sponsor).Append(" (").Append(sponsor).Append(GoalRules.IsPerson(model, sponsor) ? ", a person" : ", not a person").Append("); accepted by ").Append(view.Accepter.Description).Append('\n');
        text.Append("  Status: ").Append(view.Status);
        if (view.Since is not null)
        {
            text.Append(" since ").Append(view.Since.At);
        }

        if (view.Activated is not null && view.Status != "proposed")
        {
            text.Append(" (activated ").Append(view.Activated.At).Append(')');
        }

        text.Append('\n');
        List<string> granted = view.Budget.Where(b => b.Cap is not null).Select(b => $"{b.Unit} {b.Cap}").ToList();
        List<string> absent = view.Budget.Where(b => b.Cap is null).Select(b => b.Unit).ToList();
        text.Append("  Budget its activation grants: ").Append(string.Join(", ", granted)).Append(absent.Count == 0 ? string.Empty : "; not granted: " + string.Join(", ", absent)).Append('\n');
        List<string> constraints = Strings(goal, "constraints");
        text.Append("  Constraints: ").Append(constraints.Count == 0 ? "none" : string.Join("; ", constraints)).Append('\n');
        text.Append("Conditions:\n");
        foreach (ConditionView condition in view.Conditions)
        {
            text.Append("  ").Append(condition.Index.ToString(CultureInfo.InvariantCulture)).Append(". ");
            if (condition.Kind == "record")
            {
                text.Append(condition.Verdict.Value).Append(": ").Append(condition.Verdict.Reason).Append('\n');
            }
            else
            {
                text.Append("evidence, judged by an evaluator: ").Append(condition.Description).Append('\n');
            }

            text.Append("     evaluation: ");
            if (condition.Evaluation is null)
            {
                text.Append("none yet\n");
            }
            else
            {
                text.Append(condition.Evaluation.Verdict).Append(": ").Append(condition.Evaluation.Reason);
                if (condition.Evaluation.Read.Count > 0)
                {
                    text.Append(" (read ").Append(string.Join(", ", condition.Evaluation.Read.Select(r => $"{r.Type} {r.Id}"))).Append(')');
                }

                text.Append('\n');
            }
        }

        if (view.Evaluation is null)
        {
            text.Append("Evaluation: none of this revision yet\n");
        }
        else
        {
            text.Append("Evaluation: ").Append(view.Evaluation.Id).Append(" by ").Append(PrincipalName(model, view.Evaluation.By) ?? view.Evaluation.By).Append(" (").Append(view.Evaluation.By).Append(") at ").Append(view.Evaluation.At)
                .Append(", ").Append(EstateModel.BodyString(view.Evaluation, "outcome")).Append(": ").Append(EstateModel.BodyString(view.Evaluation, "summary")).Append('\n');
        }

        text.Append("Steps:");
        text.Append(view.Steps.Count == 0 ? " none yet; the work orders its plan gives, and those that name it among their inputs, are its steps\n" : "\n");
        foreach (StepView step in view.Steps)
        {
            text.Append("  ").Append(step.Key).Append("  ").Append(step.Status).Append("  ").Append(step.Title).Append('\n');
            foreach (WorkView.AssignmentItem assignment in step.Assignments)
            {
                text.Append("    assignment ").Append(assignment.AssignmentId).Append(" to ").Append(assignment.ContributorName).Append(": ").Append(assignment.Outcome).Append(", ").Append(assignment.State).Append('\n');
            }
        }

        if (view.Acceptance is not null)
        {
            text.Append("Achieved: acceptance ").Append(view.Acceptance.Id).Append(" by ").Append(PrincipalName(model, view.Acceptance.By) ?? view.Acceptance.By).Append(" at ").Append(view.Acceptance.At).Append('\n');
        }
        else if (view.NotReady.Count == 0)
        {
            text.Append("Ready for acceptance by ").Append(view.Accepter.Description).Append(": vericairn goal accept ").Append(goal.Id).Append(" --by <id> --statement <text>\n");
        }
        else
        {
            text.Append("Not ready for acceptance: ").Append(string.Join("; ", view.NotReady)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The goal in canonical pretty JSON, the same bytes on every machine for the same records.</summary>
    public static string RenderJson(EstateModel model, RecordEnvelope goal)
    {
        Snapshot view = Take(model, goal);
        string sponsor = EstateModel.BodyString(goal, "sponsor") ?? "?";
        var json = new JsonObject
        {
            ["goal"] = new JsonObject
            {
                ["id"] = goal.Id,
                ["hash"] = goal.ComputedHash,
                ["revision"] = view.Revision,
                ["by"] = goal.By,
                ["at"] = goal.At,
                ["body"] = JsonNode.Parse(goal.Body.GetRawText()),
            },
            ["sponsor"] = new JsonObject { ["id"] = sponsor, ["name"] = PrincipalName(model, sponsor), ["person"] = GoalRules.IsPerson(model, sponsor) },
            ["status"] = view.Status,
            ["since"] = view.Since?.At,
            ["activated"] = view.Activated?.At,
            ["budget"] = new JsonArray(view.Budget.Select(b => (JsonNode)new JsonObject { ["unit"] = b.Unit, ["granted"] = b.Cap }).ToArray()),
            ["conditions"] = new JsonArray(view.Conditions.Select(c => (JsonNode)new JsonObject
            {
                ["index"] = c.Index,
                ["kind"] = c.Kind,
                ["description"] = c.Description,
                ["verdict"] = c.Verdict.Value,
                ["reason"] = c.Verdict.Reason,
                ["read"] = References(c.Verdict.Read),
                ["evaluation"] = c.Evaluation is null ? null : new JsonObject { ["verdict"] = c.Evaluation.Verdict, ["reason"] = c.Evaluation.Reason, ["read"] = References(c.Evaluation.Read) },
            }).ToArray()),
            ["evaluation"] = view.Evaluation is null ? null : new JsonObject
            {
                ["id"] = view.Evaluation.Id,
                ["hash"] = view.Evaluation.ComputedHash,
                ["by"] = view.Evaluation.By,
                ["at"] = view.Evaluation.At,
                ["outcome"] = EstateModel.BodyString(view.Evaluation, "outcome"),
                ["summary"] = EstateModel.BodyString(view.Evaluation, "summary"),
            },
            ["steps"] = new JsonArray(view.Steps.Select(s => (JsonNode)new JsonObject
            {
                ["key"] = s.Key,
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["status"] = s.Status,
                ["assignments"] = new JsonArray(s.Assignments.Select(a => (JsonNode)new JsonObject { ["id"] = a.AssignmentId, ["contributor"] = a.ContributorId, ["outcome"] = a.Outcome, ["state"] = a.State }).ToArray()),
            }).ToArray()),
            ["accepter"] = new JsonObject { ["principal"] = view.Accepter.Principal, ["delegate"] = view.Accepter.Delegate, ["description"] = view.Accepter.Description },
            ["ready"] = view.Acceptance is null && view.NotReady.Count == 0,
            ["notReady"] = new JsonArray(view.NotReady.Select(r => (JsonNode)r).ToArray()),
            ["acceptance"] = view.Acceptance is null ? null : new JsonObject { ["id"] = view.Acceptance.Id, ["by"] = view.Acceptance.By, ["at"] = view.Acceptance.At, ["statement"] = EstateModel.BodyString(view.Acceptance, "statement") },
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static Snapshot Take(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        string status = GoalRules.Status(model, goal);
        RecordEnvelope? acceptance = GoalRules.Achievement(model, goal);
        IReadOnlyList<RecordEnvelope> statuses = GoalRules.Statuses(model, goal);
        RecordEnvelope? since = acceptance ?? (statuses.Count == 0 ? null : statuses[^1]);
        RecordEnvelope? activated = statuses.FirstOrDefault(s => EstateModel.BodyString(s, "status") == "active");
        RecordEnvelope? evaluation = GoalRules.CurrentEvaluation(model, goal);
        List<JsonElement> evaluated = evaluation is not null && evaluation.Body.TryGetProperty("conditions", out JsonElement e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToList() : [];
        var conditions = new List<ConditionView>();
        foreach ((JsonElement condition, int index) in Completion(goal).Select((c, i) => (c, i)))
        {
            EvaluatedView? judged = null;
            if (index < evaluated.Count)
            {
                JsonElement entry = evaluated[index];
                judged = new EvaluatedView(Text(entry, "verdict") ?? "?", Text(entry, "reason") ?? string.Empty, Read(entry));
            }

            conditions.Add(new ConditionView(index + 1, condition.TryGetProperty("record", out _) ? "record" : "evidence", GoalPredicates.Describe(condition), GoalPredicates.Evaluate(model, condition), judged));
        }

        Dictionary<string, string> orderStatus = EstateStatus.WorkItems(model).ToDictionary(w => w.Id, w => w.Status, StringComparer.Ordinal);
        IReadOnlyList<WorkView.AssignmentItem> assignments = WorkView.AssignmentItems(model);
        var steps = GoalRules.Work(model, goal).Select(order =>
        {
            IReadOnlyList<string> chain = model.Chain(order);
            List<WorkView.AssignmentItem> mine = assignments.Where(a => model.ById.TryGetValue(a.AssignmentId, out RecordEnvelope? record) && record.Subject is not null && chain.Contains(record.Subject.Id)).ToList();
            return new StepView(EstateModel.BodyString(order, "key") ?? "?", order.Id, EstateModel.BodyString(order, "title") ?? "?", orderStatus.TryGetValue(order.Id, out string? s) ? s : "planned", mine);
        }).ToList();
        JsonElement budget = goal.Body.TryGetProperty("budget", out JsonElement b) ? b : default;
        return new Snapshot(model.Chain(goal).Count, status, since, activated, Caps.Granted(budget), conditions, evaluation, steps, GoalRules.AccepterOf(model, goal), NotReady(model, goal), acceptance);
    }

    private static IEnumerable<JsonElement> Completion(RecordEnvelope goal) =>
        goal.Body.TryGetProperty("completion", out JsonElement completion) && completion.ValueKind == JsonValueKind.Array ? completion.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object) : [];

    private static List<RecordReference> Read(JsonElement entry) =>
        entry.TryGetProperty("read", out JsonElement read) && read.ValueKind == JsonValueKind.Array
            ? read.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => new RecordReference(Text(r, "type") ?? "?", Text(r, "id") ?? "?", Text(r, "hash"))).ToList()
            : [];

    private static JsonArray References(IEnumerable<RecordReference> references) =>
        new(references.Select(r => (JsonNode)new JsonObject { ["type"] = r.Type, ["id"] = r.Id, ["hash"] = r.Hash }).ToArray());

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(RecordEnvelope record, string name) =>
        record.Body.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList() : [];

    private static string? PrincipalName(EstateModel model, string id) =>
        model.CurrentPrincipal(id) is RecordEnvelope principal ? EstateModel.BodyString(principal, "name") : null;

    private sealed record EvaluatedView(string Verdict, string Reason, IReadOnlyList<RecordReference> Read);

    private sealed record ConditionView(int Index, string Kind, string Description, GoalPredicates.Verdict Verdict, EvaluatedView? Evaluation);

    private sealed record StepView(string Key, string Id, string Title, string Status, IReadOnlyList<WorkView.AssignmentItem> Assignments);

    private sealed record Snapshot(int Revision, string Status, RecordEnvelope? Since, RecordEnvelope? Activated, IReadOnlyList<(string Unit, string? Cap)> Budget, IReadOnlyList<ConditionView> Conditions, RecordEnvelope? Evaluation, IReadOnlyList<StepView> Steps, GoalRules.Accepter Accepter, IReadOnlyList<string> NotReady, RecordEnvelope? Acceptance);
}
