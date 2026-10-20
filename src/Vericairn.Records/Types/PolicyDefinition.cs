// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;

namespace Vericairn.Records.Types;

/// <summary>Recognizes a prepared policy body without resolving estate references or requiring approval.</summary>
internal static class PolicyDefinition
{
    internal static string Hash(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw Invalid("Supply the body object, without a record envelope.");

        foreach (string name in new[] { "key", "title", "owner", "reason", "state", "level", "startsAt" })
            Text(body, name);
        JsonElement selection = Field(body, "appliesTo", JsonValueKind.Object);
        Text(selection, "kind");
        PositiveInteger(body, "maxExceptionDays");
        JsonElement rules = Field(body, "rules", JsonValueKind.Array);
        if (rules.GetArrayLength() == 0) throw Invalid("'rules' must contain at least one rule.");
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            Text(rule, "key");
            Text(rule, "kind");
            PositiveInteger(rule, "version");
            Field(rule, "parameters", JsonValueKind.Object);
        }
        if (body.TryGetProperty("basis", out _)) Field(body, "basis", JsonValueKind.Array);
        if (body.TryGetProperty("body", out _) || body.TryGetProperty("type", out _) || body.TryGetProperty("format", out _))
            throw Invalid("Supply the body object, without a record envelope.");

        // Check the whole JSON value, including basis, for the record format's canonical restrictions.
        // Only the hash omits basis; declaration and approval use this exact implementation.
        CanonicalJson.Serialize(body);
        return PolicyRequirements.DefinitionHash(body);
    }

    private static JsonElement Field(JsonElement value, string name, JsonValueKind kind)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out JsonElement field) || field.ValueKind != kind)
            throw Invalid($"'{name}' must be a JSON {kind.ToString().ToLowerInvariant()}.");
        return field;
    }

    private static void Text(JsonElement value, string name)
    {
        if (string.IsNullOrWhiteSpace(Field(value, name, JsonValueKind.String).GetString()))
            throw Invalid($"'{name}' must be nonempty.");
    }

    private static void PositiveInteger(JsonElement value, string name)
    {
        if (!Field(value, name, JsonValueKind.Number).TryGetInt64(out long number) || number < 1)
            throw Invalid($"'{name}' must be a positive integer.");
    }

    private static CanonicalJsonException Invalid(string reason) => new("Expected a prepared policy or duty body. " + reason);
}
