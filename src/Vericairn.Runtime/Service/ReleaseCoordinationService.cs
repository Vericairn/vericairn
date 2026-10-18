// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Store;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>Owns release admission, in-flight identities, dispatch receipts and reconciliation under each original identity.</summary>
internal sealed class ReleaseCoordinationService(
    EstateRecordAccess recordAccess,
    Func<ProviderRegistry> providers,
    Func<string, RecordResponse?> readRecord,
    Func<NewRecordRequest, WriteOutcome> writeRecord)
{
    private readonly object gate = recordAccess.Gate;
    private readonly EstateSession session = recordAccess.Session;
    private readonly HashSet<string> releaseDispatches = new(StringComparer.Ordinal);
    private ProviderRegistry Providers => providers();

    /// <summary>
    /// Reads each refuse-level repository document at its selected commit, where a clone is registered here: those whose bytes
    /// differ from their latest review, and what was compared or could not be. Covered paths use the same selected commit.
    /// </summary>
    private static (List<string> Changed, List<string> Compared) CompareLivingDocuments(EstateModel model, IReadOnlyList<LivingDocuments.State> documents, IReadOnlySet<string> releaseRepositories)
    {
        var changed = new List<string>();
        var compared = new List<string>();
        foreach (LivingDocuments.State document in documents.Where(d => d.Repository is not null && d.DeclaresRefuse))
        {
            RecordEnvelope? review = document.Clearance is { Type: "document-review" } cleared ? cleared : null;
            JsonElement content = review is not null && review.Body.TryGetProperty("content", out JsonElement c) ? c : default;
            string? clearedHash = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("hash", out JsonElement hash) ? hash.GetString() : null;
            string clearedAt = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("commit", out JsonElement at) ? at.GetString() ?? "?" : "?";
            if (clearedHash is null)
            {
                changed.Add($"{document.Path}: no review has cleared its content");
                continue;
            }

            IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(document.Declaration, "source"));
            string? commit = document.ComparisonCommit;
            string? resolved = commit is null ? null : source?.Resolve(model, document.Declaration, commit);
            if (commit is null || resolved != commit)
            {
                RecordEnvelope? repository = model.ById.GetValueOrDefault(document.Repository!);
                bool registered = repository is not null && model.Manifest is not null &&
                    model.Chain(model.Latest(repository)).Any(id => RepositoryClones.Find(model.Manifest.Estate, id) is not null);
                if (registered || source?.Head(model, document.Declaration) is not null)
                {
                    string missing = commit is null ? "has no readable HEAD commit"
                        : $"does not contain {(releaseRepositories.Contains(document.Repository!) ? "release" : "repository")} commit {commit}";
                    changed.Add($"{document.Path}: the registered clone of {EstateModel.BodyString(repository!, "name")} ({document.Repository}) {missing}; fetch that repository's clone, then retry");
                }
                else
                {
                    string where = releaseRepositories.Contains(document.Repository!) ? $"release commit {commit}"
                        : $"a commit of its own repository (latest review: {clearedAt})";
                    compared.Add($"{document.Path} not compared, since no clone of its repository is registered here; the independent review reads it at {where}");
                }

                continue;
            }

            byte[]? bytes = source!.Read(model, document.Declaration, commit);
            string? now = bytes is null ? null : ContentHash.Sha256(bytes);
            if (now == clearedHash)
            {
                compared.Add($"{document.Path} at {commit[..12]} is the content its review {review!.Id} cleared");
            }
            else
            {
                changed.Add(now is null
                    ? $"{document.Path} is not in its repository at {commit[..12]}"
                    : $"{document.Path} changed after its review at {clearedAt[..Math.Min(12, clearedAt.Length)]}: at {commit[..12]} it is {now}, and the review cleared {clearedHash}");
            }
        }

        return (changed, compared);
    }

    private static (List<string> Changed, List<string> Compared) ReleaseDocumentChecks(EstateModel model, string product, string? commit, ProviderDescriptor provider, string destination)
    {
        IReadOnlySet<string> repositories = ReleaseRepositories(model, provider, destination);
        // Recheck inside the admission boundary: a declaration may have arrived while the provider was being resolved.
        if (commit is null && NeedsReleaseCommit(model, product, repositories))
        {
            return (["The exact release commit is unavailable for the product's living documents; resolve it at the build provider before submitting again"], []);
        }

        IReadOnlyList<LivingDocuments.State> documents = LivingDocuments.AtReleaseCommit(model, product, commit, repositories);
        (List<string> changed, List<string> compared) = CompareLivingDocuments(model, documents, repositories);
        changed.AddRange(documents.Where(d => d.Closes).Select(d => d.Describe()));
        return (changed, compared);
    }

    private static bool NeedsReleaseCommit(EstateModel model, string product, IReadOnlySet<string> repositories) =>
        model.Current("living-document").Any(d => EstateModel.BodyString(d, "repository") is string repository && repositories.Contains(repository) &&
            LivingDocuments.NamesProduct(model, d, product) && LivingDocuments.Triggers(d).Any(t => t.Level == "refuse"));

    // Match the effective build destination, including request overrides, through the provider's resource location keys.
    // Every revision of a matching resource denotes the same repository, including an older declaration or clone registration.
    private static HashSet<string> ReleaseRepositories(EstateModel model, ProviderDescriptor provider, string destination) =>
        model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository" &&
            r.Body.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.Object &&
            provider.ServedLocations.Any(key => location.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
                string.Equals(value.GetString(), destination, key == "github" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            .SelectMany(model.Chain).ToHashSet(StringComparer.Ordinal);

    /// <summary>Returns the current release records.</summary>
    internal IReadOnlyList<ReleaseRow> Releases()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.Current("release").OrderBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => new ReleaseRow(EstateModel.BodyString(r, "version"), EstateModel.BodyString(r, "status"), EstateModel.BodyString(r, "commit"), r.Id, r.ComputedHash, EstateModel.BodyString(r, "signing"))).ToList();
        }
    }

    /// <summary>Claims a release identity before dispatch and records the provider's receipt.</summary>
    internal ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string pending = $"release {request.Version}";
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Product))
        {
            return ReleaseProblem(pending, "not-started", "refused", "The recording principal and the product must be ULIDs.");
        }

        RecordResponse? productRecord = readRecord(request.Product);
        if (productRecord is null || productRecord.Type != "product" || !productRecord.Current)
        {
            return ReleaseProblem(pending, "not-started", "not-found", $"No current product has id {request.Product}.");
        }

        ReleaseProfile profile = ReleaseProfile.FromProductBody(productRecord.Envelope.GetProperty("body"));
        string? providerId = string.IsNullOrWhiteSpace(request.Provider) ? profile.Destination?.Provider : request.Provider.Trim();
        if (providerId is null)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"No release provider is selected: product {request.Product} declares no release destination and the request names no provider. Declare the destination on the product record or name the provider; nothing was recorded.");
        }

        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"The release provider '{providerId}' has no implementation on the runtime's machine; nothing selected another and nothing was recorded. Available: {string.Join(", ", Providers.ReleaseProviders.Select(p => p.Provider).DefaultIfEmpty("none"))}.");
        }

        var requested = new Dictionary<string, string>(request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        AddIfGiven(requested, "repository", request.Repository);
        AddIfGiven(requested, "ref", request.Ref);
        AddIfGiven(requested, "workflow", request.Workflow);
        IReadOnlyDictionary<string, string> settings = profile.Settings(provider.Descriptor, providerId, requested);
        List<string> missing = provider.Descriptor.DeclaredSettings.Where(s => s.Required && !settings.ContainsKey(s.Name)).Select(s => s.Name).ToList();
        string? destinationSetting = provider.Descriptor.Destination;
        if (destinationSetting is not null && !settings.ContainsKey(destinationSetting) && !missing.Contains(destinationSetting))
        {
            missing.Add(destinationSetting);
        }

        if (missing.Count > 0)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"The provider '{providerId}' needs the settings {string.Join(", ", missing)}; neither the request nor product {request.Product}'s declared destination gives them, and nothing was recorded.");
        }

        string destination = destinationSetting is null ? providerId : settings[destinationSetting];
        string identity = ReleaseFlow.Identity(providerId, destination, request.Version);
        bool needsCommit;
        lock (gate)
        {
            EstateModel model = session.Load();
            needsCommit = NeedsReleaseCommit(model, request.Product, ReleaseRepositories(model, provider.Descriptor, destination));
        }

        // Resolve at the build provider, not at a potentially stale clone. Provider I/O never holds the service gate.
        string? releaseCommit = null;
        if (needsCommit)
        {
            try
            {
                releaseCommit = provider.ResolveCommit(destination, settings);
            }
            catch (ProviderRefusedException exception)
            {
                return ReleaseProblem(identity, "not-started", "refused", exception.Message);
            }

            if (releaseCommit is null || !Patterns.Commit.IsMatch(releaseCommit))
            {
                return ReleaseProblem(identity, "not-started", "refused", $"The provider '{providerId}' could not resolve the release's exact commit at {destination}; its living documents cannot be compared. Nothing was recorded or dispatched.");
            }
        }

        string previousId;
        JsonElement previousBody;
        // Checking the identity and recording its claim are one admission boundary. The claim survives a restart.
        lock (gate)
        {
            RecordEnvelope? current = CurrentOperation(identity);
            string? status = current is null ? null : EstateModel.BodyString(current, "status");
            if (releaseDispatches.Contains(identity))
            {
                return ReleaseProblem(identity, "uncertain", "refused", $"A dispatch under {identity} is still in progress; observe and reconcile it before any retry.");
            }

            switch (status)
            {
                case "observed-success":
                    return ReleaseProblem(identity, status, "refused", $"Release {request.Version} was already observed as built under {identity}; a corrective release is a new version.");
                case "not-started":
                    return ReleaseProblem(identity, "uncertain", "refused", $"The intent under {identity} has no dispatch receipt; its effect is unknown. Observe and reconcile it before any retry.");
                case "dispatched" or "uncertain":
                    return ReleaseProblem(identity, status, "refused", $"An operation under {identity} is {status}; observe and reconcile it before any retry.");
                case "known-failure" or "canceled" when string.IsNullOrWhiteSpace(request.Reconciliation):
                    return ReleaseProblem(identity, status, "refused", $"The earlier attempt under {identity} is {status}; retry with a reconciliation stating how it was settled under that identity.");
            }

            // The living-document gate: the product's documents due at level refuse, or unavailable while they declare it, refuse the
            // submission before any intent is recorded, all named at once, so a refused release costs no build.
            (List<string> changed, List<string> compared) = ReleaseDocumentChecks(session.Load(), request.Product, releaseCommit, provider.Descriptor, destination);
            if (changed.Count > 0)
            {
                return ReleaseProblem(identity, "not-started", "refused", $"Release {request.Version} waits for the product's living documents: {string.Join(" | ", changed)}. Review each at its repository's required commit, then submit again; nothing was recorded.");
            }

            JsonObject intentBody = ReleaseFlow.IntentBody(providerId, destination, settings, request.Version, request.Product);
            if (compared.Count > 0)
            {
                intentBody["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Living documents of the product at their repository commits: " + string.Join("; ", compared) + "." });
            }

            if (current is null)
            {
                WriteOutcome intent = writeRecord(new NewRecordRequest("operation", request.By, Element(intentBody), $"product:{request.Product}"));
                if (!intent.Ok)
                {
                    return ReleaseProblem(identity, "not-started", intent.Problem!);
                }

                previousId = intent.Written!.Id;
                previousBody = readRecord(previousId)!.Envelope.GetProperty("body");
            }
            else
            {
                // A reconciled retry also needs a durable claim: leaving the old failure or cancellation current
                // would authorize another dispatch if this process stopped before storing the receipt.
                JsonObject retry = ReleaseFlow.RetryIntent(current.Body, intentBody, request.Reconciliation!);
                WriteOutcome claim = writeRecord(new NewRecordRequest("operation", request.By, Element(retry), $"product:{request.Product}", current.Id));
                if (!claim.Ok)
                {
                    return ReleaseProblem(identity, status!, claim.Problem!);
                }

                previousId = claim.Written!.Id;
                previousBody = Element(retry);
            }

            releaseDispatches.Add(identity);
        }

        try
        {
            // The provider is called outside the gate: a dispatch may wait for the host, and every other operation must keep answering.
            JsonObject body;
            try
            {
                DispatchReceipt receipt = provider.Dispatch(new DispatchRequest(destination, settings, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = request.Version }));
                body = ReleaseFlow.AfterDispatch(previousBody, receipt);
            }
            catch (ProviderRefusedException exception)
            {
                body = ReleaseFlow.AfterRefusal(previousBody, exception.Message);
            }
            catch (ProviderUncertainException exception)
            {
                body = ReleaseFlow.AfterUncertainty(previousBody, exception.Message);
            }

            WriteOutcome revision = writeRecord(new NewRecordRequest("operation", request.By, Element(body), $"product:{request.Product}", previousId));
            if (!revision.Ok)
            {
                Problem problem = revision.Problem! with { Message = revision.Problem!.Message + " The dispatch receipt was not recorded; observe and reconcile under this identity before any retry." };
                return ReleaseProblem(identity, "uncertain", problem);
            }

            string newStatus = body["status"]!.GetValue<string>();
            string? run = body["target"]!.AsObject().TryGetPropertyValue("run", out JsonNode? runNode) ? runNode?.GetValue<string>() : null;
            string message = newStatus switch
            {
                "dispatched" => "The intent was recorded before the dispatch. Observe the run and the draft; publication stays the operator's manual step.",
                "uncertain" => "The dispatch may or may not have started a run. Observe and reconcile under this identity before any retry.",
                _ => "Nothing was dispatched. Fix the cause, then retry with a reconciliation.",
            };
            return new ReleaseResponse(identity, newStatus, message, newStatus == "dispatched", revision.Written, Run: run);
        }
        finally
        {
            lock (gate)
            {
                releaseDispatches.Remove(identity);
            }
        }
    }

    /// <summary>Reconciles the original operation and records observed build and publication facts.</summary>
    internal ReleaseResponse ReleaseObserve(ReleaseObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tag = ReleaseFlow.Tag(request.Version);
        string pending = $"release {request.Version}";
        if (!Ulid.IsValid(request.By))
        {
            return ReleaseProblem(pending, "unknown", "refused", "The recording principal must be a ULID.");
        }

        RecordEnvelope? current;
        string identity;
        JsonElement body;
        string status;
        string providerId;
        JsonElement target;
        string product;
        RecordWritten? recovered = null;
        lock (gate)
        {
            (current, string? problem) = FindReleaseOperation(request);
            if (current is null)
            {
                return ReleaseProblem(pending, "unknown", "not-found", problem!);
            }

            identity = EstateModel.BodyString(current, "identity") ?? pending;
            body = current.Body;
            status = EstateModel.BodyString(current, "status") ?? "?";
            providerId = EstateModel.BodyString(current, "provider") ?? "?";
            target = body.GetProperty("target");
            product = target.TryGetProperty("product", out JsonElement productElement) ? productElement.GetString() ?? "?" : "?";
            if (releaseDispatches.Contains(identity))
            {
                return ReleaseProblem(identity, "uncertain", "refused", $"A dispatch under {identity} is still in progress; observe and reconcile it once the dispatch returns.");
            }

            if (request.Cancel is not null)
            {
                if (status is not ("not-started" or "uncertain" or "known-failure"))
                {
                    return ReleaseProblem(identity, status, "refused", $"An operation that is {status} is not canceled; cancellation settles an intent that was never dispatched or an outcome reconciled as having had no effect.");
                }

                WriteOutcome canceled = writeRecord(new NewRecordRequest("operation", request.By, Element(ReleaseFlow.Canceled(body, request.Cancel)), $"product:{product}", current.Id));
                return canceled.Ok ? new ReleaseResponse(identity, "canceled", $"{identity}: canceled; {request.Cancel}", true, canceled.Written) : ReleaseProblem(identity, status, canceled.Problem!);
            }

            if (status == "not-started")
            {
                // An intent without a receipt is not proof that no effect happened. Persist that uncertainty before observing it.
                body = Element(ReleaseFlow.AfterUncertainty(body, "The intent has no dispatch receipt; the external effect is unknown and must be observed and reconciled."));
                WriteOutcome uncertain = writeRecord(new NewRecordRequest("operation", request.By, body, $"product:{product}", current.Id));
                if (!uncertain.Ok)
                {
                    return ReleaseProblem(identity, "uncertain", uncertain.Problem!);
                }

                current = CurrentOperation(identity)!;
                status = "uncertain";
                recovered = uncertain.Written;
            }
        }

        string? runId = body.GetProperty("target").TryGetProperty("run", out JsonElement runElement) ? runElement.GetString() : null;
        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null)
        {
            return ReleaseProblem(identity, status, "refused", $"The operation was dispatched through '{providerId}', which has no implementation on the runtime's machine; it is observed only through that provider, never another.");
        }

        RecordResponse? productRecord = readRecord(product);
        ReleaseProfile profile = productRecord is not null && productRecord.Type == "product" ? ReleaseProfile.FromProductBody(productRecord.Envelope.GetProperty("body")) : ReleaseProfile.None;
        var recordedSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in target.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name is not ("version" or "product" or "run")))
        {
            recordedSettings[property.Name] = property.Value.GetString()!;
        }

        IReadOnlyDictionary<string, string> settings = profile.Settings(provider.Descriptor, providerId, recordedSettings);
        string destination = provider.Descriptor.Destination is string key && settings.TryGetValue(key, out string? named) ? named : request.Repository ?? providerId;
        string checksums = settings.TryGetValue("checksums", out string? checksumFile) ? checksumFile : "SHA256SUMS";
        ReleaseObservation? release;
        RunObservation? run;
        try
        {
            run = runId is null ? null : provider.ObserveRun(destination, runId);
            release = provider.ObserveRelease(destination, tag, settings);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
        {
            return ReleaseProblem(identity, status, "provider", exception.Message);
        }

        ReleaseRow? recorded = Releases().FirstOrDefault(r => r.Version == request.Version && SubjectProduct(r.Id) == product);
        switch (status)
        {
            case "observed-success":
                return ObservePublication(request, identity, product, recorded, release, destination, tag, provider);
            case "dispatched" or "uncertain":
            {
                JsonObject revisionBody;
                try
                {
                    revisionBody = ReleaseFlow.AfterObservation(body, run, release, request.Version, profile, checksums);
                }
                catch (InvalidOperationException)
                {
                    string waiting = run is not null && run.Status != "completed"
                        ? $"run {run.RunId} is {run.Status.Replace('_', ' ')}"
                        : release is not null
                            ? $"draft {tag} lacks checksums for {string.Join(", ", profile.Missing(release, request.Version, checksums))}"
                            : run is null ? "no run is identified and no release is observed yet" : $"run {run.RunId} completed and no draft {tag} is observed yet";
                    string recordedNow = recovered is null ? "Nothing recorded" : "The missing receipt was recorded as uncertain";
                    return new ReleaseResponse(identity, status, $"{identity}: {status}, pending ({waiting}). {recordedNow}; observe again later{(status == "uncertain" ? ", or cancel once reconciliation shows no effect" : string.Empty)}.", true, Operation: recovered, Pending: waiting, Run: run?.RunId, Url: run?.Url ?? release?.Url);
                }

                // A branch may move after submission. Judge the commit actually built before recording success or a candidate,
                // so a failed document check can be reviewed and observed again without losing the dispatched operation.
                if (revisionBody["status"]?.GetValue<string>() == "observed-success")
                {
                    string? built = run?.HeadSha ?? release?.TargetCommit;
                    if (built is null || !Patterns.Commit.IsMatch(built))
                    {
                        return ReleaseProblem(identity, status, "refused", "The release was observed but its exact commit is unknown; the candidate record needs the commit the run built.");
                    }

                    lock (gate)
                    {
                        (List<string> changed, _) = ReleaseDocumentChecks(session.Load(), product, built, provider.Descriptor, destination);
                        if (changed.Count > 0)
                        {
                            return ReleaseProblem(identity, status, "refused", $"Release {request.Version} waits for its living documents for built commit {built}: {string.Join(" | ", changed)}. Review each at its repository's required commit, then observe again; no candidate was recorded.");
                        }
                    }
                }

                WriteOutcome revision = writeRecord(new NewRecordRequest("operation", request.By, Element(revisionBody), $"product:{product}", current.Id));
                if (!revision.Ok)
                {
                    return ReleaseProblem(identity, status, revision.Problem!);
                }

                string newStatus = revisionBody["status"]!.GetValue<string>();
                if (newStatus != "observed-success")
                {
                    return new ReleaseResponse(identity, newStatus, $"{identity}: {newStatus}. Retry with a reconciliation once the cause is settled.", false, revision.Written, Run: run?.RunId, Url: run?.Url);
                }

                string? commit = run?.HeadSha ?? release?.TargetCommit;
                if (commit is null || commit.Length != 40)
                {
                    return ReleaseProblem(identity, newStatus, "refused", "The release was observed but its exact commit is unknown; the candidate record needs the commit the run built.");
                }

                if (recorded is not null)
                {
                    return new ReleaseResponse(identity, newStatus, $"{identity}: {newStatus}; release {request.Version} is already recorded as {recorded.Status} ({recorded.Id}).", true, revision.Written, ReleaseId: recorded.Id, Run: run?.RunId, Url: release?.Url);
                }

                var evidence = new List<string> { $"Draft release {tag} observed at {release!.Url} with {release.Assets.Count} assets and checksums for every {(profile.DeclaresArtifacts ? "declared artifact" : "asset")}." };
                if (run is not null)
                {
                    evidence.Insert(0, $"Release build run {run.RunId} on commit {run.HeadSha} succeeded: {run.Url}");
                }

                (ReleaseRecordFormat formats, string formatEvidence, bool stated) = CandidateFormats(provider, destination, tag, request.Version, release, profile, checksums);
                evidence.Add(formatEvidence);
                WriteOutcome candidate = writeRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.CandidateBody(request.Version, destination, commit, release, evidence, request.Qualified ?? [], request.Signing, request.Limitations ?? [], profile, checksums, formats)), $"product:{product}"));
                if (!candidate.Ok)
                {
                    return ReleaseProblem(identity, newStatus, candidate.Problem!);
                }

                string publish = provider.ManualPublicationCommand(destination, tag);
                string formatNote = stated ? string.Empty : $" The draft states no record formats, so the candidate carries this runtime's ({formats.Describe()}); a superseding revision corrects them if the release's differ.";
                return new ReleaseResponse(identity, newStatus, $"{identity}: observed-success; release {request.Version} recorded as a candidate at commit {commit[..12]}.{formatNote} Publication is the operator's: after accepting candidate {candidate.Written!.Id} at its exact hash, publish with '{publish}', then observe again to record the observed publication.", true, revision.Written, candidate.Written, Run: run?.RunId, Publish: publish, Url: release.Url);
            }

            default:
                return new ReleaseResponse(identity, status, $"{identity}: {status}. Retry with a reconciliation once the cause is settled.", false);
        }
    }

    /// <summary>
    /// The record formats a candidate records: those the release states in the manifest of the documentation set its draft carries,
    /// read through the provider and held to the hash its checksum file names; otherwise this runtime's own, and the evidence says
    /// which, and why.
    /// </summary>
    private static (ReleaseRecordFormat Formats, string Evidence, bool Stated) CandidateFormats(IReleaseProvider provider, string destination, string tag, string version, ReleaseObservation release, ReleaseProfile profile, string checksums)
    {
        ReleaseAsset? documentation = release.Assets
            .Where(a => a.Name != checksums && profile.Classify(a.Name, version).Role == "documentation")
            .OrderBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault();
        string why;
        if (documentation is null)
        {
            why = "it carries no documentation set";
        }
        else if (documentation.Hash is null || documentation.Bytes > ReleaseFlow.DocumentationLimit)
        {
            why = documentation.Hash is null ? $"{documentation.Name} is named in no checksum" : $"{documentation.Name} is larger than an observer reads";
        }
        else
        {
            byte[]? archive;
            string? failure = null;
            try
            {
                archive = provider.ReadAsset(destination, tag, documentation.Name);
            }
            catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
            {
                archive = null;
                failure = exception.Message.Length > 200 ? exception.Message[..200] + "..." : exception.Message;
            }

            if (archive is null)
            {
                why = $"{documentation.Name} could not be read through {provider.Provider}{(failure is null ? string.Empty : " (" + failure + ")")}";
            }
            else if (ContentHash.Sha256(archive) != documentation.Hash)
            {
                why = $"{documentation.Name}, as read through {provider.Provider}, is not the content its checksum file names";
            }
            else if (ReleaseFlow.DocumentedRecordFormat(archive, version, out string missing) is ReleaseRecordFormat stated)
            {
                return (stated, $"Record formats as the release states them in the manifest of its documentation set {documentation.Name} ({documentation.Hash}): {stated.Describe()}.", true);
            }
            else
            {
                why = $"{documentation.Name} {missing}";
            }
        }

        ReleaseRecordFormat own = ReleaseRecordFormat.ThisBuild;
        return (own, $"The draft states no record formats, since {why}; recorded are those of the Vericairn {EstateService.Version.Split('+')[0]} that observed it, {own.Describe()}, and a superseding revision corrects them if the release's differ.", false);
    }

    private ReleaseResponse ObservePublication(ReleaseObserveRequest request, string identity, string product, ReleaseRow? recorded, ReleaseObservation? release, string destination, string tag, IReleaseProvider provider)
    {
        string where = release?.Url ?? destination;
        if (recorded is null)
        {
            return ReleaseProblem(identity, "observed-success", "refused", $"Release {request.Version} was observed as built but no release record exists; observe it again after the operation is recorded, or record the candidate.");
        }

        if (recorded.Status == "published")
        {
            return new ReleaseResponse(identity, "published", $"release {request.Version} is published ({recorded.Id}); nothing to observe.", true, ReleaseId: recorded.Id, Url: release?.Url);
        }

        if (release is null || release.Draft || release.PublishedAt is null)
        {
            return new ReleaseResponse(identity, recorded.Status ?? "candidate", $"release {request.Version} is recorded as {recorded.Status} and observed as {(release is null ? "absent" : "a draft")} at {where}; publication is the operator's manual step after accepting the candidate.", true, ReleaseId: recorded.Id, Publish: provider.ManualPublicationCommand(destination, tag), Url: release?.Url);
        }

        RecordResponse candidate = readRecord(recorded.Id)!;
        WriteOutcome published = writeRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.PublishedBody(candidate.Envelope.GetProperty("body"), release)), $"product:{product}", recorded.Id));
        if (!published.Ok)
        {
            return ReleaseProblem(identity, recorded.Status ?? "candidate", published.Problem!);
        }

        return new ReleaseResponse(identity, "published", $"release {request.Version} observed published at {release.PublishedAt}: {release.Url}", true, Release: published.Written, ReleaseId: recorded.Id, PublishedAt: release.PublishedAt, Url: release.Url);
    }

    /// <summary>The current release operation a request names: by provider and destination, by product and version, or by a destination that one operation alone matches.</summary>
    private (RecordEnvelope? Operation, string? Problem) FindReleaseOperation(ReleaseObserveRequest request)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            List<RecordEnvelope> operations = model.Current("operation")
                .Where(o => o.Body.TryGetProperty("target", out JsonElement t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("version", out JsonElement v) && v.GetString() == request.Version)
                .ToList();
            if (!string.IsNullOrWhiteSpace(request.Provider) && !string.IsNullOrWhiteSpace(request.Repository))
            {
                string identity = ReleaseFlow.Identity(request.Provider.Trim(), request.Repository.Trim(), request.Version);
                RecordEnvelope? byIdentity = operations.FirstOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
                return byIdentity is null ? (null, $"No operation is recorded under {identity}; a submission records the intent and dispatches the build.") : (byIdentity, null);
            }

            IEnumerable<RecordEnvelope> matching = operations;
            if (!string.IsNullOrWhiteSpace(request.Product))
            {
                matching = matching.Where(o => o.Body.GetProperty("target").TryGetProperty("product", out JsonElement p) && p.GetString() == request.Product);
            }

            if (!string.IsNullOrWhiteSpace(request.Provider))
            {
                matching = matching.Where(o => EstateModel.BodyString(o, "provider") == request.Provider);
            }

            if (!string.IsNullOrWhiteSpace(request.Repository))
            {
                matching = matching.Where(o => (EstateModel.BodyString(o, "identity") ?? string.Empty).EndsWith($":{request.Repository}:v{request.Version}", StringComparison.Ordinal));
            }

            List<RecordEnvelope> found = matching.ToList();
            if (string.IsNullOrWhiteSpace(request.Product) && string.IsNullOrWhiteSpace(request.Repository))
            {
                return (null, "Name the product, or the destination (and the provider), of the release to observe.");
            }

            return found.Count switch
            {
                1 => (found[0], null),
                0 => (null, $"No current release operation of version {request.Version} matches; a submission records the intent and dispatches the build."),
                _ => (null, $"Several release operations of version {request.Version} match ({string.Join(", ", found.Select(o => EstateModel.BodyString(o, "identity")))}); name the provider and the destination."),
            };
        }
    }

    private string? SubjectProduct(string releaseId)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.ById.TryGetValue(releaseId, out RecordEnvelope? release) ? release.Subject?.Id : null;
        }
    }

    private static void AddIfGiven(Dictionary<string, string> settings, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            settings[name] = value.Trim();
        }
    }

    private RecordEnvelope? CurrentOperation(string identity)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.Current("operation").FirstOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
        }
    }

    private static ReleaseResponse ReleaseProblem(string identity, string status, string code, string message) =>
        new(identity, status, message, false, Problem: new Problem(code, message, []));

    private static ReleaseResponse ReleaseProblem(string identity, string status, Problem problem) =>
        new(identity, status, problem.Message, false, Problem: problem);
}
