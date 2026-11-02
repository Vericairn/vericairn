// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Runtime.Workers;

/// <summary>The shared, path-free presentation of the measured adapter result.</summary>
public static class AdapterPresentation
{
    /// <summary>Prints measured eligible UTF-8 bytes, never inferred tokens or provider billing.</summary>
    public static string Describe(JsonElement adapter) => "adapter " + adapter.GetProperty("name").GetString() + ": "
        + adapter.GetProperty("state").GetString() + "; eligible bytes " + adapter.GetProperty("eligibleBeforeBytes").GetRawText()
        + " -> " + adapter.GetProperty("eligibleAfterBytes").GetRawText() + "; requests " + adapter.GetProperty("requests").GetRawText()
        + "; fallbacks " + adapter.GetProperty("fallbacks").GetRawText() + "; tokens unknown"
        + (adapter.TryGetProperty("address", out JsonElement address) && address.ValueKind == JsonValueKind.String ? "; " + address.GetString() : "");
}
