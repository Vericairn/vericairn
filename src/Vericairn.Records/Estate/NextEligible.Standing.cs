// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// A standing agent's occurrences routed from this machine (from 0.9.4, GOAL-07; the estate's design/standing-agents.md § 7): each due
/// occurrence routed by the role of the flow's work step through the goal's roles, its roster or the estate's, exactly as a step is, from
/// the assignments that hold its key and the asks that name them; and the failure pause routed to the sponsor as an ask. Read-only.
/// </summary>
public static partial class NextEligible
{
    /// <summary>An occurrence with its route, or null for one that is not routed now.</summary>
    /// <param name="Occurrence">The occurrence and its state.</param>
    /// <param name="Route">Its route from this machine.</param>
    internal sealed record OccurrenceRoute(StandingAgents.OccurrenceState Occurrence, Route? Route)
    {
        /// <summary>True when the one-step operation acts on it.</summary>
        public bool Dispatches => Route is { Dispatches: true };
    }

    /// <summary>A standing agent's occurrences routed from this machine.</summary>
    /// <param name="Agent">The agent as of the time it was evaluated at.</param>
    /// <param name="Ledger">Its ledger.</param>
    /// <param name="Occurrences">Each due occurrence, oldest first, with its route.</param>
    /// <param name="Failure">The failure pause routed to the sponsor, while it holds.</param>
    internal sealed record StandingRoutes(StandingAgents.Agent Agent, AgentRuns.Ledger Ledger, IReadOnlyList<OccurrenceRoute> Occurrences, AttentionRoute? Failure);

    /// <summary>A standing agent's due occurrences routed from this machine, and its failure pause; nothing is recorded or started.</summary>
    internal static StandingRoutes Standing(EstateModel model, StandingAgents.Agent agent, Routing.Machine machine)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(machine);
        RecordEnvelope current = agent.Goal;
        string status = agent.Status;
        AgentRuns.Ledger ledger = AgentRuns.Of(model, current);
        AttentionRoute? failure = null;
        // From FIX-156 an agent held only by a record it cannot read asks nothing of the sponsor about failures.
        if (agent.State == "held" && agent.Failures >= agent.FailureLimit && agent.FailureTrigger is RecordEnvelope failed)
        {
            var attention = new FlowPosition.Attention("consecutive-failures", failed, $"the standing agent's runs failed {agent.Failures} times in a row, the limit of {agent.FailureLimit} its goal sets: nothing more of it is dispatched until the sponsor answers");
            failure = new AttentionRoute(attention, AttentionOf(model, current, status, "sponsor", attention, machine, attentionOnly: true));
        }

        var routes = new List<OccurrenceRoute>();
        if (agent.Flow is not FlowDefinition flow || agent.Step is not FlowStep step || agent.Order is not RecordEnvelope order)
        {
            return new StandingRoutes(agent, ledger, agent.Due.Select(o => new OccurrenceRoute(o, null)).ToList(), failure);
        }

        List<AgentRuns.Run> runs = AgentRuns.All(model).Where(r => r.InFlight).ToList();
        IReadOnlyList<string> goals = model.Chain(current);
        IReadOnlyList<string> orders = model.Chain(model.Latest(order));
        bool person = flow.Role(step.Role)?.Holder == "person";
        foreach (StandingAgents.OccurrenceState occurrence in agent.Due)
        {
            // From 0.9.4 (ID-11) the occurrence's holder is the holder rule's; its history, every assignment of its key, names the asks.
            IReadOnlyList<RecordEnvelope> history = Routing.Assignments(model, order).Where(a => StandingAgents.KeyOf(a) == occurrence.Occurrence.Key).ToList();
            IReadOnlyList<RecordEnvelope> assignments = Routing.Holding(model, order).Where(a => StandingAgents.KeyOf(a) == occurrence.Occurrence.Key).ToList();
            var held = history.SelectMany(a => model.Chain(a)).ToHashSet(StringComparer.Ordinal);
            // The asks that name the occurrence's assignments, and those about the work order that name none, which no principal being
            // available raised for every occurrence alike.
            List<RecordEnvelope> asks = model.Current("ask")
                .Where(a => EstateModel.BodyString(a, "goal") is string g && goals.Contains(g)
                    && (EstateModel.BodyString(a, "assignment") is string named ? held.Contains(named) : a.Subject is not null && orders.Contains(a.Subject.Id)))
                .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
            // A due occurrence whose latest result is blocked or abandoned goes on with the assignment that holds it.
            bool goesOn = occurrence.Result is RecordEnvelope latest && EstateModel.BodyString(latest, "outcome") is "blocked" or "abandoned";
            Route route = RouteOf(model, current, status, ledger, step.Role, step.Reserved ?? Routing.ReservedAction(step.Role), assignments, asks, runs, machine, candidate => person && candidate.IsAgent
                ? candidate with { Available = false, Reasons = [.. candidate.Reasons, $"the flow marks the {step.Role} role a person's, and an agent never holds it"] }
                : candidate, resultGoesOn: goesOn);
            routes.Add(new OccurrenceRoute(occurrence, route));
        }

