// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Mcp;

namespace Vericairn.DocsTool;

/// <summary>
/// From 0.9.4 (MCP-07): this build's MCP tool definitions for the release notes, each with its behavior hints and the SHA-256 of its
/// definition, and the set's SHA-256 that initialize's _meta carries.
/// </summary>
internal static class McpTools
{
    internal static int Print(bool json)
    {
        JsonArray definitions = ToolRegistry.Definitions();
        if (json)
        {
            var document = new JsonObject { ["count"] = definitions.Count, ["hash"] = ToolRegistry.DefinitionsHash, ["tools"] = definitions };
            Console.Out.Write(document.ToJsonString() + "\n");
            return 0;
        }

        var text = new StringBuilder();
        text.Append("MCP tools: ").Append(definitions.Count).Append(" tool definitions, SHA-256 ").Append(ToolRegistry.DefinitionsHash)
            .Append(" over their RFC 8785 canonical JSON, the hash initialize's _meta carries as ").Append(McpServer.ToolDefinitionsMeta)
            .Append(". Each definition, as tools/list gives it, has its own SHA-256:\n\n");
        text.Append("| Tool | Read-only | Destructive | Idempotent | Definition SHA-256 |\n|---|---|---|---|---|\n");
        foreach (JsonObject definition in definitions.Select(d => d!.AsObject()))
        {
            JsonNode annotations = definition["annotations"]!;
            text.Append("| `").Append(definition["name"]!.GetValue<string>()).Append("` | ")
                .Append(Hint(annotations, "readOnlyHint")).Append(" | ").Append(Hint(annotations, "destructiveHint")).Append(" | ")
                .Append(Hint(annotations, "idempotentHint")).Append(" | ").Append(ToolRegistry.DefinitionHash(definition)).Append(" |\n");
        }

        Console.Out.Write(text.ToString());
        return 0;
    }

    private static string Hint(JsonNode annotations, string name) => annotations[name]!.GetValue<bool>() ? "yes" : "no";
}
