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
        using IDisposable counted = presence.Action("changes push", PresencePlan(request.By, () => PushPresence("branch " + request.Branch)));
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
            RefuseHeldDisclosure(claim, adapter, directory, plan);
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
            return ChangeAnswer(claim, true, "The exact scanned head was pushed and observed.") with { Observation = receipt };
        });
    }

    // A held work order's key and distinctive text stay out of a public repository until the release carrying it is published,
    // whoever wrote the commits. A repository whose visibility is not recorded as public is not held to this check.
    private void RefuseHeldDisclosure(ChangeClaim claim, IPushAdapter adapter, string directory, PushPlan plan)
    {
        if (EstateModel.BodyString(claim.Configuration.Repository, "visibility") != "public") return;
        IReadOnlyList<DisclosureHolds.Hold> holds;
        lock (gate) holds = DisclosureHolds.Active(session.Load());
        if (holds.Count == 0) return;
        string text = adapter.PublishedText(directory, plan)
            ?? throw new ProviderRefusedException("A disclosure hold is in force, and the push's text could not be read to check it; nothing was pushed.");
        if (DisclosureHolds.Disclosed(holds, text) is string key)
            throw new ProviderRefusedException($"The push would disclose held work order {key} in a public repository before the release that carries it is published; nothing was pushed.");
    }

    /// <summary>Opens only from a recorded successful push of the same repository, branch and exact head.</summary>
    public ChangeResponse ChangesOpen(ChangesOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using IDisposable counted = presence.Action("changes open", PresencePlan(request.By, () => OpenPresence("branch " + request.Branch)));
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
        using IDisposable counted = presence.Action("changes merge", PresencePlan(request.By, () => MergePresence("pull request " + request.PullRequest)));
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
            if (pull is null || pull.Number != request.PullRequest || pull.Head != request.Head || pull.Target != claim.Configuration.Target || pull.State != "open")
                throw new ProviderRefusedException("The pull request is absent, closed, targets another branch or its head moved.");
            IReadOnlyList<string> refused = ChangeGate.Refusals(request.Head, claim.Configuration.Required, pull.Checks, pull.Complete);
            if (refused.Count > 0) throw new ProviderRefusedException(string.Join(" ", refused));
            if (!WholeQueue(queue))
                throw new ProviderRefusedException("Whether the target branch requires a merge queue could not be observed completely; nothing was merged.");
            // Every merged head rests on a scan of exactly its commits, even when its pull request was opened elsewhere.
            lock (gate)
            {
                if (!ScannedPush(claim, pull.Branch, request.Head))
                    throw new ProviderRefusedException("No successful scanned push of this exact head to the pull request's branch is recorded; push it with vericairn changes push, which scans exactly its commits, before merging.");
            }
            claim.Body["target"]!["pullRequest"] = pull.Number;
            claim.Body["target"]!["branch"] = pull.Branch;
            claim.Body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Checks observed before merge.", ["hash"] = observed.Hash, ["location"] = observed.Path });
            if (queue.Required || pinnedToQueue)
            {
                if (!queue.Required)
                    throw new ProviderRefusedException("This merge was recorded through the target branch's merge queue, which the branch no longer requires; nothing was merged. Push a new head to merge it directly.");
                if (queue.State == "queued" || queue.Head is not null)
                    throw new ProviderRefusedException("The pull request is already in the merge queue, enqueued outside this operation; nothing was requested.");
                return EnqueueChange(claim, pull.Number);
            }
            DispatchChange(claim);
            claim.EffectMayHaveStarted = true;
            ChangeReceipt receipt = claim.Configuration.Provider.Merge(claim.Configuration.Destination, pull.Number, request.Head);
            if (receipt.PullRequest != pull.Number || receipt.MergeCommit is null || !Patterns.Commit.IsMatch(receipt.MergeCommit))
                throw new ProviderUncertainException("The merge was requested but its exact merge commit was not confirmed.");
            claim.Body["target"]!["mergeCommit"] = receipt.MergeCommit;
            FinishChange(claim, "observed-success", "The host confirmed the merge of the pinned head.");
            return ObserveChangeTarget(claim, receipt.MergeCommit);
        });
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
    /// Observes a queued merge's pull request and its place in the merge queue in one recorded observation, and records the outcome
    /// against the merge (CODE-71): merged at a commit, then that commit's target run; still queued at the pinned head, waiting; failed or
    /// removed from the queue unmerged, a known failure that a retry reconciles; anything else stays uncertain and is never retried.
    /// </summary>
    private ChangeResponse ObserveQueuedMerge(ChangeClaim claim)
    {
        string number = claim.Body["target"]!["pullRequest"]!.GetValue<string>();
        bool confirmed;
        lock (gate) confirmed = QueuedConfirmed(session.Load(), claim);
        MergeQueueObservation? queue = null;
        PullRequestObservation? pull = ObserveChange(claim, "pull-request", observation =>
        {
            PullRequestObservation? value = claim.Configuration.Provider.Observe(claim.Configuration.Destination, number);
            if (value is not null)
            {
                StorePullObservation(observation, value);
                queue = claim.Configuration.Provider.ObserveQueue(claim.Configuration.Destination, number);
                StoreQueueObservation(observation, queue);
            }
            return value;
        }, out RecordWritten observation);
        string key = OperationKey(EstateServiceValues.Element(claim.Body));
        bool complete = pull is { Complete: true } && WholeQueue(queue) && pull.Number == number && pull.Target == claim.Configuration.Target;
        if (complete && pull!.State == "merged" && pull.Head == claim.Head && pull.MergeCommit is not null && Patterns.Commit.IsMatch(pull.MergeCommit))
        {
            ExpectPresence(claim.By, claim.Status is "not-started" or "known-failure" or "canceled"
                ? [OperationSignature("change-merge", "uncertain", key), OperationSignature("change-merge", "observed-success", key)] : [OperationSignature("change-merge", "observed-success", key)],
                "the merge queue merged the change, so its outcome is recorded");
            if (claim.Status is "not-started" or "known-failure" or "canceled")
            {
                claim.Body["dispatchedAt"] ??= Now();
                claim.Body["reconciliation"] = "The queued merge was subsequently observed under its original identity.";
                FinishChange(claim, "uncertain", "Reconciliation of the earlier unconfirmed effect.");
            }
            claim.Body["target"]!["mergeCommit"] = pull.MergeCommit;
            FinishChange(claim, "observed-success", "The merge queue merged the pinned head; the target branch moved to the commit its group's run tested.");
            ExpectPresence(claim.By, ObservationPresence(key, "target-run"), "the merge queue merged the change, so the target branch's run at the merge commit is observed");
            return ObserveChangeTarget(claim, pull.MergeCommit);
        }
        bool open = complete && pull!.State == "open" && pull.Head == claim.Head;
        if (open && queue!.State == "queued" && queue.Head == claim.Head)
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
        // The queue settled the change without merging it: still listed as unmergeable at the pinned head, or gone after its place
        // there was confirmed.
        bool unmergeable = open && queue!.State == "failed" && queue.Head == claim.Head;
        bool left = complete && confirmed && pull!.State != "merged";
        if ((unmergeable || left) && claim.Status is "dispatched" or "uncertain")
        {
            bool failed = queue!.State == "failed";
            string because = QueueReason(queue.Reason) is string reason ? " (" + reason + ")." : ".";
            ExpectPresence(claim.By, [OperationSignature("change-merge", "known-failure", key)], "the merge queue settled the change without merging it, so its known failure is recorded");
            FinishChange(claim, "known-failure", failed
                ? "The merge queue found the change could not merge and took it out unmerged" + because
                : "The change left the merge queue unmerged" + because);
            return ChangeAnswer(claim, false, (failed ? "The merge queue failed the change; " : "The change was removed from the merge queue; ")
                + "nothing was merged. Resolve the cause, then retry the merge with --reconciliation.") with { Observation = observation };
        }
        if (claim.Status == "dispatched")
        {
            ExpectPresence(claim.By, [OperationSignature("change-merge", "uncertain", key)], "the change's place in the merge queue was not confirmed, so the merge is recorded as uncertain");
            FinishChange(claim, "uncertain", "The enqueue was requested but its place in the merge queue was not confirmed; observe it before any retry.");
        }
        return ChangeAnswer(claim, false, "The queued merge remains unconfirmed; do not retry.") with { Observation = observation, CheckStatus = "uncertain" };
    }

    /// <summary>
    /// Whether this attempt of a queued merge was seen in the merge queue at its pinned head: a recorded observation of its pull request,
    /// since the attempt began after the merge chain's latest known failure or cancellation, that found it queued there. Caller holds the gate.
    /// </summary>
    private static bool QueuedConfirmed(EstateModel model, ChangeClaim claim)
    {
        if (!model.ById.TryGetValue(claim.Written.Id, out RecordEnvelope? current)) return false;
        IReadOnlyList<string> chain = model.Chain(current);
        int start = 0;
        for (int i = 0; i < chain.Count; i++)
            if (model.ById.TryGetValue(chain[i], out RecordEnvelope? revision) && EstateModel.BodyString(revision, "status") is "known-failure" or "canceled") start = i + 1;
        HashSet<string> attempt = [.. chain.Skip(start)];
        return model.Current("operation").Any(o => EstateModel.BodyString(o, "action") == "change-observe" && EstateModel.BodyString(o, "status") == "observed-success"
            && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "observation") == "pull-request"
            && attempt.Contains(ChangeText(t, "operation") ?? string.Empty) && ChangeText(t, "queue") == "queued" && ChangeText(t, "queueHead") == claim.Head);
    }

    // The merge queue's facts beside a pull-request observation (CODE-71): whether the target branch requires it, the pull request's
    // place in it, the head its entry holds and the host's removal reason, each a target fact of that observation.
    private static void StoreQueueObservation(ChangeClaim claim, MergeQueueObservation queue)
    {
        JsonObject target = claim.Body["target"]!.AsObject();
        target["mergeQueue"] = queue.Required ? "required" : "none";
        target["queue"] = MergeQueueObservation.States.Contains(queue.State) ? queue.State : "unknown";
        if (queue.Head is not null && Patterns.Commit.IsMatch(queue.Head)) target["queueHead"] = queue.Head;
        if (QueueReason(queue.Reason) is string reason) target["queueReason"] = reason;
        if (!queue.Complete || queue.Head is not null && !Patterns.Commit.IsMatch(queue.Head)) target["complete"] = "false";
    }

    // A complete queue observation in a known state, whose entry, when it has one, holds an exact commit.
    private static bool WholeQueue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] MergeQueueObservation? queue) => queue is { Complete: true }
        && MergeQueueObservation.States.Contains(queue.State) && (queue.Head is null || Patterns.Commit.IsMatch(queue.Head));

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
        using IDisposable counted = presence.Action("changes observe", PresencePlan(request.By, () =>
        {
            RecordEnvelope? named = Ulid.IsValid(request.Operation) && session.Load().ById.TryGetValue(request.Operation, out RecordEnvelope? found) ? session.Load().Latest(found) : null;
            return named is null || !ChangeOperationRulesForRuntime(named) ? [] : request.Cancel is null ? ObservePresence(named)
                : [OperationSignature(EstateModel.BodyString(named, "action"), "canceled", OperationKey(named.Body))];
        }));
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
        // A queued merge's commit is the one its group's run tested: that run and the branch's push runs of the commit both count (CODE-71).
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
            observation.Body["target"]!["complete"] = value.Complete ? "true" : "false";
            return value;
        }, out RecordWritten recorded);
        bool exact = target.Head == commit && target.Branch == claim.Configuration.Target && target.Complete;
        bool passed = exact && (queued ? ChangeGate.QueuedRefusals(commit, claim.Configuration.Required, target.Checks, true)
            : ChangeGate.Refusals(commit, claim.Configuration.Required, target.Checks, true)).Count == 0;
        bool failed = exact && target.Checks.Any(c => c.Head == commit && c.Status == "completed"
            && c.Conclusion is "failure" or "cancelled" or "timed_out" or "action_required" or "stale" or "startup_failure");
        bool unknown = !exact || target.Checks.Any(c => c.Head != commit || c.Status == "unknown" || c.Conclusion == "unknown");
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
