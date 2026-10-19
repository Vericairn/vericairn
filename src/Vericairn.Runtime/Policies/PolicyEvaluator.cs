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
    internal static PolicyEvaluation Evaluate(PolicyBasis basis, PolicyKindRegistry? registry = null)
    {
        string? unavailable = basis.UnavailableReason();
        List<PolicyFinding> findings = [];
        List<Finding> parameterErrors = [];
        foreach (RecordEnvelope policy in basis.Model.Current("estate-policy"))
        {
            bool active = EstateModel.BodyString(policy, "state") == "active";
            JsonElement[] currentRules = PolicyRecords.Items(PolicyRecords.Property(policy.Body, "rules")).ToArray();
            JsonElement[] historicalDuties = basis.Model.Chain(policy).Select(id => basis.Model.ById[id]).SelectMany(p => PolicyRecords.Items(PolicyRecords.Property(p.Body, "rules")))
                .Where(DutyRecords.IsDuty).GroupBy(r => PolicyRecords.Text(r, "key"), StringComparer.Ordinal).Select(g => g.Last())
                .Where(r => !active || !currentRules.Any(c => PolicyRecords.Text(c, "key") == PolicyRecords.Text(r, "key") && DutyRecords.IsDuty(c))).ToArray();
            foreach (JsonElement rule in (active ? currentRules : []).Concat(historicalDuties))
            {
                var context = new PolicyRuleContext(basis, policy, rule);
                if (unavailable is not null) findings.Add(context.Unavailable(unavailable));
                else if (DutyRecords.Time(EstateModel.BodyString(policy, "startsAt")) is null) findings.Add(context.Unavailable("The policy activation is unavailable."));
                else
                {
                    try { findings.AddRange(registry is null ? [context.Unavailable("The exact rule kind and version are not installed.")] : registry.Evaluate(context)); }
                    catch (PolicyInputException error) { findings.Add(context.Unavailable(error.Message)); }
                    catch (PolicyParameterException error)
                    {
                        findings.Add(context.Unavailable(error.Message));
                        parameterErrors.Add(new(Severity.Error, "policy.parameters", PolicyInputs.RecordPath(policy) + "#" + context.Key, error.Message));
                    }
                }
            }
        }
        if (basis.Submission is { } submission)
            findings = findings.Select(f => f with { Enforced = f.Enforced && Applies(basis.Model, basis.Model.ById[f.Policy.Id], submission) }).ToList();
        ImmutableArray<PolicyFinding> ordered = PolicyExceptions.Apply(basis, findings).OrderBy(f => f.PolicyKey, StringComparer.Ordinal).ThenBy(f => f.Rule, StringComparer.Ordinal)
            .ThenBy(f => f.Target.Identity, StringComparer.Ordinal).ThenBy(f => f.Item, StringComparer.Ordinal).ThenBy(f => f.Policy.Id, StringComparer.Ordinal).ToImmutableArray();
        ImmutableArray<PolicyRuleSummary> rules = ordered.GroupBy(f => (f.Policy, f.Rule)).Select(g => new PolicyRuleSummary(g.Key.Policy, g.Key.Rule,
            g.Where(f => f.SubjectCount > 0).Select(f => f.Target.Identity).Distinct(StringComparer.Ordinal).Count(), g.Count(f => f.Verdict == "satisfied"), g.Count(f => f.Verdict == "unmet"),
            g.Count(f => f.Verdict == "unavailable"), g.Count(f => f.Disposition == "excepted"))).ToImmutableArray();
        ImmutableArray<Finding> diagnostics = basis.Findings.AddRange(parameterErrors.OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal));
        if (unavailable is not null) diagnostics = diagnostics.Add(new(Severity.Warning, "policy.basis-unavailable", "policy-basis", unavailable));
        PolicyRequirement[] requirements = Requirements(basis).ToArray();
        return new(basis.Digest, basis.AsOf, basis.Origin, unavailable is null && ordered.All(f => f.Verdict != "unavailable"), diagnostics, ordered, rules)
        { Requirements = requirements.Length == 0 ? null : requirements };
    }

    // A decision requirement tracks whether its exact approved change was recorded. It supplies no rule or release hold.
    private static IEnumerable<PolicyRequirement> Requirements(PolicyBasis basis)
    {
        var candidates = basis.Model.OfType("estate-policy").OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .SelectMany(r => PolicyInputs.Items(PolicyInputs.Get(r.Body, "basis")).Select(b => (Record: r, Basis: b)))
            .ToLookup(x => (PolicyInputs.Text(PolicyInputs.Get(x.Basis, "decision"), "id"),
                PolicyInputs.Text(PolicyInputs.Get(x.Basis, "decision"), "hash"), PolicyInputs.Text(x.Basis, "requirement")), x => x.Record);
        foreach (RecordEnvelope decision in basis.Model.Current("decision").OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        foreach (JsonElement requirement in PolicyInputs.Items(PolicyInputs.Get(decision.Body, "policies")))
        {
            string key = PolicyInputs.Text(requirement, "key") ?? "?", action = PolicyInputs.Text(requirement, "action") ?? "?";
            string policy = PolicyInputs.Text(requirement, "policy") ?? "?";
            string? definition = PolicyInputs.Text(requirement, "definitionHash"), prior = PolicyInputs.Text(requirement, "priorHash");
            RecordEnvelope? recorded = definition is null || basis.UnavailableReason() is not null ? null : candidates[(decision.Id, decision.ComputedHash, key)].FirstOrDefault(r =>
                    r.AtTime >= decision.AtTime && SignatureChecks.IsAuthentic(basis.Model, r) &&
                    (r.Supersedes is null ? "declare" : EstateModel.BodyString(r, "state") == "retired" ? "retire" : "revise") == action &&
                    (r.Supersedes is null ? EstateModel.BodyString(r, "key") : PolicyWork.Identity(basis.Model, r)) == policy &&
                    (r.Supersedes is { } previous ? basis.Model.ById.GetValueOrDefault(previous)?.ComputedHash : null) == prior &&
                    PolicyRequirements.DefinitionHash(r.Body) == definition);
            string state = basis.UnavailableReason() is not null ? "unavailable" : recorded is null ? "pending" : "recorded";
            yield return new(decision.Reference, key, action, policy, PolicyInputs.Text(requirement, "change") ?? "", prior, definition, state,
                recorded?.Reference, state == "unavailable" ? "The record basis is invalid or incomplete." : recorded is not null
                    ? "The exact approved change was recorded; any separate review or obligation remains in force."
                    : definition is null ? "The decision requires design or reconsideration; no exact definition has been approved."
                    : "No authorized policy revision records this exact approved definition and prior revision.");
        }
    }

    private static bool Applies(EstateModel model, RecordEnvelope policy, PolicySubmission submission)
    {
        JsonElement selection = PolicyRecords.Property(policy.Body, "appliesTo");
        string Identity(string id) => model.ById.TryGetValue(id, out RecordEnvelope? record) ? PolicyWork.Identity(model, record) : id;
        return PolicyRecords.Text(selection, "kind") switch
        {
            "estate" => true,
            "product" => Identity(PolicyRecords.Text(selection, "product") ?? "") == Identity(submission.Product),
            "repository" => submission.Repositories?.Contains(Identity(PolicyRecords.Text(selection, "repository") ?? ""), StringComparer.Ordinal) ?? true,
            "work-orders" => submission.WorkOrders is null || PolicyRecords.Items(PolicyRecords.Property(selection, "workOrders"))
                .Any(w => w.ValueKind == JsonValueKind.String && submission.WorkOrders.Contains(Identity(w.GetString()!), StringComparer.Ordinal)),
            _ => true,
        };
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
    internal bool CurrentRule => PolicyRecords.Items(PolicyRecords.Property(Policy.Body, "rules"))
        .Any(r => PolicyRecords.Text(r, "key") == Key && PolicyRecords.Text(r, "kind") == PolicyRecords.Text(Rule, "kind") && PolicyRecords.Equal(PolicyRecords.Property(r, "version"), PolicyRecords.Property(Rule, "version")));
    internal bool Enforced => (Basis.UnavailableReason() is not null || !(Basis.Time is { } at && DutyRecords.Time(EstateModel.BodyString(Policy, "startsAt")) is { } starts && starts > at)) &&
        EstateModel.BodyString(Policy, "state") == "active" && CurrentRule;

    internal PolicyFinding Finding(PolicyTarget target, string item, string verdict, string actual, string required, string nextAction,
        RecordEnvelope? anchor = null, IEnumerable<RecordReference>? evidence = null, string disposition = "none", int subjects = 1) =>
        new(Identity, Policy.Reference, EstateModel.BodyString(Policy, "key") ?? "?", Key, PolicyRecords.Text(Rule, "kind") ?? "?",
            PolicyRecords.Property(Rule, "version") is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out int version) ? version : 0, target, item, verdict, disposition,
            EstateModel.BodyString(Policy, "level") ?? "refuse", EstateModel.BodyString(Policy, "owner") ?? "?", Basis.Digest, Basis.AsOf,
            actual, required, nextAction, anchor?.Reference, anchor?.At,
            (evidence ?? []).Distinct().OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Hash, StringComparer.Ordinal).ToImmutableArray(), [] , subjects, Enforced: Enforced);

    internal PolicyFinding Unavailable(string reason) => Finding(new("policy", Identity, Policy.Reference), "", "unavailable", reason,
        "Complete, valid inputs and an installed implementation of this exact rule version.", "Repair the named input and evaluate again.", subjects: 0);
}
