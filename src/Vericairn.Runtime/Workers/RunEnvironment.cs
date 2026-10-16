// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Admission of a launcher's effective inventory; an installation alone grants no authority.</summary>
public static class RunEnvironment
{
    /// <summary>Records identities only after the observed inventory matches the configured admission policy.</summary>
    public static JsonObject Admit(RunEnvironmentPolicy? policy, RunPreflight preflight, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(environment);
        if (policy is null || !preflight.Complete || !preflight.InheritsEnvironment)
        {
            throw new ProviderRefusedException("The launcher has not established a complete inventory with inherited environment limits; dispatch is unsupported.");
        }

        JsonNode inventory = JsonSerializer.SerializeToNode(preflight.Components, ProtocolJson.Options)!;
        if (!JsonNode.DeepEquals(inventory, JsonSerializer.SerializeToNode(policy.Components, ProtocolJson.Options)))
        {
            throw new ProviderRefusedException("The effective tool, configuration or dependency inventory differs from the admitted policy.");
        }

        return new JsonObject
        {
            ["policy"] = policy.Id,
            ["inventoryHash"] = RunExecutionObservation.Hash(inventory),
            ["inventory"] = inventory,
            ["variables"] = new JsonArray(environment.Keys.Order(StringComparer.Ordinal).Select(n => (JsonNode)n).ToArray()),
        };
    }
}
