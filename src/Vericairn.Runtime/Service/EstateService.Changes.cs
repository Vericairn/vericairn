// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Changes;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private static readonly JsonSerializerOptions ChangeJson = new(ProtocolJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HashSet<string> changeEffects = new(StringComparer.Ordinal);

    private sealed record ChangeConfiguration(RecordEnvelope Product, RecordEnvelope Repository, IPullRequestProvider Provider,
        string Destination, string Target, IReadOnlyList<string> Required);

    private sealed class ChangeClaim(ChangeConfiguration configuration, string by, JsonObject body, RecordWritten written)
    {
        internal ChangeConfiguration Configuration { get; } = configuration;
        internal string By { get; } = by;
        internal JsonObject Body { get; set; } = body;
        internal RecordWritten Written { get; set; } = written;
        internal string Identity => Body["identity"]!.GetValue<string>();
        internal string Head => Body["target"]!["head"]!.GetValue<string>();
        internal string Action => Body["action"]!.GetValue<string>();
        internal string Status => Body["status"]!.GetValue<string>();
        internal bool EffectMayHaveStarted { get; set; }
    }

    private sealed class ChangeWriteException(Problem problem) : Exception("The operation record could not be stored.")
    {
        internal Problem Problem { get; } = problem;
    }

    /// <summary>Records the intent, scans exactly the planned commits, records the scan, pushes with a lease, then records the observed remote head.</summary>
    public ChangeResponse ChangesPush(ChangesPushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using IDisposable counted = SessionAction("changes push", PresencePlan(request.By, () => PushPresence("branch " + request.Branch)),
            ChangeSession("changes push", request.Product, request.Head, new("change-push", request.Branch, null, null, 3, [("push-head", 3, true)])));
        return ChangeEffect(request.Product, request.By, "change-push", request.Branch, request.Head, request.Reconciliation, claim =>
        {
            IPushAdapter adapter = Providers.PushAdapter ?? throw new ProviderRefusedException("No push adapter is registered.");
            ISecretScanner scanner = Providers.SecretScanner ?? throw new ProviderRefusedException("No secret scanner is registered.");
            string directory = ChangeClone(claim.Configuration);
            string remote = claim.Configuration.Provider.PushRemote(claim.Configuration.Destination);
            PushPlan plan = adapter.Prepare(directory, remote, request.Branch, claim.Configuration.Target, request.Head);
            if (plan.Head != request.Head || plan.Branch != request.Branch || plan.Target != claim.Configuration.Target
                || !Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.TargetHead))
                throw new ProviderRefusedException("The adapter's push plan does not match the requested head and branches.");
            JsonObject target = claim.Body["target"]!.AsObject();
            target["base"] = plan.Base;
            target["remoteHead"] = plan.RemoteHead ?? string.Empty;
            target["targetHead"] = plan.TargetHead;
            SecretScan scan = scanner.Scan(directory, plan);
            claim.Body["scan"] = JsonSerializer.SerializeToNode(scan, ChangeJson);
            if (!scan.Passed(plan)) throw new ProviderRefusedException("The scan of exactly the push's commits did not pass.");
            string? warned = CheckPushedMessages(claim, adapter, directory, plan);
            RefuseHeldDisclosure(claim, adapter, directory, plan);
            // The dispatch record precedes the push, which runs within its two-minute bound: the session must outlast it (ID-16).
            PresenceEffectAhead();
            DispatchChange(claim);
            claim.EffectMayHaveStarted = true;
            adapter.Push(directory, remote, plan, scan);
            // The transport's success is not the observed effect. This read also has its own intent and outcome.
            string? observed = ObserveChange(claim, "push-head", observation =>
            {
                string? head = adapter.Observe(directory, remote, request.Branch);
                if (head is not null) observation.Body["target"]!["remoteObserved"] = head;
                observation.Body["target"]!["state"] = head is null ? "absent" : "present";
                return head;
            }, out RecordWritten receipt);
            if (observed != request.Head) throw new ProviderUncertainException("The pushed head was not confirmed at the remote branch.");
            FinishChange(claim, "observed-success", "The scanned commit was observed at the remote branch.");
            return ChangeAnswer(claim, true, "The exact scanned head was pushed and observed." + (warned is null ? string.Empty : " " + warned)) with { Observation = receipt };
        });
    }

    // CODE-81: every commit message the push carries, trailers included, is matched against the estate's blocked terms before anything
    // is pushed, and each carries every trailer line the estate requires. A blocked term refuses, or warns where the estate configures
    // it; a missing or altered trailer refuses. The refusal and the recorded evidence name the commit and the policy, never the matched
    // text or the trailer. Returns the warning, if any.
    private string? CheckPushedMessages(ChangeClaim claim, IPushAdapter adapter, string directory, PushPlan plan)
    {
        CommitMessages.Settings settings;
        lock (gate) settings = CommitMessages.In(session.Load(), DateTimeOffset.UtcNow);
        if (!settings.Checks) return null;
        IReadOnlyList<PushedCommit> commits = adapter.CommitMessages(directory, plan)
            ?? throw new ProviderRefusedException("The estate checks commit messages, and the push's messages could not be read to check them; nothing was pushed.");
        if (commits.Any(c => c is null || !Patterns.Commit.IsMatch(c.Commit) || c.Message is null) || commits.Count == 0 && plan.Base != plan.Head)
            throw new ProviderRefusedException("The push's commit messages could not be read completely to check them; nothing was pushed.");
        (CommitMessages.Finding? refusal, IReadOnlyList<CommitMessages.Finding> warnings) = CommitMessages.Check(settings, commits.Select(c => ("Commit `" + c.Commit + "`", c.Message)));
        if (refusal is not null) throw new ProviderRefusedException(CommitMessages.Describe(refusal) + " Nothing was pushed; reword the commit and push again.");
        string checkedWhat = (settings.Lists.IsEmpty ? string.Empty : "the estate's blocked terms") + (settings.Lists.IsEmpty || settings.Trailers.IsEmpty ? string.Empty : " and ")
            + (settings.Trailers.IsEmpty ? string.Empty : "the trailer lines it requires");
        string? warned = MessageWarning(settings, warnings, "pushed");
        claim.Body["evidence"]!.AsArray().Add(new JsonObject
        {
            ["summary"] = $"The messages of the push's {commits.Count} {(commits.Count == 1 ? "commit" : "commits")} were checked against {checkedWhat}" + (warned is null ? "; none was refused." : ". " + warned),
        });
        return warned;
    }

    // The warning a blocked term at the warn level gives: each commit and its list, and the policies that set the level.
    private static string? MessageWarning(CommitMessages.Settings settings, IReadOnlyList<CommitMessages.Finding> warnings, string effect) => warnings.Count == 0 ? null
        : "Warned, not refused, as estate policy " + string.Join(" and ", settings.LevelPolicies) + " sets blocked terms in commit messages to warn: "
            + string.Join(" ", warnings.Take(5).Select(CommitMessages.Describe)) + (warnings.Count > 5 ? $" {warnings.Count - 5} more commits carry one too." : string.Empty)
            + " The change was " + effect + ".";

    // A held work order's key and distinctive text stay out of a public repository until the release carrying it is published,
    // whoever wrote the commits. A repository whose visibility is not recorded as public is not held to this check. Before then, one
    // push may carry them (FIX-137): the carrying release's documentation set, with the held fixes it carries, once the operator has
    // approved that release's Security section (DisclosureHolds.DocumentationSetRelease); the push must carry that set itself.
    private void RefuseHeldDisclosure(ChangeClaim claim, IPushAdapter adapter, string directory, PushPlan plan)
    {
        if (EstateModel.BodyString(claim.Configuration.Repository, "visibility") != "public") return;
        List<(DisclosureHolds.Hold Hold, string? Release)> holds;
        lock (gate)
        {
            EstateModel model = session.Load();
            holds = [.. DisclosureHolds.Active(model).Select(h => (h, DisclosureHolds.DocumentationSetRelease(model, h, claim.Configuration.Product.Id)))];
        }
        if (holds.Count == 0) return;
        // The repository is read outside the gate, once for each release whose documentation set could let a hold pass.
        Dictionary<string, bool> carried = holds.Select(h => h.Release).OfType<string>().Distinct(StringComparer.Ordinal)
            .ToDictionary(version => version, version => CarriesDocumentationSet(adapter, directory, plan, version), StringComparer.Ordinal);
        List<DisclosureHolds.Hold> held = [.. holds.Where(h => h.Release is null || !carried[h.Release]).Select(h => h.Hold)];
        if (held.Count == 0) return;
        string text = adapter.PublishedText(directory, plan)
            ?? throw new ProviderRefusedException("A disclosure hold is in force, and the push's text could not be read to check it; nothing was pushed.");
        if (DisclosureHolds.Disclosed(held, text) is string key)
            throw new ProviderRefusedException($"The push would disclose held work order {key} in a public repository before the release that carries it is published; nothing was pushed."
                + (holds.Any(h => h.Hold.Key == key && h.Release is not null) ? " Only the push that carries that release's documentation set may carry it now." : string.Empty));
    }

    // Whether the push carries a release's documentation set, unmerged: its head holds the set's manifest for that version and the target
    // branch's head holds none, as while the set's pull request is open. A manifest of another format or version is not the set.
    private static bool CarriesDocumentationSet(IPushAdapter adapter, string directory, PushPlan plan, string version)
    {
        string path = DocumentationSetDirectory + version + "/manifest.json";
        string? manifest = adapter.FileText(directory, plan, plan.Head, path);
        if (manifest is null || adapter.FileText(directory, plan, plan.TargetHead, path) is not null) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(manifest);
            return document.RootElement.ValueKind == JsonValueKind.Object && ChangeText(document.RootElement, "format") == Vericairn.Runtime.Release.ReleaseFlow.DocumentationManifest
                && ChangeText(document.RootElement, "version") == version;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Where a software product's release keeps its documentation sets, one directory per version (the release flow's docs-set step).
    private const string DocumentationSetDirectory = "docs/releases/";

    /// <summary>Opens only from a recorded successful push of the same repository, branch and exact head.</summary>
    public ChangeResponse ChangesOpen(ChangesOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using IDisposable counted = SessionAction("changes open", PresencePlan(request.By, () => OpenPresence("branch " + request.Branch)),
            ChangeSession("changes open", request.Product, request.Head, new("change-open", request.Branch, null, null, 3, [])));
        return ChangeEffect(request.Product, request.By, "change-open", request.Branch, request.Head, request.Reconciliation, claim =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.Body is null || request.Body.Length > 20000)
                throw new ProviderRefusedException("An opening needs a title of at most 200 characters and a body of at most 20000.");
            lock (gate)
            {
                if (!ScannedPush(claim, request.Branch, request.Head))
                    throw new ProviderRefusedException("No successful scanned push is recorded for this repository, branch and exact head.");
            }
            claim.Body["target"]!["contentHash"] = ContentHash.Sha256(JsonSerializer.SerializeToUtf8Bytes(new { request.Title, request.Body }));
            PresenceEffectAhead();
            DispatchChange(claim);
            claim.EffectMayHaveStarted = true;
            ChangeReceipt receipt = claim.Configuration.Provider.Open(claim.Configuration.Destination, request.Branch,
                claim.Configuration.Target, request.Head, request.Title, request.Body);
            if (receipt.PullRequest is null) throw new ProviderUncertainException("The opening has no confirmed pull-request identity.");
            claim.Body["target"]!["pullRequest"] = receipt.PullRequest;
            FinishChange(claim, "observed-success", "The host confirmed the opened pull request at the requested head.");
            return ChangeAnswer(claim, true, "The opening and its receipt are recorded.");
        });
    }

    /// <summary>Records an exact-head observation, refuses every unmet check, merges with a head pin, then records the target run.</summary>
    public ChangeResponse ChangesMerge(ChangesMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using IDisposable counted = SessionAction("changes merge", PresencePlan(request.By, () => MergePresence("pull request " + request.PullRequest)),
            ChangeSession("changes merge", request.Product, request.Head, new("change-merge", null, request.PullRequest, null, 3, [("pull-request", 3, true), ("target-run", 3, true)])));
        return ChangeEffect(request.Product, request.By, "change-merge", request.PullRequest, request.Head, request.Reconciliation, claim =>
        {
            if (claim.Configuration.Required.Count == 0) throw new ProviderRefusedException("No required checks are declared on the product.");
            // A retry keeps the route its earlier attempt recorded: a merge pinned to the merge queue goes through it again.
            bool pinnedToQueue = QueuedMerge(claim.Body);
            MergeQueueObservation? queue = null;
            PullRequestObservation? pull = ObserveChange(claim, "pull-request", observation =>
            {
                PullRequestObservation? value = claim.Configuration.Provider.Observe(claim.Configuration.Destination, request.PullRequest);
                if (value is not null)
                {
                    StorePullObservation(observation, value);
                    // The same observation records whether the target branch requires a merge queue, and the pull request's place in it.
                    queue = claim.Configuration.Provider.ObserveQueue(claim.Configuration.Destination, value.Number);
                    StoreQueueObservation(observation, queue);
                }
                return value;
            }, out RecordWritten observed);
            claim.Body["checks"] = JsonSerializer.SerializeToNode(pull?.Checks ?? [], ChangeJson);
            IReadOnlyList<string> refused = pull is null ? [] : ChangeGate.Refusals(request.Head, claim.Configuration.Required, pull.Checks, pull.Complete);
            // A retry of a queued merge whose pull request the queue has merged at the pinned head records that merge, without another effect.
            if (pinnedToQueue && pull is { Complete: true, State: "merged" } && pull.Number == request.PullRequest && pull.Head == request.Head
                && pull.Target == claim.Configuration.Target && pull.MergeCommit is not null && Patterns.Commit.IsMatch(pull.MergeCommit) && refused.Count == 0)
                return RecordQueuedMerge(claim, pull.MergeCommit);
            if (pull is null || pull.Number != request.PullRequest || pull.Head != request.Head || pull.Target != claim.Configuration.Target || pull.State != "open")
                throw new ProviderRefusedException(pinnedToQueue && pull?.State == "merged"
                    ? "The pull request is already merged; observe the original merge with vericairn changes observe --operation " + claim.Written.Id + ", which records the queue's outcome."
                    : "The pull request is absent, closed, targets another branch or its head moved.");
            if (refused.Count > 0) throw new ProviderRefusedException(string.Join(" ", refused));
            if (!WholeQueue(queue))
                throw new ProviderRefusedException("Whether the target branch requires a merge queue could not be observed completely; nothing was merged.");
            // Every merged head rests on a scan of exactly its commits, even when its pull request was opened elsewhere.
            lock (gate)
            {
                if (!ScannedPush(claim, pull.Branch, request.Head))
                    throw new ProviderRefusedException("No successful scanned push of this exact head to the pull request's branch is recorded; push it with vericairn changes push, which scans exactly its commits, before merging.");
            }
            (string? message, string? warned) = CheckMergeMessage(claim, pull, queue.Required || pinnedToQueue);
            claim.Body["target"]!["pullRequest"] = pull.Number;
            claim.Body["target"]!["branch"] = pull.Branch;
            claim.Body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Checks observed before merge.", ["hash"] = observed.Hash, ["location"] = observed.Path });
            if (queue.Required || pinnedToQueue)
            {
                if (!queue.Required)
                    throw new ProviderRefusedException("This merge was recorded through the target branch's merge queue, which the branch no longer requires; nothing was merged. Push a new head to merge it directly.");
                if (queue.State is "queued" or "unmergeable")
                    throw new ProviderRefusedException("The pull request is already in the merge queue, enqueued outside this operation; nothing was requested.");
                ChangeResponse enqueued = EnqueueChange(claim, pull.Number);
                return warned is null ? enqueued : enqueued with { Message = enqueued.Message + " " + warned };
            }
            PresenceEffectAhead();
            DispatchChange(claim);
            claim.EffectMayHaveStarted = true;
            // Where the estate checks commit messages, the merge commit is created with exactly the message checked (CODE-81).
            ChangeReceipt receipt = message is null ? claim.Configuration.Provider.Merge(claim.Configuration.Destination, pull.Number, request.Head)
                : claim.Configuration.Provider.Merge(claim.Configuration.Destination, pull.Number, request.Head, message);
            if (receipt.PullRequest != pull.Number || receipt.MergeCommit is null || !Patterns.Commit.IsMatch(receipt.MergeCommit))
                throw new ProviderUncertainException("The merge was requested but its exact merge commit was not confirmed.");
            claim.Body["target"]!["mergeCommit"] = receipt.MergeCommit;
            FinishChange(claim, "observed-success", "The host confirmed the merge of the pinned head.");
            ChangeResponse merged = ObserveChangeTarget(claim, receipt.MergeCommit);
            return warned is null ? merged : merged with { Message = merged.Message + " " + warned };
        });
    }

    // CODE-81: the message of the merge commit this merge would create is matched against the estate's blocked terms at the estate's
    // level before anything is merged. The trailer lines the estate requires apply to the commits a push carries, not to the merge commit
    // the host creates. Returns the message to merge with, null where the estate checks no commit message, and the warning, if any.
    private (string? Message, string? Warned) CheckMergeMessage(ChangeClaim claim, PullRequestObservation pull, bool queued)
    {
        CommitMessages.Settings settings;
        lock (gate) settings = CommitMessages.In(session.Load(), DateTimeOffset.UtcNow);
        if (settings.Lists.IsEmpty) return (null, null);
        if (queued && pull.Title is null)
            throw new ProviderRefusedException("The estate checks commit messages, and the pull request's title, which its merge commit's message carries, could not be observed; nothing was merged.");
        // As the host composes a merge commit's message: a title naming the pull request and its branch, then the pull request's title.
        string message = $"Merge pull request #{pull.Number} from {claim.Configuration.Destination.Split('/')[0]}/{pull.Branch}"
            + (string.IsNullOrEmpty(pull.Title) ? string.Empty : "\n\n" + pull.Title);
        (CommitMessages.Finding? refusal, IReadOnlyList<CommitMessages.Finding> warnings) =
            CommitMessages.Check(settings, [("The merge commit of pull request " + pull.Number, message)], trailers: false);
        if (refusal is not null) throw new ProviderRefusedException(CommitMessages.Describe(refusal) + " Nothing was merged; retitle the pull request and merge again.");
        string? warned = MessageWarning(settings, warnings, queued ? "enqueued" : "merged");
        claim.Body["evidence"]!.AsArray().Add(new JsonObject
        {
            ["summary"] = (queued ? "The message the merge queue composes for this change's merge commit was checked" : "The merge commit's message was checked, and the merge sets it exactly,")
                + " against the estate's blocked terms" + (warned is null ? "; none was refused." : ". " + warned),
        });
        return (message, warned);
    }

    /// <summary>
    /// Enqueues a checked pull request into its target branch's required merge queue (CODE-71), under the same authority, checks and
    /// scanned push a direct merge needs: the merge records the queue as its route, its dispatch is the enqueue request at the pinned
    /// head, and an observation confirms the pull request's place in the queue before the merge waits there. The queue merges it later;
    /// <see cref="ChangesObserve"/> records the outcome against the same merge.
    /// </summary>
    private ChangeResponse EnqueueChange(ChangeClaim claim, string number)
    {
        claim.Body["target"]!["mergeQueue"] = "required";
        string key = OperationKey(EstateServiceValues.Element(claim.Body));
        RevisePresence(claim.By, [OperationSignature("change-merge", "dispatched", key), .. ObservationPresence(key, "pull-request")],
            "the target branch requires its merge queue, so the change is enqueued and its place in the queue observed");
        PresenceEffectAhead();
        DispatchChange(claim);
        claim.EffectMayHaveStarted = true;
        ChangeReceipt receipt = claim.Configuration.Provider.Enqueue(claim.Configuration.Destination, number, claim.Head);
        if (receipt.PullRequest != number) throw new ProviderUncertainException("The enqueue was requested but its pull request was not confirmed.");
        return ObserveQueuedMerge(claim);
    }

    /// <summary>Whether a merge operation's body records the target branch's merge queue as its route (CODE-71).</summary>
    private static bool QueuedMerge(JsonObject body) => body["target"]?["mergeQueue"]?.GetValue<string>() == "required";

    /// <summary>Whether a recorded merge operation went through the target branch's merge queue (CODE-71).</summary>
    private static bool QueuedMerge(RecordEnvelope merge) =>
        merge.Body.TryGetProperty("target", out JsonElement target) && ChangeText(target, "mergeQueue") == "required";

    /// <summary>
    /// Observes a queued merge's place in the merge queue and then its pull request, in one recorded observation, and records the outcome
    /// against the merge (CODE-71): merged at a commit, then that commit's target run; still queued at the pinned head, waiting; a known
    /// failure only on the host's positive evidence about this attempt (still listed as unmergeable at the pinned head, a removal the host
    /// dates after this attempt's dispatch, the pull request closed or its head moved); anything else, such as an entry gone with no such
    /// removal while the pull request does not yet read merged, stays uncertain and is never retried before it is reconciled.
    /// </summary>
    private ChangeResponse ObserveQueuedMerge(ChangeClaim claim)
    {
        string number = claim.Body["target"]!["pullRequest"]!.GetValue<string>();
        MergeQueueObservation? queue = null;
        PullRequestObservation? pull = ObserveChange(claim, "pull-request", observation =>
        {
            // The queue is read first: a merge between the two reads then shows as merged, never as an open change out of the queue.
            queue = claim.Configuration.Provider.ObserveQueue(claim.Configuration.Destination, number);
            PullRequestObservation? value = claim.Configuration.Provider.Observe(claim.Configuration.Destination, number);
            if (value is not null) StorePullObservation(observation, value);
            StoreQueueObservation(observation, queue);
            return value;
        }, out RecordWritten observation);
        string key = OperationKey(EstateServiceValues.Element(claim.Body));
        bool complete = pull is { Complete: true } && WholeQueue(queue) && pull.Number == number && pull.Target == claim.Configuration.Target;
        if (complete && pull!.State == "merged" && pull.Head == claim.Head && pull.MergeCommit is not null && Patterns.Commit.IsMatch(pull.MergeCommit))
            return RecordQueuedMerge(claim, pull.MergeCommit);
        bool open = complete && pull!.State == "open";
        bool atHead = open && pull!.Head == claim.Head && queue!.Head == claim.Head;
        if (atHead && queue!.State == "queued")
        {
            // An enqueue whose receipt was lost is confirmed by its place in the queue, under its original identity.
            if (claim.Status is "not-started" or "uncertain")
            {
                ExpectPresence(claim.By, [OperationSignature("change-merge", "dispatched", key)], "the change was observed in the merge queue, so its enqueue is recorded as dispatched");
                claim.Body["dispatchedAt"] ??= Now();
                claim.Body["reconciliation"] = "The change was observed in the merge queue at its pinned head under its original identity.";
                SaveChange(claim, "dispatched");
            }
            return ChangeAnswer(claim, false, "The change waits in the target branch's merge queue at the pinned head; observe this operation again for the queue's outcome.")
                with { Observation = observation, CheckStatus = "queued" };
        }
        // Only the host's positive evidence about this attempt settles it unmerged.
        string? evidence = !complete ? null
            : pull!.State == "closed" ? "The pull request was closed without its merge; nothing was merged."
            : open && pull!.Head != claim.Head ? "The pull request's head moved from the pinned head, so the queue cannot merge it; nothing was merged."
            : atHead && queue!.State == "unmergeable" ? "The merge queue found the change could not merge" + Because(queue.Reason)
            : open && queue!.State is "failed" or "removed" && queue.RemovedAt is { } removedAt && DispatchedBefore(claim, removedAt)
                ? (queue.State == "failed" ? "The merge queue took the change out unmerged for a failure" : "The change was removed from the merge queue unmerged") + Because(queue.Reason)
            : null;
        if (evidence is not null && claim.Status is "dispatched" or "uncertain")
        {
            ExpectPresence(claim.By, [OperationSignature("change-merge", "known-failure", key)], "the host shows the change was not merged, so its known failure is recorded");
            FinishChange(claim, "known-failure", evidence);
            return ChangeAnswer(claim, false, evidence + " Resolve the cause, then retry the merge with --reconciliation; if the change may still have merged, observe this operation first.")
                with { Observation = observation };
        }
        if (claim.Status == "dispatched")
        {
            ExpectPresence(claim.By, [OperationSignature("change-merge", "uncertain", key)], "the change's merge is not confirmed in the queue or on the pull request, so the merge is recorded as uncertain");
            FinishChange(claim, "uncertain", "The change is not confirmed in the merge queue and its pull request is not seen merged; observe it again before any retry.");
        }
        return ChangeAnswer(claim, false, "The queued merge remains unconfirmed; observe this operation again and do not retry.") with { Observation = observation, CheckStatus = "uncertain" };

        static string Because(string? reason) => QueueReason(reason) is string token ? " (" + token + ")." : ".";
    }

    // Records the merge queue's merge of the pinned head at its merge commit, then observes that commit's target run (CODE-71).
    private ChangeResponse RecordQueuedMerge(ChangeClaim claim, string mergeCommit)
    {
        string key = OperationKey(EstateServiceValues.Element(claim.Body));
        ExpectPresence(claim.By, claim.Status is "not-started" or "known-failure" or "canceled"
            ? [OperationSignature("change-merge", "uncertain", key), OperationSignature("change-merge", "observed-success", key)] : [OperationSignature("change-merge", "observed-success", key)],
            "the merge queue merged the change, so its outcome is recorded");
        if (claim.Status is "not-started" or "known-failure" or "canceled")
        {
            claim.Body["dispatchedAt"] ??= Now();
            claim.Body["reconciliation"] = "The queued merge was subsequently observed under its original identity.";
            FinishChange(claim, "uncertain", "Reconciliation of the earlier unconfirmed effect.");
        }
        claim.Body["target"]!["mergeCommit"] = mergeCommit;
        FinishChange(claim, "observed-success", "The merge queue merged the pinned head; the target branch moved to the commit its group's run tested.");
        ExpectPresence(claim.By, ObservationPresence(key, "target-run"), "the merge queue merged the change, so the target branch's run at the merge commit is observed");
        return ObserveChangeTarget(claim, mergeCommit);
    }

    // Whether the host dates a removal after this attempt's dispatch, recorded before the enqueue was requested.
    private static bool DispatchedBefore(ChangeClaim claim, DateTimeOffset removedAt) =>
        claim.Body["dispatchedAt"]?.GetValue<string>() is string dispatched
        && DateTimeOffset.TryParse(dispatched, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset at)
        && removedAt > at;

    /// <summary>
    /// Whether this attempt of a queued merge was seen in the merge queue at its pinned head: a recorded observation of its pull request,
    /// since the attempt began after the merge chain's latest known failure or cancellation, that found it queued there. Caller holds the gate.
    /// </summary>
    private static bool QueuedConfirmed(EstateModel model, ChangeClaim claim) =>
        model.ById.TryGetValue(claim.Written.Id, out RecordEnvelope? current) && QueuedConfirmed(model, current, claim.Head);

    /// <summary>As <see cref="QueuedConfirmed(EstateModel, ChangeClaim)"/>, for a recorded merge revision and its pinned head.</summary>
    private static bool QueuedConfirmed(EstateModel model, RecordEnvelope merge, string head)
    {
        IReadOnlyList<string> chain = model.Chain(merge);
        int start = 0;
        for (int i = 0; i < chain.Count; i++)
            if (model.ById.TryGetValue(chain[i], out RecordEnvelope? revision) && EstateModel.BodyString(revision, "status") is "known-failure" or "canceled") start = i + 1;
        HashSet<string> attempt = [.. chain.Skip(start)];
        return model.Current("operation").Any(o => EstateModel.BodyString(o, "action") == "change-observe" && EstateModel.BodyString(o, "status") == "observed-success"
            && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "observation") == "pull-request"
            && attempt.Contains(ChangeText(t, "operation") ?? string.Empty) && ChangeText(t, "queue") == "queued" && ChangeText(t, "queueHead") == head
            && ChangeText(t, "observedHead") == head && ChangeText(t, "state") == "open");
    }

    // The merge queue's facts beside a pull-request observation (CODE-71): whether the target branch requires it, the pull request's
    // place in it, the pull request's own head read with that place, the queue's group commit, and the host's latest removal, each a
    // target fact of that observation.
    private static void StoreQueueObservation(ChangeClaim claim, MergeQueueObservation? queue)
    {
        JsonObject target = claim.Body["target"]!.AsObject();
        if (queue is null)
        {
            target["complete"] = "false";
            return;
        }
        target["mergeQueue"] = queue.Required ? "required" : "none";
        target["queue"] = MergeQueueObservation.States.Contains(queue.State) ? queue.State : "unknown";
        if (queue.Head is not null && Patterns.Commit.IsMatch(queue.Head)) target["queueHead"] = queue.Head;
        if (queue.Group is not null && Patterns.Commit.IsMatch(queue.Group)) target["queueGroup"] = queue.Group;
        if (QueueReason(queue.Reason) is string reason) target["queueReason"] = reason;
        if (queue.RemovedAt is { } removedAt) target["queueRemovedAt"] = RecordParser.FormatTimestamp(removedAt);
        if (!WholeQueue(queue)) target["complete"] = "false";
    }

    // A complete queue observation in a known state, whose commits, when it names them, are exact.
    private static bool WholeQueue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] MergeQueueObservation? queue) => queue is { Complete: true }
        && MergeQueueObservation.States.Contains(queue.State) && (queue.Head is null || Patterns.Commit.IsMatch(queue.Head))
        && (queue.Group is null || Patterns.Commit.IsMatch(queue.Group));

    // A host's removal reason enters a record only as a short upper-case token, never as free text.
    private static string? QueueReason(string? reason) => reason is { Length: > 0 and <= 64 } && reason.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') && char.IsAsciiLetterUpper(reason[0]) ? reason : null;

    // A recorded push, observed at the remote after its scan of exactly the pushed commits passed. Caller holds the gate.
    private bool ScannedPush(ChangeClaim claim, string? branch, string head) =>
        session.Load().Current("operation").Any(o => EstateModel.BodyString(o, "action") == "change-push"
            && EstateModel.BodyString(o, "status") == "observed-success" && o.Body.TryGetProperty("target", out JsonElement t)
            && ChangeText(t, "repository") == claim.Configuration.Repository.Id && ChangeText(t, "branch") == branch
            && ChangeText(t, "head") == head && ChangeText(t, "destination") == claim.Configuration.Destination);

    /// <summary>Observes an original change under its recorded pins. Absence, stale heads and incomplete reads remain uncertain.</summary>
    public ChangeResponse ChangesObserve(ChangesObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using IDisposable counted = SessionAction("changes observe", PresencePlan(request.By, () =>
        {
            RecordEnvelope? named = Ulid.IsValid(request.Operation) && session.Load().ById.TryGetValue(request.Operation, out RecordEnvelope? found) ? session.Load().Latest(found) : null;
            return named is null || !ChangeOperationRulesForRuntime(named) ? [] : request.Cancel is null ? ObservePresence(named)
                : [OperationSignature(EstateModel.BodyString(named, "action"), "canceled", OperationKey(named.Body))];
        }), Ulid.IsValid(request.Operation) && request.Cancel is null ? ObserveSession(request.Operation) : null);
        ChangeClaim? claim = null;
        try
        {
            lock (gate)
            {
                RequireChangeAuthority(request.By);
                EstateModel model = session.Load();
                if (request.Cancel is not null && (!model.HasRole(request.By, Roles.Operator)
                    || string.IsNullOrWhiteSpace(request.Cancel) || request.Cancel.Length > 4000))
                    throw new ProviderRefusedException("Only the operator can cancel an intent, with an explicit no-effect reconciliation of at most 4000 characters.");
                if (!Ulid.IsValid(request.Operation) || !model.ById.TryGetValue(request.Operation, out RecordEnvelope? named) || !ChangeOperationRulesForRuntime(named))
                    throw new ProviderRefusedException("Name a recorded push, opening or merge operation.");
                RecordEnvelope current = model.Latest(named);
                if (changeEffects.Contains(EstateModel.BodyString(current, "identity")!))
                    return ChangePriorRefusal(current, "The original effect is still in progress; observe it after it returns.");
                JsonElement target = current.Body.GetProperty("target");
                ChangeConfiguration configuration = ChangeConfigurationFor(ChangeText(target, "product")!, currentDeclaration: false);
                claim = new(configuration, request.By, JsonNode.Parse(current.Body.GetRawText())!.AsObject(),
                    new(current.ExpectedPath, current.Id, current.ComputedHash));
                changeEffects.Add(claim.Identity);
            }

            // A queued merge whose place in the merge queue was confirmed waits there as dispatched; the queue's outcome settles it (CODE-71).
            bool waiting = claim.Status == "dispatched" && claim.Action == "change-merge" && QueuedMerge(claim.Body) && request.Cancel is null;
            if (waiting) lock (gate) waiting = QueuedConfirmed(session.Load(), claim);
            if (claim.Status is "not-started" or "dispatched" && !waiting)
            {
                ExpectPresence(request.By, [OperationSignature(claim.Action, "uncertain", OperationKey(EstateServiceValues.Element(claim.Body)))], "the original intent has no confirmed outcome, so it is recorded as uncertain first");
                FinishChange(claim, "uncertain", "The original intent has no confirmed outcome; reconcile it by observation before any retry.");
            }
            if (request.Cancel is not null)
            {
                if (claim.Status is "observed-success" or "canceled")
                    return ChangeAnswer(claim, false, "A confirmed effect or an already canceled intent cannot be canceled again.");
                claim.Body["reconciliation"] = request.Cancel;
                FinishChange(claim, "canceled", "The operator explicitly reconciled the original intent as having had no effect.");
                return ChangeAnswer(claim, true, "The operator's no-effect reconciliation is recorded. A retry still requires fresh checks or a fresh scan.");
            }
            string action = claim.Action;
            if (action == "change-merge" && claim.Status == "observed-success" && claim.Body["target"]!["mergeCommit"] is JsonNode merged)
                return ObserveChangeTarget(claim, merged.GetValue<string>());
            if (action == "change-merge" && QueuedMerge(claim.Body)) return ObserveQueuedMerge(claim);
            bool confirmed;
            RecordWritten observation;
            if (action == "change-push")
            {
                IPushAdapter adapter = Providers.PushAdapter ?? throw new ProviderRefusedException("No push adapter is registered.");
                string? head = ObserveChange(claim, "push-head", observed =>
                {
                    string? value = adapter.Observe(ChangeClone(claim.Configuration), claim.Configuration.Provider.PushRemote(claim.Configuration.Destination),
                        claim.Body["target"]!["branch"]!.GetValue<string>());
                    if (value is not null) observed.Body["target"]!["remoteObserved"] = value;
                    observed.Body["target"]!["state"] = value is null ? "absent" : "present";
                    return value;
                }, out observation);
                confirmed = head == claim.Head;
            }
            else
            {
                string identity = claim.Body["target"]!["pullRequest"]?.GetValue<string>() ?? claim.Body["target"]!["branch"]!.GetValue<string>();
                PullRequestObservation? pull = ObserveChange(claim, "pull-request", observed =>
                {
                    PullRequestObservation? value = claim.Configuration.Provider.Observe(claim.Configuration.Destination, identity);
                    if (value is not null) StorePullObservation(observed, value);
                    return value;
                }, out observation);
                confirmed = pull is { Complete: true } && pull.Head == claim.Head && pull.Target == claim.Configuration.Target
                    && (action == "change-open" || pull.State == "merged" && pull.MergeCommit is not null && Patterns.Commit.IsMatch(pull.MergeCommit));
                if (confirmed)
                {
                    claim.Body["target"]!["pullRequest"] = pull!.Number;
                    if (action == "change-merge") claim.Body["target"]!["mergeCommit"] = pull.MergeCommit;
                }
            }
            if (confirmed && claim.Status != "observed-success")
            {
                string key = OperationKey(EstateServiceValues.Element(claim.Body));
                ExpectPresence(request.By, claim.Status is "not-started" or "known-failure" or "canceled"
                    ? [OperationSignature(action, "uncertain", key), OperationSignature(action, "observed-success", key)] : [OperationSignature(action, "observed-success", key)],
                    "the observation confirmed the original effect, so its outcome is recorded");
                // A crash may have left the initial intent current. Preserve uncertainty as the bridge to the observation.
                if (claim.Status is "not-started" or "known-failure" or "canceled")
                {
                    claim.Body["dispatchedAt"] ??= Now();
                    claim.Body["reconciliation"] = "The effect was subsequently observed under its original identity.";
                    FinishChange(claim, "uncertain", "Reconciliation of the earlier unconfirmed effect.");
                }
                FinishChange(claim, "observed-success", "The original effect was confirmed by a new recorded observation.");
            }
            if (confirmed && action == "change-merge")
            {
                ExpectPresence(request.By, ObservationPresence(OperationKey(EstateServiceValues.Element(claim.Body)), "target-run"), "the merge is confirmed, so the target branch's run at the merge commit is observed");
                return ObserveChangeTarget(claim, claim.Body["target"]!["mergeCommit"]!.GetValue<string>());
            }
            return ChangeAnswer(claim, confirmed, confirmed ? "The original effect is confirmed." : "The original effect remains unconfirmed; do not retry.")
                with { Observation = observation, CheckStatus = confirmed ? null : "uncertain" };
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException or ChangeWriteException or IOException or UnauthorizedAccessException)
        {
            return claim is null ? ChangeProblem(exception is ProviderRefusedException ? exception.Message : "Observation could not be recorded.")
                : ChangeAnswer(claim, false, "The observation is incomplete; the original outcome has not been changed.") with { CheckStatus = "uncertain" };
        }
        finally { if (claim is not null) lock (gate) changeEffects.Remove(claim.Identity); }
    }

    private ChangeResponse ChangeEffect(string product, string by, string action, string name, string head, string? reconciliation, Func<ChangeClaim, ChangeResponse> effect)
    {
        ChangeClaim? claim = null;
        try
        {
            lock (gate)
            {
                RequireChangeAuthority(by);
                if (string.IsNullOrEmpty(head) || !Patterns.Commit.IsMatch(head) || string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.StartsWith('-'))
                    throw new ProviderRefusedException("An exact commit and an explicit branch or pull-request number are required.");
                ChangeConfiguration configuration = ChangeConfigurationFor(product, currentDeclaration: true);
                string identity = $"{configuration.Provider.Provider}:{configuration.Repository.Id}:{action}:{name}:{head}:{configuration.Product.ComputedHash[7..]}";
                RecordEnvelope? prior = CurrentOperation(identity);
                if (changeEffects.Contains(identity) && prior is not null)
                    return ChangePriorRefusal(prior, "This effect is already in progress; observe it after it returns.");
                if (prior is not null && (EstateModel.BodyString(prior, "status") is not ("known-failure" or "canceled") || string.IsNullOrWhiteSpace(reconciliation)))
                    return ChangePriorRefusal(prior, "This identity has an earlier intent; observe any unresolved effect, and supply reconciliation before retrying a known failure.");
                // A changed head must not bypass an unresolved request for the same branch or pull request.
                RecordEnvelope? unresolved = session.Load().Current("operation").FirstOrDefault(o => EstateModel.BodyString(o, "action") == action
                    && EstateModel.BodyString(o, "identity") != identity && EstateModel.BodyString(o, "status") is "not-started" or "dispatched" or "uncertain"
                    && o.Body.TryGetProperty("target", out JsonElement t)
                    && (ChangeText(t, "repository") == configuration.Repository.Id
                        || EstateModel.BodyString(o, "provider") == configuration.Provider.Provider
                            && string.Equals(ChangeText(t, "destination"), configuration.Destination, StringComparison.OrdinalIgnoreCase))
                    && ChangeText(t, action == "change-merge" ? "pullRequest" : "branch") == name);
                if (unresolved is not null)
                    return ChangePriorRefusal(unresolved, "An earlier head of this change has an unresolved effect; reconcile that original identity first.");
                JsonObject body = ChangeIntent(configuration, action, identity, head);
                body["target"]![action == "change-merge" ? "pullRequest" : "branch"] = name;
                if (prior is not null)
                {
                    foreach (JsonProperty fact in prior.Body.GetProperty("target").EnumerateObject())
                        if (fact.Name is not ("base" or "remoteHead" or "targetHead") && body["target"]![fact.Name] is null)
                            body["target"]![fact.Name] = JsonNode.Parse(fact.Value.GetRawText());
                    body["status"] = "uncertain";
                    body["dispatchedAt"] = Now();
                    body["reconciliation"] = reconciliation;
                }
                WriteOutcome written = NewRecord(new("operation", by, EstateServiceValues.Element(body), $"product:{configuration.Product.Id}", prior?.Id));
                if (!written.Ok) throw new ChangeWriteException(written.Problem!);
                claim = new(configuration, by, body, written.Written!);
                changeEffects.Add(identity);
            }
            return effect(claim);
        }
        catch (ProviderRefusedException exception)
        {
            if (claim is null) return ChangeProblem(exception.Message);
            return TryFinishChange(claim, "known-failure", exception.Message);
        }
        catch (ProviderUncertainException)
        {
            return claim is null ? ChangeProblem("The provider outcome is uncertain.")
                : claim.EffectMayHaveStarted
                    ? TryFinishChange(claim, "uncertain", "The request may have taken effect; observe its original identity before any retry.")
                    : TryFinishChange(claim, "known-failure", "The prerequisite observation is incomplete; no repository effect was requested.");
        }
        catch (Exception exception) when (exception is ChangeWriteException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Once a claim exists, a lost receipt must never become permission for another effect.
            return claim is null ? ChangeProblem("The operation intent could not be recorded.")
                : TryFinishChange(claim, claim.EffectMayHaveStarted ? "uncertain" : "known-failure",
                    claim.EffectMayHaveStarted ? "The outcome could not be recorded completely; observe the original identity before any retry."
                        : "The prerequisites could not be completed; no repository effect was requested.");
        }
        finally { if (claim is not null) lock (gate) changeEffects.Remove(claim.Identity); }
    }

    private ChangeConfiguration ChangeConfigurationFor(string product, bool currentDeclaration)
    {
        EstateModel model = session.Load();
        if (!Ulid.IsValid(product) || !model.ById.TryGetValue(product, out RecordEnvelope? record) || record.Type != "product" || currentDeclaration && !model.IsCurrent(record)
            || !record.Body.TryGetProperty("changes", out JsonElement declaration))
            throw new ProviderRefusedException("Name a current product with a repository changes declaration.");
        string repository = ChangeText(declaration, "repository")!;
        if (!model.ById.TryGetValue(repository, out RecordEnvelope? resource) || resource.Type != "resource" || EstateModel.BodyString(resource, "kind") != "repository"
            || currentDeclaration && !model.IsCurrent(resource))
            throw new ProviderRefusedException("The declared repository resource is unavailable or superseded.");
        IPullRequestProvider provider = Providers.PullRequests(ChangeText(declaration, "provider")!) ?? throw new ProviderRefusedException("The declared pull-request provider is unavailable.");
        JsonElement location = resource.Body.GetProperty("location");
        string[] destinations = provider.Descriptor.ServedLocations.Select(k => ChangeText(location, k)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (destinations.Length != 1) throw new ProviderRefusedException("The declared provider cannot identify exactly one destination from this repository resource.");
        return new(record, resource, provider, destinations[0], ChangeText(declaration, "target")!,
            declaration.GetProperty("requiredChecks").EnumerateArray().Select(n => n.GetString()!).ToArray());
    }

    private void RequireChangeAuthority(string by)
    {
        EstateModel model = session.Load();
        if (session.Manifest.RecordFormat < 2) throw new ProviderRefusedException("Repository changes require record format 2 so target failures can raise an operator ask.");
        if (model.Findings.HasErrors) throw new ProviderRefusedException("The estate has validation errors; no repository operation is admitted.");
        DateTimeOffset at = DateTimeOffset.UtcNow;
        // Grants are read only from records their authors may write (EstateModel.Authority).
        bool granted = model.Authority.GrantLines.Select(model.Authority.Current).OfType<RecordEnvelope>().Any(a => a.AtTime <= at && !EstateModel.Expired(a, at)
            && EstateModel.BodyString(a, "grantee") == by && a.Body.GetProperty("scope").EnumerateArray().Any(s => s.GetString() == Scopes.RepositoryWrite));
        if (!Ulid.IsValid(by) || model.CurrentPrincipal(by) is null || !model.HasRole(by, Roles.Operator) && !granted)
            throw new ProviderRefusedException("The acting principal needs repository-write authority in force.");
    }

    private string ChangeClone(ChangeConfiguration configuration) =>
        RepositoryClones.Find(session.Manifest.Estate, configuration.Repository.Id)
        ?? throw new ProviderRefusedException("No local clone is registered for the declared repository.");

    private static JsonObject ChangeIntent(ChangeConfiguration configuration, string action, string identity, string head) => new()
    {
        ["provider"] = configuration.Provider.Provider, ["action"] = action, ["identity"] = identity,
        ["intent"] = $"{action} at the pinned head, with the declared repository and checks.", ["status"] = "not-started",
        ["target"] = new JsonObject { ["product"] = configuration.Product.Id, ["productHash"] = configuration.Product.ComputedHash,
            ["repository"] = configuration.Repository.Id, ["repositoryHash"] = configuration.Repository.ComputedHash,
            ["destination"] = configuration.Destination, ["target"] = configuration.Target, ["head"] = head },
        ["requiredChecks"] = new JsonArray(configuration.Required.Select(n => (JsonNode)n).ToArray()), ["evidence"] = new JsonArray(),
    };

    private void DispatchChange(ChangeClaim claim, bool checkDeclaration = true)
    {
        lock (gate)
        {
            RequireChangeAuthority(claim.By);
            if (checkDeclaration && (ChangeConfigurationFor(claim.Configuration.Product.Id, true).Product.ComputedHash != claim.Configuration.Product.ComputedHash))
                throw new ProviderRefusedException("The product's declaration changed before the effect.");
            claim.Body["dispatchedAt"] = Now();
            SaveChange(claim, "dispatched");
        }
    }

    private T ObserveChange<T>(ChangeClaim original, string kind, Func<ChangeClaim, T> observe, out RecordWritten written)
    {
        JsonObject body = ChangeIntent(original.Configuration, "change-observe", original.Identity + ":observe:" + Ulid.NewUlid().Value, original.Head);
        body["target"]!["operation"] = original.Written.Id;
        body["target"]!["observation"] = kind;
        ChangeClaim observation;
        lock (gate)
        {
            RequireChangeAuthority(original.By);
            WriteOutcome intent = NewRecord(new("operation", original.By, EstateServiceValues.Element(body), $"operation:{original.Written.Id}"));
            if (!intent.Ok) throw new ChangeWriteException(intent.Problem!);
            observation = new(original.Configuration, original.By, body, intent.Written!);
            DispatchChange(observation, checkDeclaration: false);
        }
        try
        {
            T result = observe(observation);
            FinishChange(observation, result is null || observation.Body["target"]!["complete"]?.GetValue<string>() == "false" ? "uncertain" : "observed-success",
                "The host observation was recorded; missing facts and incomplete inventories remain uncertain.");
            written = observation.Written;
            return result;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException or ChangeWriteException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TryFinishChange(observation, "uncertain", "The host could not be observed completely; no successful checks are inferred.");
            throw new ProviderUncertainException("The observation is incomplete.");
        }
    }

    private ChangeResponse ObserveChangeTarget(ChangeClaim claim, string commit)
    {
        // A queued merge's commit is the one its group's run tested: that run and the branch's push runs of the commit both count; a
        // commit the queue landed inside a group is judged by the runs of the group's head that landed it (CODE-71).
        bool queued = QueuedMerge(claim.Body);
        TargetObservation target = ObserveChange(claim, "target-run", observation =>
        {
            observation.Body["target"]!["mergeCommit"] = commit;
            if (queued) observation.Body["target"]!["mergeQueue"] = "required";
            TargetObservation value = queued
                ? claim.Configuration.Provider.ObserveQueuedTarget(claim.Configuration.Destination, claim.Configuration.Target, commit)
                : claim.Configuration.Provider.ObserveTarget(claim.Configuration.Destination, claim.Configuration.Target, commit);
            observation.Body["checks"] = JsonSerializer.SerializeToNode(value.Checks, ChangeJson);
            observation.Body["target"]!["observedHead"] = value.Head;
            observation.Body["target"]!["observedTarget"] = value.Branch;
            if (queued && value.Group is not null) observation.Body["target"]!["queueGroup"] = value.Group;
            observation.Body["target"]!["complete"] = value.Complete && (value.Group is null || queued && Patterns.Commit.IsMatch(value.Group)) ? "true" : "false";
            return value;
        }, out RecordWritten recorded);
        string gate = queued && target.Group is string group && Patterns.Commit.IsMatch(group) ? group : commit;
        bool exact = target.Head == commit && target.Branch == claim.Configuration.Target && target.Complete && (target.Group is null || gate == target.Group);
        bool passed = exact && (queued ? ChangeGate.QueuedRefusals(gate, claim.Configuration.Required, target.Checks, true)
            : ChangeGate.Refusals(commit, claim.Configuration.Required, target.Checks, true)).Count == 0;
        bool failed = exact && target.Checks.Any(c => c.Head == gate && c.Status == "completed"
            && c.Conclusion is "failure" or "cancelled" or "timed_out" or "action_required" or "stale" or "startup_failure");
        bool unknown = !exact || target.Checks.Any(c => c.Head != gate || c.Status == "unknown" || c.Conclusion == "unknown");
        RecordWritten? ask = null;
        if (failed)
        {
            lock (gate)
            {
                EstateModel model = session.Load();
                RecordEnvelope? existing = model.Current("ask").FirstOrDefault(a => a.Subject?.Id == claim.Written.Id);
                if (existing is not null) ask = new(existing.ExpectedPath, existing.Id, existing.ComputedHash);
                else
                {
                    string op = model.RootOperator!;
                    ExpectPresence(claim.By, [new("ask", "operation " + claim.Written.Id, "records a question that waits on its principal")], "the target branch's run failed, so you are asked how to correct it");
                    JsonObject body = new() { ["to"] = op, ["state"] = "open", ["question"] = "The target branch's run failed at the recorded merge commit. How should it be corrected?",
                        ["recommendation"] = "Inspect the recorded failing checks and prepare a corrective change.", ["consequence"] = "The failed target run remains unresolved." };
                    WriteOutcome result = NewRecord(new("ask", claim.By, EstateServiceValues.Element(body), $"operation:{claim.Written.Id}:{claim.Written.Hash}"));
                    if (!result.Ok) throw new ChangeWriteException(result.Problem!);
                    ask = result.Written;
                }
            }
        }
        return ChangeAnswer(claim, passed, passed ? "The pinned merge and its target checks are recorded as successful."
            : failed ? "The merge is recorded; its target run failed and an operator ask was recorded."
            : "The merge is recorded; its target checks are incomplete or pending. Observe this operation again.")
            with { Observation = recorded, Ask = ask, CheckStatus = passed ? "passed" : failed ? "failed" : unknown ? "uncertain" : "pending" };
    }

    private static void StorePullObservation(ChangeClaim claim, PullRequestObservation pull)
    {
        JsonObject target = claim.Body["target"]!.AsObject();
        target["observedHead"] = pull.Head; target["pullRequest"] = pull.Number; target["state"] = pull.State;
        target["branch"] = pull.Branch; target["observedTarget"] = pull.Target; target["complete"] = pull.Complete ? "true" : "false";
        if (pull.MergeCommit is not null) target["mergeCommit"] = pull.MergeCommit;
        claim.Body["checks"] = JsonSerializer.SerializeToNode(pull.Checks, ChangeJson);
    }

    private void SaveChange(ChangeClaim claim, string status)
    {
        JsonObject next = claim.Body.DeepClone().AsObject();
        next["status"] = status;
        string subject = claim.Action == "change-observe" ? $"operation:{claim.Body["target"]!["operation"]!.GetValue<string>()}" : $"product:{claim.Configuration.Product.Id}";
        WriteOutcome saved = NewRecord(new("operation", claim.By, EstateServiceValues.Element(next), subject, claim.Written.Id));
        if (!saved.Ok) throw new ChangeWriteException(saved.Problem!);
        claim.Body = next;
        claim.Written = saved.Written!;
    }

    private void FinishChange(ChangeClaim claim, string status, string summary)
    {
        claim.Body["observed"] = new JsonObject { ["summary"] = summary, ["at"] = Now() };
        if (status == "uncertain") claim.Body["dispatchedAt"] ??= Now();
        SaveChange(claim, status);
    }

    private ChangeResponse TryFinishChange(ChangeClaim claim, string status, string summary)
    {
        // A later observation failure cannot turn an already confirmed effect into a failed effect.
        if (claim.Status == "observed-success") return ChangeAnswer(claim, false, "The effect succeeded; its follow-up observation is incomplete.") with { CheckStatus = "uncertain" };
        try { FinishChange(claim, status, summary); return ChangeAnswer(claim, false, summary); }
        catch (Exception exception) when (exception is ChangeWriteException or IOException or UnauthorizedAccessException)
        { return ChangeAnswer(claim, false, "The receipt could not be recorded; reconcile the original identity before any retry.") with { Status = "uncertain" }; }
    }

    private static ChangeResponse ChangeAnswer(ChangeClaim claim, bool ok, string message) => new(claim.Identity, claim.Status, message, ok,
        claim.Written, PullRequest: claim.Body["target"]!["pullRequest"]?.GetValue<string>(), Head: claim.Head,
        MergeCommit: claim.Body["target"]!["mergeCommit"]?.GetValue<string>());

    private static ChangeResponse ChangeProblem(string message) => new("changes", "not-started", message, false, Problem: new("refused", message, []));
    private static ChangeResponse ChangePriorRefusal(RecordEnvelope prior, string message) => new(EstateModel.BodyString(prior, "identity")!,
        EstateModel.BodyString(prior, "status") is "not-started" or "dispatched" ? "uncertain" : EstateModel.BodyString(prior, "status")!, message, false,
        new(prior.ExpectedPath, prior.Id, prior.ComputedHash), Head: ChangeText(prior.Body.GetProperty("target"), "head"), Problem: new("refused", message, []));
    private static bool ChangeOperationRulesForRuntime(RecordEnvelope record) => record.Type == "operation" && EstateModel.BodyString(record, "action") is "change-push" or "change-open" or "change-merge";
    private static string? ChangeText(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    private static string Now() => RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);

    private RecordEnvelope? CurrentOperation(string identity)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.Current("operation").FirstOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
        }
    }
}
