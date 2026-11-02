// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    // Use the same complete evaluation already read for progress. No client clock or second policy evaluator.
    private static IEnumerable<AttentionNotice> PolicyNotices(ProgressResponse progress, string principal)
    {
        PolicyResponse policies = progress.Json.GetProperty("policies").Deserialize<PolicyResponse>(ProtocolJson.Options)
            ?? throw new InvalidOperationException("The policy attention source is unavailable.");
        OperatorDutyDto[] duties = progress.Json.GetProperty("operator").GetProperty("duties").Deserialize<OperatorDutyDto[]>(ProtocolJson.Options)
            ?? throw new InvalidOperationException("The operator duty attention source is unavailable.");
        foreach (PolicyFindingDto finding in policies.Findings.Where(f => f.Verdict is "unmet" or "unavailable" ||
            f.Disposition is "excepted" or "due-within-grace" or "discharged-late"))
        {
            string kind = finding.ReleaseHold ? "blocked" : finding.IsReleaseCadence || !finding.Enforced || finding.Verdict == "satisfied" ||
                finding.Disposition is "excepted" or "ended-unmet" || finding.Level == "remind" ? "information" : "failed check";
            // An occurrence is its own notice. A later due date cannot collapse or acknowledge older debt.
            string id = Digest(new { finding.PolicyIdentity, finding.Rule, finding.Target.Identity, finding.Item });
            var detail = new PolicyAttention(finding, policies.Basis, duties.Where(d => d.Operator == principal &&
                d.Finding.PolicyIdentity == finding.PolicyIdentity && d.Finding.Rule == finding.Rule &&
                d.Finding.Target.Identity == finding.Target.Identity && d.Finding.Item == finding.Item).ToArray());
            PolicyRecordReference target = finding.Duty?.Subject ?? finding.Target.Record ?? finding.Policy;
            string why = $"Policy {finding.PolicyKey} {finding.Policy.Id} at {finding.Policy.Hash}; rule {finding.Rule}; subject {finding.Target.Identity}; item {finding.Item}.\n" +
                $"{finding.Verdict} ({finding.Disposition}); level {finding.Level}; owner {finding.Owner}. {finding.Actual}\nRequired: {finding.Required}\n" +
                $"Basis {finding.Basis}; as of {policies.Basis.Origin} time {finding.AsOf ?? "unavailable"}.";
            if (finding.Duty is { } duty)
                why += $"\nDue {duty.DueAt}; deadline {duty.Deadline}; work {(duty.Completed ? "completed" : "incomplete")}; missed {duty.Missed}; occurrence {duty.Occurrence.GetRawText()}.";
            foreach (OperatorDutyDto waiting in detail.Operator)
                why += $"\nWaiting on {waiting.Operator}; owner {waiting.Owner}. {waiting.Preparation}" +
                    (waiting.Asks.Count == 0 ? "" : " Linked asks: " + string.Join(", ", waiting.Asks.Select(a => a.Id + " at " + a.Hash)) + ".");
            yield return new(kind, finding.PolicyKey + "/" + finding.Rule + ": " + finding.Target.Identity, why,
                finding.Disposition, finding.AsOf ?? "unavailable", finding.NextAction, "policy-finding", id, Digest(detail))
            {
                Target = new(target.Type, target.Id, target.Hash),
                Policy = detail,
            };
        }
        if (!policies.Complete || policies.Problem is not null)
            yield return new("failed check", "Policy evaluation", "Policy sources are unavailable or incomplete. " +
                $"Basis {policies.Basis.Digest}; as of {policies.Basis.Origin} time {policies.Basis.AsOf ?? "unavailable"}. " +
                (policies.Problem?.Message ?? string.Join("\n", policies.Diagnostics.Select(d => d.Message))), "unavailable",
                policies.Basis.AsOf ?? "unavailable", "Inspect the policy sources in Workbench or run vericairn policies check.",
                "policy-source", "evaluation", Digest(new { policies.Basis, policies.Diagnostics, policies.Problem }));
    }

    private static string Digest<T>(T value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, ProtocolJson.Options)));
}
