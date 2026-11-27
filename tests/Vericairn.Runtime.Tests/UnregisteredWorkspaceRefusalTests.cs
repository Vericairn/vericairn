// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A directory at a workspace's location that the worker did not prepare, or that was altered, is refused by its path and why (FIX-86,
/// issue 238), where a run said only that a scope entry was not a regular file. Nothing is recorded and the directory stays.
/// </summary>
[Collection("home")]
public sealed class UnregisteredWorkspaceRefusalTests
{
    [Fact]
    public void ACloneMadeByHandAtTheWorkspacesLocationIsNamedAndNotRegistered()
    {
        using var run = new RunEstate();
        string workspace = Path.Combine(VericairnHome.Path(), "estates", run.Service.Session.Manifest.Estate, "workspaces", "work-w-1");
        Directory.CreateDirectory(Path.GetDirectoryName(workspace)!);
        RunEstate.Git(Path.GetDirectoryName(workspace)!, "clone", "-q", "--no-hardlinks", "-b", "main", run.Clone, workspace);
        RunEstate.Git(workspace, "checkout", "-q", "-b", "work/w-1");
        File.WriteAllText(Path.Combine(workspace, "installed.txt"), "installed by hand\n");
        int records = run.Estate.Load().ById.Count;

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        // From 0.9.5 (CODE-88) a workspace is an independent repository the worker registers, so a clone made by hand has no registration.
        Assert.Contains($"'{HarnessWorkspace.Canonical(workspace)}' exists but is not a registered workspace of branch work/w-1: it has no worker preparation registration", refused.Message, StringComparison.Ordinal);
        Assert.Contains("The worker prepares and registers each workspace itself", refused.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn worker run --prepare-only", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not a regular file", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.True(File.Exists(Path.Combine(workspace, "installed.txt")));
    }

    [Fact]
    public void AHardLinkedCloneOfTheRegisteredCloneRefusesItWithTheIndependentCloneRemedy()
    {
        using var run = new RunEstate();
        // A local git clone hard-links the objects it copies, so the registered clone's object files gain a link outside its boundary.
        RunEstate.Git(run.Scratch, "clone", "-q", run.Clone, Path.Combine(run.Scratch, "linked"));
        int records = run.Estate.Load().ById.Count;

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Contains("The registered clone is not a git repository that could be acquired safely.", refused.Message, StringComparison.Ordinal);
        Assert.Contains("hard links are refused. Its files are shared through hard links with another local clone, made from it or it from another: remove that clone, or replace this one with an independent clone made with git clone --no-hardlinks, and retry.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
    }

    [Theory]
    [InlineData("head", "its HEAD is not on branch work/w-1.")]
    [InlineData("alternates", "its Git object store has an alternate.")]
    [InlineData("git", "it has no .git directory.")]
    public void ARegisteredWorkspaceThatWasAlteredIsNamedWithWhatChanged(string alteration, string why)
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        RunResponse prepared = run.Service.RunPrepare(PrepareOnlyWorkspaceTests.Request(run, run.Report("prepared")));
        Assert.True(prepared.Ok, prepared.Message);
        string workspace = prepared.Workspace!;
        string git = Path.Combine(workspace, ".git");
        switch (alteration)
        {
            case "head":
                File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
                break;
            case "alternates":
                File.WriteAllText(Path.Combine(git, "objects", "info", "alternates"), Path.Combine(run.Scratch, "other", "objects").Replace('\\', '/') + "\n");
                break;
            default:
                Directory.Move(git, Path.Combine(run.Scratch, "moved-git"));
                break;
        }

        int records = run.Estate.Load().ById.Count;
        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Contains($"The registered workspace of branch work/w-1 at '{workspace}' was altered: {why}", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.True(Directory.Exists(workspace));
    }
}
