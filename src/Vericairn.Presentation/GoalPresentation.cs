// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;

namespace Vericairn.Presentation;

/// <summary>A goal as a list of goals shows it.</summary>
public sealed record GoalListRow(string Id, string Title, string Status, string Sponsor)
{
    /// <summary>The label of the status.</summary>
    public string StatusLabel => StateLanguage.Label("goal", Status);

    /// <summary>The status and the sponsor, in words.</summary>
    public string Detail => $"{StatusLabel}; sponsored by {Sponsor}";
}

/// <summary>A completion condition with the runtime's verdict now and the current evaluation's.</summary>
public sealed record ConditionRow(int Index, string Description, string Verdict, string Reason, string? Evaluation, string? EvaluationReason)
{
    /// <summary>The condition and its verdict now.</summary>
    public string Heading => $"{Index.ToString(CultureInfo.InvariantCulture)}. {StateLanguage.Label("condition", Verdict)}: {Description}";

    /// <summary>Why, and what the current evaluation found.</summary>
    public string Detail => $"{Reason} Evaluation: {(Evaluation is null ? "none of this revision yet" : $"{StateLanguage.Label("evaluation", Evaluation)}, {EvaluationReason}")}";
}

/// <summary>A step of a goal's plan with its state and, when it is eligible, the principal it reaches from the runtime's machine.</summary>
public sealed record StepRow(string Key, string Title, string Status, string Role, string? Route, string? Principal, string Detail)
{
    /// <summary>The step's route when it is eligible, or the work order's state.</summary>
    public string State => Route is null ? StateLanguage.Label("work-order", Status) : StateLanguage.Label("step-route", Route);

    /// <summary>The step, its role, its state and whom it reaches.</summary>
    public string Heading => $"{Key}  {Title} ({Role}): {State}{(Principal is null ? string.Empty : ", " + Principal)}";
}

/// <summary>One unit of a goal's budget with what its runs were charged.</summary>
public sealed record SpendRow(string Unit, string Detail);

/// <summary>An ask of a goal, the recommendation first.</summary>
public sealed record AskRow(string Id, string State, string To, string Recommendation, string Question, string Consequence)
{
    /// <summary>The recommendation first, then whom it waits on.</summary>
    public string Heading => $"Recommendation: {Recommendation}";

    /// <summary>The question, its state and the consequence of leaving it unanswered.</summary>
    public string Detail => $"{StateLanguage.Label("ask", State)}, to {To}: {Question} If unanswered: {Consequence} [ask {Id}]";
}

/// <summary>An evaluation of a goal's revision.</summary>
public sealed record EvaluationRow(string Id, string By, string At, string Outcome, string Summary, bool Current)
{
    /// <summary>When, by whom and what it found.</summary>
    public string Heading => $"{At}  {StateLanguage.Label("review-outcome", Outcome)} by {By}{(Current ? ", the current evaluation" : string.Empty)}";
}

/// <summary>
/// A goal as the Goals workspace shows it (the estate's design/goals.md § 8): its objective, its conditions with their current verdicts,
/// its plan and steps with the principal each reaches, spend against its budget from the agent-run ledger, its asks and its
/// evaluations. It reads the runtime's goal view (GET /v1/goals/{id}) and its routed next steps (GET /v1/goals/{id}/next), and derives
/// no state of its own.
/// </summary>
public sealed class GoalPresentation
{
    private GoalPresentation()
    {
    }

    /// <summary>The goal's id.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Its title and status.</summary>
    public string Heading { get; private set; } = string.Empty;

    /// <summary>The objective, in the sponsor's words.</summary>
    public string Objective { get; private set; } = string.Empty;

    /// <summary>Who sponsors it and who accepts it, and whether it is ready.</summary>
    public string Acceptance { get; private set; } = string.Empty;

    /// <summary>The conditions with their current verdicts.</summary>
    public IReadOnlyList<ConditionRow> Conditions { get; private set; } = [];

    /// <summary>The steps of its plan.</summary>
    public IReadOnlyList<StepRow> Steps { get; private set; } = [];

    /// <summary>What comes next.</summary>
    public string Next { get; private set; } = string.Empty;

    /// <summary>Spend against the budget.</summary>
    public IReadOnlyList<SpendRow> Spend { get; private set; } = [];

