// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Updates;

namespace Vericairn.Providers.GitHub;

/// <summary>
/// Compatibility entry point for the release source. Client hosts compose <see cref="Releases.GitHubReleaseSource"/>
/// directly so that update checks do not bring the runtime providers into their dependency closure.
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    private readonly Releases.GitHubReleaseSource source;

    /// <summary>Uses the supplied HTTP transport for the anonymous stable-release lookup.</summary>
    public GitHubReleaseSource(HttpClient client) : this(new Releases.GitHubReleaseSource(client)) { }

    private GitHubReleaseSource(Releases.GitHubReleaseSource source) => this.source = source;

    internal const string Endpoint = Releases.GitHubReleaseSource.Endpoint;

    internal static SocketsHttpHandler CreateHandler() => Releases.GitHubReleaseSource.CreateHandler();

    /// <summary>The shipped anonymous transport, with a two-second timeout and bounded response size.</summary>
    public static GitHubReleaseSource Default { get; } = new(Releases.GitHubReleaseSource.Default);

    /// <inheritdoc />
    public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken) => source.LatestAsync(cancellationToken);

    /// <inheritdoc />
    public Task<VerifiedRelease> VerifyAsync(ReleaseBuild build, string platform, CancellationToken cancellationToken = default) => source.VerifyAsync(build, platform, cancellationToken);
}
