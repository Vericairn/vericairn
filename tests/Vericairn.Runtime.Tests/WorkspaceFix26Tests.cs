// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceFix26Tests
{
    [Fact]
    public void IgnoredOutputAndRunFilesAreExcludedWhileTrackedAndUntrackedEditsRemainVisible()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        File.WriteAllText(Path.Combine(run.Clone, "tracked.cache"), "tracked baseline");
        RunEstate.Git(run.Clone, "add", "-f", "tracked.cache");
        RunEstate.Git(run.Clone, "commit", "-q", "-m", "Track a file matching an ignore rule");
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/ignored", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceMetadata.Write(workspace.Path, "brief before");
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        Assert.DoesNotContain(".vericairn/brief.md", before.Files.Keys);
        FakeHarness.WriteLargeIgnoredOutput(workspace.Path);
        File.WriteAllText(Path.Combine(workspace.Path, "tracked.cache"), "tracked change");
        File.WriteAllText(Path.Combine(workspace.Path, "outside.txt"), "untracked change");
        WorkspaceMetadata.Write(workspace.Path, "brief after");
        File.Delete(Path.Combine(workspace.Path, ".vericairn", ".gitignore"));
        File.WriteAllText(Path.Combine(workspace.Path, ".vericairn", "outside.txt"), "an ordinary untracked file");
        RunEstate.Git(workspace.Path, "update-index", "--assume-unchanged", "tracked.cache");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal([".vericairn/outside.txt", "outside.txt", "tracked.cache"], scope.Outside);
        Assert.Equal(3, scope.Changed);
    }

    [Fact]
    public void ACommittedIgnoredFileAndTrackedRunFileStillCountOutsideTheBrief()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/tracked", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "artifacts"));
        File.WriteAllText(Path.Combine(workspace.Path, "artifacts", "tracked.txt"), "committed output");
        WorkspaceMetadata.Write(workspace.Path, "tracked brief");
        RunEstate.Git(workspace.Path, "add", "-f", "artifacts/tracked.txt", ".vericairn/brief.md");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "Force tracked files");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal([".vericairn/brief.md", "artifacts/tracked.txt"], scope.Outside);
    }

    [Fact]
    public void SettlementFetchesThePermittedCommitDespiteOversizedIgnoredBuildOutput()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        RunResponse started = run.Start("codex", "workspace-commit-ignored", run.Report("retention"));
        Assert.True(started.Ok, started.Message);

        RunRow settled = run.Settled(started.Run);

        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Scope!.Problem);
        Assert.Equal(1, settled.Scope.Changed);
        Assert.Empty(settled.Scope.Outside);
        Assert.DoesNotContain(settled.Signals!, signal => signal.Code == "scope-settlement");
        string retained = RunEstate.Git(run.Clone, "rev-parse", "refs/heads/work/w-1");
        Assert.NotEqual(RunEstate.Git(run.Clone, "rev-parse", "HEAD"), retained);
        Assert.Equal("permitted work", RunEstate.Git(run.Clone, "show", retained + ":widget/permitted.txt"));
        Assert.False(Directory.Exists(Path.Combine(run.Clone, "artifacts")));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ACompleteCommittedAuditRemainsAvailableWhenTheFileInventoryIsIncomplete()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/review", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "widget"));
        File.WriteAllText(Path.Combine(workspace.Path, "widget", "permitted.txt"), "permitted");
        RunEstate.Git(workspace.Path, "add", "widget");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "Permitted work");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "nested", ".git"));

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.NotNull(scope.Problem);
        Assert.Null(provider.RetainForReview(workspace));
        Assert.Equal(RunEstate.Git(workspace.Path, "rev-parse", "HEAD"), RunEstate.Git(run.Clone, "rev-parse", "refs/heads/" + workspace.Branch));
    }

    private static void IgnoreOutput(RunEstate run)
    {
        File.WriteAllText(Path.Combine(run.Clone, ".gitignore"), "artifacts/\n*.cache\n");
        RunEstate.Git(run.Clone, "add", ".gitignore");
        RunEstate.Git(run.Clone, "commit", "-q", "-m", "Ignore build output");
    }

}
