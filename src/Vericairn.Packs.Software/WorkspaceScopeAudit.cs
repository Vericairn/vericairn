// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>A scope observation, not a sandbox or an attribution of who made a concurrent edit.</summary>
internal static class WorkspaceScopeAudit
{
    internal static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(30);

    public static WorkspaceSnapshot Capture(string workspace) => WorkspaceScopeProbe.Capture(workspace, Deadline);

    internal static WorkspaceSnapshot CaptureLocal(string workspace, TimeSpan deadline)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string? problem = null;
        string? identity = null;
        var watch = Stopwatch.StartNew();
        long bytes = 0;
        try
        {
            if (!Path.Exists(Path.Combine(workspace, ".git")))
            {
                return new WorkspaceSnapshot(files, "The workspace repository identity is unavailable; assignment scope is unverified.");
            }

            identity = DirectoryIdentity(workspace);
            // Inventory the physical tree, including ignored and untracked files. Agent-modifiable Git configuration and
            // GIT_DIR/GIT_WORK_TREE never decide which ordinary workspace files the audit can see.
            Visit(workspace, "");
            if (identity != DirectoryIdentity(workspace))
            {
                throw new IOException("The workspace changed identity during its inventory.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException)
        {
            problem = "The worker could not read the whole workspace inventory; assignment scope is incomplete.";
        }

        return new WorkspaceSnapshot(files, problem, identity);

        void CheckDeadline()
        {
            if (watch.Elapsed >= deadline || files.Count >= 100_000 || bytes >= 1024L * 1024 * 1024)
            {
                throw new IOException("The scope audit reached its work or time limit.");
            }
        }

        void Visit(string directory, string prefix)
        {
            CheckDeadline();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckDeadline();
                string leaf = Path.GetFileName(path);
                if (leaf == ".git")
                {
                    if (prefix.Length > 0)
                    {
                        problem = "A submodule or nested repository needs a separate scope audit; assignment scope is incomplete.";
                    }

                    continue;
                }

                string name = prefix + leaf;
                if (!Relative(name) || name.Contains('\ufffd', StringComparison.Ordinal))
                {
                    problem = "An inventory name could not be decoded losslessly or safely; assignment scope is incomplete.";
                    continue;
                }

                try
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        files[name] = "link:" + Hash(Encoding.UTF8.GetBytes(new FileInfo(path).LinkTarget ?? "unreadable-link"));
                    }
                    else if ((attributes & FileAttributes.Directory) != 0)
                    {
                        Visit(path, name + "/");
                    }
                    else
                    {
                        using SafeFileHandle handle = OpenRegular(path);
                        using var stream = new FileStream(handle, FileAccess.Read);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        while ((read = stream.Read(buffer)) != 0)
                        {
                            bytes += read;
                            CheckDeadline();
                            hash.AppendData(buffer, 0, read);
                        }

                        string mode = OperatingSystem.IsWindows() ? string.Empty : ((int)File.GetUnixFileMode(path)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        files[name] = mode + ":" + Convert.ToHexString(hash.GetHashAndReset());
                    }
                }
                catch (FileNotFoundException)
                {
                    // Deletions are represented by absence.
                }
                catch (DirectoryNotFoundException)
                {
                    // Deletions are represented by absence.
                }
            }
        }
    }

