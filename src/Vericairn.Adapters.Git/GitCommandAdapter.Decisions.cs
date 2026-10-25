// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitCommandAdapter
{
    /// <inheritdoc />
    public DesignChangeSet? InspectDesignChanges(string root, string baseCommit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (string.IsNullOrEmpty(baseCommit) || !Patterns.Commit.IsMatch(baseCommit)) return null;
        try
        {
            if (Read("rev-parse", "--show-prefix")?.Trim() != "" || Read("rev-parse", "--verify", baseCommit + "^{commit}")?.Trim() != baseCommit
                || Read("rev-parse", "--verify", "HEAD^{commit}")?.Trim() is not string head || !Patterns.Commit.IsMatch(head)
                || Read("ls-files", "--unmerged", "-z", "--", "design/") != "") return null;
            string? changed = Read("diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", baseCommit, "--", "design/");
            string? staged = Read("diff", "--cached", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", baseCommit, "--", "design/");
            string? untracked = Read("ls-files", "--others", "--exclude-standard", "-z", "--", "design/");
            string? tracked = Read("ls-files", "-v", "-z", "--", "design/");
            // Sparse and assume-unchanged entries can hide working-tree edits from diff. Require ordinary observed entries.
            if (changed is null || staged is null || untracked is null || tracked is null
                || tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(entry => !entry.StartsWith("H ", StringComparison.Ordinal))) return null;
            string[] paths = (changed + staged + untracked).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (paths.Length > 10000 || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length) return null;
            var documents = new List<DesignFileChange>();
            foreach (string path in paths)
            {
                if (!path.StartsWith("design/", StringComparison.Ordinal) || !DocumentPatterns.IsSafePath(path)) return null;
                // Deletions, links and aliases cannot receive a content pin and refuse the whole change.
                string file = GeneratedDocumentVerification.SafeFile(root, path);
                documents.Add(new(path, ContentHash.Sha256File(file)));
            }
            if (Read("rev-parse", "--verify", "HEAD^{commit}")?.Trim() != head) return null;
            return new(baseCommit, head, documents);

            string? Read(params string[] arguments) => GitFileSource.RunIsolated(root, out byte[] bytes,
                ["-c", "core.fsmonitor=false", .. arguments]) == 0 ? new UTF8Encoding(false, true).GetString(bytes) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
