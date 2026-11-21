// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal static class PolicyExceptions
{
    internal static IEnumerable<PolicyFinding> Apply(PolicyBasis basis, IEnumerable<PolicyFinding> findings)
    {
        if (basis.UnavailableReason() is not null)
        {
            foreach (PolicyFinding finding in findings) yield return finding;
            yield break;
        }
        RecordEnvelope[] grants = basis.Time is { } at ? basis.Model.Current("policy-exception").Where(g => Valid(basis.Model, at, g)).OrderBy(g => g.Id, StringComparer.Ordinal).ToArray() : [];
        foreach (PolicyFinding finding in findings)
        {
            if (finding.Verdict != "unmet" || finding.Disposition == "ended-unmet") { yield return finding; continue; }
            ImmutableArray<RecordReference> matches = grants.Where(g => g.Subject == finding.Policy && EstateModel.BodyString(g, "rule") == finding.Rule &&
                Items(Get(g.Body, "targets")).Any(t => TargetMatches(basis, t, finding))).Select(g => g.Reference).ToImmutableArray();
            yield return matches.Length == 0 ? finding : finding with { Disposition = "excepted", Exceptions = matches };
        }
    }

    /// <summary>
    /// The valid exceptions that pin this exact pair of assignment revisions under one rule of one policy at an instant (from 0.9.4,
    /// POLICY-08): the write boundary applies them at assignment start as the evaluator applies them to an unmet pair's finding.
    /// </summary>
    internal static ImmutableArray<RecordReference> ForPair(EstateModel model, DateTimeOffset at, RecordEnvelope policy, string rule, IReadOnlyCollection<RecordReference> revisions) =>
        [.. model.Current("policy-exception").Where(g => g.Subject == policy.Reference && EstateModel.BodyString(g, "rule") == rule
                && Items(Get(g.Body, "targets")).Any(t => Get(t, "submission").ValueKind == JsonValueKind.Undefined && PairMatches(t, revisions)) && Valid(model, at, g))
            .OrderBy(g => g.Id, StringComparer.Ordinal).Select(g => g.Reference)];

    /// <summary>Whether a pair target names exactly these two assignment revisions, in either order.</summary>
    internal static bool PairMatches(JsonElement target, IReadOnlyCollection<RecordReference> revisions)
    {
        JsonElement[] named = [.. Items(Get(target, "assignments"))];
        return Text(target, "kind") == "pair" && revisions.Count == 2 && named.Length == 2 && revisions.All(r => r.Type == "assignment" && named.Count(n => ReferenceMatches(n, r)) == 1);
    }

    private static bool Valid(EstateModel model, DateTimeOffset at, RecordEnvelope grant)
    {
        if (grant.AtTime > at || EstateModel.BodyString(grant, "state") != "granted" || DutyRecords.Time(EstateModel.BodyString(grant, "expiresAt")) is not { } expiry || at >= expiry ||
            !SignatureChecks.IsAuthentic(model, grant) || grant.Subject is not { Type: "estate-policy" } reference || !model.ById.TryGetValue(reference.Id, out RecordEnvelope? policy) || !model.IsCurrent(policy) || reference != policy.Reference) return false;
        var validation = new FindingList();
        new PolicyExceptionType().Validate(new ValidationContext(model, grant, validation));
        if (validation.HasErrors) return false;
        JsonElement delegated = Get(Get(grant.Body, "basis"), "authority");
        if (delegated.ValueKind == JsonValueKind.Undefined) return PolicyAuthority.OperatorAt(model, grant);
        RecordEnvelope authority;
        try { authority = Exact(model, delegated); }
        catch (PolicyInputException) { return false; }
        // Authorship was validated above. A delegate additionally needs the same grant still in force at
        // evaluation; renewing or replacing that authority never silently renews the original exception.
        return model.IsCurrent(authority) && !EstateModel.Expired(authority, at) &&
            PolicyRecords.Contains(Get(authority.Body, "scope"), Scopes.GrantPolicyExceptions);
    }

    internal static bool TargetMatches(PolicyBasis basis, JsonElement target, PolicyFinding finding)
    {
        JsonElement submission = Get(target, "submission");
        if (submission.ValueKind != JsonValueKind.Undefined)
        {
            if (basis.Submission is not { } proposed || !ReferenceMatches(Get(submission, "release"), proposed.Release) || Text(submission, "product") != proposed.Product || Text(submission, "version") != proposed.Version) return false;
            RecordEnvelope release = Exact(basis.Model, Get(submission, "release"));
            if (EstateModel.BodyString(release, "commit") != proposed.Commit) return false;
        }
        else if (basis.Submission is not null) return false;
        return Text(target, "kind") switch
        {
            "record" => finding.Target.Kind == "record" && ReferenceMatches(Get(target, "record"), finding.Target.Record),
            "publication" => finding.Target.Kind == "publication" && ReferenceMatches(Get(target, "publication"), finding.Target.Record),
            // A pair of assignments (0.9.4, POLICY-08) is excepted only at its exact two revisions, which its finding names as evidence.
            "pair" => finding.Target.Kind == "pair" && PairMatches(target, [.. finding.Evidence.Where(e => e.Type == "assignment")]),
            "file" => finding.Target.Kind == "file" && Text(target, "repository") == finding.Target.Repository && Text(target, "path") == finding.Target.Path && Text(target, "hash") == finding.Target.Hash,
            "occurrence" => finding.Duty is { } duty && ReferenceMatches(Get(target, "policy"), duty.Origin) && Text(target, "rule") == finding.Rule &&
                ReferenceMatches(Get(target, "subject"), finding.Target.Record) && PolicyRecords.Equal(Get(target, "occurrence"), duty.Occurrence),
            _ => false,
        };
    }

    internal static bool ReferenceMatches(JsonElement reference, RecordReference? expected) => expected is not null &&
        Text(reference, "type") == expected.Type && Text(reference, "id") == expected.Id && Text(reference, "hash") == expected.Hash;
}
