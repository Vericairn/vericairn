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
        RecordEnvelope[] grants = basis.Model.Current("policy-exception").Where(g => Valid(basis, g)).OrderBy(g => g.Id, StringComparer.Ordinal).ToArray();
        foreach (PolicyFinding finding in findings)
        {
            if (finding.Verdict != "unmet" || finding.Disposition == "ended-unmet") { yield return finding; continue; }
            ImmutableArray<RecordReference> matches = grants.Where(g => g.Subject == finding.Policy && EstateModel.BodyString(g, "rule") == finding.Rule &&
                Items(Get(g.Body, "targets")).Any(t => TargetMatches(basis, t, finding))).Select(g => g.Reference).ToImmutableArray();
            yield return matches.Length == 0 ? finding : finding with { Disposition = "excepted", Exceptions = matches };
        }
    }

    private static bool Valid(PolicyBasis basis, RecordEnvelope grant)
    {
        if (basis.Time is not { } at || grant.AtTime > at || EstateModel.BodyString(grant, "state") != "granted" || DutyRecords.Time(EstateModel.BodyString(grant, "expiresAt")) is not { } expiry || at >= expiry ||
            !SignatureChecks.IsAuthentic(basis.Model, grant) || grant.Subject is not { Type: "estate-policy" } reference || !basis.Model.ById.TryGetValue(reference.Id, out RecordEnvelope? policy) || !basis.Model.IsCurrent(policy) || reference != policy.Reference) return false;
        var validation = new FindingList();
        new PolicyExceptionType().Validate(new ValidationContext(basis.Model, grant, validation));
        if (validation.HasErrors) return false;
        JsonElement delegated = Get(Get(grant.Body, "basis"), "authority");
        if (delegated.ValueKind == JsonValueKind.Undefined) return PolicyAuthority.OperatorAt(basis.Model, grant.By, grant.At);
        RecordEnvelope authority = Exact(basis.Model, delegated);
        // Authorship was validated above. A delegate additionally needs the same grant still in force at
        // evaluation; renewing or replacing that authority never silently renews the original exception.
        return basis.Model.IsCurrent(authority) && !EstateModel.Expired(authority, at) &&
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
            "file" => finding.Target.Kind == "file" && Text(target, "repository") == finding.Target.Repository && Text(target, "path") == finding.Target.Path && Text(target, "hash") == finding.Target.Hash,
            "occurrence" => finding.Duty is { } duty && ReferenceMatches(Get(target, "policy"), duty.Origin) && Text(target, "rule") == finding.Rule &&
                ReferenceMatches(Get(target, "subject"), finding.Target.Record) && PolicyRecords.Equal(Get(target, "occurrence"), duty.Occurrence),
            _ => false,
        };
    }

    internal static bool ReferenceMatches(JsonElement reference, RecordReference? expected) => expected is not null &&
        Text(reference, "type") == expected.Type && Text(reference, "id") == expected.Id && Text(reference, "hash") == expected.Hash;
}
