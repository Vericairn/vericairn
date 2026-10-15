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
/// The next-eligible view routed (the estate's design/goals.md §§ 6 and 7): each step of a goal with the state status derives, what
/// holds it while it is planned, and for each eligible step the principal its role's roster reaches from this machine, or why none is
/// available, with whether the goal's budget allows it. A step a principal already holds stays with it: a run in flight, a person's
/// assignment, an agent's result awaiting review, an ask about it that waits; an agent that holds a step and cannot go on is never
/// replaced by another contributor. The routes read this machine's launchers and keys, so the view is not the same on every machine; it
/// stays a read-only query, and only the one-step operation acts on its next step.
/// </summary>
public static partial class NextEligible
{
    /// <summary>
    /// The route an eligible step takes from this machine, or what holds it, in the state language's step-route vocabulary: agent (an
    /// agent carries it, in a run), person (a person is assigned it and asked), reserved (a reserved action: a person who holds it is
    /// assigned it and asked), unavailable (no principal is available, and the sponsor is asked), running (a run of it is in flight),
    /// assigned (with the person it is assigned to), review (an agent's result awaits review), asked (an ask about it waits) and
    /// answered (no principal is available, and the principal asked answered, so is not asked again).
    /// </summary>
    public static readonly string[] RouteStates = ["agent", "person", "reserved", "unavailable", "running", "assigned", "review", "asked", "answered"];

    /// <summary>The route states the one-step operation acts on; every other state holds the step.</summary>
    public static readonly string[] Dispatching = ["agent", "person", "reserved", "unavailable"];

    /// <summary>The route of an eligible step from this machine.</summary>
    /// <param name="Role">The role it is carried in.</param>
    /// <param name="Reserved">The reserved action its role holds, or null.</param>
    /// <param name="Source">Where the role's principals come from.</param>
    /// <param name="Candidates">The principals the roster names for the role, in order of preference, each with whether it is available; empty when the step is held.</param>
    /// <param name="State">The route, or what holds the step: one of <see cref="RouteStates"/>.</param>
    /// <param name="Principal">Who it reaches: the agent or the person it is routed to or held by, or the principal asked.</param>
    /// <param name="Launcher">For an agent, the launcher here that starts its harness.</param>
    /// <param name="Assignment">The assignment that holds it, the latest current one of its work order.</param>
    /// <param name="Ask">The latest ask about it, in any state.</param>
    /// <param name="Run">A run of it in flight.</param>
    /// <param name="Budget">Why the goal's budget does not allow the step now (for an agent, a run of it); empty when it does.</param>
    /// <param name="Custody">For an agent, the custody guard here: why an unattended run of the goal may not launch; empty when nothing stands in the way.</param>
    /// <param name="Reason">Why it takes this route, or what it waits for, in words.</param>
    public sealed record Route(string Role, string? Reserved, Routing.Source Source, IReadOnlyList<Routing.Candidate> Candidates, string State, string? Principal, string? Launcher, RecordEnvelope? Assignment, RecordEnvelope? Ask, AgentRuns.Run? Run, IReadOnlyList<string> Budget, IReadOnlyList<string> Custody, string Reason)
    {
        /// <summary>True when the one-step operation acts on it.</summary>
        public bool Dispatches => Array.IndexOf(Dispatching, State) >= 0;

        /// <summary>True when the goal's budget allows it now.</summary>
        public bool BudgetAllows => Budget.Count == 0;
    }

    /// <summary>One step of a goal: a work order of its work.</summary>
    /// <param name="Order">The work order's current revision.</param>
    /// <param name="Status">The state status derives for it.</param>
    /// <param name="Planned">For a planned work order, what relates it to the records that can hold it.</param>
    /// <param name="Route">For an eligible step, its route from this machine.</param>
    /// <param name="Assignment">The latest current assignment of it, which holds it.</param>
    public sealed record GoalStep(RecordEnvelope Order, string Status, Item? Planned, Route? Route, RecordEnvelope? Assignment);

