// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public WorkProgressResponse WorkProgress(WorkProgressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is Problem denied)
            {
                return new WorkProgressResponse(null, Problem: denied);
            }

            RecordEnvelope? attempt = Latest(model, request.Attempt, "attempt");
            if (attempt is null)
            {
                return new WorkProgressResponse(null, Problem: NotFound("No attempt has that id.").Problem);
            }

            if (attempt.By != request.By)
            {
                return new WorkProgressResponse(null, Problem: Refuse("Only the principal that made the attempt claims its progress.").Problem);
            }

            RecordEnvelope? completed = model.Records.FirstOrDefault(r => r.Type == "work-progress" && r.Subject?.Id == attempt.Id && EstateModel.BodyString(r, "phase") == request.Phase);
            if (completed is not null)
            {
                // A retry of the same completion returns the original record and does not replace a later live note.
                return request.Completed && EstateModel.BodyString(completed, "note") == request.Note
                    && EstateModel.BodyString(completed, "statusText") == request.StatusText && SameProgress(completed, request.Progress)
                    ? new WorkProgressResponse(Claim(completed), new RecordWritten(completed.ExpectedPath, completed.Id, completed.ComputedHash))
                    : new WorkProgressResponse(null, Problem: Refuse("This phase was already recorded complete; it cannot be recorded or reopened again.").Problem);
            }

            if (model.Current("result").Any(r => r.Subject?.Id == attempt.Id))
            {
                return new WorkProgressResponse(null, Problem: Refuse("This attempt already has a result; its progress is closed.").Problem);
            }

            if (request.Progress is { ValueKind: not JsonValueKind.Object })
                return new WorkProgressResponse(null, Problem: Refuse("Progress must be an object with an admitted type and its value.").Problem);

            RecordEnvelope? assignment = WorkView.AssignmentOfAttempt(model, attempt);
            ProgressRequestSelection selection = assignment is null ? new() : ProgressRequests.Resolve(model, assignment);
            if (selection.Problem is string problem)
                return new WorkProgressResponse(null, Problem: Refuse(problem).Problem);
            if (selection.Request is JsonElement requested)
            {
                if (request.Progress is not JsonElement value)
                    return new WorkProgressResponse(null, Problem: Refuse("The effective progress request requires a typed progress value.").Problem);
                if (requested.TryGetProperty("types", out JsonElement types)
                    && (!value.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String
                        || !types.EnumerateArray().Any(t => t.GetString() == type.GetString())
                        || value.TryGetProperty("estimatePercent", out _) && !types.EnumerateArray().Any(t => t.GetString() == "best-effort")))
                    return new WorkProgressResponse(null, Problem: Refuse("The progress type or accompanying best-effort estimate is not admitted by the effective request.").Problem);
                if (requested.TryGetProperty("statusText", out JsonElement required) && required.GetBoolean() && string.IsNullOrWhiteSpace(request.StatusText))
                    return new WorkProgressResponse(null, Problem: Refuse("The effective progress request requires statusText with each claim.").Problem);
            }

            string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
            var body = new JsonObject { ["phase"] = request.Phase, ["note"] = request.Note, ["completedAt"] = at };
            if (request.Progress is JsonElement progress) body["progress"] = JsonNode.Parse(progress.GetRawText());
            if (request.StatusText is not null) body["statusText"] = request.StatusText;
            var draft = new RecordDraft("work-progress", request.By, Element(body), attempt.Reference);
            // Interim notes pass the same author, shape and portable-content checks, without storing the candidate record.
            (AuthoredRecord? candidate, string? refusal) = AuthorLocked(draft, model);
            if (candidate is null)
            {
                return new WorkProgressResponse(null, Problem: Refuse(refusal!).Problem);
            }

            FindingList findings = Invalid(LoadWith([candidate]), [candidate]);
            if (findings.HasErrors)
            {
                return new WorkProgressResponse(null, Problem: new Problem("rejected", "The progress claim was not stored.", ToFindings(findings, "rejected").Findings));
            }

            RecordWritten? written = null;
            if (request.Completed)
            {
                WriteOutcome outcome = WriteLocked(draft, model);
                if (!outcome.Ok)
                {
                    return new WorkProgressResponse(null, Problem: outcome.Problem);
                }

                written = outcome.Written;
            }

            var claim = new WorkProgressClaim(attempt.Id, request.By, request.Phase, request.Note, at, request.Completed, written?.Id)
                { Progress = request.Progress?.Clone(), StatusText = request.StatusText };
            session.Live.SetMeta("work-progress:" + attempt.Id, JsonSerializer.Serialize(claim, ProtocolJson.Options));
            return new WorkProgressResponse(claim, written);
        }
    }

    private List<WorkProgressClaim> ProgressClaims(EstateModel model)
    {
        var claims = new List<WorkProgressClaim>();
        foreach (RecordEnvelope attempt in model.Current("attempt").OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            RecordEnvelope? recorded = model.Current("work-progress").Where(r => r.Subject?.Id == attempt.Id)
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            WorkProgressClaim? claim = recorded is null ? null : Claim(recorded);
            if (session.Live.Meta("work-progress:" + attempt.Id) is string live)
            {
                WorkProgressClaim? latest = JsonSerializer.Deserialize<WorkProgressClaim>(live, ProtocolJson.Options);
                if (latest is not null && (claim is null || string.CompareOrdinal(latest.At, claim.At) >= 0))
                {
                    claim = latest;
                }
            }

            if (claim is not null)
            {
                claims.Add(claim);
            }
        }

        return claims;
    }

    private static WorkProgressClaim Claim(RecordEnvelope record) => new(record.Subject!.Id, record.By,
        EstateModel.BodyString(record, "phase")!, EstateModel.BodyString(record, "note")!, record.At, true, record.Id)
    {
        Progress = record.Body.TryGetProperty("progress", out JsonElement progress) ? progress.Clone() : null,
        StatusText = EstateModel.BodyString(record, "statusText"),
    };

    private static bool SameProgress(RecordEnvelope record, JsonElement? progress)
    {
        if (!record.Body.TryGetProperty("progress", out JsonElement recorded)) return progress is null;
        if (progress is not JsonElement value || value.ValueKind != JsonValueKind.Object) return false;
        try
        {
            // Semantic JSON number equality also equates 1 with 1.0 and 1e0. Those are malformed in format 3, even on a retry.
            return CanonicalJson.Serialize(recorded) == CanonicalJson.Serialize(value);
        }
        catch (CanonicalJsonException) { return false; }
    }
}
