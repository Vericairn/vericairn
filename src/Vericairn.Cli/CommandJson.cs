// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Json = System.Text.Json.Nodes;

namespace Vericairn.Cli;

internal static class CommandJson
{
    public static JsonElement Element(Json.JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }
}
