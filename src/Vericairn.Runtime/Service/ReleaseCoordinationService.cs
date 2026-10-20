// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
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
    Func<NewRecordRequest, WriteOutcome> writeRecord,
    Func<EstateModel, string, string, string?, IReadOnlySet<string>, ReleasePolicyAssessment> assessPolicies,
    Func<ReleasePolicyAssessment, IReadOnlyList<string>, IReadOnlyList<RecordReference>, string> retainBasis,
    Func<DateTimeOffset> now,
    Action<string> boundary)
{
    private readonly object gate = recordAccess.Gate;
    private readonly EstateSession session = recordAccess.Session;
    private readonly HashSet<string> releaseDispatches = new(StringComparer.Ordinal);
    private ProviderRegistry Providers => providers();

    private WriteOutcome WriteRecord(NewRecordRequest request)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Manifest?.RecordFormat >= 3)
            {
                string[] parts = request.Subject?.Split(':', 3) ?? [];
                RecordReference? subject = parts.Length >= 2 ? new(parts[0], parts[1], parts.Length == 3 ? parts[2] : null) : null;
                (AuthoredRecord? signed, string? refusal) = recordAccess.AuthorLocked(new(request.Type, request.By, request.Body, subject, request.Supersedes, now()), model);
                if (refusal is not null || signed?.Envelope.Signatures.Count is not > 0)
                    return WriteOutcome.Fail(new("refused", refusal ?? "The acting principal has no admitted held signing device for the release evidence; retain and reconcile the original identity.", []));
            }
            return writeRecord(request);
        }
    }

    private (ReleasePolicyAssessment Assessment, List<FindingDto> Objections, List<string> Compared) Admission(EstateModel model,
        ReleaseSubmitRequest request, string? commit, ProviderDescriptor provider, string destination, JsonObject body, string? previous, bool probe = true)
    {
        (List<string> changed, List<string> compared) = ReleaseDocumentChecks(model, request.Product, commit, provider, destination);
        ReleasePolicyAssessment assessment = assessPolicies(model, request.Product, request.Version, commit, ReleaseRepositories(model, provider, destination));
        List<FindingDto> objections = [.. assessment.Objections];
        if (recordAccess.RequestDeviceRefusal(model) is { } denied) objections.AddRange(denied.Findings.Count > 0 ? denied.Findings : [new("error", "release.device", "release", denied.Message)]);
        RecordEnvelope? product = model.ById.GetValueOrDefault(request.Product);
        if (product is not { Type: "product" } || !model.IsCurrent(product)) objections.Add(new("error", "release.product", "release", "The prepared product revision is no longer current."));
        if ((model.Manifest?.RecordFormat >= 3 || request.Expected is not null) && request.Expected != product?.ComputedHash)
            objections.Add(new("error", "release.expected", "release", "The expected product hash is missing or stale; prepare again from its current revision."));
        if (commit is not null && !Patterns.Commit.IsMatch(commit) || model.Manifest?.RecordFormat >= 3 && commit is null)
            objections.Add(new("error", "release.commit", "release", "Supply the prepared exact lower-case forty-digit commit; a branch name or preview verdict cannot authorize a release."));
        objections.AddRange(changed.Select(message => new FindingDto("warning", "document.release-hold", "release-documents", message)));
        // Validate current authority and the actual proposed record without recording it, even when another gate holds.
        if (probe)
        {
            (AuthoredRecord? authored, string? refusal) = recordAccess.AuthorLocked(new("operation", request.By, Element(body), new("product", request.Product, null), previous, now()), model);
            if (refusal is not null) objections.Add(new("error", "release.author", "release", refusal));
            if (authored is not null)
            {
                if (model.Manifest?.RecordFormat >= 3 && authored.Envelope.Signatures.Count == 0) objections.Add(new("error", "release.signature", "release", "A format-3 release intent needs the acting principal's admitted signing device."));
                objections.AddRange(recordAccess.LoadWith([authored]).Findings.Concat(EstateChecks.Content(authored.RelativePath, authored.Bytes)).Where(f => f.Severity == Severity.Error)
                    .Select(f => new FindingDto("error", f.Code, f.Location, f.Message)));
            }
        }
        else if (!model.HasRole(request.By, Roles.Operator) && !model.HasScope(request.By, Scopes.LocalEngineering, now()))
            objections.Add(new("error", "release.author", "release", "Release authority is no longer in force for the acting principal."));
        return (assessment, objections.Distinct().ToList(), compared);
    }

    private void Retain(ReleasePolicyAssessment assessment, IReadOnlyList<string> documents, JsonObject body, IReadOnlyList<RecordReference> excluded, List<string> hashes)
    {
        string hash = retainBasis(assessment, documents, excluded);
        hashes.Add(hash);
        body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = $"Release admission basis {assessment.Basis.Digest}, at {assessment.Basis.AsOf} (submission time); exact policy, duty and source evidence.", ["hash"] = hash });
    }

    private static ReleaseResponse GateRefusal(string identity, string status, ReleasePolicyAssessment assessment, IReadOnlyList<FindingDto> objections) =>
        ReleaseProblem(identity, status, new Problem("refused", "Release admission refused: " + string.Join(" | ", objections.Select(o => o.Code + ": " + o.Message)) + ". No build or provider call was made.", objections)) with { Policies = assessment.View };

    private ReleaseResponse SetupRefusal(ReleaseSubmitRequest request, string message)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            HashSet<string> repositories = model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository" && r.Body.TryGetProperty("location", out JsonElement location) &&
                location.TryGetProperty("github", out JsonElement github) && string.Equals(github.GetString(), request.Repository, StringComparison.OrdinalIgnoreCase)).SelectMany(model.Chain).ToHashSet(StringComparer.Ordinal);
            ReleasePolicyAssessment assessment = assessPolicies(model, request.Product, request.Version, request.Commit, repositories);
            IReadOnlyList<LivingDocuments.State> documents = LivingDocuments.AtReleaseCommit(model, request.Product, request.Commit, repositories);
            var compared = CompareLivingDocuments(model, documents, repositories);
            List<FindingDto> objections = [new("error", "release.setup", "release", message), .. assessment.Objections,
                .. compared.Changed.Concat(documents.Where(d => d.Closes).Select(d => d.Describe())).Select(m => new FindingDto("warning", "document.release-hold", "release-documents", m))];
            return GateRefusal("release " + request.Version, "not-started", assessment, objections);
        }
    }

    /// <summary>
    /// Reads each refuse-level repository document at its selected commit, where a clone is registered here: those whose bytes
    /// differ from their latest review, and what was compared or could not be. Covered paths use the same selected commit.
    /// </summary>
    private static (List<string> Changed, List<string> Compared) CompareLivingDocuments(EstateModel model, IReadOnlyList<LivingDocuments.State> documents, IReadOnlySet<string> releaseRepositories)
    {
        var changed = new List<string>();
        var compared = new List<string>();
        foreach (LivingDocuments.State document in documents.Where(d => d.Repository is not null && (d.DeclaresRefuse || GeneratedDocumentContract.RequiresFreshCheck(model, d.Declaration))))
        {
            if (document.Generation is { } generation)
            {
                if (generation.Current) compared.Add($"{document.Path} at {generation.Commit}: every generated member equals its verified sources");
                else changed.AddRange(generation.Problems.Select(p => $"{document.Path}: {p}"));
                continue;
            }

            if (GeneratedDocumentContract.UsesStrictRegions(model, document.Declaration))
            {
                string? regionCommit = document.ComparisonCommit;
                IReadOnlyList<string> failures = CompareRegionsAtCommit(model, document.Declaration, regionCommit);
                if (failures.Count != 0) { changed.AddRange(failures.Select(p => document.Path + ": " + p)); continue; }
                compared.Add(document.Path + " at " + regionCommit + ": every generated region equals its verified sources");
                if (!LivingDocuments.Triggers(document.Declaration).Any(t => t.Level == "refuse")) continue;
            }

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

    private static IReadOnlyList<string> CompareRegionsAtCommit(EstateModel model, RecordEnvelope declaration, string? commit)
    {
        try
        {
            IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(declaration, "source"));
            byte[]? bytes = commit is not null && source?.Resolve(model, declaration, commit) == commit ? source.Read(model, declaration, commit) : null;
            return bytes is null ? ["The exact generated-region source commit is unavailable."]
                : DocumentChecks.Generated(model, declaration, new System.Text.UTF8Encoding(false, true).GetString(bytes), commit);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { return ["The exact generated-region comparison could not complete safely."]; }
    }

    private static (List<string> Changed, List<string> Compared) ReleaseDocumentChecks(EstateModel model, string product, string? commit, ProviderDescriptor provider, string destination)
    {
        IReadOnlySet<string> repositories = ReleaseRepositories(model, provider, destination);
        // Recheck inside the admission boundary: a declaration may have arrived while the provider was being resolved.
        IReadOnlyList<LivingDocuments.State> documents = LivingDocuments.AtReleaseCommit(model, product, commit, repositories);
        (List<string> changed, List<string> compared) = CompareLivingDocuments(model, documents, repositories);
        if (commit is null && NeedsReleaseCommit(model, product, repositories))
            changed.Add("The exact release commit is unavailable for the product's living documents; prepare its exact commit and submit it explicitly");
        foreach (LivingDocuments.State document in documents.Where(d => d.Repository is null && d.Generation is null && GeneratedDocumentContract.RequiresFreshCheck(model, d.Declaration)))
        {
            try
            {
                string file = GeneratedDocumentVerification.SafeFile(model.Root, document.Path);
                if (!File.Exists(file)) changed.Add(document.Path + ": required generated regions are missing");
                else changed.AddRange(DocumentChecks.Generated(model, document.Declaration, File.ReadAllText(file)).Select(p => document.Path + ": " + p));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            { changed.Add(document.Path + ": required generated regions are unavailable"); }
        }
        changed.AddRange(documents.Where(d => d.Closes).Select(d => d.Describe()));
        return (changed, compared);
    }

    private static bool NeedsReleaseCommit(EstateModel model, string product, IReadOnlySet<string> repositories) =>
        model.Current("living-document").Any(d => EstateModel.BodyString(d, "repository") is string repository && repositories.Contains(repository) &&
            (GeneratedDocumentContract.RequiresFreshCheck(model, d) || (LivingDocuments.NamesProduct(model, d, product) && LivingDocuments.Triggers(d).Any(t => t.Level == "refuse"))));

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
            return SetupRefusal(request, $"No release provider is selected: product {request.Product} declares no release destination and the request names no provider. Declare the destination on the product record or name the provider; nothing was recorded.");
        }

        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null)
        {
            return SetupRefusal(request, $"The release provider '{providerId}' has no implementation on the runtime's machine; nothing selected another and nothing was recorded. Available: {string.Join(", ", Providers.ReleaseProviders.Select(p => p.Provider).DefaultIfEmpty("none"))}.");
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
            return SetupRefusal(request, $"The provider '{providerId}' needs the settings {string.Join(", ", missing)}; neither the request nor product {request.Product}'s declared destination gives them, and nothing was recorded.");
        }

        string destination = destinationSetting is null ? providerId : settings[destinationSetting];
        string identity = ReleaseFlow.Identity(providerId, destination, request.Version);
        string? releaseCommit = request.Commit ?? (settings.TryGetValue("ref", out string? exactRef) && Patterns.Commit.IsMatch(exactRef) ? exactRef : null);
        List<string> basisEvidence = [];
        List<RecordReference> excluded = [];
        string previousId;
        JsonElement previousBody;
        ReleasePolicyAssessment admitted;
        // All gates run before the durable claim. Resolving a provider ref is not part of admission.
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? current = CurrentOperation(identity);
            string? status = current is null ? null : EstateModel.BodyString(current, "status");
            JsonObject intentBody = ReleaseFlow.IntentBody(providerId, destination, settings, request.Version, request.Product);
            JsonObject claimBody = current is null ? intentBody : ReleaseFlow.RetryIntent(current.Body, intentBody, string.IsNullOrWhiteSpace(request.Reconciliation) ? "Pending admission." : request.Reconciliation);
            if (current is not null) claimBody["dispatchedAt"] = RecordParser.FormatTimestamp(now());
            var first = Admission(model, request, releaseCommit, provider.Descriptor, destination, claimBody, current?.Id);
            string? identityProblem = releaseDispatches.Contains(identity) ? "A dispatch is still in progress; observe and reconcile it before any retry." : status switch
            {
                "observed-success" => "This release was already observed as built; a corrective release is a new version.",
                "not-started" => "The intent has no dispatch receipt; its effect is unknown. Observe and reconcile it before any retry.",
                "dispatched" or "uncertain" => "The operation is unresolved; observe and reconcile it before any retry.",
                "known-failure" or "canceled" when string.IsNullOrWhiteSpace(request.Reconciliation) => "Retry only with a reconciliation stating how the earlier attempt was settled under this identity.",
                _ => null,
            };
            if (identityProblem is not null) first.Objections.Add(new("error", "release.identity", identity, identityProblem));
            if (first.Objections.Count > 0) return GateRefusal(identity, status is "not-started" ? "uncertain" : status ?? "not-started", first.Assessment, first.Objections);
            boundary("prepared");
            var fresh = Admission(session.Load(), request, releaseCommit, provider.Descriptor, destination, claimBody, current?.Id);
            if (fresh.Objections.Count > 0) return GateRefusal(identity, "not-started", fresh.Assessment, fresh.Objections);
            admitted = fresh.Assessment;
            Retain(first.Assessment, first.Compared, claimBody, excluded, basisEvidence);
            if (!first.Assessment.HasSameInputsAndFindings(admitted)) Retain(admitted, fresh.Compared, claimBody, excluded, basisEvidence);
            if (fresh.Compared.Count > 0) claimBody["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Living documents of the product at their repository commits: " + string.Join("; ", fresh.Compared) + "." });
            WriteOutcome claim = recordAccess.WriteLocked(new("operation", request.By, Element(claimBody), new("product", request.Product, null), current?.Id, now()), session.Load());
            if (!claim.Ok) return ReleaseProblem(identity, status ?? "not-started", claim.Problem!) with { Policies = admitted.View, BasisEvidence = basisEvidence };
            previousId = claim.Written!.Id;
            RecordEnvelope stored = session.Load().ById[previousId];
            excluded.Add(stored.Reference);
            previousBody = stored.Body;
            releaseDispatches.Add(identity);
        }

        try
        {
            boundary("before-dispatch");
            lock (gate)
            {
                EstateModel currentModel = session.Load();
                HashSet<string> ownPaths = excluded.Select(e => currentModel.ById[e.Id].ExpectedPath).ToHashSet(StringComparer.Ordinal);
                EstateModel basisModel = EstateLoader.Load(session.Root, session.Files().Where(f => !ownPaths.Contains(f.Path)).ToArray());
                var fresh = Admission(basisModel, request, releaseCommit, provider.Descriptor, destination, JsonNode.Parse(previousBody.GetRawText())!.AsObject(),
                    currentModel.ById[previousId].Supersedes, probe: false);
                if (recordAccess.RequestDeviceRefusal(currentModel) is { } denied) fresh.Objections.AddRange(denied.Findings.Count > 0 ? denied.Findings : [new("error", "release.device", identity, denied.Message)]);
                if (!currentModel.IsCurrent(currentModel.ById[previousId])) fresh.Objections.Add(new("error", "release.intent-changed", identity, "The admitted intent is no longer current; reconcile its identity."));
                JsonObject authorityProbe = ReleaseFlow.Canceled(previousBody, "Validate the current acting principal before dispatch.");
                (AuthoredRecord? probeRecord, string? signerRefusal) = recordAccess.AuthorLocked(new("operation", request.By, Element(authorityProbe), new("product", request.Product, null), previousId, now()), currentModel);
                if (signerRefusal is not null || currentModel.Manifest?.RecordFormat >= 3 && probeRecord?.Envelope.Signatures.Count is not > 0)
                    fresh.Objections.Add(new("error", "release.signature", identity, signerRefusal ?? "The acting principal no longer has an admitted held signing device; reconcile the intent."));
                if (probeRecord is not null) fresh.Objections.AddRange(recordAccess.LoadWith([probeRecord]).Findings.Where(f => f.Severity == Severity.Error).Select(f => new FindingDto("error", f.Code, f.Location, f.Message)));
                JsonObject checkedBody = JsonNode.Parse(previousBody.GetRawText())!.AsObject();
                if (!admitted.HasSameInputsAndFindings(fresh.Assessment)) Retain(fresh.Assessment, fresh.Compared, checkedBody, excluded, basisEvidence);
                admitted = fresh.Assessment;
                if (fresh.Objections.Count > 0)
                {
                    JsonObject canceled = ReleaseFlow.Canceled(Element(checkedBody), "Admission changed before dispatch; no provider was called. " + string.Join(" | ", fresh.Objections.Select(f => f.Message)));
                    WriteOutcome settled = signerRefusal is not null || currentModel.Manifest?.RecordFormat >= 3 && probeRecord?.Envelope.Signatures.Count is not > 0
                        ? WriteOutcome.Fail(new("refused", signerRefusal ?? "No admitted held signer remains to record cancellation.", []))
                        : recordAccess.WriteLocked(new("operation", request.By, Element(canceled), new("product", request.Product, null), previousId, now()), currentModel);
                    ReleaseResponse refusal = GateRefusal(identity, settled.Ok ? "canceled" : "not-started", admitted, fresh.Objections);
                    return refusal with { Operation = settled.Written, BasisEvidence = basisEvidence, Message = refusal.Message + (settled.Ok ? " The intent was canceled before effects." : " The intent could not be canceled; reconcile it before retrying. " + settled.Problem!.Message) };
                }
                // A changed pre-dispatch basis must survive a crash before the provider returns a receipt.
                if (checkedBody.ToJsonString() != JsonNode.Parse(previousBody.GetRawText())!.ToJsonString())
                {
                    checkedBody["status"] = "uncertain";
                    checkedBody["dispatchedAt"] = RecordParser.FormatTimestamp(now());
                    checkedBody["reconciliation"] = "Preparation re-evaluated before dispatch; this identity is reserved and no provider has yet been called.";
                    WriteOutcome checkedClaim = recordAccess.WriteLocked(new("operation", request.By, Element(checkedBody), new("product", request.Product, null), previousId, now()), currentModel);
                    if (!checkedClaim.Ok) return ReleaseProblem(identity, "not-started", checkedClaim.Problem!) with { Policies = admitted.View, BasisEvidence = basisEvidence };
                    previousId = checkedClaim.Written!.Id;
                    excluded.Add(session.Load().ById[previousId].Reference);
                }
                previousBody = Element(checkedBody);
            }
            // The provider is called outside the gate: a dispatch may wait for the host, and every other operation must keep answering.
            JsonObject body;
            try
            {
                DispatchReceipt receipt = provider.Dispatch(new DispatchRequest(destination, settings, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = request.Version }, releaseCommit));
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

            WriteOutcome revision = WriteRecord(new NewRecordRequest("operation", request.By, Element(body), $"product:{request.Product}", previousId));
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
            return new ReleaseResponse(identity, newStatus, message, newStatus == "dispatched", revision.Written, Run: run, Policies: admitted.View, BasisEvidence: basisEvidence);
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

                WriteOutcome canceled = WriteRecord(new NewRecordRequest("operation", request.By, Element(ReleaseFlow.Canceled(body, request.Cancel)), $"product:{product}", current.Id));
                return canceled.Ok ? new ReleaseResponse(identity, "canceled", $"{identity}: canceled; {request.Cancel}", true, canceled.Written) : ReleaseProblem(identity, status, canceled.Problem!);
            }

            if (status == "not-started")
            {
                // An intent without a receipt is not proof that no effect happened. Persist that uncertainty before observing it.
                body = Element(ReleaseFlow.AfterUncertainty(body, "The intent has no dispatch receipt; the external effect is unknown and must be observed and reconciled."));
                WriteOutcome uncertain = WriteRecord(new NewRecordRequest("operation", request.By, body, $"product:{product}", current.Id));
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
                ReleasePolicyAssessment? observedPolicies = null;
                List<string> observedBasis = [];
                if (revisionBody["status"]?.GetValue<string>() == "observed-success")
                {
                    string? built = run?.HeadSha ?? release?.TargetCommit;
                    if (built is null || !Patterns.Commit.IsMatch(built))
                    {
                        return ReleaseProblem(identity, status, "refused", "The release was observed but its exact commit is unknown; the candidate record needs the commit the run built.");
                    }

                    lock (gate)
                    {
                        EstateModel model = session.Load();
                        (List<string> changed, List<string> compared) = ReleaseDocumentChecks(model, product, built, provider.Descriptor, destination);
                        observedPolicies = assessPolicies(model, product, request.Version, built, ReleaseRepositories(model, provider.Descriptor, destination));
                        List<FindingDto> objections = [.. observedPolicies.Objections, .. changed.Select(message => new FindingDto("warning", "document.release-hold", "release-documents", message))];
                        if (objections.Count > 0)
                        {
                            return ReleaseProblem(identity, status, new Problem("refused", $"Release {request.Version} waits at built commit {built}: {string.Join(" | ", objections.Select(o => o.Message))}. No candidate was recorded; the observed effect remains under its original identity.", objections)) with { Policies = observedPolicies.View };
                        }
                        Retain(observedPolicies, compared, revisionBody, [], observedBasis);
                    }
                }

                WriteOutcome revision = WriteRecord(new NewRecordRequest("operation", request.By, Element(revisionBody), $"product:{product}", current.Id));
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
                JsonObject candidateBody = ReleaseFlow.CandidateBody(request.Version, destination, commit, release, evidence, request.Qualified ?? [], request.Signing, request.Limitations ?? [], profile, checksums, formats);
                foreach (string hash in observedBasis) candidateBody["evidence"]!.AsArray().Insert(0, new JsonObject { ["summary"] = "Exact policy and duty basis at observed candidate admission.", ["hash"] = hash });
                WriteOutcome candidate = WriteRecord(new NewRecordRequest("release", request.By, Element(candidateBody), $"product:{product}"));
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
        WriteOutcome published = WriteRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.PublishedBody(candidate.Envelope.GetProperty("body"), release)), $"product:{product}", recorded.Id, Expected: candidate.Hash));
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
