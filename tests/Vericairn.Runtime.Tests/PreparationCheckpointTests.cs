// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The local checkpoint between a preparation alone and its run (FIX-86): an altered, shared or stale one refuses the run before its
/// intent, naming the checkpoint and the remedy, and preparing again replaces a stale one.
/// </summary>
[Collection("home")]
public sealed class PreparationCheckpointTests
{
    [Fact]
    public void AnAlteredOrSharedCheckpointRefusesTheRunAndNamesIt()
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        RunResponse prepared = run.Service.RunPrepare(PrepareOnlyWorkspaceTests.Request(run, run.Report("prepared")));
        Assert.True(prepared.Ok, prepared.Message);
        string checkpoint = PrepareOnlyWorkspaceTests.Checkpoint(run);
        string saved = File.ReadAllText(checkpoint);
        int records = run.Estate.Load().ById.Count;

        File.WriteAllText(checkpoint, saved.Replace("\"commit\":\"", "\"commit\":\"not-", StringComparison.Ordinal));
        RunResponse altered = run.Start("claude-code", "claude", run.Report("altered"));
        Assert.False(altered.Ok);
        Assert.Contains($"The workspace preparation checkpoint '{checkpoint}' cannot be trusted (it is unreadable or was altered)", altered.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn worker run --prepare-only", altered.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was recorded.", altered.Message, StringComparison.Ordinal);

        File.WriteAllText(checkpoint, saved);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(checkpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            RunResponse shared = run.Start("claude-code", "claude", run.Report("shared"));
            Assert.False(shared.Ok);
            Assert.Contains($"'{checkpoint}' cannot be trusted (it is readable or writable by other users)", shared.Message, StringComparison.Ordinal);
            File.SetUnixFileMode(checkpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.True(Directory.Exists(prepared.Workspace));

        RunResponse started = run.Start("claude-code", "claude", run.Report("started"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        Assert.False(File.Exists(checkpoint));
    }

    [Fact]
    public void AStaleCheckpointRefusesTheRunAndPreparingAgainReplacesIt()
    {
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        RunResponse prepared = run.Service.RunPrepare(PrepareOnlyWorkspaceTests.Request(run, run.Report("prepared")));
        Assert.True(prepared.Ok, prepared.Message);
        string workspace = prepared.Workspace!;
        string checkpoint = PrepareOnlyWorkspaceTests.Checkpoint(run);
        int records = run.Estate.Load().ById.Count;

        // The person removed the prepared workspace. The run prepares a new one, which no installation reached.
        WorkspaceCleanup.Delete(workspace);
        RunResponse stale = run.Start("claude-code", "claude", run.Report("stale"));
        Assert.False(stale.Ok);
        Assert.Contains($"The preparation checkpoint '{checkpoint}' no longer describes this branch's workspace: the workspace it was made for was removed or replaced", stale.Message, StringComparison.Ordinal);
        Assert.Contains("Prepare it again with vericairn worker run --prepare-only", stale.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);
        // The workspace that refused run made, still unused, is gone again; the checkpoint is the person's to replace.
        Assert.False(Directory.Exists(workspace));
        Assert.True(File.Exists(checkpoint));

        RunResponse again = run.Service.RunPrepare(PrepareOnlyWorkspaceTests.Request(run, run.Report("again")));
        Assert.True(again.Ok, again.Message);
        Assert.Contains("no longer described its workspace", again.Message, StringComparison.Ordinal);
        Assert.Contains("was replaced", again.Message, StringComparison.Ordinal);
        PrepareOnlyWorkspaceTests.Install(again.Workspace!);

        RunResponse started = run.Start("claude-code", "claude", run.Report("started"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        Assert.True(File.Exists(Path.Combine(again.Workspace!, "node_modules", "left-pad", "index.js")));
        Assert.False(File.Exists(checkpoint));
    }

    [Fact]
    public void ACheckpointThatIsALinkIsNeverFollowed()
    {
        if (OperatingSystem.IsWindows()) return;
        using RunEstate run = PrepareOnlyWorkspaceTests.Ignoring(new RunEstate());
        string checkpoint = PrepareOnlyWorkspaceTests.Checkpoint(run);
        string target = Path.Combine(run.Scratch, "elsewhere.json");
        File.WriteAllText(target, "{}");
        Directory.CreateDirectory(Path.GetDirectoryName(checkpoint)!);
        File.CreateSymbolicLink(checkpoint, target);

        RunResponse refused = run.Service.RunPrepare(PrepareOnlyWorkspaceTests.Request(run, run.Report("prepared")));

        Assert.False(refused.Ok);
        Assert.Contains("is a symbolic link or reparse point", refused.Message, StringComparison.Ordinal);
        Assert.Equal("{}", File.ReadAllText(target));
        Assert.Empty(run.Service.Runs().Runs);
    }
}
