// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Types;

/// <summary>
/// A change in a goal's lifecycle, from record format 2 (the estate's design/goals.md § 4). Achievement is the sponsor's acceptance,
/// never a status. From 0.9.4 (FIX-143, design/flow.md § 8) a run of a flow may be closed by who decides for its goal, a person, with a
/// reason for each step its run had not settled; closing is neither acceptance nor abandonment, and never makes a step done.
/// </summary>
public sealed class GoalStatusType : IRecordType
{
    /// <summary>The statuses a goal moves through after it is proposed.</summary>
    public static readonly ImmutableArray<string> Statuses = ["active", "paused", "budget-limited", "blocked", "impossible", "abandoned", "closed"];

    /// <inheritdoc />
    public string Name => "goal-status";

    /// <inheritdoc />
    public string Summary => "A change in a goal's lifecycle: active (activated or resumed by its sponsor, a person with the authority to spend, or by an operator when an agent sponsors it, which grants its budget), paused, budget-limited, blocked, impossible (the evaluator's verdict, confirmed by the sponsor), abandoned or, from format 3, closed (a run of a flow closed by who decides for its goal, a person, with a reason for each step it had not settled). Achievement is an acceptance of the goal, never a status.";

    /// <inheritdoc />
    public string? SubjectType => "goal";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("status", string.Join('|', Statuses), true, "The new status of the exact goal revision; a revised goal starts again from its activation. Active is recorded by the sponsor, a person who holds the operator role or a grant of the 'spending' scope in force, or by an operator when an agent sponsors the goal; impossible and abandoned by the sponsor or, for a goal an agent sponsors, an operator, and from 0.9.3 impossible only when the goal's current evaluation, by a principal who authored none of its steps, judges a condition impossible. From 0.9.4, closed, in record format 3, closes a run of a flow with its unsettled steps: recorded only by who decides for the goal when that is a person, after the goal was activated, and followed by no status but impossible or abandoned; the goal may still be accepted.", 2),
        new("note", "string", true, "Why, and what remains.", 2),
        new("unsettled", "object[]", false, "With status closed, and only then: each step instance of the run that was not done or off when it was closed, once, as key, instance (for a step that runs over a list) and reason, the closer's reason for it; never empty. A step named here is never done by its closing.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? status = body.Enum("status", true, Statuses);
        body.Text("note", true, 4000);
        IReadOnlyList<BodyReader>? unsettled = body.Since(3, "unsettled") ? body.NestedArray("unsettled", status == "closed", allowEmpty: false) : null;
        var named = new List<(string Key, string? Instance, BodyReader Entry)>();
        foreach (BodyReader entry in unsettled ?? [])
        {
            string? key = entry.Text("key", true, 100, Patterns.Slug, "a step key");
            string? instance = entry.Text("instance", false, 200);
            string? reason = entry.Text("reason", true, RunClosings.MaximumReason);
            entry.NoOtherKeys();
            if (reason is not null && string.IsNullOrWhiteSpace(reason))
            {
                entry.Error("goal-status.unsettled", "reason", "Each unsettled step carries its closer's reason, not blank text.");
            }

            if (key is not null)
            {
                if (named.Any(n => n.Key == key && n.Instance == instance))
                {
                    entry.Error("goal-status.unsettled", "key", $"Step {key}{(instance is null ? string.Empty : $"[{instance}]")} is named more than once.");
                }

                named.Add((key, instance, entry));
            }
        }

        body.NoOtherKeys();
        if (status == "closed" && context.Format < 3)
        {
            context.BodyError("goal-status.closed", "A run is closed in record format 3, where runs of a flow are recorded.");
        }
        else if (status is not null && status != "closed" && unsettled is not null)
        {
            context.BodyError("goal-status.unsettled", "Only a closing names unsettled steps: unsettled goes with status closed.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("goal-status.author", $"Authoring principal {context.Record.By} has no principal record.");
        }

        RecordEnvelope? goal = context.Find(context.Record.Subject?.Id);
        if (status is null || goal is null || goal.Type != "goal")
        {
            return;
        }

        if (status == "active")
        {
            // Activation and resumption grant the budget: the sponsor, a person within the authority to spend, or an operator when an agent sponsors it.
            foreach ((string code, string message) in GoalRules.ActivationRefusals(context.Model, goal, context.Record.By, context.Record.AtTime, context.Record))
            {
                context.Error(code, message);
            }

            // From 0.9.4 (GOAL-07): a standing agent is never activated past its until time, nor beside an active one of its role over its subject.
            foreach ((string code, string message) in StandingAgents.ActivationRefusals(context.Model, goal, context.Record))
            {
                context.Error(code, message);
            }
        }
        else if (status is "impossible" or "abandoned" && !GoalRules.MayEnd(context.Model, goal, context.Record.By, context.Record))
        {
            context.Error("goal-status.sponsor", $"Only the goal's sponsor, or an operator for a goal an agent sponsors, records it as {status}.");
        }
        else if (status == "closed")
        {
            // Closing a run is who decides for its goal, a person, as accepting and stopping are; an agent never closes one (FIX-143).
            foreach ((string code, string message) in GoalRules.CloseRefusals(context.Model, goal, context.Record.By, context.Record))
            {
                context.Error(code, message);
            }

            Unsettled(context, goal, named);
        }
        else if (status == "impossible" && GoalRules.ImpossibleVerdictMissing(context.Model, goal, context.Record) is string missing)
        {
            // Impossible is the evaluator's verdict, confirmed by the sponsor (design/goals.md § 4). From 0.9.3 the write boundary refuses a
            // new status without that verdict; a status already in the estate keeps its reading, and validation reports it as information.
            context.Findings.Info(GoalRules.ImpossibleVerdictCode, context.Location + "#body.status", missing
                + " From 0.9.3 a new goal-status of impossible needs that verdict; a status already in the estate keeps its reading.");
        }

        // The statuses before this one that count: a status its author could not record changes nothing.
        IReadOnlyList<RecordEnvelope> before = GoalRules.Statuses(context.Model, goal, context.Record);
        RecordEnvelope? previous = before.Count == 0 ? null : before[^1];
        if (previous is null && status is not ("active" or "abandoned"))
        {
            context.Error("goal-status.first", "A goal starts when its sponsor activates it, or ends unstarted when its sponsor abandons it.");
        }
        else if (previous is not null && EstateModel.BodyString(previous, "status") is { } last && GoalRules.Ended.IndexOf(last) >= 0)
        {
            context.Error("goal-status.ended", $"The goal ended as {last}; a new objective is a new goal.");
        }
        else if (previous is not null && EstateModel.BodyString(previous, "status") == "closed" && status is not ("impossible" or "abandoned"))
        {
            context.Error("goal-status.closed", $"The goal's run was closed by goal-status {previous.Id}; it is accepted, stopped or revised, and its run is never resumed or closed again.");
        }
    }

    /// <summary>
    /// The steps a closing names: each a step of the run's flow, with an instance exactly when the step runs over a list, and one of
    /// the run's items. Which steps were unsettled at the closing is the runtime's to check when it writes it; the record keeps its claim.
    /// </summary>
    private static void Unsettled(ValidationContext context, RecordEnvelope goal, List<(string Key, string? Instance, BodyReader Entry)> named)
    {
        RecordEnvelope revision = goal;
        if (!FlowRuns.IsRun(revision))
        {
            context.Error("goal-status.closed", $"Goal {goal.Id} is not a run of a flow; a goal without a run is accepted or stopped.");
            return;
        }

        if (FlowRuns.Resolve(context.Model, revision).Flow is not { } flow)
        {
            return;
        }

        foreach ((string key, string? instance, BodyReader entry) in named)
        {
            FlowStep? step = flow.Step(key);
            if (step is null)
            {
                entry.Error("goal-status.unsettled", "key", $"Flow {flow.Pin.Id} {flow.Pin.Version} has no step '{key}'.");
            }
            else if (step.Each is null && instance is not null)
            {
                entry.Error("goal-status.unsettled", "instance", $"Step {key} runs once; it names no instance.");
            }
            else if (step.Each is not null && (instance is null || !FlowRuns.Items(revision, step.Each).Contains(instance, StringComparer.Ordinal)))
            {
                entry.Error("goal-status.unsettled", "instance", $"Step {key} runs once for each of the run's {step.Each}; it names one of them as its instance.");
            }
        }
    }
}
