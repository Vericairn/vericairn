// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitCommandAdapter
{
    /// <inheritdoc />
    public IReadOnlySet<string>? IgnoredEstateEntries(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        // Do not discover an enclosing repository for a non-Git estate. A gitfile also admits linked worktrees.
        string git = Path.Combine(root, ".git");
        if (!Directory.Exists(git) && !File.Exists(git)) return null;
        string[] candidates = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).OfType<string>()
            .Where(name => !EstateLayout.AllowedTopLevel.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (candidates.Length == 0) return ignored;

        if (Observe(root, git, "ls-files", "--cached", "-z") is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 } tracked
            || tracked.StandardOutput.Length > 0 && !tracked.StandardOutput.EndsWith('\0')) return null;
        ProcessResult caseSetting = Observe(root, git, "config", "--bool", "--get", "core.ignorecase");
        if (caseSetting is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 or 1 }
            || caseSetting.ExitCode == 0 && caseSetting.StandardOutput.Trim() is not ("true" or "false")) return null;
        StringComparer comparer = caseSetting.StandardOutput.Trim() == "true" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var trackedRoots = tracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Split('/')[0]).ToHashSet(comparer);
        foreach (string name in candidates)
        {
            // check-ignore can match a directory even when some of its children are tracked.
            if (trackedRoots.Contains(name)) continue;
            ProcessResult result = Observe(root, git, "check-ignore", "--quiet", "--", "./" + name);
            if (result is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 or 1 }) return null;
            if (result.ExitCode == 0) ignored.Add(name);
        }
        return ignored;
    }

    /// <inheritdoc />
    public RecordDeletions? DeletedRecordFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string git = Path.Combine(root, ".git");
        if (!Directory.Exists(git) && !File.Exists(git)) return null;
        if (Observe(root, git, "rev-parse", "--is-shallow-repository") is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 } shallow) return null;
        if (shallow.StandardOutput.Trim() == "true") return new RecordDeletions(true, []);
        // Merges are compared with each parent, so a deletion made while resolving a merge is listed too; the log runs newest first.
        if (Observe(root, git, "-c", "core.quotePath=false", "log", "-m", "--no-renames", "--diff-filter=D", "--name-only", "--format=commit %H", "--", EstateLayout.RecordsDirectory + "/")
            is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 } log) return null;
        var deleted = new Dictionary<string, string>(StringComparer.Ordinal);
        string? commit = null;
        foreach (string line in log.StandardOutput.Split('\n'))
        {
            string text = line.TrimEnd('\r');
            if (text.StartsWith("commit ", StringComparison.Ordinal)) commit = text["commit ".Length..].Trim();
            else if (commit is not null && text.StartsWith(EstateLayout.RecordsDirectory + "/", StringComparison.Ordinal)) deleted.TryAdd(text, commit);
        }

        return new RecordDeletions(false, [.. deleted.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new RecordDeletion(d.Key, d.Value))]);
    }

    /// <summary>Runs git against the estate's own repository with both directories pinned and ambient overrides, alternate indexes and configuration discarded.</summary>
    private ProcessResult Observe(string root, string git, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = root,
        };
        foreach (string name in info.Environment.Keys.Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(name);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = string.Empty;
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string argument in new[]
        {
            "--no-pager", "--no-replace-objects",
            "--git-dir=" + Path.GetFullPath(git), "--work-tree=" + Path.GetFullPath(root),
            "-c", "core.excludesFile=", "-c", "core.fsmonitor=false",
            "-c", "core.untrackedCache=false", "-c", "protocol.allow=never",
        }) info.ArgumentList.Add(argument);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return run(info);
    }
}
