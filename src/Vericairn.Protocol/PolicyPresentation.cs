// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;

namespace Vericairn.Protocol;

/// <summary>Human views of the same complete policy responses transported by every client.</summary>
public static class PolicyPresentation
{
    /// <summary>Render declarations, findings, duty evidence and the explicit basis.</summary>
    public static string Render(PolicyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = new StringBuilder("Policies\n");
        Basis(text, response.Basis);
        foreach (RecordResponse policy in response.Policies)
        {
            var body = policy.Envelope.GetProperty("body");
            text.AppendLine($"  {body.GetProperty("key").GetString()}: {body.GetProperty("state").GetString()} at {body.GetProperty("level").GetString()}; owner {body.GetProperty("owner").GetString()} [{policy.Id} {policy.Hash}]");
        }
        foreach (PolicyFindingDto finding in response.Findings)
        {
            text.AppendLine($"  {finding.PolicyKey}/{finding.Rule} {finding.Target.Identity}: {finding.Verdict} ({finding.Disposition}), {finding.Level}{(finding.ReleaseHold ? "; release hold" : "")}");
            text.AppendLine($"    {finding.Actual} Required: {finding.Required} Next: {finding.NextAction}");
            if (finding.Duty is { } duty)
                text.AppendLine($"    Recurring duty: due {duty.DueAt}; deadline {duty.Deadline}; completed {duty.Completed}; missed {duty.Missed}; completed at {duty.CompletedAt ?? "none"}; occurrence {duty.Occurrence.GetRawText()}");
            foreach (PolicyRecordReference evidence in finding.Evidence) text.AppendLine($"    evidence {evidence.Type} {evidence.Id} {evidence.Hash}");
            foreach (PolicyRecordReference exception in finding.Exceptions) text.AppendLine($"    exception {exception.Id} {exception.Hash}");
        }
        foreach (RecordResponse exception in response.Exceptions)
            text.AppendLine($"  Exception history {exception.Id} {exception.Hash}: {exception.Envelope.GetProperty("body").GetRawText()}");
        foreach (FindingDto diagnostic in response.Diagnostics) text.AppendLine($"  {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        if (response.Problem is { } problem) text.AppendLine(problem.Message);
        text.AppendLine($"{response.Holds} release holds; evaluation {(response.Complete ? "complete" : "incomplete")}.");
        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Render active entries, removal history and every matching location.</summary>
    public static string Render(WritingExceptionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = new StringBuilder("Writing exceptions\n");
        Basis(text, response.Basis);
        foreach (WritingEntryDto entry in response.Entries)
            text.AppendLine($"  {entry.Token}: {entry.Reason}; author {entry.Author}; scope {entry.Scope?.GetRawText() ?? "everywhere"}; matches {entry.Matches?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} [{entry.Entry.Id} {entry.Entry.Hash}]");
        foreach (RecordResponse record in response.Records.Where(r => r.Envelope.GetProperty("body").GetProperty("state").GetString() == "removed"))
            text.AppendLine($"  Removed {record.Id} {record.Hash}: {record.Envelope.GetProperty("body").GetRawText()}");
        foreach (WritingOccurrenceDto occurrence in response.ExceptedOccurrences)
            text.AppendLine($"  excepted {occurrence.Occurrence.Token} at {occurrence.Location.Identity}:{occurrence.Location.Member}:{occurrence.Occurrence.Line}; entries {string.Join(", ", occurrence.Entries.Select(e => e.Id + " " + e.Hash))}");
        foreach (WritingOccurrenceDto occurrence in response.Warnings) text.AppendLine($"  warning {occurrence.Occurrence.Token} at {occurrence.Location.Identity}:{occurrence.Location.Member}:{occurrence.Occurrence.Line}");
        foreach (FindingDto diagnostic in response.Diagnostics) text.AppendLine($"  {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        text.AppendLine($"{response.ExceptedOccurrences.Count} excepted occurrences; scan {(response.Complete ? "complete" : "incomplete")}.");
        return text.ToString().ReplaceLineEndings("\n");
    }

    private static void Basis(StringBuilder text, PolicyBasisDto basis)
    {
        text.AppendLine($"Basis {basis.Digest}; as of {basis.Origin} time {basis.AsOf ?? "unavailable"}; records {basis.RecordDigest}.");
        if (basis.Submission is { } release) text.AppendLine($"Release target {release.Release.Id}: {release.Product} {release.Version} at {release.Commit}.");
    }
}
