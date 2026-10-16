// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Admission of a launcher's effective inventory; an installation alone grants no authority.</summary>
public static class RunEnvironment
{
    private static readonly string[] Kinds = ["harness", "tool", "skill", "plugin", "hook", "mcp", "helper", "dependency", "configuration"];
    private static readonly string[] LocalDestinations = ["local", "estate"];
    private static readonly string[] RunVariables = ["VERICAIRN_RUN", "VERICAIRN_STOP_FILE", "VERICAIRN_BRIEF", "VERICAIRN_MCP_CONFIG", "VERICAIRN_CONDITION"];

    /// <summary>Records identities only after the observed inventory matches the configured admission policy.</summary>
    public static JsonObject Admit(RunEnvironmentPolicy? policy, RunPreflight preflight, IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string>? destinations = null, IReadOnlyList<string>? launcherNames = null)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(environment);
        if (policy is null || policy.Components is null || preflight.Components is null || !preflight.Complete || !preflight.InheritsEnvironment)
        {
            throw new ProviderRefusedException("The launcher has not established a complete inventory with inherited environment limits; dispatch is unsupported.");
        }

        var allowedNames = WorkerEnvironment.Build(environment, launcherNames).Keys.Concat(RunVariables).ToHashSet(WorkerEnvironment.Names);
        if (!RunExecutionObservation.Safe(policy.Id) || !RunExecutionObservation.Safe(preflight.HarnessVersion)
            || environment.Keys.Distinct(WorkerEnvironment.Names).Count() != environment.Count
            || environment.Keys.Any(n => !WorkerEnvironment.IsVariableName(n) || !allowedNames.Contains(n) || WorkerEnvironment.IsCredential(n) || WorkerEnvironment.IsLoadDirective(n)))
        {
            throw new ProviderRefusedException("The effective environment contains an unadmitted variable or loader directive, or an invalid policy identity.");
        }

        if (policy.Components.Any(c => c is null || c.Environment is null || c.Dependencies is null || c.Destinations is null)
            || preflight.Components.Any(c => c is null || c.Environment is null || c.Dependencies is null || c.Destinations is null)
            || preflight.Components.Count is 0 or > 1000 || !preflight.Components.Any(c => c.Kind == "harness")
            || preflight.Components.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != preflight.Components.Count)
        {
            throw new ProviderRefusedException("The effective inventory must name its harness and every component once, within the inventory bound.");
        }

        HashSet<string> ids = preflight.Components.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (RunComponent component in preflight.Components)
        {
            if (!RunExecutionObservation.Safe(component.Id) || !RunExecutionObservation.Safe(component.Version)
                || !Kinds.Contains(component.Kind, StringComparer.Ordinal) || !ContentHash.IsValid(component.Hash) || !ContentHash.IsValid(component.ConfigurationHash)
                || component.Environment.Any(n => !WorkerEnvironment.IsVariableName(n) || !allowedNames.Contains(n) || !environment.ContainsKey(n) || WorkerEnvironment.IsCredential(n) || WorkerEnvironment.IsLoadDirective(n))
                || component.Dependencies.Any(d => !ids.Contains(d))
                || component.Destinations.Any(d => !(destinations ?? LocalDestinations).Contains(d, StringComparer.Ordinal)))
            {
                throw new ProviderRefusedException("An effective component has an invalid identity, undeclared dependency, unadmitted environment need or undeclared route.");
            }
        }

        Dictionary<string, string> spellings = environment.Keys.ToDictionary(n => n, n => n, WorkerEnvironment.Names);
        JsonNode inventory = Inventory(preflight.Components, spellings);
        if (!JsonNode.DeepEquals(inventory, Inventory(policy.Components, spellings)))
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

    private static JsonNode Inventory(IReadOnlyList<RunComponent> components, IReadOnlyDictionary<string, string> spellings) => JsonSerializer.SerializeToNode(components.OrderBy(c => c.Id, StringComparer.Ordinal)
        .Select(c => c with { Environment = c.Environment.Select(n => spellings.GetValueOrDefault(n, n)).Order(StringComparer.Ordinal).ToArray(), Dependencies = c.Dependencies.Order(StringComparer.Ordinal).ToArray(),
            Destinations = c.Destinations.Order(StringComparer.Ordinal).ToArray() }), ProtocolJson.Options)!;
}
