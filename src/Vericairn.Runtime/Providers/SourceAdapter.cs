// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Providers;

/// <summary>One commit in a repository's recent history.</summary>
/// <param name="Commit">Commit id.</param>
/// <param name="At">Committer time.</param>
/// <param name="Summary">First line of the message.</param>
public sealed record RecentCommit(string Commit, string At, string Summary);

/// <summary>What a read-only inspection of a repository observed, with no machine path in it.</summary>
/// <param name="Commit">HEAD commit.</param>
/// <param name="Branch">Current branch, or null when detached.</param>
/// <param name="Remote">The origin remote's URL, or null when there is none.</param>
/// <param name="RootCommit">The first commit: the identity of a repository without a remote.</param>
/// <param name="Clean">True when nothing is modified, staged or untracked.</param>
/// <param name="Modified">Tracked files with changes, staged or not.</param>
/// <param name="Untracked">Untracked files.</param>
/// <param name="Tags">Tags, sorted.</param>
/// <param name="Recent">Most recent commits, newest first, at most ten.</param>
/// <param name="AsOf">When the observation was made.</param>
public sealed record RepositoryObservation(string Commit, string? Branch, string? Remote, string RootCommit, bool Clean, int Modified, int Untracked, IReadOnlyList<string> Tags, IReadOnlyList<RecentCommit> Recent, string AsOf);

/// <summary>A local observation used by release admission, never proof of a remote's current state.</summary>
/// <param name="Clean">Whether staged, tracked and untracked changes are absent.</param>
/// <param name="DefaultBranch">Default branch named by the clone's origin HEAD, or null when unknown.</param>
/// <param name="DefaultHead">Commit of the local remote-tracking default branch, or null when unavailable.</param>
public sealed record ReleaseSourceObservation(bool Clean, string? DefaultBranch, string? DefaultHead);

/// <summary>Exact current bytes of one changed design file, identified without a machine path.</summary>
/// <param name="Path">Repository-relative design path.</param>
/// <param name="Hash">SHA-256 of the current file bytes.</param>
public sealed record DesignFileChange(string Path, string Hash);

/// <summary>A local design change set, including committed, staged, unstaged and untracked files.</summary>
/// <param name="BaseCommit">Exact local base commit.</param>
/// <param name="Head">Exact HEAD at observation.</param>
/// <param name="Documents">Complete changed design-file membership and current content hashes.</param>
public sealed record DesignChangeSet(string BaseCommit, string Head, IReadOnlyList<DesignFileChange> Documents);

/// <summary>One regular file of a repository at an exact commit, with its exact bytes.</summary>
/// <param name="Path">Repository-relative path with '/' separators.</param>
/// <param name="Bytes">The blob's bytes.</param>
public sealed record SourceFile(string Path, byte[] Bytes);

/// <summary>A record file deleted in a repository's history.</summary>
/// <param name="Path">Repository-relative path under records/.</param>
/// <param name="Commit">The newest commit, reachable from the checked-out one, that deleted it.</param>
public sealed record RecordDeletion(string Path, string Commit);

/// <summary>Record files deleted in the checked-out commit's reachable history (design/signed-history.md § 7).</summary>
/// <param name="Shallow">True when the clone is shallow, so its history is incomplete and nothing is listed.</param>
/// <param name="Deleted">Each deleted record file with the newest commit that deleted it, sorted by path.</param>
public sealed record RecordDeletions(bool Shallow, IReadOnlyList<RecordDeletion> Deleted);

/// <summary>The commit that added each record file, as the checked-out commit's history shows it (design/signed-history.md § 10).</summary>
/// <param name="Shallow">True when the clone is shallow, so its history is incomplete and nothing is listed.</param>
/// <param name="Added">Each record file's repository-relative path under records/, with the oldest reachable commit that added it.</param>
public sealed record RecordOrigins(bool Shallow, IReadOnlyDictionary<string, string> Added)
{
    /// <summary>When each adding commit was committed, by the same path; empty when the adapter does not say.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> AddedAt { get; init; } = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
}

/// <summary>
/// An estate's main line (design/signed-history.md § 7): the local branch its history is judged through whatever is checked out, and
/// that branch's tip.
/// </summary>
/// <param name="Branch">The branch: the one the clone's origin HEAD names when held locally, else main, else master.</param>
/// <param name="Commit">The branch's tip commit.</param>
public sealed record EstateMainLine(string Branch, string Commit);

