// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;

namespace Vericairn.Protocol;

/// <summary>An explicit, recorded adoption of one immutable presentation contract.</summary>
public sealed record PresentationTemplateUse(TemplatePinDto Template, TemplateRecordReference Adoption, string Depth = "standard");

/// <summary>Prepare or check a complete artifact from an exact subject. Authored fields are proposals, never source facts.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record TemplateRenderRequest(TemplatePinDto Template, TemplateRecordReference Subject, string? Depth = null,
    JsonElement? Authored = null, string? Explanation = null, long? MaxBytes = null, string? Candidate = null,
    TemplateRecordReference? Adoption = null);

/// <summary>A complete measured artifact and its content checks. Passing these checks grants no review or acceptance.</summary>
public sealed record TemplateRenderResponse(PresentationTemplateUse? Use, string? Text, string? Hash, long? Bytes,
    IReadOnlyList<TemplateCheckDto> Checks, Problem? Problem = null);

/// <summary>Shared client presentation for an explicitly adopted communication template.</summary>
public static class CommunicationTemplatePresentation
{
    /// <summary>Checks a candidate against the complete independently supplied original and profile at the selected depth.</summary>
    public static TemplateCheckDto Check(PresentationTemplateUse use, string depth, string explanation, string original,
        string subject, CommunicationProfile profile, string candidate) => new("complete-presentation",
            Render(use, depth, explanation, original, subject, profile) == candidate ? "passed" : "failed",
            "Every original fact, reference, profile contribution and accounting field must match the separately supplied basis.");

    /// <summary>Wraps the complete original and source metadata without changing the source or the recipient's choice.</summary>
    public static string Render(PresentationTemplateUse use, string depth, string explanation, string original,
        string subject, CommunicationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(use);
        ArgumentNullException.ThrowIfNull(profile);
        if (use.Template.Id != PresentationContent.Communication || use.Template.Version != "1.0.0"
            || use.Template.Hash != PresentationContent.Hash(PresentationContent.Communication))
            throw new InvalidOperationException("The adopted communication template is unavailable in this client.");
        string identity = PresentationContent.Json(new
        {
            kind = "complete-authorized-presentation", subject, hash = PresentationContent.Digest(original), bytes = System.Text.Encoding.UTF8.GetByteCount(original),
            profile.Recipient, profile.Assignment, profile.ObservedAt, profile.RevisionHash, profile.Revisions,
        });
        var sections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["explanation"] = explanation, ["facts"] = original,
            ["originals"] = PresentationContent.Json(new { encoding = "base64", content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original)) }),
            ["source-identities"] = identity,
            ["withheld"] = "[]\nScope: the complete authorized presentation and its communication profile; no claim about unselected estate sources.",
        };
        return PresentationContent.Render(use.Template.Id, depth, $"{use.Adoption.Type}:{use.Adoption.Id}@{use.Adoption.Hash}", sections).Text;
    }
}
