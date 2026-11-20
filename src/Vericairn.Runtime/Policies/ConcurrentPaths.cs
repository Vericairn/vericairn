// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

/// <summary>
/// The <c>vericairn.estate/concurrent-paths</c> kind, version 1 (from 0.9.4, POLICY-08; the estate's design/measured-work.md § 5 and
/// design/estate-policies.md § 5.11). It has no parameters and takes a <c>repository</c> selection whose selectors say which shared paths
/// count; one policy covers one repository. Its subjects are the unordered pairs of active assignments whose effective permitted paths
/// (permitted patterns less exclusions) share a valid path within the selection, as the overlap check (CODE-73) compares them: satisfied
/// when the pair is sequenced, one naming the other in <c>after</c>; unavailable when a party names no repository the estate knows and no
/// run of it named one, or when the comparison within the selection reached its bound undetermined; unmet otherwise. Disjoint effective
/// paths (one owner, the others excluding the shared paths) make no subject. The kind is evaluated in the estate check and status from the
/// records alone, and at refuse the runtime's write boundary refuses a new assignment, a run admission or a new attempt that leaves an
/// unmet or unavailable pair (<see cref="AtStart"/>); record validation never refuses. An exception pins the exact pair of assignment
/// revisions (a <c>pair</c> target) and applies to an unmet pair only.
/// </summary>
internal static class ConcurrentPaths
{
    internal const string Kind = AssignmentOverlaps.PolicyKind;

    /// <summary>The item key of every pair's finding.</summary>
    internal const string Item = "overlap";

    /// <summary>What a pair's finding requires.</summary>
    internal const string Required = "The pair runs in sequence (the later's brief names the earlier in after), or its effective permitted paths share no path within the selection (one owner: every other party excludes the shared paths in excludedChanges).";

    /// <summary>The kind's registration in the foundation.</summary>
    internal static PolicyKindRegistration Registration(string implementationDigest) =>
        new(Kind, 1, implementationDigest, ["repository"], [], 100000, 100000, Validate, Evaluate);

    /// <summary>One pair of the selection: the overlap, its hold within the policy's selection, the verdict and why.</summary>
    internal sealed record Pair(AssignmentOverlaps.Overlap Overlap, AssignmentOverlaps.Hold Hold, string Verdict, string Reason);

    /// <summary>A pair at assignment start under one refuse-level rule, with the exceptions that cover it.</summary>
    internal sealed record Standing(AssignmentOverlaps.Policy Policy, string Rule, Pair Pair, ImmutableArray<RecordReference> Exceptions)
    {
        /// <summary>Whether it holds the start: unmet without a valid exception, or unavailable, which no exception covers.</summary>
        internal bool Holds => Pair.Verdict == "unavailable" || Pair.Verdict == "unmet" && Exceptions.IsEmpty;
    }

    /// <summary>The kind takes no parameters and a repository selection with valid selectors.</summary>
    internal static void Validate(PolicyRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Shape(context.Parameters, []);
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        Choice(selection, "kind", "repository");
        String(selection, "repository");
        Selectors(Strings(selection, "selectors"));
    }

    /// <summary>The pairs of the policy's selection as the records alone show them, each a finding.</summary>
    internal static IReadOnlyList<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate(context);
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        if (AssignmentOverlaps.IdentityOf(context.Model, Text(selection, "repository")) is null)
            throw new PolicyInputException("The selected repository is neither the estate's own nor a repository resource the records hold.");
        // From the records alone, so that the estate check and status read the same pairs on every machine.
        IReadOnlyList<AssignmentOverlaps.Overlap> overlaps = AssignmentOverlaps.Overlaps(context.Model);
        var findings = new List<PolicyFinding>();
        foreach (Pair pair in Pairs(context.Model, context.Policy.Id, overlaps))
        {
            AssignmentOverlaps.Overlap overlap = pair.Overlap;
            RecordEnvelope later = context.Model.Latest(overlap.Second.Assignment);
            findings.Add(context.Finding(Target(context.Model, overlap), Item, pair.Verdict, pair.Reason, Required, NextAction(pair.Verdict), later, Revisions(context.Model, overlap)));
        }