/// <summary>
/// The git adapter contract, version 1: read-only observation and optional isolated worker workspaces. It preserves uncommitted
/// work and never publishes; integration and remote publication are provider operations, not adapter ones.
/// </summary>
public interface ISourceAdapter
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.adapter.git/1";

    /// <summary>The kind of source it observes, which the registry keys it by: git for every implementation of this contract.</summary>
    string Kind => "git";

    /// <summary>What this implementation is.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>True when the path is inside a repository with at least one commit.</summary>
    bool IsRepository(string path);

    /// <summary>Observes a repository without changing it; throws <see cref="ProviderRefusedException"/> when the path is not a repository with a commit.</summary>
    RepositoryObservation Inspect(string path);

    /// <summary>Reads cleanliness and the locally known default branch head without fetching or modifying the clone. Null when unsupported or unavailable.</summary>
    ReleaseSourceObservation? InspectRelease(string path) => null;

    /// <summary>Observe design files changed from a full local base commit, without fetching or modifying the repository. Null means unsupported or unavailable.</summary>
    DesignChangeSet? InspectDesignChanges(string root, string baseCommit) => null;

    /// <summary>
    /// Reads a regular file at its exact SHA-256 hash from this repository's local history, without fetching or changing it.
    /// Returns null for unsupported, shallow, missing or unreadable history, or when no revision matches.
    /// </summary>
    byte[]? ReadFileAtHash(string root, string relativePath, string hash) => null;

    /// <summary>Reads the bytes selected by a document pin's hash mode. Implementations that only support whole files return null for other modes.</summary>
    byte[]? ReadFileAtHash(string root, string relativePath, string hash, string? hashMode) =>
        hashMode is null ? ReadFileAtHash(root, relativePath, hash) : null;

    /// <summary>
    /// Unexpected top-level estate entries that repository-owned ignore rules exclude and the index does not track,
    /// including descendants. Never uses machine-wide ignore rules or changes the repository.
    /// Null means unavailable or unsupported; without an observation the estate checks every entry.
    /// </summary>
    IReadOnlySet<string>? IgnoredEstateEntries(string root) => null;

    /// <summary>
    /// The regular files under the given top-level directories at an exact commit of this repository's local history, read without
    /// checkout, fetch or change. Null when unsupported, when the commit is absent, or when the history is shallow or unreadable.
    /// </summary>
    IReadOnlyList<SourceFile>? ReadTreeAtCommit(string root, string commit, IReadOnlyList<string> directories) => null;

    /// <summary>
    /// The record files deleted in the checked-out commit's reachable history, merges included, each with the newest commit that deleted
    /// it, without fetching or changing the repository; a shallow clone reports itself and lists nothing. Null means unavailable or
    /// unsupported.
    /// </summary>
    RecordDeletions? DeletedRecordFiles(string root) => null;

    /// <summary>
    /// The commit that added each record file in the checked-out commit's reachable history, the oldest when several did, without fetching
    /// or changing the repository; a shallow clone reports itself and lists nothing. Null means unavailable or unsupported.
    /// </summary>
    RecordOrigins? RecordFileOrigins(string root) => null;

    /// <summary>
    /// The estate's main line: the local branch the clone's origin HEAD names when it is held locally, else main, else master, and its tip,
    /// whatever is checked out; read without fetching or changing the repository. Null when there is none or it is unavailable.
    /// </summary>
    EstateMainLine? MainLine(string root) => null;

    /// <summary>A file's exact bytes at a commit, without fetching or changing the repository; null when absent or unavailable.</summary>
    byte[]? FileAtCommit(string root, string commit, string path) => null;

    /// <summary>
    /// The adapter's isolated workspace operations, including recovery, scope observation and local retention after settlement.
    /// Null means workspace preparation is unsupported. The implementation must preserve the workspace contract's isolation
    /// and never load agent-controlled configuration while observing or retaining work.
    /// </summary>
    IWorkspaceProvider? Workspaces => null;
}

/// <summary>A source adapter that replays recorded observations: a genuinely different implementation of the contract for conformance tests and offline fixtures; it qualifies no live repository.</summary>
public sealed class RecordedSourceAdapter : ISourceAdapter
{
    private readonly IReadOnlyDictionary<string, RepositoryObservation> observations;

    /// <summary>Creates an adapter that answers for the given paths only.</summary>
    public RecordedSourceAdapter(IReadOnlyDictionary<string, RepositoryObservation> observations)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("git-recorded", ISourceAdapter.Contract, "implemented", ["inspect"], ["Replays recorded observations; it observes no real repository."]);

    /// <inheritdoc />
    public bool IsRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return observations.ContainsKey(Path.GetFullPath(path));
    }

    /// <inheritdoc />
    public RepositoryObservation Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return observations.TryGetValue(Path.GetFullPath(path), out RepositoryObservation? observation)
            ? observation with { AsOf = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) }
            : throw new ProviderRefusedException($"{Path.GetFileName(path)} is not a recorded repository.");
    }
}
