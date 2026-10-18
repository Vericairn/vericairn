// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Policy permissions are explicit, signed, bounded grants, checked at the action's recorded time.</summary>
internal static class PolicyAuthority
{
    internal static readonly string[] ScopesWithBounds = [Scopes.ManageEstatePolicies, Scopes.GrantPolicyExceptions, Scopes.ManageWritingExceptions];

    internal static bool RequiresSignature(EstateModel model, RecordEnvelope record) => record.Type switch
    {
        "estate-policy" or "policy-exception" or "writing-exception" => true,
        "authority" => IsPolicyGrant(model, record),
        "decision" => PolicyRecords.Property(record.Body, "policies").ValueKind != JsonValueKind.Undefined,
        "result" => PolicyRecords.Property(record.Body, "duties").ValueKind != JsonValueKind.Undefined,
        "review" => PolicyRecords.Property(record.Body, "read").ValueKind != JsonValueKind.Undefined || (record.Subject is { Type: "result" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) && PolicyRecords.Property(result.Body, "duties").ValueKind != JsonValueKind.Undefined),
        "reconciliation" => record.Subject is { } reference && model.ById.TryGetValue(reference.Id, out RecordEnvelope? target) &&
            (target.Type is "estate-policy" or "policy-exception" or "writing-exception" || target.Type == "authority" && IsPolicyGrant(model, target)),
        _ => false,
    };

    internal static bool IsPolicyGrant(EstateModel model, RecordEnvelope record) => model.Chain(record).Select(id => model.ById[id]).Any(r =>
        ScopesWithBounds.Any(s => PolicyRecords.Contains(PolicyRecords.Property(r.Body, "scope"), s)));

    internal static bool OperatorAt(EstateModel model, string by, string at)
    {
        string? root = model.CurrentEstate is { } estate ? EstateModel.BodyString(model.ById[PolicyRecords.Identity(model, estate)], "operator") : null;
        var checking = new HashSet<string>(StringComparer.Ordinal);
        return Trusted(by, at);

        bool Trusted(string author, string instant)
        {
            if (!model.ById.TryGetValue(author, out RecordEnvelope? principal) || principal.Type != "principal") return false;
            string identity = PolicyRecords.Identity(model, principal);
            List<RecordEnvelope> revisions = model.OfType("principal").Where(p => !checking.Contains(p.Id) && PolicyRecords.Identity(model, p) == identity && string.CompareOrdinal(p.At, instant) <= 0).ToList();
            List<RecordEnvelope> heads = revisions.Where(p => !revisions.Any(n => n.Supersedes == p.Id)).ToList();
            if (heads.Count != 1 || EstateModel.BodyString(heads[0], "kind") != "human" || !PolicyRecords.Contains(PolicyRecords.Property(heads[0].Body, "roles"), Roles.Operator)) return false;
            RecordEnvelope head = heads[0];
            // Only the charter's original operator bootstraps unsigned. An admitted key cannot appoint its own principal.
            if (head.Id == root && head.By == root && head.Supersedes is null) return true;
            if (!SignatureChecks.IsAuthentic(model, head)) return false;
            checking.Add(head.Id);
            bool trusted = Trusted(head.By, head.At);
            checking.Remove(head.Id);
            return trusted;
        }
    }

