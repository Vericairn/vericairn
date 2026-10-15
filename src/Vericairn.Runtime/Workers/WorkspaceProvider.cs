// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>A workspace a run works in: a directory on this machine, on a work branch of a repository, and never the shared clone itself.</summary>
/// <param name="Path">The workspace's directory on this machine; never recorded.</param>
/// <param name="Branch">The work branch it is on.</param>
/// <param name="Commit">The commit it was at once prepared.</param>
/// <param name="Reused">True when an earlier run's workspace on the same branch was taken up again, so an attempt continues where the last one stopped.</param>
public sealed record PreparedWorkspace(string Path, string Branch, string Commit, bool Reused);

/// <summary>A local inventory of workspace files; fingerprints and local configuration never enter a record.</summary>
/// <param name="Files">Repository-relative names and content or link fingerprints.</param>
/// <param name="Problem">Why the inventory is incomplete, when it is.</param>
public sealed record WorkspaceSnapshot(IReadOnlyDictionary<string, string> Files, string? Problem = null);

/// <summary>
/// The workspace contract, version 1 (vericairn.workspace/1): an isolated working tree of a repository on a work branch, made from this
/// machine's registered clone of it and never the clone's own working tree. The workspace stays after the run, for review.
/// </summary>
public interface IWorkspaceProvider
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.workspace/1";

    /// <summary>The kind of repository it prepares workspaces of, which the registry keys it by: git.</summary>
    string Kind => "git";

    /// <summary>What this implementation is.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Prepares the workspace of a work branch at a directory from a clone, the branch made from a base reference (the clone's HEAD
    /// when null) when it does not exist yet, or takes up again the workspace already there on that branch. Throws
    /// <see cref="ProviderRefusedException"/> with the reason when it cannot.
    /// </summary>
    PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory);

    /// <summary>Captures the workspace before launch, including ignored files, for the settlement's scope audit.</summary>
    WorkspaceSnapshot Capture(PreparedWorkspace workspace) => new(new Dictionary<string, string>(StringComparer.Ordinal), "This workspace provider supplies no file inventory.");

    /// <summary>Compares changes since launch with the assignment's permitted repository-relative paths.</summary>
    RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted) => new(0, 0, [], "This workspace provider cannot verify assignment scope.");

}