        // No pair meets the selection, as when one owner's exclusions resolved the last one: the rule is satisfied with no subject.
        return findings.Count > 0 ? findings
            : [context.Finding(new("policy", context.Identity, context.Policy.Reference), "", "satisfied", "No subjects selected.", Required, "No action.", subjects: 0)];
    }

    /// <summary>
    /// The pairs of one policy's selection among these overlaps, sequenced ones included: each overlap whose hold names the policy.
    /// </summary>
    internal static IReadOnlyList<Pair> Pairs(EstateModel model, string policy, IEnumerable<AssignmentOverlaps.Overlap> overlaps)
    {
        ArgumentNullException.ThrowIfNull(model);
        var pairs = new List<Pair>();
        foreach (AssignmentOverlaps.Overlap overlap in overlaps)
        {
            if (overlap.Holds.FirstOrDefault(h => h.Policy.Record.Id == policy) is not { } hold) continue;
            pairs.Add(Verdict(model, overlap, hold));
        }

        return pairs;
    }

    /// <summary>The verdict of one pair within one policy's selection, and why, in words.</summary>
    internal static Pair Verdict(EstateModel model, AssignmentOverlaps.Overlap overlap, AssignmentOverlaps.Hold hold)
    {
        // The earlier recorded assignment first, whichever side the comparison put it on.
        AssignmentOverlaps.Party[] ordered = [.. new[] { overlap.First, overlap.Second }.OrderBy(p => First(model, p).At, StringComparer.Ordinal).ThenBy(p => First(model, p).Id, StringComparer.Ordinal)];
        string parties = $"{Describe(ordered[0])} and {Describe(ordered[1])} in {AssignmentOverlaps.NameOf(model, overlap.Repository)}";
        string shared = hold.Witness is null ? "paths the comparison could not rule out" : $"paths such as {hold.Witness}";
        if (overlap.Sequenced) return new Pair(overlap, hold, "satisfied", $"{Capital(parties)} share {shared} within the selection, and one runs after the other.");
        List<string> unknown = [.. ordered.Where(p => p.Repository is null).Select(Describe)];
        if (unknown.Count > 0)
        {
            return new Pair(overlap, hold, "unavailable",
                $"{Capital(parties)} may share {shared} within the selection, and neither runs after the other; {string.Join(" and ", unknown)} {(unknown.Count == 1 ? "names" : "name")} no repository and no run of {(unknown.Count == 1 ? "it" : "them")} named one, so the pair is compared as possibly in any repository.");
        }

        if (!hold.Determined)
        {
            return new Pair(overlap, hold, "unavailable",
                $"{Capital(parties)} may share paths within the selection, and neither runs after the other; the comparison within the selection reached its bound of {PathIntersection.StateBound.ToString("N0", CultureInfo.InvariantCulture)} states undetermined, so the pair counts as overlapping.");
        }

        return new Pair(overlap, hold, "unmet", $"{Capital(parties)} share {shared} within the selection, and neither runs after the other.");
    }

    /// <summary>
    /// The pairs of a party, recorded or proposed, at assignment start: under each active refuse-level concurrent-paths policy already in
    /// force at <paramref name="at"/>, each overlap of the party with another active assignment within its selection, for each of its
    /// concurrent-paths rules, with the valid exceptions that pin that exact pair of revisions. The party counts as active, since an attempt
    /// or a run starts its work again.
    /// </summary>
    internal static IReadOnlyList<Standing> AtStart(EstateModel model, RecordEnvelope assignment, IRepositoryView? view, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        List<(AssignmentOverlaps.Policy Policy, string[] Rules)> policies = [.. AssignmentOverlaps.Policies(model)
            .Where(p => p.Level == "refuse" && DutyRecords.Time(EstateModel.BodyString(p.Record, "startsAt")) is { } starts && starts <= at)
            .Select(p => (p, Items(Get(p.Record.Body, "rules")).Where(r => Text(r, "kind") == Kind && Get(r, "version").TryGetInt64Safe(out long v) && v == 1)
                .Select(r => Text(r, "key")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray()))
            .Where(p => p.Item2.Length > 0)];
        if (policies.Count == 0) return [];
        if (AssignmentOverlaps.PartyOf(model, assignment, view) is not { Patterns.Count: > 0 } party) return [];
        IReadOnlyList<AssignmentOverlaps.Overlap> overlaps = AssignmentOverlaps.With(model, party with { Active = true }, view);
        var standing = new List<Standing>();
        foreach ((AssignmentOverlaps.Policy policy, string[] rules) in policies)
        {
            foreach (Pair pair in Pairs(model, policy.Record.Id, overlaps))
            {
                ImmutableArray<RecordReference> revisions = Revisions(model, pair.Overlap);
                foreach (string rule in rules)
                {
                    ImmutableArray<RecordReference> exceptions = pair.Verdict == "unmet" ? PolicyExceptions.ForPair(model, at, policy.Record, rule, revisions) : [];
                    standing.Add(new Standing(policy, rule, pair, exceptions));
                }
            }
        }

        return standing;
    }

    /// <summary>The identity of a pair's subject: its two assignments' lineage identities in ordinal order, joined by a plus sign.</summary>
    internal static string Identity(EstateModel model, AssignmentOverlaps.Overlap overlap) =>
        string.Join('+', new[] { Lineage(model, overlap.First), Lineage(model, overlap.Second) }.Order(StringComparer.Ordinal));

    // The first record of a party's lineage, or the draft itself.
    private static RecordEnvelope First(EstateModel model, AssignmentOverlaps.Party party) =>
        party.Assignment.Id == "draft" ? party.Assignment : model.ById[model.Chain(model.Latest(party.Assignment))[0]];

    /// <summary>The lineage identity of a party's assignment.</summary>
    internal static string Lineage(EstateModel model, AssignmentOverlaps.Party party) => model.Chain(model.Latest(party.Assignment))[0];

    /// <summary>The exact current revisions of a pair's two assignments, which its exception pins.</summary>
    internal static ImmutableArray<RecordReference> Revisions(EstateModel model, AssignmentOverlaps.Overlap overlap) =>
        [.. new[] { model.Latest(overlap.First.Assignment).Reference, model.Latest(overlap.Second.Assignment).Reference }
            .OrderBy(r => r.Id, StringComparer.Ordinal)];

    /// <summary>The finding target of a pair.</summary>
    internal static PolicyTarget Target(EstateModel model, AssignmentOverlaps.Overlap overlap) => new("pair", Identity(model, overlap), Repository: overlap.Repository);

    /// <summary>What resolves a pair of this verdict.</summary>
    internal static string NextAction(string verdict) => verdict switch
    {
        "satisfied" => "No action.",
        "unavailable" => "Name each assignment's repository in its brief (brief.repository), or narrow the patterns until the comparison is determined; or resolve the pair: record the later with after naming the earlier, or exclude the shared paths from every brief but the owner's (excludedChanges). An exception cannot cover an unavailable pair.",
        _ => "Resolve the pair before work starts: record the later with after naming the earlier (sequence), or exclude the shared paths from every brief but the owner's with excludedChanges (one owner); or the operator grants a policy exception for this exact pair of assignment revisions.",
    };

    /// <summary>
    /// Why a start is refused: every pair that holds it, named with its policy, rule and the remedies that can apply to the write; null when
    /// none does. An exception pins revisions that already exist, so it is named only for an attempt or a run on recorded assignments
    /// (<paramref name="recorded"/>), never for a new assignment or a revision, which is recorded resolved.
    /// </summary>
    internal static string? Refusal(EstateModel model, IReadOnlyList<Standing> standing, string what, bool recorded = true)
    {
        List<Standing> holding = [.. standing.Where(s => s.Holds)];
        if (holding.Count == 0) return null;
        var text = new StringBuilder();
        text.Append(Capital(what)).Append(" is refused: ").Append(holding.Count == 1 ? "a refuse-level concurrent-paths policy holds a pair it leaves unresolved." : $"refuse-level concurrent-paths policies hold {holding.Count.ToString(CultureInfo.InvariantCulture)} pairs it leaves unresolved.");
        foreach (Standing item in holding)
        {
            text.Append(' ').Append(PolicyName(item.Policy)).Append(", rule ").Append(item.Rule).Append(", ").Append(item.Pair.Verdict).Append(": ").Append(item.Pair.Reason);
        }

        bool unavailable = holding.Any(s => s.Pair.Verdict == "unavailable");
        if (recorded)
        {
            text.Append(" Resolve each pair before work starts: record the later with after naming the earlier (sequence), or exclude the shared paths from every brief but the owner's with excludedChanges (one owner)");
            text.Append(holding.Any(s => s.Pair.Verdict == "unmet") ? "; or the operator grants a policy exception for the exact pair of assignment revisions." : ".");
        }
        else
        {
            text.Append(" Record it resolved: name the earlier assignment in after (sequence), or narrow its paths with excludedChanges so that they share none within the selection (one owner).");
        }

        if (unavailable) text.Append(" An unavailable pair is resolved by naming each brief's repository or narrowing the patterns until the comparison is determined; no exception covers it.");
        return text.ToString();
    }

    private static string PolicyName(AssignmentOverlaps.Policy policy) => $"Policy {EstateModel.BodyString(policy.Record, "key") ?? policy.Record.Id} ({policy.Record.Id})";

    private static string Describe(AssignmentOverlaps.Party party) => party.Assignment.Id == "draft" ? $"{party.Key}'s new assignment" : $"{party.Key}'s assignment {party.Assignment.Id}";

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
