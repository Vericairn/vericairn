// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    private static IEnumerable<ToolDefinition> CompanionTools()
    {
        yield return Read("docs_companions", "Installed readable coverage, exact collection snapshots and declared destinations. Local inspector metadata grants no remote inspection access.", NoArguments(),
            async (api, _, token) => { CompanionsResponse response = await api.CompanionsAsync(token).ConfigureAwait(false); return CompanionJson(response, response.Status); });
        yield return Read("docs_render", "Render one permitted exact revision or snapshot. The runtime resolves access before generating. A rendition grants no authority or authored clearance.", CompanionArguments(),
            async (api, a, token) => { CompanionResponse response = await api.CompanionRenderAsync(CompanionSelection(a), token).ConfigureAwait(false); return CompanionJson(response, response.Status); });
        yield return Read("docs_sources", "Inspect the exact authorized source basis for a reader selection without generating. Local-only inspectors remain local.", CompanionArguments(),
            async (api, a, token) => { CompanionResponse response = await api.CompanionSourcesAsync(CompanionSelection(a), token).ConfigureAwait(false); return CompanionJson(response, response.Status); });
        yield return Read("docs_compare", "Fresh byte and complete membership comparison for a declared generated document or register. Writes no repair, output pin, review or record.",
            Arguments(("selection", "Exact declaration id or declared repository-relative destination."), ("commit", "Exact external repository commit; no clone-head fallback."), ("audience", "Requested scope, Internal by default; never grants access.")), "selection",
            async (api, a, token) => { CompanionComparison response = await api.CompanionCompareAsync(new(Arg(a, "selection"), OptionalArg(a, "commit"), OptionalArg(a, "audience") ?? "Internal"), token).ConfigureAwait(false); return CompanionJson(response, response.Status); });
    }

    private static JsonObject CompanionArguments()
    {
        JsonObject schema = Arguments(("generator", "Installed namespaced generator identity."), ("selection", "item, nested list, current or all-revisions."),
            ("revision", "Exact source revision id; never follows a successor."), ("snapshot", "Exact snapshot returned by docs_companions."),
            ("page", "Positive decimal page, default 1."), ("field", "body or a registered collection field."), ("index", "all or a one-based collection member."),
            ("audience", "Internal by default; this requests no new permission."), ("contract", "Exact positive output contract version, default 1."),
            ("declaration", "Exact current declaration for a retained source item."), ("commit", "Exact source commit for that declaration."));
        schema["required"] = new JsonArray("generator", "selection");
        return schema;
    }

    private static CompanionRequest CompanionSelection(JsonObject? a) => new(Arg(a, "generator"), Arg(a, "selection"), OptionalArg(a, "revision"),
        OptionalArg(a, "snapshot"), Positive(a, "page"), OptionalArg(a, "field") ?? "body", OptionalArg(a, "index") ?? "all",
        OptionalArg(a, "audience") ?? "Internal", Positive(a, "contract"), OptionalArg(a, "declaration"), OptionalArg(a, "commit"));

    private static int Positive(JsonObject? a, string name) => OptionalArg(a, name) is not { } value ? 1
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0 ? number
        : throw new ArgumentException("A reader page or contract must be a positive decimal integer.");

    private static ToolCallResult CompanionJson<T>(T response, string status) => new(JsonSerializer.Serialize(response, ProtocolJson.Options), status != "success");
}