    internal static void ValidateGrant(ValidationContext context)
    {
        BodyReader body = context.Body;
        string[] governed = ScopesWithBounds.Where(s => PolicyRecords.Contains(PolicyRecords.Property(body.Element, "scope"), s)).ToArray();
        bool lifecycle = IsPolicyGrant(context.Model, context.Record);
        if (lifecycle && context.Format < 3) context.Error("authority.format", "Policy authority grants and their revocations require record format 3.");
        IReadOnlyList<BodyReader> bounds = body.Since(3, "bounds") ? body.NestedArray("bounds", governed.Length > 0, false) ?? [] : [];
        PolicyRecords.Unique(body, bounds.Select(b => PolicyRecords.Text(b.Element, "scope")), "bounds");
        foreach (BodyReader bound in bounds)
        {
            string? scope = bound.Enum("scope", true, ScopesWithBounds);
            string[] actions = scope switch
            {
                Scopes.ManageEstatePolicies => ["declare", "revise", "transfer", "retire", "reconcile"],
                Scopes.GrantPolicyExceptions => ["grant", "revoke", "reconcile"],
                _ => ["add", "remove", "reconcile"],
            };
            IReadOnlyList<string> allowed = bound.StringArray("actions", true, false, 30) ?? [];
            PolicyRecords.Unique(bound, allowed, "actions");
            if (allowed.Any(a => !actions.Contains(a, StringComparer.Ordinal))) bound.Error("authority.bounds", "actions", "An action does not belong to this scope.");
            if (scope is not null && !governed.Contains(scope, StringComparer.Ordinal)) bound.Error("authority.bounds", "scope", "Bounds belong to an explicitly granted scope.");
            if (scope == Scopes.ManageWritingExceptions)
            {
                IReadOnlyList<string>? tokens = bound.StringArray("tokens", false, false, 200);
                bool? all = bound.Flag("allTokens", false);
                if ((tokens is not null) == (all == true) || (all == false)) bound.Error("authority.bounds", "tokens", "Choose exact tokens or allTokens: true, never both.");
                foreach (string token in tokens ?? []) WritingExceptionType.ValidateToken(bound, "tokens", token);
                PolicyRecords.Unique(bound, tokens ?? [], "tokens");
                PolicyRecords.WritingCoverage(context, bound.Nested("coverage", false));
            }
            else
            {
                IReadOnlyList<string> policies = bound.StringArray("policies", true, false, 100) ?? [];
                PolicyRecords.Unique(bound, policies, "policies");
                foreach (string policy in policies)
                    if (!Patterns.Slug.IsMatch(policy) && !Records.Identity.Ulid.IsValid(policy)) bound.Error("authority.bounds", "policies", "Policies are exact keys or identities, never patterns.");
                IReadOnlyList<string> levels = bound.StringArray("levels", true, false, 20) ?? [];
                PolicyRecords.Unique(bound, levels, "levels");
                if (levels.Any(l => !PolicyRecords.Levels.Contains(l, StringComparer.Ordinal))) bound.Error("authority.bounds", "levels", "Unknown policy level.");
                if (scope == Scopes.ManageEstatePolicies)
                {
                    foreach (BodyReader selection in bound.NestedArray("appliesTo", true, false) ?? []) PolicyRecords.Selection(context, selection);
                }
                else
                {
                    IReadOnlyList<string> rules = bound.StringArray("rules", true, false, 100, Patterns.Slug) ?? [];
                    PolicyRecords.Unique(bound, rules, "rules");
                    bound.Number("maxExceptionDays", true, 1, 36500);
                    PolicyExceptionType.Targets(context, bound, "targets", null, null);
                }
            }
            bound.NoOtherKeys();
        }
        foreach (string scope in governed)
            if (!bounds.Any(b => PolicyRecords.Text(b.Element, "scope") == scope)) body.Error("authority.bounds", "bounds", $"Scope {scope} requires its own typed bounds.");
        if (governed.Length > 0)
        {
            if (!body.Has("expires")) body.Error("body.missing", "expires", "Policy grants have an explicit expiry.");
        }
        if (lifecycle && !OperatorAt(context.Model, context.Record.By, context.Record.At)) context.Error("authority.author", "Only the operator grants or revokes bounded policy authority.");
    }

