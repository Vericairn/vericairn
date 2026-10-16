// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedChangesRemainVisibleAfterRestorationInTheWorktreeOrALaterCommit(bool commitRestoration)
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/committed", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "outside the permitted paths\n");
        File.WriteAllText(Path.Combine(workspace.Path, "transient.txt"), "committed and then deleted\n");
        RunEstate.Git(workspace.Path, "add", ".");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "An outside change");
        RunEstate.Git(workspace.Path, "restore", "--source=" + workspace.Commit, "--staged", "--worktree", ".");
        if (commitRestoration)
        {
            RunEstate.Git(workspace.Path, "commit", "-q", "-m", "Restore the baseline");
        }

        // Neither a redirected worktree nor a hostile diff driver can hide committed paths or execute a command.
        string marker = Path.Combine(run.Scratch, "diff-driver-ran");
        RunEstate.Git(run.Clone, "config", "core.worktree", run.Clone);
        RunEstate.Git(run.Clone, "config", "diff.external", "touch " + marker);
        RunEstate.Git(run.Clone, "config", "core.repositoryformatversion", "999");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Null(scope.Problem);
        Assert.Equal(["README.md", "transient.txt"], scope.Outside);
        Assert.Equal(2, scope.Changed);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void TheRunBranchIsAuditedEvenWhenTheWorktreeChecksOutTheBaselineDetached()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/detached", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "outside\n");
        RunEstate.Git(workspace.Path, "add", ".");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "An outside change");
        RunEstate.Git(workspace.Path, "checkout", "-q", "--detach", workspace.Commit);
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Null(scope.Problem);
        Assert.Equal(["README.md"], scope.Outside);
    }

    [Fact]
    public void PackedBranchHeadsAreAuditedAndMissingHistoryIsIncomplete()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/packed", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "outside\n");
        RunEstate.Git(workspace.Path, "add", ".");
        RunEstate.Git(workspace.Path, "commit", "-q", "-m", "An outside change");
        RunEstate.Git(workspace.Path, "restore", "--source=" + workspace.Commit, "--worktree", ".");
        RunEstate.Git(run.Clone, "pack-refs", "--all");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Null(scope.Problem);
        Assert.Equal(["README.md"], scope.Outside);
        Assert.NotNull(provider.CheckScope(workspace with { Commit = new string('0', 40) }, before, ["**"]).Problem);
    }

    [Fact]
    public async Task ASpecialFileInTheBranchReferenceCannotBlockHistoryInspection()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FIFO acquisition is POSIX-specific.");
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/ref-fifo", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        string reference = Path.Combine(run.Clone, ".git", "refs", "heads", "work", "ref-fifo");
        File.Delete(reference);
        Assert.Equal(0, mkfifo(reference, 384));
        try
        {
            Task<Protocol.RunScope> audit = Task.Run(() => provider.CheckScope(workspace, before, ["**"]), TestContext.Current.CancellationToken);
            Assert.NotNull((await audit.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Problem);
        }
        finally
        {
            File.Delete(reference);
        }
    }

    [Fact]
    public void ScopeComparisonAlsoHasADeadlineAndCannotReportACleanResultAfterItExpires()
    {
        var before = new WorkspaceSnapshot(new Dictionary<string, string>(StringComparer.Ordinal));
        var after = new WorkspaceSnapshot(new Dictionary<string, string>(StringComparer.Ordinal) { ["outside.txt"] = "changed" });
        Protocol.RunScope scope = WorkspaceScopeAudit.Compare(before, after, ["src/**"], TimeSpan.Zero);
        Assert.NotNull(scope.Problem);
        Assert.Contains("limit", scope.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacingThePhysicalWorkspaceCannotProduceACleanScopeAudit()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/replaced", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        string displaced = workspace.Path + "-displaced";
        Directory.Move(workspace.Path, displaced);
        Directory.CreateDirectory(workspace.Path);
        File.Copy(Path.Combine(displaced, "README.md"), Path.Combine(workspace.Path, "README.md"));
        File.Copy(Path.Combine(displaced, ".git"), Path.Combine(workspace.Path, ".git"));
        File.WriteAllText(Path.Combine(displaced, "forbidden.txt"), "The agent's original workspace was moved.");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.NotNull(scope.Problem);
        Assert.Contains("identity", scope.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacingGitAdministrationCannotHideFilesInThePhysicalWorkspace()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/git-file", null, Path.Combine(run.Scratch, "worktree"));
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        string administration = Path.Combine(workspace.Path, ".git");
        // Git for Windows can mark this file hidden; CREATE_ALWAYS cannot overwrite an existing hidden file.
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(administration, File.GetAttributes(administration) | FileAttributes.Hidden);
        }

        File.Delete(administration);
        File.WriteAllText(administration, "gitdir: missing-administration\n");
        File.WriteAllText(Path.Combine(workspace.Path, "forbidden.txt"), "outside scope");
        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Null(scope.Problem);
        Assert.Contains("forbidden.txt", scope.Outside);
    }

    [Fact]
    public void HiddenWorkspaceFilesAreStillComparedWithoutChangingTheirAttributes()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/hidden-file", null, Path.Combine(run.Scratch, "worktree"));
        string hidden = Path.Combine(workspace.Path, ".hidden.txt");
        File.WriteAllText(hidden, "before");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden | FileAttributes.System);
        }

        FileAttributes attributes = File.GetAttributes(hidden);
        WorkspaceSnapshot before = provider.Capture(workspace);
        Assert.Null(before.Problem);
        using (var changed = new FileStream(hidden, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            changed.SetLength(0);
            changed.Write("after"u8);
        }

        Protocol.RunScope scope = provider.CheckScope(workspace, before, ["src/**"]);
        Assert.Null(scope.Problem);
        Assert.Equal([".hidden.txt"], scope.Outside);
        Assert.Equal(attributes, File.GetAttributes(hidden));
    }

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
