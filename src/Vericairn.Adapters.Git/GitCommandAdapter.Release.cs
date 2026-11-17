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
            // A registered clone is not a repository Vericairn created: every process runs hardened, and its working tree is read
            // the way repository observation reads it (Status), never through the clone's own configuration; a submodule checked
            // out at another commit still counts as a change.
            using GitHardening hardening = GitHardening.Create();
            if (Git(hardening, path, out string inside, "rev-parse", "--is-inside-work-tree") != 0 || inside.Trim() != "true") return null;
            string? checkedOut = Git(hardening, path, out string current, "rev-parse", "--verify", "--quiet", "HEAD") == 0 ? current.Trim() : null;
            if (Status(hardening, path, checkedOut) is not { } status) return null;
            string? branch = Git(hardening, path, out string symbolic, "symbolic-ref", "--quiet", "refs/remotes/origin/HEAD") == 0 ? symbolic.Trim() : null;
            const string prefix = "refs/remotes/origin/";
            if (branch is null || !branch.StartsWith(prefix, StringComparison.Ordinal) || branch == prefix + "HEAD") return new(status.Length == 0, null, null);
            string? head = Git(hardening, path, out string resolved, "rev-parse", "--verify", "--quiet", branch + "^{commit}") == 0 ? resolved.Trim() : null;
            return new(status.Length == 0, branch[prefix.Length..], head is not null && Patterns.Commit.IsMatch(head) ? head : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ProviderRefusedException) { return null; }
    }
}
