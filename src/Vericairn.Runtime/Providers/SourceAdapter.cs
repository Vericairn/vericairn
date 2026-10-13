// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Providers;

/// <summary>One commit in a repository's recent history.</summary>
/// <param name="Commit">Commit id.</param>
/// <param name="At">Committer time.</param>
/// <param name="Summary">First line of the message.</param>
public sealed record RecentCommit(string Commit, string At, string Summary);

/// <summary>What a read-only inspection of a repository observed, with no machine path in it.</summary>
/// <param name="Commit">HEAD commit.</param>
/// <param name="Branch">Current branch, or null when detached.</param>
/// <param name="Remote">The origin remote's URL, or null when there is none.</param>
/// <param name="RootCommit">The first commit: the identity of a repository without a remote.</param>
/// <param name="Clean">True when nothing is modified, staged or untracked.</param>
/// <param name="Modified">Tracked files with changes, staged or not.</param>
/// <param name="Untracked">Untracked files.</param>
/// <param name="Tags">Tags, sorted.</param>
/// <param name="Recent">Most recent commits, newest first, at most ten.</param>
/// <param name="AsOf">When the observation was made.</param>
public sealed record RepositoryObservation(string Commit, string? Branch, string? Remote, string RootCommit, bool Clean, int Modified, int Untracked, IReadOnlyList<string> Tags, IReadOnlyList<RecentCommit> Recent, string AsOf);

/// <summary>
/// The git adapter contract, version 1: read-only observation of a repository. It preserves uncommitted work, promises nothing
/// across repositories, and never publishes; integration and remote publication are provider operations, not adapter ones.
/// </summary>
public interface ISourceAdapter
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.adapter.git/1";

    /// <summary>What this implementation is.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>True when the path is inside a repository with at least one commit.</summary>
    bool IsRepository(string path);

    /// <summary>Observes a repository without changing it; throws <see cref="ProviderRefusedException"/> when the path is not a repository with a commit.</summary>
    RepositoryObservation Inspect(string path);
}

/// <summary>The git adapter over the git command on the PATH: every call is a read-only git command.</summary>
public sealed class GitCommandAdapter : ISourceAdapter
{
    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "git-command",
        ISourceAdapter.Contract,
        "implemented",
        ["inspect"],
        ["Requires git on the PATH.", "Observes the working tree and history only; it never fetches, commits, pushes or changes the index beyond git's own status refresh."]);

    /// <inheritdoc />
    public bool IsRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path) && Git(path, out _, "rev-parse", "--verify", "HEAD") == 0;
    }

    /// <inheritdoc />
    public RepositoryObservation Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            throw new ProviderRefusedException($"{path} does not exist.");
        }

        if (Git(path, out string inside, "rev-parse", "--is-inside-work-tree") != 0 || inside.Trim() != "true")
        {
            throw new ProviderRefusedException($"{Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))} is not inside a git working tree.");
        }

        if (Git(path, out string head, "rev-parse", "--verify", "HEAD") != 0)
        {
            throw new ProviderRefusedException("The repository has no commits yet; make the first commit, then adopt it.");
        }

        string commit = head.Trim();
        string? branch = Git(path, out string branchText, "symbolic-ref", "--short", "-q", "HEAD") == 0 ? branchText.Trim() : null;
        string? remote = Git(path, out string remoteText, "remote", "get-url", "origin") == 0 ? remoteText.Trim() : null;
        Git(path, out string roots, "rev-list", "--max-parents=0", "HEAD");
        string rootCommit = roots.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Order(StringComparer.Ordinal).FirstOrDefault() ?? commit;
        Git(path, out string status, "status", "--porcelain=v1", "--untracked-files=all");
        int modified = 0;
        int untracked = 0;
        foreach (string line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("??", StringComparison.Ordinal))
            {
                untracked++;
            }
            else
            {
                modified++;
            }
        }

        Git(path, out string tagText, "tag", "--list");
        List<string> tags = tagText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Order(StringComparer.Ordinal).ToList();
        Git(path, out string log, "log", "-n", "10", "--format=%H%x1f%cI%x1f%s");
        var recent = new List<RecentCommit>();
        foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\x1f');
            if (parts.Length == 3 && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
            {
                recent.Add(new RecentCommit(parts[0].Trim(), RecordParser.FormatTimestamp(at), parts[2].Trim()));
            }
        }

        return new RepositoryObservation(commit, branch, remote, rootCommit, modified == 0 && untracked == 0, modified, untracked, tags, recent, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
    }

    private static int Git(string path, out string output, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = path };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=off");
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        try
        {
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException("git did not start.");
            output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new ProviderRefusedException("git did not answer within 30 seconds.");
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ProviderRefusedException("git is not on the PATH; install git to adopt and observe repositories.", exception);
        }
    }
}

/// <summary>A source adapter that replays recorded observations: a genuinely different implementation of the contract for conformance tests and offline fixtures; it qualifies no live repository.</summary>
public sealed class RecordedSourceAdapter : ISourceAdapter
{
    private readonly IReadOnlyDictionary<string, RepositoryObservation> observations;

    /// <summary>Creates an adapter that answers for the given paths only.</summary>
    public RecordedSourceAdapter(IReadOnlyDictionary<string, RepositoryObservation> observations)
    {
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
    }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("git-recorded", ISourceAdapter.Contract, "implemented", ["inspect"], ["Replays recorded observations; it observes no real repository."]);

    /// <inheritdoc />
    public bool IsRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return observations.ContainsKey(Path.GetFullPath(path));
    }

    /// <inheritdoc />
    public RepositoryObservation Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return observations.TryGetValue(Path.GetFullPath(path), out RepositoryObservation? observation)
            ? observation with { AsOf = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) }
            : throw new ProviderRefusedException($"{Path.GetFileName(path)} is not a recorded repository.");
    }
}
