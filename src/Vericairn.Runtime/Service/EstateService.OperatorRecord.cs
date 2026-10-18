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

    // A local recovery checkpoint contains identities and hashes only, never a key or the operator's input body.
    // Records and MERGE-01's operations establish what happened; this checkpoint never establishes authority or a successful effect.
    private sealed record OperatorCheckpoint(string Product, string ProductHash, string RepositoryHash, string Id, string At,
        RecordCommitBasis Basis, string Branch, string Hash, string? Head = null, ChangeResponse? Completed = null);

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
                string? active = session.Live.Meta("operator-record:active");
                if (!string.IsNullOrEmpty(active) && active != identity)
                    throw new ProviderRefusedException("An earlier operator action is unfinished. Rerun its original type, author, subject and JSON body before starting another.");
                if (checkpoint is null)
                {
                    RecordCommitBasis basis = adapter.Prepare(Root);
                    string id = Ulid.NewUlid().Value;
                    string at = Now();
                    var draft = new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes, ParseAt(at), id);
                    (authored, string? refusal) = AuthorLocked(draft, session.Load());
                    if (authored is null) throw new ProviderRefusedException(refusal!);
                    if (JudgeProposedLocked([authored]) is Problem rejected)
                        return new(identity, "refused", "The proposed signed record did not pass validation.", false, "Correct the JSON body and " + resume, Problem: rejected);
                    checkpoint = new(configuration.Product.Id, configuration.Product.ComputedHash, configuration.Repository.ComputedHash,
                        id, at, basis, "operator/" + id, authored.Envelope.ComputedHash);
                    SaveOperatorCheckpoint(identity, checkpoint);
                    session.Live.SetMeta("operator-record:active", identity);
                }
                if (checkpoint.ProductHash != configuration.Product.ComputedHash || checkpoint.RepositoryHash != configuration.Repository.ComputedHash)
                    throw new ProviderRefusedException("The recorded product or repository declaration changed; reconcile the original action before continuing.");
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
                    if (written is null || !OperatorCompletionRecorded(checkpoint, completed, configuration))
                        throw new ProviderRefusedException("The completed action's recorded evidence is unavailable; restore it before resuming.");
                    adapter.Verify(Root, checkpoint.Head!, checkpoint.Basis.Head, written.Path,
                        ContentHash.Sha256(session.Files().Single(f => f.Path == written.Path).Bytes));
                    if (active == identity) session.Live.SetMeta("operator-record:active", string.Empty);
                    return Answer("completed", completed.Message, true, "The signed record and checked merge are complete.", completed);
                }
            }

            stage = "branch";
            cancellationToken.ThrowIfCancellationRequested();
            adapter.Branch(Root, checkpoint.Basis, checkpoint.Branch);
            stage = "record";
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireOperatorRecord(request.By);
                if (written is null)
                {
                    adapter.Check(Root, checkpoint.Branch, checkpoint.Basis.Head, new HashSet<string>(StringComparer.Ordinal));
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
                    if (JudgeProposedLocked([authored]) is Problem rejected)
                        return Answer("refused", "The record no longer passes validation; the branch is retained.", false, "Resolve the validation findings and " + resume) with { Problem = rejected };
                    session.Write(authored);
                    written = new(authored.RelativePath, authored.Envelope.Id, authored.Envelope.ComputedHash);
                }
            }

            stage = "commit";
            string byteHash;
            lock (gate) byteHash = ContentHash.Sha256(session.Files().Single(f => f.Path == written.Path).Bytes);
            string head = adapter.Commit(Root, checkpoint.Branch, checkpoint.Basis.Head, written.Path, byteHash, Receipts());
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
                "Record operator " + request.Type, "Signed record " + written.Id + " at " + written.Hash + ".", request.By, reconciliation)));
            if (!change.Ok || change.PullRequest is null) return Stopped(change);
            string pullRequest = change.PullRequest;
            RecordWritten opening = change.Operation!;

            // A confirmed merge is only observed again. Neither waiting nor a rerun can send that mutation a second time.
            RecordEnvelope? priorMerge = Prior("change-merge", pullRequest);
            if (priorMerge is not null && EstateModel.BodyString(priorMerge, "status") is not ("known-failure" or "canceled"))
            {
                stage = "merge";
                Guard();
                change = ChangesObserve(new(priorMerge.Id, request.By));
                if (change.Status != "observed-success") return Stopped(change);
            }
            else
            {
                stage = "checks";
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    Guard();
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
            stage = "target-checks";
            var targetWatch = Stopwatch.StartNew();
            while (!change.Ok && change.Status == "observed-success" && change.CheckStatus == "pending")
            {
                if (!Pause(targetWatch)) return Stopped(change);
                Guard();
                change = ChangesObserve(new(change.Operation!.Id, request.By));
            }
            if (!change.Ok || change.CheckStatus != "passed") return Stopped(change);
            lock (gate)
            {
                SaveOperatorCheckpoint(identity, checkpoint with { Completed = change });
                session.Live.SetMeta("operator-record:active", string.Empty);
            }
            return Answer("completed", change.Message, true, "The signed record and checked merge are complete.", change);

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
                : latest.Status is "known-failure" or "canceled" ? "Resolve the known failure, then " + resume + " --reconciliation <explanation>"
                : $"vericairn changes observe --operation {latest.Operation.Id} --by {request.By}; after reconciling the original effect, {resume}";
            return Answer("stopped", latest.Message, false, next, latest);
        }

        HashSet<string> Receipts()
        {
            lock (gate)
            {
                EstateModel model = session.Load();
                HashSet<string> pulls = model.OfType("operation").Where(o => o.By == request.By && EstateModel.BodyString(o, "action") == "change-open"
                    && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "product") == checkpoint!.Product
                    && ChangeText(t, "head") == checkpoint.Head && ChangeText(t, "branch") == checkpoint.Branch)
                    .Select(o => ChangeText(o.Body.GetProperty("target"), "pullRequest")).OfType<string>().ToHashSet(StringComparer.Ordinal);
                HashSet<string> owned = model.OfType("operation").Where(o => o.By == request.By
                    && o.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "product") == checkpoint!.Product
                    && ChangeText(t, "head") == checkpoint.Head && (ChangeText(t, "branch") == checkpoint.Branch
                        || EstateModel.BodyString(o, "action") == "change-merge" && pulls.Contains(ChangeText(t, "pullRequest") ?? string.Empty))
                    && EstateModel.BodyString(o, "action") is "change-push" or "change-open" or "change-merge").Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
                // Include every revision and the observations and asks that point to those original operation records.
                bool added;
                do
                {
                    added = false;
                    foreach (RecordEnvelope record in model.Records)
                        if (record.By == request.By && (record.Supersedes is not null && owned.Contains(record.Supersedes)
                            || record.Subject is not null && owned.Contains(record.Subject.Id) && record.Type is "operation" or "ask"))
                            added |= owned.Add(record.Id);
                } while (added);
                return model.Records.Where(r => owned.Contains(r.Id)).Select(r => r.ExpectedPath).ToHashSet(StringComparer.Ordinal);
            }
        }
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
            && ChangeGate.Refusals(completed.MergeCommit, configuration.Required,
                JsonSerializer.Deserialize<ChangeCheck[]>(observation.Body.GetProperty("checks"), ProtocolJson.Options)!, true).Count == 0;
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
