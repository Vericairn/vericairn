// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Adapters.Git;

/// <summary>A pack selects its exact retained inputs through a read-only, candidate-commit file reader.</summary>
public delegate DocumentSourceSelection GitDocumentSelection(RecordEnvelope declaration, DocumentGeneratorDescriptor generator,
    string commit, Func<string, DocumentSourceSnapshot?> read);

/// <summary>
/// The software pack's source vericairn.software/git-file: a living document of another repository read at a commit from the
/// clone of that repository registered on this machine (vericairn sources clone), with the covered paths' identity taken from
/// git's own object ids at that commit. Every call is a read-only git command; nothing is fetched or changed.
/// </summary>
public sealed class GitFileSource : IDocumentSource
{
    private readonly GitDocumentSelection? select;
    private readonly Func<EstateModel, RecordEnvelope, string?> clone;

    /// <summary>Installs an optional pack-owned immutable input selector; no clone is discovered during rendering.</summary>
    public GitFileSource(GitDocumentSelection? selection = null) { select = selection; clone = Clone; }

    internal GitFileSource(string directory, GitDocumentSelection selection) { select = selection; clone = (_, _) => directory; }

    /// <summary>The source's name, as declarations name it.</summary>
    public const string Name = "vericairn.software/git-file";

    /// <inheritdoc />
    public string Id => Name;

    /// <inheritdoc />
    public int SnapshotVersion => DocumentGeneration.InterfaceVersion;

    /// <inheritdoc />
    public DocumentSourceSelection ReadSelection(EstateModel model, RecordEnvelope declaration, string commit, DocumentGeneratorDescriptor generator)
    {
        if (select is null || clone(model, declaration) is not { } directory || Tree(directory, commit) is not { } tree)
            return new(DocumentGenerationStatus.Unavailable, [], commit, "The exact commit or installed source selector is unavailable.");
        long total = 0;
        return select(declaration, generator, commit, path =>
        {
            if (!DocumentPatterns.IsSafeSourcePath(path) || !tree.TryGetValue(path, out string? mode) || mode is not ("100644" or "100755")
                || Run(directory, out byte[] bytes, "cat-file", "blob", commit + ":" + path) != 0 || (total += bytes.Length) > 64L * 1024 * 1024) return null;
            return DocumentSourceSnapshot.Content("file", path, EstateModel.BodyString(declaration, "repository") ?? "repository", path, ContentHash.Sha256(bytes), bytes);
        });
    }

    /// <inheritdoc />
    public DocumentFileInventory ReadInventory(EstateModel model, RecordEnvelope declaration, string commit)
    {
        if (clone(model, declaration) is not { } directory || Tree(directory, commit) is not { } tree)
            return new(DocumentGenerationStatus.Unavailable, [], commit, "The complete candidate tree is unavailable.");
        var files = ImmutableArray.CreateBuilder<DocumentFile>();
        long total = 0;
        foreach ((string path, string mode) in tree.Where(p => GeneratedDocumentContract.Owns(declaration, p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (mode is not ("100644" or "100755") || !DocumentPatterns.IsSafePath(path)
                || Run(directory, out byte[] bytes, "cat-file", "blob", commit + ":" + path) != 0 || bytes.Length > 1048576 || (total += bytes.Length) > 64L * 1024 * 1024)
                return new(DocumentGenerationStatus.Unavailable, [], commit, "A candidate member cannot be read as a bounded regular file.");
            files.Add(new(path, [.. bytes]));
        }
        return new(DocumentGenerationStatus.Success, files.ToImmutable(), commit, null);
    }

    private static Dictionary<string, string>? Tree(string directory, string commit)
    {
        if (!Patterns.Commit.IsMatch(commit) || Text(directory, "rev-parse", "--verify", "--quiet", commit + "^{commit}") != commit
            || Run(directory, out byte[] bytes, "ls-tree", "-r", "-z", "--full-tree", commit) != 0) return null;
        var tree = new Dictionary<string, string>(StringComparer.Ordinal);
        var portable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in new UTF8Encoding(false, true).GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entry.IndexOf('\t');
            if (tab < 0 || tree.Count >= 100000) return null;
            string[] metadata = entry[..tab].Split(' ');
            string path = entry[(tab + 1)..];
            if (metadata.Length != 3 || !tree.TryAdd(path, metadata[0]) || !portable.Add(path)) return null;
        }
        return tree;
    }

    /// <inheritdoc />
    public string? Head(EstateModel model, RecordEnvelope declaration) => clone(model, declaration) is string directory ? Text(directory, "rev-parse", "--verify", "HEAD") : null;

    /// <inheritdoc />
    public string? Resolve(EstateModel model, RecordEnvelope declaration, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return clone(model, declaration) is string directory ? Text(directory, "rev-parse", "--verify", "--quiet", reference + "^{commit}") : null;
    }

    /// <inheritdoc />
    public bool IsAncestor(EstateModel model, RecordEnvelope declaration, string ancestor, string descendant) =>
        this.clone(model, declaration) is string clone && Run(clone, out _, "merge-base", "--is-ancestor", ancestor, descendant) == 0;

    /// <inheritdoc />
    public byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit)
    {
        string? path = EstateModel.BodyString(declaration, "path");
        return this.clone(model, declaration) is string clone && path is not null && Run(clone, out byte[] bytes, "cat-file", "blob", $"{commit}:{path}") == 0 ? bytes : null;
    }

