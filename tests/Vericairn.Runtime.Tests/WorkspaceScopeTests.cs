// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceScopeTests
{
    [Fact]
    public void CredentialShapedNamesAreHashedBeforeTheAuditCanEnterARecord()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces();
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/scope", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        string synthetic = "ghp_" + new string('x', 36) + ".txt";
        File.WriteAllText(Path.Combine(workspace.Path, synthetic), "a stand-in name, never a credential");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Equal(1, scope.OutsideCount);
        Assert.StartsWith("path with unportable name, sha256:", Assert.Single(scope.Outside), StringComparison.Ordinal);
        Assert.DoesNotContain(synthetic, Assert.Single(scope.Outside), StringComparison.Ordinal);
    }

    [Fact]
    public void AReplacementCharacterInTheInventoryNeverProducesASilentCleanAudit()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces();
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/scope", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "\ufffd.txt"), "a possibly undecodable name");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["**"]);
        Assert.NotNull(scope.Problem);
        Assert.Contains("losslessly", scope.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeUsesTheLaunchInventoryAndExactPathBoundaries()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces();
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/scope", null, Path.Combine(run.Scratch, "worktree"));
        File.WriteAllText(Path.Combine(workspace.Path, "preexisting.txt"), "from the prior attempt");
        WorkspaceSnapshot before = provider.Capture(workspace);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src-other"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", "deep"));
        File.WriteAllText(Path.Combine(workspace.Path, "src", "yes.cs"), "allowed");
        File.WriteAllText(Path.Combine(workspace.Path, "src-other", "no.cs"), "outside");
        File.WriteAllText(Path.Combine(workspace.Path, "docs", "yes.md"), "allowed");
        File.WriteAllText(Path.Combine(workspace.Path, "docs", "deep", "no.md"), "outside");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["path:src", "docs/*.md"]);
        Assert.Equal(4, scope.Changed);
        Assert.Equal(["docs/deep/no.md", "src-other/no.cs"], scope.Outside);
        Assert.Equal(2, scope.OutsideCount);
        Assert.Null(scope.Problem);
    }

    [Fact]
    public void UnsafeOrAbsentPermissionsNeverGrantChangesAndAnUnavailableInventoryIsFlagged()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces();
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/scope", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "outside.txt"), "outside");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["../**", "/**"]);
        Assert.Equal(["outside.txt"], scope.Outside);
        Assert.NotNull(scope.Problem);
        File.Move(Path.Combine(workspace.Path, ".git"), Path.Combine(workspace.Path, ".git-missing"));
        scope = provider.CheckScope(workspace, before, ["**"]);
        Assert.NotNull(scope.Problem);
        Assert.Contains("unverified", scope.Problem, StringComparison.Ordinal);
    }
}
