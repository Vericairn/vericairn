// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>An effective component, including its configuration, dependencies, environment needs and routes; all are portable identities.</summary>
/// <param name="Id">Logical component or tool name.</param>
/// <param name="Kind">harness, tool, skill, plugin, hook, mcp, helper, dependency or configuration.</param>
/// <param name="Version">Reported version, or unknown.</param>
/// <param name="Hash">Digest of the component's exact content.</param>
/// <param name="ConfigurationHash">Digest of its effective configuration, excluding credentials and machine bindings.</param>
/// <param name="Environment">The environment variable names it needs, never their values.</param>
/// <param name="Destinations">Logical routes it uses.</param>
/// <param name="Dependencies">Other component identities in the inventory.</param>
public sealed record RunComponent(string Id, string Kind, string Version, string Hash, string ConfigurationHash,
    IReadOnlyList<string> Environment, IReadOnlyList<string> Destinations, IReadOnlyList<string> Dependencies);

/// <summary>A local admission policy for the complete harness dependency tree.</summary>
/// <param name="Id">The policy's portable identity.</param>
/// <param name="Components">The exact inventory admitted by the operator's configuration.</param>
public sealed record RunEnvironmentPolicy(string Id, IReadOnlyList<RunComponent> Components);

/// <summary>What a launcher's preflight established after preparing isolated configuration, before intent or dispatch.</summary>
/// <param name="Components">Everything effectively loaded, including user and repository configuration and dependencies.</param>
/// <param name="Complete">Whether every load and route has been accounted for.</param>
/// <param name="InheritsEnvironment">Whether every component uses only the worker's admitted environment.</param>
/// <param name="HarnessVersion">Observable harness version, or unknown.</param>
/// <param name="ModelRevision">Observable resolution of the requested model selector; null means unknown.</param>
public sealed record RunPreflight(IReadOnlyList<RunComponent> Components, bool Complete, bool InheritsEnvironment, string HarnessVersion = "unknown", string? ModelRevision = null);

/// <summary>Facts a harness event actually reports; null means the event supplies no such fact.</summary>
/// <param name="Model">Reported model selector.</param>
/// <param name="ModelRevision">Reported exact revision.</param>
/// <param name="Effort">Reported effort, in the harness's vocabulary.</param>
/// <param name="Provider">Reported model provider.</param>
/// <param name="Destination">Reported route.</param>
/// <param name="HarnessVersion">Reported harness version.</param>
/// <param name="Tools">Reported effective tool identities.</param>
/// <param name="InventoryHash">Reported identity of the complete effective inventory, when exposed.</param>
public sealed record HarnessExecution(string? Model = null, string? ModelRevision = null, string? Effort = null,
    string? Provider = null, string? Destination = null, string? HarnessVersion = null, IReadOnlyList<string>? Tools = null, string? InventoryHash = null);

/// <summary>Retains reported execution facts separately from the admitted request, with bounded, explicit unknowns.</summary>
public sealed class RunExecutionObservation
{
    private static readonly string[] AllocationFields = ["inputTokens", "outputTokens"];
    private static readonly string[] Facts = ["model", "modelRevision", "effort", "provider", "destination", "harnessVersion", "tools", "inventoryHash"];
    private readonly JsonObject selection;
    private readonly JsonArray reports = [];
    private readonly HashSet<string> known = new(StringComparer.Ordinal);
    private readonly HashSet<string> discrepancies = new(StringComparer.Ordinal);
    private bool incomplete;

    /// <summary>Starts observation from an immutable intent snapshot.</summary>
    public RunExecutionObservation(JsonElement selection) => this.selection = JsonNode.Parse(selection.GetRawText())!.AsObject();

