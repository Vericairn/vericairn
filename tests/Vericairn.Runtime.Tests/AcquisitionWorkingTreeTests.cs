// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class AcquisitionWorkingTreeTests : IDisposable
{
    private readonly string scratch = Path.Combine(WorkspacePaths.Canonical(Path.GetTempPath()), "vericairn-acquisition-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("symbolic-file", "symbolic link")]
    [InlineData("symbolic-directory", "symbolic link")]
    [InlineData("hard-link", "hard links")]
    [InlineData("pipe", "non-regular file")]
    [InlineData("oversize", "file size")]
    public void InspectRefusesTheUnsafeEntryByName(string kind, string reason)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() && kind is "symbolic-file" or "symbolic-directory" or "pipe",
            "Creating symlinks needs Windows privileges and a FIFO is POSIX-specific.");
        string repository = Repository();
        string outside = Path.Combine(scratch, "outside");
        Directory.CreateDirectory(outside);
        string marker = Path.Combine(outside, "marker.txt");
        File.WriteAllText(marker, "outside the repository\n");
        string name = Path.Combine(repository, kind);
        switch (kind)
        {
            case "symbolic-file":
                File.CreateSymbolicLink(name, marker);
                break;
            case "symbolic-directory":
                Directory.CreateSymbolicLink(name, outside);
                break;
            case "hard-link":
                Assert.True(OperatingSystem.IsWindows() ? CreateHardLinkW(name, marker, 0) : link(marker, name) == 0);
                break;
            case "pipe":
                Assert.Equal(0, mkfifo(name, 0x180));
                break;
            case "oversize":
                using (FileStream stream = File.Create(name)) stream.SetLength(64L * 1024 * 1024 + 1);
                break;
        }

        var adapter = new GitCommandAdapter();
        ProviderRefusedException refusal = Assert.Throws<ProviderRefusedException>(() => adapter.Inspect(repository));
        Assert.Contains("\"" + kind + "\"", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyPreservesRegularBytesAndExecutableModeWithoutCarryingGitAdministration()
    {
        string repository = Repository();
        string original = Path.Combine(repository, "tracked.txt");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(original, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        string mirror = Path.Combine(scratch, "mirror");

        AcquisitionWorkingTree.Copy(repository, mirror);

        string copied = Path.Combine(mirror, "tracked.txt");
        Assert.Equal("initial\n", File.ReadAllText(copied));
        Assert.False(Path.Exists(Path.Combine(mirror, ".git")));
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(copied).HasFlag(UnixFileMode.UserExecute));
        File.WriteAllText(copied, "mirror only\n");
        Assert.Equal("initial\n", File.ReadAllText(original));
    }

    [Fact]
    public void OpenRegularRefusesOutsidePathsAndLinkedAncestors()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symlinks needs Windows privileges.");
        string repository = Repository();
        string outside = Path.Combine(scratch, "other");
        Directory.CreateDirectory(outside);
        string marker = Path.Combine(outside, "marker.txt");
        File.WriteAllText(marker, "outside the repository\n");
        Directory.CreateSymbolicLink(Path.Combine(repository, "linked"), outside);

        Assert.Throws<IOException>(() => AcquisitionWorkingTree.OpenRegular(repository, marker));
        Assert.Throws<IOException>(() => AcquisitionWorkingTree.OpenRegular(repository, Path.Combine("linked", "marker.txt")));
    }

    [Fact]
    public void APopulatedNestedGitDirectoryIsRefusedBeforeCopyingItsContents()
    {
        string repository = Repository();
        string nested = Path.Combine(repository, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "ordinary.txt"), "nested repository contents\n");
        File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: unused-administration\n");
        string mirror = Path.Combine(scratch, "mirror");

        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => AcquisitionWorkingTree.Copy(repository, mirror));

        Assert.Contains("nested/.git", refused.Message, StringComparison.Ordinal);
        Assert.False(Path.Exists(Path.Combine(mirror, "nested", "ordinary.txt")));
    }

    [Fact]
    public void InspectTreatsPackageLifecycleScriptsAsOrdinaryFiles()
    {
        string repository = Repository();
        File.WriteAllText(Path.Combine(repository, "package.json"), """{"name":"acquisition-fixture","scripts":{"install":"unused-install-entry"}}""");
        File.WriteAllText(Path.Combine(repository, "unused-install-entry"), "installer fixture data\n");
        var adapter = new GitCommandAdapter(info =>
        {
            Assert.Equal("git", info.FileName);
            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });

        RepositoryObservation observation = adapter.Inspect(repository);

        Assert.Equal(0, observation.Modified);
        Assert.Equal(2, observation.Untracked);
    }

    [Fact]
    public void PublicationStagesExclusivelyThenMovesAnOrdinaryFile()
    {
        Directory.CreateDirectory(scratch);
        AcquisitionWorkingTree.CreateDirectories(scratch, "refs/heads");
        const string target = "refs/heads/review";
        const string stage = target + ".lock";
        using var first = new MemoryStream("first\n"u8.ToArray());
        using var second = new MemoryStream("second\n"u8.ToArray());

        AcquisitionWorkingTree.WriteNew(scratch, stage, first);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(scratch, stage)));
        Assert.Throws<IOException>(() => AcquisitionWorkingTree.WriteNew(scratch, stage, second));
        AcquisitionWorkingTree.Move(scratch, stage, target, overwrite: false);
        Assert.False(File.Exists(Path.Combine(scratch, stage)));
        Assert.Equal("first\n", File.ReadAllText(Path.Combine(scratch, target)));

        AcquisitionWorkingTree.WriteNew(scratch, stage, second);
        Assert.Throws<IOException>(() => AcquisitionWorkingTree.Move(scratch, stage, target, overwrite: false));
        Assert.Equal("first\n", File.ReadAllText(Path.Combine(scratch, target)));
        AcquisitionWorkingTree.Move(scratch, stage, target, overwrite: true);
        AcquisitionWorkingTree.RemoveFile(scratch, stage);
        Assert.False(File.Exists(Path.Combine(scratch, stage)));
        Assert.Equal("second\n", File.ReadAllText(Path.Combine(scratch, target)));
    }

    private string Repository()
    {
        string repository = Path.Combine(scratch, "repository");
        Directory.CreateDirectory(repository);
        RunEstate.Git(repository, "init", "-q", "--initial-branch=main");
        RunEstate.Git(repository, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(repository, "tracked.txt"), "initial\n");
        RunEstate.Git(repository, "add", "tracked.txt");
        RunEstate.Git(repository, "commit", "-qm", "Initial fixture");
        return repository;
    }

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [DllImport("libc", SetLastError = true)] private static extern int mkfifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int link([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string name, string existing, nint security);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
