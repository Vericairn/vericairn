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
/// A run's steps routed from this machine (the estate's design/flow.md §§ 6 to 8): its position from the records, and for each work or
/// review step that is ready, the principal its role reaches through the goal's roles, its roster or the estate's, exactly as a work
/// order's step is routed, with the assignments and asks of that step alone. A step of a role the flow marks person is never routed to
/// an agent; a reserved step is never routed; a stale, reopened or rerun step is routed afresh, so its new brief pins its inputs as they
/// are now. A review before hand-off is a routed step too: dispatched to its reviewer role, never to an author of the work it reviews
/// unless the review is self. Required attention, a step's or the whole run's, is routed to the sponsor as an ask; the run's own
/// attention holds every dispatch until the sponsor answers.
/// </summary>
public static partial class NextEligible
{
    /// <summary>
    /// The route states the one-step operation acts on for a run: an agent's run, a person's assignment and ask, the sponsor's ask for
    /// required attention, or the operator request of a reserved act.
    /// </summary>
    public static readonly ImmutableArray<string> FlowDispatching = ["agent", "person", "reserved", "attention", "request"];

    /// <summary>One step instance of a run, with its route from this machine when it is a ready work or review step, a review before hand-off, or required attention.</summary>
    /// <param name="Step">The step instance and its derived state.</param>
    /// <param name="Route">Its route, or null for a step that is not routed now.</param>
    /// <param name="Review">For a review before hand-off of the step's items, the review routed; null for the step itself.</param>
    public sealed record FlowStepRoute(FlowPosition.StepInstance Step, Route? Route, FlowPosition.PendingReview? Review = null)
    {
        /// <summary>True when the one-step operation acts on it.</summary>
        public bool Dispatches => Route is not null && FlowDispatching.IndexOf(Route.State) >= 0;

        /// <summary>The step, or its review, in words.</summary>
        public string Name => Review is null ? Step.Name : Step.Name + " review";

        /// <summary>The required attention it routes to the sponsor, when its route is that ask.</summary>
        public FlowPosition.Attention? Attention => Route?.State is "attention" or "asked" or "answered" && Review is null ? Step.Attention : null;
    }

    /// <summary>The required attention a whole run raises, routed to the sponsor as an ask.</summary>
    /// <param name="Attention">The attention.</param>
    /// <param name="Route">Its route: attention (to ask), asked (its ask waits) or answered.</param>
    public sealed record AttentionRoute(FlowPosition.Attention Attention, Route Route);

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
        /// <summary>The required attention the whole run raises, each with its route to the sponsor.</summary>
        public IReadOnlyList<AttentionRoute> Attention { get; init; } = [];

        /// <summary>True while the run's own attention holds every dispatch: it is still to be asked, or its ask waits.</summary>
        public bool Held => Attention.Any(a => a.Route.State is "attention" or "asked");

        /// <summary>The run's attention the one-step operation asks the sponsor about first; null when none is still to be asked.</summary>
        public AttentionRoute? AttentionNext => Attention.FirstOrDefault(a => a.Route.State == "attention");

