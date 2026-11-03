// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// One step of a run per command (the estate's design/flow.md §§ 6 and 10): routing's assignment of a flow's work or review step names
/// the step and item, and its brief pins each input the step takes by type, id and hash, so a later change to an upstream output shows
/// the step stale. The brief carries the step's role brief, its hand-off contract, its recovery and the flow's never lines.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>The assignment routing writes for the step it takes: a work order's, or a run's flow step's.</summary>
    private static JsonObject Routed(EstateModel model, RecordEnvelope goal, Taken taken, string workspace, string routedWith) =>
        taken.Step is null ? RoutedAssignment(model, goal, taken.Order, taken.Route, workspace, routedWith) : RoutedFlowAssignment(model, goal, taken, workspace, routedWith);

    /// <summary>
    /// The assignment of a run's step: of the run's work order, naming the step and item and pinning its inputs; its brief from the
    /// step, its role and the flow, with the goal's constraints, the reserved effects and the flow's never lines among what it prohibits.
    /// </summary>
    private static JsonObject RoutedFlowAssignment(EstateModel model, RecordEnvelope goal, Taken taken, string workspace, string routedWith)
    {
        FlowPosition.Instance instance = taken.Step!;
        FlowDefinition flow = taken.Flow!.Flow!;
        FlowStep step = instance.Step;
        NextEligible.Route route = taken.Route;
        RecordEnvelope order = taken.Order;
        string orderKey = EstateModel.BodyString(order, "key") ?? order.Id;
        List<string> constraints = Strings(goal.Body, "constraints");
        constraints.Add(ReservedEffects);
        if (route.Reserved is string action && route.Principal is string carrier && !model.HasRole(carrier, Roles.Operator) && Routing.Grant(model, carrier, action, DateTimeOffset.UtcNow) is RecordEnvelope grant)
        {
            string until = EstateModel.BodyString(grant, "expires") is string expires ? $" until {expires}" : string.Empty;
            constraints.Add($"no {action} except under grant {grant.Id}, which gives it to {Routing.NameOf(model, carrier)}{until}");
        }

        constraints.AddRange(flow.Never);
        var sources = new JsonArray(
            new JsonObject { ["type"] = "work-order", ["id"] = order.Id, ["hash"] = order.ComputedHash },
            new JsonObject { ["type"] = "goal", ["id"] = goal.Id, ["hash"] = goal.ComputedHash });
        var pins = new JsonArray();
        var cited = new HashSet<string>(StringComparer.Ordinal) { order.Id, goal.Id };
        foreach (FlowPosition.Input input in instance.Inputs.Where(i => i.Carrier is not null))
        {
            RecordEnvelope pinned = input.Carrier!;
            pins.Add(new JsonObject { ["name"] = input.Name, ["type"] = pinned.Type, ["id"] = pinned.Id, ["hash"] = pinned.ComputedHash });
            if (cited.Add(pinned.Id))
            {
                sources.Add(new JsonObject { ["type"] = pinned.Type, ["id"] = pinned.Id, ["hash"] = pinned.ComputedHash });
            }
        }

        string role = flow.Role(step.Role)?.Brief is string brief ? $" The {step.Role} role's brief: {brief}" : string.Empty;
        List<string> outputs = step.Outputs.Select(o => o.Type switch
        {
            "file" => $"{o.Name}: a file named {o.Name} among the result's outputs, by hash{(o.MediaType is null ? string.Empty : ", " + o.MediaType)}",
            "review" => $"{o.Name}: the review recorded of the reviewed result",
            _ => $"{o.Name}: the newest {o.Counts} record{(o.Kind is null ? string.Empty : $" of kind {o.Kind}")} the step's done predicates count",
        }).ToList();
        if (outputs.Count == 0)
        {
            outputs.AddRange(Strings(order.Body, "deliverables"));
        }

        var criteria = new List<string>();
        if (step.Kind == "review")
        {
            FlowInput reviewed = step.Inputs.First(i => i.Step is not null);
            FlowPosition.Instance? producer = taken.Flow.Find(new FlowRuns.StepRef(reviewed.Step!, flow.Step(reviewed.Step)?.Each is null ? null : instance.Item));
            criteria.Add(producer?.Result is RecordEnvelope result
                ? $"Review result {result.Id} at {result.ComputedHash}, of step {producer.Name}: record a review of it with outcome pass, findings or fail; a review by its author does not count."
                : $"Review the result of step {reviewed.Step}: record a review of it with outcome pass, findings or fail; a review by its author does not count.");
        }
        else
        {
            criteria.Add(step.Outputs.Count == 0 ? $"The step is complete: {step.Title}." : $"Each declared output exists at an exact hash: {string.Join(", ", step.Outputs.Select(o => o.Name))}.");
            criteria.AddRange(instance.Done.Select(d => "Its done predicate holds: " + GoalPredicates.DescribePredicate(d.Predicate) + "."));
            if (step.Review is not null and not "none")
            {
                criteria.Add($"Its output is handed on only after {(step.Review == "person" ? "a person's" : "an independent")} review by a principal other than its author.");
            }
        }

        string granted = string.Join(", ", Caps.Granted(goal.Body.TryGetProperty("budget", out JsonElement budget) ? budget : default).Where(g => g.Cap is not null).Select(g => $"{g.Unit} {g.Cap}"));
        var body = new JsonObject
        {
            ["outcome"] = Clip($"{instance.Name} {step.Title}: the {step.Kind} step of flow {flow.Pin.Id} {flow.Pin.Version} that goal {EstateModel.BodyString(goal, "title")} runs, carried as its {step.Role}.{(step.Action is null ? string.Empty : " " + step.Action)}{role}", 4000),
            ["sources"] = sources,
            ["permittedChanges"] = new JsonArray(Strings(order.Body, "scope").Select(s => (JsonNode)Clip(s, 2000)).ToArray()),
            ["prohibitedEffects"] = new JsonArray(constraints.Select(c => (JsonNode)Clip(c, 2000)).ToArray()),
            ["workspace"] = new JsonArray(Clip(workspace, 500)),
            ["limits"] = new JsonArray(Clip($"within the budget goal {goal.Id} grants: {granted}", 2000)),
            ["expectedOutputs"] = new JsonArray(outputs.Select(o => (JsonNode)Clip(o, 2000)).ToArray()),
            ["reviewCriteria"] = new JsonArray(criteria.Select(c => (JsonNode)Clip(c, 2000)).ToArray()),
            ["recovery"] = Clip($"{step.Recovery} Record an attempt when the work starts and a result when it stops; whoever continues starts from this assignment's brief and vericairn work brief {orderKey}, never from a conversation.", 4000),
        };
        var named = new JsonObject { ["key"] = step.Key };
        if (instance.Item is not null)
        {
            named["item"] = instance.Item;
        }

        if (pins.Count > 0)
        {
            named["inputs"] = pins;
        }

        return new JsonObject
        {
            ["contributor"] = route.Principal,
            ["brief"] = body,
            ["note"] = Clip($"Routed by roster ({route.Role}, from {route.Source.Description}) for step {instance.Name} of flow {flow.Pin.Id} {flow.Pin.Version}: {route.Reason}.", 4000),
            ["step"] = named,
            ["routedWith"] = routedWith,
        };
    }

    /// <summary>Why the one-step operation takes no step of a run now: each step that is part of it and not done, with its state and why.</summary>
    private static string NothingToTake(string goal, NextEligible.FlowRunRoutes routes)
    {
        if (routes.Position.Complete)
        {
            return $"Nothing to take: every step of goal {goal}'s run is done or off; its evaluation and acceptance remain, as for any goal.";
        }

        IEnumerable<string> held = routes.Steps.Where(s => s.Step.State is not ("done" or "off"))
            .Select(s => s.Route is NextEligible.Route route ? $"{s.Step.Name} is {s.Step.State}, {route.State}: {route.Reason}." : $"{s.Step.Name} is {s.Step.State}: {s.Step.Reasons[0]}.");
        return $"Nothing to dispatch: no step of goal {goal}'s run is one goal step takes now. {string.Join(" ", held)}";
    }
}
