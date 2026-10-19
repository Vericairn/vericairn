// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Records.Estate;

/// <summary>Owned nested collection schemas; [] preserves an array's recorded order, not a current-state join.</summary>
public static class RecordCollections
{
    /// <summary>Collections below the top-level fields documented by the admitted record registry.</summary>
    public static ImmutableArray<DocumentManagedKind> Nested { get; } = Build();

    /// <summary>Schema-owned maps with open contribution keys; their reader retains every field and nested list.</summary>
    public static bool IsOpenTree(string type, string field) => (type, field) is
        ("observation", "facts") or ("estate-policy", "rules[].parameters");

    private static ImmutableArray<DocumentManagedKind> Build()
    {
        var kinds = ImmutableArray.CreateBuilder<DocumentManagedKind>();
        Add("estate", "policies.releaseAcceptance");
        Add("assignment", "brief.sources", "brief.permittedChanges", "brief.prohibitedEffects", "brief.workspace", "brief.limits", "brief.expectedOutputs", "brief.reviewCriteria");
        Add("product", "changes.requiredChecks", "release.artifacts", "release.artifacts[].platforms");
        Add("release", "recordFormat.reads");
        Add("observation", "facts");
        Add("review", "conditions[].read");
        Add("goal", "plan.workOrders");
        Add("living-document", "generator.inputs", "regions[].inputs", "triggers[].paths", "triggers[].keys");
        Add("estate-policy", "appliesTo.workOrders", "appliesTo.selectors", "rules[].parameters", "rules[].parameters.discharge.checks", "rules[].parameters.discharge.records");
        Add("authority", "bounds[].actions", "bounds[].policies", "bounds[].levels", "bounds[].appliesTo", "bounds[].appliesTo[].workOrders", "bounds[].appliesTo[].selectors", "bounds[].rules", "bounds[].targets", "bounds[].tokens", "bounds[].coverage.paths", "bounds[].coverage.paths[].selectors", "bounds[].coverage.records");
        Add("writing-exception", "scope.paths", "scope.paths[].selectors", "scope.records");
        Add("result", "duties[].scope.repositories", "duties[].scope.objects", "duties[].scope.records", "duties[].proofs", "duties[].proofs[].records");
        foreach (string type in new[] { "assignment", "goal", "roster" }) Execution(type, "execution");
        foreach (string type in new[] { "goal", "roster" })
        {
            Add(type, "roles[].principals");
            Execution(type, "roles[].execution");
        }
        Add("operation", "usage.unreported", "scope.outside", "execution.provenance", "execution.allocation.unknown", "execution.environment.variables", "execution.environment.inventory",
            "execution.environment.inventory[].environment", "execution.environment.inventory[].destinations", "execution.environment.inventory[].dependencies",
            "executionObserved.reports", "executionObserved.reports[].tools", "executionObserved.unknown", "executionObserved.discrepancies");
        Execution("operation", "execution.provenance[].policy");
        return [.. kinds.OrderBy(k => k.Id, StringComparer.Ordinal)];

        void Add(string type, params string[] fields)
        {
            foreach (string field in fields)
            {
                string id = "record/" + type + "#" + field;
                kinds.Add(new(id, id));
            }
        }

        void Execution(string type, string path) => Add(type, path + ".restrictions.permittedProviders", path + ".restrictions.permittedDestinations");
    }
}