        /// <summary>The step the one-step operation takes: the first, in the flow's order, whose route dispatches; null when none does or the run is held.</summary>
        public FlowStepRoute? Next => Held ? null : Steps.FirstOrDefault(s => s.Dispatches);
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
        foreach (FlowPosition.StepInstance instance in position.Steps)
        {
            // Required attention of a held step is routed to the sponsor as an ask, never to another contributor.
            if (position.Work is not null && instance.Attention is FlowPosition.Attention attention && instance.Step.IsRouted)
            {
                steps.Add(new FlowStepRoute(instance, AttentionOf(model, current, status, instance.Step.Role, attention, machine)));
                continue;
            }

            // A reserved act is never routed to a contributor: it waits on the operator as an operator request carrying its exact command,
            // computed only now that the step is next (design/flow.md § 8; design/release-procedure.md § 6).
            if (position.Work is not null && position.Flow is not null && instance.State == "stopped" && instance.Stop == "reserved")
            {
                steps.Add(new FlowStepRoute(instance, RequestRoute(model, current, status, instance, position.Stops.FirstOrDefault(s => s.Kind == "reserved" && s.Step == instance.Ref))));
                continue;
            }

            // A review before hand-off is routed to its reviewer role, from its own assignments and asks.
            if (position.Work is not null && position.Flow is not null && instance.State == "in-review" && instance.Reviews.Count > 0)
            {
                foreach (FlowPosition.PendingReview review in instance.Reviews)
                {
                    steps.Add(new FlowStepRoute(instance, ReviewRoute(model, current, status, ledger, position.Flow, instance, review, runs, goals, machine), review));
                }

                continue;
            }

            if (position.Work is null || position.Flow is null || !instance.Step.IsRouted || instance.Step.Reserved is not null || instance.Waiting
                || instance.State is not ("next" or "stopped" or "stale" or "in-review"))
            {
                steps.Add(new FlowStepRoute(instance, null));
                continue;
            }

            IReadOnlyList<RecordEnvelope> assignments = Routing.Assignments(model, position.Work, instance.Ref);
            var held = assignments.SelectMany(a => model.Chain(a)).ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<RecordEnvelope> asks = AsksNaming(model, goals, held);

            // A stale, reopened or rerun step reruns from a fresh assignment whose brief pins its inputs as they are now, once no run of
            // it is in flight; the earlier assignments, attempts and results stay recorded.
            if (instance.Rerun && !runs.Any(r => r.Assignment is not null && held.Contains(r.Assignment)))
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

        // At most three stops reach one person at a time, across every run (design/cohesion-contract.md § 5): a route whose stop the
        // delivery across runs queued waits queued, one it lets through goes on, and a route to a person this machine reaches that no stop
        // foresaw counts against every stop already reaching that person.
        var reaching = new Dictionary<string, int>(position.Reaching, StringComparer.Ordinal);
        Dictionary<FlowRuns.StepRef, FlowStops.RunStop> foreseen = position.Stops.Where(s => s.Step is not null).GroupBy(s => s.Step!).ToDictionary(g => g.Key, g => g.First());
        for (int index = 0; index < steps.Count; index++)
        {
            if (steps[index] is not { Dispatches: true, Route: { State: not "agent", Principal: string principal } route } step)
            {
                continue;
            }

            FlowRuns.StepRef key = step.Review is null ? step.Step.Ref : step.Step.Ref with { Review = true };
            bool queued;
            if (foreseen.TryGetValue(key, out FlowStops.RunStop? stop) && stop.Principal == principal && stop.Delivery is "due" or "queued")
            {
                queued = stop.Delivery == "queued";
            }
            else
            {
                queued = reaching.GetValueOrDefault(principal) >= FlowStops.PerDelivery;
                if (!queued)
                {
                    reaching[principal] = reaching.GetValueOrDefault(principal) + 1;
                }
            }

            if (queued)
            {
                steps[index] = steps[index] with { Route = route with { State = "queued", Reason = $"{route.Reason}; {FlowStops.PerDelivery.ToString(System.Globalization.CultureInfo.InvariantCulture)} stops of the estate's runs already reach {Routing.NameOf(model, principal)}, at most {FlowStops.PerDelivery.ToString(System.Globalization.CultureInfo.InvariantCulture)} at a time, so it waits queued and is delivered in order as one settles" } };
            }
        }

        List<AttentionRoute> attentions = position.RunAttention.Select(a => new AttentionRoute(a, AttentionOf(model, current, status, "sponsor", a, machine))).ToList();
        return new FlowRunRoutes(current, status, ledger, fenced, position, steps, Routing.AsksOf(model, current)) { Attention = attentions };
    }

