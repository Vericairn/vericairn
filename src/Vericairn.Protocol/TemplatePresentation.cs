// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Readable section evidence for clients, retaining every required source and criterion.</summary>
public static class TemplatePresentation
{
    /// <summary>Each section's state, required fidelity and every check or judgment outcome.</summary>
    public static string Sections(DocumentSectionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Problem is not null) return response.Problem.Message;
        var text = new StringBuilder();
        foreach (TemplateSectionDto section in response.Sections)
        {
            text.Append(section.Id).Append(": ").Append(section.State).Append("; requires ").Append(section.RequiredFidelity)
                .Append(section.Required ? "; required" : "; optional").Append('\n');
            foreach (string reason in section.Reasons) text.Append("  ").Append(reason).Append('\n');
            foreach (TemplateCheckDto check in section.Checks.Concat(section.Cohesion)) text.Append("  ").Append(check.Id).Append(": ").Append(check.Outcome).Append(" — ").Append(check.Summary).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>The complete brief, including original source payloads and their exact identity.</summary>
    public static string Brief(SectionBriefResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Brief is not { } brief) return response.Problem?.Message ?? "The section brief is unavailable.";
        if (response.Problem is null && response.Presentation is { Problem: null, Text: { } presentation }) return presentation;
        var text = new StringBuilder("# Section brief: ").Append(brief.Section).Append("\n\n").Append(brief.Purpose).Append("\n\n")
            .Append("Instance: ").Append(brief.Declaration).Append(" at ").Append(brief.DeclarationHash).Append('\n')
            .Append("Template: ").Append(brief.Template.Id).Append(' ').Append(brief.Template.Version).Append(" at ").Append(brief.Template.Hash).Append('\n')
            .Append("Writing role: ").Append(brief.Role).Append("; fidelity: ").Append(brief.Fidelity).Append(". Roles require recorded standing; this brief grants nothing.\n")
            .Append("Role brief: ").Append(brief.RoleBrief).Append('\n')
            .Append("Quality: ").Append(brief.Quality.GetRawText()).Append('\n')
            .Append("Options: ").Append(JsonSerializer.Serialize(brief.Options, ProtocolJson.Options)).Append("\n\n");
        foreach (TemplateCheckDto rule in brief.Cohesion) text.Append(rule.Id).Append(": ").Append(rule.Outcome).Append(" — ").Append(rule.Summary).Append('\n');
        foreach (TemplateSourceDto source in brief.Sources)
        {
            text.Append("\n## Source: ").Append(source.Id).Append("\n\n").Append(source.Kind).Append(": ").Append(source.Identity)
                .Append(" at ").Append(source.Hash).Append("; ").Append(source.Bytes).Append(" UTF-8 bytes; ").Append(source.Status).Append("\n\n");
            text.Append(source.Content ?? "Required original unavailable.").Append('\n');
        }
        text.Append("\nMeasured canonical JSON package: ").Append(brief.Bytes).Append(" UTF-8 bytes; ").Append(brief.Complete ? "complete" : "incomplete").Append(".\n");
        if (response.Problem is not null) text.Append(response.Problem.Message).Append('\n');
        return text.ToString();
    }
}