    /// <summary>A goal's steps routed from this machine.</summary>
    /// <param name="Goal">The goal's current revision.</param>
    /// <param name="Status">Its status.</param>
    /// <param name="Ledger">Its agent-run ledger: what its budget grants and what its runs were charged.</param>
    /// <param name="Fenced">Why a new run of it would not start now, whatever the step; empty when one would.</param>
    /// <param name="Steps">Every work order of its work, by key.</param>
    /// <param name="Asks">Every current ask that names the goal, oldest first.</param>
    public sealed record GoalRoutes(RecordEnvelope Goal, string Status, AgentRuns.Ledger Ledger, IReadOnlyList<string> Fenced, IReadOnlyList<GoalStep> Steps, IReadOnlyList<RecordEnvelope> Asks)
    {
        /// <summary>The eligible steps, each with its route.</summary>
        public IEnumerable<GoalStep> Eligible => Steps.Where(s => s.Route is not null);

        /// <summary>The step the one-step operation takes: the first eligible one, by key, that a route dispatches; null when none does.</summary>
        public GoalStep? Next => Steps.FirstOrDefault(s => s.Route is { Dispatches: true });
    }

    /// <summary>
    /// A goal's steps routed from this machine: every work order of its work with its state, what holds each planned one, and the route
    /// of each eligible one. Read-only: nothing is recorded or started.
    /// </summary>
    public static GoalRoutes Routes(EstateModel model, RecordEnvelope goal, Routing.Machine machine)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(machine);
        RecordEnvelope current = model.Latest(goal);
        string status = GoalRules.Status(model, current);
        AgentRuns.Ledger ledger = AgentRuns.Of(model, current);
        IReadOnlyList<string> fenced = AgentRuns.StartRefusals(model, current, null, Element(AgentRuns.CapFor(ledger))).Select(r => r.Message).ToList();
        Dictionary<string, string> statuses = EstateStatus.WorkItems(model).ToDictionary(i => i.Id, i => i.Status, StringComparer.Ordinal);
        List<AgentRuns.Run> runs = AgentRuns.All(model).Where(r => r.InFlight).ToList();
        var steps = new List<GoalStep>();
        foreach (RecordEnvelope order in GoalRules.Work(model, current))
        {
            string state = statuses.GetValueOrDefault(order.Id) ?? "planned";
            IReadOnlyList<RecordEnvelope> assignments = Routing.Assignments(model, order);
            RecordEnvelope? holder = assignments.Count == 0 ? null : assignments[^1];
            if (state != "planned")
            {
                steps.Add(new GoalStep(order, state, null, null, holder));
                continue;
            }

            var item = new Item(order, Relations(model, order, statuses));
            Route? route = item.Eligible ? RouteOf(model, current, status, ledger, order, assignments, runs, machine) : null;
            steps.Add(new GoalStep(order, state, item, route, holder));
        }

