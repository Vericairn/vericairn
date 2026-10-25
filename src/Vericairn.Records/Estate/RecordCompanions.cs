// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>The foundation's readable record coverage, independent of any estate's adoption or output files.</summary>
public static class RecordCompanions
{
    /// <summary>Explicitly supported types. Adding a type to the admitted registry requires a reader decision here.</summary>
    public static ImmutableArray<string> Types { get; } =
    [
        "acceptance", "admission", "amendment", "ask", "assignment", "attempt", "authority", "communication-delegation",
        "communication-setting", "credential-reference", "custody-exception", "decision", "device", "discharge", "document-review",
        "document-revision", "estate", "estate-policy", "goal", "goal-status", "harness-qualification", "living-document", "migration",
        "observation", "operation", "operator-request", "operator-request-close", "policy-exception", "possibility", "principal", "product", "qualification", "reconciliation",
        "prompt-preference", "release", "resource", "result", "retirement", "review", "roster", "saved-prompt", "template-defaults", "verification", "work-order", "work-progress", "work-status", "writing-exception",
    ];

    /// <summary>Exact descriptors for item and list readers, including nested collections.</summary>
    public static ImmutableArray<DocumentCoverageDescriptor> Coverage =>
        [.. DocumentCoverage.RecordKinds.Where(k => Types.Contains(k.Id[7..].Split('#')[0])).Select(k => new DocumentCoverageDescriptor(
            DocumentCoverage.Version, k.Id, Task(k.Id), new(RecordCompanionGenerator.Name, 1), new(RecordCompanionGenerator.Name, 1),
            k.InputSchema, "Internal", "on-demand"))];

    private static string Task(string kind)
    {
        string type = kind[7..].Split('#')[0];
        string summary = RecordTypeRegistry.Find(type)!.Summary;
        return kind.Contains('#', StringComparison.Ordinal)
            ? "Inspect each recorded member of " + kind.Split('#')[1] + ", retaining its source order, exact parent revision and unknowns. " + summary
            : summary + " Read exact revisions and their recorded fields without inferring current state.";
    }
}
