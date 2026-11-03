// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// A run's steps routed from this machine (the estate's design/flow.md § 6): its position from the records, and for each work or review
/// step that is ready, the principal its role reaches through the goal's roles, its roster or the estate's, exactly as a work order's
/// step is routed, with the assignments and asks of that step alone. A step of a role the flow marks person is never routed to an agent;
/// a reserved step is never routed; a stale step is routed afresh, so its new brief pins its inputs as they are now.
/// </summary>
public static partial class NextEligible
{
    /// <summary>The route states the one-step operation acts on for a run: an agent's run, or a person's assignment and ask.</summary>
    public static readonly ImmutableArray<string> FlowDispatching = ["agent", "person", "reserved"];

    /// <summary>One step instance of a run, with its route from this machine when it is a ready work or review step.</summary>
    /// <param name="Step">The step instance and its derived state.</param>
    /// <param name="Route">Its route, or null for a step that is not routed now.</param>
    public sealed record FlowStepRoute(FlowPosition.Instance Step, Route? Route)
    {
        /// <summary>True when the one-step operation acts on it.</summary>
        public bool Dispatches => Route is not null && FlowDispatching.IndexOf(Route.State) >= 0;
    }

    /// <summary>A run's steps routed from this machine.</summary>
    /// <param name="Goal">The goal's current revision.</param>
    /// <param name="Status">Its status.</param>
    /// <param name="Ledger">Its agent-run ledger.</param>
    /// <param name="Fenced">Why a new run of it would not start now, whatever the step.</param>
    /// <param name="Position">The run's position from the records.</param>
    /// <param name="Steps">Every step instance, in the flow's order, with its route.</param>
    /// <param name="Asks">Every current ask that names the goal, oldest first.</param>
    public sealed record FlowRunRoutes(RecordEnvelope Goal, string Status, AgentRuns.Ledger Ledger, IReadOnlyList<string> Fenced, FlowPosition.Position Position, IReadOnlyList<FlowStepRoute> Steps, IReadOnlyList<RecordEnvelope> Asks)
    {
        /// <summary>The step the one-step operation takes: the first, in the flow's order, whose route dispatches; null when none does.</summary>
        public FlowStepRoute? Next => Steps.FirstOrDefault(s => s.Dispatches);
    }

    /// <summary>A run's steps routed from this machine; read-only, nothing is recorded or started.</summary>
    public static FlowRunRoutes FlowRoutes(EstateModel model, RecordEnvelope goal, Routing.Machine machine)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(machine);
        RecordEnvelope current = model.Latest(goal);
        string status = GoalRules.Status(model, current);
        AgentRuns.Ledger ledger = AgentRuns.Of(model, current);
        IReadOnlyList<string> fenced = AgentRuns.StartRefusals(model, current, null, Element(AgentRuns.CapFor(ledger))).Select(r => r.Message).ToList();
        FlowPosition.Position position = FlowPosition.Derive(model, current, machine.Now);
        List<AgentRuns.Run> runs = AgentRuns.All(model).Where(r => r.InFlight).ToList();
        IReadOnlyList<string> goals = model.Chain(current);
        var steps = new List<FlowStepRoute>();
        foreach (FlowPosition.Instance instance in position.Steps)
        {
            if (position.Work is null || position.Flow is null || !instance.Step.IsRouted || instance.Step.Reserved is not null || instance.Waiting
                || instance.State is not ("next" or "stopped" or "stale" or "in-review"))
            {
                steps.Add(new FlowStepRoute(instance, null));
                continue;
            }

            IReadOnlyList<RecordEnvelope> assignments = Routing.Assignments(model, position.Work, instance.Ref);
            var held = assignments.SelectMany(a => model.Chain(a)).ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<RecordEnvelope> asks = model.Current("ask")
                .Where(a => EstateModel.BodyString(a, "goal") is string g && goals.Contains(g) && EstateModel.BodyString(a, "assignment") is string named && held.Contains(named))
                .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();

            // A stale step reruns from a fresh assignment whose brief pins its inputs as they are now, once no run of it is in flight.
            if (instance.State == "stale" && !runs.Any(r => r.Assignment is not null && held.Contains(r.Assignment)))
            {
                assignments = [];
                asks = [];
            }

            string role = instance.Step.Role;
            bool person = position.Flow.Role(role)?.Holder == "person";
            Route route = RouteOf(model, current, status, ledger, role, Routing.ReservedAction(role), assignments, asks, runs, machine, candidate => person && candidate.IsAgent
                ? candidate with { Available = false, Reasons = [.. candidate.Reasons, $"the flow marks the {role} role a person's, and an agent never holds it"] }
                : candidate);
            steps.Add(new FlowStepRoute(instance, route));
        }

