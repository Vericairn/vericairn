// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

internal static class EstateServiceValues
{
    internal static JsonElement Element(JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }

    internal static RecordEnvelope? Latest(EstateModel model, string id, string type) =>
        model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == type ? model.Latest(record) : null;

    internal static WriteOutcome Refuse(string message) => WriteOutcome.Fail(new Problem("refused", message, []));

    internal static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    internal static string PrincipalName(EstateModel model, string? id) =>
        model.CurrentPrincipal(id) is RecordEnvelope principal ? EstateModel.BodyString(principal, "name") ?? id ?? "?" : id ?? "?";
}
