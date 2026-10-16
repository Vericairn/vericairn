// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

internal static class HarnessSelection
{
    public static (string Model, string Effort) Requested(LaunchContext context)
    {
        if (context.Execution is not JsonElement execution || !execution.TryGetProperty("requested", out JsonElement requested)
            || !requested.TryGetProperty("model", out JsonElement model) || !requested.TryGetProperty("effort", out JsonElement effort)
            || model.ValueKind != JsonValueKind.String || effort.ValueKind != JsonValueKind.String
            || !ExecutionPolicy.IsIdentity(model.GetString()!) || !ExecutionPolicy.IsIdentity(effort.GetString()!))
        {
            throw new ProviderRefusedException("An explicit model and effort selection is required; the launcher never uses an ambient choice.");
        }

        return (model.GetString()!, effort.GetString()!);
    }

    public static HarnessExecution? Read(string line, string type, string? subtype = null)
    {
        if (!line.TrimStart().StartsWith('{')) return null;
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != type || (subtype is not null && Text(root, "subtype") != subtype)) return null;
        return new HarnessExecution(Text(root, "model"), Text(root, "model_revision"), Text(root, "effort"),
            Text(root, "provider"), Text(root, "destination"), Text(root, "harness_version") ?? Text(root, "claude_code_version"),
            root.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array
                ? tools.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString()! : "unknown").ToArray() : null, Text(root, "inventory_hash"));
    }

    private static string? Text(JsonElement element, string field) => element.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
