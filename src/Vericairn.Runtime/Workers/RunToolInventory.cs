// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Runtime.Workers;

/// <summary>Matches observed tools to the admitted snapshot, including tools of an exactly named MCP server.</summary>
internal sealed class RunToolInventory
{
    private readonly HashSet<string> tools = new(StringComparer.Ordinal);
    private readonly HashSet<string> servers = new(StringComparer.Ordinal);

    internal RunToolInventory(JsonElement selection)
    {
        if (!selection.TryGetProperty("environment", out JsonElement environment) || !environment.TryGetProperty("inventory", out JsonElement inventory)) return;
        foreach (JsonElement component in inventory.EnumerateArray())
        {
            string? kind = component.GetProperty("kind").GetString();
            string id = component.GetProperty("id").GetString()!;
            if (kind is "tool" or "mcp") tools.Add(id);
            if (kind == "mcp") servers.Add(id);
        }
    }

    internal bool Contains(string tool)
    {
        if (tools.Contains(tool)) return true;
        if (!tool.StartsWith("mcp__", StringComparison.Ordinal)) return false;
        int separator = tool.IndexOf("__", 5, StringComparison.Ordinal);
        return separator > 5 && separator + 2 < tool.Length && tool.IndexOf("__", separator + 2, StringComparison.Ordinal) < 0
            && servers.Contains(tool[5..separator]) && RunExecutionObservation.Safe(tool);
    }
}
