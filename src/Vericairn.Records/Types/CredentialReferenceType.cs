// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A credential named by reference and never by its value, from record format 2 (the estate's design/identity-and-connectivity.md § 5).</summary>
public sealed class CredentialReferenceType : IRecordType
{
    /// <summary>Kinds of credential.</summary>
    public static readonly string[] Kinds = ["api-token", "oauth-client", "signing-certificate", "ssh-key", "password", "device-key", "other"];

    /// <summary>Whether a credential is in use.</summary>
    public static readonly string[] Statuses = ["active", "rotated", "revoked", "retired"];

    /// <inheritdoc />
    public string Name => "credential-reference";

    /// <inheritdoc />
    public string Summary => "A credential named by reference and never by its value: what it is for, who holds it, where it is kept, what it permits, how its presence is checked without using it, and when it is rotated. Records, briefs and logs carry references, never secrets.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("name", "slug", true, "The stable name it is referred to by; unique among current references.", 2),
        new("purpose", "string", true, "What it is for.", 2),
        new("kind", string.Join('|', Kinds), true, "What kind of credential it is.", 2),
        new("holder", "ulid", true, "The principal responsible for it.", 2),
        new("store", "string", true, "Where it is kept, described without its value or a machine path, for example a named keychain item or an Actions secret of a repository.", 2),
        new("scopes", "string[]", true, "What it permits; may be empty.", 2),
        new("check", "string", false, "How its presence is checked without using it; a presence check is not authentication.", 2),
        new("status", string.Join('|', Statuses), true, "active, rotated (a successor reference replaces it), revoked or retired.", 2),
        new("rotatedAt", "timestamp", false, "When it was last issued or rotated.", 2),
        new("rotateBy", "timestamp", false, "When it is due for rotation.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? name = body.Text("name", true, 100, Patterns.Slug, "a lower-case kebab-case name");
        body.Text("purpose", true, 2000);
        body.Enum("kind", true, Kinds);
        string? holder = body.Ulid("holder", true);
        body.Text("store", true, 500);
        body.StringArray("scopes", true, allowEmpty: true, maxItemLength: 200);
        body.Text("check", false, 1000);
        body.Enum("status", true, Statuses);
        string? rotatedAt = body.Timestamp("rotatedAt", false);
        string? rotateBy = body.Timestamp("rotateBy", false);
        body.NoOtherKeys();
        context.RequireReference("holder", holder, "principal");
        if (rotatedAt is not null && rotateBy is not null && string.CompareOrdinal(rotateBy, rotatedAt) <= 0)
        {
            body.Error("credential.rotation", "rotateBy", "A credential is due for rotation after it was issued.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("credential.author", $"Authoring principal {context.Record.By} has no principal record.");
        }

        if (name is not null && context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(other => other.Id != context.Record.Id && EstateModel.BodyString(other, "name") == name))
        {
            context.Error("credential.duplicate", $"Another current credential reference is named {name}.");
        }
    }
}
