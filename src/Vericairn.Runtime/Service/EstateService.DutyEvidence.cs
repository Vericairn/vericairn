// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public WriteOutcome ReviewResult(ResultReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SignedEvidenceAbout("review", "result", request.Result, request.By, request.Body, request.Expected);
    }

    private WriteOutcome SignedEvidenceAbout(string type, string subjectType, string id, string by, JsonElement body, string? expected)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? subject = model.ById.GetValueOrDefault(id);
            if (subject is null || subject.Type != subjectType) return NotFound("No " + subjectType + " has id " + id + ".");
            if (!model.IsCurrent(subject) || subject.ComputedHash != expected) return PolicyConflict();
            return SignedPolicyWrite(new(type, by, body, subject.Reference), model);
        }
    }

    private static bool HasDuties(JsonElement body) => PolicyInputs.Get(body, "duties").ValueKind != JsonValueKind.Undefined;

    private static bool IsDutyEvidence(RecordDraft draft, EstateModel model) => draft.Type == "result" && HasDuties(draft.Body) ||
        draft.Type == "review" && (PolicyInputs.Get(draft.Body, "read").ValueKind != JsonValueKind.Undefined ||
            draft.Subject is { Type: "result" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) && HasDuties(result.Body));
}
