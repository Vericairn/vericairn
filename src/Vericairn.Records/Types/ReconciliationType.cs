// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Settles divergent revisions of one record (two clones each superseded it) by keeping one successor and withdrawing the others.</summary>
public sealed class ReconciliationType : IRecordType
{
    /// <inheritdoc />
    public string Name => "reconciliation";

    /// <inheritdoc />
    public string Summary => "Settles divergent revisions of one record by keeping one successor and withdrawing the others. Communication scopes also permit their owner to settle independent first profiles at that exact scope. Nothing is deleted.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kept", "ulid", true, "The successor of the subject that remains current, or the chosen first communication profile at that scope."),
        new("withdrawn", "ulid[]", true, "The other successors of the subject (or first communication profiles at the same scope), withdrawn in favor of the kept one."),
        new("note", "string", true, "Why this one is kept and what, if anything, from the others must be carried into a later revision."),
        new("policyAuthority", "reference", false, "Exact bounded grant used to reconcile policy or exception history; no generic engineering grant substitutes.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? kept = body.Ulid("kept", true);
        IReadOnlyList<string>? withdrawn = body.StringArray("withdrawn", true, allowEmpty: false, maxItemLength: Identity.Ulid.Length);
        body.Text("note", true, 4000);
        RecordEnvelope? subject = context.Find(context.Record.Subject?.Id);
        bool policy = subject?.Type is "estate-policy" or "policy-exception" or "writing-exception";
        bool policyGrant = subject?.Type == "authority" && PolicyAuthority.IsPolicyGrant(context.Model, subject);
        bool policyFormat = body.Since(3, "policyAuthority");
        if (policy)
        {
            if (!policyFormat) context.Error("policy.format", "Reconciling a policy or exception requires record format 3 and its bounded authority rules.");
            PolicyAuthority.Reconcile(context, subject!, context.Find(kept));
        }
        else if (policyFormat && body.Has("policyAuthority")) body.Error("policy.authority", "policyAuthority", "Only policy and exception reconciliation uses this grant.");
        if (policyGrant && !policyFormat) context.Error("policy.format", "Reconciling bounded policy authority requires record format 3.");
        body.Allow("policyAuthority");
        body.NoOtherKeys();
        bool communication = subject?.Type is "communication-setting" or "communication-delegation";
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("reconciliation.author", $"Reconciling principal {context.Record.By} has no principal record.");
        }
        else if (policy)
        {
            // The bounded policy check above replaces the general engineering permission.
        }
        else if (policyGrant)
        {
            if (!PolicyAuthority.OperatorAt(context.Model, context.Record.By, context.Record.At)) context.Error("policy.authority", "Only the operator reconciles bounded policy grants and their revocations.");
        }
        else if (communication)
        {
            // Choosing a branch changes the effective preference or consent just as a direct edit does.
            bool allowed = EstateModel.BodyString(subject!, "scope") == "estate"
                ? context.HasRole(context.Record.By, Roles.Operator) || context.HasScope(context.Record.By, Scopes.Administer)
                : context.Record.By == EstateModel.BodyString(subject!, "recipient");
            if (!allowed)
            {
                context.Error("communication.authority", "Only the recipient reconciles their personal communication choices or delegations; estate defaults require administration.");
            }
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
            else if (communication && !CommunicationSettingType.SameScope(subject!, successor))
            {
                context.Error("reconciliation.successor", $"{id} is not the same communication scope as {subjectId}.");
            }
            else if (successor.Supersedes != subjectId && !(communication && subject!.Supersedes is null && successor.Supersedes is null))
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
