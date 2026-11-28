// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.Win32.SafeHandles;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed partial class WorkerConfigurationTests
{
    [Fact]
    public void AWindowsWorkerDirectoryCannotBeRenamedWhileHeld()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows directory sharing");
        string worker = Path.Combine(home, "worker");
        string renamed = Path.Combine(home, "renamed");
        Directory.CreateDirectory(worker);

        using (SafeFileHandle homeHandle = DirectoryHandles.OpenWindows(home))
        using (SafeFileHandle workerHandle = DirectoryHandles.OpenWindows("worker", homeHandle))
        {
            Exception? error = Record.Exception(() => Directory.Move(worker, renamed));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.True(Directory.Exists(worker));
            Assert.False(Directory.Exists(renamed));
        }

        Directory.Move(worker, renamed);
        Assert.True(Directory.Exists(renamed));
    }

    [Fact]
    public void WindowsRemovalDeletesOnlyTheProvisionInTheHeldDirectory()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows directory-relative deletion");
        string original = Path.Combine(home, "original");
        string decoy = Path.Combine(home, "decoy");
        string alias = Path.Combine(home, "alias");
        string originalHome = Path.Combine(original, "local");
        string worker = Path.Combine(originalHome, "worker");
        string decoyWorker = Path.Combine(decoy, "local", "worker");
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(decoyWorker);
        const string name = WorkerConfigurationStore.ClaudeSetupTokenFile;
        string provision = Path.Combine(worker, name);
        string unrelated = Path.Combine(decoyWorker, name);
        File.WriteAllText(provision, "worker fixture");
        File.WriteAllText(unrelated, "unrelated fixture");

        WorkspaceCleanupTests.DirectoryLink(alias, original);
        string aliasHome = Path.Combine(alias, "local");
        string aliasProvision = Path.Combine(aliasHome, "worker", name);
        // Keep the fixture junction in the resolved path so the callback can retarget it while both handles stay open.
        var store = new WorkerConfigurationStore(aliasHome) { ResolveConfigurationAncestor = path => path };
        Assert.True(store.RemoveClaudeSetupToken(() =>
        {
            Assert.Equal("worker fixture", File.ReadAllText(aliasProvision));
            Directory.Delete(alias);
            WorkspaceCleanupTests.DirectoryLink(alias, decoy);
            Assert.Equal("unrelated fixture", File.ReadAllText(aliasProvision));
        }).Removed);
        Assert.False(File.Exists(provision));
        Assert.Equal("unrelated fixture", File.ReadAllText(unrelated));
        Assert.False(new WorkerConfigurationStore(originalHome).RemoveClaudeSetupToken().Removed);
    }

    [Fact]
    public void WindowsUnlinkUsesTheDirectoryPassedByHandle()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows directory-relative deletion");
        string worker = Path.Combine(home, "worker");
        string other = Path.Combine(home, "other");
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(other);
        const string name = WorkerConfigurationStore.ClaudeSetupTokenFile;
        string provision = Path.Combine(worker, name);
        string unrelated = Path.Combine(other, name);
        File.WriteAllText(provision, "worker fixture");
        File.WriteAllText(unrelated, "unrelated fixture");

        using SafeFileHandle homeHandle = DirectoryHandles.OpenWindows(home);
        using SafeFileHandle workerHandle = DirectoryHandles.OpenWindows("worker", homeHandle);
        using SafeFileHandle otherHandle = DirectoryHandles.OpenWindows("other", homeHandle);
        Assert.True(DirectoryHandles.UnlinkWindows(otherHandle, name));
        Assert.Equal("worker fixture", File.ReadAllText(provision));
        Assert.False(File.Exists(unrelated));
        Assert.False(DirectoryHandles.UnlinkWindows(otherHandle, name));
        Assert.True(DirectoryHandles.UnlinkWindows(workerHandle, name));
        Assert.False(File.Exists(provision));
    }
}
