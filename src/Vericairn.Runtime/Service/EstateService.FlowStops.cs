// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// A run's reserved act taken one step per command (the estate's design/flow.md § 8; design/release-procedure.md § 6): whoever takes the
/// run's steps records the operator request it waits on, put to the operator with the step's exact command as the records give it now,
/// computed only because the step is next. Nothing is dispatched for it, the request grants nothing, and the step is done only once the
/// records of the operator's own act hold. A request whose command the records no longer give is revised when its author takes the step
/// again; otherwise a new one carries the step, and the old one is withdrawn from delivery.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>
    /// The clock a run's live reads use: goal show, goal next, goal step's routes, human status, the operator request queue and required
    /// attention. Canonical status reads none: it derives a run at the records' own time (<see cref="FlowStops.RecordedTime"/>).
    /// </summary>
    internal TimeProvider FlowClock { get; set; } = TimeProvider.System;

    /// <summary>Records the operator request of a run's reserved act; no assignment, ask or run.</summary>
    private GoalStepResponse RequestStep(EstateModel model, RecordEnvelope goal, Taken taken, GoalStepRequest request)
    {
        FlowPosition.StepInstance instance = taken.Step!;
        FlowPosition.Position position = taken.Flow!;
        NextEligible.Route route = taken.Route;
        string key = taken.Key;
        if (instance.Step.Reserved is not string act || instance.Command is not string command || instance.State != "stopped")
        {
            return StepProblem(goal.Id, "refused", $"{key} is not a reserved act waiting on the operator with a command the records give now. Nothing was recorded.", key, route);
        }

        if (route.Principal is not string to)
        {
            return StepProblem(goal.Id, "refused", $"{key} is a reserved act, {act}, and the estate names no person with the operator role to put its request to. Nothing was recorded.", key, route);
        }

        RecordEnvelope order = taken.Order;
        string goalTitle = EstateModel.BodyString(goal, "title") ?? goal.Id;
        string done = string.Join("; ", instance.Done.Select(d => GoalPredicates.DescribePredicate(d.Predicate)));
        var step = new JsonObject { ["goal"] = model.Chain(goal)[0], ["key"] = instance.Step.Key };
        if (instance.Instance is not null)
        {
            step["instance"] = instance.Instance;
        }

        var body = new JsonObject
        {
            ["to"] = to,
            ["askedBy"] = request.By,
            ["action"] = Clip($"Carry the reserved act {act} of step {instance.Name}, {instance.Step.Title}, in goal {goalTitle}: run its exact command yourself.", 4000),
            ["why"] = Clip(FlowStops.Why(model, position, instance), 4000),
            ["recommendation"] = Clip($"Run the command yourself, as the operator, when you are ready: {command} The step is done once the records of your act hold ({done}); then close this request with vericairn operator done.", 4000),
            ["consequence"] = Clip($"Step {instance.Name} waits, and every step after it waits with it; the run's other steps go on. Nothing reserved is done for you, and this request grants nothing.", 2000),
            ["kind"] = act == "publish" ? "publication" : "action",
            ["destination"] = Clip($"the reserved act {act} of step {instance.Name} in goal {goalTitle} ({goal.Id})", 1000),
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "command", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(command)), ["text"] = command }),
            ["flowStep"] = step,
        };

        // A request its author made whose command the records no longer give is revised, keeping its identity; any other is left to the
        // derived queue, which withdraws it from delivery.
        RecordEnvelope? previous = FlowStops.Classify(model, position, instance.Ref)
            .Where(r => r.State == "withdrawn" && r.Request.By == request.By && r.Request.Subject == order.Reference && EstateModel.BodyString(r.Request, "to") == to)
            .Select(r => r.Request).LastOrDefault();
        WriteOutcome written = WriteLocked(new RecordDraft("operator-request", request.By, Element(body), order.Reference, previous?.Id), model);
        if (!written.Ok)
        {
            return new GoalStepResponse(goal.Id, "none", "Not recorded: " + written.Problem!.Message, false, key, route.State, to, Problem: written.Problem);
        }

        string whom = PrincipalName(model, to);
        return new GoalStepResponse(goal.Id, "request",
            $"{key} is a reserved act, {act}: operator request {written.Written!.Id}{(previous is null ? string.Empty : $", revising {previous.Id},")} waits on {whom} with its exact command: {command} Nothing was dispatched; the request grants nothing, and the step is done once the records of the operator's act hold.",
            true, key, route.State, to) { Request = written.Written };
    }
}