    internal static RecordEnvelope? Grant(ValidationContext context, BodyReader? reference, string scope, string action, RecordEnvelope subject)
    {
        RecordEnvelope? grant = PolicyRecords.Reference(context, reference, true, "authority");
        if (OperatorAt(context.Model, context.Record.By, context.Record.At) && reference is null) return null;
        bool valid = grant is not null && grant.Format >= 3 && EstateModel.BodyString(grant, "grantee") == context.Record.By &&
            string.CompareOrdinal(grant.At, context.Record.At) <= 0 &&
            PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "scope"), scope) && !EstateModel.Expired(grant, context.Record.AtTime) &&
            EstateModel.BodyString(grant, "expires") is not null && OperatorAt(context.Model, grant.By, grant.At) && SignatureChecks.IsAuthentic(context.Model, grant) &&
            !context.Model.OfType("authority").Any(r => r.Id != grant.Id && PolicyRecords.Identity(context.Model, r) == PolicyRecords.Identity(context.Model, grant) &&
                context.Model.Chain(r).Contains(grant.Id, StringComparer.Ordinal) && string.CompareOrdinal(r.At, context.Record.At) <= 0);
        JsonElement bound = grant is null ? default : PolicyRecords.Items(PolicyRecords.Property(grant.Body, "bounds")).FirstOrDefault(b => PolicyRecords.Text(b, "scope") == scope);
        valid &= PolicyRecords.Contains(PolicyRecords.Property(bound, "actions"), action);
        if (grant is not null)
        {
            // Prose conditions cannot be silently treated as satisfied by a bounded grant.
            valid &= !PolicyRecords.Items(PolicyRecords.Property(grant.Body, "conditions")).Any();
            valid &= !PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "reserved"), scope) && !PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "reserved"), action);
        }
        if (scope == Scopes.ManageWritingExceptions)
        {
            valid &= PolicyRecords.Property(bound, "allTokens").ValueKind == JsonValueKind.True || PolicyRecords.Contains(PolicyRecords.Property(bound, "tokens"), EstateModel.BodyString(subject, "token"));
            valid &= PolicyRecords.CoverageContains(PolicyRecords.Property(bound, "coverage"), PolicyRecords.Property(subject.Body, "scope"));
        }
        else
        {
            valid &= PolicyRecords.Contains(PolicyRecords.Property(bound, "policies"), EstateModel.BodyString(subject, "key")) || PolicyRecords.Contains(PolicyRecords.Property(bound, "policies"), PolicyRecords.Identity(context.Model, subject));
            valid &= PolicyRecords.Contains(PolicyRecords.Property(bound, "levels"), EstateModel.BodyString(subject, "level"));
            if (scope == Scopes.ManageEstatePolicies)
                valid &= PolicyRecords.Items(PolicyRecords.Property(bound, "appliesTo")).Any(s => PolicyRecords.Equal(s, PolicyRecords.Property(subject.Body, "appliesTo")));
            else
            {
                valid &= PolicyRecords.Contains(PolicyRecords.Property(bound, "rules"), EstateModel.BodyString(context.Record, "rule"));
                JsonElement days = PolicyRecords.Property(bound, "maxExceptionDays");
                valid &= days.TryGetInt64Safe(out long limit) && limit is >= 1 and <= 36500 && PolicyExceptionType.Duration(context.Record) <= TimeSpan.FromDays(limit);
                valid &= PolicyRecords.Items(PolicyRecords.Property(context.Record.Body, "targets")).All(t => PolicyRecords.Items(PolicyRecords.Property(bound, "targets")).Any(b => PolicyRecords.Equal(t, b)));
            }
        }
        if (!valid) context.Error("policy.authority", $"The action requires operator authority or an authentic, unexpired {scope} grant whose typed bounds cover this exact action and subject.");
        return grant;
    }

    internal static void Reconcile(ValidationContext context, RecordEnvelope subject, RecordEnvelope? kept)
    {
        BodyReader? grant = context.Body.Nested("policyAuthority", false);
        if (kept is null) return;
        string scope = subject.Type switch
        {
            "estate-policy" => Scopes.ManageEstatePolicies,
            "policy-exception" => Scopes.GrantPolicyExceptions,
            _ => Scopes.ManageWritingExceptions,
        };
        RecordEnvelope boundSubject = subject.Type == "policy-exception" ? context.Find(kept.Subject?.Id) ?? kept : kept;
        // Bounds concern the kept definition, but attribution and the grant's validity concern the reconciliation time.
        var action = new RecordEnvelope(context.Record.Type, context.Record.Id, context.Record.At, context.Record.By, kept.Subject, kept.Supersedes, kept.Body, context.Record.DeclaredHash, context.Record.ComputedHash, context.Record.Signatures, context.Record.Format);
        Grant(new ValidationContext(context.Model, action, context.Findings), grant, scope, "reconcile", boundSubject);
    }

    internal static bool TryGetInt64Safe(this JsonElement value, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result);
    }
}
