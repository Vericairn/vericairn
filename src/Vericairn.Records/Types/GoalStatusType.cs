// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A change in a goal's lifecycle, from record format 2 (the estate's design/goals.md § 4). Achievement is the sponsor's acceptance, never a status.</summary>
public sealed class GoalStatusType : IRecordType
{
    /// <summary>The statuses a goal moves through after it is proposed.</summary>
    public static readonly string[] Statuses = ["active", "paused", "budget-limited", "blocked", "impossible", "abandoned"];

    /// <summary>Statuses only the sponsor records: activation and resumption grant the budget, and ending is the sponsor's call.</summary>
    private static readonly string[] SponsorOnly = ["active", "impossible", "abandoned"];

    /// <summary>Statuses after which a goal never moves again.</summary>
    private static readonly string[] Ended = ["impossible", "abandoned"];

    /// <inheritdoc />
    public string Name => "goal-status";

    /// <inheritdoc />
    public string Summary => "A change in a goal's lifecycle: active (activated or resumed by its sponsor, which grants its budget), paused, budget-limited, blocked, impossible (the evaluator's verdict, confirmed by the sponsor) or abandoned. Achievement is the sponsor's acceptance, never a status.";

    /// <inheritdoc />
    public string? SubjectType => "goal";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("status", string.Join('|', Statuses), true, "The new status of the exact goal revision; a revised goal starts again from its sponsor's activation.", 2),
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

        if (Array.IndexOf(SponsorOnly, status) >= 0 && context.Record.By != EstateModel.BodyString(goal, "sponsor"))
        {
            context.Error("goal-status.sponsor", $"Only the goal's sponsor records it as {status}.");
        }

        RecordEnvelope? previous = context.Model.About(goal.Id, Name)
            .Where(s => s.Id != context.Record.Id && Before(s, context.Record))
            .OrderBy(s => s.At, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal)
            .LastOrDefault();
        if (previous is null && status is not ("active" or "abandoned"))
        {
            context.Error("goal-status.first", "A goal starts when its sponsor activates it, or ends unstarted when its sponsor abandons it.");
        }
        else if (previous is not null && EstateModel.BodyString(previous, "status") is { } last && Array.IndexOf(Ended, last) >= 0)
        {
            context.Error("goal-status.ended", $"The goal ended as {last}; a new objective is a new goal.");
        }
    }

    private static bool Before(RecordEnvelope a, RecordEnvelope b) =>
        string.CompareOrdinal(a.At, b.At) < 0 || (a.At == b.At && string.CompareOrdinal(a.Id, b.Id) < 0);
}