        return new StandingRoutes(agent, ledger, routes, failure);
    }

    /// <summary>A standing agent's occurrences routed from this machine, as text: its state, its period, each occurrence with its route.</summary>
    internal static string RenderText(EstateModel model, StandingRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        StandingAgents.Agent agent = routes.Agent;
        var text = new StringBuilder();
        text.Append("Next occurrences of standing agent ").Append(EstateModel.BodyString(agent.Goal, "title")).Append(" [goal ").Append(agent.Goal.Id).Append("], ").Append(agent.State).Append(", routed from this machine as of ").Append(RecordParser.FormatTimestamp(agent.AsOf)).Append('\n');
        text.Append("  ").Append(agent.Reason).Append('\n');
        if (routes.Failure is AttentionRoute failure)
        {
            text.Append("  Failure pause: ").Append(failure.Route.State).Append(": ").Append(failure.Route.Reason).Append('\n');
        }

        foreach (StandingAgents.OccurrenceState occurrence in agent.Occurrences)
        {
            text.Append("  ").Append(occurrence.Occurrence.Key).Append("  ").Append(occurrence.State).Append(": ").Append(occurrence.Reason).Append('\n');
            if (routes.Occurrences.FirstOrDefault(o => o.Occurrence.Occurrence.Key == occurrence.Occurrence.Key)?.Route is Route route)
            {
                text.Append("    ").Append(route.Role).Append(": ").Append(route.State).Append(route.Principal is null ? string.Empty : $", {Routing.NameOf(model, route.Principal)} [principal {route.Principal}]")
                    .Append(route.Launcher is null ? string.Empty : $" through {route.Launcher}").Append("; ").Append(route.Reason).Append('\n');
            }
        }

        OccurrenceRoute? next = NextOccurrence(routes);
        string? refusal = NextPathRefusal(model, agent, next);
        if (refusal is not null)
        {
            text.Append("  Next: occurrence ").Append(next!.Occurrence.Occurrence.Key).Append('.').Append(' ').Append(StandingView.PathRefusalLine(refusal)).Append('\n');
        }
        else
        {
            if (StandingView.PathRefusal(model, agent) is { } warning)
            {
                text.Append("  ").Append(StandingView.PathRefusalLine(warning)).Append('\n');
            }

            text.Append(next is null
                ? "  Next: nothing to dispatch now; vericairn goal due lists each occurrence\n"
                : $"  Next: occurrence {next.Occurrence.Occurrence.Key}, {next.Route!.State}; vericairn goal step {agent.Goal.Id} carries it\n");
        }
        return text.ToString();
    }

    /// <summary>A standing agent's occurrences routed from this machine, as the next-eligible view's JSON (vericairn.next/1), each occurrence a step.</summary>
    internal static string RenderJson(EstateModel model, StandingRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        StandingAgents.Agent agent = routes.Agent;
        AgentRuns.Ledger ledger = routes.Ledger;
        OccurrenceRoute? next = NextOccurrence(routes);
        var json = new JsonObject
        {
            ["format"] = "vericairn.next/1",
            ["goal"] = new JsonObject { ["id"] = agent.Goal.Id, ["hash"] = agent.Goal.ComputedHash, ["title"] = EstateModel.BodyString(agent.Goal, "title"), ["status"] = agent.Status, ["sponsor"] = EstateModel.BodyString(agent.Goal, "sponsor") },
            ["budget"] = new JsonObject
            {
                ["units"] = new JsonArray(AgentRuns.ChargedUnits.Where(ledger.Budget.ContainsKey).Select(u => (JsonNode)new JsonObject { ["unit"] = u, ["budget"] = ledger.Budget[u], ["spent"] = ledger.Spent.GetValueOrDefault(u), ["remaining"] = ledger.Remaining(u), ["currency"] = u == "currency" ? ledger.Currency : null }).ToArray()),
                ["attemptsPerStep"] = ledger.AttemptsPerStep,
                ["concurrency"] = ledger.Concurrency,
                ["inFlight"] = ledger.InFlight,
                ["fenced"] = new JsonArray(),
            },
            ["standing"] = StandingView.Summary(model, agent, NextPathRefusal(model, agent, next)),
            ["steps"] = new JsonArray(agent.Occurrences.Select(o =>
            {
                string? contributor = o.Assignment is null ? null : EstateModel.BodyString(o.Assignment, "contributor");
                return (JsonNode)new JsonObject
                {
                    ["key"] = o.Occurrence.Key,
                    ["id"] = agent.Order?.Id,
                    ["title"] = o.Occurrence.Summary,
                    ["kind"] = o.Occurrence.Kind,
                    ["status"] = o.State,
                    ["role"] = agent.Step?.Role,
                    ["eligible"] = o.State == "due",
                    ["holds"] = null,
                    ["assignment"] = o.Assignment is null ? null : new JsonObject { ["id"] = o.Assignment.Id, ["contributor"] = contributor, ["contributorName"] = Routing.NameOf(model, contributor) },
                    ["route"] = RouteJson(model, routes.Occurrences.FirstOrDefault(r => r.Occurrence.Occurrence.Key == o.Occurrence.Key)?.Route),
                };
            }).ToArray()),
            ["next"] = next?.Occurrence.Occurrence.Key,
            ["asks"] = new JsonArray(Routing.AsksOf(model, agent.Goal).Select(a => (JsonNode)AskJson(model, a)).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static OccurrenceRoute? NextOccurrence(StandingRoutes routes) =>
        routes.Failure is null ? routes.Occurrences.FirstOrDefault(o => o.Dispatches) : null;

    // Inspect the same agent route that goal step would take, including an assignment already holding the occurrence.
    private static string? NextPathRefusal(EstateModel model, StandingAgents.Agent agent, OccurrenceRoute? next)
    {
        if (next?.Route is not { State: "agent" } route || agent.Step is not { } step || agent.Order is not { } order)
        {
            return null;
        }

        IReadOnlyList<string> paths = FlowPermissions.For(model, agent.Goal, order, step);
        if (route.Assignment is { } assignment && FlowPermissions.Carried(paths, assignment) is null)
        {
            return FlowPermissions.HeldMessage(assignment, step.Key);
        }

        return paths.Count == 0 ? FlowPermissions.Message(step.Key) : null;
    }
}
