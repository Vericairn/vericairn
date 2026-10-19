// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>A request to dispatch the build that produces a release.</summary>
/// <param name="Destination">Where the release lands, named by the provider's destination setting (for GitHub, owner/name).</param>
/// <param name="Settings">The provider's settings for this operation, defaults applied (for GitHub: repository, workflow, ref, checksums).</param>
/// <param name="Inputs">The build's inputs (the version).</param>
/// <param name="ExpectedCommit">Exact commit used for admission. A provider must refuse before starting a build if its ref no longer resolves to this commit.</param>
public sealed record DispatchRequest(string Destination, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Inputs, string? ExpectedCommit = null)
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
    /// establish it. Admission uses the caller's prepared exact commit without invoking this method; a provider may use it
    /// inside dispatch to verify ExpectedCommit before an effect. The observed run's commit is checked again.
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
