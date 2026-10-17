// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Providers;

/// <summary>A request to dispatch the build that produces a release.</summary>
/// <param name="Destination">Where the release lands, named by the provider's destination setting (for GitHub, owner/name).</param>
/// <param name="Settings">The provider's settings for this operation, defaults applied (for GitHub: repository, workflow, ref, checksums).</param>
/// <param name="Inputs">The build's inputs (the version).</param>
public sealed record DispatchRequest(string Destination, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Inputs)
{
    /// <summary>A setting's value, or the fallback when the request does not carry it.</summary>
    public string Setting(string name, string fallback) => Settings.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}

/// <summary>What a dispatch returned: a run id when the run could be identified, otherwise the operation is uncertain.</summary>
/// <param name="RunId">The run, or null.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="Summary">What happened.</param>
/// <param name="At">When.</param>
public sealed record DispatchReceipt(string? RunId, string? Url, string Summary, string At);

/// <summary>A build run as observed at its host.</summary>
/// <param name="RunId">Run id.</param>
/// <param name="Status">queued, in_progress or completed.</param>
/// <param name="Conclusion">success, failure, cancelled or another conclusion once completed.</param>
/// <param name="HeadSha">The commit the run built.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="At">When observed.</param>
/// <param name="Method">How the host ran it, for a verification's method (for GitHub, github-actions).</param>
/// <param name="Environment">Facts about where it ran that the host reports, never a path.</param>
public sealed record RunObservation(string RunId, string Status, string? Conclusion, string HeadSha, string Url, string At, string Method = "workflow-run", IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>One asset of a release as observed, with its hash when the release's checksum file names it.</summary>
/// <param name="Name">File name.</param>
/// <param name="Bytes">Size.</param>
/// <param name="Hash">sha256:hex, or null when no checksum names the asset.</param>
public sealed record ReleaseAsset(string Name, long Bytes, string? Hash);

/// <summary>A release as observed at its destination.</summary>
/// <param name="Tag">Tag.</param>
/// <param name="Draft">Whether it is still a draft.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="TargetCommit">The commit the release was created from, when known.</param>
/// <param name="PublishedAt">When it was published, once it is.</param>
/// <param name="Assets">Assets with hashes.</param>
/// <param name="At">When observed.</param>
public sealed record ReleaseObservation(string Tag, bool Draft, string? Url, string? TargetCommit, string? PublishedAt, IReadOnlyList<ReleaseAsset> Assets, string At);

/// <summary>
/// The release provider contract, version 1: a release is built by a dispatched build on a ref of a destination, observed as a
/// run and as a draft with the checksums of its assets, and published by the operator's own manual step, which the provider
/// names and never performs. Implementations declare their settings and the setting that names the destination; the provider
/// they serve is recorded on every operation, so an operation is always observed through the provider that dispatched it.
/// </summary>
public interface IReleaseProvider
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.provider.release/1";

    /// <summary>The provider this implementation serves, recorded on operations: for example github-releases.</summary>
    string Provider { get; }

    /// <summary>What this implementation is, with its settings and the location keys it serves.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Resolves the build's ref at its destination to an exact commit, without dispatching. Null means this implementation cannot
    /// establish it; a release needing repository document checks is then refused. The observed run's commit is checked again.
    /// </summary>
    string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) => null;

    /// <summary>Dispatches the build; throws <see cref="ProviderRefusedException"/> before any effect, or <see cref="ProviderUncertainException"/> when an effect may have happened.</summary>
    DispatchReceipt Dispatch(DispatchRequest request);

    /// <summary>Observes a run, or null when no such run exists.</summary>
    RunObservation? ObserveRun(string destination, string runId);

    /// <summary>Observes a release by tag with the checksums its settings name, or null when none exists.</summary>
    ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings);

    /// <summary>The operator's manual publication command for a tag; the provider never runs it.</summary>
    string ManualPublicationCommand(string destination, string tag);

    /// <summary>
    /// Reads one asset of a release by its name; null when the release or the asset does not exist, or when the implementation reads
    /// no assets, as one written before this member was added does not. The observer reads the documentation set a draft carries
    /// this way, for the record formats the release states, and holds what it read to the hash the release's checksum file names.
    /// </summary>
    byte[]? ReadAsset(string destination, string tag, string name) => null;
}

