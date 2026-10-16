// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A person's explicit, revocable grant to change one scope of their presentation values.</summary>
public sealed class CommunicationDelegationType : IRecordType
{
    /// <inheritdoc />
    public string Name => "communication-delegation";
    /// <inheritdoc />
    public string Summary => "A person delegates changes to their own communication setting at one scope; no other authority is granted.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 2;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("recipient", "ulid", true, "The person granting authority; must be the record's author."),
        new("grantee", "ulid", true, "The principal allowed to change this scope."),
        new("scope", "person|assignment", true, "The personal scope delegated."),
        new("assignment", "ulid", false, "Required only for the assignment scope."),
        new("status", "active|revoked", true, "Revocation supersedes the grant; earlier authorized changes remain valid."),
        new("expires", "timestamp", false, "No change at or after this time is authorized."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? recipient = body.Ulid("recipient", true);
        string? grantee = body.Ulid("grantee", true);
        string? scope = body.Enum("scope", true, ["person", "assignment"]);
        string? assignment = body.Ulid("assignment", scope == "assignment");
        body.Enum("status", true, ["active", "revoked"]);
        string? expires = body.Timestamp("expires", false);
        body.NoOtherKeys();
        context.RequireReference("recipient", recipient, "principal");
        context.RequireReference("grantee", grantee, "principal");
        context.RequireReference("assignment", assignment, "assignment");
        if (recipient != context.Record.By || context.Principal(recipient) is not { } person || EstateModel.BodyString(person, "kind") != "human")
        {
            context.Error("communication.authority", "Only the person whose setting this is may delegate its changes.");
        }

        if (scope != "assignment" && assignment is not null)
        {
            context.Error("communication.scope", "Only assignment delegations name an assignment.");
        }

        if (expires is not null && string.CompareOrdinal(expires, context.Record.At) <= 0)
        {
            context.Error("communication.expires", "A grant expires after it is authored.");
        }

        CommunicationSettingType.ValidateRevision(context, Name, ["recipient", "grantee", "scope", "assignment"]);
    }

    internal static bool Authorizes(ValidationContext context, string? id, string? hash)
    {
        if (context.Find(id) is not { Type: "communication-delegation" } grant || grant.ComputedHash != hash ||
            EstateModel.BodyString(grant, "status") != "active" || EstateModel.BodyString(grant, "grantee") != context.Record.By ||
            grant.AtTime > context.Record.AtTime || EstateModel.Expired(grant, context.Record.AtTime))
        {
            return false;
        }

        foreach (string key in new[] { "recipient", "scope", "assignment" })
        {
            if (EstateModel.BodyString(grant, key) != EstateModel.BodyString(context.Record, key))
            {
                return false;
            }
        }

        // Revoking authority affects subsequent changes, never the validity of already recorded history.
        return !context.Model.OfType("communication-delegation").Any(r => r.Id != grant.Id && context.Model.Chain(r).Contains(grant.Id, StringComparer.Ordinal) &&
            (r.AtTime < context.Record.AtTime || (r.AtTime == context.Record.AtTime && string.CompareOrdinal(r.Id, context.Record.Id) < 0)));
    }
}
