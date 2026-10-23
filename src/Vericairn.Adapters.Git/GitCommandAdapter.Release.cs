// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
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
            if (!Directory.Exists(path) || GitFileSource.RunIsolated(path, out byte[] inside, "rev-parse", "--is-inside-work-tree") != 0 ||
                Encoding.UTF8.GetString(inside).Trim() != "true" ||
                GitFileSource.RunIsolated(path, out byte[] status, "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none") != 0) return null;
            string? branch = GitFileSource.RunIsolated(path, out byte[] symbolic, "symbolic-ref", "--quiet", "refs/remotes/origin/HEAD") == 0 ? Encoding.UTF8.GetString(symbolic).Trim() : null;
            const string prefix = "refs/remotes/origin/";
            if (branch is null || !branch.StartsWith(prefix, StringComparison.Ordinal) || branch == prefix + "HEAD") return new(status.Length == 0, null, null);
            string? head = GitFileSource.RunIsolated(path, out byte[] resolved, "rev-parse", "--verify", "--quiet", branch + "^{commit}") == 0 ? Encoding.UTF8.GetString(resolved).Trim() : null;
            return new(status.Length == 0, branch[prefix.Length..], head is not null && Patterns.Commit.IsMatch(head) ? head : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }
}
