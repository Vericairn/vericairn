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

namespace Vericairn.Adapters.Git;

/// <summary>
/// Workspaces with their own Git administration and objects acquired through an isolated view. The historical
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
            "Keeps index, references and objects inside the workspace, acquiring the named base commit through an isolated view. The clone's working tree and index never change; the audited branch is fetched into it after settlement, with a guarded reference update.",
            "Reuses a branch's workspace only inside the selected root. Workspaces stay for review; preserve work before deleting an independent workspace directory or removing a legacy linked worktree with git worktree remove.",
        ]);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory) =>
        Prepare(clone, branch, baseReference, directory, Path.GetDirectoryName(Path.GetFullPath(directory))!);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory, string workspaceRoot) =>
        Prepare(clone, branch, baseReference, directory, workspaceRoot, []);

    /// <inheritdoc />
    public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory, string workspaceRoot,
        IReadOnlyList<RepositoryConfigurationReview> configurationReviews)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clone);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        using GitHardening hardening = GitHardening.Create();
        AcquisitionGitView source;
        AcquisitionConfiguration settings;
        try { (source, settings) = new GitCommandAdapter().Acquire(hardening, clone, null); }
        catch (ProviderRefusedException refusal)
        {
            throw new ProviderRefusedException("The registered clone is not a git repository that could be acquired safely. " + refusal.Message, refusal);
        }

        Reviewed(settings, configurationReviews);
        string common = source.Common;
        if (Git(source.Directory, out _, "check-ref-format", "refs/heads/" + branch) != 0 || branch.StartsWith('-'))
            throw new ProviderRefusedException($"'{branch}' is not a branch name git accepts.");

        string root = CanonicalRoot(workspaceRoot);
        RefuseCheckedOut(source, branch);
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

        bool catalogued = registered is not null && Directory.Exists(registered.Path);

        string? expected = CloneHead(source.Directory, branch);
        RefuseLink(directory);
        bool reused = Directory.Exists(directory);
        WorkspaceSnapshot? unused = null;
        if (reused)
        {
            directory = WorkspacePaths.Canonical(directory);
            if (!Within(root, directory))
                throw new ProviderRefusedException($"Branch {branch} already has a workspace at '{directory}', outside the selected workspace root. Preserve its work, then remove that workspace directory, or configure a root containing it.");
            OwnRepository(directory, branch, catalogued);
            using GitHardening existing = GitHardening.Create();
            (_, AcquisitionConfiguration workspaceSettings) = new GitCommandAdapter().Acquire(existing, directory, null);
            Reviewed(workspaceSettings, configurationReviews);
        }
        else
        {
            // Check the eventual physical parent before creating any directories, including dangling-link destinations.
            string planned = CanonicalRoot(Path.GetFullPath(directory));
            if (!Within(root, planned)) throw new ProviderRefusedException("The workspace resolves outside the selected workspace root.");
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            string parent = WorkspacePaths.Canonical(Path.GetDirectoryName(directory)!);
            if (!Within(root, parent) && !Same(root, parent)) throw new ProviderRefusedException("The workspace parent resolves outside the selected workspace root.");
            string reference = (string.IsNullOrWhiteSpace(baseReference) ? "HEAD" : baseReference) + "^{commit}";
            string baseline = expected ?? WorkspaceGitCommand.Require(source.Directory, "rev-parse", "--verify", "--end-of-options", reference);
            string? baseCommit = expected is null ? baseline
                : Git(source.Directory, out string resolved, "rev-parse", "--verify", "--end-of-options", reference) == 0 ? resolved.Trim() : null;
            if (!WorkspaceGitScope.ObjectId(baseline)) throw new ProviderRefusedException("The prepared baseline is not a commit.");

            // Fetch the named commit through the disposable object view. The checkout is created only in the worker-owned workspace.
            // Publish only a complete checkout, so a failed preparation cannot be mistaken for a reusable workspace.
            // Only this fresh, worker-created repository is ever passed to Git as a working repository.
            string preparing = Path.Combine(parent, ".vericairn-preparing-" + Guid.NewGuid().ToString("N"));
            try
            {
                WorkspaceGitCommand.Require(parent, "init", "--quiet", "--template=", "--object-format=" + (baseline.Length == 64 ? "sha256" : "sha1"), preparing);
                WorkspaceGitCommand.Require(preparing, "fetch", "--no-tags", "--no-write-fetch-head", "--no-recurse-submodules",
                    "--no-auto-maintenance", "--", source.Directory, baseline);
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
            directory = WorkspacePaths.Canonical(directory);
        }

        PreparedWorkspace? workspace = null;
        try
        {
            string git = Path.Combine(directory, ".git");
            using GitHardening prepared = GitHardening.Create();
            AcquisitionGitView view = prepared.Acquire(directory);
            string head = CloneHead(view.Directory, branch) ?? throw new ProviderRefusedException("The prepared branch head is unavailable.");
            if (reused && registered is not null && Same(registered.Path, directory) && registered.CloneHead != expected && expected != head)
                throw new ProviderRefusedException("The branch in the registered clone moved unexpectedly since this workspace was prepared or retained. Preserve the workspace and reconcile the branch before retrying.");
            WorkspaceGitCommand.Require(view.Directory, "cat-file", "-e", head + "^{commit}");
            string preparation = Guid.NewGuid().ToString("N");
            workspace = new PreparedWorkspace(directory, branch, head, reused);
            var review = new Review(clone, expected, git, registration, preparation, configurationReviews) { Unused = unused };
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
    public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted) => CheckScope(workspace, baseline, permitted, []);

    /// <inheritdoc />
    public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted, IReadOnlyList<string> excluded)
    {
        if (ShallowProblem(workspace.Path) is string shallow)
        {
            if (reviews.TryGetValue(workspace, out Review? refused)) refused.Head = null;
            return new RunScope(0, 0, [], shallow);
        }

        WorkspaceSnapshot after = WorkspaceScopeProbe.Capture(workspace.Path, WorkspaceScopeAudit.Deadline, auditCommand, workspace.Branch, workspace.Commit, baseline.GitDirectory,
            startExclude: baseline.ExcludeHash);
        RunScope scope = WorkspaceScopeAudit.Compare(baseline, after, permitted, excluded);
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
            // A transfer's sender and receiver both use disposable, runtime-owned administration.
            if (ShallowProblem(workspace.Path) is string shallow) return shallow + " Preserve the workspace for review.";
            using GitHardening source = GitHardening.Create();
            AcquisitionGitView sender = source.Acquire(workspace.Path);
            if (CloneHead(sender.Directory, workspace.Branch) != review.Head)
                return "The workspace branch was not fetched: its head changed after the scope audit. Preserve the workspace for review.";

            using GitHardening target = GitHardening.Create();
            (AcquisitionGitView destination, AcquisitionConfiguration settings) = new GitCommandAdapter().Acquire(target, review.Clone, null, includeObjects: false);
            Reviewed(settings, review.ConfigurationReviews);
            if (CloneHead(destination.Directory, workspace.Branch) != review.Expected)
                return "The workspace branch was not fetched: the branch in the registered clone moved unexpectedly after preparation. Preserve the workspace and reconcile the branch.";
            RefuseCheckedOut(destination, workspace.Branch);

            string staging = Path.Combine(target.Control, "transfer");
            WorkspaceGitCommand.Require(target.Control, "init", "--quiet", "--bare", "--template=", "--object-format=" + settings.ObjectFormat, staging);
            bool staged = StageNewObjects(sender.Directory, staging, Path.Combine(target.Control, "packed"), review.Head, ClonedCommits(destination.Directory));

            beforeReferenceUpdate?.Invoke();
            // Use Git's ref lock convention, so concurrent Git writers cannot overwrite this comparison.
            string refName = "refs/heads/" + workspace.Branch;
            string reference = Path.Combine(destination.Common, refName);
            string locked = reference + ".lock";
            string packedLock = Path.Combine(destination.Common, "packed-refs.lock");
            AcquisitionWorkingTree.CreateDirectories(destination.Boundary, Path.GetDirectoryName(reference)!);
            using var next = new MemoryStream(Encoding.ASCII.GetBytes(review.Head + "\n"));
            AcquisitionWorkingTree.WriteNew(destination.Boundary, locked, next);
            bool committed = false;
            bool packedLocked = false;
            try
            {
                AcquisitionWorkingTree.WriteNew(destination.Boundary, packedLock, Stream.Null);
                packedLocked = true;
                using GitHardening final = GitHardening.Create();
                (AcquisitionGitView latest, AcquisitionConfiguration latestSettings) = new GitCommandAdapter().Acquire(final, review.Clone, null, includeObjects: false);
                Reviewed(latestSettings, review.ConfigurationReviews);
                if (!Same(latest.Common, destination.Common) || CloneHead(latest.Directory, workspace.Branch) != review.Expected
                    || Git(latest.Directory, out _, "symbolic-ref", "--quiet", refName) == 0)
                    return "The workspace branch was not retained: the registered clone refused the guarded branch update, possibly because the branch moved during the fetch. Preserve the workspace and reconcile the branch.";
                if (latest.HasPackedReferenceConflict(refName))
                    return "The workspace branch was not retained: a packed branch name conflicts with the requested branch. Preserve the workspace and choose a different branch name.";
                RefuseCheckedOut(latest, workspace.Branch);
                if (staged) PublishPacks(staging, destination);
                AcquisitionWorkingTree.Move(destination.Boundary, locked, reference, overwrite: true);
                committed = true;
            }
            finally
            {
                try { if (packedLocked) AcquisitionWorkingTree.RemoveFile(destination.Boundary, packedLock); }
                finally { if (!committed) AcquisitionWorkingTree.RemoveFile(destination.Boundary, locked); }
            }
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

    /// <summary>
    /// Refuses a directory at the workspace's location that is not this branch's own repository of the clone, naming it and why: one
    /// the catalogue registered and someone altered, or one the worker never prepared, such as a clone made by hand.
    /// </summary>
    private static void OwnRepository(string directory, string branch, bool registered)
    {
        string git = Path.Combine(directory, ".git");
        if (File.Exists(git) && (File.GetAttributes(git) & FileAttributes.ReparsePoint) == 0)
            throw new ProviderRefusedException($"Branch {branch} has a legacy linked worktree at '{directory}'. Preserve its committed and uncommitted work, then use git worktree remove with that path from the registered clone. Retry to prepare an independent workspace repository.");
        if (Unowned() is not string why) return;
        throw new ProviderRefusedException(registered
            ? $"The registered workspace of branch {branch} at '{directory}' was altered: {why} Preserve its work, then remove that directory or name another branch."
            : $"'{directory}' exists but is not a registered workspace of branch {branch}: {why} The worker prepares and registers each workspace itself; to install dependencies before the agent starts, prepare one with vericairn worker run --prepare-only and install there. Preserve anything in that directory, then remove it or name another branch.");

        string? Unowned()
        {
            if (!Path.Exists(git)) return "it has no .git directory.";
            FileAttributes attributes = File.GetAttributes(git);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return "its .git is a symbolic link or reparse point.";
            if ((attributes & FileAttributes.Directory) == 0) return "its .git is a file pointing elsewhere, not its own repository directory.";
            if (Path.Exists(Path.Combine(git, "commondir"))) return "its .git shares another repository's administration (.git/commondir).";
            if (!registered) return "it has no worker preparation registration.";
            if (Path.Exists(Path.Combine(git, "objects", "info", "alternates"))) return "its Git object store has an alternate.";
            string head;
            try { head = WorkspaceGitScope.Read(Path.Combine(git, "HEAD"), 4096).Trim(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return "its .git/HEAD is missing or unreadable."; }
            return head == "ref: refs/heads/" + branch ? null : $"its HEAD is not on branch {branch}.";
        }
    }

    private static void Reviewed(AcquisitionConfiguration settings, IReadOnlyList<RepositoryConfigurationReview> reviews)
    {
        if (settings.Findings.Count == 0 || reviews.Contains(new RepositoryConfigurationReview(settings.ConfigurationHash, settings.WorktreeConfigurationHash))) return;
        string worktree = settings.WorktreeConfigurationHash is { } hash ? $" and worktree configuration {hash}" : string.Empty;
        throw new ProviderRefusedException($"Workspace refused pending a person's signed review of Git configuration {settings.ConfigurationHash}{worktree}. Ignored keys: {string.Join(", ", settings.Findings)}.");
    }

    private static void RefuseCheckedOut(AcquisitionGitView source, string branch)
    {
        if (!source.CheckedOut(branch, out bool selected)) return;
        throw new ProviderRefusedException(selected
            ? $"Branch {branch} is checked out in the registered clone; choose a work branch so its working tree stays unchanged."
            : $"Branch {branch} has a legacy linked worktree. Preserve its committed and uncommitted work, then use git worktree remove from the registered clone before retrying.");
    }

    /// <summary>
    /// Stages only the objects the reviewed head adds to what the clone holds: everything reachable from it and from none of the
    /// commits the clone's own branches, remote branches and tags name, since Git keeps everything those reach. A pack is made in
    /// the sender's runtime-owned view, then indexed and checked object by object by a separate Git process in the staging
    /// repository. A commit the workspace has but the clone does not, such as one a reused workspace never retained, is packed with
    /// the rest. False when the clone already holds everything the head reaches, as for a branch moved back.
    /// </summary>
    private static bool StageNewObjects(string sender, string staging, string scratch, string head, IReadOnlyList<string> cloned)
    {
        Directory.CreateDirectory(scratch);
        // Only the clone's commits the sender also has can be excluded by the sender's Git.
        HashSet<string> held = [];
        if (cloned.Count > 0 && WorkspaceGitCommand.RunWithInput(sender, out string present, TimeSpan.FromMinutes(2),
            string.Concat(cloned.Select(commit => commit + "^{commit}\n")), "cat-file", "--batch-check=%(objectname)") == 0)
            held = [.. present.Split('\n').Where(line => line.Length is 40 or 64 && line.All(char.IsAsciiHexDigitLower))];
        string revisions = head + "\n" + string.Concat(held.Where(commit => commit != head).Select(commit => "^" + commit + "\n"));

        if (WorkspaceGitCommand.RunWithInput(sender, out string reachable, TimeSpan.FromMinutes(10), revisions, "rev-list", "--objects", "--stdin") != 0)
            throw new IOException("The workspace's new objects could not be listed for the registered clone.");
        if (reachable.Trim().Length == 0) return false;

        if (WorkspaceGitCommand.RunWithInput(sender, out string name, TimeSpan.FromMinutes(10), revisions,
            "pack-objects", "--revs", "--quiet", Path.Combine(scratch, "pack")) != 0 || name.Trim() is not { Length: 40 or 64 } packName)
            throw new IOException("The workspace's new objects could not be packed for the registered clone.");

        string pack = Path.Combine(staging, "objects", "pack", "pack-" + packName + ".pack");
        File.Copy(Path.Combine(scratch, "pack-" + packName + ".pack"), pack);
        WorkspaceGitCommand.Require(staging, "index-pack", "--fsck-objects", pack);
        WorkspaceGitCommand.Require(staging, "cat-file", "-e", head + "^{commit}");
        return true;
    }

    // The commits the registered clone's branches, remote branches and tags name, read from its copied references alone.
    private static IReadOnlyList<string> ClonedCommits(string clone) =>
        Git(clone, out string output, "for-each-ref", "--format=%(objectname)", "--count=5000", "refs/heads", "refs/remotes", "refs/tags") == 0
            ? [.. output.Split('\n').Select(line => line.Trim()).Where(line => line.Length is 40 or 64).Distinct(StringComparer.Ordinal)]
            : [];

    // A worker's workspace is an independent repository Vericairn prepared; a shallow file in it can only be the agent's, and would
    // hide commits from the audit and the transfer.
    private static string? ShallowProblem(string workspace)
    {
        string git = Path.Combine(workspace, ".git");
        if (File.Exists(git)) return "The workspace's .git is a file, not the independent repository Vericairn prepared; its scope cannot be audited.";
        return File.Exists(Path.Combine(git, "shallow"))
            ? "The workspace holds a shallow file, which Vericairn never writes and which would hide commits from the audit; its scope cannot be audited."
            : null;
    }

    private static void PublishPacks(string staging, AcquisitionGitView destination)
    {
        string packs = Path.Combine(destination.Common, "objects", "pack");
        AcquisitionWorkingTree.CreateDirectories(destination.Boundary, packs);
        string[] staged = [.. Directory.EnumerateFiles(Path.Combine(staging, "objects", "pack")).Where(f => Path.GetExtension(f) is ".pack" or ".idx")];
        // A pack's name is the hash of the objects it holds, so a pack of that name already in the clone holds these objects, though
        // its bytes may differ: such a pack is left whole, its pack and index never replaced or mixed with this one's. Decided before
        // anything is published.
        HashSet<string> held = [.. staged.Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct(StringComparer.Ordinal).Where(stem =>
        {
            using var heldPack = AcquisitionWorkingTree.TryOpenRegular(destination.Boundary, Path.Combine(packs, stem + ".pack"));
            using var heldIndex = AcquisitionWorkingTree.TryOpenRegular(destination.Boundary, Path.Combine(packs, stem + ".idx"));
            return heldPack is not null || heldIndex is not null;
        })];
        foreach (string file in staged.OrderByDescending(Path.GetExtension, StringComparer.Ordinal))
        {
            if (held.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            string member = Path.Combine(packs, Path.GetFileName(file));
            using var bytes = File.OpenRead(file);

            string temporary = Path.Combine(packs, ".vericairn-" + Guid.NewGuid().ToString("N"));
            AcquisitionWorkingTree.WriteNew(destination.Boundary, temporary, bytes);
            try { AcquisitionWorkingTree.Move(destination.Boundary, temporary, member, overwrite: false); }
            catch
            {
                AcquisitionWorkingTree.RemoveFile(destination.Boundary, temporary);
                throw;
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
        string path = WorkspacePaths.Canonical(parent.FullName);
        foreach (string name in missing) path = Path.Combine(path, name);
        return path;
    }

    private static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    private static int Git(string directory, out string output, params string[] arguments) => WorkspaceGitCommand.Run(directory, out output, arguments);

    private sealed record Location(string Path, string? CloneHead, string? Preparation = null);

    private sealed class Review(string clone, string? expected, string gitDirectory, string registration, string preparation,
        IReadOnlyList<RepositoryConfigurationReview> configurationReviews)
    {
        internal string Clone { get; } = clone;
        internal string? Expected { get; set; } = expected;
        internal string GitDirectory { get; } = gitDirectory;
        internal string Registration { get; } = registration;
        internal string Preparation { get; } = preparation;
        internal bool Registered { get; set; }
        internal IReadOnlyList<RepositoryConfigurationReview> ConfigurationReviews { get; } = configurationReviews;
        internal string? Head { get; set; }
        internal WorkspaceSnapshot? Unused { get; init; }
    }
}
