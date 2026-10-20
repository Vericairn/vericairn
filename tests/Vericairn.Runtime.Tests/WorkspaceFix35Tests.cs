// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Until 0.8.1 settlement excluded ignored output by the workspace's own ignore files, info/exclude and index as the agent
/// left them, so an agent could hide a new file by adding a rule, and a nested repository under an excluded directory was
/// skipped silently.
/// </summary>
[Collection("home")]
public class WorkspaceFix35Tests
{
    [Fact]
    public void AnIgnoreRuleAddedDuringTheRunHidesNothingAndIsReported()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/hide");
        File.AppendAllText(Path.Combine(workspace.Path, ".gitignore"), "hidden.txt\n");
        File.WriteAllText(Path.Combine(workspace.Path, "hidden.txt"), "a change the agent hid");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal([".gitignore", "hidden.txt"], scope.Outside);
    }

    [Fact]
    public void ARuleInANewNestedIgnoreFileHidesNothingAndIsReportedEvenWhenPermitted()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/nested-rule");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "widget"));
        File.WriteAllText(Path.Combine(workspace.Path, "widget", ".gitignore"), "*\n");
        File.WriteAllText(Path.Combine(workspace.Path, "widget", "source.cs"), "permitted work");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal(["widget/.gitignore"], scope.Outside);
        Assert.Equal(2, scope.Changed);
    }

    [Fact]
    public void AnInfoExcludeRuleAddedDuringTheRunHidesNothingAndIsReported()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/exclude");
        string exclude = Path.Combine(RunEstate.Git(workspace.Path, "rev-parse", "--absolute-git-dir"), "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(exclude)!);
        File.AppendAllText(exclude, "hidden.txt\n");
        File.WriteAllText(Path.Combine(workspace.Path, "hidden.txt"), "a change the agent hid");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal([".git/info/exclude", "hidden.txt"], scope.Outside);
    }

    [Fact]
    public void UntrackingACommittedFileDoesNotLetAStartingRuleHideIt()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        File.WriteAllText(Path.Combine(run.Clone, "tracked.cache"), "tracked baseline");
        RunEstate.Git(run.Clone, "add", "-f", "tracked.cache");
        RunEstate.Git(run.Clone, "commit", "-q", "-m", "Track a file matching an ignore rule");
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/untrack");
        File.WriteAllText(Path.Combine(workspace.Path, "tracked.cache"), "tracked change");
        RunEstate.Git(workspace.Path, "rm", "-q", "--cached", "tracked.cache");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Equal(["tracked.cache"], scope.Outside);
    }

    [Fact]
    public void StartingRulesStillExcludeIgnoredOutputWithoutReportingAChange()
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/output");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "artifacts", "bin"));
        File.WriteAllText(Path.Combine(workspace.Path, "artifacts", "bin", "widget.dll"), "build output");
        File.WriteAllText(Path.Combine(workspace.Path, "widget.cache"), "build output");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "widget"));
        File.WriteAllText(Path.Combine(workspace.Path, "widget", "source.cs"), "permitted work");

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Null(scope.Problem);
        Assert.Empty(scope.Outside);
        Assert.Equal(1, scope.Changed);
    }

    [Theory]
    [InlineData("artifacts/inner")]
    [InlineData("artifacts/deep/inner")]
    [InlineData("widget/inner")]
    public void ANestedRepositoryAnywhereMarksTheAuditIncomplete(string location)
    {
        using var run = new RunEstate();
        IgnoreOutput(run);
        (GitWorktreeWorkspaces provider, PreparedWorkspace workspace, WorkspaceSnapshot before) = Prepare(run, "work/nested");
        RunEstate.Git(workspace.Path, "init", "-q", location);

        RunScope scope = provider.CheckScope(workspace, before, ["widget/**"]);

        Assert.Equal("A submodule or nested repository needs a separate scope audit; assignment scope is incomplete.", scope.Problem);
    }

    private static (GitWorktreeWorkspaces Provider, PreparedWorkspace Workspace, WorkspaceSnapshot Before) Prepare(RunEstate run, string branch)
    {
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, branch, null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceMetadata.Write(workspace.Path, "brief");
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        return (provider, workspace, before);
    }

    private static void IgnoreOutput(RunEstate run)
    {
        File.WriteAllText(Path.Combine(run.Clone, ".gitignore"), "artifacts/\n*.cache\n");
        RunEstate.Git(run.Clone, "add", ".gitignore");
        RunEstate.Git(run.Clone, "commit", "-q", "-m", "Ignore build output");
    }
}
