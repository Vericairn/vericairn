// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Routing by roster from this machine (the estate's design/goals.md §§ 6, 7, 8 and 11): the next-eligible view of a goal with the
/// principal each eligible step reaches, read from the records and from what this machine offers, its coding-agent launchers, the
/// device keys its stores hold, and the custody guard over those keys; the one-step operation that takes a goal's next step, the
/// smallest form of Community's conductor, one step in sequence; and the hand-over of a step an agent holds to a person.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public NextResponse? GoalNext(string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? current = GoalView.Find(model, goal);
            if (current is null)
            {
                return null;
            }

            NextEligible.GoalRoutes routes = NextEligible.Routes(model, current, Machine(model));
            using JsonDocument json = JsonDocument.Parse(NextEligible.RenderJson(model, routes));
            return new NextResponse(NextEligible.RenderText(model, routes), json.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public GoalStepResponse GoalStep(string goal, GoalStepRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By) || (request.Repository is not null && !Ulid.IsValid(request.Repository)))
        {
            return StepProblem(goal, "refused", "The recording principal and the repository are named by ULIDs; nothing was recorded.");
        }

        AuthoredRecord? routed = null;
        RunStartRequest run;
        string key;
        NextEligible.Route route;
        string goalId;
        string agentName;
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? current = GoalView.Find(model, goal);
            if (current is null)
            {
                return StepProblem(goal, "not-found", $"No goal has id {goal}.");
            }

            goalId = current.Id;
            string status = GoalRules.Status(model, current);
            if (status != "active")
            {
                return StepProblem(goalId, "refused", $"Goal {goalId} is {status}: a step is taken only for an active goal, and nothing was recorded.");
            }

            NextEligible.GoalRoutes routes = NextEligible.Routes(model, current, Machine(model));
            if (routes.Next is not NextEligible.GoalStep next)
            {
                List<string> held = routes.Eligible.Select(s => $"{EstateModel.BodyString(s.Order, "key")} is {s.Route!.State}: {s.Route.Reason}").ToList();
                return new GoalStepResponse(goalId, "none", held.Count == 0
                    ? $"Nothing to dispatch: goal {goalId} has no eligible step; what holds its planned steps is in vericairn goal next."
                    : $"Nothing to dispatch: every eligible step of goal {goalId} is held. {string.Join(" ", held)}", true);
            }

            route = next.Route!;
            key = EstateModel.BodyString(next.Order, "key") ?? next.Order.Id;
            if (route.State != "agent")
            {
                return Ask(model, current, next, key, route, request);
            }

            // An agent's step: refused before anything is recorded when the run could not start here.
            agentName = Routing.NameOf(model, route.Principal);
            var refusals = new List<string>();
            if (request.Repository is null)
            {
                refusals.Add("An agent's run is made in a workspace of a repository: name the repository resource whose clone is registered here.");
            }

            if (!route.CustodyAllows)
            {
                refusals.Add($"The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine (design/goals.md § 10): {string.Join(" ", route.Custody)} Or the goal's sponsor records a custody exception for the goal: vericairn goal custody-exception {goalId} --by <sponsor> --reason <why> --expires <time>.");
            }

            refusals.AddRange(route.Budget);
            if (refusals.Count > 0)
            {
                return StepProblem(goalId, "refused", $"{key} is routed to {agentName} through {route.Launcher}, and its run cannot start: {string.Join(" ", refusals)} Nothing was recorded.", key, route);
            }

            // Routing produces the assignment to the chosen agent when none holds the step: signed now and stored only with the run's
            // intent, after the worker's whole preflight. A step its assignment holds goes on with it.
            string carried = route.Assignment?.Id ?? string.Empty;
            if (route.Assignment is null)
            {
                string where = Latest(model, request.Repository!, "resource") is RecordEnvelope resource ? $"{EstateModel.BodyString(resource, "name") ?? resource.Id}, a work branch" : "the step's repository, a work branch";
                (routed, string? unsigned) = AuthorLocked(new RecordDraft("assignment", request.By, Element(RoutedAssignment(model, current, next.Order, route, where)), next.Order.Reference), model);
                if (routed is null)
                {
                    return StepProblem(goalId, "refused", $"Not recorded: {unsigned} Nothing was recorded.", key, route);
                }

                FindingList invalid = Invalid(LoadWith([routed]), [routed]);
                if (invalid.HasErrors)
                {
                    var problem = new Problem("rejected", $"Not recorded: the estate would be invalid with routing's assignment of {key} ({invalid.Errors} errors). Nothing was recorded.", ToFindings(invalid, "rejected").Findings);
                    return new GoalStepResponse(goalId, "none", problem.Message, false, key, route.State, route.Principal, Problem: problem);
                }

                carried = routed.Envelope.Id;
            }

            run = new RunStartRequest(carried, route.Launcher!, route.Principal!, request.By, request.Repository!, goalId, request.Branch, request.Base, request.WallMinutes, request.Settings);
        }

        // The run starts through the worker, whose preflight comes before anything is recorded and which judges the custody precondition
        // and the budget again when it records the intent, routing's assignment with it.
        RunResponse started = Start(run, routed, out RecordWritten? assignment);
        string assigned = assignment is null ? string.Empty : $"Routing assigned {key} to its agent (assignment {assignment.Id}), recorded with the run's intent. ";
        return new GoalStepResponse(goalId, started.Ok ? "run" : "none", $"{assigned}{key} is routed to the agent {agentName} [principal {route.Principal}] through {route.Launcher}: {route.Reason}. {started.Message}", started.Ok, key, route.State, route.Principal, assignment, null, started, started.Ok ? null : started.Problem);
    }

    /// <inheritdoc />
    public TakeoverResponse WorkTakeover(TakeoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string person = request.Contributor ?? request.By;
        if (!Ulid.IsValid(request.Assignment) || !Ulid.IsValid(request.By) || !Ulid.IsValid(person))
        {
            return TakeoverProblem("refused", "The assignment, the recording principal and the person taking the step over are named by ULIDs; nothing was recorded.");
        }

        RecordWritten written;
        AgentRuns.Run[] flying;
        string personName;
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? held = Latest(model, request.Assignment, "assignment");
            if (held is null)
            {
                return TakeoverProblem("not-found", $"No assignment has id {request.Assignment}.");
            }

            string? agent = EstateModel.BodyString(held, "contributor");
            string agentKind = model.CurrentPrincipal(agent) is RecordEnvelope a ? EstateModel.BodyString(a, "kind") ?? "?" : "missing";
            RecordEnvelope? order = WorkView.OrderOfAssignment(model, held) is RecordEnvelope pinned ? model.Latest(pinned) : null;
            IReadOnlyList<RecordEnvelope> assignments = order is null ? [] : Routing.Assignments(model, order);
            string orderStatus = order is null ? "?" : EstateStatus.WorkItems(model).FirstOrDefault(i => i.Id == order.Id)?.Status ?? "planned";
            personName = PrincipalName(model, person);
            string? refusal = agentKind is not ("agent" or "service") ? $"Assignment {held.Id} is {PrincipalName(model, agent)}'s, not an agent's: a hand-over takes a step over from an agent."
                : order is null ? $"Assignment {held.Id} delegates no work order of this estate."
                : orderStatus is "accepted" or "retired" ? $"Work order {EstateModel.BodyString(order, "key")} is {orderStatus}: no step of it is left to take over."
                : assignments.Count == 0 || assignments[^1].Id != held.Id ? $"Assignment {held.Id} no longer holds work order {EstateModel.BodyString(order, "key")}: assignment {(assignments.Count == 0 ? "none" : assignments[^1].Id)} does."
                : !GoalRules.IsPerson(model, person) ? $"{personName} ({person}) is not a person: a step an agent holds is taken over by a person."
                : null;
            if (refusal is not null)
            {
                return TakeoverProblem("refused", refusal + " Nothing was recorded.");
            }

            // The hand-over is the person's assignment: the agent's brief, with the agent's assignment among its sources.
            JsonObject brief = JsonNode.Parse(held.Body.GetProperty("brief").GetRawText())!.AsObject();
            JsonArray sources = brief["sources"] as JsonArray ?? [];
            sources.Add(new JsonObject { ["type"] = "assignment", ["id"] = held.Id, ["hash"] = held.ComputedHash });
            brief["sources"] = sources;
            string note = string.IsNullOrWhiteSpace(request.Note) ? $"Takes the step over from {PrincipalName(model, agent)}'s assignment {held.Id}: the hand-over to {personName}." : request.Note.Trim();
            var body = new JsonObject { ["contributor"] = person, ["brief"] = brief, ["note"] = note.Length > 4000 ? note[..3999] + "…" : note };
            WriteOutcome outcome = WriteLocked(new RecordDraft("assignment", request.By, Element(body), order!.Reference), model);
            if (!outcome.Ok)
            {
                return new TakeoverResponse("Not recorded: " + outcome.Problem!.Message, false, Problem: outcome.Problem);
            }

            written = outcome.Written!;
            IReadOnlyList<string> chain = model.Chain(held);
            flying = AgentRuns.All(model).Where(r => r.InFlight && r.Assignment is not null && chain.Contains(r.Assignment)).ToArray();
        }

        // Every run of the agent's assignment still in flight is asked to stop; each stop's facts are reported as observed.
        List<RunStopResponse> stops = flying.Select(r => RunStop(r.Id, new RunStopRequest(request.By, $"the step was taken over by {personName}"))).ToList();
        string stopped = stops.Count == 0 ? "No run of the agent's assignment was in flight." : string.Join(" ", stops.Select(s => s.Message));
        return new TakeoverResponse($"{personName} takes the step over by assignment {written.Id}, which carries the agent's brief and names its assignment {request.Assignment}. {stopped}", true, written, stops);
    }

    /// <summary>Records what a step routed to a person, or to no one, asks: the person's assignment and ask, or the sponsor's ask.</summary>
    private GoalStepResponse Ask(EstateModel model, RecordEnvelope goal, NextEligible.GoalStep step, string key, NextEligible.Route route, GoalStepRequest request)
    {
        string title = EstateModel.BodyString(step.Order, "title") ?? key;
        string goalTitle = EstateModel.BodyString(goal, "title") ?? goal.Id;
        if (route.Principal is null)
        {
            return StepProblem(goal.Id, "refused", $"{key} has no principal available and the estate names no one to ask: it has no charter operator. Nothing was recorded.", key, route);
        }

        AuthoredRecord? assignment = null;
        string question, recommendation, consequence;
        string[] options = [];
        if (route.State is "person" or "reserved")
        {
            // Routing produces the assignment to the chosen person, and the ask tells the person the step is theirs: the two are stored
            // together, or neither is.
            (assignment, string? unsigned) = AuthorLocked(new RecordDraft("assignment", request.By, Element(RoutedAssignment(model, goal, step.Order, route, "the repositories its scope names, on a work branch, named without machine paths")), step.Order.Reference), model);
            if (assignment is null)
            {
                return StepProblem(goal.Id, "refused", $"Not recorded: {unsigned} Nothing was recorded.", key, route);
            }

            string id = assignment.Envelope.Id;
            question = route.State == "reserved"
                ? $"{key} {title}, a step of goal {goalTitle}, is a reserved action, {Routing.Describe(route.Reserved!)}: will you carry it? It is assigned to you as its {route.Role} (assignment {id})."
                : $"Will you carry {key} {title}, the next step of goal {goalTitle}? It is assigned to you as its {route.Role} (assignment {id}).";
            recommendation = route.State == "reserved"
                ? $"Carry it yourself, from vericairn work brief {key}: no agent carries a reserved action without a grant in force, and you hold it. Record an attempt on assignment {id} when you start and a result when you stop."
                : $"Carry it: record an attempt on assignment {id} when the work starts and a result when it stops; vericairn work brief {key} is everything it needs.";
            consequence = route.State == "reserved"
                ? $"{key} waits until you act, and nothing reserved is done for you; the goal's other steps go on."
                : $"{key} waits with you, and no other contributor is given it; the goal's other steps go on.";
        }
        else
        {
            Routing.Candidate? first = route.Candidates.Count > 0 ? route.Candidates[0] : null;
            string because = first is { Reasons.Count: > 0 } ? first.Reasons[0] : "it can carry the step";
            question = $"No principal is available to carry {key} {title}, a step of goal {goalTitle}, as its {route.Role}{(route.Reserved is null ? string.Empty : $", a reserved action ({Routing.Describe(route.Reserved)})")}: {route.Reason}. Assign someone, or change the roster?";
            recommendation = route.Assignment is RecordEnvelope holder && first is not null
                ? $"Take the step over yourself (vericairn work takeover {holder.Id} --by <person>), or make {first.Name} available again: {because}."
                : first is null
                    ? $"Name a principal for the {route.Role} role in the roster, or in the goal's own roles, and take the step again."
                    : $"Make {first.Name} available: {because}; or name another principal for the {route.Role} role.";
            consequence = $"{key} waits; the conductor never gives it to a contributor the roster does not name, and the goal's other steps go on.";
            options = ["assign someone", "change the roster"];
        }

        var ask = new JsonObject
        {
            ["to"] = route.Principal,
            ["question"] = Clip(question, 4000),
            ["recommendation"] = Clip(recommendation, 4000),
            ["consequence"] = Clip(consequence, 2000),
            ["goal"] = goal.Id,
            ["state"] = "open",
        };
        if (options.Length > 0)
        {
            ask["options"] = new JsonArray(options.Select(o => (JsonNode)o).ToArray());
        }

        var draft = new RecordDraft("ask", request.By, Element(ask), new RecordReference("work-order", step.Order.Id, null));
        RecordWritten? assigned = null;
        RecordWritten asked;
        if (assignment is null)
        {
            WriteOutcome written = WriteLocked(draft, model);
            if (!written.Ok)
            {
                return new GoalStepResponse(goal.Id, "none", "Not recorded: " + written.Problem!.Message, false, key, route.State, route.Principal, Problem: written.Problem);
            }

            asked = written.Written!;
        }
        else
        {
            (AuthoredRecord? authored, string? unsigned) = AuthorLocked(draft, model);
            if (authored is null)
            {
                return StepProblem(goal.Id, "refused", $"Not recorded: {unsigned} Nothing was recorded.", key, route);
            }

            (IReadOnlyList<RecordWritten> stored, Problem? problem) = StoreTogetherLocked([assignment, authored]);
            if (problem is not null)
            {
                assigned = stored.Count > 0 ? stored[0] : null;
                string what = assigned is null ? "Nothing was recorded." : $"Routing's assignment {assigned.Id} was recorded and holds the step, and its ask could not be.";
                return new GoalStepResponse(goal.Id, "none", $"Not recorded: {problem.Message} {what}", false, key, route.State, route.Principal, assigned, Problem: problem);
            }

            assigned = stored[0];
            asked = stored[1];
        }

        string whom = PrincipalName(model, route.Principal);
        string said = route.State switch
        {
            "reserved" => $"{key} is a reserved action: it is assigned to {whom}, who holds it (assignment {assigned!.Id}), and asked of them (ask {asked.Id}).",
            "person" => $"{key} is routed to a person: it is assigned to {whom} (assignment {assigned!.Id}) and asked of them (ask {asked.Id}).",
            _ => $"No principal is available for {key}: {whom} is asked to assign someone or change the roster (ask {asked.Id}); it is given to no one else.",
        };
        return new GoalStepResponse(goal.Id, "ask", $"{said} {route.Reason}. Waiting answers nothing: the ask is answered by a decision of {whom}.", true, key, route.State, route.Principal, assigned, asked);
    }

    /// <summary>The assignment routing writes for the principal it chose: a brief from the work order, the goal and the route, with no machine path.</summary>
    private static JsonObject RoutedAssignment(EstateModel model, RecordEnvelope goal, RecordEnvelope order, NextEligible.Route route, string workspace)
    {
        string key = EstateModel.BodyString(order, "key") ?? order.Id;
        List<string> constraints = Strings(goal.Body, "constraints");
        if (route.Reserved is null)
        {
            constraints.Add("nothing reserved to the operator (publication, spending, credentials, DNS, destructive operations, material product changes, milestone and release acceptance) without a grant in force");
        }

        List<string> scope = Strings(order.Body, "scope");
        List<string> deliverables = Strings(order.Body, "deliverables");
        string granted = string.Join(", ", Caps.Granted(goal.Body.TryGetProperty("budget", out JsonElement budget) ? budget : default).Where(g => g.Cap is not null).Select(g => $"{g.Unit} {g.Cap}"));
        var brief = new JsonObject
        {
            ["outcome"] = Clip($"{key} {EstateModel.BodyString(order, "title")}, until its exit question is answered yes: {EstateModel.BodyString(order, "exitQuestion")}", 4000),
            ["sources"] = new JsonArray(
                new JsonObject { ["type"] = "work-order", ["id"] = order.Id, ["hash"] = order.ComputedHash },
                new JsonObject { ["type"] = "goal", ["id"] = goal.Id, ["hash"] = goal.ComputedHash }),
            ["permittedChanges"] = new JsonArray(scope.Select(s => (JsonNode)Clip(s, 2000)).ToArray()),
            ["prohibitedEffects"] = new JsonArray(constraints.Select(c => (JsonNode)Clip(c, 2000)).ToArray()),
            ["workspace"] = new JsonArray(Clip(workspace, 500)),
            ["limits"] = new JsonArray(Clip($"within the budget goal {goal.Id} grants: {granted}", 2000)),
            ["expectedOutputs"] = new JsonArray(deliverables.Select(d => (JsonNode)Clip(d, 2000)).ToArray()),
            ["reviewCriteria"] = new JsonArray(Clip($"The exit question is answered yes: {EstateModel.BodyString(order, "exitQuestion")}", 2000)),
            ["recovery"] = $"Record an attempt when the work starts and a result when it stops; whoever continues starts from vericairn work brief {key}, never from a conversation.",
        };
        return new JsonObject { ["contributor"] = route.Principal, ["brief"] = brief, ["note"] = Clip($"Routed by roster ({route.Role}, from {route.Source.Description}): {route.Reason}.", 4000) };
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static GoalStepResponse StepProblem(string goal, string code, string message, string? key = null, NextEligible.Route? route = null) =>
        new(goal, "none", message, false, key, route?.State, route?.Principal, Problem: new Problem(code, message, []));

    private static TakeoverResponse TakeoverProblem(string code, string message) => new(message, false, Problem: new Problem(code, message, []));

    /// <summary>
    /// What this machine offers routing now: the launchers its registry holds with the harness each starts, the device keys its stores
    /// hold, and the custody guard for an unattended run of a goal by an agent as the operating-system user this runtime runs as.
    /// </summary>
    private Routing.Machine Machine(EstateModel model)
    {
        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        return new Routing.Machine(
            Providers.Launchers.Select(l => (l.Launcher, l.Harness)).ToList(),
            identity => held.TryGetValue(identity, out bool known) ? known : held[identity] = stores.Any(s => s.Has(identity)),
            (goal, agent) => CustodyGuard.Refusals(model, CustodyGuard.Guarded(model, goal, agent), stores, Environment.UserName),
            DateTimeOffset.UtcNow);
    }
}
