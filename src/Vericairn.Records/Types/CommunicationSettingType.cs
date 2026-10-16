// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An attributable presentation profile; an empty values object resets its scope to inheritance.</summary>
public sealed class CommunicationSettingType : IRecordType
{
    /// <inheritdoc />
    public string Name => "communication-setting";
    /// <inheritdoc />
    public string Summary => "Estate defaults or a person's estate or assignment communication values, with an append-only revision history.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 2;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("scope", "estate|person|assignment", true, "The scope being revised."),
        new("recipient", "ulid", false, "The person whose choices these are; required outside estate defaults."),
        new("assignment", "ulid", false, "Required only for an assignment override."),
        new("values", "object", true, "Optional progress (none|completion-and-need|milestones|verbose), status, decision, result and handoff (short|standard|comprehensive). Omission inherits."),
        new("authority", "ulid", false, "Explicit communication-delegation for a change by another principal."),
        new("authorityHash", "hash", false, "Exact delegation revision used."),
        new("basis", "ulid", false, "Existing decision explicitly adopted; never inferred from conversation."),
        new("basisHash", "hash", false, "Exact decision revision adopted."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? scope = body.Enum("scope", true, ["estate", "person", "assignment"]);
        string? recipient = body.Ulid("recipient", scope is "person" or "assignment");
        string? assignment = body.Ulid("assignment", scope == "assignment");
        BodyReader? values = body.Nested("values", true);
        if (values is not null)
        {
            values.Enum("progress", false, ["none", "completion-and-need", "milestones", "verbose"]);
            foreach (string field in new[] { "status", "decision", "result", "handoff" })
            {
                values.Enum(field, false, ["short", "standard", "comprehensive"]);
            }

            values.NoOtherKeys();
        }

        string? authority = body.Ulid("authority", false);
        string? authorityHash = body.Hash("authorityHash", authority is not null);
        string? basis = body.Ulid("basis", false);
        string? basisHash = body.Hash("basisHash", basis is not null);
        body.NoOtherKeys();
        if ((scope == "estate" && recipient is not null) || (scope != "assignment" && assignment is not null))
        {
            context.Error("communication.scope", "Estate defaults have no recipient; only assignment overrides name an assignment.");
        }

        if (recipient is not null && context.Principal(recipient) is { } person && EstateModel.BodyString(person, "kind") != "human")
        {
            context.Error("communication.person", "Communication values belong to a person, not an agent.");
        }

        context.RequireReference("recipient", recipient, "principal");
        context.RequireReference("assignment", assignment, "assignment");
        RecordEnvelope? decision = context.RequireReference("basis", basis, "decision");
        if ((decision is not null && (decision.ComputedHash != basisHash || EstateModel.BodyString(decision, "decidedBy") != recipient)) || (basis is null && basisHash is not null))
        {
            context.Error("communication.basis", "Adoption pins the exact decision of the recipient.");
        }

        if (scope == "estate")
        {
            if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
            {
                context.Error("communication.authority", "Only an estate administrator sets estate defaults.");
            }
        }
        else if (context.Record.By != recipient && !CommunicationDelegationType.Authorizes(context, authority, authorityHash))
        {
            context.Error("communication.authority", "A change belongs to its recipient or an explicit, unexpired delegate for this exact scope; being an operator or assignment author does not imply consent.");
        }
        else if (authority is not null && !CommunicationDelegationType.Authorizes(context, authority, authorityHash))
        {
            context.Error("communication.authority", "The named delegation does not authorize this change.");
        }

        if (authority is null && authorityHash is not null)
        {
            context.Error("communication.authority", "An authority hash requires its delegation id.");
        }

        ValidateRevision(context, Name, ["scope", "recipient", "assignment"]);
    }

    internal static void ValidateRevision(ValidationContext context, string type, IReadOnlyList<string> keys)
    {
        bool Same(RecordEnvelope record) => keys.All(k => EstateModel.BodyString(record, k) == EstateModel.BodyString(context.Record, k));
        if (context.Record.Supersedes is string prior && context.Find(prior) is { } previous && !Same(previous))
        {
            context.Error("communication.scope", "A revision keeps its recipient and scope; reset the old scope separately.");
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current(type).Any(r => r.Id != context.Record.Id && Same(r)))
        {
            context.Error("communication.conflict", "Several current revisions exist for this communication scope; reconcile them explicitly, never select the last arrival.");
        }
    }
}
