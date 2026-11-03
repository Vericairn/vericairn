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
        RunEstate.Git(Path.GetDirectoryName(workspace)!, "clone", "-q", "-b", "main", run.Clone, workspace);
        RunEstate.Git(workspace, "checkout", "-q", "-b", "work/w-1");
        File.WriteAllText(Path.Combine(workspace, "installed.txt"), "installed by hand\n");
        int records = run.Estate.Load().ById.Count;

        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Contains($"'{HarnessWorkspace.Canonical(workspace)}' exists but is not a registered workspace of branch work/w-1: it does not borrow the registered clone's objects", refused.Message, StringComparison.Ordinal);
        Assert.Contains("a repository made with git clone is not a worker workspace", refused.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn worker run --prepare-only", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not a regular file", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.True(File.Exists(Path.Combine(workspace, "installed.txt")));
    }

    [Theory]
    [InlineData("head", "its HEAD is not on branch work/w-1.")]
    [InlineData("alternates", "its .git/objects/info/alternates names objects other than the registered clone's.")]
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
