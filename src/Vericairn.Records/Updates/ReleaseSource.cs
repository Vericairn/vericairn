// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Updates;

/// <summary>
/// A source of the latest stable published release. It receives no caller identity, estate data or usage information.
/// The client owns disclosure, the shared daily limit, cached offline knowledge and the off switch; a source only looks up a release.
/// Hosts select an implementation explicitly, outside the foundation.
/// </summary>
public interface IReleaseSource
{
    /// <summary>Returns the latest stable published release, or null when no usable release was observed.</summary>
    Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>A release source that replays a recorded stable tag without any transport; it qualifies no live release host.</summary>
/// <param name="tag">The recorded tag, or null when no release was observed. Invalid or prerelease tags yield no release.</param>
public sealed class RecordedReleaseSource(string? tag) : IReleaseSource
{
    private readonly PublishedRelease? release = PublishedRelease.FromTag(tag);

    /// <inheritdoc />
    public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(release);
    }
}
