// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Release;

/// <summary>The record formats a release reads and the newest it writes, as its release record states them.</summary>
/// <param name="Reads">Every format it reads, oldest first.</param>
/// <param name="Writes">The newest format it writes; an estate gets its own format, never one beyond this.</param>
public sealed record ReleaseRecordFormat(IReadOnlyList<long> Reads, long Writes)
{
    /// <summary>The formats of this build: what a runtime records for a release whose draft states none.</summary>
    public static ReleaseRecordFormat ThisBuild => new(RecordFormats.Reads, RecordFormats.Current);

    /// <summary>The formats for a person, for example "reads 1, 2; writes 2".</summary>
    public string Describe() => $"reads {string.Join(", ", Reads.Select(f => f.ToString(CultureInfo.InvariantCulture)))}; writes {Writes.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The release as intent, then observed effect: an operation record is written before the build is dispatched through the
/// provider the product or the request names, every change of what is known is a superseding revision, the observed draft becomes
/// a candidate release record classified by the product's declared profile (or the generic rule), and publication stays the
/// operator's manual step that is observed afterwards. Nothing here knows a particular product or provider.
/// </summary>
public static class ReleaseFlow
{
    /// <summary>The action recorded on release operations.</summary>
    public const string DispatchAction = "dispatch-release-workflow";

    /// <summary>The platform identifiers a release's artifacts can name.</summary>
    public static readonly string[] Platforms = ["linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64"];

    /// <summary>The original identity of releasing a version to a destination through a provider; every retry reconciles under it.</summary>
    public static string Identity(string provider, string destination, string version) => $"{provider}:{destination}:v{version}";

    /// <summary>The tag a version is released under.</summary>
    public static string Tag(string version) => "v" + version;

    /// <summary>The format of a documentation set's manifest, which states the record formats of the build it documents.</summary>
    public const string DocumentationManifest = "vericairn.docs/1";

    /// <summary>The largest documentation archive the observer reads for the record formats its manifest states.</summary>
    public const long DocumentationLimit = 64L * 1024 * 1024;

    /// <summary>
    /// The record formats a release states in the documentation set it carries: the archive's manifest of format vericairn.docs/1 for
    /// the same version (<c>&lt;version&gt;/manifest.json</c>, or <c>manifest.json</c> at its root), whose <c>recordFormat</c> the
    /// product's build tooling writes from the build. Null when the archive states none, as the sets of releases before 0.6.0 do, with
    /// what the archive lacks, worded to follow its name.
    /// </summary>
    public static ReleaseRecordFormat? DocumentedRecordFormat(byte[] archive, string version, out string missing)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(archive, writable: false), ZipArchiveMode.Read);
            ZipArchiveEntry? entry = zip.GetEntry(version + "/manifest.json") ?? zip.GetEntry("manifest.json");
            if (entry is null || entry.Length > 1024 * 1024)
            {
                missing = "holds no manifest.json of its set";
                return null;
            }

            using Stream content = entry.Open();
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement manifest = document.RootElement;
            if (manifest.ValueKind != JsonValueKind.Object || Text(manifest, "format") != DocumentationManifest || Text(manifest, "version") != version)
            {
                missing = $"holds no {DocumentationManifest} manifest of version {version}";
                return null;
            }

            if (!manifest.TryGetProperty("recordFormat", out JsonElement format))
            {
                missing = "has a manifest that states none";
                return null;
            }

            List<long>? reads = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("reads", out JsonElement r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0
                ? r.EnumerateArray().Select(f => f.ValueKind == JsonValueKind.Number && f.TryGetInt64(out long n) && n is >= 1 and <= 1000 ? n : 0).ToList()
                : null;
            long writes = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("writes", out JsonElement w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt64(out long v) ? v : 0;
            if (reads is null || reads.Contains(0) || reads.Distinct().Count() != reads.Count || !reads.Contains(writes))
            {
                missing = "has a manifest whose recordFormat is malformed (reads: distinct formats from 1; writes: one of them)";
                return null;
            }

            missing = string.Empty;
            return new ReleaseRecordFormat([.. reads.Order()], writes);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            missing = "is not a zip archive holding a readable manifest";
            return null;
        }
    }

