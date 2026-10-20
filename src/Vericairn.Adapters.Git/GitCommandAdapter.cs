// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>The git adapter over the git command on the PATH, with read-only observations and isolated worker workspaces.</summary>
public sealed partial class GitCommandAdapter : ISourceAdapter
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Observes git with a thirty-second deadline and bounded output for each command.</summary>
    public GitCommandAdapter() : this(info => BoundedProcess.Run(info, TimeSpan.FromSeconds(30)))
    {
    }

    internal GitCommandAdapter(Func<ProcessStartInfo, ProcessResult> run, IWorkspaceProvider? workspaces = null)
    {
        this.run = run;
        Workspaces = workspaces ?? new GitWorktreeWorkspaces();
    }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "git-command",
        ISourceAdapter.Contract,
        "implemented",
        ["inspect", "read-file-at-hash", "workspaces"],
        ["Requires git on the PATH.", "Observations are read-only. Worker workspaces have independent administration and retain audited branches locally after settlement; the adapter never publishes."]);

    /// <inheritdoc />
    public IWorkspaceProvider Workspaces { get; }

    /// <inheritdoc />
    public byte[]? ReadFileAtHash(string root, string relativePath, string hash) => GitDocumentHistory.Read(root, relativePath, hash);

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

    private int Git(string path, out string output, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = path };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=off");
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        ProcessResult result = run(info);
        if (result.Outcome != ProcessOutcome.Exited)
        {
            throw new ProviderRefusedException(result.Outcome == ProcessOutcome.NotStarted
                ? "git could not be started; install git on the PATH to adopt and observe repositories."
                : $"git {result.Description}; repository observation is incomplete.");
        }

        output = result.StandardOutput;
        return result.ExitCode!.Value;
    }
}