        return new FlowRunRoutes(current, status, ledger, fenced, position, steps, Routing.AsksOf(model, current));
    }

    /// <summary>A run's steps as text: the budget, the position, each routed step with its route, and the goal's asks.</summary>
    public static string RenderText(EstateModel model, FlowRunRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        var text = new StringBuilder();
        RecordEnvelope goal = routes.Goal;
        text.Append("Next steps of goal ").Append(EstateModel.BodyString(goal, "title")).Append(" [goal ").Append(goal.Id).Append("], ").Append(routes.Status).Append(", a run, routed from this machine\n");
        AgentRuns.Ledger ledger = routes.Ledger;
        List<string> units = AgentRuns.ChargedUnits.Where(ledger.Budget.ContainsKey).Select(u => $"{ledger.Describe(u)} charged, {ledger.Remaining(u)!.Value.ToString(CultureInfo.InvariantCulture)} remain").ToList();
        text.Append("  Budget: ").Append(string.Join("; ", units)).Append("; ").Append(ledger.InFlight.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(ledger.Concurrency.ToString(CultureInfo.InvariantCulture))
            .Append(ledger.Concurrency == 1 ? " run" : " runs").Append(" in flight; ").Append(ledger.AttemptsPerStep.ToString(CultureInfo.InvariantCulture)).Append(" attempts per step\n");
        foreach (string fence in routes.Fenced)
        {
            text.Append("    a new run is fenced: ").Append(fence).Append('\n');
        }

        foreach (string line in FlowPosition.RenderText(model, routes.Position).TrimEnd('\n').Split('\n'))
        {
            text.Append("  ").Append(line).Append('\n');
        }

        List<FlowStepRoute> routed = routes.Steps.Where(s => s.Route is not null).ToList();
        text.Append("  Routed: ").Append(routed.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (FlowStepRoute step in routed)
        {
            Route route = step.Route!;
            text.Append("  ").Append(step.Step.Name).Append("  ").Append(step.Step.Step.Title).Append(" [").Append(step.Step.State).Append("]\n");
            text.Append("    ").Append(route.Role).Append(route.Reserved is null ? string.Empty : $", a reserved action ({Routing.Describe(route.Reserved)})").Append(": ").Append(route.State)
                .Append(route.Principal is null ? string.Empty : $", {Routing.NameOf(model, route.Principal)} [principal {route.Principal}]").Append(route.Launcher is null ? string.Empty : $" through {route.Launcher}").Append('\n');
            text.Append("      ").Append(route.Reason).Append('\n');
            text.Append("      budget: ").Append(route.BudgetAllows ? "allows it" : "does not allow it now: " + string.Join(" ", route.Budget)).Append('\n');
            if (route.State == "agent")
            {
                text.Append("      custody here: ").Append(route.Custody.Count == 0
                    ? "no device of an operator or of the sponsor signs here without its owner's presence"
                    : route.Exception is RecordEnvelope exception
                        ? $"an unattended run is allowed under {CustodyExceptions.Describe(model, exception)}; without it: {string.Join(" ", route.Custody)}"
                        : "an unattended run is refused: " + string.Join(" ", route.Custody)).Append('\n');
            }

            foreach (Routing.Candidate candidate in route.Candidates)
            {
                text.Append("      candidate ").Append(candidate.Name).Append(" (").Append(candidate.Kind).Append("): ").Append(candidate.Available ? "available" : "not available: " + string.Join("; ", candidate.Reasons)).Append('\n');
            }
        }

        text.Append("  Asks about the goal: ").Append(routes.Asks.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (RecordEnvelope ask in routes.Asks)
        {
            RecordEnvelope latest = model.Latest(ask);
            text.Append("    ").Append(Asks.State(model, ask).State).Append(", to ").Append(Routing.NameOf(model, EstateModel.BodyString(latest, "to"))).Append(": ").Append(EstateModel.BodyString(latest, "question")).Append(" [ask ").Append(ask.Id).Append("]\n");
        }

        text.Append(routes.Next is FlowStepRoute next
            ? $"  Next: {next.Step.Name}, {next.Route!.State}; vericairn goal step {goal.Id} takes it\n"
            : "  Next: nothing goal step dispatches; every ready step is held, stopped or carried outside it\n");
        return text.ToString();
    }

    /// <summary>A run's steps as canonical JSON, in the next-eligible view's form, with the run's position.</summary>
    public static string RenderJson(EstateModel model, FlowRunRoutes routes)
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
            ["flow"] = FlowPosition.RenderJson(model, routes.Position),
            ["steps"] = new JsonArray(routes.Steps.Select(s => (JsonNode)FlowStepJson(model, s)).ToArray()),
            ["next"] = routes.Next?.Step.Name,
            ["asks"] = new JsonArray(routes.Asks.Select(a => (JsonNode)AskJson(model, a)).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static JsonObject FlowStepJson(EstateModel model, FlowStepRoute step)
    {
        Route? route = step.Route;
        RecordEnvelope? assignment = step.Step.Assignment;
        string? contributor = assignment is null ? null : EstateModel.BodyString(assignment, "contributor");
        return new JsonObject
        {
            ["key"] = step.Step.Step.Key,
            ["item"] = step.Step.Item,
            ["title"] = step.Step.Step.Title,
            ["kind"] = step.Step.Step.Kind,
            ["status"] = step.Step.State,
            ["role"] = step.Step.Step.Role,
            ["reasons"] = Strings(step.Step.Reasons),
            ["assignment"] = assignment is null ? null : new JsonObject { ["id"] = assignment.Id, ["contributor"] = contributor, ["contributorName"] = Routing.NameOf(model, contributor) },
            ["route"] = route is null ? null : new JsonObject
            {
                ["state"] = route.State,
                ["dispatches"] = step.Dispatches,
                ["reserved"] = route.Reserved,
                ["roster"] = new JsonObject { ["id"] = route.Source.Roster?.Id, ["source"] = route.Source.Description },
                ["principal"] = route.Principal,
                ["principalName"] = route.Principal is null ? null : Routing.NameOf(model, route.Principal),
                ["launcher"] = route.Launcher,
                ["reason"] = route.Reason,
                ["ask"] = route.Ask?.Id,
                ["run"] = route.Run?.Id,
                ["budget"] = new JsonObject { ["allows"] = route.BudgetAllows, ["reasons"] = Strings(route.Budget) },
                ["custody"] = route.State == "agent" ? new JsonObject
                {
                    ["allows"] = route.CustodyAllows,
                    ["refusals"] = Strings(route.Custody),
                    ["exception"] = route.Exception is RecordEnvelope exception ? new JsonObject { ["id"] = exception.Id, ["by"] = exception.By, ["expires"] = EstateModel.BodyString(exception, "expires"), ["reason"] = EstateModel.BodyString(exception, "reason") } : null,
                } : null,
                ["candidates"] = new JsonArray(route.Candidates.Select(c => (JsonNode)new JsonObject { ["principal"] = c.Principal, ["name"] = c.Name, ["kind"] = c.Kind, ["available"] = c.Available, ["launcher"] = c.Launcher, ["reasons"] = Strings(c.Reasons) }).ToArray()),
            },
        };
    }
}
