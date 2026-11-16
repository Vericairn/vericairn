// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The decisions of decision transitions (from 0.9.4, PROC-19; the estate's design/flow.md § 5, decision 01M75YYK958AFMS24W71BHRH26). A
/// step's decision is its item of kind decision: a role's principal, an agent or a person, records it as the result of its assignment of the
/// step, whose decision field names the path or paths taken, the reasons and the evidence by hash, signed by the decider's own device; a
/// rule's is derived by the runtime from the records it read, never stored. A step's decision is distinct from the estate's decision
/// records, which stay the operator's, and choosing a path performs nothing reserved: it only makes the steps the path leads to eligible.
/// The runtime plans a person's or an agent's decision here, and the result record type keeps the same rules.
/// </summary>
public static class FlowDecisions
{
    /// <summary>The longest reasons a decision records.</summary>
    public const int MaximumReasons = 4000;

    /// <summary>A decision as its result records it: the path or paths taken, the reasons and the evidence by hash; its decider is the result's author.</summary>
    /// <param name="Result">The result that carries it.</param>
    /// <param name="Paths">The path or paths taken, as recorded.</param>
    /// <param name="Reasons">The decider's reasons.</param>
    /// <param name="Evidence">The evidence it rests on, each an exact record by hash.</param>
    public sealed record Recorded(RecordEnvelope Result, IReadOnlyList<string> Paths, string Reasons, IReadOnlyList<RecordReference> Evidence)
    {
        /// <summary>Who decided: the result's author, the principal that held the step's assignment.</summary>
        public string By => Result.By;
    }

    /// <summary>A result's decision, when it carries one; null otherwise.</summary>
    public static Recorded? Of(RecordEnvelope result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Type != "result" || TJson.Object(result.Body, "decision") is not { ValueKind: JsonValueKind.Object } decision)
        {
            return null;
        }

