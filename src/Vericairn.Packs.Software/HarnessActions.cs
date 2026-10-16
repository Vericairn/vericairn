// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Reads invocation inputs only. Text, tool results, stdout and stderr are not commands to audit.</summary>
internal static class HarnessActions
{
    public static IReadOnlyList<HarnessAction> Claude(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (Text(root, "type") != "assistant" || !root.TryGetProperty("message", out JsonElement message)
            || !message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return content.EnumerateArray().Where(block => Text(block, "type") == "tool_use")
            .Select(block => new HarnessAction(Required(block, "id"), Required(block, "name"), block.GetProperty("input").Clone())).ToArray();
    }

    public static IReadOnlyList<HarnessAction> Codex(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (Text(root, "type") is not ("item.started" or "item.updated" or "item.completed")
            || !root.TryGetProperty("item", out JsonElement item))
        {
            return [];
        }

        string? kind = Text(item, "type");
        if (kind is not ("command_execution" or "file_change" or "mcp_tool_call" or "web_search"))
        {
            return [];
        }

        string id = Required(item, "id");
        if (kind == "mcp_tool_call")
        {
            JsonElement arguments = item.TryGetProperty("arguments", out JsonElement args) ? args : JsonSerializer.SerializeToElement(new JsonObject());
            if (arguments.ValueKind == JsonValueKind.String)
            {
                using JsonDocument parsed = JsonDocument.Parse(arguments.GetString()!);
                arguments = parsed.RootElement.Clone();
            }

            return [new HarnessAction(id, "mcp__" + Required(item, "server") + "__" + Required(item, "tool"), arguments.Clone())];
        }

        var input = new JsonObject();
        foreach (string field in kind switch { "command_execution" => new[] { "command", "cwd" }, "file_change" => ["changes"], _ => ["query"] })
        {
            if (item.TryGetProperty(field, out JsonElement value))
            {
                input[field] = JsonNode.Parse(value.GetRawText());
            }
        }

        return [new HarnessAction(id, kind, JsonSerializer.SerializeToElement(input))];
    }

    private static string Required(JsonElement value, string field) => Text(value, field) ?? throw new JsonException("An invocation has no identity or tool name.");

    private static string? Text(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(field, out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
}
