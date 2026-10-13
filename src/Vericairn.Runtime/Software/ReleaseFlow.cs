// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Software;

/// <summary>
/// The release as intent, then observed effect: an operation record is written before the release workflow is dispatched,
/// every change of what is known is a superseding revision, the observed draft becomes a candidate release record, and
/// publication stays the operator's manual step that is observed afterwards.
/// </summary>
public static class ReleaseFlow
{
    /// <summary>The provider id recorded on operations.</summary>
    public const string ProviderId = "github-releases";

    /// <summary>The action recorded on operations.</summary>
    public const string DispatchAction = "dispatch-release-workflow";

    /// <summary>The distributable platforms a release of Vericairn names.</summary>
    public static readonly string[] Platforms = ["linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64"];

    /// <summary>The original identity of releasing a version from a repository; every retry reconciles under it.</summary>
    public static string Identity(string repository, string version) => $"{ProviderId}:{repository}:v{version}";

    /// <summary>The tag a version is released under.</summary>
    public static string Tag(string version) => "v" + version;

    /// <summary>The intent, recorded before any dispatch.</summary>
    public static JsonObject IntentBody(string repository, string workflow, string reference, string version, string product) => new()
    {
        ["provider"] = ProviderId,
        ["action"] = DispatchAction,
        ["identity"] = Identity(repository, version),
        ["intent"] = $"Build release {version} of {repository} from {reference} through {workflow} into a draft GitHub release; publication stays the operator's manual step.",
        ["target"] = new JsonObject { ["repository"] = repository, ["workflow"] = workflow, ["ref"] = reference, ["version"] = version, ["product"] = product },
        ["status"] = "not-started",
        ["evidence"] = new JsonArray(),
    };

    /// <summary>The revision after a dispatch: dispatched when the run was identified, uncertain when it was not.</summary>
    public static JsonObject AfterDispatch(JsonElement previous, DispatchReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        JsonObject body = Copy(previous);
        body["dispatchedAt"] = receipt.At;
        var evidence = body["evidence"]!.AsArray();
        if (receipt.RunId is not null)
        {
            body["status"] = "dispatched";
            body["target"]!.AsObject()["run"] = receipt.RunId;
            evidence.Add(new JsonObject { ["summary"] = receipt.Summary, ["location"] = receipt.Url });
        }
        else
        {
            body["status"] = "uncertain";
            evidence.Add(new JsonObject { ["summary"] = receipt.Summary });
        }

        return body;
    }

    /// <summary>The revision after the provider refused before any effect.</summary>
    public static JsonObject AfterRefusal(JsonElement previous, string reason) => Observed(previous, "known-failure", $"Refused before any dispatch: {reason}", null, null);