        List<RecordReference> evidence = TJson.Array(decision, "evidence")
            .Where(e => TJson.Text(e, "type") is not null && TJson.Text(e, "id") is not null)
            .Select(e => new RecordReference(TJson.Text(e, "type")!, TJson.Text(e, "id")!, TJson.Text(e, "hash"))).ToList();
        return new Recorded(result, TJson.Strings(decision, "paths").ToList(), TJson.Text(decision, "reasons") ?? string.Empty, evidence);
    }

    /// <summary>
    /// Why a decision naming these paths is refused for a step's transition: no path, a path the transition does not declare or named
    /// twice, several paths where the flow allows one, or several where one of them goes back, ends the run or hands it to another flow,
    /// which is taken alone. Empty when it holds.
    /// </summary>
    public static List<string> PathRefusals(FlowDefinition flow, FlowStep step, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(paths);
        var refusals = new List<string>();
        if (step.Transition is not FlowTransition transition)
        {
            refusals.Add($"step {step.Key} declares no decision transition");
            return refusals;
        }

        string declared = string.Join(", ", transition.Paths.Select(p => $"{p.Name} ({p.Leads})"));
        if (paths.Count == 0)
        {
            refusals.Add($"a decision names the path it takes, one of: {declared}");
        }

        foreach (string path in paths.Where(p => transition.Path(p) is null))
        {
            refusals.Add($"'{path}' is not a path of step {step.Key}, whose paths are {declared}");
        }

        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
        {
            refusals.Add("each path is named once");
        }

        if (paths.Count > 1 && !transition.Several)
        {
            refusals.Add($"step {step.Key}'s transition takes one path; the flow does not let one decision take several");
        }
        else if (paths.Count > 1)
        {
            foreach (FlowPath alone in paths.Select(transition.Path).OfType<FlowPath>().Where(p => p.Ends || FlowTransition.IsBack(p, flow.IndexOf, step.Key)))
            {
                refusals.Add($"path {alone.Name} leads to {alone.Leads}, which is taken alone, never with other paths");
            }
        }

        return refusals;
    }

    /// <summary>
    /// Validates a result's decision field (record format 3): the paths, the reasons and the evidence, each an exact record by hash; and,
    /// where the run's flow resolves here, that the result is a completed result of an assignment of a decision step whose transition its
    /// role decides in the run's configuration, naming its declared paths as the flow allows, by a person where the decision is a person's.
    /// </summary>
    internal static void Validate(ValidationContext context, BodyReader body, string? outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Since(3, "decision") || body.Nested("decision", false) is not BodyReader decision)
        {
            return;
        }

        IReadOnlyList<string> paths = decision.StringArray("paths", true, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "a path name") ?? [];
        string? reasons = decision.Text("reasons", true, MaximumReasons);
        if (reasons is not null && string.IsNullOrWhiteSpace(reasons))
        {
            decision.Error("result.decision", "reasons", "A decision records its decider's reasons, not blank text.");
        }

        foreach (BodyReader evidence in decision.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            string? type = evidence.Text("type", true, 100, Patterns.Slug, "a record type");
            string? id = evidence.Ulid("id", true);
            string? hash = evidence.Hash("hash", true);
            evidence.NoOtherKeys();
            if (type is not null && context.RequireReference("decision.evidence", id, type) is RecordEnvelope pinned && pinned.ComputedHash != hash)
            {
                evidence.Error("result.decision", "hash", "The evidence does not have the recorded content hash.");
            }
        }

        decision.NoOtherKeys();
        if (outcome is not null && outcome != "completed")
        {
            context.Error("result.decision", "A decision is the completed result of its step's assignment: its outcome is completed.");
        }

        RecordEnvelope? attempt = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        RecordEnvelope? assignment = attempt?.Subject is null ? null : context.Find(attempt.Subject.Id);
        FlowRuns.StepRef? named = assignment is null ? null : FlowRuns.StepOf(assignment);
        RecordEnvelope? order = assignment?.Subject is null ? null : context.Find(assignment.Subject.Id);
        RecordEnvelope? run = order is { Type: "work-order" } ? FlowRuns.RunOf(context.Model, order, context.Record) : null;
        if (attempt is not { Type: "attempt" } || named is null || named.Review || run is null)
        {
            context.Error("result.decision", "A decision is recorded on an attempt of an assignment that carries a decision step of a run's flow, never on a review or on other work.");
            return;
        }

        if (FlowRuns.Resolve(context.Model, run).Flow is not FlowDefinition flow)
        {
            return;
        }

        FlowStep? step = flow.Step(named.Key);
        if (step is not { Decides: true })
        {
            context.Error("result.decision", $"Step {named.Key} of flow {flow.Pin.Id} is not a decision step whose role decides its transition.");
            return;
        }

        string decider = FlowRuns.Configure(context.Model, run, flow, context.Record).Decider(step);
        if (decider == "rule")
        {
            context.Error("result.decision", $"Step {step.Key}'s transition is decided by a rule in this run, from the records; no principal records its decision.");
        }

        foreach (string refusal in PathRefusals(flow, step, paths))
        {
            context.Error("result.decision", char.ToUpperInvariant(refusal[0]) + refusal[1..] + ".");
        }

        if (step.Transition!.PersonOnly && !GoalRules.IsPerson(context.Model, context.Record.By))
        {
            context.Error("result.decision", $"Step {step.Key}'s decision is a person's{(step.Transition.Undelegable is string act ? $", an act the operating-modes design never delegates ({act})" : ", as the flow requires")}, and {Routing.NameOf(context.Model, context.Record.By)} is not a person.");
        }
    }

    /// <summary>A decision the runtime may record: the step, the assignment it is recorded under, an attempt to reuse when one is open, and the decision; or why not.</summary>
    /// <param name="Step">The step instance decided, in words.</param>
    /// <param name="Assignment">The assignment that holds the step for its decider.</param>
    /// <param name="Attempt">The decider's open attempt on it, which the result answers; null when a new attempt is recorded with it.</param>
    /// <param name="Paths">The path or paths taken.</param>
    /// <param name="Reasons">The reasons.</param>
    /// <param name="Evidence">The evidence, each an exact record by hash.</param>
    /// <param name="Refusals">Every reason it is refused, all at once; empty when it may be recorded.</param>
    public sealed record Plan(string Step, RecordEnvelope? Assignment, RecordEnvelope? Attempt, IReadOnlyList<string> Paths, string Reasons, IReadOnlyList<RecordReference> Evidence, IReadOnlyList<string> Refusals)
    {
        /// <summary>The attempt body recorded with the decision when the decider has no open attempt on the assignment.</summary>
        public JsonObject AttemptBody(DateTimeOffset at) => new()
        {
            ["startedAt"] = RecordParser.FormatTimestamp(at),
            ["workspace"] = "the run's decision, recorded through the runtime",
            ["note"] = $"Decide step {Step}'s transition.",
        };

        /// <summary>The result body that records the decision: completed, its summary the paths and reasons, and its decision field.</summary>
        public JsonObject ResultBody()
        {
            string taken = string.Join(", ", Paths);
            string summary = $"Decided step {Step}: path{(Paths.Count == 1 ? string.Empty : "s")} {taken}. {Reasons}";
            return new JsonObject
            {
                ["outcome"] = "completed",
                ["summary"] = summary.Length > 8000 ? summary[..7999] + "…" : summary,
                ["outputs"] = new JsonArray(),
                ["evidence"] = new JsonArray(),
                ["checks"] = new JsonArray(),
                ["limitations"] = new JsonArray("A decision only makes the steps its path leads to eligible; it performs nothing reserved."),
                ["unresolvedEffects"] = new JsonArray(),
                ["nextStep"] = $"The run takes path {taken} when goal step is called.",
                ["decision"] = new JsonObject
                {
                    ["paths"] = new JsonArray(Paths.Select(p => (JsonNode)p).ToArray()),
                    ["reasons"] = Reasons,
                    ["evidence"] = new JsonArray(Evidence.Select(e => (JsonNode)new JsonObject { ["type"] = e.Type, ["id"] = e.Id, ["hash"] = e.Hash }).ToArray()),
                },
            };
        }
    }

    /// <summary>
    /// Plans a principal's decision of a run's step (from 0.9.4, PROC-19), refusing every problem at once before anything is written: a goal
    /// that is not an active run, a step that is not a decision step its role decides in this run, one not waiting on its decider (not yet
    /// ready, already decided, in review, or held by attention), a decider that does not hold the step's assignment for this round, an
    /// agent where the decision is a person's, a path the transition does not offer, blank reasons, and evidence that is not a record of
    /// the estate. The evidence is named by id, or type:id, and pinned at its current hash.
    /// </summary>
    public static Plan Prepare(EstateModel model, RecordEnvelope goal, string by, string step, IReadOnlyList<string> paths, string? reasons, IReadOnlyList<string> evidence, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(evidence);
        var refusals = new List<string>();
        RecordEnvelope current = model.Latest(goal);
        var pinned = new List<RecordReference>();
        foreach (string named in evidence)
        {
            string id = named.Contains(':', StringComparison.Ordinal) ? named[(named.IndexOf(':', StringComparison.Ordinal) + 1)..] : named;
            string? type = named.Contains(':', StringComparison.Ordinal) ? named[..named.IndexOf(':', StringComparison.Ordinal)] : null;
            if (!model.ById.TryGetValue(id, out RecordEnvelope? record) || (type is not null && record.Type != type))
            {
                refusals.Add($"Evidence {named} is not a record of this estate{(type is null ? string.Empty : $" of type {type}")}.");
                continue;
            }

            pinned.Add(new RecordReference(record.Type, record.Id, record.ComputedHash));
        }

        if (string.IsNullOrWhiteSpace(reasons))
        {
            refusals.Add("A decision records its reasons: why this path, from the evidence it names.");
        }
        else if (reasons.Length > MaximumReasons)
        {
            refusals.Add($"A decision's reasons are at most {MaximumReasons} characters.");
        }

        Plan Refused(params string[] more) => new(step, null, null, paths, reasons ?? string.Empty, pinned, [.. refusals, .. more]);
        if (!FlowRuns.IsRun(current))
        {
            return Refused($"Goal {current.Id} is not a run of a flow; only a run's steps are decided.");
        }

        string status = GoalRules.Status(model, current);
        if (status != "active")
        {
            return Refused($"Goal {current.Id} is {status}: a step of a run is decided only while its goal is active.");
        }

        FlowPosition.Position position = FlowPosition.Derive(model, current, at);
        if (position.Flow is not FlowDefinition flow)
        {
            return Refused($"The run's {position.Unavailable}: none of its steps is decided on this machine.");
        }

        FlowPosition.StepInstance? instance = position.Steps.FirstOrDefault(s => s.Name == step);
        if (instance is null)
        {
            return Refused($"Run {current.Id} has no step {step}; its steps are {string.Join(", ", position.Steps.Select(s => s.Name))}.");
        }

        if (!instance.Step.Decides || instance.Step.Transition is not FlowTransition transition)
        {
            return Refused($"Step {step} is not a decision step whose role decides its transition.");
        }

        if (position.Configuration!.Decider(instance.Step) == "rule")
        {
            return Refused($"Step {step}'s transition is decided by a rule in this run, from the records; no principal decides it.");
        }

        refusals.AddRange(PathRefusals(flow, instance.Step, paths).Select(r => char.ToUpperInvariant(r[0]) + r[1..] + "."));
        if (instance.Decision is not { State: "pending" or "held" })
        {
            refusals.Add(instance.Decision is { } decided
                ? $"Step {step} is not waiting on its decider: its decision is {decided.State}{(decided.Paths.Count == 0 ? string.Empty : $", path {string.Join(", ", decided.Paths)}")} ({(instance.Reasons.Count > 0 ? instance.Reasons[0] : instance.State)})."
                : $"Step {step} is not waiting on its decider: it is {instance.State} ({(instance.Reasons.Count > 0 ? instance.Reasons[0] : "no reason given")}).");
        }

        if (transition.PersonOnly && !GoalRules.IsPerson(model, by))
        {
            refusals.Add($"Step {step}'s decision is a person's{(transition.Undelegable is string act ? $", an act the operating-modes design never delegates ({act})" : ", as the flow requires")}, and {Routing.NameOf(model, by)} is not a person.");
        }

        RecordEnvelope? assignment = instance.Rerun ? null : instance.Assignment;
        string? holder = assignment is null ? null : EstateModel.BodyString(assignment, "contributor");
        if (assignment is null)
        {
            refusals.Add($"No assignment holds step {step} for this round: vericairn goal step {current.Id} routes it to its decider in the {instance.Step.Role} role first.");
        }
        else if (holder != by)
        {
            refusals.Add($"Step {step} is assigned to {Routing.NameOf(model, holder)} (assignment {assignment.Id}), who decides it; {Routing.NameOf(model, by)} does not hold it.");
        }

        RecordEnvelope? open = assignment is null ? null : WorkView.AttemptsOf(model, assignment).LastOrDefault(a => a.By == by && WorkView.ResultOf(model, a) is null);
        return new Plan(step, refusals.Count == 0 ? assignment : null, open, paths, reasons ?? string.Empty, pinned, refusals);
    }
}
