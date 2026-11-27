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

/// <summary>
/// A work order proposed for a goal whose plan says sponsor-approves, waiting for the sponsor's approval before it joins the goal, as the
/// goal view gives it (FIX-117): who proposed it and the command that approves it.
/// </summary>
public sealed record ProposedRow(string Key, string Title, string Id, string By, string? Approve)
{
    /// <summary>The work order and who proposed it.</summary>
    public string Heading => $"{Key}  {Title}: waiting for the sponsor's approval";

    /// <summary>Who proposed it, and how the sponsor approves it.</summary>
    public string Detail => $"Proposed by {By}; work order {Id}. It joins the goal only once its sponsor approves it{(Approve is null ? "." : ": " + Approve)}";
}

/// <summary>
/// A stop of a run as every surface shows it (the estate's design/flow.md § 8): the step, why it waits, on whom, how it reaches them, and
/// for a reserved act that is next its exact command. Read from the runtime's stops, the same in status, the goal view and the routed
/// steps; nothing here derives a state of its own.
/// </summary>
public sealed record StopRow(string Kind, string Name, string Title, string? Principal, string Reason, string Delivery, string? Record, string? Command)
{
    /// <summary>Why it waits, in the state language.</summary>
    public string KindLabel => StateLanguage.Label("flow-stop", Kind);

    /// <summary>How it reaches its person, in the state language.</summary>
    public string DeliveryLabel => StateLanguage.Label("flow-delivery", Delivery);

    /// <summary>The step, why it waits and on whom.</summary>
    public string Heading => $"{Name}: {KindLabel}; waits on {Principal ?? "a person the records do not name"}";

    /// <summary>How it reaches its person, why, and a reserved act's exact command.</summary>
    public string Detail => $"{DeliveryLabel}{(Record is null ? string.Empty : $" [{Record}]")}. {Reason}{(Command is null ? string.Empty : $" Command: {Command}")}";

    /// <summary>The rows of a run's stops, as the runtime renders them; none when it gave none.</summary>
    public static IReadOnlyList<StopRow> From(JsonElement stops) =>
        stops.ValueKind != JsonValueKind.Array ? [] : stops.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object).Select(s =>
        {
            JsonElement record = s.TryGetProperty("record", out JsonElement r) ? r : default;
            string? principal = Str(s, "principalName") ?? Str(s, "principal");
            return new StopRow(Str(s, "kind") ?? "?", Str(s, "name") ?? "?", Str(s, "title") ?? "?", principal, Str(s, "reason") ?? string.Empty, Str(s, "delivery") ?? "?",
                record.ValueKind == JsonValueKind.Object ? $"{Str(record, "type")} {Str(record, "id")}" : null, Str(s, "command"));
        }).ToList();

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// A step's due time as every surface shows it (the estate's design/flow.md § 7; from 0.9.4, PROC-18): when it is due, why, its timing and
/// zone, its deadline and a missed-time rule it took, read from the runtime's position or status and never derived here.
/// </summary>
public sealed record DueRow(string Name, string? At, string Reason, string Timing, string Zone, string? Deadline, string? Missed)
{
    /// <summary>The step and when it is due.</summary>
    public string Heading => $"{Name}: due {At ?? "once the records give its time"}";

    /// <summary>Why, its timing and zone, its deadline and a missed-time rule it took.</summary>
    public string Detail => $"{Reason}. Timing: {Timing}, in {Zone}.{(Deadline is null ? string.Empty : $" Deadline {Deadline}.")}{(Missed is null ? string.Empty : $" Missed its deadline; its rule: {Missed}.")}";

    /// <summary>The rows of due times as status gives them, each named, or a position's steps that carry one; none when the runtime gave none.</summary>
    public static IReadOnlyList<DueRow> From(JsonElement items) =>
        items.ValueKind != JsonValueKind.Array ? [] : items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i =>
        {
            JsonElement due = i.TryGetProperty("due", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object ? nested : i;
            string name = Str(i, "name") ?? (Str(i, "instance") is string instance ? $"{Str(i, "key")}[{instance}]" : Str(i, "key")) ?? "?";
            return due.ValueKind == JsonValueKind.Object && due.TryGetProperty("reason", out _)
                ? new DueRow(name, Str(due, "at"), Str(due, "reason") ?? string.Empty, Str(due, "timing") ?? "instant", Str(due, "zone") ?? "UTC", Str(due, "deadline"), Str(due, "missed"))
                : null;
        }).OfType<DueRow>().ToList();

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// A step's decision as every surface shows it (the estate's design/flow.md § 5; from 0.9.4, PROC-19): its state, who decided what and
/// why, the evidence by hash, or that it waits on its decider, read from the runtime's position or status and never derived here.
/// </summary>
public sealed record DecisionRow(string Name, string State, string Decider, IReadOnlyList<string> Paths, string? DecidedBy, string? By, string? Record, string? Reasons, string? At, IReadOnlyList<string> Evidence)
{
    /// <summary>The step and its decision in brief: the paths taken and who took them, or its state.</summary>
    public string Heading => Paths.Count == 0
        ? $"{Name}: decision {State}, by {Decider}"
        : $"{Name}: path{(Paths.Count == 1 ? string.Empty : "s")} {string.Join(", ", Paths)}, {Who}{(At is null ? string.Empty : $" at {At}")} ({State})";

