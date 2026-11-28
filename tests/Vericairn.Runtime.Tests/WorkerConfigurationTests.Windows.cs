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
        string worker = Path.Combine(home, "worker");
        string other = Path.Combine(home, "other");
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(other);
        string provision = Path.Combine(worker, WorkerConfigurationStore.ClaudeSetupTokenFile);
        string unrelated = Path.Combine(other, WorkerConfigurationStore.ClaudeSetupTokenFile);
        File.WriteAllText(provision, "worker fixture");
        File.WriteAllText(unrelated, "unrelated fixture");

        var store = new WorkerConfigurationStore(home);
        Assert.True(store.RemoveClaudeSetupToken().Removed);
        Assert.False(File.Exists(provision));
        Assert.Equal("unrelated fixture", File.ReadAllText(unrelated));
        Assert.False(store.RemoveClaudeSetupToken().Removed);
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