/// <summary>An in-memory release provider for conformance tests and journeys: it behaves as the GitHub provider would, under the test's control, declares the same settings, and qualifies no live service.</summary>
public sealed class FakeReleaseProvider : IReleaseProvider
{
    private readonly Dictionary<string, RunObservation> runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReleaseObservation> releases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> assetContents = new(StringComparer.Ordinal);
    private readonly List<DispatchRequest> dispatches = [];
    private int nextRun = 1000;

    /// <summary>How the next dispatch behaves.</summary>
    public enum DispatchBehavior
    {
        /// <summary>The run starts and is identified.</summary>
        Normal,

        /// <summary>Refused before any effect: not logged in.</summary>
        RefuseAuthentication,

        /// <summary>The request may have been sent but no run was identified.</summary>
        Uncertain,
    }

    /// <summary>Behavior of the next dispatch.</summary>
    public DispatchBehavior NextDispatch { get; set; } = DispatchBehavior.Normal;

    /// <summary>The commit the fake runs build.</summary>
    public string HeadSha { get; set; } = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Every dispatch requested.</summary>
    public IReadOnlyList<DispatchRequest> Dispatches => dispatches;

    /// <summary>An in-memory provider serving a provider id (github-releases unless a test names another).</summary>
    public FakeReleaseProvider(string provider = "github-releases")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        Provider = provider;
        Descriptor = new(
            provider + "-fake",
            IReleaseProvider.Contract,
            "implemented",
            ["dispatch", "observe-run", "observe-release"],
            ["Simulates the GitHub provider in memory; qualifies no live service."],
            [
                new ProviderSetting("repository", "Owner/name of the repository whose workflow builds the release.", true),
                new ProviderSetting("workflow", "The workflow file that builds the release into a draft.", false, "release.yml"),
                new ProviderSetting("ref", "The branch or tag the workflow runs on.", false, "main"),
                new ProviderSetting("checksums", "The release asset that lists every other asset's SHA-256.", false, "SHA256SUMS"),
            ],
            "repository",
            ["github"]);
    }

    /// <inheritdoc />
    public string Provider { get; }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; }

    /// <inheritdoc />
    public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) => HeadSha;

    /// <inheritdoc />
    public DispatchReceipt Dispatch(DispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        switch (NextDispatch)
        {
            case DispatchBehavior.RefuseAuthentication:
                NextDispatch = DispatchBehavior.Normal;
                throw new ProviderRefusedException("The GitHub CLI is not logged in on this machine; run 'gh auth login' yourself. Vericairn stores no token.");
            case DispatchBehavior.Uncertain:
                NextDispatch = DispatchBehavior.Normal;
                dispatches.Add(request);
                return new DispatchReceipt(null, null, "The dispatch was sent but no run was identified within thirty seconds; observe the workflow's runs before any retry.", at);
            default:
                dispatches.Add(request);
                string id = (nextRun++).ToString(CultureInfo.InvariantCulture);
                string url = $"https://github.com/{request.Destination}/actions/runs/{id}";
                runs[request.Destination + "#" + id] = new RunObservation(id, "in_progress", null, HeadSha, url, at, "github-actions", new Dictionary<string, string>(StringComparer.Ordinal) { ["runner"] = "github-hosted" });
                string workflow = request.Setting("workflow", "release.yml");
                return new DispatchReceipt(id, url, $"Workflow {workflow} dispatched on {request.Setting("ref", "main")}; run {id} started.", at);
        }
    }

    /// <summary>
    /// Completes a run; on success a draft release appears with its assets and a checksum file. Without names, the assets are
    /// Vericairn's own (the command line and Workbench per platform, the bill of materials, the notices); with names, exactly those,
    /// and an asset the contents name has those bytes, their size and their hash, and can be read.
    /// </summary>
    public void CompleteRun(string repository, string runId, bool success, string version, IReadOnlyList<string>? names = null, string checksums = "SHA256SUMS", IReadOnlyDictionary<string, byte[]>? contents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksums);
        RunObservation run = runs[repository + "#" + runId];
        runs[repository + "#" + runId] = run with { Status = "completed", Conclusion = success ? "success" : "failure", At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
        if (!success)
        {
            return;
        }

        string tag = "v" + version;
        foreach (string stale in assetContents.Keys.Where(k => k.StartsWith(repository + "#" + tag + "#", StringComparison.Ordinal)).ToList())
        {
            assetContents.Remove(stale);
        }

        var assets = new List<ReleaseAsset>();
        if (names is not null)
        {
            int k = 1;
            foreach (string name in names)
            {
                if (contents is not null && contents.TryGetValue(name, out byte[]? content))
                {
                    assetContents[repository + "#" + tag + "#" + name] = content.ToArray();
                    assets.Add(new ReleaseAsset(name, content.Length, ContentHash.Sha256(content)));
                }
                else
                {
                    assets.Add(new ReleaseAsset(name, 100 + k, "sha256:" + new string('c', 60) + k.ToString("D4", CultureInfo.InvariantCulture)));
                }

                k++;
            }

            assets.Add(new ReleaseAsset(checksums, 300, null));
            releases[repository + "#" + tag] = new ReleaseObservation(tag, true, $"https://github.com/{repository}/releases/tag/{tag}", run.HeadSha, null, assets.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
            return;
        }

        int n = 1;
        foreach (string platform in new[] { "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64" })
        {
            string suffix = platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
            assets.Add(new ReleaseAsset($"vericairn-{version}-{platform}{suffix}", 1000 + n, "sha256:" + new string((char)('0' + n), 64)));
            assets.Add(new ReleaseAsset($"vericairn-workbench-{version}-{platform}{suffix}", 2000 + n, "sha256:" + new string('f', 60) + n.ToString("D4", CultureInfo.InvariantCulture)));
            n++;
        }

        assets.Add(new ReleaseAsset($"vericairn-{version}-sbom.cdx.json", 500, "sha256:" + new string('a', 64)));
        assets.Add(new ReleaseAsset($"vericairn-{version}-THIRD-PARTY-NOTICES.md", 400, "sha256:" + new string('b', 64)));
        assets.Add(new ReleaseAsset(checksums, 300, null));
        releases[repository + "#" + tag] = new ReleaseObservation(tag, true, $"https://github.com/{repository}/releases/tag/{tag}", run.HeadSha, null, assets.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
    }

    /// <summary>What the operator's manual step does at GitHub: the draft becomes public.</summary>
    public void Publish(string repository, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ReleaseObservation release = releases[repository + "#" + tag];
        releases[repository + "#" + tag] = release with { Draft = false, PublishedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
    }

    /// <inheritdoc />
    public RunObservation? ObserveRun(string destination, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return runs.TryGetValue(destination + "#" + runId, out RunObservation? run) ? run with { At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) } : null;
    }

    /// <inheritdoc />
    public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(settings);
        return releases.TryGetValue(destination + "#" + tag, out ReleaseObservation? release) ? release with { At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) } : null;
    }

    /// <inheritdoc />
    public string ManualPublicationCommand(string destination, string tag) => $"gh release edit {tag} -R {destination} --draft=false --latest";

    /// <inheritdoc />
    public byte[]? ReadAsset(string destination, string tag, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return assetContents.TryGetValue(destination + "#" + tag + "#" + name, out byte[]? content) ? content.ToArray() : null;
    }
}