    /// <inheritdoc />
    public string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (this.clone(model, declaration) is not string clone || Text(clone, "rev-parse", "--verify", "--quiet", commit + "^{commit}") is null)
        {
            return null;
        }

        // Each path's object at the commit (a tree for a directory, a blob for a file), or absent; hashed in path order.
        var lines = new StringBuilder();
        foreach (string path in paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            lines.Append(path).Append('\t').Append(Text(clone, "rev-parse", "--verify", "--quiet", $"{commit}:{path}") ?? "absent").Append('\n');
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
    }

    /// <inheritdoc />
    public bool Exists(EstateModel model, RecordEnvelope declaration, string commit, string path) =>
        this.clone(model, declaration) is string clone && Run(clone, out _, "cat-file", "-e", $"{commit}:{path}") == 0;

    private static string? Clone(EstateModel model, RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        // A clone is registered under any revision of the repository's resource record.
        if (EstateModel.BodyString(declaration, "repository") is not string resource || model.Manifest?.Estate is not string estate || !model.ById.TryGetValue(resource, out RecordEnvelope? record))
        {
            return null;
        }

        return model.Chain(model.Latest(record)).Reverse().Select(id => RepositoryClones.Find(estate, id)).FirstOrDefault(c => c is not null && Directory.Exists(c));
    }

    private static string? Text(string clone, params string[] arguments) =>
        Run(clone, out byte[] output, arguments) == 0 && Encoding.UTF8.GetString(output).Trim() is { Length: > 0 } text ? text : null;

    internal static int Run(string clone, out byte[] output, params string[] arguments) => Run(clone, out output, false, arguments);

    internal static int RunIsolated(string clone, out byte[] output, params string[] arguments) => Run(clone, out output, true, arguments);

    private static int Run(string clone, out byte[] output, bool isolated, string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = clone };
        info.ArgumentList.Add("--no-replace-objects");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=off");
        if (isolated)
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("protocol.allow=never");
            // Match the adapter's local history observations: ambient Git routing and configuration cannot select another source.
            foreach (string variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_INDEX_FILE", "GIT_NAMESPACE", "GIT_SHALLOW_FILE", "GIT_CONFIG_PARAMETERS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_LITERAL_PATHSPECS", "GIT_ICASE_PATHSPECS" })
                info.Environment.Remove(variable);
            info.Environment["GIT_CONFIG_COUNT"] = "0";
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = string.Empty;
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        }
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        try
        {
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            using var buffer = new MemoryStream();
            Task copy = CopyBounded(process.StandardOutput.BaseStream, buffer);
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                output = [];
                return -1;
            }

            try { copy.GetAwaiter().GetResult(); }
            catch (IOException) { output = []; return -1; }
            _ = error.Result;
            output = buffer.ToArray();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output = [];
            return -1;
        }
    }

    private static async Task CopyBounded(Stream input, Stream output)
    {
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > 64L * 1024 * 1024) throw new IOException("The candidate input byte bound was exhausted.");
            await output.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
        }
    }
}
