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
        // A review is duty evidence by its subject: a result with duties. Its read list alone does not make it one (FIX-94).
        "review" => IsDutyReview(model, record.Subject),
        // From 0.9.2 (FIX-100), settling operations that share an identity is signed as well.
        "reconciliation" => record.Subject is { } reference && model.ById.TryGetValue(reference.Id, out RecordEnvelope? target) &&
            (target.Type is "estate-policy" or "policy-exception" or "writing-exception" || target.Type == "authority" && IsPolicyGrant(model, target))
            || ReconciliationType.SettlesIdentity(model, record),
        _ => false,
    };

    /// <summary>Whether a review of this subject is duty evidence: its subject is a result revision that carries duties.</summary>
    internal static bool IsDutyReview(EstateModel model, RecordReference? subject) => subject is { Type: "result" } &&
        model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) && PolicyRecords.Property(result.Body, "duties").ValueKind != JsonValueKind.Undefined;

    internal static bool IsPolicyGrant(EstateModel model, RecordEnvelope record) => model.Chain(record).Select(id => model.ById[id]).Any(r =>
        ScopesWithBounds.Any(s => PolicyRecords.Contains(PolicyRecords.Property(r.Body, "scope"), s)));

    /// <summary>Whether a record's author acts as the operator for policy authority (<see cref="OperatorAt(EstateModel, string, string, RecordEnvelope?)"/>).</summary>
    internal static bool OperatorAt(EstateModel model, RecordEnvelope record) => OperatorAt(model, record.By, record.At, record);

    /// <summary>Whether a principal acts as the operator for policy authority at a time, with no record to place in signed history.</summary>
    internal static bool OperatorAt(EstateModel model, string by, string at) => OperatorAt(model, by, at, null);

    /// <summary>
    /// Set while a record is judged as it would stand had no removal come before the estate's history checkpoints could place the record
    /// earlier (<see cref="RemovedBeforeHistory"/>), with the removals that reading set aside.
    /// </summary>
    [ThreadStatic]
    private static List<RecordEnvelope>? beforeHistory;

    /// <summary>
    /// Whether a principal acts as the operator for policy authority at a time: the root operator always; another person when their line
    /// gives the operator role at that time and still gives it now, as policy authority reads principal records (<see cref="OperatorReplay"/>:
    /// only signed records widen, a removal keeps counting, and a former operator's appointment counts only where signed history places it
    /// before the removal). A person who has since lost the role acted as the operator only through a record that signed history places
    /// before the loss (<see cref="EstateTrust.WitnessedBefore"/>): one listed by a checkpoint that the root operator or a standing
    /// operator signed before the revision that took the role away. So a record dated back into the time a removed operator held the role,
    /// and written after, authorizes nothing.
    /// </summary>
    internal static bool OperatorAt(EstateModel model, string by, string at, RecordEnvelope? record)
    {
        string? root = model.CurrentEstate is { } estate ? EstateModel.BodyString(model.ById[PolicyRecords.Identity(model, estate)], "operator") : null;
        if (by == root && root is not null) return true;
        if (!model.ById.TryGetValue(by, out RecordEnvelope? principal) || principal.Type != "principal") return false;
        string line = model.Chain(principal)[0];
        EstateTrust trust = model.Trust;
        List<RecordEnvelope>? setAside = beforeHistory;
        OperatorReplay operators = setAside is null ? trust.PolicyOperators : trust.PolicyOperatorsBeforeHistory;
        if (!operators.OperatorAt(line, at)) return false;
        if (operators.StandingNow.Contains(line)) return true;
        if (record is null || operators.LossAfter(line, at) is not { } loss) return false;
        if (trust.WitnessedBefore(record, loss)) return true;
        if (setAside is null || !trust.BeforeHistory(record)) return false;
        setAside.Add(loss);
        return true;
    }

    /// <summary>The policy record types whose authority <see cref="OperatorAt(EstateModel, string, string, RecordEnvelope?)"/> reads.</summary>
    private static bool IsPolicyRecord(EstateModel model, RecordEnvelope record) =>
        record.Type is "estate-policy" or "policy-exception" or "writing-exception" or "prompt-preference" or "reconciliation" or "saved-prompt" ||
        record.Type == "authority" && IsPolicyGrant(model, record);

    /// <summary>
    /// The policy records whose own validation finds their approval or their authority missing: each counts for nothing, so no policy,
    /// exception, writing entry, prompt preference, shared prompt or reconciliation takes effect through an act its author could not perform.
    /// </summary>
    internal static IReadOnlySet<string> Unauthorized(EstateModel model)
    {
        var refused = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in model.CountingRecords.Where(r => r.Type is "estate-policy" or "policy-exception" or "writing-exception" or "prompt-preference" or "reconciliation" or "saved-prompt"))
        {
            if (RecordTypeRegistry.Find(record.Type) is not { } type || record.Format < type.SinceFormat) continue;
            var findings = new FindingList();
            type.Validate(new ValidationContext(model, record, findings));
            if (findings.Any(f => f.Severity == Severity.Error && f.Code is "policy.approval" or "policy.authority" or "prompt.authority")) refused.Add(record.Id);
        }

        return refused;
    }

    /// <summary>
    /// The policy records that fail only because an operator role they rest on was taken away before the estate's history checkpoints
    /// could place them earlier, each with the revisions that took it away: in an estate that has not adopted checkpoints, or for a record
    /// its adoption witnessed, a record whose own validation finds errors and finds none once such removals are set aside. Each counts for
    /// nothing and is reported as one warning, policy.operator-removed, rather than as errors, so the removal leaves the estate valid;
    /// a record written after adoption that no checkpoint places before the removal stays an error.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<RecordEnvelope>> RemovedBeforeHistory(EstateModel model)
    {
        var removed = new Dictionary<string, IReadOnlyList<RecordEnvelope>>(StringComparer.Ordinal);
        string? root = model.RootOperator;
        foreach (RecordEnvelope record in model.CountingRecords.Where(r => r.By != root && IsPolicyRecord(model, r)))
        {
            if (RecordTypeRegistry.Find(record.Type) is not { } type || record.Format < type.SinceFormat) continue;
            var findings = new FindingList();
            type.Validate(new ValidationContext(model, record, findings));
            if (!findings.HasErrors) continue;
            var losses = new List<RecordEnvelope>();
            var reading = new FindingList();
            beforeHistory = losses;
            try
            {
                type.Validate(new ValidationContext(model, record, reading));
            }
            finally
            {
                beforeHistory = null;
            }

            if (!reading.HasErrors && losses.Count > 0) removed[record.Id] = [.. losses.DistinctBy(l => l.Id).OrderBy(l => l.Id, StringComparer.Ordinal)];
        }

        return removed;
    }

    /// <summary>The warning a policy record that fails only because of a removal before history (<see cref="RemovedBeforeHistory"/>) is reported with.</summary>
    internal static Finding RemovedWarning(EstateModel model, RecordEnvelope record, IReadOnlyList<RecordEnvelope> losses)
    {
        string removals = string.Join("; ", losses.Select(l => $"{model.Chain(l)[0]} lost it by revision {l.Id}"));
        return new Finding(Severity.Warning, "policy.operator-removed", record.ExpectedPath,
            $"This {record.Type} record rests on an operator role that was taken away ({removals}) before any history checkpoint the root operator or a " +
            "standing operator signed placed the record earlier, so it counts for nothing. A history checkpoint written before a removal keeps such records: " +
            "adopt history checkpoints before removing a role, and the runtime writes one with each removal; the operator may write the change again.");
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
        if (lifecycle && !OperatorAt(context.Model, context.Record)) context.Error("authority.author", "Only the operator grants or revokes bounded policy authority.");
    }

    internal static RecordEnvelope? Grant(ValidationContext context, BodyReader? reference, string scope, string action, RecordEnvelope subject)
    {
        RecordEnvelope? grant = PolicyRecords.Reference(context, reference, true, "authority");
        if (OperatorAt(context.Model, context.Record) && reference is null) return null;
        bool valid = grant is not null && grant.Format >= 3 && EstateModel.BodyString(grant, "grantee") == context.Record.By &&
            string.CompareOrdinal(grant.At, context.Record.At) <= 0 &&
            PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "scope"), scope) && !EstateModel.Expired(grant, context.Record.AtTime) &&
            EstateModel.BodyString(grant, "expires") is not null && OperatorAt(context.Model, grant) && SignatureChecks.IsAuthentic(context.Model, grant) &&
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
