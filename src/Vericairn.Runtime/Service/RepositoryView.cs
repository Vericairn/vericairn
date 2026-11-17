// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// What this machine's registered clones show the overlap check (from 0.9.4, CODE-73): the estate's own repository is the estate's
/// working tree, and a repository resource is the clone registered for any revision of it. Read through the git source adapter, without
/// fetching or changing anything; every answer is local, and none enters status.
/// </summary>
internal sealed class RepositoryView(EstateModel model, ISourceAdapter? git, string estate) : IRepositoryView
{
    private readonly Dictionary<string, (string Branch, IReadOnlyList<string> Paths)?> tracked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> lines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> folding = new(StringComparer.Ordinal);

    /// <summary>The view for a model through a provider registry's git adapter.</summary>
    public static RepositoryView For(EstateModel model, ProviderRegistry providers, string estate) => new(model, providers.Source("git"), estate);

    /// <summary>The directory of a repository's clone on this machine, or null.</summary>
    public string? Clone(string repository)
    {
        if (AssignmentOverlaps.IsEstate(model, repository)) return model.Root;
        if (!model.ById.TryGetValue(repository, out RecordEnvelope? resource)) return null;
        return model.Chain(model.Latest(resource)).Select(id => RepositoryClones.Find(estate, id)).FirstOrDefault(c => c is not null && Directory.Exists(c));
    }

    /// <inheritdoc />
    public (string Branch, IReadOnlyList<string> Paths)? Tracked(string repository)
    {
        if (tracked.TryGetValue(repository, out (string Branch, IReadOnlyList<string> Paths)? known)) return known;
        (string Branch, IReadOnlyList<string> Paths)? read = null;
        if (git is not null && MainLine(repository) is string line && Clone(repository) is string clone && git.TrackedPaths(clone, line) is { } paths)
        {
            read = (line, paths);
        }

        tracked[repository] = read;
        return read;
    }

    /// <inheritdoc />
    public string? MainLine(string repository)
    {
        if (lines.TryGetValue(repository, out string? known)) return known;
        string? line = git is not null && Clone(repository) is string clone ? git.MainLine(clone)?.Branch : null;
        lines[repository] = line;
        return line;
    }

    /// <inheritdoc />
    public bool? Reachable(string repository, string commit, string branch) =>
        git is not null && Clone(repository) is string clone ? git.Reachable(clone, commit, branch) : null;

    /// <inheritdoc />
    public bool? Precedes(string repository, string ancestor, string descendant) =>
        git is not null && Clone(repository) is string clone ? git.Precedes(clone, ancestor, descendant) : null;

    /// <inheritdoc />
    public bool CaseInsensitive(string repository)
    {
        if (folding.TryGetValue(repository, out bool known)) return known;
        bool folds = false;
        try
        {
            // A clone's .git entry answers to .GIT only where the filesystem folds case.
            if (Clone(repository) is string clone && (Directory.Exists(Path.Combine(clone, ".git")) || File.Exists(Path.Combine(clone, ".git"))))
            {
                folds = Directory.Exists(Path.Combine(clone, ".GIT")) || File.Exists(Path.Combine(clone, ".GIT"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            folds = false;
        }

        folding[repository] = folds;
        return folds;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Path, string Change)>? Changed(string repository, string from, string to) =>
        git is not null && Clone(repository) is string clone && git.ChangedPaths(clone, from, to) is { } changes ? [.. changes.Select(c => (c.Path, c.Change))] : null;
}
