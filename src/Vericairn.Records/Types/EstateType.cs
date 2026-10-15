// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>The estate's charter: identity, purpose, the root operator and policies.</summary>
public sealed class EstateType : IRecordType
{
    /// <inheritdoc />
    public string Name => "estate";

    /// <inheritdoc />
    public string Summary => "The estate's charter: name, purpose, root operator principal and policies. Exactly one is current.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("name", "string", true, "Display name of the estate."),
        new("slug", "slug", true, "Lower-case kebab-case identifier used in paths and tooling."),
        new("purpose", "string", true, "What the estate exists to do, in one or two sentences."),
        new("operator", "ulid", true, "The root operator principal; the only principal allowed to author itself."),
        new("recordFormat", "integer", true, "The record format the estate was chartered in, at most the format of the charter record itself; the manifest states the format new records are written in."),
        new("policies", "object", true, "signatures: optional|required; releaseAcceptance: roles that may accept a release."),
        new("organization", "object", false, "name and optional github organization login."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("name", true, 200);
        body.Text("slug", true, 100, Patterns.Slug, "a lower-case kebab-case slug");
        body.Text("purpose", true, 2000);
        string? operatorId = body.Ulid("operator", true);
        long? format = body.Number("recordFormat", true, 1, context.Format);
        BodyReader? policies = body.Nested("policies", true);
        if (policies is not null)
        {
            policies.Enum("signatures", true, "optional", "required");
            policies.StringArray("releaseAcceptance", true, allowEmpty: false, maxItemLength: 50);
            policies.NoOtherKeys();
        }

        BodyReader? organization = body.Nested("organization", false);
        if (organization is not null)
        {
            organization.Text("name", true, 200);
            organization.Text("github", false, 100);
            organization.NoOtherKeys();
        }

        body.NoOtherKeys();
        if (format is null || operatorId is null)
        {
            return;
        }

        if (context.Record.By != operatorId)
        {
            context.Error("estate.operator", "The estate record must be authored by its own operator principal.");
        }

        Model.RecordEnvelope? operatorRecord = context.Principal(operatorId);
        if (operatorRecord is null)
        {
            context.Error("estate.operator", $"Operator principal {operatorId} has no principal record.");
        }
        else if (!context.HasRole(operatorId, Roles.Operator))
        {
            context.Error("estate.operator", "The operator principal must hold the operator role.");
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current("estate").Count() > 1)
        {
            context.Error("estate.multiple", "More than one current estate record exists; later charters must supersede the earlier one.");
        }
    }
}
