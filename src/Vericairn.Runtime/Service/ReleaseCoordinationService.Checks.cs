// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

internal sealed partial class ReleaseCoordinationService
{
    private static void CheckBuild(EstateModel model, ReleaseSubmitRequest request, List<FindingDto> objections)
    {
        if (request.ClientBuild is null && model.Manifest?.RecordFormat < 3) return;
        string runtime = EstateService.Version;
        string? revision = runtime.Split('+').ElementAtOrDefault(1);
        if (request.ClientBuild != runtime || revision is null || !Patterns.Commit.IsMatch(revision))
            objections.Add(new("error", "release.client-build", "release-client", "The submitting client's full build identity is missing, unavailable or differs from the serving runtime."));
    }

    private void CheckSources(EstateModel model, ReleaseSubmitRequest request, string? commit, ProviderDescriptor provider, string destination,
        ReleasePolicyAssessment assessment, List<FindingDto> objections)
    {
        HashSet<string> releaseRepositories = ReleaseRepositories(model, provider, destination);
        if (model.Manifest?.RecordFormat >= 3 && releaseRepositories.Count == 0)
            objections.Add(new("error", "release.repository", "release-source", "No repository resource matches the effective release destination; its default branch head cannot be checked."));
        HashSet<string> reads = assessment.Basis.Repositories.Select(r => r.Identity).Concat(releaseRepositories).ToHashSet(StringComparer.Ordinal);
        foreach (var document in LivingDocuments.AtReleaseCommit(model, request.Product, commit, releaseRepositories))
            if (document.Repository is { } repository) reads.Add(repository);
        if (model.Manifest is not { } manifest) return;
        IReadOnlyDictionary<string, string> clones;
        try { clones = RepositoryClones.All(manifest.Estate); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            objections.Add(new("error", "release.clones", "release-source", "The clone registrations cannot be read."));
            return;
        }
        ISourceAdapter? source = Providers.Source("git");
        foreach (RecordEnvelope resource in model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository"))
        {
            IReadOnlyList<string> chain = model.Chain(resource);
            if (!chain.Any(reads.Contains)) continue;
            bool release = chain.Any(releaseRepositories.Contains);
            string[] paths = chain.Where(clones.ContainsKey).Select(id => clones[id]).Distinct(StringComparer.Ordinal).ToArray();
            if (release && paths.Length == 0)
                objections.Add(new("error", "release.clone", resource.Id, "The release repository has no registered clone; its default branch head is unavailable."));
            foreach (string path in paths)
            {
                ReleaseSourceObservation? observed;
                try { observed = source?.InspectRelease(path); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { observed = null; }
                if (observed is null)
                {
                    objections.Add(new("error", "release.clone", resource.Id, "A registered source clone cannot be inspected by the installed source adapter."));
                    continue;
                }
                if (!observed.Clean) objections.Add(new("error", "release.clone-dirty", resource.Id, "A registered clone read by release admission has staged, modified or untracked files."));
                if (release && (commit is null || observed.DefaultHead is null || observed.DefaultHead != commit))
                    objections.Add(new("error", "release.default-head", resource.Id, "The requested commit does not equal the locally known default branch head, or that head is unavailable."));
            }
        }
    }

    private static ReleaseObjection Objection(FindingDto finding)
    {
        int next = finding.Message.IndexOf(" Next: ", StringComparison.Ordinal);
        string action = next >= 0 ? finding.Message[(next + 7)..] : finding.Code switch
        {
            "release.client-build" => "Use a client from the same exact build as the serving runtime; compare their full informational versions and check again.",
            "release.default-head" => "Refresh and verify the registered clone's origin default branch separately, then supply that exact commit and check again. The check never fetches.",
            "release.clone-dirty" => "Commit, stash or remove the changes in every registered clone read by the gate, then check again.",
            "release.clone" or "release.clones" or "release.repository" => "Register a readable clone of the destination repository resource and install its source adapter, then check again.",
            "release.basis-size" or "release.basis-file-size" => "Reduce the retained source selection to fit each object within the estate's object limit, then check again.",
            "release.signature" => "Admit and make available a signing device for the submitting principal before submission. The check itself never signs or requests presence.",
            "release.device" => "Reconnect with an admitted device for the submitting principal and the required connection profile, then check again.",
            "batch.unreadable" or "batch.outputs-incomplete" => "Inspect vericairn reconcile and recover the original batch and generated outputs before checking again; do not repeat its record writes.",
            "document.release-hold" or "release.companions" => "Resolve every listed document comparison or trigger and obtain its required review at the exact revision, then check again.",
            "release.expected" or "release.product" => "Reload the current product revision and supply its exact id and hash, then check again.",
            "release.commit" => "Supply the exact lower-case forty-digit release commit and check again.",
            "release.identity" => "Observe and reconcile the original release identity before retrying; use a new version for an already built release.",
            "release.author" or "release.principal" => "Choose an existing principal with current release authority and check again.",
            "release.setup" or "release.setting" => "Supply the declared release provider and all required destination settings, then check again.",
            _ => "Resolve the reported finding at its named subject, then run the submission check again.",
        };
        return new(finding.Code, finding.Location, finding.Message, action);
    }
}
