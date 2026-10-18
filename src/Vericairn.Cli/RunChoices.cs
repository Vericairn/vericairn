// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Json = System.Text.Json.Nodes;

namespace Vericairn.Cli;

internal sealed record RunChoices(string? Configuration, string[] Execution, long? InputTokens, long? OutputTokens, string? ContextPackage, long? ContextBytes = null)
{
    public static bool TryParse(RunChoices? choices, out JsonElement? execution, out JsonElement? allocation, out string? problem)
    {
        execution = null;
        allocation = null;
        Dictionary<string, string>? values = CommandSettings.Parse(choices?.Execution ?? [], out problem);
        if (values is null) return false;
        if (values.Count > 0) execution = JsonSerializer.SerializeToElement(values);
        if (choices?.InputTokens is not null || choices?.OutputTokens is not null || choices?.ContextBytes is not null)
        {
            var counts = new Json.JsonObject();
            if (choices.InputTokens is long input) counts["inputTokens"] = input;
            if (choices.OutputTokens is long output) counts["outputTokens"] = output;
            if (choices.ContextBytes is long maxBytes) counts["maxBytes"] = maxBytes;
            allocation = CommandJson.Element(counts);
        }
        return true;
    }
}
