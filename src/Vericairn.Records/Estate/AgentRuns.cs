// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The agent-run ledger (the estate's design/goals.md § 9, decision 01M4YBR5PGCPQD94BVNC547EBA). Every coding agent's run is an
/// operation of action agent-run that records its cap before it starts and its observed usage once it settles, charged to the goal it
/// names. A settled run is charged what its usage reports, and its cap in every unit its usage does not report: unknown usage is
/// never zero. A run not yet settled is charged its whole cap, so a run in flight reserves its allowance and two runs never spend the
/// same allowance twice; only a run that ended before anything was dispatched is charged nothing. A run starts only while its goal is
/// active, within its goal's concurrency (one at a time when the budget states none) and its step's attempts, with a cap in the units
/// the budget grants and no larger than what remains of them. The record type validates with these rules and the worker refuses with
/// them before it writes; a rule judged for a record already written reads only the records before it.
/// </summary>
public static class AgentRuns
{
    /// <summary>The worker contract agent runs are recorded under.</summary>
    public const string WorkerContract = "vericairn.worker/1";

    /// <summary>The units a run spends and is charged in: wall-clock minutes, tokens and currency.</summary>
    public static readonly string[] ChargedUnits = ["wallMinutes", "tokens", "currency"];

    /// <summary>How many of a goal's runs may be in flight at once when its budget states no concurrency: an absent cap is not unlimited.</summary>
    public const int DefaultConcurrency = 1;

    /// <summary>The statuses of a run that settled with an observed outcome.</summary>
    public static readonly string[] Settled = ["observed-success", "known-failure"];

    /// <summary>One run: its intent, the first record of its operation, and its revisions in order, the latest last.</summary>
    /// <param name="Intent">The first record: the operation recorded as not-started, with its cap, before anything started.</param>
    /// <param name="Revisions">Every revision from the intent on, in order.</param>
    public sealed record Run(RecordEnvelope Intent, IReadOnlyList<RecordEnvelope> Revisions)
    {
        /// <summary>The run's id: its intent's.</summary>
        public string Id => Intent.Id;

        /// <summary>The latest revision.</summary>
        public RecordEnvelope Latest => Revisions[^1];

        /// <summary>The latest status.</summary>
        public string Status => EstateModel.BodyString(Latest, "status") ?? "?";

        /// <summary>True once it settled with an observed outcome, success or failure.</summary>
        public bool IsSettled => Array.IndexOf(Settled, Status) >= 0;

        /// <summary>True while nothing settled it: not started, dispatched or uncertain.</summary>
        public bool InFlight => !IsSettled && Status != "canceled";

        /// <summary>True when a revision records a dispatch: the harness was started.</summary>
        public bool Dispatched => Revisions.Any(r => EstateModel.BodyString(r, "dispatchedAt") is not null);

        /// <summary>True when the run is charged: it was dispatched or may still be; a run that ended before any dispatch spent nothing.</summary>
        public bool Charged => Dispatched || InFlight;

        /// <summary>The launcher, the provider its operation names.</summary>
        public string Launcher => EstateModel.BodyString(Intent, "provider") ?? "?";

        /// <summary>The goal it is charged to, as its target names it; null when it names none.</summary>
        public string? Goal => Target("goal");

        /// <summary>The assignment it carries, as its target names it.</summary>
        public string? Assignment => Target("assignment");

        /// <summary>The agent principal it runs as.</summary>
        public string? Principal => Target("principal");

        /// <summary>The workspace, a repository and branch.</summary>
        public string? Workspace => Target("workspace");

        /// <summary>The cap its intent recorded.</summary>
        public JsonElement Cap => Intent.Body.TryGetProperty("cap", out JsonElement cap) ? cap : default;

        /// <summary>The usage its settlement recorded; undefined while it has none.</summary>
        public JsonElement Usage => Latest.Body.TryGetProperty("usage", out JsonElement usage) ? usage : default;

