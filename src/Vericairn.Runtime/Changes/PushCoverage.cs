// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Changes;

/// <summary>
/// Which recorded checked pushes are complete (FIX-154, design/checked-changes.md § 6): a push observed at the remote after a passing,
/// zero-finding scan of exactly its range, whose base was the merge base with its target or the head of an earlier complete push of the
/// same repository and branch, so every commit its head carries that the target does not hold was scanned and its message checked by a
/// recorded checked push. Derived from the records, older ones included; a push records its basis in <c>target.basis</c> from 0.9.4.
/// Only a push scanned against the declaration's current target counts: one recorded against another target, as before the operator
/// moved the declaration, says nothing about what the current target holds, so its branch falls back to the merge base.
/// </summary>
internal static class PushCoverage
{
    /// <summary>The basis of a push whose scan began at the merge base of its head with the target branch's observed head.</summary>
    internal const string MergeBase = "merge-base";

    /// <summary>How many complete pushes' heads a push passes to its adapter, newest first; beyond them the merge base serves.</summary>
    internal const int Limit = 16;

    /// <summary>A complete push: its current operation record and its head.</summary>
    internal sealed record Covered(string Operation, string Head);

    /// <summary>
    /// The complete recorded pushes of a repository's branch at a destination against a target, newest first, one for each head, at most
    /// <see cref="Limit"/>: the candidates a push's base is chosen from.
    /// </summary>
    internal static IReadOnlyList<Covered> Covering(EstateModel model, string repository, string destination, string branch, string target)
    {
        ArgumentNullException.ThrowIfNull(model);
        var judge = new Judge(model, Pushes(model, repository, destination, branch, target));
        return [.. judge.Pushes.Where(judge.Complete).OrderByDescending(p => p.AtTime).ThenByDescending(p => p.Id, StringComparer.Ordinal)
            .DistinctBy(p => Text(p, "head"), StringComparer.Ordinal).Take(Limit).Select(p => new Covered(p.Id, Text(p, "head")!))];
    }

    /// <summary>
    /// Whether a recorded push is complete against the target the declaration names now. Anything other than a current, observed checked
    /// push scanned against that target is not.
    /// </summary>
    internal static bool Complete(EstateModel model, RecordEnvelope push, string target)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(push);
        if (!model.IsCurrent(push) || !Observed(push) || Text(push, "repository") is not string repository || Text(push, "destination") is not string destination
            || Text(push, "branch") is not string branch || Text(push, "target") != target) return false;
        return new Judge(model, Pushes(model, repository, destination, branch, target)).Complete(push);
    }

    // The current, observed checked pushes of a branch, scanned against the target.
    private static List<RecordEnvelope> Pushes(EstateModel model, string repository, string destination, string branch, string target) =>
        [.. model.Current("operation").Where(o => Observed(o) && Text(o, "repository") == repository && Text(o, "destination") == destination
            && Text(o, "branch") == branch && Text(o, "target") == target)];

    // A checked push observed at the remote after its scan of exactly base..head passed with no finding, as the record rules require of it.
    private static bool Observed(RecordEnvelope push) => EstateModel.BodyString(push, "action") == "change-push"
        && EstateModel.BodyString(push, "status") == "observed-success" && Text(push, "head") is string head && Patterns.Commit.IsMatch(head)
        && Text(push, "base") is string basis && Patterns.Commit.IsMatch(basis)
        && push.Body.TryGetProperty("scan", out JsonElement scan) && scan.ValueKind == JsonValueKind.Object
        && Field(scan, "verdict") == "passed" && Field(scan, "range") == basis + ".." + head
        && scan.TryGetProperty("findingCount", out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int n) && n == 0;

    private sealed class Judge(EstateModel model, List<RecordEnvelope> pushes)
    {
        private readonly Dictionary<string, bool> known = new(StringComparer.Ordinal);
        private readonly HashSet<string> open = new(StringComparer.Ordinal);
        internal List<RecordEnvelope> Pushes { get; } = pushes;

        // A chain that returns to a push still being judged rests on nothing, so it is not complete.
        internal bool Complete(RecordEnvelope push)
        {
            if (known.TryGetValue(push.Id, out bool complete)) return complete;
            if (!open.Add(push.Id)) return false;
            complete = Derive(push);
            open.Remove(push.Id);
            known[push.Id] = complete;
            return complete;
        }

        private bool Derive(RecordEnvelope push)
        {
            string basis = Text(push, "base")!;
            // From 0.9.4 a push records where its scan began: the merge base, or the complete push of the branch it continued, whose head
            // must be its base.
            if (Text(push, "basis") is string recorded)
            {
                if (recorded == MergeBase) return true;
                if (!Ulid.IsValid(recorded) || !model.ById.TryGetValue(recorded, out RecordEnvelope? named)) return false;
                RecordEnvelope continued = model.Latest(named);
                return Pushes.Contains(continued) && Text(continued, "head") == basis && Complete(continued);
            }

            // A push recorded before the field: one to a new branch began at the merge base, as every release did; one to an existing
            // branch began at the remote branch's head, which counts only where an earlier complete push of the branch left that head.
            if (Text(push, "remoteHead") is { Length: 0 }) return true;
            return Pushes.Any(other => other.Id != push.Id && Text(other, "head") == basis && Complete(other));
        }
    }

    private static string? Text(RecordEnvelope record, string name) =>
        record.Body.TryGetProperty("target", out JsonElement target) ? Field(target, name) : null;

    private static string? Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
}