    /// <summary>The revision after the provider could not say whether an effect happened.</summary>
    public static JsonObject AfterUncertainty(JsonElement previous, string reason)
    {
        JsonObject body = Copy(previous);
        body["status"] = "uncertain";
        body["dispatchedAt"] = body["dispatchedAt"]?.GetValue<string>() ?? RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = reason });
        return body;
    }

    /// <summary>A retry after an uncertain or failed outcome, dispatched again under the same identity with the reconciliation stated.</summary>
    public static JsonObject Retry(JsonElement previous, DispatchReceipt receipt, string reconciliation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliation);
        JsonObject body = AfterDispatch(previous, receipt);
        body["reconciliation"] = reconciliation;
        body.Remove("observed");
        return body;
    }

    /// <summary>A cancellation before any effect or after reconciliation showed none.</summary>
    public static JsonObject Canceled(JsonElement previous, string reason)
    {
        JsonObject body = Copy(previous);
        body["status"] = "canceled";
        body["reconciliation"] = reason;
        return body;
    }

    /// <summary>The revision after the run and the release were observed: success only when the run succeeded and the draft names every distributable with a hash.</summary>
    public static JsonObject AfterObservation(JsonElement previous, RunObservation? run, ReleaseObservation? release, string version)
    {
        if (run is not null && run.Status == "completed" && run.Conclusion != "success")
        {
            return Observed(previous, "known-failure", $"Run {run.RunId} completed with conclusion {run.Conclusion ?? "none"}; no release was produced.", run.Url, run.RunId);
        }

        if (release is not null && MissingDistributables(release, version).Count == 0 && (run is null || (run.Status == "completed" && run.Conclusion == "success")))
        {
            return Observed(previous, "observed-success", $"Draft release {release.Tag} observed with {release.Assets.Count} assets and checksums for every distributable{(run is null ? string.Empty : $"; run {run.RunId} succeeded")}.", release.Url, run?.RunId);
        }

        throw new InvalidOperationException("Nothing settled yet: the run is still in progress or the draft release is incomplete.");
    }

    /// <summary>The distributables a release of a version must name with hashes, and does not yet.</summary>
    public static IReadOnlyList<string> MissingDistributables(ReleaseObservation release, string version)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Platforms.Select(p => EstateUpgrade.DistributableName(version, p)).Where(name => !release.Assets.Any(a => a.Name == name && a.Hash is not null)).ToList();
    }

    /// <summary>The candidate release record body from the observed draft.</summary>
    public static JsonObject CandidateBody(string version, string repository, string commit, ReleaseObservation release, IReadOnlyList<string> evidence, IReadOnlyList<string> qualified, string signing, IReadOnlyList<string> limitations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(qualified);
        ArgumentException.ThrowIfNullOrWhiteSpace(signing);
        ArgumentNullException.ThrowIfNull(limitations);
        var artifacts = new JsonArray();
        string? sbom = null;
        string? notices = null;
        foreach (ReleaseAsset asset in release.Assets.OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            if (asset.Name == "SHA256SUMS" || asset.Hash is null)
            {
                continue;
            }

            string platform = Platforms.FirstOrDefault(p => EstateUpgrade.IsDistributable(asset.Name, version, p)) ?? "any";
            artifacts.Add(new JsonObject { ["name"] = asset.Name, ["hash"] = asset.Hash, ["bytes"] = asset.Bytes, ["platform"] = platform });
            if (asset.Name == $"vericairn-{version}-sbom.cdx.json")
            {
                sbom = asset.Hash;
            }

            if (asset.Name == $"vericairn-{version}-THIRD-PARTY-NOTICES.md")
            {
                notices = asset.Hash;
            }
        }

        var reads = new JsonArray();
        for (long format = 1; format <= RecordFormats.Latest; format++)
        {
            reads.Add(format);
        }

        var body = new JsonObject
        {
            ["version"] = version,
            ["repository"] = repository,
            ["commit"] = commit,
            ["status"] = "candidate",
            ["artifacts"] = artifacts,
            ["recordFormat"] = new JsonObject { ["reads"] = reads, ["writes"] = RecordFormats.Current },
            ["evidence"] = new JsonArray(evidence.Select(e => (JsonNode)new JsonObject { ["summary"] = e }).ToArray()),
            ["qualified"] = new JsonArray(qualified.Select(q => (JsonNode)q).ToArray()),
            ["signing"] = signing,
            ["limitations"] = new JsonArray(limitations.Select(l => (JsonNode)l).ToArray()),
        };
        if (sbom is not null)
        {
            body["sbom"] = sbom;
        }

        if (notices is not null)
        {
            body["notices"] = notices;
        }

        return body;
    }

    /// <summary>The published revision of a candidate: the same release with the observed publication.</summary>
    public static JsonObject PublishedBody(JsonElement candidate, ReleaseObservation release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Draft || release.PublishedAt is null)
        {
            throw new InvalidOperationException("The release is still a draft; publication is observed, never assumed.");
        }

        JsonObject body = Copy(candidate);
        body["status"] = "published";
        body["publishedAt"] = release.PublishedAt;
        body["url"] = release.Url ?? string.Empty;
        return body;
    }

    private static JsonObject Observed(JsonElement previous, string status, string summary, string? url, string? reference)
    {
        JsonObject body = Copy(previous);
        body["status"] = status;
        var observed = new JsonObject { ["summary"] = summary, ["at"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
        if (url is not null)
        {
            observed["url"] = url;
        }

        if (reference is not null)
        {
            observed["reference"] = reference;
        }

        body["observed"] = observed;
        return body;
    }

    private static JsonObject Copy(JsonElement previous) => JsonNode.Parse(previous.GetRawText())!.AsObject();
}
