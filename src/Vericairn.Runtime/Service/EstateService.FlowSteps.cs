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
        FlowPosition.StepInstance instance = taken.Step!;
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
        // Each item the step closes on, with what it must show: its kind, its provenance, its evidence and its review.
        List<string> outputs = instance.Items.Where(i => i.State != "off").Select(i => Clip(Describe(step, i.Declared), 2000)).ToList();
        if (outputs.Count == 0)
        {
            outputs.AddRange(Strings(order.Body, "deliverables"));
        }

        var criteria = new List<string>();
        if (step.Kind == "review")
        {
            FlowInput reviewed = step.Inputs.First(i => i.Step is not null);
            FlowPosition.StepInstance? producer = taken.Flow.Find(new FlowRuns.StepRef(reviewed.Step!, flow.Step(reviewed.Step)?.Each is null ? null : instance.Instance));
            criteria.Add(producer?.Result is RecordEnvelope result
                ? $"Review result {result.Id} at {result.ComputedHash}, of step {producer.Name}: record a review of it with outcome pass, findings or fail; a review by its author does not count."
                : $"Review the result of step {reviewed.Step}: record a review of it with outcome pass, findings or fail; a review by its author does not count.");
        }
        else
        {
            criteria.Add(instance.Items.All(i => i.State == "off") ? $"The step is complete: {step.Title}." : "The step closes only when every required item holds its provenance, evidence and review at its exact hash.");
            criteria.AddRange(instance.Done.Select(d => "Its done predicate holds: " + GoalPredicates.DescribePredicate(d.Predicate) + "."));
            if (step.Outputs.Count == 0 && step.Review is { Independence: not "none" } review)
            {
                criteria.Add($"Its result is handed on only after a review ({review.Independence}{(review.Role is null ? string.Empty : ", in the " + review.Role + " role")}) by a principal other than its author.");
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
        if (instance.Instance is not null)
        {
            named["instance"] = instance.Instance;
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

    /// <summary>One item of a step in words, with every requirement it declares and the review it takes, its step's when it declares none.</summary>
    private static string Describe(FlowStep step, FlowOutput output)
    {
        var text = new System.Text.StringBuilder();
        text.Append(output.Name).Append(output.Required ? string.Empty : " (optional)").Append(": ").Append(output.Kind switch
        {
            "file" => $"a file named {output.Name} among the result's outputs, by hash{(output.MediaType is null ? string.Empty : ", " + output.MediaType)}",
            _ when step.Kind == "review" => "the review recorded of the reviewed result",
            _ => $"a{(output.Kind is "observation" or "attestation" ? "n" : string.Empty)} {output.Kind}: the newest {output.Counts?.Replace("|", " or ", StringComparison.Ordinal)} record{(output.RecordKind is null ? string.Empty : $" of kind {output.RecordKind}")} the step's done predicates count",
        });
        if (output.Provenance is FlowProvenance provenance)
        {
            var from = new List<string>();
            if (provenance.Principal is not null) from.Add("by principal " + provenance.Principal);
            if (provenance.Holder is not null) from.Add("by " + (provenance.Holder == "either" ? "a person or an agent" : "a " + provenance.Holder));
            if (provenance.Role is not null) from.Add("in the " + provenance.Role + " role");
            if (provenance.Inputs.Count > 0) from.Add("from the pinned inputs " + string.Join(", ", provenance.Inputs));
            if (provenance.Provider is not null) from.Add("with provider " + provenance.Provider);
            if (provenance.Tool is not null) from.Add("with tool " + provenance.Tool);
            if (provenance.Settings.ValueKind == JsonValueKind.Object) from.Add("with " + string.Join(", ", provenance.Settings.EnumerateObject().Select(t => $"{t.Name} {t.Value.GetString()}")));
            text.Append("; produced ").Append(string.Join(", ", from));
        }

        foreach (FlowEvidence evidence in output.Evidence)
        {
            text.Append("; evidence ").Append(evidence.Name).Append(": a passing verification by ").Append(evidence.Method).Append(evidence.Command is null ? string.Empty : " running " + evidence.Command).Append(" at its exact hash");
        }

        if (step.Kind != "review" && (output.Review ?? step.Review) is FlowReview review && review.Independence != "none")
        {
            text.Append("; reviewed (").Append(review.Independence).Append(review.Role is null ? string.Empty : ", in the " + review.Role + " role").Append(") at its exact hash before the step closes");
        }

        return text.ToString();
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
