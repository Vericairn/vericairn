// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitCommandAdapter
{
    /// <inheritdoc />
    public SourceFile? DeletedRecordAtCommit(string root, string commit, string id) =>
        ReadRecordDeletion(root, commit, id, carriedFrom: null);

    /// <inheritdoc />
    public SourceFile? DeletedRecordAtCommit(string root, string commit, string id, string carriedFrom) =>
        ReadRecordDeletion(root, commit, id, carriedFrom);

    private SourceFile? ReadRecordDeletion(string root, string commit, string id, string? carriedFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Ulid.IsValid(id) || !IsFullObjectId(commit) || MainLine(root) is not { } main) return null;

        try
        {
            using GitHardening hardening = GitHardening.Create();
            string? Text(params string[] arguments) =>
                Run(out byte[] bytes, arguments) == 0 ? Encoding.UTF8.GetString(bytes).Trim() : null;
            int Run(out byte[] bytes, params string[] arguments) =>
                GitFileSource.Run(OverlapRead(root, hardening, ["-c", "core.commitGraph=false", .. arguments]), out bytes);

            if (Text("rev-parse", "--is-shallow-repository") != "false" ||
                Text("rev-parse", "--verify", commit + "^{commit}") != commit ||
                Run(out _, "merge-base", "--is-ancestor", commit, main.Commit) != 0)
                return null;

            // Compare the commit to each parent. A file must be absent in the resulting tree, and every parent from
            // which it disappeared must agree on its path and blob. A type change is not a deletion.
            string pathspec = $":(glob){EstateLayout.RecordsDirectory}/*/*/{id}.json";
            if (Run(out byte[] output, "diff-tree", "-m", "-r", "--no-commit-id", "--no-abbrev", "--no-renames",
                "--no-ext-diff", "--no-textconv", "--diff-filter=D", "--raw", "-z", commit, "--", pathspec) != 0 ||
                output.Length == 0 || output[^1] != 0)
                return null;

            string[] fields = new UTF8Encoding(false, true).GetString(output).Split('\0');
            if ((fields.Length - 1) % 2 != 0) return null;
            var versions = new HashSet<(string Path, string Blob)>();
            for (int i = 0; i < fields.Length - 1; i += 2)
            {
                string[] metadata = fields[i].Split(' ');
                if (metadata.Length != 5 || metadata[0] is not (":100644" or ":100755") ||
                    metadata[1] != "000000" || metadata[4] != "D" || !IsFullObjectId(metadata[2]))
                    return null;
                versions.Add((fields[i + 1], metadata[2]));
            }

            if (versions.Count != 1) return null;
            (string path, string blob) = versions.Single();
            if (carriedFrom is not null && !CarriesDeletion(carriedFrom, path)) return null;
            if (!long.TryParse(Text("cat-file", "-s", blob), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ||
                size > EstateLayout.MaxFileBytes || Run(out byte[] bytes, "cat-file", "blob", blob) != 0 || bytes.LongLength != size)
                return null;
            return new SourceFile(path, bytes);

            bool CarriesDeletion(string ancestor, string path)
            {
                if (!IsFullObjectId(ancestor) || Text("rev-parse", "--verify", ancestor + "^{commit}") != ancestor ||
                    Text("rev-list", "--parents", "--max-count=1", commit) is not { } line)
                    return false;

                string[] commits = line.Split(' ');
                // The listing names only the newest deleting commit, so a merge's parents alone cannot tell the acknowledged deletion
                // carried through from a later one: no commit between the named one and this one may add the path back.
                return commits.Length >= 3 && commits[0] == commit && commits[1..].Any(parent =>
                    IsFullObjectId(parent) && Run(out _, "merge-base", "--is-ancestor", ancestor, parent) == 0 &&
                    Run(out byte[] tree, "ls-tree", "-z", "--full-tree", parent, "--", ":(literal)" + path) == 0 &&
                    tree.Length == 0) &&
                    Run(out byte[] added, "log", "-m", "--no-renames", "--no-ext-diff", "--no-textconv", "--diff-filter=A", "--format=%H",
                        ancestor + ".." + commit, "--", ":(literal)" + path) == 0 && added.Length == 0;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsFullObjectId(string value) => value.Length is 40 or 64 && value.All(char.IsAsciiHexDigitLower);
}