    /// <summary>
    /// The route of a run's reserved act (design/flow.md § 8): an operator request put to the operator with the step's exact command,
    /// recorded by the one-step operation (request); waiting on the operator once recorded (requested); not made again once a request
    /// carrying that command was closed (closed); queued behind three stops already reaching the operator; and unavailable when no
    /// person with the operator role is named to put it to. No contributor is a candidate: a reserved act is never dispatched.
    /// </summary>
    private static Route RequestRoute(EstateModel model, RecordEnvelope goal, string status, FlowPosition.StepInstance instance, FlowStops.RunStop? stop)
    {
        IReadOnlyList<string> fence = status == "active" ? [] : [$"The goal is {status}: nothing of it is dispatched until it is active."];
        var source = new Routing.Source(null, "the operator, who holds every reserved act in this build");
        string act = instance.Step.Reserved!;
        string? principal = stop?.Principal ?? FlowStops.RequestTo(model, goal, instance.Step);
        string name = principal is null ? "the operator" : Routing.NameOf(model, principal);
        (string state, string reason) = stop?.Delivery switch
        {
            "delivered" => ("requested", $"a reserved act, {act}: operator request {stop.Record!.Id} waits on {name} with the step's exact command; no agent carries it, and the step is done once the operator's act is recorded"),
            "closed" => ("closed", $"a reserved act, {act}: operator request {stop.Record!.Id} carrying this command was closed, and it is not made again while the command holds; the step is done once the operator's act is recorded"),
            "queued" => ("request", $"a reserved act, {act}: an operator request with its exact command is recorded for {name}, who holds it; no agent carries it, and nothing is dispatched"),
            "outside" => ("unavailable", $"a reserved act, {act}: the estate names no person with the operator role to put its request to"),
            _ when principal is null => ("unavailable", $"a reserved act, {act}: the estate names no person with the operator role to put its request to"),
            _ => ("request", $"a reserved act, {act}: an operator request with its exact command is recorded for {name}, who holds it; no agent carries it, and nothing is dispatched"),
        };
        return new Route(instance.Step.Role, act, source, [], state, principal, null, null, null, null, fence, [], reason, Request: stop?.Record);
    }

    /// <summary>The current asks of a goal that name one of a step's assignments, oldest first.</summary>
    private static List<RecordEnvelope> AsksNaming(EstateModel model, IReadOnlyList<string> goals, HashSet<string> held) =>
        model.Current("ask")
            .Where(a => EstateModel.BodyString(a, "goal") is string g && goals.Contains(g) && EstateModel.BodyString(a, "assignment") is string named && held.Contains(named))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The route of a review before hand-off (design/flow.md § 6): to the principals of its reviewer role, or for a self review to the
    /// step's authors, each admitted only when it meets the review's independence against the step's authors; from the review
    /// assignments of this exact subject and these items, and the asks that name them.
    /// </summary>
    private static Route ReviewRoute(EstateModel model, RecordEnvelope goal, string status, AgentRuns.Ledger ledger, FlowDefinition flow, FlowPosition.StepInstance instance,
        FlowPosition.PendingReview review, List<AgentRuns.Run> runs, IReadOnlyList<string> goals, Routing.Machine machine)
    {
        var held = review.Assignments.SelectMany(a => model.Chain(a)).ToHashSet(StringComparer.Ordinal);
        (IReadOnlyList<string> Principals, Routing.Source Source)? named = review.Requirement.Independence == "self"
            ? (review.Authors.Where(a => instance.Assignment is null || EstateModel.BodyString(instance.Assignment, "contributor") == a).ToList(), new Routing.Source(null, "the step's own holder, for a self review"))
            : null;
        return RouteOf(model, goal, status, ledger, review.Role, null, review.Assignments, AsksNaming(model, goals, held), runs, machine, candidate =>
            FlowRuns.ReviewerRefusals(model, flow, review.Requirement, review.Role, candidate.Principal, review.Authors) is { Count: > 0 } refusals
                ? candidate with { Available = false, Reasons = [.. candidate.Reasons, .. refusals] }
                : candidate, named);
    }

