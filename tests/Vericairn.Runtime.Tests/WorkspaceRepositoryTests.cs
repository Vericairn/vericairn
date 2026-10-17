// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceRepositoryTests
{
    [Fact]
    public void AWorkspaceOwnsItsIndexReferencesAndNewObjects()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/own", null, Path.Combine(run.Scratch, "workspace"));
        Assert.True(Directory.Exists(Path.Combine(workspace.Path, ".git")));
        Assert.Equal(HarnessWorkspace.Canonical(Path.Combine(workspace.Path, ".git")),
            RunEstate.Git(workspace.Path, "rev-parse", "--path-format=absolute", "--git-common-dir"));
        Assert.Equal(HarnessWorkspace.Canonical(Path.Combine(run.Clone, ".git", "objects")),
            File.ReadAllText(Path.Combine(workspace.Path, ".git", "objects", "info", "alternates")).Trim());
        File.WriteAllText(Path.Combine(workspace.Path, "allowed.txt"), "an agent's commit\n");
        RunEstate.Git(workspace.Path, "add", "allowed.txt");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "Permitted work");
        string head = RunEstate.Git(workspace.Path, "rev-parse", "HEAD");
        Assert.NotEqual(workspace.Commit, head);
        Assert.Equal(workspace.Commit, RunEstate.Git(run.Clone, "rev-parse", "HEAD"));
        Assert.Empty(RunEstate.Git(run.Clone, "branch", "--list", workspace.Branch));
        Assert.Empty(RunEstate.Git(workspace.Path, "remote"));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".git", "objects", head[..2], head[2..])));
        Assert.False(File.Exists(Path.Combine(run.Clone, ".git", "objects", head[..2], head[2..])));
    }

    [Fact]
    public void PreparingAWorkspaceNeverExecutesTheClonesCheckoutHook()
    {
        using var run = new RunEstate();
        string hooks = Path.Combine(run.Scratch, "hooks");
        Directory.CreateDirectory(hooks);
        string marker = Path.Combine(run.Scratch, "hook-ran");
        string hook = Path.Combine(hooks, "post-checkout");
        File.WriteAllText(hook, "#!/bin/sh\nprintf ran > '" + marker.Replace("'", "'\\''", StringComparison.Ordinal) + "'\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RunEstate.Git(run.Clone, "config", "core.hooksPath", hooks);
        var provider = new GitWorktreeWorkspaces();
        _ = provider.Prepare(run.Clone, "work/hook", null, Path.Combine(run.Scratch, "workspace"));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void TheWorkerNeverLoadsWorkspaceConfigurationWhenResumingAuditingOrFetching()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/hostile", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "outside the brief\n");
        RunEstate.Git(workspace.Path, "add", ".");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "Outside change");
        string head = RunEstate.Git(workspace.Path, "rev-parse", "HEAD");
        string marker = Path.Combine(run.Scratch, "configuration-ran");
        string command = "printf ran > '" + marker.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        foreach (string key in new[] { "core.fsmonitor", "diff.external", "diff.hostile.textconv", "filter.hostile.clean", "filter.hostile.smudge",
            "filter.hostile.process", "core.pager", "core.sshCommand", "core.alternateRefsCommand", "uploadpack.packObjectsHook" })
            RunEstate.Git(workspace.Path, "config", key, command);
        string hooks = Path.Combine(workspace.Path, ".git", "hostile-hooks");
        Directory.CreateDirectory(hooks);
        foreach (string name in new[] { "post-checkout", "reference-transaction", "pre-auto-gc" })
        {
            string hook = Path.Combine(hooks, name);
            File.WriteAllText(hook, "#!/bin/sh\n" + command + "\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        RunEstate.Git(workspace.Path, "config", "core.hooksPath", hooks);
        // A deliberately unreadable format proves even non-executing workspace configuration is not parsed.
        RunEstate.Git(workspace.Path, "config", "core.repositoryformatversion", "999");
        Assert.True(provider.Prepare(run.Clone, workspace.Branch, null, workspace.Path).Reused);
        RunScope scope = provider.CheckScope(workspace, baseline, ["widget/**"]);
        Assert.Null(scope.Problem);
        Assert.Contains("README.md", scope.Outside);
        Assert.Null(provider.RetainForReview(workspace));
        Assert.Equal(head, RunEstate.Git(run.Clone, "rev-parse", "refs/heads/" + workspace.Branch));
        Assert.False(File.Exists(marker));
        Assert.Equal(workspace.Commit, RunEstate.Git(run.Clone, "rev-parse", "HEAD"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetentionRefusesACloneBranchThatMovedOrAppeared(bool existed)
    {
        using var run = new RunEstate();
        const string branch = "work/moved";
        if (existed) RunEstate.Git(run.Clone, "branch", branch);
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, branch, null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        RunEstate.Git(workspace.Path, "commit", "--allow-empty", "-q", "-m", "Agent work");
        RunEstate.Git(run.Clone, "commit", "--allow-empty", "-q", "-m", "Concurrent operator work");
        string moved = RunEstate.Git(run.Clone, "rev-parse", "HEAD");
        RunEstate.Git(run.Clone, "update-ref", "refs/heads/" + branch, moved);
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Contains("moved unexpectedly", provider.RetainForReview(workspace), StringComparison.Ordinal);
        Assert.Equal(moved, RunEstate.Git(run.Clone, "rev-parse", "refs/heads/" + branch));
    }

    [Fact]
    public void RetentionRequiresTheAuditedHeadAndCanBeRepeated()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/pinned", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        Assert.Contains("audit", ((IWorkspaceProvider)provider).RetainForReview(workspace), StringComparison.Ordinal);
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        RunEstate.Git(workspace.Path, "commit", "--allow-empty", "-q", "-m", "After the audit");
        Assert.Contains("changed after the scope audit", provider.RetainForReview(workspace), StringComparison.Ordinal);
        Assert.Empty(RunEstate.Git(run.Clone, "branch", "--list", workspace.Branch));
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Null(provider.RetainForReview(workspace));
        Assert.Null(provider.RetainForReview(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABranchMoveDuringFetchCannotBeOverwritten(bool deleted)
    {
        using var run = new RunEstate();
        const string branch = "work/race";
        RunEstate.Git(run.Clone, "branch", branch);
        string? moved = null;
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand(), beforeReferenceUpdate: () =>
        {
            if (deleted) RunEstate.Git(run.Clone, "branch", "-D", branch);
            else
            {
                RunEstate.Git(run.Clone, "commit", "--allow-empty", "-q", "-m", "Concurrent move during fetch");
                moved = RunEstate.Git(run.Clone, "rev-parse", "HEAD");
                RunEstate.Git(run.Clone, "update-ref", "refs/heads/" + branch, moved);
            }
        });
        PreparedWorkspace workspace = provider.Prepare(run.Clone, branch, null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        RunEstate.Git(workspace.Path, "commit", "--allow-empty", "-q", "-m", "Agent change");
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Contains("guarded branch update", provider.RetainForReview(workspace), StringComparison.Ordinal);
        if (deleted) Assert.Empty(RunEstate.Git(run.Clone, "branch", "--list", branch));
        else Assert.Equal(moved, RunEstate.Git(run.Clone, "rev-parse", "refs/heads/" + branch));
    }

    [Fact]
    public void RecoveryRefusesAnUnexpectedCloneBranchMoveBetweenRuns()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/between", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Null(provider.RetainForReview(workspace));
        RunEstate.Git(run.Clone, "commit", "--allow-empty", "-q", "-m", "Operator work between runs");
        string moved = RunEstate.Git(run.Clone, "rev-parse", "HEAD");
        RunEstate.Git(run.Clone, "update-ref", "refs/heads/" + workspace.Branch, moved);
        string refusal = Assert.Throws<ProviderRefusedException>(() => new GitWorktreeWorkspaces().Prepare(run.Clone, workspace.Branch, null, workspace.Path)).Message;
        Assert.Contains("moved unexpectedly", refusal, StringComparison.Ordinal);
        Assert.Equal(moved, RunEstate.Git(run.Clone, "rev-parse", "refs/heads/" + workspace.Branch));
    }

    [Fact]
    public void PreparationCannotFollowADanglingWorkspaceLinkIntoTheClone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic-link creation requires a Windows privilege not granted by this fixture.");
        using var run = new RunEstate();
        string workspace = Path.Combine(run.Scratch, "workspace-link");
        string target = Path.Combine(run.Clone, ".git", "planted");
        Directory.CreateSymbolicLink(workspace, target);
        try
        {
            Assert.Throws<ProviderRefusedException>(() => new GitWorktreeWorkspaces().Prepare(run.Clone, "work/link", null, workspace));
            Assert.False(Directory.Exists(target));
        }
        finally { File.Delete(workspace); }
    }

    [Fact]
    public void AFailedPreparationLeavesNoPartialWorkspaceAndCanBeRetried()
    {
        using var run = new RunEstate();
        const string branch = "work/retry";
        string reference = Path.Combine(run.Clone, ".git", "refs", "heads", "work", "retry");
        Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
        // A damaged clone can name a commit whose object is missing. Failure must not publish an empty checkout.
        File.WriteAllText(reference, new string('1', 40) + "\n");
        string root = Path.Combine(run.Scratch, "workspaces");
        string path = Path.Combine(root, "workspace");
        var provider = new GitWorktreeWorkspaces();
        Assert.Throws<ProviderRefusedException>(() => provider.Prepare(run.Clone, branch, null, path, root));
        Assert.False(Directory.Exists(path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        File.Delete(reference);
        PreparedWorkspace workspace = provider.Prepare(run.Clone, branch, null, path, root);
        Assert.False(workspace.Reused);
        Assert.True(File.Exists(Path.Combine(workspace.Path, "README.md")));
        Assert.Equal(RunEstate.Git(run.Clone, "rev-parse", "HEAD"), workspace.Commit);
    }

    [Fact]
    public void ReopeningAndCleaningAnIndependentWorkspacePreservesItsSettledBranch()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/recover", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        RunEstate.Git(workspace.Path, "commit", "--allow-empty", "-q", "-m", "Retain this commit");
        File.WriteAllText(Path.Combine(workspace.Path, "uncommitted.txt"), "keep this too");
        var reopened = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace recovered = reopened.Prepare(run.Clone, workspace.Branch, null, workspace.Path);
        Assert.True(recovered.Reused);
        Assert.True(File.Exists(Path.Combine(recovered.Path, "uncommitted.txt")));
        Assert.NotEqual(workspace.Commit, recovered.Commit);
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Null(provider.RetainForReview(workspace));
        // Cleanup is the operator's, after preserving uncommitted files. No worktree registration needs removal.
        Delete(workspace.Path);
        PreparedWorkspace fresh = reopened.Prepare(run.Clone, workspace.Branch, null, workspace.Path);
        Assert.False(fresh.Reused);
        Assert.Equal(recovered.Commit, fresh.Commit);
        Assert.Single(RunEstate.Git(run.Clone, "worktree", "list", "--porcelain").Split("worktree ", StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALegacyWorkspaceIsRecognizedPreservedAndRecreatedAfterOperatorCleanup(bool outsideRoot)
    {
        using var run = new RunEstate();
        string old = Path.Combine(run.Scratch, "legacy");
        RunEstate.Git(run.Clone, "worktree", "add", "-b", "work/legacy", old);
        RunEstate.Git(old, "commit", "--allow-empty", "-q", "-m", "Legacy work");
        string head = RunEstate.Git(old, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(old, "uncommitted.txt"), "preserve before cleanup");
        var provider = new GitWorktreeWorkspaces();
        string root = outsideRoot ? Path.Combine(run.Scratch, "new-root") : run.Scratch;
        string path = Path.Combine(root, "workspace");
        string refusal = Assert.Throws<ProviderRefusedException>(() => provider.Prepare(run.Clone, "work/legacy", null, path, root)).Message;
        Assert.Contains("legacy linked worktree", refusal, StringComparison.Ordinal);
        Assert.Contains("git worktree remove", refusal, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(old, "uncommitted.txt")));
        File.Move(Path.Combine(old, "uncommitted.txt"), Path.Combine(run.Scratch, "saved.txt"));
        RunEstate.Git(run.Clone, "worktree", "remove", old);
        PreparedWorkspace fresh = provider.Prepare(run.Clone, "work/legacy", null, path, root);
        Assert.Equal(head, fresh.Commit);
        Assert.True(Directory.Exists(Path.Combine(fresh.Path, ".git")));
    }

    [Fact]
    public void AWorkerRunCommitsLocallyAndFetchesItsSettledBranch()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("generic", "workspace-commit", run.Report("committing"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        RunObservedState? observed = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).Read();
        Assert.Equal("observed-success", observed?.Settled);
        Assert.Null(observed?.Problem);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Scope!.Problem);
        Assert.Equal(0, settled.Scope.OutsideCount);
        Assert.DoesNotContain(settled.Signals ?? [], signal => signal.Summary.Contains("not fetched", StringComparison.Ordinal) || signal.Summary.Contains("not retained", StringComparison.Ordinal));
        Assert.Equal("permitted work", RunEstate.Git(run.Clone, "show", "work/w-1:widget/permitted.txt"));
        Assert.Equal("main", RunEstate.Git(run.Clone, "branch", "--show-current"));
        Assert.Empty(RunEstate.Git(run.Clone, "status", "--porcelain"));
    }

    [Fact]
    public void APostSettlementTransferRefusalIsVisibleWithoutChangingTheSettlement()
    {
        using var run = new RunEstate();
        run.Service.Providers.Register(new HookedLauncher(new ReportedLauncher(new GenericLauncher()),
            command: () => RunEstate.Git(run.Clone, "branch", "work/w-1")));
        RunResponse started = run.Start("generic", "workspace-commit", run.Report("refused-transfer"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        RunSignal refusal = Assert.Single(settled.Signals!, s => s.Summary.Contains("moved unexpectedly", StringComparison.Ordinal));
        Assert.Equal("warning", refusal.Severity);
        Assert.DoesNotContain(run.Scratch, refusal.Summary, StringComparison.Ordinal);
        Assert.Equal(RunEstate.Git(run.Clone, "rev-parse", "HEAD"), RunEstate.Git(run.Clone, "rev-parse", "work/w-1"));
        run.Reopen();
        Assert.Contains(run.Row(started.Run).Signals!, s => s.Summary == refusal.Summary);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void CodexWritableRootsNeverIncludeTheRegisteredClone()
    {
        using var run = new RunEstate();
        PreparedWorkspace workspace = new GitWorktreeWorkspaces().Prepare(run.Clone, "work/sandbox", null, Path.Combine(run.Scratch, "workspace"));
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var context = new LaunchContext(workspace.Path, "Commit the permitted change.", "brief.md", "mcp.json", ["vericairn"], empty, empty, empty,
            Execution: RunEstate.Json("""{"requested":{"model":"model-a","effort":"high"}}"""));
        IReadOnlyList<string> command = new CodexLauncher().Command(context);
        Assert.Equal(workspace.Path, command[command.ToList().IndexOf("--cd") + 1]);
        Assert.Equal("workspace-write", command[command.ToList().IndexOf("--sandbox") + 1]);
        Assert.DoesNotContain("--add-dir", command);
        Assert.DoesNotContain(command, value => value.Contains(run.Clone, StringComparison.Ordinal));
        Assert.DoesNotContain(command, value => value.Contains("writable_roots", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(workspace.Path, ".git")));
    }

    private static void Delete(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }
}