    /// <summary>Reads a harness report, never narrative or tool output. Invalid identities remain unknown.</summary>
    public void Add(HarnessExecution report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (reports.Count >= 128)
        {
            incomplete = true;
            return;
        }

        var entry = new JsonObject();
        foreach ((string field, string? value) in new[] { ("model", report.Model), ("modelRevision", report.ModelRevision), ("effort", report.Effort),
            ("provider", report.Provider), ("destination", report.Destination), ("harnessVersion", report.HarnessVersion), ("inventoryHash", report.InventoryHash) })
        {
            if (value is null)
            {
                continue;
            }

            if (!Safe(value) || value is "unknown" or "provider-default" || (field == "inventoryHash" && !ContentHash.IsValid(value)))
            {
                incomplete = true;
                continue;
            }

            entry[field] = value;
            known.Add(field);
            string? requested = field switch
            {
                "modelRevision" => selection["resolvedRevision"]?.GetValue<string>(),
                "harnessVersion" => selection["harnessVersion"]?.GetValue<string>(),
                "inventoryHash" => selection["environment"]?["inventoryHash"]?.GetValue<string>(),
                _ => selection["requested"]?[field]?.GetValue<string>(),
            };
            bool resolvedAlias = field == "model" && value == selection["resolvedRevision"]?.GetValue<string>();
            if (!resolvedAlias && requested is not null and not "unknown" and not "provider-default" and not "not-supported" && requested != value)
            {
                discrepancies.Add(field);
            }
        }

        if (report.Tools is not null)
        {
            if (report.Tools.Count > 1000 || report.Tools.Any(t => !Safe(t)))
            {
                incomplete = true;
            }
            else
            {
                entry["tools"] = new JsonArray(report.Tools.Order(StringComparer.Ordinal).Select(t => (JsonNode)t).ToArray());
                known.Add("tools");
                HashSet<string> admitted = selection["environment"]?["inventory"]?.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
                if (report.Tools.Any(t => !admitted.Contains(t)))
                {
                    discrepancies.Add("tools");
                }
            }
        }

        if (entry.Count > 0 && !reports.Any(r => JsonNode.DeepEquals(r, entry)))
        {
            reports.Add(entry);
        }
    }

    /// <summary>Facts still unknown and discrepancies remain explicit even when the process exits successfully.</summary>
    public JsonObject Snapshot() => new()
    {
        ["reports"] = reports.DeepClone(),
        ["unknown"] = new JsonArray(Facts.Where(f => !known.Contains(f)).Concat(incomplete ? ["complete-report"] : []).Select(f => (JsonNode)f).ToArray()),
        ["discrepancies"] = new JsonArray(discrepancies.Order(StringComparer.Ordinal).Select(f => (JsonNode)f).ToArray()),
    };

    internal static bool Safe(string value) => ExecutionPolicy.IsIdentity(value)
        && !EstateChecks.Content("execution.txt", System.Text.Encoding.UTF8.GetBytes(value)).HasErrors;

    internal static string Hash(JsonNode value) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(value)));

    internal static JsonObject Allocation(JsonElement? allocation, JsonElement cap)
    {
        var result = new JsonObject();
        if (allocation is JsonElement supplied)
        {
            var findings = new Records.Model.FindingList();
            var reader = new BodyReader(supplied, "allocation", findings, 2);
            long? input = reader.Number("inputTokens", false, 0, CanonicalJson.MaxSafeInteger);
            long? output = reader.Number("outputTokens", false, 0, CanonicalJson.MaxSafeInteger);
            reader.NoOtherKeys();
            if (findings.HasErrors || supplied.ValueKind != JsonValueKind.Object || (input ?? 0) + (output ?? 0) > (AgentRuns.Amount(cap, "tokens") ?? long.MaxValue))
            {
                throw new ProviderRefusedException("The context allocation is invalid or exceeds the run's token reservation.");
            }

            if (input is not null) result["inputTokens"] = input;
            if (output is not null) result["outputTokens"] = output;
        }

        result["unknown"] = new JsonArray(AllocationFields.Where(f => !result.ContainsKey(f)).Select(f => (JsonNode)f).ToArray());
        return result;
    }
}
