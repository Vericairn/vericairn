// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Changes;

/// <summary>The exact-head gate shared by merge admission and target-run observation.</summary>
public static class ChangeGate
{
    /// <summary>Every reason these observations do not authorize the expected head; no check name or platform is guessed.</summary>
    public static IReadOnlyList<string> Refusals(string head, IReadOnlyList<string> required, IReadOnlyList<ChangeCheck> checks, bool complete)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(checks);
        List<string> reasons = [];
        if (!complete) reasons.Add("The host's check inventory is incomplete.");
        if (required.Count == 0) reasons.Add("No required checks are declared on the product.");
        foreach (string name in required)
        {
            if (!checks.Any(c => c.Name == name)) reasons.Add($"Required check '{name}' is missing.");
        }
        foreach (ChangeCheck check in checks)
        {
            if (check.Head != head) reasons.Add($"Check '{check.Name}' is for another head.");
            if (check.Status != "completed" || check.Conclusion != "success")
                reasons.Add($"Check '{check.Name}' has not completed successfully.");
        }
        return reasons;
    }

    /// <summary>
    /// As <see cref="Refusals"/>, for the target run of a merge a required merge queue made (CODE-71). The queue moved the branch to the
    /// exact commit its group's run tested, so the branch's push run of that commit may skip what that run already passed: a check
    /// reported as skipped counts when a check of the same name completed successfully at the same commit. Every other check still has
    /// to complete successfully there, and every required check has to be present.
    /// </summary>
    public static IReadOnlyList<string> QueuedRefusals(string head, IReadOnlyList<string> required, IReadOnlyList<ChangeCheck> checks, bool complete)
    {
        ArgumentNullException.ThrowIfNull(checks);
        HashSet<string> passed = checks.Where(c => c.Head == head && c.Status == "completed" && c.Conclusion == "success").Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        return Refusals(head, required, [.. checks.Where(c => !(c.Head == head && c.Status == "completed" && c.Conclusion == "skipped" && passed.Contains(c.Name)))], complete);
    }
}
