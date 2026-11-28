// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>
/// From 0.9.4 (CODE-73): what a registered clone shows the overlap check, read without checkout, fetch or change. Each read starts as
/// the adapter's hardened reads do (<see cref="GitHardening"/>: no system or global configuration, no transport, no replacement objects,
/// no lazy fetch, no pager, file-system monitor or hooks, and the repository's own program settings overridden), with the clone's grafts
/// ignored too, so that the clone's own configuration and info files cannot run a program or steer which commits are ancestors; a diff
/// compares submodules too and names every path from the repository's root, whatever subdirectory the clone's configured working tree
/// puts it in.
/// </summary>
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
                || Read(root, out byte[] tree, "ls-tree", "-r", "-z", "--name-only", "--full-tree", head) != 0) return null;
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
            return Read(root, out _, "merge-base", "--is-ancestor", commit, head) switch { 0 => true, 1 => false, _ => null };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    /// <inheritdoc />
    public bool? Precedes(string root, string ancestor, string descendant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        try
        {
            if (!Directory.Exists(root) || !Patterns.Commit.IsMatch(ancestor) || !Patterns.Commit.IsMatch(descendant) || !HasCommit(root, ancestor) || !HasCommit(root, descendant)) return null;
            if (ancestor == descendant) return false;
            return Read(root, out _, "merge-base", "--is-ancestor", ancestor, descendant) switch { 0 => true, 1 => false, _ => null };
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
                || Read(root, out byte[] bytes, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--no-relative", "--ignore-submodules=none", "--name-status", "-z", baseCommit, head, "--") != 0) return null;
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
        Read(root, out byte[] bytes, "rev-parse", "--verify", "--quiet", commit + "^{commit}") == 0 && Encoding.UTF8.GetString(bytes).Trim() == commit;

    // The branch's local head, else origin's branch of that name.
    private static string? Head(string root, string branch)
    {
        foreach (string reference in new[] { "refs/heads/" + branch, "refs/remotes/origin/" + branch })
        {
            if (Read(root, out byte[] resolved, "rev-parse", "--verify", "--quiet", reference + "^{commit}") == 0
                && Encoding.UTF8.GetString(resolved).Trim() is string head && Patterns.Commit.IsMatch(head)) return head;
        }

        return null;
    }

    /// <summary>
    /// The arguments and environment every read of the overlap check starts git with: the adapter's hardened start
    /// (<see cref="GitFileSource.StartInfo"/>), whose hooks path names a directory that does not exist, and a graft file that does not
    /// exist, so that the clone's <c>info/grafts</c> cannot rewrite which commits are ancestors. <paramref name="hardening"/> must outlive
    /// the process.
    /// </summary>
    internal static ProcessStartInfo OverlapRead(string root, GitHardening hardening, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(hardening);
        ProcessStartInfo info = GitFileSource.StartInfo(root, hardening, arguments);
        info.Environment["GIT_GRAFT_FILE"] = Path.Combine(hardening.Control, "absent", "grafts");
        return info;
    }

    private static int Read(string root, out byte[] output, params string[] arguments)
    {
        GitHardening hardening;
        try { hardening = GitHardening.Create(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            output = [];
            return -1;
        }

        using (hardening)
        {
            try { return GitFileSource.Run(OverlapRead(root, hardening, arguments), out output); }
            catch (ProviderRefusedException) { output = []; return -1; }
        }
    }
}
