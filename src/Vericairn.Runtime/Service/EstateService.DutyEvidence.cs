// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Service.EstateServiceValues;

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
            var draft = new RecordDraft(type, by, body, subject.Reference);
            string what = type == "review" ? "A signed result review" : HasDuties(body) ? "A result with duties is signed duty evidence and" : "A result submitted with an expected attempt hash is signed and";
            string? hint = type == "review" && !IsDutyEvidence(draft, model) ? " A review of a result without duties can be recorded with records new review, which needs neither." : null;
            if (MissingEvidencePreconditions(model, draft, subjectType, true, expected, "the current " + subjectType, what, hint) is { } missing) return missing;
            if (!model.IsCurrent(subject) || subject.ComputedHash != expected) return PolicyConflict();
            return SignedPolicyWrite(draft, model);
        }
    }

    /// <summary>
    /// Generic authoring of a result or a review. Duty evidence, a result with duties or a review of one, is signed: it needs its exact
    /// subject, the expected hash and an admitted device, and a refusal names whichever is missing. Any other result or review is an
    /// ordinary record that needs neither; an expected hash supplied for one is still checked, never ignored. Caller holds the gate.
    /// </summary>
    private WriteOutcome NewEvidenceLocked(NewRecordRequest request, RecordDraft draft, EstateModel model)
    {
        RecordReference? subject = draft.Subject;
        // An expected hash holds the record a revision supersedes, otherwise the exact current subject.
        string? target = request.Supersedes is { } prior ? model.ById.GetValueOrDefault(prior)?.ComputedHash
            : subject is not null && model.ById.TryGetValue(subject.Id, out RecordEnvelope? held) && model.IsCurrent(held) && subject == held.Reference ? held.ComputedHash : null;
        if (!IsDutyEvidence(draft, model)) return request.Expected is not null && request.Expected != target ? PolicyConflict() : WriteLocked(draft, model);
        if (request.At is not null || request.Id is not null) return Refuse("Policy and duty evidence authoring uses the runtime's fresh time and record identity.");
        string subjectType = request.Type == "review" ? "result" : "attempt";
        string what = request.Type == "review" ? "A review of a result with duties is signed duty evidence and" : "A result with duties is signed duty evidence and";
        if (MissingEvidencePreconditions(model, draft, subjectType, subject?.Hash is not null, request.Expected,
            request.Supersedes is null ? "the current " + subjectType : "the record it revises", what) is { } missing) return missing;
        if (subject is null || !model.ById.TryGetValue(subject.Id, out RecordEnvelope? current) || !model.IsCurrent(current) || subject != current.Reference ||
            target is null || request.Expected != target) return PolicyConflict();
        return SignedPolicyWrite(draft, model);
    }

    /// <summary>
    /// Signed evidence needs its exact subject, an expected hash and an admitted device whose key this runtime holds. A refusal names
    /// each one missing; a supplied hash that is no longer current is a conflict instead.
    /// </summary>
    private WriteOutcome? MissingEvidencePreconditions(EstateModel model, RecordDraft draft, string subjectType, bool subjectPinned, string? expected,
        string expecting, string what, string? hint = null)
    {
        List<string> missing = [];
        if (!subjectPinned) missing.Add("its exact subject, pinned as " + subjectType + ":<id>:<hash>");
        if (string.IsNullOrEmpty(expected)) missing.Add("the expected hash of " + expecting + " (--expected; expected over the API and MCP)");
        if (ResolveSigner(model, draft, out IRecordSigner? signer) is not null || signer is null) missing.Add("an admitted device of principal " + draft.By + " whose key this runtime holds");
        if (missing.Count == 0) return null;
        string needs = missing.Count == 1 ? missing[0] : string.Join(", ", missing.Take(missing.Count - 1)) + " and " + missing[^1];
        return Refuse(what + " needs " + needs + "." + hint + " Nothing was written.");
    }

    private static bool HasDuties(JsonElement body) => PolicyInputs.Get(body, "duties").ValueKind != JsonValueKind.Undefined;

    // A review is duty evidence by its subject result's duties, never by its read list alone (FIX-94).
    private static bool IsDutyEvidence(RecordDraft draft, EstateModel model) => draft.Type == "result" && HasDuties(draft.Body) ||
        draft.Type == "review" && PolicyAuthority.IsDutyReview(model, draft.Subject);
}