        /// <summary>A string value of the intent's target.</summary>
        public string? Target(string key) =>
            Intent.Body.TryGetProperty("target", out JsonElement target) && target.ValueKind == JsonValueKind.Object && target.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>A goal's ledger: what its budget grants, what its runs were charged, and what remains.</summary>
    /// <param name="Goal">The goal revision whose budget counts.</param>
    /// <param name="Budget">The charged units the budget states: wallMinutes, tokens and currency (in minor units).</param>
    /// <param name="Currency">The budget's currency unit, when it states currency.</param>
    /// <param name="AttemptsPerStep">How many runs each step may have.</param>
    /// <param name="Concurrency">How many runs may be in flight at once.</param>
    /// <param name="StatesConcurrency">Whether the budget states its concurrency.</param>
    /// <param name="Spent">What its runs were charged, by unit.</param>
    /// <param name="Runs">The runs charged to the goal, any revision of it, oldest first.</param>
    public sealed record Ledger(RecordEnvelope Goal, IReadOnlyDictionary<string, long> Budget, string? Currency, int AttemptsPerStep, int Concurrency, bool StatesConcurrency, IReadOnlyDictionary<string, long> Spent, IReadOnlyList<Run> Runs)
    {
        /// <summary>The runs in flight, each charged its cap.</summary>
        public int InFlight => Runs.Count(r => r.InFlight);

        /// <summary>What remains of a unit the budget states; null for a unit it does not.</summary>
        public long? Remaining(string unit) => Budget.TryGetValue(unit, out long cap) ? cap - Spent.GetValueOrDefault(unit) : null;

        /// <summary>The units of which nothing remains.</summary>
        public IReadOnlyList<string> Exhausted => ChargedUnits.Where(u => Remaining(u) is <= 0).ToList();

        /// <summary>The runs a step has had: those charged to an assignment of the step, any revision of it.</summary>
        public int Attempts(IReadOnlyCollection<string> step) => Runs.Count(r => r.Charged && r.Assignment is not null && step.Contains(r.Assignment));

        /// <summary>A unit's budget, spend and what remains, in words.</summary>
        public string Describe(string unit) => unit == "currency"
            ? $"currency {Spent.GetValueOrDefault(unit).ToString(CultureInfo.InvariantCulture)} of {Budget.GetValueOrDefault(unit).ToString(CultureInfo.InvariantCulture)} minor units of {Currency}"
            : $"{unit} {Spent.GetValueOrDefault(unit).ToString(CultureInfo.InvariantCulture)} of {Budget.GetValueOrDefault(unit).ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Every agent run, oldest first: each operation of action agent-run whose first record was written before a record when one is
    /// given, with its revisions written before it.
    /// </summary>
    public static IReadOnlyList<Run> All(EstateModel model, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var runs = new List<Run>();
        foreach (RecordEnvelope intent in model.OfType("operation").Where(o => o.Supersedes is null && EstateModel.BodyString(o, "action") == OperationType.AgentRun && (before is null || GoalRules.Before(o, before))))
        {
            var revisions = new List<RecordEnvelope> { intent };
            var seen = new HashSet<string>(StringComparer.Ordinal) { intent.Id };
            RecordEnvelope current = intent;
            while (model.SupersededBy.TryGetValue(current.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && successor.Type == "operation" && seen.Add(successor.Id) && (before is null || GoalRules.Before(successor, before)))
            {
                revisions.Add(successor);
                current = successor;
            }

            runs.Add(new Run(intent, revisions));
        }

        return runs.OrderBy(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Intent.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The run an id names: its intent's id, the id of any of its revisions, or its operation's identity; null when none.</summary>
    public static Run? Find(EstateModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(id);
        return All(model).FirstOrDefault(r => r.Revisions.Any(v => v.Id == id) || EstateModel.BodyString(r.Intent, "identity") == id);
    }

    /// <summary>
    /// What a run is charged, by unit: what its usage reports once it settled, and its cap in every unit its usage does not report or
    /// while it has not settled; nothing for a run that ended before anything was dispatched.
    /// </summary>
    public static IReadOnlyDictionary<string, long> ChargeOf(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var charge = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!run.Charged)
        {
            return charge;
        }

        JsonElement usage = run.IsSettled ? run.Usage : default;
        HashSet<string> unreported = Strings(usage, "unreported");
        foreach (string unit in ChargedUnits)
        {
            long? used = usage.ValueKind == JsonValueKind.Object && !unreported.Contains(unit) ? Amount(usage, unit) : null;
            if ((used ?? Amount(run.Cap, unit)) is long value)
            {
                charge[unit] = value;
            }
        }

        return charge;
    }

    /// <summary>
    /// The ledger of a goal revision, from the records before a record when one is given: its budget, and the charges of every run that
    /// names any revision of the goal.
    /// </summary>
    public static Ledger Of(EstateModel model, RecordEnvelope goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        IReadOnlyList<string> chain = model.Chain(model.Latest(goal));
        List<Run> runs = All(model, before).Where(r => r.Goal is not null && chain.Contains(r.Goal)).ToList();
        JsonElement budget = goal.Body.TryGetProperty("budget", out JsonElement b) ? b : default;
        var granted = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string unit in ChargedUnits)
        {
            if (Amount(budget, unit) is long cap)
            {
                granted[unit] = cap;
            }
        }

        var spent = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, long> charge in runs.Select(ChargeOf))
        {
            foreach ((string unit, long value) in charge)
            {
                spent[unit] = spent.GetValueOrDefault(unit) + value;
            }
        }

        long? concurrency = Amount(budget, "concurrency");
        return new Ledger(goal, granted, CurrencyUnit(budget), (int)(Amount(budget, "attemptsPerStep") ?? 0), (int)(concurrency ?? DefaultConcurrency), concurrency is not null, spent, runs);
    }

