// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static class ReleaseReviewRules
{
    internal static void ValidateDecision(ValidationContext context)
    {
        BodyReader body = context.Body;
        BodyReader? review = body.Since(3, "releaseReview") ? body.Nested("releaseReview", false) : null;
        if (review is null) return;
        Exact("candidate", "release");
        Exact("workOrder", "work-order");
        string? scope = review.Enum("scope", true, "full", "focused");
        IReadOnlyList<string>? focus = review.StringArray("focus", true, allowEmpty: true, maxItemLength: 2000);
        review.StringArray("probes", true, allowEmpty: true, maxItemLength: 2000);
        if (scope == "focused" && focus is { Count: 0 })
            review.Error("decision.release-review", "focus", "A focused review names the approved focus.");
        if (scope == "full" && focus is { Count: > 0 })
            review.Error("decision.release-review", "focus", "A full review has no narrowing focus.");
        review.NoOtherKeys();

        void Exact(string field, string type)
        {
            BodyReader? value = review.Nested(field, true);
            if (value is null) return;
            RecordReference? reference = RecordParser.ParseReference(value.Element, value.Location, context.Findings);
            RecordEnvelope? target = reference is null ? null : context.RequireReference("releaseReview." + field, reference.Id, type);
            if (reference is not null && (reference.Type != type || reference.Hash is null || target?.ComputedHash != reference.Hash))
                value.Error("decision.release-review", "hash", "A release review pins the exact " + type + " revision and hash.");
            if (field == "candidate" && target is not null && EstateModel.BodyString(target, "status") != "candidate")
                value.Error("decision.release-review", "id", "The review names a release candidate.");
        }
    }
}
