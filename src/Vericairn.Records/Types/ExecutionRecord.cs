// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Representation;
using Vericairn.Records.Search;

namespace Vericairn.Records.Types;

internal static class ExecutionRecord
{
    public static void Read(ValidationContext context, string? status)
    {
        BodyReader body = context.Body;
        BodyReader? execution = body.Since(2, "execution") ? body.Nested("execution", false) : null;
        BodyReader? observed = body.Since(2, "executionObserved") ? body.Nested("executionObserved", false) : null;
        if (execution is not null)
        {
            Identity(execution, "configuration");
            execution.Hash("configurationHash", true);
            Identity(execution, "resolvedRevision");
            Identity(execution, "harnessVersion");
            if (execution.Nested("harnessAdmission", false) is BodyReader admission) Harness(context, execution, admission);
            Identity(execution, "role");
            string? adapter = execution.Enum("adapter", true, "disabled", RepresentationContract.LocalLossless);
            if (adapter == RepresentationContract.LocalLossless) _ = execution.Since(3, "adapter");
            if (adapter == RepresentationContract.LocalLossless && !execution.Has("adapterAddress"))
                execution.Error("body.missing", "adapterAddress", "An admitted adapter records its loopback authority, or null for startup fallback.");
            if (execution.Since(3, "adapterAddress") && execution.Has("adapterAddress"))
            {
                if (execution.Element.GetProperty("adapterAddress").ValueKind == JsonValueKind.Null) execution.Allow("adapterAddress");
                else Loopback(execution, "adapterAddress");
                if (adapter == "disabled") context.Error("operation.execution", "A disabled adapter has no hook address.");
            }
            string? package = execution.Text("contextPackage", true, 71);
            if (package is not null and not "unknown" && !Hashing.ContentHash.IsValid(package))
            {
                context.Error("operation.execution", "A context package is its exact content hash or unknown.");
            }

            // Dispatch-time searches, fixed at intent with the package they selected for (format 3, additive).
            if (execution.Since(3, "contextSelections") && execution.Has("contextSelections"))
            {
                IReadOnlyList<BodyReader> selections = execution.NestedArray("contextSelections", true, allowEmpty: false) ?? [];
                if (selections.Count > SearchSelections.MaxContextSelections)
                    execution.Error("search.selections", "contextSelections", $"A run's intent records at most {SearchSelections.MaxContextSelections} context selections.");
                foreach (BodyReader selection in selections) SearchSelections.Validate(selection, recordReaders: true);
                if (package is null or "unknown") context.Error("operation.execution", "Context selections pin the exact package they selected for.");
            }

            if (execution.Nested("requested", true) is BodyReader requested)
            {
                ExecutionPolicy.ReadChoices(requested);
                foreach (string required in new[] { "model", "effort", "provider", "destination", "dataBoundary" })
                {
                    if (!requested.Has(required)) requested.Error("body.missing", required, "An admitted execution choice is required.");
                    if (requested.Element.TryGetProperty(required, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() == "unknown")
                    {
                        context.Error("operation.execution", "Unknown is not a resolved required execution choice.");
                    }
                }
            }

            foreach (BodyReader source in execution.NestedArray("provenance", true, allowEmpty: false) ?? [])
            {
                string? scope = source.Enum("scope", true, "launcher", "roster", "goal", "assignment", "run");
                Identity(source, "source");
                string? record = source.Ulid("record", false);
                string? hash = source.Hash("hash", record is not null);
                if (source.Nested("policy", true) is BodyReader policy) ExecutionPolicy.Validate(policy);
                if ((scope is "roster" or "goal" or "assignment") != (record is not null))
                {
                    context.Error("operation.execution", "Recorded policy scopes pin their source record; launcher and run scopes have no record source.");
                }
                if (record is not null && scope is not null)
                {
                    RecordEnvelope? origin = context.RequireReference("execution.provenance.record", record, scope);
                    if (origin is not null && origin.ComputedHash != hash) context.Error("operation.execution", "Execution provenance pins the exact contributing record hash.");
                    if (origin is not null && source.Element.TryGetProperty("policy", out JsonElement policyValue)
                        && source.Element.TryGetProperty("source", out JsonElement name) && name.ValueKind == JsonValueKind.String
                        && !MatchesOrigin(origin, scope, name.GetString()!, policyValue))
                    {
                        context.Error("operation.execution", "Execution provenance preserves the policy of the exact named source and role.");
                    }
                }
                source.NoOtherKeys();
            }

            if (execution.Nested("allocation", true) is BodyReader allocation)
            {
                allocation.Number("inputTokens", false, 0, CanonicalJson.MaxSafeInteger);
                allocation.Number("outputTokens", false, 0, CanonicalJson.MaxSafeInteger);
                allocation.StringArray("unknown", true, allowEmpty: true, maxItemLength: 40);
                if (allocation.Nested("context", false) is BodyReader input)
                {
                    input.Enum("units", true, "bytes");
                    long? bytes = input.Number("bytes", true, 1, CanonicalJson.MaxSafeInteger);
                    if (input.Element.TryGetProperty("maxBytes", out JsonElement limit) && limit.ValueKind == JsonValueKind.Null)
                    {
                        input.Allow("maxBytes");
                    }
                    else if (input.Number("maxBytes", true, 1, CanonicalJson.MaxSafeInteger) is long maxBytes && bytes > maxBytes)
                    {
                        context.Error("operation.execution", "The retained context payload fits its explicit byte allocation.");
                    }
                    if (package is null or "unknown") context.Error("operation.execution", "Context accounting pins the exact package identity.");
                    input.NoOtherKeys();
                }
                allocation.NoOtherKeys();
            }

            if (execution.Nested("environment", true) is BodyReader environment)
            {
                Identity(environment, "policy");
                environment.Hash("inventoryHash", true);
                environment.StringArray("variables", true, allowEmpty: true, maxItemLength: 160);
                foreach (BodyReader component in environment.NestedArray("inventory", true, allowEmpty: false) ?? [])
                {
                    Identity(component, "id");
                    component.Enum("kind", true, "harness", "tool", "skill", "plugin", "hook", "mcp", "helper", "dependency", "configuration");
                    Identity(component, "version");
                    component.Hash("hash", true);
                    component.Hash("configurationHash", true);
                    foreach (string field in new[] { "environment", "destinations", "dependencies" }) component.StringArray(field, true, allowEmpty: true, maxItemLength: 160);
                    component.NoOtherKeys();
                }
                environment.NoOtherKeys();
            }
            execution.NoOtherKeys();
            if (!context.Findings.HasErrors) Consistency(context, execution.Element);
        }

        if (observed is not null)
        {
            foreach (BodyReader report in observed.NestedArray("reports", true, allowEmpty: true) ?? [])
            {
                foreach (string field in new[] { "model", "modelRevision", "effort", "provider", "destination", "harnessVersion" }) Identity(report, field, false);
                report.StringArray("tools", false, allowEmpty: true, maxItemLength: 160);
                report.Hash("inventoryHash", false);
                report.NoOtherKeys();
            }
            observed.StringArray("unknown", true, allowEmpty: true, maxItemLength: 40);
            observed.StringArray("discrepancies", true, allowEmpty: true, maxItemLength: 40);
            if (observed.Since(3, "adapter") && observed.Nested("adapter", false) is BodyReader measurement)
            {
                measurement.Enum("name", true, RepresentationContract.LocalLossless);
                string? state = measurement.Enum("state", true, "engaged", "direct-start-fallback", "direct-request-fallback");
                if (measurement.Has("address") && measurement.Element.GetProperty("address").ValueKind == JsonValueKind.Null) measurement.Allow("address");
                else Loopback(measurement, "address");
                long? requests = measurement.Number("requests", true, 0, CanonicalJson.MaxSafeInteger);
                long? fallbacks = measurement.Number("fallbacks", true, 0, CanonicalJson.MaxSafeInteger);
                long? before = measurement.Number("eligibleBeforeBytes", true, 0, CanonicalJson.MaxSafeInteger);
                long? after = measurement.Number("eligibleAfterBytes", true, 0, CanonicalJson.MaxSafeInteger);
                measurement.Enum("tokens", true, "unknown");
                measurement.NoOtherKeys();
                string? address = measurement.Element.TryGetProperty("address", out JsonElement measuredAddress) && measuredAddress.ValueKind == JsonValueKind.String ? measuredAddress.GetString() : null;
                string? admittedAddress = execution is not null && execution.Element.TryGetProperty("adapterAddress", out JsonElement selectedAddress) && selectedAddress.ValueKind == JsonValueKind.String ? selectedAddress.GetString() : null;
                if (fallbacks > requests || after > before || execution is null
                    || !execution.Element.TryGetProperty("adapter", out JsonElement selected) || selected.ValueKind != JsonValueKind.String || selected.GetString() != RepresentationContract.LocalLossless
                    || address != admittedAddress || (state == "direct-start-fallback") != (address is null)
                    || state == "engaged" && fallbacks != 0
                    || state == "direct-start-fallback" && (requests != 0 || fallbacks != 0 || before != 0 || after != 0))
                    context.Error("operation.execution", "Adapter measurement agrees with the admitted adapter and counts only observed eligible bytes.");
            }
            observed.NoOtherKeys();
            if (execution is null || status is not ("known-failure" or "observed-success")) context.Error("operation.execution", "Execution observations describe a settled run with an admitted execution snapshot.");
        }

        if ((execution is not null || observed is not null) && EstateModel.BodyString(context.Record, "action") != OperationType.AgentRun)
        {
            context.Error("operation.execution", "Execution describes an agent run only.");
        }

        if (execution is not null && status is "known-failure" or "observed-success" && observed is null)
        {
            context.Error("operation.execution", "A settled admitted run records execution observations, including unknowns.");
        }

        if (context.Find(context.Record.Supersedes) is RecordEnvelope previous)
        {
            bool had = previous.Body.TryGetProperty("execution", out JsonElement prior);
            if (had != (execution is not null) || (had && CanonicalJson.Serialize(prior) != CanonicalJson.Serialize(execution!.Element)))
            {
                context.Error("operation.execution-kept", "Execution is fixed in the intent; a different selection needs a new admitted run.");
            }
        }
    }

    /// <summary>
    /// A settled agent run's observed searches: those its agent made through the run's MCP server, recorded once at settlement
    /// (format 3, additive) and kept by every later revision.
    /// </summary>
    public static void ReadObservedSearches(ValidationContext context, string? status)
    {
        BodyReader body = context.Body;
        if (!body.Since(3, "searchesObserved") || body.Nested("searchesObserved", false) is not BodyReader observed) return;
        SearchSelections.ValidateObserved(observed);
        if (EstateModel.BodyString(context.Record, "action") != OperationType.AgentRun || status is not ("known-failure" or "observed-success"))
            context.Error("operation.searches-observed", "Observed searches belong to a settled agent run.");
        if (context.Find(context.Record.Supersedes) is RecordEnvelope previous && EstateModel.BodyString(previous, "status") is "known-failure" or "observed-success"
            && !previous.Body.TryGetProperty("searchesObserved", out _))
            context.Error("operation.searches-observed", "Observed searches are recorded at settlement, never added afterwards.");
    }

    private static void Identity(BodyReader reader, string field, bool required = true)
    {
        if (reader.Text(field, required, 160) is string value && !ExecutionPolicy.IsIdentity(value)) reader.Error("execution.identity", field, "A portable execution identity contains no machine path or environment value.");
    }

    private static void Loopback(BodyReader reader, string field)
    {
        if (reader.Text(field, true, 64) is string address && (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port is <= 0 or > 65535 || uri.UserInfo.Length != 0
            || address != uri.GetLeftPart(UriPartial.Authority)))
            reader.Error("execution.adapter-address", field, "An adapter address is an IPv4 loopback HTTP authority only, with no path or credential.");
    }

    private static void Harness(ValidationContext context, BodyReader execution, BodyReader admission)
    {
        string? kind = admission.Enum("kind", true, "baseline", "qualification");
        string? launcher = admission.Enum("launcher", true, "codex", "claude-code");
        string? version = admission.Text("version", true, 80);
        if (version is not null && (!HarnessQualificationType.ExactVersion(version)
            || !execution.Element.TryGetProperty("harnessVersion", out JsonElement harnessVersion) || harnessVersion.ValueKind != JsonValueKind.String || harnessVersion.GetString() != version))
            context.Error("operation.harness-admission", "Harness admission names the execution's exact version.");
        if (launcher != EstateModel.BodyString(context.Record, "provider"))
            context.Error("operation.harness-admission", "Harness admission names the operation's launcher.");
        BodyReader? probes = admission.Nested("probes", true);
        if (probes is not null)
        {
            foreach (string name in HarnessQualificationType.ProbeNames) probes.Hash(name, true);
            probes.NoOtherKeys();
        }
        if (kind == "qualification")
        {
            string? id = admission.Ulid("record", true);
            string? hash = admission.Hash("hash", true);
            RecordEnvelope? qualification = context.RequireReference("execution.harnessAdmission.record", id, "harness-qualification");
            RecordEnvelope intent = context.Model.ById[context.Model.Chain(context.Record)[0]];
            if (qualification is not null && (qualification.ComputedHash != hash
                || EstateModel.BodyString(qualification, "status") != "qualified"
                || EstateModel.BodyString(qualification, "launcher") != launcher || EstateModel.BodyString(qualification, "version") != version
                || !GoalRules.Before(qualification, intent)
                || (context.Model.SupersededBy.TryGetValue(qualification.Id, out string? next) && GoalRules.Before(context.Model.ById[next], intent))
                || probes is null || !qualification.Body.TryGetProperty("probes", out JsonElement recorded)
                || CanonicalJson.Serialize(recorded) != CanonicalJson.Serialize(probes.Element)))
                context.Error("operation.harness-admission", "Harness admission pins the matching qualification and probe digests, unrevoked when the intent was recorded.");
        }
        admission.NoOtherKeys();
    }

    private static bool MatchesOrigin(RecordEnvelope origin, string scope, string name, JsonElement policy) =>
        (name == scope && origin.Body.TryGetProperty("execution", out JsonElement defaults) && CanonicalJson.Serialize(defaults) == CanonicalJson.Serialize(policy))
        || (origin.Body.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array
            && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("role", out JsonElement role) && role.ValueKind == JsonValueKind.String && role.GetString() == name
                && r.TryGetProperty("execution", out JsonElement rolePolicy) && CanonicalJson.Serialize(rolePolicy) == CanonicalJson.Serialize(policy)));

