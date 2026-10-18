// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Hashing;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal sealed record PolicyKindRegistration(string Kind, int Version, string ImplementationDigest,
    ImmutableArray<string> Selections, ImmutableArray<string> SourceContracts, int MaximumRecords, int MaximumFindings,
    Action<PolicyRuleContext> Validate, Func<PolicyRuleContext, IReadOnlyList<PolicyFinding>> Evaluate);

// Registrations are supplied by admitted application composition. Declarations never load code. The
// adapter pins the digest of the implementation artifact it loaded; the evaluator only compares inputs.
internal sealed class PolicyKindRegistry(IEnumerable<PolicyKindRegistration> registrations)
{
    internal ImmutableArray<PolicyKindRegistration> Registrations { get; } = registrations.OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Version)
        .ThenBy(r => r.ImplementationDigest, StringComparer.Ordinal).ToImmutableArray();

    internal ImmutableArray<PolicyImplementation> Implementations => Registrations.Select(r => new PolicyImplementation(r.Kind, r.Version, r.ImplementationDigest)).ToImmutableArray();

    internal static PolicyKindRegistry Foundation(string implementationDigest) => new(new[]
    {
        FoundationPolicyKinds.Placement, FoundationPolicyKinds.Staleness, FoundationPolicyKinds.KeyFamily, FoundationPolicyKinds.RevisionLine, FoundationPolicyKinds.Consistency,
    }.Select(kind => new PolicyKindRegistration(kind, 1, implementationDigest,
        kind == FoundationPolicyKinds.RevisionLine ? ["repository", "product"] : ["estate", "product", "work-orders"],
        kind == FoundationPolicyKinds.RevisionLine ? [PolicyBasis.RepositoryContract] : [], 100000, 100000,
        FoundationPolicyKinds.Validate, FoundationPolicyKinds.Evaluate)).Append(new(Vericairn.Records.Types.DutyRecords.Kind, 1, implementationDigest,
            ["estate", "product"], [], 100000, RecurringDuty.MaximumOccurrences, FoundationPolicyKinds.Validate, RecurringDuty.Evaluate)));

    internal IReadOnlyList<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        string kind = String(context.Rule, "kind");
        long version = Integer(context.Rule, "version", 1);
        PolicyKindRegistration[] matches = Registrations.Where(r => r.Kind == kind && r.Version == version).ToArray();
        if (matches.Length != 1) return [context.Unavailable("The exact kind implementation is missing or registered more than once.")];
        PolicyKindRegistration registration = matches[0];
        PolicyImplementation[] pins = context.Basis.Implementations.Where(i => i.Kind == kind && i.Version == version).ToArray();
        if (pins.Length != 1 || pins[0].Digest != registration.ImplementationDigest || !ContentHash.IsValid(registration.ImplementationDigest))
            return [context.Unavailable("The exact implementation digest is missing, conflicting or different from the basis.")];
        if (!registration.Selections.Contains(Text(Get(context.Policy.Body, "appliesTo"), "kind") ?? "", StringComparer.Ordinal)) throw new PolicyParameterException("This rule version does not support the selected scope.");
        if (registration.MaximumRecords < 1 || registration.MaximumFindings < 1 || context.Basis.Records.Length > registration.MaximumRecords)
            return [context.Unavailable("The declared record history bound was exhausted; no partial evaluation is reported.")];
        if (registration.SourceContracts.Any(contract => !context.Basis.Repositories.Any(r => r.Contract == contract && r.Complete)))
            return [context.Unavailable("A required source contract is unavailable.")];
        registration.Validate(context);
        IReadOnlyList<PolicyFinding> findings = registration.Evaluate(context);
        if (findings.Count > registration.MaximumFindings) return [context.Unavailable("The declared finding bound was exhausted; no partial evaluation is reported.")];
        if (findings.Count == 0 || findings.Any(f => f.Policy != context.Policy.Reference || f.Rule != context.Key || f.Kind != kind || f.Version != version || f.Basis != context.Basis.Digest || f.AsOf != context.Basis.AsOf ||
            f.Verdict is not ("satisfied" or "unmet" or "unavailable") || f.Disposition is not ("none" or "scheduled" or "due-within-grace" or "discharged" or "discharged-late" or "ended-unmet") ||
            f.Disposition == "ended-unmet" && kind != Vericairn.Records.Types.DutyRecords.Kind || !f.Exceptions.IsEmpty || f.SubjectCount is < 0 or > 1 ||
            f.Enforced != context.Enforced || f.Level != Vericairn.Records.Estate.EstateModel.BodyString(context.Policy, "level") || f.Owner != Vericairn.Records.Estate.EstateModel.BodyString(context.Policy, "owner")) ||
            findings.GroupBy(f => (f.Target.Identity, f.Item)).Any(g => g.Count() != 1))
            return [context.Unavailable("The kind violated the finding contract; it cannot alter policy identity, level, owner, exceptions or item uniqueness.")];
        return findings;
    }

    // A second implementation is checked against complete canonical results, including errors, evidence,
    // counts and ordering. Vectors are explicit snapshots, so conformance has no source or clock callback.
    internal static bool Conforms(PolicyKindRegistry candidate, PolicyKindRegistry reference, IEnumerable<PolicyBasis> vectors)
    {
        PolicyBasis[] cases = vectors.ToArray();
        return cases.Length > 0 && cases.All(v => PolicyEvaluator.Evaluate(v, candidate).Canonical() == PolicyEvaluator.Evaluate(v, reference).Canonical());
    }
}
