// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;

namespace Vericairn.Protocol;

/// <summary>The typed progress value shared by OpenAPI and MCP discovery. The runtime also validates every submission.</summary>
public static class ProgressSchemas
{
    /// <summary>A fresh schema for the three exclusive value shapes; counts stay counts and percentages are estimates.</summary>
    public static JsonObject Value() => new()
    {
        ["type"] = "object",
        ["description"] = "A contributor claim, never worker liveness or acceptance. Integer percentages are always estimates.",
        ["oneOf"] = new JsonArray(
            Shape("accurate", new JsonObject
            {
                ["done"] = Count(), ["total"] = Count(),
                ["unit"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 100 },
                ["estimatePercent"] = Percent(),
            }, "type", "done", "unit"),
            Shape("best-effort", new JsonObject { ["percent"] = Percent() }, "type", "percent"),
            Shape("not-measurable", new JsonObject(), "type")),
    };

    /// <summary>A fresh schema for an optional short status sentence.</summary>
    public static JsonObject StatusText() => new()
    {
        ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 500,
        ["description"] = "Nonblank status sentence on one line; required when the effective progress request says so.",
    };

    private static JsonObject Shape(string type, JsonObject properties, params string[] required)
    {
        properties["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(type) };
        return new JsonObject
        {
            ["type"] = "object", ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()), ["additionalProperties"] = false,
        };
    }

    private static JsonObject Count() => new() { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 9_007_199_254_740_991 };

    private static JsonObject Percent() => new() { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 100, ["description"] = "Labelled best-effort estimate." };
}
