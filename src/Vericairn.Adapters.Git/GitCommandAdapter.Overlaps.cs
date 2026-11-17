// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>From 0.9.4 (CODE-73): what a registered clone shows the overlap check, read without checkout, fetch or change.</summary>
public sealed partial class GitCommandAdapter
{
    /// <inheritdoc />
    public IReadOnlyList<string>? TrackedPaths(string root, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        try
        {
            if (!Directory.Exists(root) || !Branch(branch) || Head(root, branch) is not string head
                || GitFileSource.RunIsolated(root, out byte[] tree, "ls-tree", "-r", "-z", "--name-only", "--full-tree", head) != 0) return null;
            return [.. new UTF8Encoding(false, true).GetString(tree).Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException) { return null; }
    }

    /// <inheritdoc />
    public bool? Reachable(string root, string commit, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        try
        {
            if (!Directory.Exists(root) || !Patterns.Commit.IsMatch(commit) || !Branch(branch) || !HasCommit(root, commit) || Head(root, branch) is not string head) return null;
            return GitFileSource.RunIsolated(root, out _, "merge-base", "--is-ancestor", commit, head) switch { 0 => true, 1 => false, _ => null };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceChange>? ChangedPaths(string root, string baseCommit, string head)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        try
        {
            if (!Directory.Exists(root) || !Patterns.Commit.IsMatch(baseCommit) || !Patterns.Commit.IsMatch(head) || !HasCommit(root, baseCommit) || !HasCommit(root, head)
                || GitFileSource.RunIsolated(root, out byte[] bytes, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-status", "-z", baseCommit, head, "--") != 0) return null;
            string[] fields = new UTF8Encoding(false, true).GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var changes = new List<SourceChange>();
            for (int i = 0; i + 1 < fields.Length; i += 2)
            {
                string kind = fields[i] switch { "A" => "added", "D" => "removed", "M" or "T" => "modified", _ => string.Empty };
                if (kind.Length == 0) return null;
                changes.Add(new SourceChange(fields[i + 1], kind));
            }

            return [.. changes.OrderBy(c => c.Path, StringComparer.Ordinal)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException) { return null; }
    }

    // A plain branch name as git reads one, so that nothing reaches git as an option or a revision expression.
    private static bool Branch(string name) => name.Length is > 0 and <= 200 && !name.StartsWith('-') && !name.StartsWith('/') && !name.EndsWith('/')
        && !name.EndsWith(".lock", StringComparison.Ordinal) && !name.EndsWith('.') && !name.Contains("..", StringComparison.Ordinal) && !name.Contains("//", StringComparison.Ordinal)
        && !name.Contains("@{", StringComparison.Ordinal) && name != "@" && !name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\');

    private static bool HasCommit(string root, string commit) =>
        GitFileSource.RunIsolated(root, out byte[] bytes, "rev-parse", "--verify", "--quiet", commit + "^{commit}") == 0 && Encoding.UTF8.GetString(bytes).Trim() == commit;

    // The branch's local head, else origin's branch of that name.
    private static string? Head(string root, string branch)
    {
        foreach (string reference in new[] { "refs/heads/" + branch, "refs/remotes/origin/" + branch })
        {
            if (GitFileSource.RunIsolated(root, out byte[] resolved, "rev-parse", "--verify", "--quiet", reference + "^{commit}") == 0
                && Encoding.UTF8.GetString(resolved).Trim() is string head && Patterns.Commit.IsMatch(head)) return head;
        }

        return null;
    }
}
