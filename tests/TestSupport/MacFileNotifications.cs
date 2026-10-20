// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vericairn.TestSupport;

/// <summary>
/// Watches only test-owned files through vnode notifications. Unlike FSEvents, kqueue needs no
/// connection to a system service, so the same barriers work in a sandbox and on a CI runner.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacFileNotifications : IDisposable
{
    private const short Vnode = -4;
    private const short User = -10;
    private const ushort AddAndClear = 0x21;
    private const uint Changed = 0x7f;
    private const uint DeletedOrRenamed = 0x21;
    private const uint Trigger = 0x01000000;
    private readonly string[] directories;
    private readonly Dictionary<string, int> files = new(StringComparer.Ordinal);
    private readonly int queue;
    private readonly Task listening;
    private readonly Action notify;
    private readonly Action<Exception> failed;

    internal MacFileNotifications(string[] directories, Action notify, Action<Exception> failed)
    {
        this.directories = directories;
        this.notify = notify;
        this.failed = failed;
        queue = kqueue();
        if (queue < 0) throw new IOException("The test requires macOS kqueue file notifications.", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            Change(new Event { Ident = 1, Filter = User, Flags = AddAndClear });
            RegisterFiles();
            listening = Task.Factory.StartNew(Listen, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch
        {
            Close();
            throw;
        }
    }

    private void RegisterFiles()
    {
        foreach (string directory in directories)
        {
            Register(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            })) Register(path);
        }
    }

    private void Register(string path)
    {
        if (files.ContainsKey(path)) return;
        // O_EVTONLY | O_NONBLOCK | O_CLOEXEC: no content is read, a fixture FIFO cannot
        // block registration, and no descriptor reaches a child process.
        int descriptor = open(System.Text.Encoding.UTF8.GetBytes(path + "\0"), 0x8000 | 4 | 0x1000000);
        if (descriptor < 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return; // An atomic replacement can remove the old path during enumeration.
            throw new IOException("Could not observe a test-owned file.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        try
        {
            Change(new Event { Ident = (nuint)descriptor, Filter = Vnode, Flags = AddAndClear, FilterFlags = Changed });
            files.Add(path, descriptor);
        }
        catch
        {
            _ = close(descriptor);
            throw;
        }
    }

    private void Listen()
    {
        var events = new Event[64];
        try
        {
            while (true)
            {
                int count = kevent(queue, null, 0, events, events.Length, IntPtr.Zero);
                if (count < 0)
                {
                    if (Marshal.GetLastPInvokeError() == 4) continue; // Interrupted by a process signal.
                    throw new IOException("The test's file notification wait failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
                }

                for (int i = 0; i < count; i++)
                {
                    if (events[i].Filter == User) return;
                    if ((events[i].FilterFlags & DeletedOrRenamed) == 0) continue;
                    int descriptor = (int)events[i].Ident;
                    string? path = files.FirstOrDefault(pair => pair.Value == descriptor).Key;
                    if (path is not null)
                    {
                        files.Remove(path);
                        _ = close(descriptor);
                    }
                }

                RegisterFiles();
                notify();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failed(exception);
        }
    }

    private void Change(Event change)
    {
        if (kevent(queue, [change], 1, null, 0, IntPtr.Zero) < 0)
            throw new IOException("Could not register the test's file notification.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    public void Dispose()
    {
        Change(new Event { Ident = 1, Filter = User, FilterFlags = Trigger });
        Assert.True(listening.Wait(TestTiming.HangTimeout), "The test's file notification listener did not stop.");
        Close();
    }

    private void Close()
    {
        foreach (int descriptor in files.Values) _ = close(descriptor);
        _ = close(queue);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Event
    {
        internal nuint Ident;
        internal short Filter;
        internal ushort Flags;
        internal uint FilterFlags;
        internal nint Data;
        internal nint UserData;
    }

#pragma warning disable SYSLIB1054 // Test-only interop; shared test projects do not require unsafe compilation.
    [DllImport("libc", SetLastError = true)]
    private static extern int kqueue();
    [DllImport("libc", SetLastError = true)]
    private static extern int kevent(int queue, [In] Event[]? changes, int changeCount, [Out] Event[]? events, int eventCount, IntPtr timeout);
    [DllImport("libc", SetLastError = true)]
    private static extern int open(byte[] path, int flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int descriptor);
#pragma warning restore SYSLIB1054
}
