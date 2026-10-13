// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>A standing grant of scoped permission from the operator to a principal.</summary>
public sealed class AuthorityType : IRecordType
{
    /// <inheritdoc />
    public string Name => "authority";

    /// <inheritdoc />
    public string Summary => "A standing, scoped grant of permission to a principal, with reserved actions and conditions, based on a decision.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("grantee", "ulid", true, "The principal receiving the grant."),
        new("scope", "string[]", true, "Granted scopes; known names: " + string.Join(", ", Scopes.Known) + ". Others are free text."),
        new("reserved", "string[]", true, "Actions explicitly never granted (publication, spending, credentials, ...)."),
        new("conditions", "string[]", true, "Conditions under which the grant applies."),
        new("basis", "ulid", true, "The decision record that authorizes this grant."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? grantee = body.Ulid("grantee", true);
        body.StringArray("scope", true, allowEmpty: false, maxItemLength: 200);
        body.StringArray("reserved", true, allowEmpty: true, maxItemLength: 200);
        body.StringArray("conditions", true, allowEmpty: true, maxItemLength: 2000);
        string? basis = body.Ulid("basis", true);
        body.NoOtherKeys();
        if (grantee is not null && context.RequireReference("grantee", grantee, "principal") is null)
        {
            return;
        }

        context.RequireReference("basis", basis, "decision");
        if (!context.HasRole(context.Record.By, Roles.Operator))
        {
            context.Error("authority.author", "Only an operator can grant authority.");
        }
    }
}
