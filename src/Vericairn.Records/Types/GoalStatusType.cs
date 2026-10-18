// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A change in a goal's lifecycle, from record format 2 (the estate's design/goals.md § 4). Achievement is the sponsor's acceptance, never a status.</summary>
public sealed class GoalStatusType : IRecordType
{
    /// <summary>The statuses a goal moves through after it is proposed.</summary>
    public static readonly ImmutableArray<string> Statuses = ["active", "paused", "budget-limited", "blocked", "impossible", "abandoned"];

    /// <inheritdoc />
    public string Name => "goal-status";

    /// <inheritdoc />
    public string Summary => "A change in a goal's lifecycle: active (activated or resumed by its sponsor, a person with the authority to spend, or by an operator when an agent sponsors it, which grants its budget), paused, budget-limited, blocked, impossible (the evaluator's verdict, confirmed by the sponsor) or abandoned. Achievement is an acceptance of the goal, never a status.";

    /// <inheritdoc />
    public string? SubjectType => "goal";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("status", string.Join('|', Statuses), true, "The new status of the exact goal revision; a revised goal starts again from its activation. Active is recorded by the sponsor, a person who holds the operator role or a grant of the 'spending' scope in force, or by an operator when an agent sponsors the goal; impossible and abandoned by the sponsor or, for a goal an agent sponsors, an operator.", 2),
        new("note", "string", true, "Why, and what remains.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? status = body.Enum("status", true, Statuses);
        body.Text("note", true, 4000);
        body.NoOtherKeys();
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
            foreach ((string code, string message) in GoalRules.ActivationRefusals(context.Model, goal, context.Record.By, context.Record.AtTime))
            {
                context.Error(code, message);
            }
        }
        else if (status is "impossible" or "abandoned" && !GoalRules.MayEnd(context.Model, goal, context.Record.By))
        {
            context.Error("goal-status.sponsor", $"Only the goal's sponsor, or an operator for a goal an agent sponsors, records it as {status}.");
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
    }
}
