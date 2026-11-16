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
        // From 0.9.4 the policies also admit presence (below); this description stays as it was, since the charter's readable companion
        // renders it and its golden bytes are pinned to their generator's version.
        new("policies", "object", true, "signatures: optional|required; releaseAcceptance: roles that may accept a release."),
        new("organization", "object", false, "name and optional github organization login."),
        new("progressRequest", "object", false, "Default requested progress: optional types (accurate, best-effort, not-measurable; all by default), statusText (false by default), and cadence (perStep true, positive minutes, or both). More specific requests replace it.", 3),
    ];

    /// <summary>From 0.9.4 (ID-16): the presence an estate's charter asks of every person's device: per-signature, per-action, or null when it states none.</summary>
    public static string? PresencePolicy(Estate.EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model.RecordEnvelope? charter = model.Current("estate").FirstOrDefault();
        return charter is not null && charter.Body.TryGetProperty("policies", out System.Text.Json.JsonElement policies) && policies.ValueKind == System.Text.Json.JsonValueKind.Object
            && policies.TryGetProperty("presence", out System.Text.Json.JsonElement presence) && presence.ValueKind == System.Text.Json.JsonValueKind.String ? presence.GetString() : null;
    }

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
            // From 0.9.4 (ID-16), within record format 3: per-signature or per-action presence for every person's device in the estate,
            // the stricter of it and the device owner's setting winning (design/presence-sessions.md § 10). Additive, and refused by
            // readers that lack it, whose policies admit no other member, so an estate writes it once every writer has upgraded.
            if (policies.Since(3, "presence")) policies.Enum("presence", false, "per-signature", "per-action");
            policies.NoOtherKeys();
        }

        BodyReader? organization = body.Nested("organization", false);
        if (organization is not null)
        {
            organization.Text("name", true, 200);
            organization.Text("github", false, 100);
            organization.NoOtherKeys();
        }

        ProgressReporting.Request(body);
        body.NoOtherKeys();
        if (format is null || operatorId is null)
        {
            return;
        }

        if (context.Record.By != operatorId)
        {
            context.Error("estate.operator", "The estate record must be authored by its own operator principal.");
        }

        if (context.Record.Supersedes is not null && context.Model.RootOperator is { } rootOperator && operatorId != rootOperator)
        {
            context.Error("estate.operator", $"A charter revision keeps the root operator the first charter names ({rootOperator}); a revision cannot change it.");
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