    /// <summary>Its asks, in any state.</summary>
    public IReadOnlyList<AskRow> Asks { get; private set; } = [];

    /// <summary>Its evaluations, oldest first.</summary>
    public IReadOnlyList<EvaluationRow> Evaluations { get; private set; } = [];

    /// <summary>Builds the workspace's view from the goal view and the routed next steps; a missing part is shown as unavailable, never as empty.</summary>
    public static GoalPresentation From(JsonElement goal, JsonElement next)
    {
        var view = new GoalPresentation();
        JsonElement record = Obj(goal, "goal");
        JsonElement body = Obj(record, "body");
        view.Id = Str(record, "id") ?? string.Empty;
        view.Heading = $"{Str(body, "title") ?? "?"}: {StateLanguage.Label("goal", Str(goal, "status"))}";
        view.Objective = Str(body, "objective") ?? "unavailable: the runtime gave no goal";
        JsonElement sponsor = Obj(goal, "sponsor");
        JsonElement accepter = Obj(goal, "accepter");
        List<string> notReady = Items(goal, "notReady").Select(r => r.GetString() ?? "?").ToList();
        view.Acceptance = $"Sponsored by {Str(sponsor, "name") ?? Str(sponsor, "id")}; accepted by {Str(accepter, "description") ?? "?"}. {(Bool(goal, "ready") ? "Ready for acceptance." : notReady.Count == 0 ? string.Empty : "Not ready: " + string.Join("; ", notReady) + ".")}".Trim();
        view.Conditions = Items(goal, "conditions").Select(c =>
        {
            JsonElement evaluation = Obj(c, "evaluation");
            return new ConditionRow(Int(c, "index"), Str(c, "description") ?? "?", Str(c, "verdict") ?? "unknown", Str(c, "reason") ?? string.Empty, Str(evaluation, "verdict"), Str(evaluation, "reason"));
        }).ToList();
        string? current = Str(Obj(goal, "evaluation"), "id");
        view.Evaluations = Items(goal, "evaluations").Select(e => new EvaluationRow(Str(e, "id") ?? "?", Str(e, "byName") ?? Str(e, "by") ?? "?", Str(e, "at") ?? "?", Str(e, "outcome") ?? "?", Str(e, "summary") ?? string.Empty, Str(e, "id") == current)).ToList();
        if (next.ValueKind != JsonValueKind.Object || Str(next, "format") != "vericairn.next/1")
        {
            view.Next = "unavailable: the runtime gave no routed steps";
            view.Steps = Items(goal, "steps").Select(s => new StepRow(Str(s, "key") ?? "?", Str(s, "title") ?? "?", Str(s, "status") ?? "?", "?", null, null, "Routes unavailable.")).ToList();
            return view;
        }

        view.Steps = Items(next, "steps").Select(s =>
        {
            JsonElement route = Obj(s, "route");
            JsonElement assignment = Obj(s, "assignment");
            string? principal = route.ValueKind == JsonValueKind.Object ? Str(route, "principalName") : Str(assignment, "contributorName");
            string detail = route.ValueKind == JsonValueKind.Object
                ? $"{Str(route, "reason")}. Budget: {(Bool(Obj(route, "budget"), "allows") ? "allows it" : "does not allow it now: " + string.Join(" ", Items(Obj(route, "budget"), "reasons").Select(r => r.GetString())))}.{Custody(Obj(route, "custody"))}"
                : Items(s, "holds").Count > 0
                    ? "Held: " + string.Join("; ", Items(s, "holds").Select(h => $"{Str(h, "relation")} {Str(h, "key") ?? Str(h, "type") + " " + Str(h, "id")}: {Str(h, "state")}")) + "."
                    : assignment.ValueKind == JsonValueKind.Object ? $"Assigned to {Str(assignment, "contributorName")} by assignment {Str(assignment, "id")}." : StateLanguage.Label("work-order", Str(s, "status"));
            return new StepRow(Str(s, "key") ?? "?", Str(s, "title") ?? "?", Str(s, "status") ?? "?", Str(s, "role") ?? "builder", Str(route, "state"), principal, detail);
        }).ToList();
        string? nextKey = Str(next, "next");
        view.Next = nextKey is null ? "Next: nothing to dispatch; every eligible step is held, or none is eligible." : $"Next: {nextKey}, taken by vericairn goal step.";
        JsonElement budget = Obj(next, "budget");
        var spend = Items(budget, "units").Select(u => new SpendRow(Str(u, "unit") ?? "?", $"{Long(u, "spent")} of {Long(u, "budget")}{(Str(u, "currency") is string unit ? " minor units of " + unit : string.Empty)} charged, {Long(u, "remaining")} remain")).ToList();
        spend.Add(new SpendRow("runs", $"{Long(budget, "inFlight")} of {Long(budget, "concurrency")} in flight; {Long(budget, "attemptsPerStep")} attempts per step"));
        spend.AddRange(Items(budget, "fenced").Select(f => new SpendRow("fenced", f.GetString() ?? "?")));
        view.Spend = spend;
        view.Asks = Items(next, "asks").Select(a => new AskRow(Str(a, "id") ?? "?", Str(a, "state") ?? "?", Str(a, "toName") ?? Str(a, "to") ?? "?", Str(a, "recommendation") ?? string.Empty, Str(a, "question") ?? string.Empty, Str(a, "consequence") ?? string.Empty)).ToList();
        return view;
    }

