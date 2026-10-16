// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

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
            Identity(execution, "role");
            execution.Enum("adapter", true, "disabled");
            string? package = execution.Text("contextPackage", true, 71);
            if (package is not null and not "unknown" && !Hashing.ContentHash.IsValid(package))
            {
                context.Error("operation.execution", "A context package is its exact content hash or unknown.");
            }

            if (execution.Nested("requested", true) is BodyReader requested)
            {
                ExecutionPolicy.ReadChoices(requested);
                foreach (string required in new[] { "model", "effort", "provider", "destination", "dataBoundary" })
                {
                    if (!requested.Has(required)) requested.Error("body.missing", required, "An admitted execution choice is required.");
                }
            }

            foreach (BodyReader source in execution.NestedArray("provenance", true, allowEmpty: false) ?? [])
            {
                string? scope = source.Enum("scope", true, "launcher", "roster", "goal", "assignment", "run");
                Identity(source, "source");
                string? record = source.Ulid("record", false);
                string? hash = source.Hash("hash", record is not null);
                if (source.Nested("policy", true) is BodyReader policy) ExecutionPolicy.Validate(policy);
                if (record is not null && scope is not null)
                {
                    RecordEnvelope? origin = context.RequireReference("execution.provenance.record", record, scope);
                    if (origin is not null && origin.ComputedHash != hash) context.Error("operation.execution", "Execution provenance pins the exact contributing record hash.");
                }
                source.NoOtherKeys();
            }

            if (execution.Nested("allocation", true) is BodyReader allocation)
            {
                allocation.Number("inputTokens", false, 0, CanonicalJson.MaxSafeInteger);
                allocation.Number("outputTokens", false, 0, CanonicalJson.MaxSafeInteger);
                allocation.StringArray("unknown", true, allowEmpty: true, maxItemLength: 40);
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

    private static void Identity(BodyReader reader, string field, bool required = true)
    {
        if (reader.Text(field, required, 160) is string value && !ExecutionPolicy.IsIdentity(value)) reader.Error("execution.identity", field, "A portable execution identity contains no machine path or environment value.");
    }
}