    private static string DirectoryIdentity(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = CreateFileW(path, 0, 7, 0, 3, 0x02200000, 0); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
            byte[] identity = new byte[24]; // FILE_ID_INFO: volume serial number and 128-bit file id.
            if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out FileTag tag, 8)
                || (tag.Attributes & (0x400u | 0x10u)) != 0x10u
                || !GetFileIdentity(handle, 18, identity, identity.Length))
            {
                throw new IOException("The physical workspace identity is unavailable.");
            }

            return Hash(identity);
        }

        byte[] stat = new byte[256];
        if (lstat(path, stat) != 0 || (BitConverter.ToUInt16(stat, ModeOffset()) & 0xf000) != 0x4000)
        {
            throw new IOException("The physical workspace is not an ordinary directory.");
        }

        // Darwin's device is 32 bits; Linux's is 64. The inode starts at offset eight on supported 64-bit platforms.
        return Hash(stat.Take(OperatingSystem.IsMacOS() ? 4 : 8).Concat(stat.Skip(8).Take(8)).ToArray());
    }

    private static int ModeOffset()
    {
        if (OperatingSystem.IsMacOS())
        {
            return 4;
        }

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => 16,
            Architecture.X64 => 24,
            _ => throw new IOException("The scope observer cannot classify files on this architecture."),
        };
    }

    private static SafeFileHandle OpenRegular(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            SafeFileHandle handle = CreateFileW(path, 0x80000000, 7, 0, 3, 0x00200000, 0); // OPEN_REPARSE_POINT
            if (handle.IsInvalid || GetFileType(handle) != 1 || !GetFileInformationByHandleEx(handle, 9, out FileTag tag, 8)
                || (tag.Attributes & (0x400u | 0x10u)) != 0)
            {
                handle.Dispose();
                throw new IOException("The scope entry is not a regular file.");
            }

            return handle;
        }

        byte[] stat = new byte[256];
        int modeOffset = ModeOffset();
        if (lstat(path, stat) != 0 || (BitConverter.ToUInt16(stat, modeOffset) & 0xf000) != 0x8000)
        {
            throw new IOException("The scope entry is not a regular file.");
        }

        // Classify before acquisition, then nonblocking/no-follow open and reclassify the acquired descriptor to close leaf swaps.
        int descriptor = open(path, OperatingSystem.IsMacOS() ? 0x4 | 0x100 : 0x800 | 0x20000);
        var result = new SafeFileHandle(descriptor, ownsHandle: true);
        if (result.IsInvalid || fstat(result, stat) != 0 || (BitConverter.ToUInt16(stat, modeOffset) & 0xf000) != 0x8000)
        {
            result.Dispose();
            throw new IOException("The scope entry is not a regular file.");
        }

        return result;
    }

#pragma warning disable SYSLIB1054
#pragma warning disable CA2101
    [DllImport("libc", SetLastError = true)] private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int lstat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] stat);
    [DllImport("libc", SetLastError = true)] private static extern int fstat(SafeFileHandle file, [Out] byte[] stat);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out FileTag info, int size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileIdentity(SafeFileHandle handle, int kind, [Out] byte[] info, int size);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
    [StructLayout(LayoutKind.Sequential)] private struct FileTag { public uint Attributes, Tag; }

    public static RunScope Compare(WorkspaceSnapshot before, WorkspaceSnapshot after, IReadOnlyList<string> permitted, TimeSpan? deadline = null)
    {
        var watch = Stopwatch.StartNew();
        string[] changes = before.Files.Keys.Union(after.Files.Keys, StringComparer.Ordinal)
            .Where(path => before.Files.GetValueOrDefault(path) != after.Files.GetValueOrDefault(path)).Order(StringComparer.Ordinal).ToArray();
        string[] patterns = permitted.Select(p => p.StartsWith("path:", StringComparison.Ordinal) ? p[5..].Trim() : p.Trim())
            .Where(Relative).ToArray();
        var outside = new List<string>();
        string? problem = before.Problem ?? after.Problem;
        if (before.Identity != after.Identity)
        {
            problem ??= "The physical workspace identity changed after launch; assignment scope is unverified.";
        }
        if (patterns.Length == 0)
        {
            problem ??= "The assignment declares no interpretable repository-relative permitted paths; no changed path is authorized by this audit.";
        }

        try
        {
            foreach (string path in changes)
            {
                CheckDeadline();
                bool allowed = false;
                foreach (string pattern in patterns)
                {
                    CheckDeadline();
                    if (Matches(path, pattern))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    outside.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is TimeoutException or NotSupportedException)
        {
            problem ??= "The scope comparison reached its time or pattern-complexity limit; assignment scope is incomplete.";
        }

        return new RunScope(changes.Length, outside.Count, outside.Take(1000).Select(Display).ToArray(), problem);

        void CheckDeadline()
        {
            if (watch.Elapsed >= (deadline ?? Deadline))
            {
                throw new TimeoutException("The scope comparison reached its deadline.");
            }
        }
    }

    private static bool Matches(string path, string pattern)
    {
        string plain = pattern.TrimEnd('/');
        if (!plain.Contains('*', StringComparison.Ordinal) && !plain.Contains('?', StringComparison.Ordinal))
        {
            return path == plain || path.StartsWith(plain + "/", StringComparison.Ordinal);
        }

        string expression = Regex.Escape(plain).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(path, "\\A" + expression + "\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }

    private static bool Relative(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2000
        && !Path.IsPathRooted(path) && !path.Contains(':', StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl) && path.Split('/').All(part => part is not ("." or ".."));

    private static string Display(string path) => path.Length <= 500 && Regex.IsMatch(path, @"\A[A-Za-z0-9_./ @()+,=\-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && !EstateChecks.Content("scope.txt", Encoding.UTF8.GetBytes(path)).HasErrors
        ? path : "path with unportable name, sha256:" + Hash(Encoding.UTF8.GetBytes(path));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
