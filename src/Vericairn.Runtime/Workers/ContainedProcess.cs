// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Vericairn.Runtime.Workers;

/// <summary>A harness launched inside a boundary before it can execute: a POSIX process group or a Windows Job Object.</summary>
internal sealed class ContainedProcess
{
    private readonly object lifecycle = new();
    private readonly SafeFileHandle? job;
    private readonly SafeProcessHandle? windowsProcess;
    private volatile bool closed;
    private int detached;

    internal Func<int[], (int Count, int Error)>? ProcessInventory { get; set; }
    internal Func<IEnumerable<string>>? ProcfsDirectories { get; set; }
    internal Func<int, int, int>? UnixSignal { get; set; }
    internal Func<uint>? WindowsJobInventory { get; set; }
    private bool exited;
    private int exitCode;

    private ContainedProcess(int id, StreamReader output, StreamReader error, SafeFileHandle? job = null, SafeProcessHandle? windowsProcess = null)
    {
        Id = id;
        StandardOutput = output;
        StandardError = error;
        this.job = job;
        this.windowsProcess = windowsProcess;
    }

    internal bool IsClosed => closed && (job?.IsClosed ?? true) && (windowsProcess?.IsClosed ?? true);

    public int Id { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public int ExitCode => exited ? exitCode : throw new InvalidOperationException("Exit has not been observed.");

    public static ContainedProcess Start(ProcessStartInfo info) => OperatingSystem.IsWindows() ? StartWindows(info) : StartUnix(info);

    public bool WaitForExit(TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            if (RootExited())
            {
                return true;
            }

            Thread.Sleep(10);
        }
        while (watch.Elapsed < timeout);
        return RootExited();
    }

    public bool BoundaryExited()
    {
        if (OperatingSystem.IsWindows() || WindowsJobInventory is not null)
        {
            uint active;
            if (WindowsJobInventory is not null)
            {
                active = WindowsJobInventory();
            }
            else if (QueryInformationJobObject(job!, 1, out JobAccounting queried, Marshal.SizeOf<JobAccounting>(), out _))
            {
                active = queried.ActiveProcesses;
            }
            else
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return active == 0;
        }

        return UnixMembersExited();
    }

    // A reparented descendant can remain a zombie until its new parent reaps it. Its kernel exit state is an observed exit,
    // whereas kill(group, 0) alone still reports that group present. Require two stable exited-member inventories so a child
    // created while its parent exits cannot disappear between enumeration and exit inspection. Unreadable is never exited.
    private bool UnixMembersExited() => ExitedMembers(out HashSet<int> first) && ExitedMembers(out HashSet<int> second) && first.SetEquals(second);

    private bool ExitedMembers(out HashSet<int> members)
    {
        members = [];
        if (OperatingSystem.IsMacOS())
        {
            int[] ids = new int[256];
            int count;
            while (true)
            {
                int error;
                if (ProcessInventory is not null)
                {
                    (count, error) = ProcessInventory(ids);
                }
                else
                {
                    Marshal.SetLastPInvokeError(0);
                    count = proc_listpids(2, (uint)Id, ids, ids.Length * sizeof(int));
                    error = Marshal.GetLastPInvokeError();
                }

                // libproc returns zero on syscall failure, not minus one. Never turn a denied inventory into an empty group.
                if (count < 0 || (count == 0 && error != 0))
                {
                    return false;
                }

                if (count < (ids.Length - 1) * sizeof(int))
                {
                    break;
                }

                ids = new int[checked(ids.Length * 2)];
            }

            byte[] info = new byte[256];
            foreach (int member in ids.Take(count / sizeof(int)).Where(p => p != 0))
            {
                members.Add(member);
                if (member == Id && exited)
                {
                    continue;
                }
                int read = proc_pidinfo(member, 3, 0, info, info.Length); // PROC_PIDTBSDINFO; pbi_status follows pbi_flags.
                if (read < 8)
                {
                    if (kill(member, 0) != 0 && Marshal.GetLastPInvokeError() == 3)
                    {
                        continue;
                    }

                    return false;
                }

                if (BitConverter.ToUInt32(info, 4) != 5) // SZOMB
                {
                    return false;
                }
            }

            return true;
        }

        if (!ProcfsInventoryVisible(File.ReadAllText("/proc/mounts")))
        {
            return false;
        }

        foreach (string directory in ProcfsDirectories?.Invoke() ?? Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _))
            {
                continue;
            }