    /// <summary>Why, the record that carries it and the evidence it rests on.</summary>
    public string Detail => (Reasons ?? (State == "pending" ? "It waits on its decider." : State == "held" ? "Held: its decider decides again." : string.Empty))
        + (Record is null ? string.Empty : $" Recorded as result {Record}.")
        + (Evidence.Count == 0 ? string.Empty : $" {(Record is null ? "Records read" : "Evidence")}: {string.Join(", ", Evidence)}.");

    private string Who => DecidedBy switch
    {
        "rule" => "by its rule",
        "default" => "by its rule's default",
        "deadline" => "by its deadline",
        "person" => $"by {By}, a person",
        "agent" => $"by {By}, an agent",
        _ => "not yet",
    };

    /// <summary>The rows of decisions as status gives them, each named, or a position's steps that carry one; none when the runtime gave none.</summary>
    public static IReadOnlyList<DecisionRow> From(JsonElement items) =>
        items.ValueKind != JsonValueKind.Array ? [] : items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i =>
        {
            JsonElement decision = i.TryGetProperty("decision", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object ? nested : i;
            string name = Str(i, "name") ?? (Str(i, "instance") is string instance ? $"{Str(i, "key")}[{instance}]" : Str(i, "key")) ?? "?";
            if (decision.ValueKind != JsonValueKind.Object || !decision.TryGetProperty("state", out _) || !decision.TryGetProperty("decider", out _))
            {
                return null;
            }

            List<string> paths = decision.TryGetProperty("paths", out JsonElement taken) && taken.ValueKind == JsonValueKind.Array ? taken.EnumerateArray().Select(p => p.GetString() ?? "?").ToList() : [];
            List<string> evidence = decision.TryGetProperty("evidence", out JsonElement read) && read.ValueKind == JsonValueKind.Array
                ? read.EnumerateArray().Select(e => $"{Str(e, "type")} {Str(e, "id")}{(Str(e, "hash") is string hash ? " at " + hash : string.Empty)}").ToList()
                : [];
            string? record = decision.TryGetProperty("record", out JsonElement carrier) && carrier.ValueKind == JsonValueKind.Object ? Str(carrier, "id") : null;
            return new DecisionRow(name, Str(decision, "state") ?? "?", Str(decision, "decider") ?? "?", paths, Str(decision, "decidedBy"), Str(decision, "byName") ?? Str(decision, "by"), record, Str(decision, "reasons"), Str(decision, "at"), evidence);
        }).OfType<DecisionRow>().ToList();

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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

    /// <summary>For a run, its stops in the order the flow reaches them; none for a goal that is not a run, or a run with none.</summary>
    public IReadOnlyList<StopRow> Stops { get; private set; } = [];

    /// <summary>For a run, from 0.9.4 (PROC-18), each step whose timing gates its start with its due time, before it runs; none otherwise.</summary>
    public IReadOnlyList<DueRow> Due { get; private set; } = [];

    /// <summary>For a run, from 0.9.4 (PROC-19), each step's decision: who decided what and why, or that it waits on its decider; none otherwise.</summary>
    public IReadOnlyList<DecisionRow> Decisions { get; private set; } = [];

    /// <summary>From 0.9.4 (GOAL-07), for a standing agent, its state, next occurrence, deferrals and period spend; none for any other goal.</summary>
    public IReadOnlyList<StandingRow> Standing { get; private set; } = [];

    /// <summary>Work orders proposed for the goal that wait for its sponsor's approval; none unless its plan says sponsor-approves.</summary>
    public IReadOnlyList<ProposedRow> Proposed { get; private set; } = [];

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
        view.Stops = StopRow.From(Obj(goal, "flow").ValueKind == JsonValueKind.Object && Obj(goal, "flow").TryGetProperty("stops", out JsonElement stops) ? stops : default);
        view.Due = DueRow.From(Obj(goal, "flow").ValueKind == JsonValueKind.Object && Obj(goal, "flow").TryGetProperty("steps", out JsonElement timed) ? timed : default);
        view.Decisions = DecisionRow.From(Obj(goal, "flow").ValueKind == JsonValueKind.Object && Obj(goal, "flow").TryGetProperty("steps", out JsonElement decided) ? decided : default);
        view.Standing = StandingRow.From(Obj(goal, "standing"));
        view.Proposed = Items(goal, "proposed").Select(p => new ProposedRow(Str(p, "key") ?? "?", Str(p, "title") ?? "?", Str(p, "id") ?? "?", Str(p, "byName") ?? Str(p, "by") ?? "?", Str(p, "approve"))).ToList();
        string? current = Str(Obj(goal, "evaluation"), "id");
        view.Evaluations = Items(goal, "evaluations").Select(e => new EvaluationRow(Str(e, "id") ?? "?", Str(e, "byName") ?? Str(e, "by") ?? "?", Str(e, "at") ?? "?", Str(e, "outcome") ?? "?", Str(e, "summary") ?? string.Empty, Str(e, "id") == current)).ToList();
        string pathRefusals = string.Concat(Items(Obj(goal, "flow"), "pathRefusals").Select(refusal => $"\n{Str(refusal, "code")} — {Str(refusal, "message")}"));
        if (next.ValueKind != JsonValueKind.Object || Str(next, "format") != "vericairn.next/1")
        {
            view.Next = "unavailable: the runtime gave no routed steps" + pathRefusals;
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
        view.Next += pathRefusals;
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
