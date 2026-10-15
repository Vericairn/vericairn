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
    private readonly nint job;
    private readonly nint windowsProcess;
    private bool exited;
    private int exitCode;

    private ContainedProcess(int id, StreamReader output, StreamReader error, nint job = 0, nint windowsProcess = 0)
    {
        Id = id;
        StandardOutput = output;
        StandardError = error;
        this.job = job;
        this.windowsProcess = windowsProcess;
    }

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
        if (OperatingSystem.IsWindows())
        {
            if (!QueryInformationJobObject(job, 1, out JobAccounting accounting, Marshal.SizeOf<JobAccounting>(), out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return accounting.ActiveProcesses == 0;
        }

        return UnixMembersExited();
    }

    // A reparented descendant can remain a zombie until its new parent reaps it. Its kernel exit state is an observed exit,
    // whereas kill(group, 0) alone still reports that group present. Never mistake an unreadable process for an exited one.
    private bool UnixMembersExited()
    {
        if (OperatingSystem.IsMacOS())
        {
            int[] members = new int[256];
            int count;
            while ((count = proc_listpids(2, (uint)Id, members, members.Length * sizeof(int))) >= (members.Length - 1) * sizeof(int))
            {
                members = new int[checked(members.Length * 2)];
            }

            if (count < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            byte[] info = new byte[256];
            foreach (int member in members.Take(count / sizeof(int)).Where(p => p != 0))
            {
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

        foreach (string directory in Directory.EnumerateDirectories("/proc"))
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

                if (group == Id && fields[0] is not ("Z" or "X"))
                {
                    return false;
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

        return true;
    }

    public void Signal(bool force)
    {
        if (OperatingSystem.IsWindows())
        {
            if (force && !TerminateJobObject(job, 137))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        else if (kill(-Id, force ? 9 : 15) != 0 && Marshal.GetLastPInvokeError() != 3)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Close()
    {
        StandardOutput.Dispose();
        StandardError.Dispose();
        if (OperatingSystem.IsWindows())
        {
            CloseHandle(windowsProcess);
            CloseHandle(job);
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
            if (WaitForSingleObject(windowsProcess, 0) != 0)
            {
                return false;
            }

            if (!GetExitCodeProcess(windowsProcess, out uint code))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            exitCode = unchecked((int)code);
        }
        else
        {
            int result = waitpid(Id, out int status, 1);
            if (result == 0)
            {
                return false;
            }

            if (result < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            exitCode = (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
        }

        exited = true;
        return true;
    }

    // posix_spawn installs the group and redirections atomically, before the executable runs. addchdir_np is supplied by both
    // macOS and Linux's libc; there is no shell, fork of the managed runtime, or dependency on a setsid utility.
    private static ContainedProcess StartUnix(ProcessStartInfo info)
    {
        nint attributes = Marshal.AllocHGlobal(1024);
        nint actions = Marshal.AllocHGlobal(1024);
        var descriptors = new List<int>();
        var strings = new List<nint>();
        bool attrReady = false;
        bool actionsReady = false;
        bool created = false;
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
            Check(posix_spawnp(out int pid, info.FileName, actions, attributes, argv, env));
            created = true;
            _ = close(output[1]);
            descriptors.Remove(output[1]);
            _ = close(error[1]);
            descriptors.Remove(error[1]);
            var result = new ContainedProcess(pid, Reader(output[0]), Reader(error[0]));
            descriptors.Clear();
            return result;

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
            throw new RunLaunchException(DateTimeOffset.UtcNow, exception);
        }
        finally
        {
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

    private static StreamReader Reader(nint handle) => new(new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Read), new UTF8Encoding(false));

    private static void Check(int error)
    {
        if (error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    private static ContainedProcess StartWindows(ProcessStartInfo info)
    {
        nint job = CreateJobObjectW(0, null);
        if (job == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var handles = new List<nint>();
        ProcessInformation process = default;
        bool started = false;
        bool resumed = false;
        try
        {
            // No breakaway; closing the worker's last handle also terminates the job. Assignment happens while the root is suspended.
            var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
            (nint read, nint write) Output()
            {
                if (!CreatePipe(out nint read, out nint write, ref security, 0))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                handles.AddRange([read, write]);
                if (!SetHandleInformation(read, 1, 0))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                return (read, write);
            }

            (nint stdout, nint output) = Output();
            (nint stderr, nint error) = Output();
            (nint stdin, nint input) = Output();
            // An already closed stdin pipe is inherited as a valid read handle.
            SetHandleInformation(stdin, 1, 1);
            CloseHandle(input);
            handles.Remove(input);
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100, Input = stdin, Output = output, Error = error };
            string command = string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList).Select(Quote));
            string environment = string.Join('\0', info.Environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Key + "=" + e.Value)) + "\0\0";
            if (!CreateProcessW(null, (command + "\0").ToCharArray(), 0, 0, true, 0x4 | 0x400 | 0x08000000, environment, info.WorkingDirectory, ref startup, out process))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            started = true;
            if (!AssignProcessToJobObject(job, process.Process) || ResumeThread(process.Thread) == uint.MaxValue)
            {
                // No harness instruction has executed if assignment fails. Terminate and observe before reporting prelaunch failure.
                int failure = Marshal.GetLastPInvokeError();
                if (!TerminateProcess(process.Process, 137) || WaitForSingleObject(process.Process, 5000) != 0)
                {
                    throw new RunLaunchException(DateTimeOffset.UtcNow, new Win32Exception(failure));
                }

                throw new Win32Exception(failure);
            }

            resumed = true;
            CloseHandle(process.Thread);
            process.Thread = 0;
            foreach (nint handle in new[] { stdin, output, error })
            {
                CloseHandle(handle);
                handles.Remove(handle);
            }

            var result = new ContainedProcess((int)process.Id, Reader(stdout), Reader(stderr), job, process.Process);
            handles.Clear();
            return result;
        }
        catch (Exception exception)
        {
            if (process.Thread != 0)
            {
                CloseHandle(process.Thread);
            }

            if (started)
            {
                CloseHandle(process.Process);
            }

            CloseHandle(job);
            if (resumed)
            {
                throw new RunLaunchException(DateTimeOffset.UtcNow, exception);
            }

            throw;
        }
        finally
        {
            foreach (nint handle in handles)
            {
                CloseHandle(handle);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int kind, ref JobLimits information, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(nint job, int kind, out JobAccounting information, int size, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(nint job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out nint read, out nint write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string? application, [In, Out] char[] command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, string environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
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