            try
            {
                string stat = File.ReadAllText(Path.Combine(directory, "stat"));
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (fields.Length < 3 || !int.TryParse(fields[2], out int group))
                {
                    return false;
                }

                if (group == Id)
                {
                    members.Add(int.Parse(Path.GetFileName(directory), System.Globalization.CultureInfo.InvariantCulture));
                    if (fields[0] is not ("Z" or "X"))
                    {
                        return false;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // It exited while the directory was being enumerated.
            }
            catch (DirectoryNotFoundException)
            {
                // It exited while the directory was being enumerated.
            }
        }

        // The unreaped leader must still be visible. A filtered or unavailable procfs view is never an empty boundary.
        return members.Contains(Id);
    }

    internal static bool ProcfsInventoryVisible(string mounts)
    {
        string[]? mounted = mounts.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .LastOrDefault(fields => fields.Length >= 4 && fields[1] == "/proc");
        return mounted is not null && mounted[2] == "proc"
            && !mounted[3].Split(',').Any(option => option.StartsWith("hidepid=", StringComparison.Ordinal) && option is not ("hidepid=0" or "hidepid=off"));
    }

    public void Signal(bool force)
    {
        lock (lifecycle)
        {
            SignalOwned(force);
        }
    }

    private void SignalOwned(bool force)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (OperatingSystem.IsWindows())
        {
            if (force && !TerminateJobObject(job!, 137))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        else
        {
            // Even a cached exit observation is not permission to signal after another component stole child ownership.
            _ = UnixStatus();
            int result = UnixSignal?.Invoke(-Id, force ? 9 : 15) ?? kill(-Id, force ? 9 : 15);
            if (result != 0 && Marshal.GetLastPInvokeError() != 3)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    // When record observation is abandoned, native ownership continues without settling or releasing any reservation.
    public void Detach()
    {
        if (Interlocked.Exchange(ref detached, 1) != 0 || closed)
        {
            return;
        }

        var owner = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (WaitForExit(TimeSpan.FromSeconds(1)) && BoundaryExited())
                    {
                        Close();
                        return;
                    }
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
                {
                    // Inventory failures retain ownership and are retried.
                }

                if (closed)
                {
                    return;
                }

                Thread.Sleep(100);
            }
        }) { IsBackground = true, Name = "vericairn-native-owner" };
        owner.Start();
    }

    public void Close()
    {
        lock (lifecycle)
        {
            CloseOwned();
        }
    }

    private void CloseOwned()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        if (!OperatingSystem.IsWindows())
        {
            // WNOWAIT pins the leader's PID until no further group signal can be sent. Reap exactly once, here.
            int result;
            do
            {
                result = waitpid(Id, out _, 0);
            }
            while (result < 0 && Marshal.GetLastPInvokeError() == 4);
        }

        try
        {
            StandardOutput.Dispose();
        }
        finally
        {
            try
            {
                StandardError.Dispose();
            }
            finally
            {
                windowsProcess?.Dispose();
                job?.Dispose();
            }
        }
    }

    private bool RootExited()
    {
        if (exited)
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            uint wait = WaitForProcess(windowsProcess!, 0);
            if (wait == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (wait != 0)
            {
                return false;
            }

            if (!GetExitCodeProcess(windowsProcess!, out uint code))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            exitCode = unchecked((int)code);
        }
        else
        {
            // siginfo_t is 128 bytes on Linux and smaller on Darwin. Both start with signo, errno, code;
            // Linux aligns its union to eight bytes; Darwin puts pid immediately after code.
            byte[] info;
            try
            {
                info = UnixStatus();
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 4)
            {
                return false;
            }

            if (BitConverter.ToInt32(info, OperatingSystem.IsMacOS() ? 12 : 16) == 0)
            {
                return false;
            }

            int code = BitConverter.ToInt32(info, 8);
            int status = BitConverter.ToInt32(info, OperatingSystem.IsMacOS() ? 20 : 24);
            exitCode = code == 1 ? status : 128 + status; // CLD_EXITED or signal death

        }

        exited = true;
        return true;
    }

    private byte[] UnixStatus()
    {
        byte[] info = new byte[128];
        if (waitid(1, (uint)Id, info, 4 | 1 | (OperatingSystem.IsMacOS() ? 0x20 : 0x1000000)) < 0) // P_PID, WEXITED | WNOHANG | WNOWAIT
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return info;
    }

    internal static void VerifyUnixHost(int processId, bool autoReap = false)
    {
        if (processId == 1 || autoReap)
        {
            // .NET's SIGCHLD handler reaps all children as PID 1, including children started through native APIs.
            throw new Providers.ProviderRefusedException("A POSIX worker needs exclusive child ownership: run it under an init process, without ignored SIGCHLD or automatic child reaping.");
        }
    }

    // posix_spawn installs the group and redirections atomically, before the executable runs. addchdir_np is supplied by both
    // macOS and Linux's libc; there is no shell, fork of the managed runtime, or dependency on a setsid utility.
    private static ContainedProcess StartUnix(ProcessStartInfo info)
    {
        VerifyUnixHost(Environment.ProcessId);
        byte[] signalAction = new byte[256];
        if (sigaction(OperatingSystem.IsMacOS() ? 20 : 17, 0, signalAction) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        int signalFlags = BitConverter.ToInt32(signalAction, OperatingSystem.IsMacOS() ? 12 : 136);
        VerifyUnixHost(Environment.ProcessId, BitConverter.ToInt64(signalAction, 0) == 1
            || (signalFlags & (OperatingSystem.IsMacOS() ? 0x20 : 2)) != 0); // SIG_IGN or SA_NOCLDWAIT
        nint attributes = Marshal.AllocHGlobal(1024);
        nint actions = Marshal.AllocHGlobal(1024);
        var descriptors = new List<int>();
        var streams = new List<StreamReader>();
        var strings = new List<nint>();
        bool attrReady = false;
        bool actionsReady = false;
        bool created = false;
        int pid = 0;
        try
        {
            Check(posix_spawnattr_init(attributes));
            attrReady = true;
            Check(posix_spawn_file_actions_init(actions));
            actionsReady = true;
            Check(posix_spawnattr_setpgroup(attributes, 0));
            Check(posix_spawnattr_setflags(attributes, 2)); // POSIX_SPAWN_SETPGROUP on Linux and macOS.
            Check(posix_spawn_file_actions_addchdir_np(actions, info.WorkingDirectory));
            int[] output = Pipe();
            int[] error = Pipe();
            Check(posix_spawn_file_actions_addopen(actions, 0, "/dev/null", 0, 0));
            Redirect(output, 1);
            Redirect(error, 2);
            nint argv = Vector(new[] { info.FileName }.Concat(info.ArgumentList));
            nint env = Vector(info.Environment.Select(e => e.Key + "=" + e.Value));
            Check(posix_spawnp(out pid, info.FileName, actions, attributes, argv, env));
            created = true;
            _ = close(output[1]);
            descriptors.Remove(output[1]);
            _ = close(error[1]);
            descriptors.Remove(error[1]);
            var result = new ContainedProcess(pid, TakeReader(output[0]), TakeReader(error[0]));
            streams.Clear();
            return result;

            StreamReader TakeReader(int descriptor)
            {
                // Transfer each descriptor once, including failure while constructing the second stream.
                descriptors.Remove(descriptor);
                var handle = new SafeFileHandle(descriptor, ownsHandle: true);
                try
                {
                    StreamReader reader = Reader(handle);
                    streams.Add(reader);
                    return reader;
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            int[] Pipe()
            {
                int[] pair = new int[2];
                if (pipe(pair) != 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                descriptors.AddRange(pair);
                foreach (int descriptor in pair)
                {
                    if (fcntl(descriptor, 2, 1) != 0) // F_SETFD, FD_CLOEXEC; dup2's targets remain inherited.
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }
                }

                return pair;
            }

            void Redirect(int[] pair, int target)
            {
                Check(posix_spawn_file_actions_adddup2(actions, pair[1], target));
                Check(posix_spawn_file_actions_addclose(actions, pair[0]));
                Check(posix_spawn_file_actions_addclose(actions, pair[1]));
            }

            nint Vector(IEnumerable<string> values)
            {
                nint[] items = values.Select(value =>
                {
                    nint pointer = Marshal.StringToCoTaskMemUTF8(value);
                    strings.Add(pointer);
                    return pointer;
                }).Append(nint.Zero).ToArray();
                nint vector = Marshal.AllocCoTaskMem(items.Length * nint.Size);
                strings.Add(vector);
                Marshal.Copy(items, 0, vector, items.Length);
                return vector;
            }
        }
        catch (Exception exception) when (created)
        {
            new ContainedProcess(pid, StreamReader.Null, StreamReader.Null).Detach();
            throw new RunLaunchException(DateTimeOffset.UtcNow, exception);
        }
        finally
        {
            foreach (StreamReader stream in streams)
            {
                stream.Dispose();
            }

            foreach (int descriptor in descriptors)
            {
                _ = close(descriptor);
            }

            foreach (nint pointer in strings)
            {
                Marshal.FreeCoTaskMem(pointer);
            }

            if (actionsReady)
            {
                _ = posix_spawn_file_actions_destroy(actions);
            }

            if (attrReady)
            {
                _ = posix_spawnattr_destroy(attributes);
            }

            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
        }
    }

    private static StreamReader Reader(SafeFileHandle handle) => new(new FileStream(handle, FileAccess.Read), new UTF8Encoding(false));

    private static void Check(int error)
    {
        if (error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    private static ContainedProcess StartWindows(ProcessStartInfo info)
    {
        SafeFileHandle job = CreateJobObjectW(0, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var handles = new List<SafeFileHandle>();
        SafeProcessHandle? processHandle = null;
        SafeWaitHandle? thread = null;
        bool resumed = false;
        bool transferred = false;
        try
        {
            // No breakaway; closing the worker's last handle terminates the job. Assignment precedes resumption.
            var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
            (SafeFileHandle Read, SafeFileHandle Write) Output()
            {
                if (!CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref security, 0))
                {
                    read.Dispose();
                    write.Dispose();
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                handles.AddRange([read, write]);
                if (!SetHandleInformation(read, 1, 0))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                return (read, write);
            }

            (SafeFileHandle stdout, SafeFileHandle output) = Output();
            (SafeFileHandle stderr, SafeFileHandle error) = Output();
            (SafeFileHandle stdin, SafeFileHandle input) = Output();
            if (!SetHandleInformation(stdin, 1, 1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            input.Dispose();
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100, Input = stdin.DangerousGetHandle(), Output = output.DangerousGetHandle(), Error = error.DangerousGetHandle() };
            string command = string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList).Select(Quote));
            string environment = string.Join('\0', info.Environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Key + "=" + e.Value)) + "\0\0";
            if (!CreateProcessW(null, (command + "\0").ToCharArray(), 0, 0, true, 0x4 | 0x400 | 0x08000000, environment, info.WorkingDirectory, ref startup, out ProcessInformation process))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            processHandle = new SafeProcessHandle(process.Process, ownsHandle: true);
            thread = new SafeWaitHandle(process.Thread, ownsHandle: true);
            if (!AssignProcessToJobObject(job, processHandle) || ResumeThread(thread) == uint.MaxValue)
            {
                int failure = Marshal.GetLastPInvokeError();
                if (!TerminateProcess(processHandle, 137) || WaitForProcess(processHandle, 5000) != 0)
                {
                    throw new RunLaunchException(DateTimeOffset.UtcNow, new Win32Exception(failure));
                }

                throw new Win32Exception(failure);
            }

            resumed = true;
            foreach (SafeFileHandle handle in new[] { stdin, output, error })
            {
                handle.Dispose();
            }

            var result = new ContainedProcess((int)process.Id, Reader(stdout), Reader(stderr), job, processHandle);
            handles.Clear();
            transferred = true;
            return result;
        }
        catch (Exception exception) when (resumed)
        {
            throw new RunLaunchException(DateTimeOffset.UtcNow, exception);
        }
        finally
        {
            thread?.Dispose();
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }

            if (!transferred)
            {
                job.Dispose();
                processHandle?.Dispose();
            }
        }
    }

    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }

        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project.
#pragma warning disable CA2101 // Unix strings are explicitly UTF-8; Windows entry points explicitly use UTF-16.
    [DllImport("libproc.dylib", SetLastError = true)] private static extern int proc_listpids(uint kind, uint group, [Out] int[] buffer, int size);
    [DllImport("libproc.dylib", SetLastError = true)] private static extern int proc_pidinfo(int pid, int flavor, ulong argument, [Out] byte[] buffer, int size);
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    [DllImport("libc", SetLastError = true)] private static extern int sigaction(int signal, nint action, [Out] byte[] previous);
    [DllImport("libc", SetLastError = true)] private static extern int waitid(int kind, uint pid, [Out] byte[] info, int options);
    [DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
    [DllImport("libc", SetLastError = true)] private static extern int pipe([Out] int[] pair);
    [DllImport("libc", SetLastError = true)] private static extern int fcntl(int descriptor, int command, int value);
    [DllImport("libc")] private static extern int close(int descriptor);
    [DllImport("libc")] private static extern int posix_spawnattr_init(nint attributes);
    [DllImport("libc")] private static extern int posix_spawnattr_destroy(nint attributes);
    [DllImport("libc")] private static extern int posix_spawnattr_setpgroup(nint attributes, int group);
    [DllImport("libc")] private static extern int posix_spawnattr_setflags(nint attributes, short flags);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_init(nint actions);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_destroy(nint actions);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_adddup2(nint actions, int source, int target);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_addclose(nint actions, int descriptor);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_addopen(nint actions, int descriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc")] private static extern int posix_spawn_file_actions_addchdir_np(nint actions, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libc")] private static extern int posix_spawnp(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string executable, nint actions, nint attributes, nint arguments, nint environment);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobLimits information, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out JobAccounting information, int size, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string? application, [In, Out] char[] command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, string environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)] private static extern uint WaitForProcess(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public nint Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int Size; public nint Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags; public ushort Show, ReservedSize; public nint ReservedData, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public uint Id, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public nuint Minimum, Maximum; public uint ActiveLimit; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [StructLayout(LayoutKind.Sequential)] private struct JobAccounting { public long UserTime, KernelTime, PeriodUser, PeriodKernel; public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
}

/// <summary>A failure after process creation: dispatch is uncertain and the allowance remains reserved.</summary>
internal sealed class RunLaunchException(DateTimeOffset launchedAt, Exception inner) : Exception("Observation failed after process creation.", inner)
{
    public DateTimeOffset LaunchedAt { get; } = launchedAt;
}
