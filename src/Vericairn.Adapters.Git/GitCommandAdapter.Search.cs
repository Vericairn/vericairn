// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitCommandAdapter
{
    // Bounds on one commit's read: enough for an estate many times today's size, never an unbounded read of history.
    private const int MaximumTreeFiles = 200_000;
    private const long MaximumTreeBytes = 512L * 1024 * 1024;

    /// <inheritdoc />
    public IReadOnlyList<SourceFile>? ReadTreeAtCommit(string root, string commit, IReadOnlyList<string> directories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(directories);
        if (!Directory.Exists(root) || commit is null || !Patterns.Commit.IsMatch(commit) || directories.Count == 0
            || directories.Any(d => d.Length == 0 || d.Contains('/', StringComparison.Ordinal) || d.Contains('\\', StringComparison.Ordinal) || d is "." or ".."))
            return null;

        // The estate's own repository only: never a containing repository, and never a shallow clone whose history is incomplete.
        if (Line(root, "rev-parse", "--is-inside-work-tree") != "true" || Line(root, "rev-parse", "--show-prefix") != string.Empty
            || Line(root, "rev-parse", "--is-shallow-repository") != "false" || Line(root, "rev-parse", "--verify", "--quiet", commit + "^{commit}") != commit)
            return null;

        string[] arguments = ["ls-tree", "-r", "-z", "--full-tree", commit, "--", .. directories.Select(d => ":(literal)" + d)];
        if (GitFileSource.RunIsolated(root, out byte[] listing, arguments) != 0) return null;
        var entries = new List<(string Path, string Id)>();
        foreach (string entry in new UTF8Encoding(false, true).GetString(listing).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entry.IndexOf('\t');
            if (tab < 0 || entries.Count >= MaximumTreeFiles) return null;
            string[] metadata = entry[..tab].Split(' ');
            string path = entry[(tab + 1)..];
            if (metadata.Length != 3) return null;
            if (metadata[0] is not ("100644" or "100755") || metadata[1] != "blob" || !DocumentPatterns.IsSafePath(path)) continue;
            entries.Add((path, metadata[2]));
        }

        using GitBlobBatch? batch = GitBlobBatch.Start(root);
        if (batch is null) return null;
        var files = new List<SourceFile>(entries.Count);
        long total = 0;
        foreach ((string path, string id) in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            byte[]? bytes = batch.Read(id);
            if (bytes is null || (total += bytes.LongLength) > MaximumTreeBytes) return null;
            files.Add(new SourceFile(path, bytes));
        }

        return files;
    }

    private static string? Line(string root, params string[] arguments) =>
        GitFileSource.RunIsolated(root, out byte[] output, arguments) == 0 ? Encoding.UTF8.GetString(output).Trim() : null;
}