    /// <summary>The revision of the goal an id names that was current when a record was written, or the latest; null when the id names no goal or none before it.</summary>
    public static RecordEnvelope? GoalAt(EstateModel model, string goal, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (!model.ById.TryGetValue(goal, out RecordEnvelope? record) || record.Type != "goal")
        {
            return null;
        }

        RecordEnvelope? current = null;
        foreach (string id in model.Chain(model.Latest(record)))
        {
            if (before is null || GoalRules.Before(model.ById[id], before))
            {
                current = model.ById[id];
            }
        }

        return current;
    }

    /// <summary>
    /// The cap a new run of a goal takes from its budget: the wall minutes asked for, or what remains, whichever is less; the goal's
    /// attempts per step; and what remains of the tokens and the currency where the budget states them.
    /// </summary>
    public static JsonObject CapFor(Ledger ledger, long? wallMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        long remaining = Math.Max(ledger.Remaining("wallMinutes") ?? 0, 0);
        var cap = new JsonObject
        {
            ["wallMinutes"] = wallMinutes is long asked ? Math.Min(asked, remaining) : remaining,
            ["attemptsPerStep"] = ledger.AttemptsPerStep,
        };
        if (ledger.Remaining("tokens") is long tokens)
        {
            cap["tokens"] = Math.Max(tokens, 0);
        }

        if (ledger.Remaining("currency") is long amount)
        {
            cap["currency"] = new JsonObject { ["amount"] = Math.Max(amount, 0), ["unit"] = ledger.Currency };
        }

        return cap;
    }

