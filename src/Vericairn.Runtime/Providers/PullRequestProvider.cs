// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>A named check as the host observed it, bound to the commit it tested.</summary>
public sealed record ChangeCheck(string Name, string Head, string Status, string? Conclusion);

/// <summary>A pull request and its complete check inventory. Incomplete observations never authorize a merge.</summary>
public sealed record PullRequestObservation(string Number, string Branch, string Target, string Head, string State,
    string? MergeCommit, IReadOnlyList<ChangeCheck> Checks, bool Complete = true);

/// <summary>The target branch's push runs at the exact merge commit, including their jobs.</summary>
public sealed record TargetObservation(string Branch, string Head, IReadOnlyList<ChangeCheck> Checks, bool Complete = true);

/// <summary>The provider's receipt. Missing identities remain uncertain until observed.</summary>
public sealed record ChangeReceipt(string? PullRequest = null, string? MergeCommit = null);

/// <summary>
/// Pull requests, contract version 1. Every observation is complete or explicitly incomplete. Implementations merge with an
/// atomic expected-head condition, never auto-merge later, and never read credentials. The host records intent before calling.
/// </summary>
public interface IPullRequestProvider
{
    /// <summary>The version implemented by every provider.</summary>
    const string Contract = "vericairn.provider.pull-request/1";

    /// <summary>The stable provider id recorded with each operation.</summary>
    string Provider { get; }

    /// <summary>The capabilities and repository location keys this provider serves.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>The credential-free git transport address for the declared destination.</summary>
    string PushRemote(string destination);

    /// <summary>Opens a pull request from an already pushed branch at the expected head.</summary>
    ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body);

    /// <summary>Observes the pull request, or null when it is absent. A branch may be used to reconcile an unacknowledged opening.</summary>
    PullRequestObservation? Observe(string destination, string numberOrBranch);

    /// <summary>Merges immediately only if the pull request still has this head; an ambiguous receipt throws uncertainty.</summary>
    ChangeReceipt Merge(string destination, string number, string head);

    /// <summary>Observes push runs on the target branch at the merge commit, never the branch's latest commit.</summary>
    TargetObservation ObserveTarget(string destination, string branch, string commit);
}

/// <summary>An independent in-memory implementation for conformance tests; qualifies no external service.</summary>
public sealed class MemoryPullRequestProvider : IPullRequestProvider
{
    private readonly object gate = new();
    private readonly Dictionary<string, PullRequestObservation> pulls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TargetObservation> targets = new(StringComparer.Ordinal);
    private int nextNumber;

    /// <inheritdoc />
    public string Provider => "memory-pull-requests";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("memory-pull-requests", IPullRequestProvider.Contract, "implemented",
        ["open", "observe", "merge", "observe-target"], ["In-memory conformance implementation; no external effects."], Locations: ["github"]);

    /// <summary>The checks assigned to the next opening.</summary>
    public IReadOnlyList<ChangeCheck> Checks { get; set; } = [];

    /// <summary>The commit produced by the next merge.</summary>
    public string MergeCommit { get; set; } = new('b', 40);

    /// <summary>The number of effects requested.</summary>
    public int Effects { get; private set; }

    /// <inheritdoc />
    public string PushRemote(string destination) => "https://example.invalid/" + destination;

    /// <inheritdoc />
    public ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body)
    {
        lock (gate)
        {
            if (Observe(destination, branch) is not null) throw new ProviderRefusedException("A pull request already exists for this branch.");
            string number = (++nextNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
            pulls[destination + "#" + number] = new(number, branch, target, head, "open", null, Checks.ToArray());
            Effects++;
            return new(number);
        }
    }

    /// <inheritdoc />
    public PullRequestObservation? Observe(string destination, string numberOrBranch)
    {
        lock (gate) return pulls.TryGetValue(destination + "#" + numberOrBranch, out PullRequestObservation? pull) ? pull
            : pulls.Where(p => p.Key.StartsWith(destination + "#", StringComparison.Ordinal)).Select(p => p.Value)
                .SingleOrDefault(p => p.Branch == numberOrBranch);
    }

    /// <summary>Supplies an observation, including a moved head or a failure.</summary>
    public void Set(string destination, PullRequestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate)
        {
            pulls[destination + "#" + observation.Number] = observation;
            if (int.TryParse(observation.Number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number))
                nextNumber = Math.Max(nextNumber, number);
        }
    }

    /// <summary>Supplies the target branch's observed run.</summary>
    public void SetTarget(string destination, TargetObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate) targets[destination + "#" + observation.Branch + "#" + observation.Head] = observation;
    }

    /// <inheritdoc />
    public ChangeReceipt Merge(string destination, string number, string head)
    {
        lock (gate)
        {
            PullRequestObservation? pull = Observe(destination, number);
            if (pull is null || pull.State != "open" || pull.Head != head)
                throw new ProviderRefusedException("The pull request is absent, closed or its head moved.");
            pulls[destination + "#" + number] = pull with { State = "merged", MergeCommit = MergeCommit };
            Effects++;
            return new(number, MergeCommit);
        }
    }

    /// <inheritdoc />
    public TargetObservation ObserveTarget(string destination, string branch, string commit)
    {
        lock (gate) return targets.GetValueOrDefault(destination + "#" + branch + "#" + commit) ?? new(branch, commit, []);
    }
}
