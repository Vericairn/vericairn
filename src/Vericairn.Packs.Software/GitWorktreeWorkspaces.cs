// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>
/// Workspaces as git worktrees (vericairn.workspace/1): a worktree of this machine's registered clone on the run's work branch, made
/// from the base when the branch does not exist yet, beside the estate's per-machine state. The clone's own working tree, index and
/// checked-out branch never change, and the worktree stays after the run for review.
/// </summary>
public sealed class GitWorktreeWorkspaces : IWorkspaceProvider
{
    private readonly IReadOnlyList<string>? auditCommand;

    /// <summary>Uses the installed Vericairn command as the isolated scope observer.</summary>
    public GitWorktreeWorkspaces()
    {
    }

    internal GitWorktreeWorkspaces(IReadOnlyList<string> auditCommand) => this.auditCommand = auditCommand;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "git-worktree",
        IWorkspaceProvider.Contract,
        "implemented",
        ["prepare"],
        [
            "Requires git on the PATH.",
            "Adds a worktree of this machine's registered clone on the work branch, making the branch from the base when it does not exist; the clone's own working tree, index and checked-out branch never change.",
            "Takes up again the worktree an earlier run of the branch left, so a later attempt continues where the last one stopped; the worktree stays after the run, and removing it is the person's.",
        ]);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clone);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (Git(clone, out string common, "rev-parse", "--path-format=absolute", "--git-common-dir") != 0)
        {
            throw new ProviderRefusedException("The registered clone is not a git repository with this git.");
        }

        if (Git(clone, out _, "check-ref-format", "--branch", branch) != 0)
        {
            throw new ProviderRefusedException($"'{branch}' is not a branch name git accepts.");
        }

        if (Directory.Exists(directory))
        {
            bool same = Git(directory, out string own, "rev-parse", "--path-format=absolute", "--git-common-dir") == 0 && Same(own, common);
            if (same && Git(directory, out string current, "symbolic-ref", "--short", "-q", "HEAD") == 0 && current.Trim() == branch)
            {
                return new PreparedWorkspace(directory, branch, Head(directory), true);
            }

            throw new ProviderRefusedException($"The workspace of branch {branch} exists on this machine and is not a worktree of the clone on that branch; remove it, or name another branch.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        bool exists = Git(clone, out _, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch) == 0;
        string output;
        int code = exists
            ? Git(clone, out output, "worktree", "add", directory, branch)
            : Git(clone, out output, "worktree", "add", "-b", branch, directory, string.IsNullOrWhiteSpace(baseReference) ? "HEAD" : baseReference);
        if (code != 0)
        {
            throw new ProviderRefusedException($"git could not add the worktree of {branch}: {output.Trim()}");
        }

        return new PreparedWorkspace(directory, branch, Head(directory), false);
    }

    /// <inheritdoc />
    public WorkspaceSnapshot Capture(PreparedWorkspace workspace) => WorkspaceScopeProbe.Capture(workspace.Path, WorkspaceScopeAudit.Deadline, auditCommand);

    /// <inheritdoc />
    public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted) => WorkspaceScopeAudit.Compare(baseline, Capture(workspace), permitted);

    private static string Head(string directory) => Git(directory, out string head, "rev-parse", "HEAD") == 0 ? head.Trim() : "unknown";

    private static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a.Trim()).TrimEnd('/', '\\'), Path.GetFullPath(b.Trim()).TrimEnd('/', '\\'), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static int Git(string directory, out string output, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = directory };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        try
        {
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException("git did not start.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                process.Kill(entireProcessTree: true);
                throw new ProviderRefusedException($"git {arguments[0]} did not answer within two minutes.");
            }

            output = process.ExitCode == 0 ? standardOutput.GetAwaiter().GetResult() : standardError.GetAwaiter().GetResult() + standardOutput.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            throw new ProviderRefusedException("git is not on the PATH; install it to prepare workspaces.", exception);
        }
    }
}
