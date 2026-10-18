// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static class PolicyRequirements
{
    private static readonly IReadOnlySet<string> DefinitionExcluded = new HashSet<string>(StringComparer.Ordinal) { "basis" };

    internal static string DefinitionHash(JsonElement body) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(body, DefinitionExcluded));

    internal static void Validate(ValidationContext context)
    {
        if (!context.Body.Since(3, "policies")) return;
        IReadOnlyList<BodyReader> requirements = context.Body.NestedArray("policies", false, false) ?? [];
        PolicyRecords.Unique(context.Body, requirements.Select(r => PolicyRecords.Text(r.Element, "key")), "policies");
        foreach (BodyReader requirement in requirements)
        {
            PolicyRecords.Key(requirement);
            string? action = requirement.Enum("action", true, "declare", "revise", "retire");
            if (action == "declare") PolicyRecords.Key(requirement, "policy");
            else PolicyRecords.IdentityField(context, requirement, "policy", "estate-policy");
            string? prior = requirement.Hash("priorHash", action is "revise" or "retire");
            if (action == "declare" && prior is not null) requirement.Error("policy.requirement", "priorHash", "A declaration has no prior revision.");
            requirement.Text("change", true, 4000);
            requirement.Hash("definitionHash", false);
            requirement.NoOtherKeys();
            if (prior is not null && !context.Model.OfType("estate-policy").Any(p => PolicyRecords.Identity(context.Model, p) == PolicyRecords.Text(requirement.Element, "policy") && p.ComputedHash == prior && string.CompareOrdinal(p.At, context.Record.At) <= 0))
                requirement.Error("policy.requirement", "priorHash", "The prior hash must name an existing revision of this policy.");
            if (requirement.Has("definitionHash") && (!PolicyAuthority.OperatorAt(context.Model, context.Record.By, context.Record.At) || context.Record.By != EstateModel.BodyString(context.Record, "decidedBy")))
                requirement.Error("policy.approval", "definitionHash", "An exact definition approval is authored and signed by the deciding operator.");
        }
    }

    internal static void Basis(ValidationContext context, string action, RecordEnvelope? previous, bool approvalRequired)
    {
        IReadOnlyList<BodyReader> basis = context.Body.NestedArray("basis", true, false) ?? [];
        PolicyRecords.Unique(context.Body, basis.Select(b => PolicyRecords.Text(PolicyRecords.Property(b.Element, "decision"), "id") + ":" + PolicyRecords.Text(b.Element, "requirement")), "basis");
        bool approved = false;
        BodyReader? authority = null;
        foreach (BodyReader entry in basis)
        {
            RecordEnvelope? decision = PolicyRecords.Reference(context, entry.Nested("decision", true), true, "decision");
            string? key = PolicyRecords.Key(entry, "requirement", false);
            BodyReader? grant = entry.Nested("authority", false);
            if (grant is not null)
            {
                if (authority is not null) entry.Error("policy.basis", "authority", "Name the one grant used for this action once.");
                authority = grant;
            }
            entry.NoOtherKeys();
            if (decision is null) continue;
            if (!SignatureChecks.IsAuthentic(context.Model, decision)) entry.Error("policy.basis", "decision", "The decision basis must be signed by an admitted device.");
            JsonElement requirement = PolicyRecords.Items(PolicyRecords.Property(decision.Body, "policies")).FirstOrDefault(r => PolicyRecords.Text(r, "key") == key);
            if (key is not null && requirement.ValueKind == JsonValueKind.Undefined) entry.Error("policy.requirement", "requirement", "The exact decision has no such requirement.");
            bool matches = PolicyRecords.Text(requirement, "action") == action &&
                PolicyRecords.Text(requirement, "policy") == (previous is null ? EstateModel.BodyString(context.Record, "key") : PolicyRecords.Identity(context.Model, previous)) &&
                PolicyRecords.Text(requirement, "priorHash") == previous?.ComputedHash && PolicyRecords.Text(requirement, "definitionHash") == DefinitionHash(context.Record.Body);
            approved |= matches && decision.By == EstateModel.BodyString(decision, "decidedBy") && PolicyAuthority.OperatorAt(context.Model, decision.By, decision.At) && SignatureChecks.IsAuthentic(context.Model, decision);
        }
        bool transfer = action == "revise" && previous is not null && !PolicyRecords.SameFields(previous, context.Record, "owner");
        string permissionAction = transfer && !approvalRequired ? "transfer" : action;
        PolicyAuthority.Grant(context, authority, Scopes.ManageEstatePolicies, permissionAction, context.Record);
        if (transfer && approvalRequired) PolicyAuthority.Grant(context, authority, Scopes.ManageEstatePolicies, "transfer", context.Record);
        if (approvalRequired && !approved) context.Error("policy.approval", "This definition requires an exact signed operator decision: matching requirement, action, prior hash and canonical body hash with only basis omitted.");
    }
}
