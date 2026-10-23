// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>A named, local launcher configuration. Settings are machine bindings and never copied into records.</summary>
/// <param name="Name">Portable configuration identity.</param>
/// <param name="Launcher">The launcher served.</param>
/// <param name="Policy">Explicit choices and restrictions, in the recorded execution-policy form.</param>
/// <param name="Models">The model selectors this configuration admits; no ambient model is admitted.</param>
/// <param name="Settings">Local launcher settings.</param>
/// <param name="EnvironmentPolicy">The closed inventory admitted for this configuration.</param>
/// <param name="Efforts">Optional narrower native effort allowlist; it cannot add to the launcher's vocabulary.</param>
public sealed record RunConfiguration(string Name, string Launcher, JsonElement Policy, IReadOnlyList<string> Models, IReadOnlyDictionary<string, string>? Settings = null, RunEnvironmentPolicy? EnvironmentPolicy = null, IReadOnlyList<string>? Efforts = null)
{
    /// <summary>Portable configuration identity.</summary>
    public string Name { get; init; } = Name;

    /// <summary>The launcher served.</summary>
    public string Launcher { get; init; } = Launcher;

    /// <summary>Explicit choices and restrictions, in the recorded execution-policy form.</summary>
    public JsonElement Policy { get; init; } = Policy;

    /// <summary>The model selectors this configuration admits; no ambient model is admitted. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> Models
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(Models);

    /// <summary>Local launcher settings. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyDictionary<string, string>? Settings
    {
        get;
        init => field = OptionCollections.OptionalDictionary(value);
    } = OptionCollections.OptionalDictionary(Settings);

    /// <summary>The closed inventory admitted for this configuration.</summary>
    public RunEnvironmentPolicy? EnvironmentPolicy { get; init; } = EnvironmentPolicy;

    /// <summary>Optional narrower native effort allowlist; it cannot add to the launcher's vocabulary. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string>? Efforts
    {
        get;
        init => field = OptionCollections.OptionalList(value);
    } = OptionCollections.OptionalList(Efforts);
}

/// <summary>The scopes and values a launcher implements, independently of a configuration's narrower restrictions.</summary>
/// <param name="Scopes">Supported choice scopes, in precedence order.</param>
/// <param name="Efforts">The harness's own effort vocabulary; empty only when there is no effort control.</param>
/// <param name="Providers">Supported model-provider identities.</param>
/// <param name="Destinations">Supported logical inference destinations.</param>
/// <param name="DataBoundaries">Supported protection policies, not inferred from a prompt.</param>
/// <param name="ProviderDefault">Whether an explicit provider-default effort choice is supported.</param>
/// <param name="ClosedEnvironment">Whether the launcher implements complete effective inventory inspection and inherited environment controls.</param>
public sealed record RunCapabilities(IReadOnlyList<string> Scopes, IReadOnlyList<string> Efforts, IReadOnlyList<string> Providers,
    IReadOnlyList<string> Destinations, IReadOnlyList<string> DataBoundaries, bool ProviderDefault = false, bool ClosedEnvironment = false)
{
    /// <summary>Supported choice scopes, in precedence order. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> Scopes
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(Scopes);

    /// <summary>The harness's own effort vocabulary; empty only when there is no effort control. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> Efforts
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(Efforts);

    /// <summary>Supported model-provider identities. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> Providers
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(Providers);

    /// <summary>Supported logical inference destinations. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> Destinations
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(Destinations);

    /// <summary>Supported protection policies, not inferred from a prompt. Copied into owned read-only storage on initialization.</summary>
    public IReadOnlyList<string> DataBoundaries
    {
        get;
        init => field = OptionCollections.List(value);
    } = OptionCollections.List(DataBoundaries);

    /// <summary>Whether an explicit provider-default effort choice is supported.</summary>
    public bool ProviderDefault { get; init; } = ProviderDefault;

    /// <summary>Whether the launcher implements complete effective inventory inspection and inherited environment controls.</summary>
    public bool ClosedEnvironment { get; init; } = ClosedEnvironment;

    /// <summary>A launcher without execution support admits no choices.</summary>
    public static RunCapabilities Unsupported { get; } = new([], [], [], [], []);
}

