// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Settles divergent revisions of one record (two clones each superseded it) by keeping one successor and withdrawing the others.</summary>
public sealed class ReconciliationType : IRecordType
{
    /// <inheritdoc />
    public string Name => "reconciliation";

    /// <inheritdoc />
    public string Summary => "Settles divergent revisions of one record: when several records supersede the same predecessor (two clones revised it), a reconciliation keeps one and withdraws the others, which then count as superseded by the kept one. Nothing is deleted.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kept", "ulid", true, "The successor of the subject that remains current."),
        new("withdrawn", "ulid[]", true, "The other successors of the subject, withdrawn in favor of the kept one."),
        new("note", "string", true, "Why this one is kept and what, if anything, from the others must be carried into a later revision."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? kept = body.Ulid("kept", true);
        IReadOnlyList<string>? withdrawn = body.StringArray("withdrawn", true, allowEmpty: false, maxItemLength: Identity.Ulid.Length);
        body.Text("note", true, 4000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("reconciliation.author", $"Reconciling principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer) && !context.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("reconciliation.author", "Divergent revisions are reconciled by the operator or under the 'administer' or 'local-engineering' scope.");
        }

        string? subjectId = context.Record.Subject?.Id;
        if (subjectId is null || kept is null || withdrawn is null)
        {
            return;
        }

        foreach (string id in withdrawn.Append(kept))
        {
            RecordEnvelope? successor = context.Find(id);
            if (successor is null)
            {
                context.Error("reconciliation.successor", $"{id} does not exist.");
            }
            else if (successor.Supersedes != subjectId)
            {
                context.Error("reconciliation.successor", $"{id} does not supersede the subject {subjectId}; a reconciliation settles only direct successors of one record.");
            }
        }

        if (withdrawn.Contains(kept, StringComparer.Ordinal))
        {
            context.Error("reconciliation.kept", "The kept successor cannot also be withdrawn.");
        }
    }
}