    /// <summary>
    /// The route of required attention (design/flow.md §§ 7 and 8): an ask to who decides for the goal, the sponsor, about the record
    /// that raised it; asked while an ask about that record waits, and answered once one is answered, so it is asked once.
    /// </summary>
    private static Route AttentionOf(EstateModel model, RecordEnvelope goal, string status, string role, FlowPosition.Attention attention, Routing.Machine machine)
    {
        string? sponsor = Routing.Asked(model, goal, null, machine.Now);
        IReadOnlyList<string> trigger = model.Chain(attention.Trigger);
        List<RecordEnvelope> asks = Routing.AsksOf(model, goal).Where(a => model.Latest(a).Subject is RecordReference subject && trigger.Contains(subject.Id)).ToList();
        IReadOnlyList<string> fence = status == "active" ? [] : [$"The goal is {status}: nothing of it is dispatched until it is active."];
        var source = new Routing.Source(null, "who decides for the goal, its sponsor");
        RecordEnvelope? waiting = asks.LastOrDefault(a => Asks.IsWaiting(Asks.State(model, a).State));
        if (waiting is not null)
        {
            return new Route(role, null, source, [], "asked", EstateModel.BodyString(model.Latest(waiting), "to"), null, null, waiting, null, fence, [], $"required attention ({attention.Kind}): {attention.Summary}; ask {waiting.Id} about it waits for an answer");
        }

        if (asks.LastOrDefault(a => Asks.State(model, a).State == "answered") is RecordEnvelope answered)
        {
            return new Route(role, null, source, [], "answered", EstateModel.BodyString(model.Latest(answered), "to"), null, null, answered, null, fence, [], $"required attention ({attention.Kind}): {attention.Summary}; ask {answered.Id} about it was answered, and it is not asked again");
        }

        return new Route(role, null, source, [], "attention", sponsor, null, null, asks.LastOrDefault(), null, fence, [], $"required attention ({attention.Kind}): {attention.Summary}; {Routing.NameOf(model, sponsor)} is asked");
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

        foreach (AttentionRoute attention in routes.Attention)
        {
            text.Append("  The run's required attention (").Append(attention.Attention.Kind).Append("): ").Append(attention.Route.State).Append(", ").Append(attention.Route.Reason).Append('\n');
        }

        List<FlowStepRoute> routed = routes.Steps.Where(s => s.Route is not null).ToList();
        text.Append("  Routed: ").Append(routed.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (FlowStepRoute step in routed)
        {
            Route route = step.Route!;
            text.Append("  ").Append(step.Name).Append("  ").Append(step.Review is null ? step.Step.Step.Title : $"review of {step.Review.What} at {step.Review.Hash} ({step.Review.Requirement.Independence})").Append(" [").Append(step.Step.State).Append("]\n");
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

        text.Append(routes.AttentionNext is AttentionRoute first
            ? $"  Next: the run's required attention ({first.Attention.Kind}), asked of the sponsor; vericairn goal step {goal.Id} asks it\n"
            : routes.Held
                ? "  Next: nothing; the run's required attention holds every dispatch until the sponsor answers\n"
                : routes.Next is FlowStepRoute next
                    ? $"  Next: {next.Name}, {next.Route!.State}; vericairn goal step {goal.Id} takes it\n"
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
            ["attention"] = new JsonArray(routes.Attention.Select(a => (JsonNode)new JsonObject
            {
                ["kind"] = a.Attention.Kind, ["summary"] = a.Attention.Summary,
                ["trigger"] = new JsonObject { ["type"] = a.Attention.Trigger.Type, ["id"] = a.Attention.Trigger.Id, ["hash"] = a.Attention.Trigger.ComputedHash },
                ["state"] = a.Route.State, ["principal"] = a.Route.Principal, ["ask"] = a.Route.Ask?.Id, ["reason"] = a.Route.Reason,
            }).ToArray()),
            ["held"] = routes.Held,
            ["next"] = routes.Next?.Name,
            ["asks"] = new JsonArray(routes.Asks.Select(a => (JsonNode)AskJson(model, a)).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static JsonObject FlowStepJson(EstateModel model, FlowStepRoute step)
    {
        Route? route = step.Route;
        RecordEnvelope? assignment = step.Review is null ? step.Step.Assignment : step.Review.Assignments.Count == 0 ? null : step.Review.Assignments[^1];
        string? contributor = assignment is null ? null : EstateModel.BodyString(assignment, "contributor");
        return new JsonObject
        {
            ["key"] = step.Step.Step.Key,
            ["instance"] = step.Step.Instance,
            ["title"] = step.Step.Step.Title,
            ["kind"] = step.Step.Step.Kind,
            ["status"] = step.Step.State,
            ["role"] = step.Step.Step.Role,
            ["reasons"] = Strings(step.Step.Reasons),
            ["review"] = step.Review is null ? null : new JsonObject
            {
                ["items"] = new JsonArray(step.Review.Items.Select(i => (JsonNode)i).ToArray()),
                ["independence"] = step.Review.Requirement.Independence,
                ["role"] = step.Review.Role,
                ["subject"] = new JsonObject { ["type"] = step.Review.Subject.Type, ["id"] = step.Review.Subject.Id, ["hash"] = step.Review.Hash },
            },
            ["attention"] = step.Attention is null ? null : new JsonObject { ["kind"] = step.Attention.Kind, ["summary"] = step.Attention.Summary, ["trigger"] = new JsonObject { ["type"] = step.Attention.Trigger.Type, ["id"] = step.Attention.Trigger.Id, ["hash"] = step.Attention.Trigger.ComputedHash } },
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
                ["request"] = route.Request?.Id,
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
