// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Interim decision prose checks derived from recorded execution and qualification facts.</summary>
public static class DecisionContent
{
    /// <summary>Return the first prose field containing a recorded model identity, without repeating that identity.</summary>
    public static string? BlockedProseField(EstateModel model, JsonElement body)
    {
        ArgumentNullException.ThrowIfNull(model);
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecordEnvelope record in model.Records)
        {
            if (record.Type == "operation" && EstateModel.BodyString(record, "action") == OperationType.AgentRun)
            {
                foreach (string field in new[] { "execution", "executionObserved" })
                    if (record.Body.TryGetProperty(field, out JsonElement execution)) Collect(execution);
            }
            else if (record.Type is "harness-qualification" or "qualification")
            {
                Collect(record.Body);
                if (record.Body.TryGetProperty("evidence", out JsonElement evidence) && evidence.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement reference in evidence.EnumerateArray())
                        if (reference.ValueKind == JsonValueKind.Object && reference.TryGetProperty("id", out JsonElement id)
                            && id.ValueKind == JsonValueKind.String && model.ById.TryGetValue(id.GetString()!, out RecordEnvelope? observed))
                            Collect(observed.Body);
            }
        }
        foreach (string field in new[] { "title", "statement", "summary", "synthesis", "effects", "notAuthorized", "obligations" })
            if (body.TryGetProperty(field, out JsonElement value) && Strings(value).Any(text => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))))
                return field;
        return null;

        void Collect(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Name is "model" or "models" or "modelRevision" or "resolvedRevision")
                        foreach (string term in Strings(property.Value))
                            if (!string.IsNullOrWhiteSpace(term) && term is not ("unknown" or "provider-default")) terms.Add(term);
                    Collect(property.Value);
                }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in value.EnumerateArray()) Collect(item);
        }
    }

    private static IEnumerable<string> Strings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => value.EnumerateArray().SelectMany(Strings),
        JsonValueKind.Object => value.EnumerateObject().SelectMany(p => Strings(p.Value)),
        _ => [],
    };
}
