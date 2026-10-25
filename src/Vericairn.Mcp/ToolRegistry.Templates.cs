// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    private static IEnumerable<ToolDefinition> TemplateTools()
    {
        JsonObject render = Arguments(("request", "Complete TemplateRenderRequest: exact template, subject and depth; optional authored fields, explanation, allocation, candidate and retained adoption. Creates no record."));
        render["properties"]!["request"]!["type"] = "object";
        yield return Read("template_render", "Prepare or check a complete presentation from an explicitly adopted pinned template. Missing facts or references fail at every depth; this read records no adoption, review or acceptance.", render, "request",
            async (api, a, token) =>
            {
                TemplateRenderRequest request = Body(a, "request").Deserialize<TemplateRenderRequest>(ProtocolJson.Options)
                    ?? throw new ArgumentException("A complete presentation request is required.");
                TemplateRenderResponse response = await api.TemplateRenderAsync(request, token).ConfigureAwait(false);
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), response.Problem is not null);
            });
        yield return Read("document_sections", "Each section's state, required fidelity, checks and evaluator judgments at the exact available basis. Reading grants no authority.",
            Arguments(("document", "Template instance declaration id or estate document path."), ("commit", "Optional exact external repository commit.")), "document",
            async (api, a, token) =>
            {
                DocumentSectionsResponse response = await api.DocumentSectionsAsync(Arg(a, "document"), OptionalArg(a, "commit"), token).ConfigureAwait(false);
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), response.Problem is not null);
            });
        JsonObject brief = Arguments(("document", "Template instance declaration id or estate document path."), ("section", "Section or field identifier."),
            ("maxBytes", "Optional positive decimal allocation; required originals are never truncated."), ("commit", "Optional exact external repository commit."));
        brief["required"] = new JsonArray("document", "section");
        yield return Read("section_brief", "A complete measured section brief: purpose, writing role, fidelity, quality, options and exact original sources. A template grants nothing.", brief,
            async (api, a, token) =>
            {
                long? allocation = null;
                if (OptionalArg(a, "maxBytes") is { } value)
                    allocation = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && parsed > 0
                        ? parsed : throw new ArgumentException("maxBytes must be a positive decimal integer.");
                SectionBriefResponse response = await api.SectionBriefAsync(Arg(a, "document"), Arg(a, "section"), allocation, OptionalArg(a, "commit"), token).ConfigureAwait(false);
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), response.Problem is not null);
            });
    }

    private static JsonObject TemplateReviewArguments()
    {
        JsonObject schema = Arguments(("path", "The document's repository-relative path."), ("outcome", "current, revised or needs-change."), ("summary", "What was checked."));
        schema["properties"]!["sections"] = new JsonObject
        {
            ["type"] = "array", ["description"] = "For a template instance, the selected sections: id, outcome, fidelity and optional per-rule judgments (rule, verdict, summary, evidence hashes).",
            ["items"] = new JsonObject { ["type"] = "object" },
        };
        return schema;
    }
}