/// <summary>One policy source; record identities and hashes pin the exact revision that supplied it.</summary>
/// <param name="Scope">launcher, roster, goal, assignment or run.</param>
/// <param name="Source">A portable identity, including a role suffix where applicable.</param>
/// <param name="Policy">Choices and non-overridable restrictions.</param>
/// <param name="Record">The contributing record, when any.</param>
/// <param name="Hash">Its exact hash, when any.</param>
public sealed record RunPolicySource(string Scope, string Source, JsonElement Policy, string? Record = null, string? Hash = null);

/// <summary>Deterministic execution selection; communication settings are never an input.</summary>
public static class RunSelection
{
    /// <summary>The precedence shared by every launcher, which may support fewer scopes.</summary>
    public static readonly ImmutableArray<string> Scopes = ["launcher", "roster", "goal", "assignment", "run"];

    /// <summary>Resolve choices and retain every contributing source, including choices later overridden.</summary>
    public static JsonObject Resolve(RunConfiguration configuration, RunCapabilities capabilities, IEnumerable<RunPolicySource> sources, JsonElement? overrides, JsonElement cap)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(sources);
        if (!ExecutionPolicy.IsIdentity(configuration.Name) || !ExecutionPolicy.IsIdentity(configuration.Launcher))
        {
            throw new ProviderRefusedException("A launcher configuration needs portable identities.");
        }
        if (capabilities.Efforts.Any(e => e is "not-supported" or "provider-default"))
        {
            throw new ProviderRefusedException("A launcher's native effort vocabulary cannot contain the reserved not-supported or provider-default choices.");
        }

        List<RunPolicySource> layers = [new("launcher", configuration.Name, configuration.Policy), .. sources];
        if (overrides is JsonElement explicitChoices)
        {
            layers.Add(new("run", "explicit-run", JsonSerializer.SerializeToElement(new { choices = explicitChoices })));
        }

        layers = layers.OrderBy(s => Scopes.IndexOf(s.Scope)).ToList();
        var requested = new JsonObject();
        var provenance = new JsonArray();
        foreach (RunPolicySource source in layers)
        {
            var findings = new FindingList();
            ExecutionPolicy.Validate(new BodyReader(source.Policy, "execution", findings, 2));
            if (findings.HasErrors || !Scopes.Contains(source.Scope, StringComparer.Ordinal) || !capabilities.Scopes.Contains(source.Scope, StringComparer.Ordinal))
            {
                throw new ProviderRefusedException("The execution policy is invalid or uses a scope this launcher does not support.");
            }

            if (source.Policy.TryGetProperty("choices", out JsonElement choices))
            {
                foreach (JsonProperty choice in choices.EnumerateObject())
                {
                    if (choice.Name == "adapter" && choice.Value.GetString() != "disabled")
                    {
                        throw new ProviderRefusedException("The efficiency Adapter is unsupported; every run keeps it disabled. EFF-01 belongs to release 0.8.4.");
                    }
                    requested[choice.Name] = choice.Value.GetString();
                }
            }

            var entry = new JsonObject { ["scope"] = source.Scope, ["source"] = source.Source, ["policy"] = JsonNode.Parse(source.Policy.GetRawText()) };
            if (source.Record is not null)
            {
                entry["record"] = source.Record;
                entry["hash"] = source.Hash;
            }

            provenance.Add(entry);
        }

        Require("model", configuration.Models);
        Require("provider", capabilities.Providers);
        Require("destination", capabilities.Destinations);
        Require("dataBoundary", capabilities.DataBoundaries);
        IReadOnlyList<string> efforts = capabilities.Efforts.Count == 0 ? ["not-supported"]
            : capabilities.ProviderDefault ? [.. capabilities.Efforts, "provider-default"] : capabilities.Efforts;
        Require("effort", configuration.Efforts is null ? efforts : efforts.Intersect(configuration.Efforts, StringComparer.Ordinal).ToArray());

        foreach (RunPolicySource source in layers)
        {
            if (source.Policy.TryGetProperty("restrictions", out JsonElement restrictions))
            {
                Restrict(requested, restrictions, cap);
            }
        }

