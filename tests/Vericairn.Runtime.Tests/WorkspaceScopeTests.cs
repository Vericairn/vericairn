// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceScopeTests
{
    [Fact]
    public async Task AFifoIsRejectedLocallyWithoutOpeningItAndNoBlockedAuditThreadSurvivesTheTest()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FIFO acquisition is POSIX-specific; Windows checks disk handles without following reparses.");
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/fifo", null, Path.Combine(run.Scratch, "fifo-worktree"));
        string path = Path.Combine(workspace.Path, "README.md");
        File.Delete(path);
        Assert.Equal(0, mkfifo(path, 384));
        Task<WorkspaceSnapshot> audit = Task.Run(() => WorkspaceScopeAudit.CaptureLocal(workspace.Path, TimeSpan.FromSeconds(10)), TestContext.Current.CancellationToken);
        bool completed = false;
        try
        {
            try
            {
                _ = await audit.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
                completed = true;
            }
            catch (TimeoutException)
            {
                // A regression must fail without leaving a blocked reader in the test worker. An O_RDWR writer releases it.
                int descriptor = open(path, OperatingSystem.IsMacOS() ? 2 | 4 : 2 | 0x800);
                Assert.True(descriptor >= 0);
                _ = close(descriptor);
            }

            WorkspaceSnapshot snapshot = await audit.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(completed, "A FIFO was opened and waited for a writer.");
            Assert.NotNull(snapshot.Problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheScopeDeadlineTerminatesAStuckObserverInsteadOfLeavingABlockedWorkerThread()
    {
        using var run = new RunEstate();
        string report = run.Report("stuck-observer");
        WorkspaceSnapshot snapshot = WorkspaceScopeProbe.Capture(run.Scratch, TimeSpan.FromSeconds(2), FakeHarness.Command("scope-hang", report));
        Assert.NotNull(snapshot.Problem);
        Assert.Contains("deadline", snapshot.Problem, StringComparison.Ordinal);
        foreach (System.Text.Json.Nodes.JsonObject given in FakeHarness.Reports(report))
        {
            int pid = given["process"]!.GetValue<int>();
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
                Assert.True(process.HasExited);
            }
            catch (ArgumentException)
            {
                // The terminated observer was already reaped.
            }
        }
    }

    [Fact]
    public void RepositoryConfigurationCannotRedirectThePhysicalInventory()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        var workspace = new PreparedWorkspace(run.Clone, "main", "baseline", true);
        WorkspaceSnapshot before = provider.Capture(workspace);
        string alternate = Path.Combine(run.Scratch, "alternate");
        Directory.CreateDirectory(alternate);
        File.Copy(Path.Combine(workspace.Path, "README.md"), Path.Combine(alternate, "README.md"));
        File.WriteAllText(Path.Combine(workspace.Path, "forbidden.txt"), "outside scope");
        RunEstate.Git(workspace.Path, "config", "core.worktree", alternate);
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Contains("forbidden.txt", scope.Outside);
        Assert.Equal(1, scope.OutsideCount);
    }

    [Fact]
    public async Task SpecialFilesNeverBlockTheAuditAndItsWholeInventoryHasADeadline()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/scope", null, Path.Combine(run.Scratch, "worktree"));
        Assert.NotNull(WorkspaceScopeProbe.Capture(workspace.Path, TimeSpan.Zero, FakeHarness.ScopeCommand()).Problem);
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX FIFO regression; Windows handle classification is exercised only on Windows.
        }

        string path = Path.Combine(workspace.Path, "README.md");
        File.Delete(path);
        Assert.Equal(0, mkfifo(path, 384));
        try
        {
            Task<WorkspaceSnapshot> audit = Task.Run(() => provider.Capture(workspace), TestContext.Current.CancellationToken);
            WorkspaceSnapshot snapshot = await audit.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotNull(snapshot.Problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int mkfifo([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, int mode);
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int open([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, int flags);
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int descriptor);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    [Fact]
    public void CredentialShapedNamesAreHashedBeforeTheAuditCanEnterARecord()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
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
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
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
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
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
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
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
