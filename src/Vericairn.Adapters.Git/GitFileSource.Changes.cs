// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Adapters.Git;

public sealed partial class GitFileSource
{
    /// <inheritdoc />
    public DocumentSourceChanges Changes(EstateModel model, RecordEnvelope declaration, string? before, string commit)
    {
        if (!Patterns.Commit.IsMatch(commit) || (before is not null && !Patterns.Commit.IsMatch(before))
            || clone(model, declaration) is not { } directory || !HasCommit(directory, commit)
            || (before is not null && !HasCommit(directory, before)))
            return new([], "The registered clone does not contain both publication commits; no comparison was made.");
        string[] arguments = before is null
            ? ["ls-tree", "-r", "-z", "--name-only", "--full-tree", commit]
            : ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-status", "-z", before, commit, "--"];
        if (RunIsolated(directory, out byte[] bytes, arguments) != 0)
            return new([], "The local publication comparison could not be read.");
        try
        {
            string[] fields = new UTF8Encoding(false, true).GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var changes = new List<DocumentSourceChange>();
            for (int i = 0; i < fields.Length; i++)
            {
                string kind = before is null ? "added" : fields[i++] switch
                {
                    "A" => "added", "D" => "deleted", "M" or "T" => "modified", _ => "unknown",
                };
                if (i >= fields.Length || kind == "unknown" || !DocumentPatterns.IsSafeSourcePath(fields[i]))
                    return new([], "The publication comparison contains an unsupported file name or status.");
                changes.Add(new(fields[i], kind));
            }
            return new(changes.OrderBy(c => c.Path, StringComparer.Ordinal).ToArray());
        }
        catch (DecoderFallbackException)
        {
            return new([], "The publication comparison contains a file name that is not UTF-8.");
        }
    }

    private static bool HasCommit(string directory, string commit) =>
        RunIsolated(directory, out byte[] bytes, "rev-parse", "--verify", "--quiet", commit + "^{commit}") == 0
        && Encoding.UTF8.GetString(bytes).Trim() == commit;
}