    private static string Custody(JsonElement custody)
    {
        if (custody.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        JsonElement exception = Obj(custody, "exception");
        List<string> refusals = Items(custody, "refusals").Select(r => r.GetString() ?? "?").ToList();
        return refusals.Count == 0 ? " Custody here: nothing stands in the way of an unattended run."
            : exception.ValueKind == JsonValueKind.Object ? $" Custody here: allowed under the sponsor's custody exception {Str(exception, "id")} until {Str(exception, "expires")} ({Str(exception, "reason")}), which lifts: {string.Join(" ", refusals)}"
            : " Custody here: an unattended run is refused: " + string.Join(" ", refusals);
    }

    private static List<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];

    private static JsonElement Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static string Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64().ToString(CultureInfo.InvariantCulture) : "?";

    private static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>
/// The attention items of asks (the estate's design/goals.md § 8, design/cohesion-contract.md § 5): each ask a delivery carries is an
/// item of the kind decision needed, worded in the state language, with its recommendation first and the consequence of leaving it
/// unanswered; the asks waiting behind a delivery are one item of information per principal. Acknowledging an item answers nothing.
/// </summary>
public static class AskAttention
{
    /// <summary>The items of what waits (GET /v1/asks), most urgent first; none when the runtime gave nothing.</summary>
    public static IReadOnlyList<AttentionItem> From(JsonElement asks, string asOf)
    {
        var items = new List<AttentionItem>();
        if (asks.ValueKind != JsonValueKind.Object || !asks.TryGetProperty("principals", out JsonElement principals) || principals.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement principal in principals.EnumerateArray())
        {
            string name = Str(principal, "name") ?? Str(principal, "id") ?? "?";
            foreach (JsonElement ask in principal.TryGetProperty("delivered", out JsonElement delivered) && delivered.ValueKind == JsonValueKind.Array ? delivered.EnumerateArray() : Enumerable.Empty<JsonElement>())
            {
                string id = Str(ask, "id") ?? "?";
                items.Add(new AttentionItem(
                    "decision needed",
                    Str(ask, "question") ?? "?",
                    $"Recommendation: {Str(ask, "recommendation")} If unanswered: {Str(ask, "consequence")}",
                    StateLanguage.Label("ask", Str(ask, "state")),
                    asOf,
                    $"Answer it with a decision of {name} that names ask {id} in its answers; waiting and acknowledging answer nothing.",
                    "ask",
                    id));
            }

            int queued = principal.TryGetProperty("queued", out JsonElement behind) && behind.ValueKind == JsonValueKind.Array ? behind.GetArrayLength() : 0;
            if (queued > 0)
            {
                items.Add(new AttentionItem("information", $"{queued.ToString(CultureInfo.InvariantCulture)} more {(queued == 1 ? "ask waits" : "asks wait")} for {name}", "At most three asks go out in one delivery; the rest wait in order and follow as those before them are answered.", StateLanguage.Label("ask", "open"), asOf, "Answer the asks delivered first.", null, null));
            }
        }

        return items;
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