        return new JsonObject
        {
            ["configuration"] = configuration.Name,
            ["configurationHash"] = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(configuration)))),
            ["requested"] = requested,
            ["provenance"] = provenance,
            ["resolvedRevision"] = "unknown",
        };

        void Require(string field, IReadOnlyList<string> allowed)
        {
            if (requested[field]?.GetValue<string>() is not string value || value == "unknown" || !allowed.Contains(value, StringComparer.Ordinal))
            {
                throw new ProviderRefusedException($"The required {field} is unresolved or unsupported by the launcher configuration; ambient fallback is not permitted.");
            }
        }
    }

    /// <summary>Retains an observable alias resolution; unknown never satisfies an exact revision restriction.</summary>
    public static void ResolveRevision(JsonObject selection, string? revision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (revision is not null && !ExecutionPolicy.IsIdentity(revision))
        {
            throw new ProviderRefusedException("The reported model revision is not a portable identity.");
        }

        foreach (JsonNode? source in selection["provenance"]!.AsArray())
        {
            if (source?["policy"]?["restrictions"]?["exactRevision"]?.GetValue<string>() is string exact
                && (revision is null or "unknown" || exact != revision))
            {
                throw new ProviderRefusedException("The observable model revision does not satisfy an inherited exact pin; unknown is not a match.");
            }
        }

        selection["resolvedRevision"] = revision ?? "unknown";
    }

    /// <summary>The recorded default and role policies, without discarding the roster's restrictions when a goal supplies its own role.</summary>
    public static IReadOnlyList<RunPolicySource> Sources(EstateModel model, RecordEnvelope goal, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(assignment);
        string role = WorkView.OrderOfAssignment(model, assignment) is RecordEnvelope order ? Routing.RoleOf(model.Latest(order)) : Routing.DefaultRole;
        var sources = new List<RunPolicySource>();
        Add(RunWatchSettings.Roster(model, goal), "roster", true);
        Add(goal, "goal", true);
        Add(assignment, "assignment", false);
        return sources;

        void Add(RecordEnvelope? record, string scope, bool roles)
        {
            if (record is null)
            {
                return;
            }

            if (record.Body.TryGetProperty("execution", out JsonElement policy))
            {
                sources.Add(new(scope, scope, policy, record.Id, record.ComputedHash));
            }

            if (roles && record.Body.TryGetProperty("roles", out JsonElement entries))
            {
                foreach (JsonElement entry in entries.EnumerateArray().Where(r => r.GetProperty("role").GetString() == role))
                {
                    if (entry.TryGetProperty("execution", out JsonElement rolePolicy))
                    {
                        sources.Add(new(scope, role, rolePolicy, record.Id, record.ComputedHash));
                    }
                }
            }
        }
    }

    private static void Restrict(JsonObject choices, JsonElement restrictions, JsonElement cap)
    {
        foreach ((string restriction, string choice) in new[] { ("requiredModel", "model"), ("requiredEffort", "effort"), ("dataBoundary", "dataBoundary") })
        {
            if (restrictions.TryGetProperty(restriction, out JsonElement value) && choices[choice]?.GetValue<string>() != value.GetString())
            {
                throw new ProviderRefusedException($"The selected {choice} conflicts with an inherited {restriction} restriction.");
            }
        }

        foreach ((string restriction, string choice) in new[] { ("permittedProviders", "provider"), ("permittedDestinations", "destination") })
        {
            if (restrictions.TryGetProperty(restriction, out JsonElement allowed) && !allowed.EnumerateArray().Any(v => v.GetString() == choices[choice]?.GetValue<string>()))
            {
                throw new ProviderRefusedException($"The selected {choice} is outside an inherited {restriction} restriction.");
            }
        }

        if (restrictions.TryGetProperty("maxCurrency", out JsonElement cost)
            && (AgentRuns.Amount(cap, "currency") is not long amount || amount > cost.GetProperty("amount").GetInt64() || AgentRuns.CurrencyUnit(cap) != cost.GetProperty("unit").GetString()))
        {
            throw new ProviderRefusedException("The run's currency reservation is absent or exceeds an inherited cost bound.");
        }
    }
}
