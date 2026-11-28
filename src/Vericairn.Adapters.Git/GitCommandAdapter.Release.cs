// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitCommandAdapter
{
    /// <inheritdoc />
    public ReleaseSourceObservation? InspectRelease(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!Directory.Exists(path)) return null;
            using GitHardening hardening = GitHardening.Create();
            (AcquisitionGitView view, AcquisitionConfiguration settings) = Acquire(hardening, path, null);
            if (view.Bare || Status(hardening, view, settings) is not { } status) return null;
            string git = view.Directory;
            string? branch = Git(hardening, git, out string symbolic, "symbolic-ref", "--quiet", "refs/remotes/origin/HEAD") == 0 ? symbolic.Trim() : null;
            const string prefix = "refs/remotes/origin/";
            if (branch is null || !branch.StartsWith(prefix, StringComparison.Ordinal) || branch == prefix + "HEAD") return new(status.Length == 0, null, null);
            string? head = Git(hardening, git, out string resolved, "rev-parse", "--verify", "--quiet", branch + "^{commit}") == 0 ? resolved.Trim() : null;
            return new(status.Length == 0, branch[prefix.Length..], head is not null && Patterns.Commit.IsMatch(head) ? head : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ProviderRefusedException) { return null; }
    }
}
