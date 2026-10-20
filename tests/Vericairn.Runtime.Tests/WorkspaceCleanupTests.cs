// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkspaceCleanupTests
{
    [Fact]
    public void ReadOnlyWorkspaceObjectsAreRemovedAndTheRetainedBranchCanBeRecovered()
    {
        using var run = new RunEstate();
        var provider = new GitWorktreeWorkspaces(FakeHarness.ScopeCommand());
        PreparedWorkspace workspace = provider.Prepare(run.Clone, "work/cleanup", null, Path.Combine(run.Scratch, "workspace"));
        WorkspaceSnapshot baseline = provider.Capture(workspace);
        RunEstate.Git(workspace.Path, "commit", "--allow-empty", "-q", "-m", "Retained before removal");
        string head = RunEstate.Git(workspace.Path, "rev-parse", "HEAD");
        string obj = Path.Combine(workspace.Path, ".git", "objects", head[..2], head[2..]);
        File.SetAttributes(obj, File.GetAttributes(obj) | FileAttributes.ReadOnly);
        Assert.True((File.GetAttributes(obj) & FileAttributes.ReadOnly) != 0);
        Assert.Null(provider.CheckScope(workspace, baseline, ["**"]).Problem);
        Assert.Null(provider.RetainForReview(workspace));

        WorkspaceCleanup.Delete(workspace.Path);

        Assert.False(Directory.Exists(workspace.Path));
        PreparedWorkspace recovered = new GitWorktreeWorkspaces().Prepare(run.Clone, workspace.Branch, null, workspace.Path);
        Assert.False(recovered.Reused);
        Assert.Equal(head, recovered.Commit);
        Assert.Equal(head, RunEstate.Git(run.Clone, "rev-parse", workspace.Branch));
    }

    [Fact]
    public void ADisposableGitViewRemovesReadOnlyObjects()
    {
        using var run = new RunEstate();
        var view = new WorkspaceGitView(Path.Combine(run.Clone, ".git", "objects"), RunEstate.Git(run.Clone, "rev-parse", "HEAD"));
        string file = Path.Combine(view.Path, "objects", "temporary-object");
        File.WriteAllText(file, "temporary object");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        view.Dispose();
        Assert.False(Directory.Exists(view.Path));
        Assert.True(Directory.Exists(Path.Combine(run.Clone, ".git", "objects")));
    }

    [Fact]
    public void WindowsReadOnlyAndHiddenDirectoriesAreAlsoRemoved()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows directory attributes differ from Unix directory write permissions.");
        using var run = new RunEstate();
        string workspace = Path.Combine(run.Scratch, "workspace");
        string nested = Path.Combine(workspace, ".git", "objects", "hidden");
        Directory.CreateDirectory(nested);
        string file = Path.Combine(nested, "object");
        File.WriteAllText(file, "read-only object");
        foreach (string path in new[] { file, nested, workspace })
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);

        WorkspaceCleanup.Delete(workspace);

        Assert.False(Directory.Exists(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLinksAreRemovedWithoutChangingTheClone(bool rootLink)
    {
        using var run = new RunEstate();
        string workspace = Path.Combine(run.Scratch, "workspace&literal");
        Directory.CreateDirectory(workspace);
        string marker = Path.Combine(run.Clone, ".git", "readonly-marker");
        File.WriteAllText(marker, "preserve");
        File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);
        FileAttributes original = File.GetAttributes(marker);
        string link = rootLink ? Path.Combine(run.Scratch, "workspace-link&literal") : Path.Combine(workspace, ".git");
        DirectoryLink(link, Path.Combine(run.Clone, ".git"));

        WorkspaceCleanup.Delete(rootLink ? link : workspace);

        Assert.False(Directory.Exists(link));
        Assert.Equal("preserve", File.ReadAllText(marker));
        Assert.Equal(original, File.GetAttributes(marker));
        Assert.True(Directory.Exists(Path.Combine(run.Clone, ".git", "objects")));
    }

    [Fact]
    public void FileAndDanglingLinksAreRemovedWithoutChangingTheirTargets()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic-link creation requires privileges; Windows directory junctions are covered separately.");
        using var run = new RunEstate();
        string workspace = Path.Combine(run.Scratch, "workspace");
        Directory.CreateDirectory(workspace);
        string marker = Path.Combine(run.Clone, "README.md");
        File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);
        FileAttributes original = File.GetAttributes(marker);
        File.CreateSymbolicLink(Path.Combine(workspace, "file-link"), marker);
        File.CreateSymbolicLink(Path.Combine(workspace, "dangling-file"), Path.Combine(run.Clone, "absent"));
        Directory.CreateSymbolicLink(Path.Combine(workspace, "dangling-directory"), Path.Combine(run.Clone, "absent-directory"));

        WorkspaceCleanup.Delete(workspace);

        Assert.False(Directory.Exists(workspace));
        Assert.Equal("widget\n", File.ReadAllText(marker));
        Assert.Equal(original, File.GetAttributes(marker));
        Assert.False(File.Exists(Path.Combine(run.Clone, "absent")));
        Assert.False(Directory.Exists(Path.Combine(run.Clone, "absent-directory")));
    }

    [Fact]
    public void RemovingAReadOnlyHardLinkDoesNotChangeItsOtherNames()
    {
        using var run = new RunEstate();
        string workspace = Path.Combine(run.Scratch, "workspace");
        Directory.CreateDirectory(workspace);
        string marker = Path.Combine(run.Clone, "README.md");
        File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);
        FileAttributes original = File.GetAttributes(marker);
        string name = Path.Combine(workspace, "hard-link");
        Assert.True(OperatingSystem.IsWindows() ? CreateHardLinkW(name, marker, 0) : link(marker, name) == 0);

        WorkspaceCleanup.Delete(workspace);

        Assert.False(Directory.Exists(workspace));
        Assert.Equal("widget\n", File.ReadAllText(marker));
        Assert.Equal(original, File.GetAttributes(marker));
    }

    private static void DirectoryLink(string name, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(name, target);
            return;
        }

        // Junctions require no symbolic-link privilege, so Windows CI exercises the no-follow cleanup boundary too.
        TestPrerequisites.RequireExecutable("cmd.exe", "/d", "/c", "ver");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            // One expansion inside quotes keeps spaces and shell metacharacters in temporary paths literal.
            Arguments = "/d /v:off /c mklink /J \"%VERICAIRN_TEST_JUNCTION%\" \"%VERICAIRN_TEST_TARGET%\"",
        };
        start.Environment["VERICAIRN_TEST_JUNCTION"] = name;
        start.Environment["VERICAIRN_TEST_TARGET"] = target;
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(TestTiming.HangTimeoutMilliseconds), "The fixture process did not exit.");
        Assert.True(process.ExitCode == 0, output);
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, nint security);
    [DllImport("libc", SetLastError = true)]
    private static extern int link([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
