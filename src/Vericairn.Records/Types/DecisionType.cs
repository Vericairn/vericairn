// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>An operator decision, with the operator's words preserved verbatim.</summary>
public sealed class DecisionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "decision";

    /// <inheritdoc />
    public string Summary => "An operator decision: the verbatim statement, a summary, its effects and the work orders it affects.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("title", "string", true, "Short title."),
        new("statement", "string", true, "The operator's words, verbatim."),
        new("summary", "string", true, "What was decided, in one or two sentences, for lists."),
        new("synthesis", "string", false, "A clean, thorough restatement for display: context, what was decided, why, consequences and limits. Expected on every decision; the recorder writes it and it never alters the verbatim statement."),
        new("effects", "string[]", true, "Concrete effects on the estate."),
        new("affects", "ulid[]", true, "Work orders this decision changes; may be empty."),
        new("decidedBy", "ulid", true, "The operator principal who decided."),
        new("decidedAt", "timestamp", true, "When the operator decided."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("title", true, 200);
        body.Text("statement", true, 20000);
        body.Text("summary", true, 4000);
        if (body.Text("synthesis", false, 40000) is null && !body.Has("synthesis") && context.Model.IsCurrent(context.Record))
        {
            body.Warning("decision.synthesis", "synthesis", "Every decision should carry a synthesis: a clean, thorough restatement for display alongside the verbatim statement.");
        }

        body.StringArray("effects", true, allowEmpty: false, maxItemLength: 2000);
        IReadOnlyList<string>? affects = body.StringArray("affects", true, allowEmpty: true, maxItemLength: Identity.Ulid.Length);
        string? decidedBy = body.Ulid("decidedBy", true);
        body.Timestamp("decidedAt", true);
        body.NoOtherKeys();
        if (affects is not null)
        {
            foreach (string id in affects)
            {
                context.RequireReference("affects", id, "work-order");
            }
        }

        if (decidedBy is not null && context.RequireReference("decidedBy", decidedBy, "principal") is not null && !context.HasRole(decidedBy, Roles.Operator))
        {
            context.Error("decision.operator", "Decisions are made by a principal holding the operator role; agents record them.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("decision.author", $"Recording principal {context.Record.By} has no principal record.");
        }
    }
}
