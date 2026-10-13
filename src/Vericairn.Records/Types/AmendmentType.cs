// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Records.Types;

/// <summary>A request to change an assignment's outcome, scope, target, budget or permitted effects; it authorizes nothing.</summary>
public sealed class AmendmentType : IRecordType
{
    /// <inheritdoc />
    public string Name => "amendment";

    /// <inheritdoc />
    public string Summary => "A request, raised during work, to change an assignment's outcome, scope, target, budget or permitted effects. It authorizes nothing: a new work-order revision, a new assignment or a decision resolves it.";

    /// <inheritdoc />
    public string? SubjectType => "assignment";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", "scope|effect|budget|target|other", true, "What the request changes."),
        new("rationale", "string", true, "Why the change is needed."),
        new("proposedChanges", "string[]", true, "The concrete changes proposed."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Enum("kind", true, "scope", "effect", "budget", "target", "other");
        body.Text("rationale", true, 4000);
        body.StringArray("proposedChanges", true, allowEmpty: false, maxItemLength: 2000);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("amendment.author", $"Requesting principal {context.Record.By} has no principal record.");
        }
    }
}
