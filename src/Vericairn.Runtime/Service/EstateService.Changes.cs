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

    /// <summary>Opens only from a recorded successful push of the same repository, branch and exact head.</summary>
    public ChangeResponse ChangesOpen(ChangesOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeEffect(request.Product, request.By, "change-open", request.Branch, request.Head, request.Reconciliation, claim =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.Body is null || request.Body.Length > 20000)
                throw new ProviderRefusedException("An opening needs a title of at most 200 characters and a body of at most 20000.");
            lock (gate)
            {
                bool pushed = session.Load().Current("operation").Any(o => EstateModel.BodyString(o, "action") == "change-push"
                    && EstateModel.BodyString(o, "status") == "observed-success" && o.Body.TryGetProperty("target", out JsonElement t)
                    && ChangeText(t, "repository") == claim.Configuration.Repository.Id && ChangeText(t, "branch") == request.Branch
                    && ChangeText(t, "head") == request.Head && ChangeText(t, "destination") == claim.Configuration.Destination);
                if (!pushed) throw new ProviderRefusedException("No successful scanned push is recorded for this repository, branch and exact head.");
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
        return ChangeEffect(request.Product, request.By, "change-merge", request.PullRequest, request.Head, request.Reconciliation, claim =>
        {
            if (claim.Configuration.Required.Count == 0) throw new ProviderRefusedException("No required checks are declared on the product.");
            PullRequestObservation? pull = ObserveChange(claim, "pull-request", observation =>
            {
                PullRequestObservation? value = claim.Configuration.Provider.Observe(claim.Configuration.Destination, request.PullRequest);
                if (value is not null) StorePullObservation(observation, value);
                return value;
            }, out RecordWritten observed);
            claim.Body["checks"] = JsonSerializer.SerializeToNode(pull?.Checks ?? [], ChangeJson);
            if (pull is null || pull.Number != request.PullRequest || pull.Head != request.Head || pull.Target != claim.Configuration.Target || pull.State != "open")
                throw new ProviderRefusedException("The pull request is absent, closed, targets another branch or its head moved.");
            IReadOnlyList<string> refused = ChangeGate.Refusals(request.Head, claim.Configuration.Required, pull.Checks, pull.Complete);
            if (refused.Count > 0) throw new ProviderRefusedException(string.Join(" ", refused));
            claim.Body["target"]!["pullRequest"] = pull.Number;
            claim.Body["target"]!["branch"] = pull.Branch;
            claim.Body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Checks observed before merge.", ["hash"] = observed.Hash, ["location"] = observed.Path });
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

    /// <summary>Observes an original change under its recorded pins. Absence, stale heads and incomplete reads remain uncertain.</summary>
    public ChangeResponse ChangesObserve(ChangesObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
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

            if (claim.Status is "not-started" or "dispatched")
                FinishChange(claim, "uncertain", "The original intent has no confirmed outcome; reconcile it by observation before any retry.");
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
                // A crash may have left the initial intent current. Preserve uncertainty as the bridge to the observation.
                if (claim.Status is "not-started" or "known-failure" or "canceled")
                {
                    claim.Body["dispatchedAt"] ??= Now();
                    claim.Body["reconciliation"] = "The effect was subsequently observed under its original identity.";
                    FinishChange(claim, "uncertain", "Reconciliation of the earlier unconfirmed effect.");
                }
                FinishChange(claim, "observed-success", "The original effect was confirmed by a new recorded observation.");
            }
            if (confirmed && action == "change-merge") return ObserveChangeTarget(claim, claim.Body["target"]!["mergeCommit"]!.GetValue<string>());
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
                WriteOutcome written = NewRecord(new("operation", by, Element(body), $"product:{configuration.Product.Id}", prior?.Id));
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
        bool granted = model.Current("authority").Any(a => a.AtTime <= at && !EstateModel.Expired(a, at)
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
            WriteOutcome intent = NewRecord(new("operation", original.By, Element(body), $"operation:{original.Written.Id}"));
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
        TargetObservation target = ObserveChange(claim, "target-run", observation =>
        {
            observation.Body["target"]!["mergeCommit"] = commit;
            TargetObservation value = claim.Configuration.Provider.ObserveTarget(claim.Configuration.Destination, claim.Configuration.Target, commit);
            observation.Body["checks"] = JsonSerializer.SerializeToNode(value.Checks, ChangeJson);
            observation.Body["target"]!["observedHead"] = value.Head;
            observation.Body["target"]!["observedTarget"] = value.Branch;
            observation.Body["target"]!["complete"] = value.Complete ? "true" : "false";
            return value;
        }, out RecordWritten recorded);
        bool exact = target.Head == commit && target.Branch == claim.Configuration.Target && target.Complete;
        bool passed = exact && ChangeGate.Refusals(commit, claim.Configuration.Required, target.Checks, true).Count == 0;
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
                    string op = EstateModel.BodyString(model.Current("estate").Single(), "operator")!;
                    JsonObject body = new() { ["to"] = op, ["state"] = "open", ["question"] = "The target branch's run failed at the recorded merge commit. How should it be corrected?",
                        ["recommendation"] = "Inspect the recorded failing checks and prepare a corrective change.", ["consequence"] = "The failed target run remains unresolved." };
                    WriteOutcome result = NewRecord(new("ask", claim.By, Element(body), $"operation:{claim.Written.Id}:{claim.Written.Hash}"));
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
        WriteOutcome saved = NewRecord(new("operation", claim.By, Element(next), subject, claim.Written.Id));
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
}
