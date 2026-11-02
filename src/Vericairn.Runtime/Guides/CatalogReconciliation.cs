// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Guides;

/// <summary>An attributable provider revision, supplied by a future qualified importer or a responsible manual observer.</summary>
public sealed record CatalogRequestObservation
{
    /// <summary>issue or comment; ordinary discussion has no request payload.</summary>
    public required string Kind { get; init; }
    /// <summary>The hosting platform's stable issue or comment identity, not a display name.</summary>
    public required string ObjectId { get; init; }
    /// <summary>The containing issue's stable identity.</summary>
    public required string IssueId { get; init; }
    /// <summary>The hosting platform's stable account identity. Attribution must come from the observation, not body text.</summary>
    public required string AccountId { get; init; }
    /// <summary>human, bot or app.</summary>
    public required string AccountKind { get; init; }
    /// <summary>Whether the provider observation establishes authenticated control of this account.</summary>
    public bool Verified { get; init; }
    /// <summary>Stable positive revision order for this object; delivery order has no effect.</summary>
    public long Revision { get; init; }
    /// <summary>The issue's actual platform creation time, in UTC.</summary>
    public DateTimeOffset IssueCreatedAt { get; init; }
    /// <summary>The object's platform creation time, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>The revision's platform time, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Deletion withdraws this source; its earlier revision is needed to identify the affected entry.</summary>
    public bool Deleted { get; init; }
    /// <summary>Closing or reopening an issue does not create or remove an attributable request.</summary>
    public bool Closed { get; init; }
    /// <summary>The structured form fields; null denotes removal of fields or ordinary discussion.</summary>
    public CatalogRequest? Fields { get; init; }
}

/// <summary>A complete observation basis, including history needed for edits and deletions. No fetching occurs.</summary>
public sealed record CatalogRequestSnapshot(string Repository, bool Complete, DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd, DateTimeOffset AsOf, CatalogRequestObservation[] Observations);

/// <summary>A deterministic proposed issue reconciliation; applying it requires separate authority.</summary>
public sealed record CatalogCanonicalIssue(string EntryKey, string IssueId, string[] Duplicates);

/// <summary>A private deduplicated account-entry vote; never part of a public bundle.</summary>
public sealed record CatalogVote(string AccountId, string EntryKey, bool NoticeOptIn);

/// <summary>A total for an exact observed public entry.</summary>
public sealed record CatalogRequestTotal(string EntryKey, int Active);

/// <summary>Private local reconciliation. Incomplete or conflicting input returns no fresh votes or totals.</summary>
public sealed record CatalogReconciliation(bool Complete, string Policy, CatalogCanonicalIssue[] Issues,
    CatalogVote[] Votes, CatalogRequestTotal[] Totals, IReadOnlyList<GuideFinding> Findings);

/// <summary>Pure serial reconciliation of provider observations, with no account lookup, provider write or publication.</summary>
public static class CatalogRequestReconciler
{
    /// <summary>The exact counting convention.</summary>
    public const string Policy = "vericairn.catalog-requests/1";

    /// <summary>Deduplicates deliveries, reconciles object histories and keeps the latest deliberate account-entry action.</summary>
    public static CatalogReconciliation Reconcile(CatalogInventory inventory, CatalogRequestSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!CatalogRequests.ValidInventory(inventory) || !ValidSnapshot(snapshot)) return Failed("invalid-observations");
        if (!snapshot.Complete) return Failed("incomplete-observations");
        var entries = inventory.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        CatalogRequestValidation Validate(CatalogRequest request) => CatalogRequests.ValidateEntry(entries.GetValueOrDefault(request.EntryKey), request);
        var histories = new List<CatalogRequestObservation[]>();
        foreach (var group in snapshot.Observations.GroupBy(o => (o.Kind, o.ObjectId)))
        {
            var revisions = new List<CatalogRequestObservation>();
            foreach (var revision in group.GroupBy(o => o.Revision).OrderBy(g => g.Key))
            {
                CatalogRequestObservation value = revision.First();
                if (revision.Any(o => o != value)) return Failed("conflicting-replay");
                revisions.Add(value);
            }
            CatalogRequestObservation first = revisions[0];
            if (revisions.Any(o => o.AccountId != first.AccountId || o.AccountKind != first.AccountKind || o.CreatedAt != first.CreatedAt ||
                o.IssueId != first.IssueId || o.IssueCreatedAt != first.IssueCreatedAt) ||
                revisions.Zip(revisions.Skip(1)).Any(p => p.First.UpdatedAt > p.Second.UpdatedAt))
                return Failed("conflicting-history");
            histories.Add([.. revisions]);
        }
        var issueHistories = histories.Where(h => h[0].Kind == "issue").ToDictionary(h => h[0].IssueId, StringComparer.Ordinal);
        if (histories.Where(h => h[0].Kind == "comment").Any(h => !issueHistories.TryGetValue(h[0].IssueId, out CatalogRequestObservation[]? issue) ||
            h[0].IssueCreatedAt != issue[0].CreatedAt)) return Failed("missing-issue-basis");