    /// <summary>
    /// Why a run with a cap may not start now on an assignment of a goal revision, judged from the records before a record when one is
    /// given; empty when it may. The goal is active; the cap states exactly the units the budget grants other than concurrency, and
    /// no more attempts per step than the budget; each charged unit's cap is no larger than what remains; fewer runs are in flight
    /// than the goal's concurrency; and the step has had fewer runs than its attempts.
    /// </summary>
    public static IReadOnlyList<(string Code, string Message)> StartRefusals(EstateModel model, RecordEnvelope goal, string? assignment, JsonElement cap, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var refusals = new List<(string Code, string Message)>();
        string status = GoalRules.Status(model, goal, before);
        if (status != "active")
        {
            refusals.Add(("operation.fenced", $"Goal {goal.Id} is {status}: a run starts only while its goal is active, and a goal that is budget-limited, paused or blocked runs nothing until it is resumed."));
        }

        Ledger ledger = Of(model, goal, before);
        foreach (string unit in ChargedUnits)
        {
            bool budgeted = ledger.Budget.ContainsKey(unit);
            bool capped = Amount(cap, unit) is not null;
            if (budgeted && !capped)
            {
                refusals.Add(("operation.cap", $"The goal's budget caps {unit}, and the run's cap states none."));
            }
            else if (!budgeted && capped)
            {
                refusals.Add(("operation.cap", $"The run's cap states {unit}, which the goal's budget does not grant."));
            }
        }

        if (ledger.Currency is not null && CurrencyUnit(cap) is string unitCapped && unitCapped != ledger.Currency)
        {
            refusals.Add(("operation.cap", $"The run's cap is in {unitCapped}, and the goal's budget in {ledger.Currency}."));
        }

        long? attempts = Amount(cap, "attemptsPerStep");
        if (attempts is null || attempts > ledger.AttemptsPerStep)
        {
            refusals.Add(("operation.cap", $"The run's cap states the step's attempts, at most the {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} the goal's budget grants."));
        }

        if (Amount(cap, "concurrency") is not null)
        {
            refusals.Add(("operation.cap", "A run's cap states no concurrency; the goal's budget says how many of its runs may be in flight."));
        }

        foreach (string unit in ChargedUnits.Where(ledger.Budget.ContainsKey))
        {
            long remaining = ledger.Remaining(unit)!.Value;
            if (remaining <= 0)
            {
                refusals.Add(("operation.budget", $"The goal's budget is spent: {ledger.Describe(unit)} charged to its runs, every run in flight at its cap."));
            }
            else if (Amount(cap, unit) is long capped && capped > remaining)
            {
                refusals.Add(("operation.budget", $"The run's cap of {capped.ToString(CultureInfo.InvariantCulture)} {unit} is more than the {remaining.ToString(CultureInfo.InvariantCulture)} that remain of the goal's budget ({ledger.Describe(unit)})."));
            }
        }

        if (ledger.InFlight >= ledger.Concurrency)
        {
            refusals.Add(("operation.budget", $"{ledger.InFlight.ToString(CultureInfo.InvariantCulture)} of the goal's runs {(ledger.InFlight == 1 ? "is" : "are")} in flight, and its budget allows {ledger.Concurrency.ToString(CultureInfo.InvariantCulture)} at a time{(ledger.StatesConcurrency ? string.Empty : ", one when it states no concurrency")}."));
        }

        if (assignment is not null)
        {
            IReadOnlyList<string> step = model.ById.TryGetValue(assignment, out RecordEnvelope? record) ? model.Chain(model.Latest(record)) : [assignment];
            int had = ledger.Attempts(step);
            if (had >= ledger.AttemptsPerStep)
            {
                refusals.Add(("operation.budget", $"The step has had {had.ToString(CultureInfo.InvariantCulture)} runs, the {ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)} attempts per step the goal's budget grants."));
            }
        }

        return refusals;
    }

    /// <summary>A unit's amount in caps or usage: a number, or for currency the amount in minor units; null when it states none.</summary>
    public static long? Amount(JsonElement caps, string unit)
    {
        if (caps.ValueKind != JsonValueKind.Object || !caps.TryGetProperty(unit, out JsonElement value))
        {
            return null;
        }

        if (unit == "currency")
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("amount", out JsonElement amount) && amount.TryGetInt64(out long minor) ? minor : null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
    }

    /// <summary>The currency unit caps or usage state, or null.</summary>
    public static string? CurrencyUnit(JsonElement caps) =>
        caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty("currency", out JsonElement currency) && currency.ValueKind == JsonValueKind.Object && currency.TryGetProperty("unit", out JsonElement unit) && unit.ValueKind == JsonValueKind.String ? unit.GetString() : null;

    private static HashSet<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
}
