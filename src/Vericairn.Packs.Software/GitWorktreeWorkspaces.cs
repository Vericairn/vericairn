// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>
/// Workspaces with their own Git administration and read-only object alternates from the registered clone. The historical
/// provider name is retained for compatibility. No agent-controlled Git configuration is loaded by the worker.
/// </summary>
public sealed partial class GitWorktreeWorkspaces : IWorkspaceProvider
{
    private readonly IReadOnlyList<string>? auditCommand;
    private readonly Action? beforeReferenceUpdate;
    private readonly ConditionalWeakTable<PreparedWorkspace, Review> reviews = new();

    /// <summary>Uses the installed Vericairn command as the isolated scope observer.</summary>
    public GitWorktreeWorkspaces() { }

    internal GitWorktreeWorkspaces(IReadOnlyList<string> auditCommand, Action? beforeReferenceUpdate = null)
    {
        this.auditCommand = auditCommand;
        this.beforeReferenceUpdate = beforeReferenceUpdate;
    }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "git-worktree", IWorkspaceProvider.Contract, "implemented", ["prepare"],
        [
            "Requires git on the PATH. The historical provider id is retained; new workspaces are independent repositories.",
            "Keeps index, references and new objects inside the workspace, borrowing the registered clone's objects read-only. The clone's working tree and index never change; the audited branch is fetched into it after settlement, with a guarded reference update.",
            "Reuses a branch's workspace only inside the selected root. Workspaces stay for review; preserve work before deleting an independent workspace directory or removing a legacy linked worktree with git worktree remove.",
        ]);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory) =>
        Prepare(clone, branch, baseReference, directory, Path.GetDirectoryName(Path.GetFullPath(directory))!);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory, string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clone);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (Git(clone, out string common, "rev-parse", "--path-format=absolute", "--git-common-dir") != 0)
            throw new ProviderRefusedException("The registered clone is not a git repository with this git.");
        common = HarnessWorkspace.Canonical(common.TrimEnd('\r', '\n'));
        if (Git(clone, out _, "check-ref-format", "refs/heads/" + branch) != 0 || branch.StartsWith('-'))
            throw new ProviderRefusedException($"'{branch}' is not a branch name git accepts.");

        string root = CanonicalRoot(workspaceRoot);
        RefuseCheckedOut(clone, branch, root);
        string registration = Registration(common, branch);
        Location? registered = null;
        if (File.Exists(registration))
        {
            try
            {
                registered = JsonSerializer.Deserialize<Location>(WorkspaceGitScope.Read(registration, 65536))
                    ?? throw new JsonException();
                if (string.IsNullOrWhiteSpace(registered.Path) || !Path.IsPathFullyQualified(registered.Path)
                    || registered.CloneHead is not null && !WorkspaceGitScope.ObjectId(registered.CloneHead)
                    || registered.Preparation is not null && !Guid.TryParseExact(registered.Preparation, "N", out _)) throw new JsonException();
            }
            catch (JsonException exception)
            {
                throw new ProviderRefusedException("The local workspace registration is unreadable; preserve the workspace and repair its registration before retrying.", exception);
            }
            if (Directory.Exists(registered.Path)) directory = registered.Path;
        }

        string? expected = CloneHead(clone, branch);
        RefuseLink(directory);
        bool reused = Directory.Exists(directory);
        WorkspaceSnapshot? unused = null;
        if (reused)
        {
            directory = HarnessWorkspace.Canonical(directory);
            if (!Within(root, directory))
                throw new ProviderRefusedException($"Branch {branch} already has a workspace at '{directory}', outside the selected workspace root. Preserve its work, then remove that workspace directory, or configure a root containing it.");
            OwnRepository(directory, common, branch);
        }
        else
        {
            // Check the eventual physical parent before creating any directories, including dangling-link destinations.
            string planned = CanonicalRoot(Path.GetFullPath(directory));
            if (!Within(root, planned)) throw new ProviderRefusedException("The workspace resolves outside the selected workspace root.");
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            string parent = HarnessWorkspace.Canonical(Path.GetDirectoryName(directory)!);
            if (!Within(root, parent) && !Same(root, parent)) throw new ProviderRefusedException("The workspace parent resolves outside the selected workspace root.");
            string reference = (string.IsNullOrWhiteSpace(baseReference) ? "HEAD" : baseReference) + "^{commit}";
            string baseline = expected ?? WorkspaceGitCommand.Require(clone, "rev-parse", "--verify", "--end-of-options", reference);
            string? baseCommit = expected is null ? baseline
                : Git(clone, out string resolved, "rev-parse", "--verify", "--end-of-options", reference) == 0 ? resolved.Trim() : null;
            if (!WorkspaceGitScope.ObjectId(baseline)) throw new ProviderRefusedException("The prepared baseline is not a commit.");

            // Equivalent object sharing to clone --shared, without importing remotes, templates or repository configuration.
            // Publish only a complete checkout, so a failed preparation cannot be mistaken for a reusable workspace.
            // Only this fresh, worker-created repository is ever passed to Git as a working repository.
            string preparing = Path.Combine(parent, ".vericairn-preparing-" + Guid.NewGuid().ToString("N"));
            try
            {
                WorkspaceGitCommand.Require(parent, "init", "--quiet", "--template=", "--object-format=" + (baseline.Length == 64 ? "sha256" : "sha1"), preparing);
                File.WriteAllText(Path.Combine(preparing, ".git", "objects", "info", "alternates"), Path.Combine(common, "objects").Replace('\\', '/') + "\n");
                WorkspaceGitCommand.Require(preparing, "update-ref", "refs/heads/" + branch, baseline);
                WorkspaceGitCommand.Require(preparing, "symbolic-ref", "HEAD", "refs/heads/" + branch);
                WorkspaceGitCommand.Require(preparing, "reset", "--hard", baseline);
                // Pin the complete repository before it is published. Index, other refs, reflogs and unreachable objects
                // can hold work even when HEAD and the ordinary working-tree inventory still match the base.
                if (baseline == baseCommit)
                    unused = WorkspaceScopeProbe.Capture(preparing, WorkspaceScopeAudit.Deadline, auditCommand, includeGit: true);
                Directory.Move(preparing, directory);
            }
            finally
            {
                if (Directory.Exists(preparing)) WorkspaceCleanup.Delete(preparing);
            }
            directory = HarnessWorkspace.Canonical(directory);
        }

        PreparedWorkspace? workspace = null;
        try
        {
            string git = Path.Combine(directory, ".git");
            string head = WorkspaceGitScope.Head(git, "refs/heads/" + branch);
            if (reused && registered is not null && Same(registered.Path, directory) && registered.CloneHead != expected && expected != head)
                throw new ProviderRefusedException("The branch in the registered clone moved unexpectedly since this workspace was prepared or retained. Preserve the workspace and reconcile the branch before retrying.");
            using (var view = new WorkspaceGitView(Path.Combine(git, "objects"), head))
                WorkspaceGitCommand.Require(view.Path, "cat-file", "-e", head + "^{commit}");
            string preparation = Guid.NewGuid().ToString("N");
            workspace = new PreparedWorkspace(directory, branch, head, reused);
            var review = new Review(clone, expected, git, registration, preparation) { Unused = unused };
            reviews.Add(workspace, review);
            Register(registration, new Location(directory, expected, preparation));
            review.Registered = true;
            return workspace;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException)
        {
            string? retained = workspace is null ? "Its completed preparation could not be verified." : DiscardUnused(workspace, null, string.Empty);
            string guidance = retained is null ? string.Empty
                : $" Workspace kept at '{directory}'. {retained} Preserve its commits and files before removing it or choosing a root that contains it.";
            throw new ProviderRefusedException(exception.Message + guidance, exception);
        }
    }

    /// <inheritdoc />
    public WorkspaceSnapshot Capture(PreparedWorkspace workspace) => WorkspaceScopeProbe.Capture(workspace.Path, WorkspaceScopeAudit.Deadline, auditCommand);

    /// <inheritdoc />
    public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted)
    {
        WorkspaceSnapshot after = WorkspaceScopeProbe.Capture(workspace.Path, WorkspaceScopeAudit.Deadline, auditCommand, workspace.Branch, workspace.Commit, baseline.GitDirectory);
        RunScope scope = WorkspaceScopeAudit.Compare(baseline, after, permitted);
        if (reviews.TryGetValue(workspace, out Review? review))
            review.Head = baseline.Identity is not null && baseline.Identity == after.Identity
                && Same(review.GitDirectory, baseline.GitDirectory ?? string.Empty) ? after.Head : null;
        return scope;
    }

    /// <inheritdoc />
    public string? RetainForReview(PreparedWorkspace workspace)
    {
        if (!reviews.TryGetValue(workspace, out Review? review) || review.Head is null)
            return "The workspace branch was not fetched: its committed scope audit is unavailable or incomplete. Preserve the workspace for review.";
        try
        {
            // Never fetch from the agent's repository itself: upload-pack would load its configuration. The disposable
            // source exposes only the audited object id and object storage, never hooks, includes, remotes or helpers.
            if (WorkspaceGitScope.Head(review.GitDirectory, "refs/heads/" + workspace.Branch) != review.Head)
                return "The workspace branch was not fetched: its head changed after the scope audit. Preserve the workspace for review.";
            if (CloneHead(review.Clone, workspace.Branch) != review.Expected)
                return "The workspace branch was not fetched: the branch in the registered clone moved unexpectedly after preparation. Preserve the workspace and reconcile the branch before retrying.";
            RefuseCheckedOut(review.Clone, workspace.Branch, null);
            using var view = new WorkspaceGitView(Path.Combine(review.GitDirectory, "objects"), review.Head);
            WorkspaceGitCommand.Require(review.Clone, "fetch", "--no-tags", "--no-write-fetch-head", "--no-recurse-submodules", "--no-auto-maintenance", "--", view.Path, "refs/heads/review");
            // Fetch objects first, then compare-and-swap the destination ref. A concurrent move, creation or deletion
            // cannot be overwritten, including a move to a descendant of the expected head.
            RefuseCheckedOut(review.Clone, workspace.Branch, null);
            beforeReferenceUpdate?.Invoke();
            if (Git(review.Clone, out _, "update-ref", "--no-deref", "refs/heads/" + workspace.Branch, review.Head,
                review.Expected ?? new string('0', review.Head.Length)) != 0)
                return "The workspace branch was not retained: the registered clone refused the guarded branch update, possibly because the branch moved during the fetch. Preserve the workspace and reconcile the branch.";
            review.Expected = review.Head;
            try { Register(review.Registration, new Location(workspace.Path, review.Head, review.Preparation)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return "The workspace branch was fetched, but its local recovery registration could not be saved. Preserve the workspace; preparation can recover when the clone still has this head.";
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException)
        {
            // Git diagnostics may contain local paths or configuration. This message is safe for the existing run signal.
            return "The workspace branch was not fetched: Git could not safely read its objects or update the registered clone; the branch may be checked out there. Preserve the workspace for review.";
        }
    }

    private static void OwnRepository(string directory, string common, string branch)
    {
        string git = Path.Combine(directory, ".git");
        if (File.Exists(git) && Same(WorkspaceGitScope.DirectoryOf(directory), common))
            throw new ProviderRefusedException($"Branch {branch} has a legacy linked worktree at '{directory}'. Preserve its committed and uncommitted work, then use git worktree remove with that path from the registered clone. Retry to prepare an independent workspace repository.");
        if (!Directory.Exists(git) || (File.GetAttributes(git) & FileAttributes.ReparsePoint) != 0 || Path.Exists(Path.Combine(git, "commondir")))
            throw new ProviderRefusedException($"The workspace of branch {branch} at '{directory}' exists and is not an independent repository of the clone on that branch. Preserve it, then remove it or name another branch.");
        string alternate = WorkspaceGitScope.Read(Path.Combine(git, "objects", "info", "alternates"), 32768).TrimEnd('\r', '\n');
        if (!Same(Path.GetFullPath(alternate), Path.Combine(common, "objects"))
            || WorkspaceGitScope.Read(Path.Combine(git, "HEAD"), 4096).Trim() != "ref: refs/heads/" + branch)
            throw new ProviderRefusedException($"The workspace of branch {branch} at '{directory}' is not an independent repository of the clone on that branch. Preserve it, then remove it or name another branch.");
    }

    private static void RefuseCheckedOut(string clone, string branch, string? root)
    {
        string inventory = WorkspaceGitCommand.Require(clone, "worktree", "list", "--porcelain", "-z");
        string? existing = null;
        foreach (string field in inventory.Split('\0'))
        {
            if (field.StartsWith("worktree ", StringComparison.Ordinal)) existing = Path.GetFullPath(field[9..]);
            else if (field == "branch refs/heads/" + branch && existing is not null)
            {
                string physical = Directory.Exists(existing) ? HarnessWorkspace.Canonical(existing) : existing;
                if (Same(physical, HarnessWorkspace.Canonical(clone)))
                    throw new ProviderRefusedException($"Branch {branch} is checked out in the registered clone; choose a work branch so its working tree stays unchanged.");
                string where = root is not null && !Within(root, physical) ? ", outside the selected workspace root" : string.Empty;
                throw new ProviderRefusedException($"Branch {branch} has a legacy linked worktree at '{physical}'{where}. Preserve its committed and uncommitted work, then use git worktree remove with that path from the registered clone. Retry to prepare an independent workspace repository.");
            }
        }
    }

    private static string? CloneHead(string clone, string branch) => Git(clone, out string head, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch) == 0 ? head.Trim() : null;

    private static string Registration(string common, string branch) => Path.Combine(VericairnHome.Path(), "workspaces",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((OperatingSystem.IsLinux() ? common : common.ToUpperInvariant()) + "\n" + branch))) + ".json");

    private static void Register(string registration, Location location)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(registration)!);
        // Replace rather than opening an existing entry for writing; the catalogue is outside every agent workspace.
        string temporary = registration + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(location));
            File.Move(temporary, registration, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static bool Within(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != "." && !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void RefuseLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ProviderRefusedException("The workspace is a symbolic link or reparse point; no repository was prepared through it.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static string CanonicalRoot(string directory)
    {
        var missing = new Stack<string>();
        var parent = new DirectoryInfo(Path.GetFullPath(directory));
        while (!parent.Exists)
        {
            RefuseLink(parent.FullName);
            missing.Push(parent.Name);
            parent = parent.Parent ?? throw new ProviderRefusedException("The workspace root could not be canonicalized.");
        }
        string path = HarnessWorkspace.Canonical(parent.FullName);
        foreach (string name in missing) path = Path.Combine(path, name);
        return path;
    }

    private static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    private static int Git(string directory, out string output, params string[] arguments) => WorkspaceGitCommand.Run(directory, out output, arguments);

    private sealed record Location(string Path, string? CloneHead, string? Preparation = null);

    private sealed class Review(string clone, string? expected, string gitDirectory, string registration, string preparation)
    {
        internal string Clone { get; } = clone;
        internal string? Expected { get; set; } = expected;
        internal string GitDirectory { get; } = gitDirectory;
        internal string Registration { get; } = registration;
        internal string Preparation { get; } = preparation;
        internal bool Registered { get; set; }
        internal string? Head { get; set; }
        internal WorkspaceSnapshot? Unused { get; init; }
    }
}
