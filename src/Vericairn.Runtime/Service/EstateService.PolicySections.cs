// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private sealed record PolicySections(PolicyEvaluation Evaluation, WritingEvaluation Writing,
        PolicyResponse Policies, WritingExceptionsResponse Exceptions, IReadOnlyList<OperatorDutyDto> Operator);

    private PolicySections ReadPolicySections(EstateModel model)
    {
        PolicyKindRegistry registry = PolicySources.Registry();
        PolicyBasis basis = PolicySnapshot(model, registry);
        PolicyEvaluation policies = PolicyEvaluator.Evaluate(basis, registry);
        WritingEvaluation writing = WritingEvaluation.Evaluate(basis);
        PolicyResponse view = PolicyView(model, basis, policies);
        return new(policies, writing, view, WritingView(model, basis, writing), OperatorDuties(model, view));
    }

    private static FindingList SectionChecks(EstateModel model, FindingList builtIn, PolicySections sections)
    {
        // Replace only exact occurrences reconstructed by the shared scanner, preserving every
        // error and every warning whose source could not be read. Multiplicity matters.
        Dictionary<Finding, int> scanned = sections.Writing.Warnings.Concat(sections.Writing.ExceptedOccurrences)
            .GroupBy(w => w.Occurrence.Diagnostic).ToDictionary(g => g.Key, g => g.Count());
        var checks = new FindingList();
        foreach (Finding finding in builtIn)
        {
            if (finding is { Code: "writing.spacing", Severity: Severity.Warning } && scanned.TryGetValue(finding, out int count) && count > 0)
                scanned[finding] = count - 1;
            else checks.Add(finding);
        }
        foreach (WritingOccurrence warning in sections.Writing.Warnings) checks.Add(warning.Occurrence.Diagnostic);
        foreach (WritingOccurrence occurrence in sections.Writing.ExceptedOccurrences)
            checks.Info("writing.excepted", occurrence.Occurrence.Diagnostic.Location,
                $"{occurrence.Occurrence.Token}: excepted by {string.Join(", ", occurrence.Entries.Select(e => e.Id + " " + e.Hash))}; basis {occurrence.Basis}.");
        foreach (Finding diagnostic in sections.Evaluation.Diagnostics.Concat(sections.Writing.Diagnostics).Distinct().Except(model.Findings).Except(checks.ToArray())) checks.Add(diagnostic);
        foreach (PolicyFinding finding in sections.Evaluation.Findings)
        {
            string code = finding.Disposition == "excepted" ? "policy.excepted" : "policy." + finding.Verdict;
            string deadline = finding.Duty is { } duty ? $" Due {duty.DueAt}; deadline {duty.Deadline}; completed {duty.Completed}; occurrence {duty.Occurrence.GetRawText()}." : "";
            checks.Add(new(finding.Severity, code, "policy:" + finding.PolicyKey + "/" + finding.Rule + ":" + finding.Target.Identity,
                $"Policy {finding.Policy.Id} {finding.Policy.Hash}; rule {finding.Rule}; {finding.Verdict} ({finding.Disposition}) at {finding.Level}: {finding.Actual}{deadline} Required: {finding.Required} Next: {finding.NextAction} Basis {finding.Basis}."));
        }
        foreach (PolicyRequirement requirement in sections.Evaluation.Requirements ?? [])
            checks.Info("policy.requirement-" + requirement.State, "decision:" + requirement.Decision.Id + "/" + requirement.Key,
                $"{requirement.Action} {requirement.Policy}: {requirement.Change} {requirement.Reason} Decision {requirement.Decision.Hash}; recorded revision {requirement.Recorded?.Hash ?? "none"}; basis {sections.Evaluation.Basis}.");
        return checks;
    }

    private static void AddPolicySections(JsonObject json, PolicySections sections)
    {
        json["policies"] = JsonSerializer.SerializeToNode(sections.Policies, ProtocolJson.Options);
        json["writingExceptions"] = JsonSerializer.SerializeToNode(sections.Exceptions, ProtocolJson.Options);
        if (json["operator"] is not JsonObject operatorView) json["operator"] = operatorView = new();
        operatorView["duties"] = JsonSerializer.SerializeToNode(sections.Operator, ProtocolJson.Options);
    }

    private static string OperatorDutyText(PolicySections sections)
    {
        var text = new StringBuilder("  Recurring duties and linked asks:\n");
        if (sections.Operator.Count == 0) text.AppendLine("    none");
        foreach (OperatorDutyDto item in sections.Operator)
        {
            PolicyFindingDto finding = item.Finding;
            text.AppendLine($"    to {item.Operator}; owner {item.Owner}: {finding.PolicyKey}/{finding.Rule}, subject {finding.Duty!.Subject.Id} {finding.Duty.Subject.Hash}, occurrence {finding.Duty.Occurrence.GetRawText()}; deadline {finding.Duty.Deadline} ({finding.Disposition})");
            text.AppendLine($"      {finding.Actual} Required: {finding.Required} Next: {finding.NextAction}");
            text.AppendLine($"      Preparation: {item.Preparation}");
            foreach (PolicyRecordReference evidence in finding.Evidence) text.AppendLine($"      evidence {evidence.Type} {evidence.Id} {evidence.Hash}");
            foreach (PolicyRecordReference ask in item.Asks) text.AppendLine($"      linked ask {ask.Id} {ask.Hash}");
        }
        return text.ToString().ReplaceLineEndings("\n");
    }

    private static List<OperatorDutyDto> OperatorDuties(EstateModel model, PolicyResponse policies)
    {
        List<OperatorDutyDto> rows = [];
        RecordEnvelope[] asks = Vericairn.Records.Estate.Asks.Deliveries(model).Where(d => model.HasRole(d.Principal, Roles.Operator)).SelectMany(d => d.Asks).ToArray();
        foreach (PolicyFindingDto finding in policies.Findings.Where(f => f.Duty is { Completed: false } && f.Enforced &&
            f.Disposition is not ("ended-unmet" or "scheduled")))
        {
            RecordEnvelope[] linked = asks.Where(a => a.Subject is { Type: "result" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) &&
                subject.Hash == result.ComputedHash && PolicyInputs.Items(PolicyInputs.Get(result.Body, "duties")).Any(d =>
                    PolicyInputs.Text(d, "rule") == finding.Rule && PolicyInputs.Text(PolicyInputs.Get(d, "policy"), "id") == finding.Duty!.Origin.Id &&
                    PolicyInputs.Text(PolicyInputs.Get(d, "policy"), "hash") == finding.Duty.Origin.Hash &&
                    PolicyRecords.Equal(PolicyInputs.Get(d, "occurrence"), finding.Duty.Occurrence) &&
                    model.ById.TryGetValue(PolicyInputs.Text(PolicyInputs.Get(d, "subject"), "id") ?? "", out RecordEnvelope? target) && PolicyWork.Identity(model, target) == finding.Duty.SubjectIdentity)).ToArray();
            string[] operators = linked.Select(a => EstateModel.BodyString(a, "to")!).Concat(model.HasRole(finding.Owner, Roles.Operator) ? [finding.Owner] : [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            foreach (string principal in operators)
                rows.Add(new(principal, finding.Owner, finding, linked.Where(a => EstateModel.BodyString(a, "to") == principal).Select(a => ConvertPolicy<PolicyRecordReference>(a.Reference)).ToArray(),
                    "Inspect the exact subject and required proofs; prepare the authorized action or review. This finding and any exception grant no authority to perform effects."));
        }
        return rows;
    }
}
