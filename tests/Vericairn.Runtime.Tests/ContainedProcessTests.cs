// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class ContainedProcessTests
{
    [Fact]
    public void FailedMacInventoryNeverCertifiesAnEmptyBoundaryAndCanBeRetried()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("proc_listpids and its zero/errno contract are macOS-specific.");
        }

        using var run = new RunEstate();
        ContainedProcess process = Start(run, "generic");
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)));
        try
        {
            process.ProcessInventory = _ => (0, 1); // EPERM: libproc returns zero on failure.
            Assert.False(process.BoundaryExited());
            Assert.False(process.BoundaryExited());
            process.ProcessInventory = null;
            Assert.True(process.BoundaryExited());
        }
        finally
        {
            process.Close();
        }
    }

    [Fact]
    public void AnExitedLeaderRemainsOwnedUntilGroupSignalingIsFinishedThenIsReaped()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("POSIX PID/group identity regression; Windows uses an owned Job Object.");
        }

        using var run = new RunEstate();
        ContainedProcess process = Start(run, "generic");
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)));
        try
        {
            Assert.Equal(0, kill(process.Id, 0)); // Still a waitable child: its numeric identity cannot be recycled.
            Assert.True(process.BoundaryExited());
        }
        finally
        {
            process.Close();
        }

        Assert.Equal(-1, waitpid(process.Id, out _, 1));
        Assert.Equal(10, Marshal.GetLastPInvokeError()); // ECHILD on macOS and Linux.
        Assert.Throws<ObjectDisposedException>(() => process.Signal(force: true));
    }

    [Fact]
    public void AbandoningSupervisionRetainsANativeOwnerThroughEventualExit()
    {
        using var run = new RunEstate();
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, Vericairn.Records.Identity.Ulid.NewUlid().Value);
        directory.Create();
        RunSupervisor supervisor = RunSupervisor.Start("detached", FakeHarness.Command("close-output", run.Report("detached")), run.Scratch,
            WorkerEnvironment.Current(), new Vericairn.Packs.Software.GenericLauncher(), directory,
            new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(20) }, 60, null);
        Assert.Null(supervisor.Supervise(new CancellationToken(canceled: true)));
        if (!OperatingSystem.IsWindows())
        {
            bool reaped = SpinWait.SpinUntil(() => kill(supervisor.ProcessId, 0) != 0, TimeSpan.FromSeconds(20));
            if (!reaped)
            {
                _ = waitpid(supervisor.ProcessId, out _, 1); // Clean up even when testing the old ownership bug.
            }

            Assert.True(reaped, "The detached owner must reap the child.");
            Assert.Equal(-1, waitpid(supervisor.ProcessId, out _, 1));
            Assert.Equal(10, Marshal.GetLastPInvokeError());
        }
        else
        {
            // Windows keeps the job alive until its processes exit; its process/job fields own SafeHandles.
            Assert.True(SpinWait.SpinUntil(() => !Alive(supervisor.ProcessId), TimeSpan.FromSeconds(20)));
        }

        Assert.True(SpinWait.SpinUntil(() => supervisor.NativeResourcesClosed, TimeSpan.FromSeconds(20)));
        Assert.Null(directory.Read()!.ExitedAt);
        Assert.Null(directory.Read()!.Settled);
    }

    private static bool Alive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ContainedProcess Start(RunEstate run, string scenario)
    {
        List<string> command = FakeHarness.Command(scenario, run.Report(scenario));
        var info = new ProcessStartInfo(command[0]) { WorkingDirectory = run.Scratch };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        return ContainedProcess.Start(info);
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    [DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
#pragma warning restore SYSLIB1054
}
