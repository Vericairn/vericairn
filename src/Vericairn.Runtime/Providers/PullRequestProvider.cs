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
/// Whether the target branch requires every merge to go through its merge queue, and the pull request's place in that queue, as the
/// host observed it (CODE-71). An incomplete observation never authorizes a merge or settles a queued one.
/// </summary>
/// <param name="Required">The target branch requires merging through its merge queue.</param>
/// <param name="State">
/// <c>absent</c> when the pull request is not in the queue and the host reports no removal; <c>queued</c> while it waits or its group
/// runs; <c>failed</c> when the queue found it cannot merge, as on failed checks or a conflict; <c>removed</c> when it left the queue
/// unmerged otherwise.
/// </param>
/// <param name="Head">The pull-request head its queue entry holds, while it has one.</param>
/// <param name="Reason">The host's reason for its latest removal from the queue, as a short upper-case token, when the host gives one.</param>
/// <param name="Complete">False when the host could not be observed completely.</param>
public sealed record MergeQueueObservation(bool Required, string State, string? Head = null, string? Reason = null, bool Complete = true)
{
    /// <summary>The states a pull request can have in a merge queue.</summary>
    public static readonly IReadOnlyList<string> States = ["absent", "queued", "failed", "removed"];
}

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

    /// <summary>
    /// Observes whether the pull request's target branch requires a merge queue, and the pull request's place in it (CODE-71). A
    /// provider that does not override this reports that no queue is required, so merges go through <see cref="Merge"/> as before.
    /// </summary>
    MergeQueueObservation ObserveQueue(string destination, string number) => new(false, "absent");

    /// <summary>
    /// Enqueues the pull request into its target branch's required merge queue only if it still has this head, never a later or
    /// automatic merge of another head; an ambiguous receipt throws uncertainty. The queue merges it later; its outcome is observed
    /// with <see cref="ObserveQueue"/> and <see cref="Observe"/>. A provider that does not override this refuses before any request.
    /// </summary>
    ChangeReceipt Enqueue(string destination, string number, string head) =>
        throw new ProviderRefusedException("This pull-request provider cannot enqueue into a merge queue; nothing was requested.");

    /// <summary>
    /// Observes the runs of exactly the commit a merge queue moved the target branch to: the queue's own runs of that commit (event
    /// merge_group) as well as the branch's push runs, never a newer commit's run. A provider that does not override this reads the
    /// push runs alone.
    /// </summary>
    TargetObservation ObserveQueuedTarget(string destination, string branch, string commit) => ObserveTarget(destination, branch, commit);
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
        ["open", "observe", "merge", "observe-target", "enqueue", "observe-queue"], ["In-memory conformance implementation; no external effects."], Locations: ["github"]);

    private readonly Dictionary<string, MergeQueueObservation> queue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TargetObservation> queuedTargets = new(StringComparer.Ordinal);

    /// <summary>Whether every target branch requires its merge queue, refusing a direct merge (CODE-71).</summary>
    public bool QueueRequired { get; set; }

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
            if (QueueRequired) throw new ProviderRefusedException("The target branch requires its merge queue; nothing was merged.");
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

    /// <inheritdoc />
    public MergeQueueObservation ObserveQueue(string destination, string number)
    {
        lock (gate) return queue.GetValueOrDefault(destination + "#" + number) is { } place ? place with { Required = QueueRequired } : new(QueueRequired, "absent");
    }

    /// <inheritdoc />
    public ChangeReceipt Enqueue(string destination, string number, string head)
    {
        lock (gate)
        {
            PullRequestObservation? pull = Observe(destination, number);
            if (!QueueRequired) throw new ProviderRefusedException("The target branch has no merge queue; nothing was enqueued.");
            if (pull is null || pull.State != "open" || pull.Head != head)
                throw new ProviderRefusedException("The pull request is absent, closed or its head moved.");
            if (queue.GetValueOrDefault(destination + "#" + number) is { State: "queued" })
                throw new ProviderRefusedException("The pull request is already in the merge queue.");
            queue[destination + "#" + number] = new(true, "queued", head);
            Effects++;
            return new(number);
        }
    }

    /// <summary>
    /// Settles a queued pull request as its merge queue would (CODE-71): <c>merged</c> merges it at <see cref="MergeCommit"/>, the commit
    /// the queue's group tested; <c>failed</c> or <c>removed</c> takes it out of the queue unmerged, with an optional host reason.
    /// </summary>
    public void SettleQueue(string destination, string number, string outcome, string? reason = null)
    {
        lock (gate)
        {
            string key = destination + "#" + number;
            if (queue.GetValueOrDefault(key) is not { State: "queued" or "failed" }) throw new InvalidOperationException("The pull request is not in the merge queue.");
            PullRequestObservation pull = pulls[key];
            switch (outcome)
            {
                case "merged":
                    pulls[key] = pull with { State = "merged", MergeCommit = MergeCommit };
                    queue.Remove(key);
                    break;
                case "unmergeable":
                    queue[key] = new(true, "failed", pull.Head);
                    break;
                case "failed" or "removed":
                    queue[key] = new(true, outcome, null, reason);
                    break;
                default:
                    throw new ArgumentException("A merge queue merges, finds unmergeable, fails or removes a pull request.", nameof(outcome));
            }
        }
    }

    /// <summary>Supplies the runs of a commit a merge queue moved the target branch to, its queue's runs among them.</summary>
    public void SetQueuedTarget(string destination, TargetObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate) queuedTargets[destination + "#" + observation.Branch + "#" + observation.Head] = observation;
    }

    /// <inheritdoc />
    public TargetObservation ObserveQueuedTarget(string destination, string branch, string commit)
    {
        lock (gate) return queuedTargets.GetValueOrDefault(destination + "#" + branch + "#" + commit) ?? ObserveTarget(destination, branch, commit);
    }
}
