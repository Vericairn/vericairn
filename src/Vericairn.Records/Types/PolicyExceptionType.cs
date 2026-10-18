// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A finite, signed exception to an exact policy revision, never completion evidence.</summary>
public sealed class PolicyExceptionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "policy-exception";
    /// <inheritdoc />
    public string Summary => "An attributable grant for exact finite targets of one policy rule, with exclusive expiry and signed revocation; it neither accepts work nor discharges debt.";
    /// <inheritdoc />
    public string? SubjectType => "estate-policy";
    /// <inheritdoc />
    public bool SubjectHashRequired => true;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("rule", "slug", true, "The rule in the exact subject policy.", 3),
        new("targets", "object[]", true, "Nonempty finite record, publication, file or occurrence targets; optional exact release submission.", 3),
        new("reason", "string", true, "Why this grant is needed.", 3),
        new("expiresAt", "timestamp", true, "Exclusive expiry, bounded from the initial grant time.", 3),
        new("basis", "object", true, "Exact decision and, for a delegate, authority reference.", 3),
        new("state", "granted|revoked", true, "A revocation is an immediate signed successor; renewal is a new identity.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? rule = PolicyRecords.Key(body, "rule");
        RecordEnvelope? policy = context.Find(context.Record.Subject?.Id);
        if (policy?.Type != "estate-policy") policy = null;
        Targets(context, body, "targets", policy, rule);
        body.Text("reason", true, 4000);
        string? expires = body.Timestamp("expiresAt", true);
        string? state = body.Enum("state", true, "granted", "revoked");
        BodyReader? basis = body.Nested("basis", true);
        BodyReader? authority = null;
        if (basis is not null)
        {
            RecordEnvelope? decision = PolicyRecords.Reference(context, basis.Nested("decision", true), true, "decision");
            if (decision is not null && !SignatureChecks.IsAuthentic(context.Model, decision)) basis.Error("policy.basis", "decision", "The decision basis must be signed by an admitted device.");
            authority = basis.Nested("authority", false);
            basis.NoOtherKeys();
        }
        body.NoOtherKeys();
        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        if (previous is null && state != "granted") context.Error("exception.lifecycle", "An exception starts as a grant.");
        if (previous is not null && (EstateModel.BodyString(previous, "state") != "granted" || state != "revoked" || previous.Subject != context.Record.Subject || !PolicyRecords.SameFields(previous, context.Record, "rule", "targets", "expiresAt"))) context.Error("exception.lifecycle", "Only a signed revocation can succeed a grant; its policy, rule, targets and expiry stay fixed. Renewal is a new grant.");
        if (policy is null) return;
        JsonElement definition = DutyRecords.Rule(policy, rule);
        if (definition.ValueKind == JsonValueKind.Undefined) body.Error("exception.rule", "rule", "The exact policy has no such rule.");
        if (state == "granted" && (EstateModel.BodyString(policy, "state") != "active" || string.CompareOrdinal(policy.At, context.Record.At) > 0 || context.Model.OfType("estate-policy").Any(p => p.Supersedes == policy.Id && string.CompareOrdinal(p.At, context.Record.At) <= 0))) context.Error("exception.policy", "A grant names an active policy revision current when it is authored.");
        if (DutyRecords.Time(expires) is { } expiry)
        {
            RecordEnvelope first = context.Model.ById[PolicyRecords.Identity(context.Model, context.Record)];
            if (!PolicyRecords.Property(policy.Body, "maxExceptionDays").TryGetInt64Safe(out long maximum) || maximum is < 1 or > 36500 || expiry <= first.AtTime || expiry - first.AtTime > TimeSpan.FromDays(maximum)) body.Error("exception.expiry", "expiresAt", "Expiry must follow the grant and fit the policy's maximum duration.");
        }
        PolicyAuthority.Grant(context, authority, Scopes.GrantPolicyExceptions, state == "revoked" ? "revoke" : "grant", policy);
        if (!PolicyAuthority.OperatorAt(context.Model, context.Record.By, context.Record.At))
        {
            RecordEnvelope first = context.Model.ById[PolicyRecords.Identity(context.Model, context.Record)];
            if (state == "revoked" && context.Record.By != first.By) context.Error("exception.revoker", "Only the operator or the still-authorized original grantor revokes an exception.");
            if (EstateModel.BodyString(policy, "owner") == context.Record.By || PolicyRecords.Items(PolicyRecords.Property(body.Element, "targets")).Any(t => OwnTarget(context, t))) context.Error("exception.self", "A delegate cannot except its own work or a condition imposed on it; the operator decides that case.");
        }
    }

    internal static TimeSpan Duration(RecordEnvelope record) => DutyRecords.Time(EstateModel.BodyString(record, "expiresAt")) is { } end ? end - record.AtTime : TimeSpan.MaxValue;

    internal static void Targets(ValidationContext context, BodyReader body, string field, RecordEnvelope? policy, string? rule)
    {
        IReadOnlyList<BodyReader> targets = body.NestedArray(field, true, false) ?? [];
        PolicyRecords.Unique(body, targets.Select(t => CanonicalJson.Serialize(t.Element)), field);
        foreach (BodyReader target in targets)
        {
            string? kind = target.Enum("kind", true, "record", "publication", "file", "occurrence");
            if (policy is not null && DutyRecords.IsDuty(DutyRecords.Rule(policy, rule)) && kind != "occurrence") target.Error("exception.occurrence", "kind", "A duty exception enumerates exact occurrences.");
            switch (kind)
            {
                case "record": PolicyRecords.Reference(context, target.Nested("record", true)); break;
                case "publication":
                    RecordEnvelope? publication = PolicyRecords.Reference(context, target.Nested("publication", true), true, "release");
                    if (publication is not null && EstateModel.BodyString(publication, "status") != "published") target.Error("exception.publication", "publication", "A publication target pins an observed publication.");
                    break;
                case "file":
                    PolicyRecords.Repository(context, target);
                    target.Text("path", true, 500, Patterns.RelativePath);
                    target.Hash("hash", true);
                    break;
                case "occurrence":
                    RecordEnvelope? origin = PolicyRecords.Reference(context, target.Nested("policy", true), true, "estate-policy");
                    string? targetRule = PolicyRecords.Key(target, "rule");
                    RecordEnvelope? subject = PolicyRecords.Reference(context, target.Nested("subject", true), true, "estate", "product", "resource", "device", "credential-reference");
                    BodyReader? occurrence = target.Nested("occurrence", true);
                    if (origin is not null)
                    {
                        DutyRecords.Occurrence(context, occurrence, origin, targetRule, subject);
                        if (policy is not null && (!context.Model.Chain(policy).Contains(origin.Id, StringComparer.Ordinal) || targetRule != rule)) target.Error("exception.occurrence", "policy", "The occurrence belongs to this policy lineage and rule.");
                    }
                    break;
            }
            if (target.Nested("submission", false) is { } submission)
            {
                RecordEnvelope? release = PolicyRecords.Reference(context, submission.Nested("release", true), true, "release");
                RecordEnvelope? product = PolicyRecords.IdentityField(context, submission, "product", "product");
                string? version = submission.Text("version", true, 100, Patterns.SemanticVersion);
                if (release is not null && (release.Subject?.Id != product?.Id || EstateModel.BodyString(release, "version") != version)) submission.Error("exception.submission", "release", "Submission product and exact version must agree with its pinned release.");
                submission.NoOtherKeys();
            }
            target.NoOtherKeys();
        }
    }

    private static bool OwnTarget(ValidationContext context, JsonElement target)
    {
        foreach (string field in new[] { "record", "subject", "publication" })
        {
            RecordEnvelope? record = context.Find(PolicyRecords.Text(PolicyRecords.Property(target, field), "id"));
            if (record is null) continue;
            if (record.By == context.Record.By || EstateModel.BodyString(record, "contributor") == context.Record.By || EstateModel.BodyString(record, "owner") == context.Record.By || EstateModel.BodyString(record, "principal") == context.Record.By || EstateModel.BodyString(record, "holder") == context.Record.By) return true;
            if (record.Type == "result" && WorkView.OrderOfResult(context.Model, record) is { } order && context.Model.OfType("assignment").Any(a => a.Subject?.Id == order.Id && EstateModel.BodyString(a, "contributor") == context.Record.By)) return true;
        }
        if (PolicyRecords.Text(target, "kind") == "file")
        {
            // A file target has no author field. Only signed output claims for this repository can establish provenance.
            string? repository = PolicyRecords.Text(target, "repository");
            List<RecordEnvelope> producers = context.Model.OfType("result").Where(r => string.CompareOrdinal(r.At, context.Record.At) <= 0 && SignatureChecks.IsAuthentic(context.Model, r) &&
                PolicyRecords.Items(PolicyRecords.Property(r.Body, "outputs")).Any(o => PolicyRecords.Text(o, "hash") == PolicyRecords.Text(target, "hash") && PolicyRecords.Text(o, "location") == PolicyRecords.Text(target, "path")) &&
                (context.Model.CurrentEstate is { } estate && repository == PolicyRecords.Identity(context.Model, estate) || PolicyRecords.Items(PolicyRecords.Property(r.Body, "duties")).Any(d =>
                    PolicyRecords.Items(PolicyRecords.Property(PolicyRecords.Property(d, "scope"), "repositories")).Any(s => context.Find(PolicyRecords.Text(PolicyRecords.Property(s, "record"), "id")) is { } resource && PolicyRecords.Identity(context.Model, resource) == repository)))).ToList();
            return producers.Count == 0 || producers.Any(r => r.By == context.Record.By);
        }
        return false;
    }
}
