// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

internal static class PolicyEvaluator
{
    internal static PolicyEvaluation Evaluate(PolicyBasis basis)
    {
        string? unavailable = basis.UnavailableReason();
        List<PolicyFinding> findings = [];
        List<Finding> parameterErrors = [];
        foreach (RecordEnvelope policy in basis.Model.Current("estate-policy"))
        {
            if (EstateModel.BodyString(policy, "state") != "active") continue;
            foreach (JsonElement rule in PolicyRecords.Items(PolicyRecords.Property(policy.Body, "rules")))
            {
                var context = new PolicyRuleContext(basis, policy, rule);
                if (unavailable is not null) findings.Add(context.Unavailable(unavailable));
                else if (DutyRecords.Time(EstateModel.BodyString(policy, "startsAt")) is not { } starts) findings.Add(context.Unavailable("The policy activation is unavailable."));
                else if (starts > basis.Time) findings.Add(context.Finding(new("policy", context.Identity, policy.Reference), "", "satisfied", "Policy activation is in the future.", "Activation at " + EstateModel.BodyString(policy, "startsAt"), "Wait for activation.", disposition: "scheduled", subjects: 0));
                else
                {
                    try { findings.AddRange(FoundationPolicyKinds.Evaluate(context)); }
                    catch (PolicyInputException error) { findings.Add(context.Unavailable(error.Message)); }
                    catch (PolicyParameterException error)
                    {
                        findings.Add(context.Unavailable(error.Message));
                        parameterErrors.Add(new(Severity.Error, "policy.parameters", policy.ExpectedPath + "#" + context.Key, error.Message));
                    }
                }
            }
        }
        ImmutableArray<PolicyFinding> ordered = findings.OrderBy(f => f.PolicyKey, StringComparer.Ordinal).ThenBy(f => f.Rule, StringComparer.Ordinal)
            .ThenBy(f => f.Target.Identity, StringComparer.Ordinal).ThenBy(f => f.Item, StringComparer.Ordinal).ThenBy(f => f.Policy.Id, StringComparer.Ordinal).ToImmutableArray();
        ImmutableArray<PolicyRuleSummary> rules = ordered.GroupBy(f => (f.Policy, f.Rule)).Select(g => new PolicyRuleSummary(g.Key.Policy, g.Key.Rule,
            g.Where(f => f.SubjectCount > 0).Select(f => f.Target.Identity).Distinct(StringComparer.Ordinal).Count(), g.Count(f => f.Verdict == "satisfied"), g.Count(f => f.Verdict == "unmet"),
            g.Count(f => f.Verdict == "unavailable"), g.Count(f => f.Disposition == "excepted"))).ToImmutableArray();
        ImmutableArray<Finding> diagnostics = basis.Findings.AddRange(parameterErrors.OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal));
        if (unavailable is not null) diagnostics = diagnostics.Add(new(Severity.Warning, "policy.basis-unavailable", "policy-basis", unavailable));
        return new(basis.Digest, basis.AsOf, basis.Origin, unavailable is null && ordered.All(f => f.Verdict != "unavailable"), diagnostics, ordered, rules);
    }
}

internal sealed class PolicyRuleContext(PolicyBasis basis, RecordEnvelope policy, JsonElement rule)
{
    internal PolicyBasis Basis { get; } = basis;
    internal EstateModel Model => Basis.Model;
    internal RecordEnvelope Policy { get; } = policy;
    internal JsonElement Rule { get; } = rule;
    internal JsonElement Parameters => PolicyRecords.Property(Rule, "parameters");
    internal string Identity => PolicyRecords.Identity(Model, Policy);
    internal string Key => PolicyRecords.Text(Rule, "key") ?? "?";

    internal PolicyFinding Finding(PolicyTarget target, string item, string verdict, string actual, string required, string nextAction,
        RecordEnvelope? anchor = null, IEnumerable<RecordReference>? evidence = null, string disposition = "none", int subjects = 1) =>
        new(Identity, Policy.Reference, EstateModel.BodyString(Policy, "key") ?? "?", Key, PolicyRecords.Text(Rule, "kind") ?? "?",
            PolicyRecords.Property(Rule, "version").TryGetInt32(out int version) ? version : 0, target, item, verdict, disposition,
            EstateModel.BodyString(Policy, "level") ?? "refuse", EstateModel.BodyString(Policy, "owner") ?? "?", Basis.Digest, Basis.AsOf,
            actual, required, nextAction, anchor?.Reference, anchor?.At,
            (evidence ?? []).Distinct().OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Hash, StringComparer.Ordinal).ToImmutableArray(), [] , subjects);

    internal PolicyFinding Unavailable(string reason) => Finding(new("policy", Identity, Policy.Reference), "", "unavailable", reason,
        "Complete, valid inputs and an installed implementation of this exact rule version.", "Repair the named input and evaluate again.", subjects: 0);
}
