// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Serialization;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;

namespace Vericairn.Remote;

/// <summary>An exact notice beside a separately observed policy, authority and optional read-only desktop package.</summary>
public sealed record RemotePolicyDetail(AttentionNotice Notice, string AsOf, PolicyResponse? Policy,
    IReadOnlyList<string>? Authority, HandoffResponse? Handoff = null)
{
    /// <summary>The original condition, its complete occurrence history and the newly observed policy response.</summary>
    [JsonIgnore]
    public string Material
    {
        get
        {
            var text = new StringBuilder("Original attention condition\n");
            if (Notice.Policy is { } original)
            {
                text.AppendLine($"Basis {original.Basis.Digest}; as of {original.Basis.Origin} time {original.Basis.AsOf ?? "unavailable"}.");
                Condition(text, original.Finding);
                foreach (OperatorDutyDto duty in original.Operator)
                {
                    text.AppendLine($"Waiting on {duty.Operator}; owner {duty.Owner}. Preparation: {duty.Preparation}");
                    foreach (PolicyRecordReference ask in duty.Asks) Reference(text, "Linked ask", ask);
                }
            }
            if (Policy is null) text.AppendLine("The complete policy and exception history were not observed. Reconnect and refresh; unavailable is not an empty history.");
            else
            {
                text.AppendLine("Observed policy and exception history");
                text.AppendLine(Policy.Text.Length == 0 ? PolicyPresentation.Render(Policy) : Policy.Text);
                foreach (RecordResponse record in Policy.Policies.Concat(Policy.Exceptions))
                {
                    text.AppendLine($"{record.Type} {record.Id} at {record.Hash}; author {RemoteWorkDetail.Text(record.Envelope, "by")}; recorded {RemoteWorkDetail.Text(record.Envelope, "at")}; current {record.Current}; supersedes {RemoteWorkDetail.Text(record.Envelope, "supersedes")}.");
                    if (record.Type == "estate-policy") text.AppendLine(CanonicalJson.SerializePretty(record.Envelope.GetProperty("body")));
                }
                foreach (PolicyFindingDto finding in Policy.Findings) Condition(text, finding);
            }
            return text.ToString().ReplaceLineEndings("\n");
        }
    }

    /// <summary>Exact original selection to carry to the desktop, including non-record sources and occurrence identity.</summary>
    [JsonIgnore]
    public string DesktopRoute => Notice.Policy is not { } policy ? "Inspect policies in Workbench." :
        $"Open {policy.Finding.PolicyKey}/{policy.Finding.Rule} in Workbench.\n" +
        $"vericairn policies show {policy.Finding.Policy.Id}\n" +
        $"Exact policy {policy.Finding.Policy.Id} at {policy.Finding.Policy.Hash}\n" +
        $"Subject {policy.Finding.Target.Identity}; item {policy.Finding.Item}\n" +
        (Notice.Target is { } target ? $"Exact {target.Type} {target.Id} at {target.Hash}\nvericairn records show {target.Id}\n" : "") +
        (policy.Finding.Duty is { } duty ? $"Occurrence {CanonicalJson.Serialize(duty.Occurrence)}; origin {duty.Origin.Id} at {duty.Origin.Hash}\n" : "") +
        "Review the exact sources, completion proofs and exceptions there. Refresh the basis and authority on the desktop before acting; this context grants nothing.";

    private static void Condition(StringBuilder text, PolicyFindingDto finding)
    {
        text.AppendLine($"{finding.PolicyKey}/{finding.Rule}: {finding.Verdict} ({finding.Disposition}); level {finding.Level}; owner {finding.Owner}; release hold {finding.ReleaseHold}.");
        Reference(text, "Policy", finding.Policy);
        text.AppendLine($"Subject {finding.Target.Kind} {finding.Target.Identity}; item {finding.Item}; repository {finding.Target.Repository ?? "none"}; path {finding.Target.Path ?? "none"}; content {finding.Target.Hash ?? "unavailable"}.");
        if (finding.Target.Record is { } record) Reference(text, "Subject revision", record);
        text.AppendLine($"{finding.Actual}\nRequired: {finding.Required}\nNext: {finding.NextAction}\nBasis {finding.Basis}; as of {finding.AsOf ?? "unavailable"}.");
        if (finding.Anchor is { } anchor) Reference(text, "Anchor at " + (finding.AnchorAt ?? "unavailable"), anchor);
        foreach (PolicyRecordReference evidence in finding.Evidence) Reference(text, "Evidence", evidence);
        foreach (PolicyRecordReference exception in finding.Exceptions) Reference(text, "Applicable exception", exception);
        if (finding.Duty is not { } duty) return;
        Reference(text, "Schedule origin", duty.Origin);
        Reference(text, "Duty subject " + duty.SubjectIdentity, duty.Subject);
        text.AppendLine($"Occurrence {CanonicalJson.Serialize(duty.Occurrence)}; due {duty.DueAt}; deadline {duty.Deadline}. Work {(duty.Completed ? "completed" : "incomplete")}; completed at {duty.CompletedAt ?? "none"}; missed {duty.Missed}; late seconds {duty.LateSeconds}; already due at adoption {duty.AlreadyDueAtAdoption}; ended reason {duty.EndedReason ?? "none"}.");
        foreach (DutyCompletionDto completion in duty.Completions)
        {
            Reference(text, "Completion result", completion.Result);
            text.AppendLine($"Qualified from {completion.From}; until {completion.Until ?? "not withdrawn at this basis"}.");
            foreach (PolicyRecordReference proof in completion.Evidence) Reference(text, "Completion proof", proof);
        }
    }

    private static void Reference(StringBuilder text, string label, PolicyRecordReference record) =>
        text.AppendLine($"{label}: {record.Type} {record.Id} at {record.Hash}");
}
