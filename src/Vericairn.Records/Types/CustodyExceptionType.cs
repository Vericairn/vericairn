// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// The sponsor's exception to the custody precondition for one goal, from record format 2 (the estate's design/goals.md § 10): while
/// it is in force, a coding agent's run of the goal may be launched unattended on a machine where a key of an operator or of the
/// sponsor signs without its owner's presence. Pinned to the goal's exact revision, recorded only by who decides for the goal, with
/// its reason and its expiry.
/// </summary>
public sealed class CustodyExceptionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "custody-exception";

    /// <inheritdoc />
    public string Summary => "The sponsor's exception to the custody precondition for one goal revision: why unattended runs of the goal may launch on a machine where a key of an operator or of the sponsor signs without its owner's presence, and when the exception expires. Recorded only by the goal's sponsor, or by an operator when an agent sponsors it; a revised goal needs a fresh one.";

    /// <inheritdoc />
    public string? SubjectType => "goal";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("reason", "string", true, "Why the sponsor accepts unattended runs of the goal while such a key signs where they run.", 2),
        new("expires", "timestamp", true, "When the exception stops being in force; later than the record. A revision by its author can bring it forward.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("reason", true, 4000);
        string? expires = body.Timestamp("expires", true);
        body.NoOtherKeys();
        if (expires is not null && string.CompareOrdinal(expires, context.Record.At) <= 0)
        {
            body.Error("custody-exception.expires", "expires", "A custody exception expires after it is made.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("custody-exception.author", $"Authoring principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? goal = context.Find(context.Record.Subject?.Id);
        if (goal is null || goal.Type != "goal")
        {
            return;
        }

        if (!GoalRules.AccepterOf(context.Model, goal).Decides(context.Model, context.Record.By))
        {
            context.Error("custody-exception.sponsor", "A custody exception is recorded by the goal's sponsor when the sponsor is a person, and by an operator when an agent sponsors it; an agent never excepts its own runs from the custody precondition.");
        }

        if (context.Find(context.Record.Supersedes) is { Type: "custody-exception" } previous && (previous.Subject?.Id != context.Record.Subject?.Id || previous.By != context.Record.By))
        {
            context.Error("custody-exception.revision", "A revision of a custody exception is its author's, about the same goal revision; another goal or revision needs its own.");
        }
    }
}
