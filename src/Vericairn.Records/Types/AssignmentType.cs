// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Delegates a work order to a contributor under a brief, pinned to the exact work-order revision.</summary>
public sealed class AssignmentType : IRecordType
{
    /// <inheritdoc />
    public string Name => "assignment";

    /// <inheritdoc />
    public string Summary => "Delegates a work order to a contributor under a brief (outcome, sources, permitted changes, prohibited effects, workspace, limits, expected outputs, review criteria, recovery), pinned to the exact work-order revision.";

    /// <inheritdoc />
    public string? SubjectType => "work-order";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("contributor", "ulid", true, "The principal that does the work: a human, a deterministic tool, a native agent or a qualified external agent. Admitted before the work is assigned."),
        new("brief", "object", true, "outcome, sources (references {type, id, hash?}), permittedChanges, prohibitedEffects, workspace (allowed trees, named without machine paths), limits, expectedOutputs, reviewCriteria, recovery."),
        new("note", "string", false, "Free text."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? contributor = body.Ulid("contributor", true);
        BodyReader? brief = body.Nested("brief", true);
        if (brief is not null)
        {
            brief.Text("outcome", true, 4000);
            foreach (BodyReader source in brief.NestedArray("sources", true, allowEmpty: true) ?? [])
            {
                RecordReference? reference = RecordParser.ParseReference(source.Element, source.Location, context.Findings);
                if (reference is null)
                {
                    continue;
                }

                RecordEnvelope? target = context.RequireReference("brief.sources", reference.Id, reference.Type);
                if (target is not null && reference.Hash is not null && reference.Hash != target.ComputedHash)
                {
                    context.Findings.Error("assignment.source-hash", source.Location, $"Source {reference.Id} does not have the pinned content hash; the brief relied on a different revision.");
                }
            }

            brief.StringArray("permittedChanges", true, allowEmpty: false, maxItemLength: 2000);
            brief.StringArray("prohibitedEffects", true, allowEmpty: true, maxItemLength: 2000);
            brief.StringArray("workspace", true, allowEmpty: false, maxItemLength: 500);
            brief.StringArray("limits", true, allowEmpty: true, maxItemLength: 2000);
            brief.StringArray("expectedOutputs", true, allowEmpty: false, maxItemLength: 2000);
            brief.StringArray("reviewCriteria", true, allowEmpty: false, maxItemLength: 2000);
            brief.Text("recovery", true, 4000);
            brief.NoOtherKeys();
        }

        body.Text("note", false, 4000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("assignment.author", $"Assigning principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.Model.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("assignment.author", "Work is assigned by the operator or by a principal granted the 'local-engineering' scope.");
        }

        if (contributor is not null && context.Principal(contributor) is null)
        {
            context.Error("assignment.contributor", $"Contributor {contributor} has no principal record; contributors are admitted before work is assigned to them.");
        }
    }
}
