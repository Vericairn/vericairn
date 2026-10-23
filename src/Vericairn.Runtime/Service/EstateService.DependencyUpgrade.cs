// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private bool deferredUpgrade;

    // An embedded upgrade must not create a live cache just to refuse an unverified release.
    internal static EstateService OpenForUpgrade(string root) => new(Store.EstateSession.Open(root, readOnly: true)) { deferredUpgrade = true };

    private void EnableUpgradeWrites()
    {
        if (!deferredUpgrade) return;
        session.EnableWrites();
        deferredUpgrade = false;
    }

    // Tests supply the target build's formats without manufacturing an installed binary.
    internal ReleaseBuild? DependencyBuild { get; set; }
    internal string DependencyPlatform { get; set; } = ReleaseBuild.Platform;

    private (UpgradePlan Plan, AuthoredRecord? Observation) PlanUpgrade(EstateModel model, UpgradeRequest request)
    {
        // Preserve the first-party publication and acceptance path, including its unpublished-release refusals.
        if (EstateUpgrade.RecordedTargets(model, request.Version).Count > 0)
            return (EstateUpgrade.Plan(model, request.Version), null);
        if (!string.Equals(model.Manifest!.Repository, VerifiedRelease.Repository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This estate pins a different source repository. Use its published release records or explicitly select the Vericairn repository before observing a Vericairn dependency. Nothing changed.");
        ReleaseBuild build = DependencyBuild ?? ReleaseBuild.Parse(Version);
        if (request.Version != build.Version || request.ClientBuild is not null && request.ClientBuild != build.Identity)
            throw new InvalidOperationException($"The target release and the command line/runtime build must agree. Run Vericairn {request.Version}'s published command line and restart any serving daemon with that build, then retry. Nothing changed.");
        VerifiedRelease verified = request.From is not null
            ? PublishedReleaseVerifier.FromDirectoryAsync(request.From, build, DependencyPlatform).GetAwaiter().GetResult()
            : (Providers.VericairnReleases ?? throw new InvalidOperationException("No published release source is available. Supply --from <directory> or use a host with the anonymous GitHub release source. Nothing changed."))
                .VerifyAsync(build, DependencyPlatform).GetAwaiter().GetResult();
        if (verified.Build.Identity != build.Identity || verified.Build.Writes != build.Writes || !verified.Build.Reads.SequenceEqual(build.Reads)
            || verified.Platform != DependencyPlatform)
            throw new InvalidOperationException("The release source returned evidence for a different build or platform. Nothing changed. Use the target build's release source or supply matching offline evidence.");
        JsonElement body = verified.ObservationBody();
        JsonElement facts = body.GetProperty("facts");
        RecordEnvelope? previous = model.Current("observation").Where(VericairnReleaseObservation.Is)
            .FirstOrDefault(r => r.Body.GetProperty("facts").GetProperty("version").GetString() == request.Version);
        if (previous is not null)
        {
            JsonElement recorded = previous.Body.GetProperty("facts");
            string[] identity = ["repository", "version", "tag", "commit", "checksumsHash", "artifactNames", "artifactPlatforms", "artifactHashes", "reads", "writes"];
            if (identity.Any(key => !JsonElement.DeepEquals(recorded.GetProperty(key), facts.GetProperty(key))))
                throw new InvalidOperationException("The published release differs from this estate's earlier observation of that version. Nothing changed. Investigate the changed tag, checksum file or target build before choosing a release.");
            return (EstateUpgrade.Plan(model, previous), null);
        }
        string by = Ulid.IsValid(request.By) ? request.By! : EstateModel.BodyString(model.CurrentEstate!, "operator")!;
        AuthoredRecord observation = RecordAuthor.Create(new RecordDraft("observation", by, body,
            model.CurrentEstate!.Reference, At: verified.ObservedAt, Format: model.Manifest.RecordFormat));
        return (EstateUpgrade.Plan(model, observation.Envelope), observation);
    }
}
