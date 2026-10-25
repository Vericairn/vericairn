// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>One immutable template contract, available without conferring any authority.</summary>
public sealed record TemplateDto(TemplatePinDto Pin, JsonElement Definition);
/// <summary>The exact immutable identity of declarative template content.</summary>
public sealed record TemplatePinDto(string Id, string Version, string Hash);
/// <summary>An exact record revision read as section evidence.</summary>
public sealed record TemplateRecordReference(string Type, string Id, string Hash);
/// <summary>A complete section source with exact bytes and record references, or an unavailable state.</summary>
public sealed record TemplateSourceDto(string Id, string Kind, string Identity, string Hash, long Bytes, string? Content, string Status, IReadOnlyList<TemplateRecordReference> Records);
/// <summary>A deterministic check or separately attributed evaluator verdict.</summary>
public sealed record TemplateCheckDto(string Id, string Outcome, string Summary);
/// <summary>A section's derived state, exact evidence and complete criteria.</summary>
public sealed record TemplateSectionDto(string Id, string State, bool Required, string RequiredFidelity, string? ContentHash,
    string SourceDigest, string? Writer, string? Review, JsonElement Definition, IReadOnlyList<TemplateSourceDto> Sources,
    IReadOnlyList<TemplateCheckDto> Checks, IReadOnlyList<TemplateCheckDto> Cohesion, IReadOnlyList<string> Reasons);
/// <summary>An evaluator's per-rule verdict, with the exact hashes read.</summary>
public sealed record DocumentSectionJudgment(string Rule, string Verdict, string Summary, IReadOnlyList<string> Evidence);
/// <summary>A requested section outcome and fidelity. Content hashes and checks are read and derived by the runtime.</summary>
public sealed record DocumentSectionReview(string Id, string Outcome, string Fidelity, IReadOnlyList<DocumentSectionJudgment>? Judgments = null);
/// <summary>The complete measured context of one section; a failed allocation preserves the required originals.</summary>
public sealed record TemplateBriefDto(string Declaration, string DeclarationHash, TemplatePinDto Template, string Section,
    string Purpose, string Role, string RoleBrief, string Fidelity, JsonElement Quality, IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<TemplateSourceDto> Sources, IReadOnlyList<TemplateCheckDto> Cohesion, long Bytes, bool Complete, string? Problem);
/// <summary>The available template versions, or an explicit catalogue problem.</summary>
public sealed record TemplatesResponse(IReadOnlyList<TemplateDto> Templates, Problem? Problem = null);
/// <summary>A document's section states and their exact evidence, or a lookup problem.</summary>
public sealed record DocumentSectionsResponse(string? Declaration, string? Path, IReadOnlyList<TemplateSectionDto> Sections, Problem? Problem = null);
/// <summary>A measured complete section brief, or an explicit unavailable/refused result.</summary>
public sealed record SectionBriefResponse(TemplateBriefDto? Brief, Problem? Problem = null)
{
    /// <summary>Complete context-template presentation, only after explicit estate adoption; measured separately from the source brief.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TemplateRenderResponse? Presentation { get; init; }
}
/// <summary>Creates one estate document from an exact template, recording effective choices and explicit overrides.</summary>
public sealed record DocumentNewRequest(string By, TemplatePinDto Template, string Path, string Title, JsonElement? Options = null,
    JsonElement? Scope = null, JsonElement? Overrides = null, string Level = "warn");
/// <summary>The instance declaration and output receipt. A stored declaration with a problem requires reconciliation.</summary>
public sealed record DocumentNewResponse(RecordWritten? Declaration, string? Path, string? ContentHash, Problem? Problem = null)
{
    /// <summary>Whether the declaration and document were both stored successfully.</summary>
    public bool Ok => Declaration is not null && Problem is null;
}