        return new GoalRoutes(current, status, ledger, fenced, steps, Routing.AsksOf(model, current));
    }

    /// <summary>The route of an eligible step, or what holds it.</summary>
    private static Route RouteOf(EstateModel model, RecordEnvelope goal, string status, AgentRuns.Ledger ledger, RecordEnvelope order, IReadOnlyList<RecordEnvelope> assignments, List<AgentRuns.Run> runs, Routing.Machine machine)
    {
        string role = Routing.RoleOf(order);
        string? reserved = Routing.ReservedAction(role);
        (IReadOnlyList<string> principals, Routing.Source source) = Routing.Principals(model, goal, role);
        IReadOnlyList<RecordEnvelope> asks = Routing.AsksAbout(model, goal, order);
        RecordEnvelope? latestAsk = asks.Count == 0 ? null : asks[^1];
        RecordEnvelope? waiting = asks.LastOrDefault(a => Asks.IsWaiting(Asks.State(model, a).State));
        RecordEnvelope? holder = assignments.Count == 0 ? null : assignments[^1];
        IReadOnlyList<string> fence = status == "active" ? [] : [$"The goal is {status}: nothing of it is dispatched until it is active."];

        // A run of the step in flight holds it, whichever of its assignments the run carries.
        if (runs.FirstOrDefault(r => r.Assignment is not null && assignments.Any(a => model.Chain(a).Contains(r.Assignment))) is AgentRuns.Run running)
        {
            return Held("running", running.Principal, $"run {running.Id} of {Routing.NameOf(model, running.Principal)} is in flight ({running.Status}); a stop is asked with vericairn worker stop", running);
        }

        string? contributor = holder is null ? null : EstateModel.BodyString(holder, "contributor");
        string holderKind = contributor is not null && model.CurrentPrincipal(contributor) is RecordEnvelope who ? EstateModel.BodyString(who, "kind") ?? "?" : "missing";
        if (holder is not null && holderKind == "human")
        {
            return Held("assigned", contributor, $"assigned to {Routing.NameOf(model, contributor)} by assignment {holder.Id}{TakesOver(model, holder)}");
        }

        // An agent's result goes to review; a review that finds what to change sends the step back to the agent for another attempt.
        RecordEnvelope? result = holder is null ? null : ResultOfLatestAttempt(model, holder);
        RecordEnvelope? findings = result is null ? null : Findings(model, result);
        if (result is not null && findings is null)
        {
            return Held("review", contributor, $"the result {result.Id} of {Routing.NameOf(model, result.By)} on assignment {holder!.Id} is {WorkView.ResultState(model, result)}; a principal other than its author reviews it");
        }

        if (waiting is not null)
        {
            string? asked = EstateModel.BodyString(model.Latest(waiting), "to");
            return Held("asked", asked, $"ask {waiting.Id} to {Routing.NameOf(model, asked)} about it waits for an answer, and the step waits with it");
        }

        if (holder is not null)
        {
            // The step stays with the agent that holds it: an assignment to another agent would start its attempts afresh.
            Routing.Candidate going = Routing.Evaluate(model, goal, ledger, contributor!, role, holder, machine);
            string again = findings is null ? "goes on with another run" : $"goes on with another attempt, since review {findings.Id} found what to change in its result {result!.Id}";
            return going.Available
                ? Agent(going, holder, [going], $"{going.Name} holds it by assignment {holder.Id} and {again}")
                : Unavailable([going], $"{going.Name} holds it by assignment {holder.Id} and cannot go on from here ({string.Join("; ", going.Reasons)}); it is never given to another contributor without the sponsor");
        }

        List<Routing.Candidate> candidates = principals.Select(p => Routing.Evaluate(model, goal, ledger, p, role, null, machine)).ToList();
        if (candidates.FirstOrDefault(c => c.Available) is Routing.Candidate first)
        {
            if (first.IsAgent)
            {
                return Agent(first, null, candidates, $"the first available of the {role} role's principals, from {source.Description}");
            }

            string why = reserved is null
                ? $"the first available of the {role} role's principals, from {source.Description}: a person is assigned the step and asked"
                : $"a reserved action, {Routing.Describe(reserved)}: the first of the {role} role's principals who holds it, from {source.Description}, is assigned the step and asked; no agent carries it without a grant in force";
            return new Route(role, reserved, source, candidates, reserved is null ? "person" : "reserved", first.Principal, null, null, latestAsk, null, fence, [], why);
        }

        return Unavailable(candidates, candidates.Count == 0
            ? $"{source.Description} names no principal for the {role} role"
            : $"no principal of the {role} role is available here, from {source.Description}");

        Route Held(string state, string? principal, string reason, AgentRuns.Run? run = null) =>
            new(role, reserved, source, [], state, principal, null, holder, latestAsk, run, fence, [], reason);

        Route Agent(Routing.Candidate agent, RecordEnvelope? assignment, IReadOnlyList<Routing.Candidate> evaluated, string reason)
        {
            IReadOnlyList<string> budget = AgentRuns.StartRefusals(model, goal, assignment?.Id, Element(AgentRuns.CapFor(ledger))).Select(r => r.Message).ToList();
            return new Route(role, reserved, source, evaluated, "agent", agent.Principal, agent.Launcher, assignment, latestAsk, null, budget, machine.Custody(goal, agent.Principal), reason);
        }

        Route Unavailable(IReadOnlyList<Routing.Candidate> evaluated, string reason)
        {
            string? asked = Routing.Asked(model, goal, reserved, machine.Now);
            if (asks.LastOrDefault(a => EstateModel.BodyString(model.Latest(a), "to") == asked && Asks.State(model, a).State == "answered") is RecordEnvelope answered)
            {
                return new Route(role, reserved, source, evaluated, "answered", asked, null, holder, latestAsk, null, fence, [], $"{reason}; {Routing.NameOf(model, asked)} answered ask {answered.Id} about it, and is not asked again while the answer holds");
            }

            return new Route(role, reserved, source, evaluated, "unavailable", asked, null, holder, latestAsk, null, fence, [], $"{reason}; {Routing.NameOf(model, asked)} is asked, since a step never falls back to a contributor the roster does not name");
        }
    }

    /// <summary>The goal's steps as text: the budget, each eligible step with its route, the held steps, the others, and the goal's asks.</summary>
    public static string RenderText(EstateModel model, GoalRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        var text = new StringBuilder();
        RecordEnvelope goal = routes.Goal;
        text.Append("Next steps of goal ").Append(EstateModel.BodyString(goal, "title")).Append(" [goal ").Append(goal.Id).Append("], ").Append(routes.Status).Append(", routed from this machine\n");
        AgentRuns.Ledger ledger = routes.Ledger;
        List<string> units = AgentRuns.ChargedUnits.Where(ledger.Budget.ContainsKey).Select(u => $"{ledger.Describe(u)} charged, {ledger.Remaining(u)!.Value.ToString(CultureInfo.InvariantCulture)} remain").ToList();
        text.Append("  Budget: ").Append(string.Join("; ", units)).Append("; ").Append(ledger.InFlight.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(ledger.Concurrency.ToString(CultureInfo.InvariantCulture))
            .Append(ledger.Concurrency == 1 ? " run" : " runs").Append(" in flight; ").Append(ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)).Append(" attempts per step\n");
        foreach (string fence in routes.Fenced)
        {
            text.Append("    a new run is fenced: ").Append(fence).Append('\n');
        }

        List<GoalStep> eligible = routes.Eligible.ToList();
        List<GoalStep> held = routes.Steps.Where(s => s.Planned is { Eligible: false }).ToList();
        List<GoalStep> others = routes.Steps.Where(s => s.Planned is null).ToList();
        text.Append("  Eligible: ").Append(eligible.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(Count(eligible.Count + held.Count, "planned step")).Append('\n');
        foreach (GoalStep step in eligible)
        {
            Route route = step.Route!;
            text.Append("  ").Append(EstateModel.BodyString(step.Order, "key")).Append("  ").Append(EstateModel.BodyString(step.Order, "title")).Append(" [work-order ").Append(step.Order.Id).Append("]\n");
            text.Append("    ").Append(route.Role).Append(route.Reserved is null ? string.Empty : $", a reserved action ({Routing.Describe(route.Reserved)})").Append(": ").Append(route.State)
                .Append(route.Principal is null ? string.Empty : $", {Routing.NameOf(model, route.Principal)} [principal {route.Principal}]").Append(route.Launcher is null ? string.Empty : $" through {route.Launcher}").Append('\n');
            text.Append("      ").Append(route.Reason).Append('\n');
            text.Append("      budget: ").Append(route.BudgetAllows ? "allows it" : "does not allow it now: " + string.Join(" ", route.Budget)).Append('\n');
            if (route.State == "agent")
            {
                text.Append("      custody here: ").Append(route.Custody.Count == 0 ? "no device of an operator or of the sponsor signs here without its owner's presence" : "an unattended run is refused: " + string.Join(" ", route.Custody)).Append('\n');
            }

            foreach (Routing.Candidate candidate in route.Candidates)
            {
                text.Append("      candidate ").Append(candidate.Name).Append(" (").Append(candidate.Kind).Append("): ").Append(candidate.Available ? "available" : "not available: " + string.Join("; ", candidate.Reasons)).Append('\n');
            }
        }

        text.Append("  Held: ").Append(held.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (GoalStep step in held)
        {
            text.Append("    ").Append(EstateModel.BodyString(step.Order, "key")).Append("  ").Append(EstateModel.BodyString(step.Order, "title")).Append(" [work-order ").Append(step.Order.Id).Append("]\n");
            foreach (Relation relation in step.Planned!.Relations.Where(r => r.Holds))
            {
                text.Append("      ").Append(relation.Kind).Append(' ').Append(relation.Key ?? relation.Type + " " + relation.Id).Append(": ").Append(relation.State).Append(", holds it\n");
            }
        }

        text.Append("  Other steps: ").Append(others.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (GoalStep step in others)
        {
            text.Append("    ").Append(EstateModel.BodyString(step.Order, "key")).Append("  ").Append(step.Status).Append("  ").Append(EstateModel.BodyString(step.Order, "title"))
                .Append(step.Assignment is null ? string.Empty : $", assigned to {Routing.NameOf(model, EstateModel.BodyString(step.Assignment, "contributor"))} [assignment {step.Assignment.Id}]").Append('\n');
        }

        text.Append("  Asks about the goal: ").Append(routes.Asks.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (RecordEnvelope ask in routes.Asks)
        {
            RecordEnvelope latest = model.Latest(ask);
            text.Append("    ").Append(Asks.State(model, ask).State).Append(", to ").Append(Routing.NameOf(model, EstateModel.BodyString(latest, "to"))).Append(": ").Append(EstateModel.BodyString(latest, "question")).Append(" [ask ").Append(ask.Id).Append("]\n");
        }

        text.Append(routes.Next is GoalStep next
            ? $"  Next: {EstateModel.BodyString(next.Order, "key")}, {next.Route!.State}\n"
            : "  Next: nothing to dispatch; every eligible step is held, or none is eligible\n");
        return text.ToString();
    }

    /// <summary>The goal's steps as canonical JSON: the same records give the same bytes on one machine, and the routes follow its launchers and keys.</summary>
    public static string RenderJson(EstateModel model, GoalRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        AgentRuns.Ledger ledger = routes.Ledger;
        RecordEnvelope goal = routes.Goal;
        var json = new JsonObject
        {
            ["format"] = "vericairn.next/1",
            ["goal"] = new JsonObject { ["id"] = goal.Id, ["hash"] = goal.ComputedHash, ["title"] = EstateModel.BodyString(goal, "title"), ["status"] = routes.Status, ["sponsor"] = EstateModel.BodyString(goal, "sponsor") },
            ["budget"] = new JsonObject
            {
                ["units"] = new JsonArray(AgentRuns.ChargedUnits.Where(ledger.Budget.ContainsKey).Select(u => (JsonNode)new JsonObject { ["unit"] = u, ["budget"] = ledger.Budget[u], ["spent"] = ledger.Spent.GetValueOrDefault(u), ["remaining"] = ledger.Remaining(u), ["currency"] = u == "currency" ? ledger.Currency : null }).ToArray()),
                ["attemptsPerStep"] = ledger.AttemptsPerStep,
                ["concurrency"] = ledger.Concurrency,
                ["inFlight"] = ledger.InFlight,
                ["fenced"] = Strings(routes.Fenced),
            },
            ["steps"] = new JsonArray(routes.Steps.Select(s => (JsonNode)StepJson(model, s)).ToArray()),
            ["next"] = routes.Next is GoalStep next ? EstateModel.BodyString(next.Order, "key") : null,
            ["asks"] = new JsonArray(routes.Asks.Select(a => (JsonNode)AskJson(model, a)).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static JsonObject StepJson(EstateModel model, GoalStep step)
    {
        Route? route = step.Route;
        string? contributor = step.Assignment is null ? null : EstateModel.BodyString(step.Assignment, "contributor");
        return new JsonObject
        {
            ["key"] = EstateModel.BodyString(step.Order, "key"),
            ["id"] = step.Order.Id,
            ["title"] = EstateModel.BodyString(step.Order, "title"),
            ["kind"] = EstateModel.BodyString(step.Order, "kind"),
            ["status"] = step.Status,
            ["role"] = Routing.RoleOf(step.Order),
            ["eligible"] = step.Planned?.Eligible,
            ["holds"] = step.Planned is null ? null : new JsonArray(step.Planned.Relations.Where(r => r.Holds).Select(r => (JsonNode)new JsonObject { ["relation"] = r.Kind, ["type"] = r.Type, ["id"] = r.Id, ["key"] = r.Key, ["state"] = r.State }).ToArray()),
            ["assignment"] = step.Assignment is null ? null : new JsonObject { ["id"] = step.Assignment.Id, ["contributor"] = contributor, ["contributorName"] = Routing.NameOf(model, contributor) },
            ["route"] = route is null ? null : new JsonObject
            {
                ["state"] = route.State,
                ["dispatches"] = route.Dispatches,
                ["reserved"] = route.Reserved,
                ["roster"] = new JsonObject { ["id"] = route.Source.Roster?.Id, ["source"] = route.Source.Description },
                ["principal"] = route.Principal,
                ["principalName"] = route.Principal is null ? null : Routing.NameOf(model, route.Principal),
                ["launcher"] = route.Launcher,
                ["reason"] = route.Reason,
                ["ask"] = route.Ask?.Id,
                ["run"] = route.Run?.Id,
                ["budget"] = new JsonObject { ["allows"] = route.BudgetAllows, ["reasons"] = Strings(route.Budget) },
                ["custody"] = route.State == "agent" ? new JsonObject { ["refusals"] = Strings(route.Custody) } : null,
                ["candidates"] = new JsonArray(route.Candidates.Select(c => (JsonNode)new JsonObject { ["principal"] = c.Principal, ["name"] = c.Name, ["kind"] = c.Kind, ["available"] = c.Available, ["launcher"] = c.Launcher, ["reasons"] = Strings(c.Reasons) }).ToArray()),
            },
        };
    }

    private static JsonObject AskJson(EstateModel model, RecordEnvelope ask)
    {
        RecordEnvelope latest = model.Latest(ask);
        (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, ask);
        string? to = EstateModel.BodyString(latest, "to");
        return new JsonObject
        {
            ["id"] = ask.Id,
            ["at"] = ask.At,
            ["to"] = to,
            ["toName"] = Routing.NameOf(model, to),
            ["state"] = state,
            ["recommendation"] = EstateModel.BodyString(latest, "recommendation"),
            ["question"] = EstateModel.BodyString(latest, "question"),
            ["consequence"] = EstateModel.BodyString(latest, "consequence"),
            ["subject"] = latest.Subject is null ? null : new JsonObject { ["type"] = latest.Subject.Type, ["id"] = latest.Subject.Id },
            ["answers"] = new JsonArray(answers.Select(d => (JsonNode)d.Id).ToArray()),
        };
    }

    /// <summary>The result of an assignment's latest attempt, when it has one.</summary>
    private static RecordEnvelope? ResultOfLatestAttempt(EstateModel model, RecordEnvelope assignment)
    {
        IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
        return attempts.Count == 0 ? null : WorkView.ResultOf(model, attempts[^1]);
    }

    /// <summary>
    /// The latest current review of a result, by another principal than its author, that finds what to change in it (outcome findings
    /// or fail); null when there is none, or when a later acceptance of it was recorded.
    /// </summary>
    private static RecordEnvelope? Findings(EstateModel model, RecordEnvelope result)
    {
        if (WorkView.ResultState(model, result) != "awaiting review")
        {
            return null;
        }

        IReadOnlyList<string> chain = model.Chain(result);
        return model.Current("review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && r.By != result.By && EstateModel.BodyString(r, "outcome") is "findings" or "fail")
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>What an assignment that takes over another one says of it: the assignment among its brief's sources.</summary>
    private static string TakesOver(EstateModel model, RecordEnvelope assignment)
    {
        if (!assignment.Body.TryGetProperty("brief", out JsonElement brief) || !brief.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (JsonElement source in sources.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object))
        {
            if (source.TryGetProperty("type", out JsonElement type) && type.GetString() == "assignment" && source.TryGetProperty("id", out JsonElement id) && model.ById.TryGetValue(id.GetString() ?? string.Empty, out RecordEnvelope? earlier))
            {
                return $", which takes it over from {Routing.NameOf(model, EstateModel.BodyString(earlier, "contributor"))}'s assignment {earlier.Id}";
            }
        }

        return string.Empty;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    private static string Count(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");

    private static JsonElement Element(JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }
}