    /// <summary>The intent, recorded before any dispatch: the provider, the destination, every setting it runs with, the version and the product.</summary>
    public static JsonObject IntentBody(string provider, string destination, IReadOnlyDictionary<string, string> settings, string version, string product)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(settings);
        var target = new JsonObject();
        foreach ((string name, string value) in settings.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            target[name] = value;
        }

        target["version"] = version;
        target["product"] = product;
        string details = string.Join(", ", settings.Where(s => s.Value != destination).OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key} {s.Value}"));
        return new JsonObject
        {
            ["provider"] = provider,
            ["action"] = DispatchAction,
            ["identity"] = Identity(provider, destination, version),
            ["intent"] = $"Build release {version} of {destination} through {provider}{(details.Length == 0 ? string.Empty : " (" + details + ")")} into a draft; publication stays the operator's manual step.",
            ["target"] = target,
            ["status"] = "not-started",
            ["evidence"] = new JsonArray(),
        };
    }

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

    /// <summary>The revision after the run and the release were observed: success only when the run succeeded and the draft is complete by the product's profile.</summary>
    public static JsonObject AfterObservation(JsonElement previous, RunObservation? run, ReleaseObservation? release, string version, ReleaseProfile profile, string checksums)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (run is not null && run.Status == "completed" && run.Conclusion != "success")
        {
            return Observed(previous, "known-failure", $"Run {run.RunId} completed with conclusion {run.Conclusion ?? "none"}; no release was produced.", run.Url, run.RunId);
        }

        if (release is not null && profile.Missing(release, version, checksums).Count == 0 && (run is null || (run.Status == "completed" && run.Conclusion == "success")))
        {
            string complete = profile.DeclaresArtifacts ? "every declared artifact named in its checksum file" : $"every asset named in {checksums}";
            return Observed(previous, "observed-success", $"Draft release {release.Tag} observed with {release.Assets.Count} assets and {complete}{(run is null ? string.Empty : $"; run {run.RunId} succeeded")}.", release.Url, run?.RunId);
        }

        throw new InvalidOperationException("Nothing settled yet: the run is still in progress or the draft release is incomplete.");
    }

    /// <summary>
    /// The candidate release record body from the observed draft, with the record formats the release states, or this build's when
    /// none are given.
    /// </summary>
    public static JsonObject CandidateBody(string version, string repository, string commit, ReleaseObservation release, IReadOnlyList<string> evidence, IReadOnlyList<string> qualified, string signing, IReadOnlyList<string> limitations, ReleaseProfile profile, string checksums, ReleaseRecordFormat? recordFormat = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksums);
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
            if (asset.Name == checksums || asset.Hash is null)
            {
                continue;
            }

            ExpectedArtifact classified = profile.Classify(asset.Name, version);
            artifacts.Add(new JsonObject { ["name"] = asset.Name, ["hash"] = asset.Hash, ["bytes"] = asset.Bytes, ["platform"] = classified.Platform });
            if (classified.Role == "sbom" && sbom is null)
            {
                sbom = asset.Hash;
            }

            if (classified.Role == "notices" && notices is null)
            {
                notices = asset.Hash;
            }
        }

        ReleaseRecordFormat formats = recordFormat ?? ReleaseRecordFormat.ThisBuild;
        var body = new JsonObject
        {
            ["version"] = version,
            ["repository"] = repository,
            ["commit"] = commit,
            ["status"] = "candidate",
            ["artifacts"] = artifacts,
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(formats.Reads.Select(f => (JsonNode)f).ToArray()), ["writes"] = formats.Writes },
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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