        bool Human(CatalogRequestObservation o) => o.Verified && o.AccountKind == "human";
        bool Admitted(CatalogRequestObservation o) => Human(o) && o.Fields is not null && Validate(o.Fields).Accepted;
        // An unchecked Request withdraws the vote but preserves an existing valid tracking issue's target.
        string? IssueKey(CatalogRequestObservation[] history)
        {
            CatalogRequestObservation last = history[^1];
            if (last.Deleted || !Human(last) || last.Fields is null) return null;
            string key = last.Fields.EntryKey;
            return history.Any(o => Admitted(o) && o.Fields!.EntryKey == key) &&
                Validate(last.Fields with { Request = true }).Accepted ? key : null;
        }
        var candidates = issueHistories.Values.Select(h => (Key: IssueKey(h), Observation: h[^1])).Where(i => i.Key is not null).ToArray();
        var candidateKeys = candidates.ToDictionary(i => i.Observation.IssueId, i => i.Key!, StringComparer.Ordinal);
        CatalogCanonicalIssue[] issues = candidates.GroupBy(i => i.Key!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                string[] ordered = g.OrderBy(i => i.Observation.IssueCreatedAt).ThenBy(i => i.Observation.IssueId, StringComparer.Ordinal)
                    .Select(i => i.Observation.IssueId).ToArray();
                return new CatalogCanonicalIssue(g.Key, ordered[0], ordered[1..]);
            }).ToArray();
        var tracked = issues.Select(i => i.EntryKey).ToHashSet(StringComparer.Ordinal);
        var actions = new List<RequestAction>();
        foreach (CatalogRequestObservation[] history in histories)
        {
            string? previous = null;
            CatalogRequestObservation? prior = null;
            foreach (CatalogRequestObservation observation in history)
            {
                // Provider metadata (including close/reopen) is not a new deliberate request and cannot undo a withdrawal elsewhere.
                bool changed = prior is null || prior.Fields != observation.Fields || prior.Deleted != observation.Deleted || prior.Verified != observation.Verified;
                prior = observation;
                if (!changed) continue;
                string? current = Admitted(observation) && !observation.Deleted ? observation.Fields!.EntryKey : null;
                if (observation.Kind == "comment" && current is not null &&
                    candidateKeys.GetValueOrDefault(observation.IssueId) != current) current = null;
                if (previous is not null && previous != current) actions.Add(new(observation, previous, false, false));
                if (current is not null) actions.Add(new(observation, current, true, observation.Fields!.NoticeOptIn));
                // A withdrawal can arrive on another object. Preserve its explicit, valid account-entry intent.
                else if (Human(observation) && observation.Fields is not null &&
                    Validate(observation.Fields with { Request = true }).Accepted &&
                    (observation.Kind == "issue" || candidateKeys.GetValueOrDefault(observation.IssueId) == observation.Fields.EntryKey))
                    actions.Add(new(observation, observation.Fields.EntryKey, false, false));
                previous = current;
            }
        }
        CatalogVote[] votes = actions.GroupBy(a => (a.Observation.AccountId, a.Key))
            .Select(g => g.OrderBy(a => a.Observation.UpdatedAt).ThenBy(a => a.Observation.Kind, StringComparer.Ordinal)
                .ThenBy(a => a.Observation.ObjectId, StringComparer.Ordinal).ThenBy(a => a.Observation.Revision).Last())
            .Where(a => a.Active && tracked.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal).ThenBy(a => a.Observation.AccountId, StringComparer.Ordinal)
            .Select(a => new CatalogVote(a.Observation.AccountId, a.Key, a.Notice)).ToArray();
        var counts = votes.GroupBy(v => v.EntryKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        CatalogRequestTotal[] totals = inventory.Entries.Where(e => e.Requestable).OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => new CatalogRequestTotal(e.Key, counts.GetValueOrDefault(e.Key))).ToArray();
        return new(true, Policy, issues, votes, totals, []);
    }

    private sealed record RequestAction(CatalogRequestObservation Observation, string Key, bool Active, bool Notice);

    private static bool ValidSnapshot(CatalogRequestSnapshot snapshot) =>
        snapshot.Repository is not null && snapshot.Repository.Length <= 240 && snapshot.Repository.Split('/') is [var owner, var repository] &&
        Token(owner) && Token(repository) && snapshot.WindowStart.Offset == TimeSpan.Zero && snapshot.WindowEnd.Offset == TimeSpan.Zero &&
        snapshot.AsOf.Offset == TimeSpan.Zero && snapshot.WindowStart <= snapshot.AsOf && snapshot.AsOf < snapshot.WindowEnd &&
        snapshot.Observations is { Length: <= 100000 } && snapshot.Observations.All(o => o is not null && o.Kind is "issue" or "comment" &&
            Token(o.ObjectId) && Token(o.IssueId) && Token(o.AccountId) && o.AccountKind is "human" or "bot" or "app" && o.Revision > 0 &&
            o.IssueCreatedAt.Offset == TimeSpan.Zero && o.CreatedAt.Offset == TimeSpan.Zero && o.UpdatedAt.Offset == TimeSpan.Zero &&
            o.IssueCreatedAt <= o.CreatedAt && o.CreatedAt <= o.UpdatedAt && o.UpdatedAt <= snapshot.AsOf &&
            (o.Kind != "issue" || o.ObjectId == o.IssueId && o.CreatedAt == o.IssueCreatedAt) &&
            (o.Fields is null || o.Fields.EntryKey is not null && o.Fields.EntryKey.Length <= 120 && o.Fields.IntendedUse is not { Length: > 12000 }));

    private static bool Token(string? value) => value is { Length: > 0 and <= 120 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static CatalogReconciliation Failed(string code) => new(false, Policy, [], [], [],
        [new("inventory", code, "No fresh totals are available. Reconcile a complete, attributable observation basis; prior totals remain stale or unavailable.")]);
}
