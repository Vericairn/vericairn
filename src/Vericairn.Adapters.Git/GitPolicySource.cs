// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;

namespace Vericairn.Adapters.Git;

// Captures local bytes only. A missing clone or object is unavailable; this source never fetches or builds.
internal sealed class GitPolicySource : IPolicyRepositorySource
{
    // Enough for a few complete source trees; beyond it, the least recently read commits are read again when next needed.
    internal const long DefaultRetainedBytes = 256L * 1024 * 1024;
    private readonly ExactCommits retained;

    public GitPolicySource() : this(DefaultRetainedBytes) { }

    internal GitPolicySource(long retainedBytes) => retained = new(retainedBytes);

    internal long RetainedBytes => retained.RetainedBytes;

    public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit)
    {
        string identity = PolicyWork.Identity(model, repository);
        PolicyRepository Missing() => new(identity, commit ?? "unavailable", PolicyBasis.RepositoryContract, false, []);
        string? clone = Clone(model, repository);
        if (clone is null) return Missing();
        try
        {
            List<PolicyFile> files = [];
            if (commit is not null)
            {
                // The commit must resolve in this clone at every read, so a removed clone or pruned commit stays unavailable.
                if (commit.Length is not (40 or 64) || !commit.All(char.IsAsciiHexDigit) ||
                    GitFileSource.RunIsolated(clone, out byte[] resolved, "rev-parse", "--verify", "--quiet", commit + "^{commit}") != 0 || Encoding.UTF8.GetString(resolved).Trim() != commit ||
                    Exact(clone, commit) is not { } exact) return Missing();
                files.AddRange(exact);
            }
            else
            {
                if (GitFileSource.RunIsolated(clone, out byte[] names, "ls-files", "--cached", "--others", "--exclude-standard", "-z") != 0) return Missing();
                foreach (string path in Encoding.UTF8.GetString(names).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    if (!PolicyInputs.Path(path)) return Missing();
                    string full = clone;
                    foreach (string component in path.Split('/'))
                    {
                        full = Path.Combine(full, component);
                        if ((File.Exists(full) || Directory.Exists(full)) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return Missing();
                    }
                    if (!File.Exists(full)) continue;
                    byte[] bytes = File.ReadAllBytes(full);
                    files.Add(new(path, ContentHash.Sha256(bytes), [.. bytes]));
                }
            }
            return new(identity, commit ?? "working-tree:" + PolicyBasis.Hash(files.Select(f => new { f.Path, f.Hash })), PolicyBasis.RepositoryContract, true, [.. files]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return Missing(); }
    }

    public PolicyTreeComparison CompareTrees(EstateModel model, RecordEnvelope repository, string selectedCommit, PolicyTreeRequest request)
    {
        PolicyTreeComparison missing = new(request.SourceCommit, selectedCommit, request.Paths, false);
        if (!Patterns.Commit.IsMatch(selectedCommit) || !Patterns.Commit.IsMatch(request.SourceCommit) || request.Paths.IsDefaultOrEmpty ||
            request.Paths.Any(p => !PolicyInputs.Path(p)) || Clone(model, repository) is not { } clone) return missing;
        try
        {
            // Recursive tree entries include object ids, modes and directory trees; no diff driver or working bytes participate.
            byte[]? source = Tree(request.SourceCommit), selected = Tree(selectedCommit);
            return source is null || selected is null ? missing : new(request.SourceCommit, selectedCommit, request.Paths, true, ContentHash.Sha256(source), ContentHash.Sha256(selected));

            byte[]? Tree(string commit)
            {
                if (GitFileSource.RunIsolated(clone, out byte[] resolved, "rev-parse", "--verify", "--quiet", commit + "^{commit}") != 0 ||
                    Encoding.UTF8.GetString(resolved).Trim() != commit) return null;
                return GitFileSource.RunIsolated(clone, out byte[] tree, ["ls-tree", "-r", "-t", "-z", "--full-tree", commit, "--", .. request.Paths]) == 0 ? tree : null;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return missing; }
    }

    // Every regular file of an exact commit, in tree order, or null when an entry is refused or a blob cannot be read. A
    // complete read is retained: the commit names its tree and each blob is named by its content, so the files cannot differ
    // when the commit is read again in the same clone. Blobs are read through one batch process, never one process per file.
    private ImmutableArray<PolicyFile>? Exact(string clone, string commit)
    {
        // Registered clones are recorded as full paths, so the path names the clone.
        string key = clone;
        if (retained.Files(key, commit) is { } known) return known;
        if (GitFileSource.RunIsolated(clone, out byte[] tree, "ls-tree", "-r", "-z", "--full-tree", commit) != 0) return null;
        List<(string Path, string Id)> entries = [];
        foreach (string entry in Encoding.UTF8.GetString(tree).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entry.IndexOf('\t');
            if (tab < 0) return null;
            string path = entry[(tab + 1)..];
            string[] metadata = entry[..tab].Split(' ');
            // Links, submodules and any other non-regular entry refuse the whole inventory, as do unsafe paths.
            if (!PolicyInputs.Path(path) || metadata.Length != 3 || metadata[1] != "blob" || metadata[0] is not ("100644" or "100755")) return null;
            entries.Add((path, metadata[2]));
        }
        var read = new Dictionary<string, ExactCommits.Blob>(StringComparer.Ordinal);
        var files = ImmutableArray.CreateBuilder<PolicyFile>(entries.Count);
        GitBlobBatch? batch = null;
        try
        {
            foreach ((string path, string id) in entries)
            {
                if (!read.TryGetValue(id, out ExactCommits.Blob? blob))
                {
                    if ((blob = retained.Held(key, id)) is null)
                    {
                        batch ??= GitBlobBatch.Start(clone);
                        if (batch?.Read(id) is not { } bytes) return null;
                        blob = new(ContentHash.Sha256(bytes), ImmutableCollectionsMarshal.AsImmutableArray(bytes));
                    }
                    read[id] = blob;
                }
                files.Add(new(path, blob.Hash, blob.Bytes));
            }
        }
        finally { batch?.Dispose(); }
        ImmutableArray<PolicyFile> complete = files.MoveToImmutable();
        retained.Keep(key, commit, complete, read);
        return complete;
    }

    private static string? Clone(EstateModel model, RecordEnvelope repository) => model.Manifest is { } manifest ? model.Chain(repository).Reverse()
        .Select(id => RepositoryClones.Find(manifest.Estate, id)).FirstOrDefault(c => c is not null) : null;
}
