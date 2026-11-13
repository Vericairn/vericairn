// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Changes;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private bool operatorRecordRunning;
    private const string OperatorActive = "operator-record:active";
    // The action last finished from its own records, whose receipts may still be uncommitted; the next new action tolerates them.
    private const string OperatorFinished = "operator-record:finished";

    // A local recovery checkpoint contains identities and hashes only, never a key or the operator's input body.
    // Records and MERGE-01's operations establish what happened; this checkpoint never establishes authority or a successful effect.
    // Type, Subject and By name the action in a refusal; Follows names the finished action whose receipts this one tolerates; Carried
    // pins, by path and byte hash, the settled trail records of earlier actions its commit carries (FIX-126).
    private sealed record OperatorCheckpoint(string Product, string ProductHash, string RepositoryHash, string Id, string At,
        RecordCommitBasis Basis, string Branch, string Hash, string? Head = null, ChangeResponse? Completed = null,
        string? Type = null, string? Subject = null, string? By = null, string? Follows = null, IReadOnlyDictionary<string, string>? Carried = null)
    {
        internal IReadOnlyDictionary<string, string> CarriedFiles => Carried ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // How far an action went, from its checkpoint and its records: the signed record, the pull request and, once merged, its target run.
    private sealed record OperatorProgress(string Identity, OperatorCheckpoint Checkpoint, RecordEnvelope? Record, string Stage,
        string? PullRequest = null, RecordEnvelope? Merge = null, RecordEnvelope? Target = null, string? CheckStatus = null, RecordEnvelope? Ask = null)
    {
        internal string? Type => Record?.Type ?? Checkpoint.Type;
        internal string? Subject => Record?.Subject is RecordReference subject ? subject.Type + ":" + subject.Id + (subject.Hash is null ? string.Empty : ":" + subject.Hash) : Checkpoint.Subject;
        internal string? By => Record?.By ?? Checkpoint.By;
        internal string? MergeCommit => Merge is null ? null : ChangeText(Merge.Body.GetProperty("target"), "mergeCommit");
        internal bool Finishable => Stage == "merged" && Record is not null;
    }

    /// <inheritdoc />
    public OperatorRecordResponse OperatorRecord(OperatorRecordRequest request) => OperatorRecord(request, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>The token also reaches the action itself, which stops at its next recorded step, before any merge; the same input resumes it.</remarks>
    public Task<OperatorRecordResponse> OperatorRecordAsync(OperatorRecordRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRecord), () => OperatorRecord(request, cancellationToken), true, this, cancellationToken);

    /// <summary>Records and carries the operator's record; cancellation stops it at its next recorded step, before any merge, and the same input resumes it.</summary>
    public OperatorRecordResponse OperatorRecord(OperatorRecordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string identity = "operator-record";
        string resume = OperatorResume(request);
        OperatorCheckpoint? checkpoint = null;
        RecordWritten? written = null;
        ChangeResponse? change = null;
        string? active = null;
        string stage = "prepare";
        lock (gate)
        {
            if (operatorRecordRunning) return new(identity, "busy", "Another operator record command is in progress.", false, "Wait for it to stop, then " + resume);
            operatorRecordRunning = true;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChangeConfiguration configuration;
            IRecordCommitAdapter adapter = Providers.RecordCommitAdapter ?? throw new ProviderRefusedException("No local record commit adapter is registered.");
            AuthoredRecord? authored = null;
            lock (gate)
            {
                RequireOperatorRecord(request.By);
                if (request.WaitSeconds is < 0 or > 240) throw new ProviderRefusedException("Wait seconds must be between 0 and 240.");
                if (request.Body.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(request.Type) || RecordTypeRegistry.Find(request.Type) is null)
                    throw new ProviderRefusedException("Name a known record type and supply its JSON object body.");
                RecordReference? subject = null;
                if (request.Subject is not null && (!TryParseSubject(request.Subject, out subject) || RecordTypeRegistry.Find(subject!.Type) is null))
                    throw new ProviderRefusedException("The subject must be type:id or type:id:hash.");
                if (request.Supersedes is not null && !Ulid.IsValid(request.Supersedes))
                    throw new ProviderRefusedException("The superseded record id must be a ULID.");
                // In an estate that has adopted history checkpoints, a revocation, a key rotation or a narrowing grant revision is written
                // with its closing in the same batch, which this path, one signed record carried through a checked merge, cannot carry (OPS-07).
                if (ClosingRequired(session.Load(), request.Type, request.Body, subject, request.Supersedes) is { } closes)
                    return new(identity, "refused", OperatorRecordClosingRefusal(closes), false, ClosingCommand(closes, request.By));
                configuration = OperatorConfiguration(request.Product);
                if (configuration.Required.Count == 0) throw new ProviderRefusedException("The estate product declares no required checks; declare them before recording an action.");
                if (Providers.PushAdapter is null || Providers.SecretScanner is null)
                    throw new ProviderRefusedException("The checked push adapter and secret scanner must be registered before recording an action.");
                identity += ":" + ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new
                {
                    request.Type, request.By, request.Body, request.Subject, request.Supersedes, Product = configuration.Product.Id,
                })))[7..];
                string? saved = session.Live.Meta(identity);
                checkpoint = saved is null ? null : JsonSerializer.Deserialize<OperatorCheckpoint>(saved, ProtocolJson.Options);
                active = session.Live.Meta(OperatorActive);
            }

            // Every record the action signs with presence is announced before the first prompt, from where the action stands; an
            // observation it repeats while checks run is announced before it asks (FIX-124).
            OperatorCheckpoint? planned = checkpoint;
            string? earlierAction = active;
            using IDisposable counted = presence.Action("operator record " + request.Type,
                PresencePlan(request.By, () => OperatorRecordPresence(request, identity, planned, earlierAction, configuration)));

            // An earlier action whose merge is recorded is finished from its own records first, when its target checks pass.
            // Any other earlier action is truly unfinished and still refuses a new one.
            if (!string.IsNullOrEmpty(active) && active != identity)
            {
                OperatorCheckpoint? earlier;
                lock (gate) earlier = OperatorCheckpointLocked(active);
                if (earlier is null) throw new ProviderRefusedException("An earlier operator action is unfinished and its local checkpoint is unavailable. Rerun its original type, author, subject and JSON body before starting another.");
                OperatorRecordResponse finished = FinishMerged(active, earlier, request.By, request.WaitSeconds, cancellationToken);
                if (!finished.Ok)
                    return new(identity, "refused", "An earlier operator action is unfinished: " + finished.Message, false, finished.NextStep, Change: finished.Change);
            }

            bool merged;
            lock (gate)
            {
                RequireOperatorRecord(request.By);
                RecordReference? subject = null;
                if (request.Subject is not null && !TryParseSubject(request.Subject, out subject))
                    throw new ProviderRefusedException("The subject must be type:id or type:id:hash.");
                if (checkpoint is null)
                {
                    // In an estate that has adopted, a role removal is refused from the unsigned intent, so nothing is signed for it.
                    if (OperatorRecordRemovalRefusal(new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes), session.Load(), null) is { } early)
                        return new(identity, "refused", early.Message, false, early.NextStep, Problem: early.Problem);
                    string? follows = session.Live.Meta(OperatorFinished);
                    OperatorCheckpoint? followed = string.IsNullOrEmpty(follows) ? null : OperatorCheckpointLocked(follows);
                    // The settled push and merge trails of earlier actions that this clone still holds untracked travel in this action's
                    // commit (FIX-126); the earlier actions' own records are not touched.
                    Dictionary<string, string> carried = UncommittedTrailLocked(adapter);
                    HashSet<string> tolerated = followed is null ? new(StringComparer.Ordinal) : OperatorReceiptsLocked(followed);
                    tolerated.UnionWith(carried.Keys);
                    RecordCommitBasis basis = adapter.Prepare(Root, tolerated);
                    string id = Ulid.NewUlid().Value;
                    string at = Now();
                    var draft = new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes, ParseAt(at), id);
                    EstateModel model = session.Load();
                    (authored, string? refusal) = AuthorLocked(draft, model);
                    if (authored is null) throw new ProviderRefusedException(refusal!);
                    // A revision taking a role away is written after a history checkpoint, which this path cannot carry, or is refused where
                    // it would make records fail (design/signed-history.md § 8).
                    if (OperatorRecordRemovalRefusal(draft, model, authored) is { } removal)
                        return new(identity, "refused", removal.Message, false, removal.NextStep, Problem: removal.Problem);
                    if (JudgeProposedLocked([authored]) is Problem rejected)
                        return new(identity, "refused", "The proposed signed record did not pass validation.", false, "Correct the JSON body and " + resume, Problem: rejected);
                    checkpoint = new(configuration.Product.Id, configuration.Product.ComputedHash, configuration.Repository.ComputedHash,
                        id, at, basis, "operator/" + id, authored.Envelope.ComputedHash, Type: request.Type, Subject: request.Subject, By: request.By,
                        Follows: followed is null ? null : follows, Carried: carried.Count == 0 ? null : carried);
                    SaveOperatorCheckpoint(identity, checkpoint);
                    session.Live.SetMeta(OperatorActive, identity);
                    if (followed is not null) session.Live.SetMeta(OperatorFinished, string.Empty);
                }
                if (session.Load().ById.TryGetValue(checkpoint.Id, out RecordEnvelope? existing))
                {
                    if (existing.ComputedHash != checkpoint.Hash || existing.By != request.By || existing.Type != request.Type || existing.Signatures.Count == 0
                        || existing.Subject != subject || existing.Supersedes != request.Supersedes
                        || CanonicalJson.Serialize(existing.Body) != CanonicalJson.Serialize(request.Body))
                        throw new ProviderRefusedException("The original signed record no longer matches this action; inspect it before continuing.");
                    written = new(existing.ExpectedPath, existing.Id, existing.ComputedHash);
                }
                if (checkpoint.Completed is ChangeResponse completed)
                {
                    if (written is null || !OperatorCompletionRecorded(checkpoint, completed, ChangeConfigurationFor(checkpoint.Product, currentDeclaration: false)))
                        throw new ProviderRefusedException("The completed action's recorded evidence is unavailable; restore it before resuming.");
                    adapter.Verify(Root, checkpoint.Head!, checkpoint.Basis.Head, OperatorRecordFiles(written), checkpoint.CarriedFiles);
                    if (session.Live.Meta(OperatorActive) == identity) session.Live.SetMeta(OperatorActive, string.Empty);
                    return Answer("completed", completed.Message, true, CompletedStep(checkpoint), completed);
                }
                // Once its merge is recorded, an action is finished from its records alone: no signature, branch, commit, push, opening or
                // merge again, whatever branch the clone is on now.
                merged = checkpoint.Head is not null && OperatorProgressLocked(identity, checkpoint).Stage == "merged";
                if (!merged && (checkpoint.ProductHash != configuration.Product.ComputedHash || checkpoint.RepositoryHash != configuration.Repository.ComputedHash))
                    throw new ProviderRefusedException("The recorded product or repository declaration changed; reconcile the original action before continuing.");
            }
            if (merged) return FinishMerged(identity, checkpoint, request.By, request.WaitSeconds, cancellationToken);

            stage = "record";
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireOperatorRecord(request.By);
                if (written is null)
                {
                    adapter.Check(Root, checkpoint.Basis.Branch, checkpoint.Basis.Head, Receipts());
                    // A resumed action is judged again: the estate may have adopted since it stopped, and that refusal needs no signature.
                    if (authored is null && OperatorRecordRemovalRefusal(new RecordDraft(request.Type, request.By, request.Body, null, request.Supersedes), session.Load(), null) is { } before)
                    {
                        if (session.Live.Meta(OperatorActive) == identity) session.Live.SetMeta(OperatorActive, string.Empty);
                        if (checkpoint.Follows is { } follows) session.Live.SetMeta(OperatorFinished, follows);
                        return Answer("refused", before.Message, false, before.NextStep) with { Problem = before.Problem };
                    }
                    // A crash before publication can require presence again, but never creates a second record identity.
                    if (authored is null)
                    {
                        RecordReference? subject = null;
                        if (request.Subject is not null && !TryParseSubject(request.Subject, out subject))
                            throw new ProviderRefusedException("The resumed subject is invalid.");
                        (authored, string? refusal) = AuthorLocked(new(request.Type, request.By, request.Body, subject,
                            request.Supersedes, ParseAt(checkpoint.At), checkpoint.Id), session.Load());
                        if (authored is null) throw new ProviderRefusedException(refusal!);
                    }
                    if (authored.Envelope.ComputedHash != checkpoint.Hash) throw new ProviderRefusedException("The resumed record differs from the original intent.");
                    // The removal is judged again at the record stage, against what arrived since the prepare step. Nothing is stored,
                    // branched or committed yet, so a refused action stops being this clone's unfinished one and the finished action it
                    // followed is tolerated again.
                    if (OperatorRecordRemovalRefusal(new RecordDraft(request.Type, request.By, request.Body, authored.Envelope.Subject, request.Supersedes), session.Load(), authored) is { } late)
                    {
                        if (session.Live.Meta(OperatorActive) == identity) session.Live.SetMeta(OperatorActive, string.Empty);
                        if (checkpoint.Follows is { } follows) session.Live.SetMeta(OperatorFinished, follows);
                        return Answer("refused", late.Message, false, late.NextStep) with { Problem = late.Problem };
                    }
                    (IReadOnlyList<RecordWritten> stored, Problem? problem) = StoreTogetherLocked([authored],
                        "Store the operator record and its generated register members.", null);
                    written = stored.SingleOrDefault();
                    if (problem is not null)
                        return Answer("refused", problem.Message, false, "Resolve the record or generated output findings and " + resume) with { Problem = problem };
                }
            }

            IReadOnlyDictionary<string, string> files;
            lock (gate) files = OperatorRecordFiles(written!);
            // Retain the signed bytes and journaled members before a fallible branch step.
            // A branch failure can then resume from those bytes without another signing prompt.
            stage = "branch";
            cancellationToken.ThrowIfCancellationRequested();
            adapter.Branch(Root, checkpoint.Basis, checkpoint.Branch, files, Receipts());
            stage = "commit";
            string head = adapter.Commit(Root, checkpoint.Branch, checkpoint.Basis.Head, files, Receipts(), checkpoint.CarriedFiles);
            if (checkpoint.Head is not null && checkpoint.Head != head) throw new ProviderRefusedException("The recorded commit changed; reconcile it before continuing.");
            checkpoint = checkpoint with { Head = head };
            lock (gate) SaveOperatorCheckpoint(identity, checkpoint);

            stage = "push";
            Guard();
            change = Step("change-push", checkpoint.Branch, reconciliation => ChangesPush(new(checkpoint.Product, checkpoint.Branch, head, request.By, reconciliation)));
            if (!change.Ok) return Stopped(change);
            stage = "open";
            Guard();
            change = Step("change-open", checkpoint.Branch, reconciliation => ChangesOpen(new(checkpoint.Product, checkpoint.Branch, head,
                "Record operator " + request.Type, "Signed record " + written!.Id + " at " + written.Hash + ".", request.By, reconciliation)));
            if (!change.Ok || change.PullRequest is null) return Stopped(change);
            string pullRequest = change.PullRequest;
            RecordWritten opening = change.Operation!;

            // A confirmed merge is only observed again. Neither waiting nor a rerun can send that mutation a second time.
            RecordEnvelope? priorMerge = Prior("change-merge", pullRequest);
            // A queued merge settled unmerged is observed again before any retry: the queue may have merged it since (CODE-71).
            bool queuedAgain = false;
            if (priorMerge is not null && EstateModel.BodyString(priorMerge, "status") is "known-failure" or "canceled" && QueuedMerge(priorMerge))
            {
                stage = "merge";
                Guard();
                change = ChangesObserve(new(priorMerge.Id, request.By));
                queuedAgain = change.Status == "observed-success" || change.CheckStatus == "queued";
            }
            if (queuedAgain)
            {
                // The queued merge observed again is merged or back in the queue: nothing is checked or merged again.
            }
            else if (priorMerge is not null && EstateModel.BodyString(priorMerge, "status") is not ("known-failure" or "canceled"))
            {
                stage = "merge";
                Guard();
                change = ChangesObserve(new(priorMerge.Id, request.By));
                if (change.Status != "observed-success" && change.CheckStatus != "queued" && !QueuedUnsettled(change)) return Stopped(change);
            }
            else
            {
                stage = "checks";
                var watch = Stopwatch.StartNew();
                bool again = false;
                while (true)
                {
                    Guard();
                    if (again) ExpectPresence(request.By, ObservationPresence("pull request " + pullRequest, "pull-request"), "the pull request's checks are still running, so it is observed again");
                    again = true;
                    change = ChangesObserve(new(opening.Id, request.By));
                    if (!change.Ok || change.Observation is null) return Stopped(change);
                    string verdict = PullChecks(change.Observation, configuration, checkpoint.Branch, pullRequest, head, out string reason);
                    if (verdict == "passed") break;
                    if (verdict != "pending") return Answer("stopped", reason, false, "Inspect the recorded checks, resolve their failures and " + resume, change);
                    if (!Pause(watch)) return Answer("waiting", reason, false, "Wait for the checks, then " + resume, change);
                }
                stage = "merge";
                Guard();
                change = Step("change-merge", pullRequest, reconciliation => ChangesMerge(new(checkpoint.Product, pullRequest, head, request.By, reconciliation)));
            }
            // Where the target branch requires a merge queue, the merge waits there; its place is observed again until the queue settles it,
            // as is a queued merge whose outcome is not yet confirmed, such as one just out of the queue whose pull request does not yet
            // read merged (CODE-71).
            var queueWatch = Stopwatch.StartNew();
            while (!change.Ok && (change.CheckStatus == "queued" || QueuedUnsettled(change)) && change.Operation is not null)
            {
                if (!Pause(queueWatch))
                    return change.CheckStatus == "queued" ? Answer("waiting", change.Message, false, "Wait for the merge queue, then " + resume, change) : Stopped(change);
                Guard();
                ExpectPresence(request.By, ObservationPresence("pull request " + pullRequest, "pull-request"), "the change is still settling in the merge queue, so it is observed again");
                change = ChangesObserve(new(change.Operation.Id, request.By));
            }
            stage = "target-checks";
            var targetWatch = Stopwatch.StartNew();
            while (!change.Ok && change.Status == "observed-success" && change.CheckStatus == "pending")
            {
                if (!Pause(targetWatch)) return Stopped(change);
                Guard();
                ExpectPresence(request.By, ObservationPresence("pull request " + pullRequest, "target-run"), "the target branch's run at the merge commit is still running, so it is observed again");
                change = ChangesObserve(new(change.Operation!.Id, request.By));
            }
            if (!change.Ok || change.CheckStatus != "passed") return Stopped(change);
            lock (gate)
            {
                SaveOperatorCheckpoint(identity, checkpoint with { Completed = change });
                session.Live.SetMeta(OperatorActive, string.Empty);
            }
            return Answer("completed", change.Message, true, CompletedStep(checkpoint), change);

            bool Pause(Stopwatch watch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan left = TimeSpan.FromSeconds(request.WaitSeconds) - watch.Elapsed;
                if (left <= TimeSpan.Zero) return false;
                if (cancellationToken.WaitHandle.WaitOne(left < TimeSpan.FromSeconds(2) ? left : TimeSpan.FromSeconds(2)))
                    cancellationToken.ThrowIfCancellationRequested();
                return true;
            }

            void Guard()
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    RequireOperatorRecord(request.By);
                    ChangeConfiguration current = OperatorConfiguration(checkpoint.Product);
                    if (current.Product.ComputedHash != checkpoint.ProductHash || current.Repository.ComputedHash != checkpoint.RepositoryHash)
                        throw new ProviderRefusedException("The product or repository declaration changed; reconcile the original action before continuing.");
                    adapter.Check(Root, checkpoint.Branch, head, Receipts());
                }
            }

            RecordEnvelope? Prior(string action, string name)
            {
                lock (gate) return CurrentOperation($"{configuration.Provider.Provider}:{configuration.Repository.Id}:{action}:{name}:{head}:{checkpoint.ProductHash[7..]}");
            }

            ChangeResponse Step(string action, string name, Func<string?, ChangeResponse> execute)
            {
                RecordEnvelope? prior = Prior(action, name);
                if (prior is null) return execute(null);
                string? status = EstateModel.BodyString(prior, "status");
                JsonElement target = prior.Body.GetProperty("target");
                if (status == "observed-success") return new(EstateModel.BodyString(prior, "identity")!, status,
                    "The original effect is already recorded.", true, new(prior.ExpectedPath, prior.Id, prior.ComputedHash),
                    PullRequest: ChangeText(target, "pullRequest"), Head: head, MergeCommit: ChangeText(target, "mergeCommit"));
                if (status is "known-failure" or "canceled")
                    return string.IsNullOrWhiteSpace(request.Reconciliation) ? ChangePriorRefusal(prior, "The earlier failure needs an explicit reconciliation before retrying.") : execute(request.Reconciliation);
                return ChangesObserve(new(prior.Id, request.By));
            }
        }
        catch (OperationCanceledException)
        {
            return Answer("stopped", "The request was interrupted after its current step; no further effect was requested.", false,
                "Wait for the current operation to settle, then " + resume, change);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException or KeyNotFoundException or System.Data.Common.DbException or System.Security.Cryptography.CryptographicException)
        {
            string message = exception is ProviderRefusedException ? exception.Message : "The " + stage + " step could not be completed; the original action is retained.";
            return Answer("stopped", message, false, "Resolve this failure and " + resume, change);
        }
        finally { lock (gate) operatorRecordRunning = false; }

        OperatorRecordResponse Answer(string status, string message, bool ok, string next, ChangeResponse? latest = null) =>
            new(identity, status, message, ok, next, written, checkpoint?.Branch, checkpoint?.Head, latest);

        OperatorRecordResponse Stopped(ChangeResponse latest)
        {
            string next = latest.Operation is null ? "Resolve the refusal and " + resume
                : latest.Status is "known-failure" or "canceled" && QueuedMergeOperation(latest.Operation)
                    ? $"Observe the queued merge with vericairn changes observe --operation {latest.Operation.Id} --by {request.By}, which records it if the queue merged it since; otherwise resolve the known failure, then {resume} --reconciliation <explanation>"
                : latest.Status is "known-failure" or "canceled" ? "Resolve the known failure, then " + resume + " --reconciliation <explanation>"
                : $"vericairn changes observe --operation {latest.Operation.Id} --by {request.By}; after reconciling the original effect, {resume}";
            return Answer("stopped", latest.Message, false, next, latest);
        }

        // A queued merge whose outcome is still unconfirmed: recorded uncertain, not yet failed or merged.
        bool QueuedUnsettled(ChangeResponse latest) => latest.Status == "uncertain" && latest.Operation is not null && QueuedMergeOperation(latest.Operation);

        bool QueuedMergeOperation(RecordWritten operation)
        {
            lock (gate) return session.Load().ById.TryGetValue(operation.Id, out RecordEnvelope? record) && EstateModel.BodyString(record, "action") == "change-merge" && QueuedMerge(record);
        }

        // This action's own receipts, those of the finished action it follows, whose receipts may still be uncommitted, and the earlier
        // trail records it carries, untracked until its commit holds them.
        HashSet<string> Receipts()
        {
            lock (gate)
            {
                HashSet<string> receipts = OperatorReceiptsLocked(checkpoint!);
                if (checkpoint!.Follows is string follows && OperatorCheckpointLocked(follows) is OperatorCheckpoint followed)
                    receipts.UnionWith(OperatorReceiptsLocked(followed));
                receipts.UnionWith(checkpoint.CarriedFiles.Keys);
                return receipts;
            }
        }
    }

    /// <summary>
    /// What an operator record signs with presence from where it stands (FIX-124): finishing an earlier merged action's target run, the
    /// record itself unless it is stored, the push, the opening, the first observation of the pull request's checks, and the merge with
    /// the first observation of the target run, each step left out once its effect is recorded. Caller holds the gate.
    /// </summary>
    private List<PresenceSignature> OperatorRecordPresence(OperatorRecordRequest request, string identity, OperatorCheckpoint? checkpoint, string? active, ChangeConfiguration configuration)
    {
        List<PresenceSignature> plan = [];
        EstateModel model = session.Load();
        if (!string.IsNullOrEmpty(active) && active != identity && OperatorCheckpointLocked(active) is { } earlier)
        {
            OperatorProgress before = OperatorProgressLocked(active, earlier);
            if (before.Finishable && before.CheckStatus != "passed") plan.AddRange(ObservationPresence("pull request " + before.PullRequest, "target-run"));
        }

        string branch = checkpoint is null ? "the action's new branch" : "branch " + checkpoint.Branch;
        OperatorProgress? progress = checkpoint is null ? null : OperatorProgressLocked(identity, checkpoint);
        string stage = progress?.Stage ?? "prepared";
        string pull = progress?.PullRequest is string number ? "pull request " + number : "the pull request of " + branch;
        if (stage == "prepared")
        {
            RecordReference? subject = request.Subject is not null && TryParseSubject(request.Subject, out RecordReference? parsed) ? parsed : null;
            plan.Add(DescribeRecord(new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes), model));
        }

        if (stage == "merged")
        {
            if (progress!.CheckStatus != "passed") plan.AddRange(ObservationPresence(pull, "target-run"));
            return plan;
        }

        if (stage is "prepared" or "recorded" or "committed") plan.AddRange(Step("change-push") ?? PushPresence(branch));
        if (stage is "prepared" or "recorded" or "committed" or "pushed") plan.AddRange(Step("change-open") ?? OpenPresence(branch));
        // A queued merge settled unmerged is observed once more before the pull request's checks, since the queue may have merged it (CODE-71).
        if (stage == "merge-failed" && progress?.Merge is { } settled && QueuedMerge(settled)) plan.AddRange(ObservationPresence(pull, "pull-request"));
        plan.AddRange(ObservationPresence(pull, "pull-request"));
        if (stage is "merge-uncertain" or "merge-queued" || stage == "merge-failed" && string.IsNullOrWhiteSpace(request.Reconciliation)) return plan;
        plan.AddRange(MergePresence(pull));
        return plan;

        // A step whose operation is recorded signs nothing once it succeeded or while its failure waits for a reconciliation, one
        // observation while its outcome is unconfirmed, and the whole step when it is retried; null when none is recorded.
        PresenceSignature[]? Step(string action)
        {
            if (checkpoint?.Head is not string head) return null;
            RecordEnvelope? prior = CurrentOperation($"{configuration.Provider.Provider}:{configuration.Repository.Id}:{action}:{checkpoint.Branch}:{head}:{checkpoint.ProductHash[7..]}");
            if (prior is null) return null;
            return EstateModel.BodyString(prior, "status") switch
            {
                "observed-success" => [],
                "known-failure" or "canceled" => string.IsNullOrWhiteSpace(request.Reconciliation) ? [] : null,
                _ => ObservePresence(prior, branch),
            };
        }
    }

    /// <inheritdoc />
    public OperatorUnfinishedResponse OperatorUnfinished()
    {
        lock (gate)
        {
            string? active = session.Live.Meta(OperatorActive);
            if (string.IsNullOrEmpty(active)) return new(null);
            if (OperatorCheckpointLocked(active) is not OperatorCheckpoint checkpoint)
                return new(null, new("unavailable", "This clone marks an operator action unfinished, but its local checkpoint is unavailable.", []));
            OperatorProgress progress = OperatorProgressLocked(active, checkpoint);
            return new(new(active, checkpoint.Id, progress.Type, progress.Subject, progress.By,
                progress.Record is null ? null : new(progress.Record.ExpectedPath, progress.Record.Id, progress.Record.ComputedHash),
                checkpoint.Branch, checkpoint.Head, progress.Stage, progress.PullRequest, Written(progress.Merge), progress.MergeCommit,
                progress.CheckStatus, Written(progress.Target), Written(progress.Ask), progress.Finishable, OperatorDescription(progress), OperatorNextStep(progress)));
        }

        static RecordWritten? Written(RecordEnvelope? record) => record is null ? null : new(record.ExpectedPath, record.Id, record.ComputedHash);
    }

    /// <inheritdoc />
    public OperatorRecordResponse OperatorFinish(OperatorFinishRequest request) => OperatorFinish(request, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>The token also reaches the observation, which stops at its next recorded step; the same request resumes it.</remarks>
    public Task<OperatorRecordResponse> OperatorFinishAsync(OperatorFinishRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorFinish), () => OperatorFinish(request, cancellationToken), true, this, cancellationToken);

    /// <summary>Finishes this clone's unfinished operator action from its own records once its recorded merge's target checks pass.</summary>
    public OperatorRecordResponse OperatorFinish(OperatorFinishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string identity = "operator-record";
        lock (gate)
        {
            if (operatorRecordRunning) return new(identity, "busy", "Another operator record command is in progress.", false, "Wait for it to stop, then finish again.");
            operatorRecordRunning = true;
        }
        try
        {
            string? active;
            OperatorCheckpoint? checkpoint;
            lock (gate)
            {
                RequireOperatorRecord(request.By);
                if (request.WaitSeconds is < 0 or > 240) throw new ProviderRefusedException("Wait seconds must be between 0 and 240.");
                active = session.Live.Meta(OperatorActive);
                if (string.IsNullOrEmpty(active)) return new(identity, "none", "This clone has no unfinished operator action.", true, "Nothing waits to be finished.");
                checkpoint = OperatorCheckpointLocked(active) ?? throw new ProviderRefusedException("The unfinished action's local checkpoint is unavailable; rerun its original operator record command.");
            }
            return FinishMerged(active, checkpoint, request.By, request.WaitSeconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new(identity, "stopped", "The request was interrupted after its current step; no further effect was requested.", false,
                "Wait for the current observation to settle, then finish again.");
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException or KeyNotFoundException or System.Data.Common.DbException or System.Security.Cryptography.CryptographicException)
        {
            return new(identity, "stopped", exception is ProviderRefusedException ? exception.Message : "The action could not be finished; it is retained as it was.", false,
                "Resolve this failure, then finish again.");
        }
        finally { lock (gate) operatorRecordRunning = false; }
    }

    // Finishes an action whose merge is recorded from its own records: a passing target run at the merge commit, already recorded or
    // observed now under the merge's original identity, completes it. Nothing is branched, committed, pushed, opened or merged, and the
    // clone's branch and files stay as they are. Any other action is truly unfinished and is described with what finishing it needs.
    private OperatorRecordResponse FinishMerged(string identity, OperatorCheckpoint checkpoint, string by, int waitSeconds, CancellationToken cancellationToken)
    {
        OperatorProgress progress;
        ChangeConfiguration configuration;
        lock (gate)
        {
            RequireOperatorRecord(by);
            progress = OperatorProgressLocked(identity, checkpoint);
            if (!progress.Finishable) return Answer(false, "refused", OperatorDescription(progress) + ".", OperatorNextStep(progress));
            if (progress.By != by)
                return Answer(false, "refused", OperatorDescription(progress) + $"; it is {progress.By}'s action, and only that operator finishes it.", OperatorNextStep(progress));
            configuration = ChangeConfigurationFor(checkpoint.Product, currentDeclaration: false);
            if (configuration.Product.ComputedHash != checkpoint.ProductHash || configuration.Repository.ComputedHash != checkpoint.RepositoryHash)
                return Answer(false, "refused", OperatorDescription(progress) + "; its recorded product or repository declaration is unavailable.", "Restore the declaration it was merged under, then finish it again.");
        }
        RecordEnvelope merge = progress.Merge!;
        // Finishing observes the target run at the merge commit, once and again while it runs, each announced before it asks (FIX-124).
        string pull = "pull request " + progress.PullRequest;
        using IDisposable counted = presence.Action("operator unfinished --finish",
            progress.CheckStatus == "passed" ? null : PresencePlan(by, () => ObservationPresence(pull, "target-run")));
        ChangeResponse change = progress.CheckStatus == "passed"
            ? new(EstateModel.BodyString(merge, "identity")!, "observed-success", "The pinned merge and its target checks are recorded as successful.", true,
                new(merge.ExpectedPath, merge.Id, merge.ComputedHash), new(progress.Target!.ExpectedPath, progress.Target.Id, progress.Target.ComputedHash),
                PullRequest: progress.PullRequest, Head: checkpoint.Head, MergeCommit: progress.MergeCommit, CheckStatus: "passed")
            : ChangesObserve(new(merge.Id, by));
        var watch = Stopwatch.StartNew();
        while (!change.Ok && change.Status == "observed-success" && change.CheckStatus == "pending" && change.Operation is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan left = TimeSpan.FromSeconds(waitSeconds) - watch.Elapsed;
            if (left <= TimeSpan.Zero) break;
            if (cancellationToken.WaitHandle.WaitOne(left < TimeSpan.FromSeconds(2) ? left : TimeSpan.FromSeconds(2))) cancellationToken.ThrowIfCancellationRequested();
            ExpectPresence(by, ObservationPresence(pull, "target-run"), "the target branch's run at the merge commit is still running, so it is observed again");
            change = ChangesObserve(new(change.Operation.Id, by));
        }
        if (!change.Ok || change.CheckStatus != "passed")
        {
            lock (gate) progress = OperatorProgressLocked(identity, checkpoint);
            return Answer(false, change.CheckStatus == "pending" ? "waiting" : "stopped", OperatorDescription(progress) + ". " + change.Message, OperatorNextStep(progress), change);
        }
        lock (gate)
        {
            if (!OperatorCompletionRecorded(checkpoint, change, configuration))
                return Answer(false, "stopped", "The finishing evidence is incomplete; the action is retained as it was.", OperatorNextStep(progress), change);
            SaveOperatorCheckpoint(identity, checkpoint with { Completed = change });
            if (session.Live.Meta(OperatorActive) == identity) session.Live.SetMeta(OperatorActive, string.Empty);
            session.Live.SetMeta(OperatorFinished, identity);
        }
        return Answer(true, "completed", $"Finished from its records: {progress.Type} {checkpoint.Id}, merged in pull request {progress.PullRequest} at {progress.MergeCommit}, with its target checks passed.",
            "The signed record and checked merge are complete. Nothing was branched or committed; the clone stays on its current branch, and the next operator action carries this action's push and merge trail, still uncommitted here, into its own commit.", change);

        OperatorRecordResponse Answer(bool ok, string status, string message, string next, ChangeResponse? latest = null) =>
            new(identity, status, message, ok, next, progress.Record is null ? null : new(progress.Record.ExpectedPath, progress.Record.Id, progress.Record.ComputedHash),
                checkpoint.Branch, checkpoint.Head, latest);
    }

    private OperatorCheckpoint? OperatorCheckpointLocked(string identity) =>
        session.Live.Meta(identity) is string saved && saved.Length > 0 ? JsonSerializer.Deserialize<OperatorCheckpoint>(saved, ProtocolJson.Options) : null;

    // The stage an action reached, read from its records under the checkpoint's pins. Caller holds the gate.
    private OperatorProgress OperatorProgressLocked(string identity, OperatorCheckpoint checkpoint)
    {
        EstateModel model = session.Load();
        RecordEnvelope? record = model.ById.TryGetValue(checkpoint.Id, out RecordEnvelope? found) && found.ComputedHash == checkpoint.Hash ? found : null;
        if (checkpoint.Head is null) return new(identity, checkpoint, record, record is null ? "prepared" : "recorded");
        RecordEnvelope[] operations = model.Current("operation").Where(o => o.Body.TryGetProperty("target", out JsonElement t)
            && ChangeText(t, "head") == checkpoint.Head && ChangeText(t, "productHash") == checkpoint.ProductHash
            && ChangeText(t, "repositoryHash") == checkpoint.RepositoryHash).ToArray();
        RecordEnvelope? push = Operation("change-push", "branch", checkpoint.Branch);
        RecordEnvelope? open = Operation("change-open", "branch", checkpoint.Branch);
        string? pull = open is not null && EstateModel.BodyString(open, "status") == "observed-success" ? ChangeText(open.Body.GetProperty("target"), "pullRequest") : null;
        RecordEnvelope? merge = pull is null ? null : Operation("change-merge", "pullRequest", pull);
        string? status = merge is null ? null : EstateModel.BodyString(merge, "status");
        string stage = push is null || EstateModel.BodyString(push, "status") != "observed-success" ? "committed" : pull is null ? "pushed"
            : merge is null ? "opened" : status == "observed-success" ? "merged" : status is "known-failure" or "canceled" ? "merge-failed"
            : status == "dispatched" && QueuedMerge(merge) && QueuedConfirmed(model, merge, checkpoint.Head) ? "merge-queued" : "merge-uncertain";
        if (stage != "merged") return new(identity, checkpoint, record, stage, pull, merge);
        HashSet<string> revisions = model.Chain(merge!).ToHashSet(StringComparer.Ordinal);
        RecordEnvelope? target = model.Current("operation").Where(o => EstateModel.BodyString(o, "action") == "change-observe"
            && EstateModel.BodyString(o, "status") == "observed-success" && o.Body.TryGetProperty("target", out JsonElement t)
            && ChangeText(t, "observation") == "target-run" && revisions.Contains(ChangeText(t, "operation") ?? string.Empty))
            .OrderBy(o => o.At, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal).LastOrDefault();
        RecordEnvelope? ask = model.Current("ask").Where(a => a.Subject is not null && revisions.Contains(a.Subject.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        return new(identity, checkpoint, record, stage, pull, merge, target, target is null ? null : TargetVerdict(merge!, target), ask);

        RecordEnvelope? Operation(string action, string field, string value) => operations.Where(o => EstateModel.BodyString(o, "action") == action
            && ChangeText(o.Body.GetProperty("target"), field) == value).OrderBy(o => o.At, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal).LastOrDefault();
    }

    // The commit whose runs judge a queued merge's target observation: the group head that landed its merge commit, when the observation
    // names one, else the merge commit itself (CODE-71).
    private static string? QueueGate(JsonElement observed, string? commit) =>
        ChangeText(observed, "queueGroup") is string group && Patterns.Commit.IsMatch(group) ? group : commit;

    // A recorded target-run observation's verdict, by the rules the merge observation applies: passed, failed, pending or uncertain.
    private static string TargetVerdict(RecordEnvelope merge, RecordEnvelope observation)
    {
        JsonElement merged = merge.Body.GetProperty("target");
        JsonElement observed = observation.Body.GetProperty("target");
        string? commit = ChangeText(merged, "mergeCommit");
        ChangeCheck[] checks = observation.Body.TryGetProperty("checks", out JsonElement recorded) && recorded.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<ChangeCheck[]>(recorded, ProtocolJson.Options) ?? [] : [];
        string[] required = merge.Body.TryGetProperty("requiredChecks", out JsonElement names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Select(n => n.GetString()).OfType<string>().ToArray() : [];
        bool exact = commit is not null && ChangeText(observed, "observedHead") == commit && ChangeText(observed, "observedTarget") == ChangeText(merged, "target")
            && ChangeText(observed, "complete") == "true";
        // A commit a merge queue landed inside a group is judged by the runs of that group's head (CODE-71).
        string? gate = QueuedMerge(merge) ? QueueGate(observed, commit) : commit;
        if (exact && (QueuedMerge(merge) ? ChangeGate.QueuedRefusals(gate!, required, checks, true) : ChangeGate.Refusals(gate!, required, checks, true)).Count == 0) return "passed";
        if (exact && checks.Any(c => c.Head == gate && c.Status == "completed"
            && c.Conclusion is "failure" or "cancelled" or "timed_out" or "action_required" or "stale" or "startup_failure")) return "failed";
        return !exact || checks.Any(c => c.Head != gate || c.Status == "unknown" || c.Conclusion == "unknown") ? "uncertain" : "pending";
    }

    // Names the action by its record id, type and subject, and where it stopped.
    private static string OperatorDescription(OperatorProgress progress)
    {
        string named = $"{progress.Type ?? "record"} {progress.Checkpoint.Id}" + (progress.Subject is null ? string.Empty : " of " + progress.Subject);
        string where = progress.Stage switch
        {
            "prepared" => "prepared, with its signed record not yet stored",
            "recorded" => "stored and not yet committed",
            "committed" => $"committed on {progress.Checkpoint.Branch} and not yet pushed",
            "pushed" => $"pushed on {progress.Checkpoint.Branch}, with no pull request open",
            "opened" => $"open as pull request {progress.PullRequest} and not merged",
            "merge-failed" => $"open as pull request {progress.PullRequest}, whose merge failed",
            "merge-uncertain" => $"open as pull request {progress.PullRequest}, whose merge is unconfirmed",
            "merge-queued" => $"open as pull request {progress.PullRequest}, waiting in the target branch's merge queue",
            _ => $"merged in pull request {progress.PullRequest} at {progress.MergeCommit}, with its target checks " + progress.CheckStatus switch
            {
                "passed" => "recorded as passed",
                "failed" => "failed",
                "pending" => "pending",
                "uncertain" => "incompletely observed",
                _ => "not yet observed",
            },
        };
        return named + ", " + where + (progress.Record is null && progress.Stage != "prepared" ? "; its signed record is missing from this clone" : string.Empty);
    }

    // What finishing an action needs, as commands built only from validated identities.
    private static string OperatorNextStep(OperatorProgress progress)
    {
        string by = progress.By is string principal && Ulid.IsValid(principal) ? principal : "<operator-id>";
        string finish = $"vericairn operator unfinished --finish --by {by}";
        if (progress.Finishable)
        {
            return progress.CheckStatus switch
            {
                "failed" => $"Rerun or correct the failed target run at {progress.MergeCommit} on the host"
                    + (progress.Ask is null ? string.Empty : $" (ask {progress.Ask.Id} records it)") + $"; once it passes, run {finish}, or start the next operator action, which finishes this one first.",
                "passed" => $"Run {finish}, or start the next operator action, which finishes this one first.",
                _ => $"Once its target checks pass, run {finish}, or start the next operator action, which finishes this one first.",
            };
        }
        if (progress.Stage == "merged") return $"Restore its signed record {progress.Checkpoint.Id} in this clone (update the clone from the target branch), then run {finish}.";
        if (progress.Stage == "merge-uncertain" && progress.Merge is not null)
            return $"Observe the merge under its original identity with vericairn changes observe --operation {progress.Merge.Id} --by {by}, then run vericairn operator unfinished again.";
        if (progress.Stage == "merge-queued" && progress.Merge is not null)
            return $"Once the merge queue settles it, observe the merge under its original identity with vericairn changes observe --operation {progress.Merge.Id} --by {by}, or rerun the original operator record command, which observes it and goes on.";
        string resume = progress.Type is string type && RecordTypeRegistry.Find(type) is not null && Ulid.IsValid(by)
            ? OperatorResume(new(type, by, default, progress.Subject, progress.Record?.Supersedes, progress.Checkpoint.Product))
            : "the original vericairn operator record command";
        string body = progress.Record is null ? "its original JSON body" : $"the body of its stored record (vericairn records show {progress.Checkpoint.Id})";
        if (progress.Stage == "merge-failed" && progress.Merge is not null && QueuedMerge(progress.Merge))
            return $"Observe the queued merge with vericairn changes observe --operation {progress.Merge.Id} --by {by}, which records it if the queue merged it since; otherwise resolve the failure, then rerun {resume} with {body} and --reconciliation <explanation>, which observes it again first.";
        return progress.Stage == "merge-failed"
            ? $"Resolve the merge failure, then rerun {resume} with {body} and --reconciliation <explanation>."
            : $"Rerun {resume} with {body} to resume it.";
    }

    // Derive membership and bytes from the same versioned generators used by record writes and estate checks.
    // A resumed action reads its stored signed record; neither missing output nor a changed member permits signing again.
    private Dictionary<string, string> OperatorRecordFiles(RecordWritten written)
    {
        EstateModel model = session.Load();
        RecordEnvelope record = model.ById[written.Id];
        if (record.ComputedHash != written.Hash || record.ExpectedPath != written.Path)
            throw new ProviderRefusedException("The stored record differs from the original action; inspect it before resuming.");
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [written.Path] = ContentHash.Sha256(session.Files().Single(f => f.Path == written.Path).Bytes),
        };
        foreach (RecordEnvelope declaration in model.Current("living-document").Where(d => EstateModel.BodyString(d, "repository") is null
            && EstateModel.BodyString(d, "pathPattern") is not null && GeneratedDocumentContract.IsStrict(model, d)
            && !GeneratedDocumentContract.IsRetired(model, d)))
        {
            if (DocumentChecks.Generator(declaration) is not (string id, _) || DocumentGenerators.Find(id)?.Descriptor is not { } descriptor)
                throw new ProviderRefusedException("A declared register generator is unavailable; restore it before resuming.");
            if (!descriptor.RecordTypes.Contains(record.Type)) continue;
            DocumentMemberSelection members = DocumentGeneration.Members(declaration, DocumentSourceSelection.Records(model, descriptor));
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration);
            if (members.Status != DocumentGenerationStatus.Success || !plan.Current)
                throw new ProviderRefusedException("The declared register members are incomplete or changed; recover their original generated bytes before resuming.");
            foreach (DocumentMember member in members.Members.Where(m => m.Context.Sources[0].Identity == record.Id))
                files.Add(member.Path, plan.Expected.Single(f => f.Path == member.Path).Hash);
        }
        return files;
    }

    private void RequireOperatorRecord(string by)
    {
        EstateModel model = session.Load();
        if (session.Profile != "repository-synced" || session.Manifest.RecordFormat < 3)
            throw new ProviderRefusedException("An operator record command requires a repository-synced estate at record format 3.");
        RequireChangeAuthority(by);
        if (!model.HasRole(by, Roles.Operator)) throw new ProviderRefusedException("Only an operator can record an operator action.");
        if (RequestDeviceRefusal(model) is Problem denied) throw new ProviderRefusedException(denied.Message);
        if (requestDevice.Value is { Closed: false } request && Standing(model, request.Device)?.Principal != by)
            throw new ProviderRefusedException("An operator command can act only for the requesting device's own principal.");
        string? refusal = ResolveSigner(model, new RecordDraft("acceptance", by, default), out IRecordSigner? signer);
        if (refusal is not null || signer is null)
            throw new ProviderRefusedException("The operator needs their own admitted signing device on this machine. Run the command on that device; no unsigned record is accepted.");
    }

    private ChangeConfiguration OperatorConfiguration(string? product)
    {
        if (product is null)
        {
            string[] candidates = session.Load().Current("product").Where(p => p.Body.TryGetProperty("changes", out JsonElement c)
                && RepositoryClones.Find(session.Manifest.Estate, ChangeText(c, "repository")!) is string clone && SameOperatorRoot(clone))
                .Select(p => p.Id).ToArray();
            if (candidates.Length != 1) throw new ProviderRefusedException("Register the estate repository clone with one product changes declaration, or select that product with --product.");
            product = candidates[0];
        }
        ChangeConfiguration configuration = ChangeConfigurationFor(product, currentDeclaration: true);
        if (!SameOperatorRoot(ChangeClone(configuration))) throw new ProviderRefusedException("The product's registered clone must be this estate repository.");
        return configuration;
    }

    private bool SameOperatorRoot(string clone) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(clone)),
        Path.TrimEndingDirectorySeparator(Root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void SaveOperatorCheckpoint(string identity, OperatorCheckpoint checkpoint) => session.Live.SetMeta(identity, JsonSerializer.Serialize(checkpoint, ProtocolJson.Options));

    private bool OperatorCompletionRecorded(OperatorCheckpoint checkpoint, ChangeResponse completed, ChangeConfiguration configuration)
    {
        EstateModel model = session.Load();
        if (completed.Operation is null || completed.Observation is null || completed.Head != checkpoint.Head || completed.CheckStatus != "passed"
            || !model.ById.TryGetValue(completed.Operation.Id, out RecordEnvelope? merge) || merge.ComputedHash != completed.Operation.Hash
            || EstateModel.BodyString(merge, "action") != "change-merge" || EstateModel.BodyString(merge, "status") != "observed-success"
            || !model.ById.TryGetValue(completed.Observation.Id, out RecordEnvelope? observation) || observation.ComputedHash != completed.Observation.Hash
            || EstateModel.BodyString(observation, "action") != "change-observe" || EstateModel.BodyString(observation, "status") != "observed-success") return false;
        JsonElement merged = merge.Body.GetProperty("target");
        JsonElement observed = observation.Body.GetProperty("target");
        return ChangeText(merged, "head") == checkpoint.Head && ChangeText(merged, "branch") == checkpoint.Branch
            && ChangeText(merged, "productHash") == checkpoint.ProductHash && ChangeText(merged, "repositoryHash") == checkpoint.RepositoryHash
            && ChangeText(merged, "mergeCommit") == completed.MergeCommit && completed.MergeCommit is not null
            && ChangeText(observed, "operation") == merge.Id && ChangeText(observed, "observation") == "target-run"
            && ChangeText(observed, "complete") == "true" && ChangeText(observed, "observedHead") == completed.MergeCommit
            && ChangeText(observed, "observedTarget") == configuration.Target
            && (QueuedMerge(merge) ? ChangeGate.QueuedRefusals(QueueGate(observed, completed.MergeCommit)!, configuration.Required,
                JsonSerializer.Deserialize<ChangeCheck[]>(observation.Body.GetProperty("checks"), ProtocolJson.Options)!, true)
            : ChangeGate.Refusals(completed.MergeCommit, configuration.Required,
                JsonSerializer.Deserialize<ChangeCheck[]>(observation.Body.GetProperty("checks"), ProtocolJson.Options)!, true)).Count == 0;
    }

    private string PullChecks(RecordWritten observation, ChangeConfiguration configuration, string branch, string pullRequest, string head, out string reason)
    {
        lock (gate)
        {
            RecordEnvelope record = session.Load().ById[observation.Id];
            JsonElement target = record.Body.GetProperty("target");
            if (record.ComputedHash != observation.Hash || ChangeText(target, "complete") != "true" || ChangeText(target, "observedHead") != head
                || ChangeText(target, "observedTarget") != configuration.Target || ChangeText(target, "branch") != branch
                || ChangeText(target, "pullRequest") != pullRequest || ChangeText(target, "state") != "open")
            {
                reason = "The recorded pull-request observation is incomplete, closed or differs from the pinned branch, target or head.";
                return "uncertain";
            }
            ChangeCheck[] checks = JsonSerializer.Deserialize<ChangeCheck[]>(record.Body.GetProperty("checks"), ProtocolJson.Options)!;
            IReadOnlyList<string> refusals = ChangeGate.Refusals(head, configuration.Required, checks, true);
            reason = string.Join(" ", refusals);
            if (refusals.Count == 0) return "passed";
            return checks.Any(c => c.Head != head || c.Status == "unknown" || c.Status == "completed" && c.Conclusion != "success") ? "failed" : "pending";
        }
    }

    private static string OperatorResume(OperatorRecordRequest request)
    {
        // Only validated command tokens are echoed. Authored prose, including reconciliation, never enters a command string.
        if (string.IsNullOrEmpty(request.Type) || RecordTypeRegistry.Find(request.Type) is null || !Ulid.IsValid(request.By)) return "rerun the operator record command with valid arguments and the same JSON body.";
        string command = $"vericairn operator record {request.Type} --by {request.By} --body -";
        if (request.Subject is not null && TryParseSubject(request.Subject, out RecordReference? subject) && RecordTypeRegistry.Find(subject!.Type) is not null)
            command += $" --subject {subject.Type}:{subject.Id}" + (subject.Hash is null ? string.Empty : ":" + subject.Hash);
        if (request.Supersedes is not null && Ulid.IsValid(request.Supersedes)) command += " --supersedes " + request.Supersedes;
        if (request.Product is not null && Ulid.IsValid(request.Product)) command += " --product " + request.Product;
        return command;
    }
}
