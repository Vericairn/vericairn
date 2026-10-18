// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.TestSupport;

/// <summary>An in-memory release provider for conformance tests and journeys: it behaves as the GitHub provider would, under the test's control, declares the same settings, and qualifies no live service.</summary>
internal sealed class FakeReleaseProvider : IReleaseProvider
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