    private static void Consistency(ValidationContext context, JsonElement execution)
    {
        string[] scopes = ["launcher", "roster", "goal", "assignment", "run"];
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonElement requested = execution.GetProperty("requested");
        JsonElement[] sources = execution.GetProperty("provenance").EnumerateArray().ToArray();
        int previous = -1;
        foreach (JsonElement source in sources)
        {
            int scope = Array.IndexOf(scopes, source.GetProperty("scope").GetString());
            if (scope < previous || (scope == 0 && previous == 0)) Error("Execution provenance keeps launcher, roster, goal, assignment, then run precedence.");
            previous = scope;
            JsonElement policy = source.GetProperty("policy");
            if (policy.TryGetProperty("choices", out JsonElement values))
            {
                foreach (JsonProperty value in values.EnumerateObject())
                {
                    choices[value.Name] = value.Value.GetString()!;
                    if (value.Name == "adapter" && value.Value.GetString() is not ("disabled" or RepresentationContract.LocalLossless)) Error("The adapter selection is unsupported.");
                }
            }
            if (!policy.TryGetProperty("restrictions", out JsonElement restrictions)) continue;
            foreach ((string limit, string field) in new[] { ("requiredModel", "model"), ("requiredEffort", "effort"), ("dataBoundary", "dataBoundary") })
            {
                if (restrictions.TryGetProperty(limit, out JsonElement value) && value.GetString() != requested.GetProperty(field).GetString()) Error("Execution choices satisfy every inherited restriction.");
            }
            foreach ((string limit, string field) in new[] { ("permittedProviders", "provider"), ("permittedDestinations", "destination") })
            {
                if (restrictions.TryGetProperty(limit, out JsonElement allowed) && !allowed.EnumerateArray().Any(v => v.GetString() == requested.GetProperty(field).GetString())) Error("Execution choices stay within every inherited provider and destination restriction.");
            }
            if (restrictions.TryGetProperty("exactRevision", out JsonElement revision)
                && (execution.GetProperty("resolvedRevision").GetString() == "unknown" || execution.GetProperty("resolvedRevision").GetString() != revision.GetString())) Error("Unknown or different model revisions cannot satisfy an exact pin.");
            if (restrictions.TryGetProperty("maxCurrency", out JsonElement cost)
                && (!context.Record.Body.TryGetProperty("cap", out JsonElement cap) || AgentRuns.Amount(cap, "currency") is not long amount
                    || amount > cost.GetProperty("amount").GetInt64() || AgentRuns.CurrencyUnit(cap) != cost.GetProperty("unit").GetString())) Error("The run's reservation satisfies every inherited currency bound.");
        }
        if (sources[0].GetProperty("scope").GetString() != "launcher" || requested.EnumerateObject().Count() != choices.Count
            || choices.Any(c => !requested.TryGetProperty(c.Key, out JsonElement value) || value.GetString() != c.Value)) Error("Requested execution choices are exactly the resolution of their recorded provenance.");
        if (execution.GetProperty("adapter").GetString() != choices.GetValueOrDefault("adapter", "disabled")) Error("The admitted adapter matches the resolved choice.");

        JsonElement environment = execution.GetProperty("environment");
        JsonElement inventory = environment.GetProperty("inventory");
        if (environment.GetProperty("inventoryHash").GetString() != Hashing.ContentHash.Sha256(CanonicalJson.SerializeUtf8(inventory))) Error("The admitted inventory hash identifies its exact canonical content.");
        HashSet<string> ids = inventory.EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        HashSet<string> variables = environment.GetProperty("variables").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != inventory.GetArrayLength() || !inventory.EnumerateArray().Any(c => c.GetProperty("kind").GetString() == "harness")) Error("The admitted inventory names its harness and each component exactly once.");
        foreach (JsonElement component in inventory.EnumerateArray())
        {
            if (component.GetProperty("dependencies").EnumerateArray().Any(d => !ids.Contains(d.GetString()!))
                || component.GetProperty("environment").EnumerateArray().Any(v => !variables.Contains(v.GetString()!))
                || component.GetProperty("destinations").EnumerateArray().Any(d => d.GetString() is not ("local" or "estate") && d.GetString() != requested.GetProperty("destination").GetString())) Error("Every admitted dependency, environment need and route is declared by the run.");
        }

        void Error(string message) => context.Error("operation.execution", message);
    }
}
