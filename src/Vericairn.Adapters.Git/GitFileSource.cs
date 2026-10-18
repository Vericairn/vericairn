// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Adapters.Git;

/// <summary>
/// The software pack's source vericairn.software/git-file: a living document of another repository read at a commit from the
/// clone of that repository registered on this machine (vericairn sources clone), with the covered paths' identity taken from
/// git's own object ids at that commit. Every call is a read-only git command; nothing is fetched or changed.
/// </summary>
public sealed class GitFileSource : IDocumentSource
{
    /// <summary>The source's name, as declarations name it.</summary>
    public const string Name = "vericairn.software/git-file";

    /// <inheritdoc />
    public string Id => Name;

    /// <inheritdoc />
    public string? Head(EstateModel model, RecordEnvelope declaration) => Clone(model, declaration) is string clone ? Text(clone, "rev-parse", "--verify", "HEAD") : null;

    /// <inheritdoc />
    public string? Resolve(EstateModel model, RecordEnvelope declaration, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return Clone(model, declaration) is string clone ? Text(clone, "rev-parse", "--verify", "--quiet", reference + "^{commit}") : null;
    }

    /// <inheritdoc />
    public bool IsAncestor(EstateModel model, RecordEnvelope declaration, string ancestor, string descendant) =>
        Clone(model, declaration) is string clone && Run(clone, out _, "merge-base", "--is-ancestor", ancestor, descendant) == 0;

    /// <inheritdoc />
    public byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit)
    {
        string? path = EstateModel.BodyString(declaration, "path");
        return Clone(model, declaration) is string clone && path is not null && Run(clone, out byte[] bytes, "cat-file", "blob", $"{commit}:{path}") == 0 ? bytes : null;
    }

    /// <inheritdoc />
    public string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (Clone(model, declaration) is not string clone || Text(clone, "rev-parse", "--verify", "--quiet", commit + "^{commit}") is null)
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
        Clone(model, declaration) is string clone && Run(clone, out _, "cat-file", "-e", $"{commit}:{path}") == 0;

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

    private static int Run(string clone, out byte[] output, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = clone };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=off");
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        try
        {
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            using var buffer = new MemoryStream();
            Task copy = process.StandardOutput.BaseStream.CopyToAsync(buffer);
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                output = [];
                return -1;
            }

            copy.Wait();
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
}
