// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Reads committed paths in an isolated observer, without loading the workspace's Git configuration or replacement refs.</summary>
internal static class WorkspaceGitScope
{
    internal static string DirectoryOf(string workspace)
    {
        string directory = Path.Combine(workspace, ".git");
        FileAttributes attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Linked Git administration is not a scope baseline.");
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            string pointer = Read(directory, 4096).Trim();
            if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal))
            {
                throw new IOException("The Git directory is unavailable.");
            }

            directory = Path.GetFullPath(pointer[8..], workspace);
        }

        string common = Path.Combine(directory, "commondir");
        return File.Exists(common) ? Path.GetFullPath(Read(common, 4096).Trim(), directory) : directory;
    }

    internal static void Changes(string? directory, string branch, string? baseline, TimeSpan deadline, HashSet<string> changed)
    {
        if (string.IsNullOrEmpty(directory) || !WorkspaceScopeAudit.Relative(branch) || !ObjectId(baseline) || deadline <= TimeSpan.Zero)
        {
            throw new IOException("The committed scope baseline is unavailable.");
        }

        var watch = Stopwatch.StartNew();
        string head = Head(directory, "refs/heads/" + branch);
        string isolated = Path.Combine(Path.GetTempPath(), "vericairn-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A private bare repository sees only the pinned object storage and explicit object ids. In particular it never
            // loads config, config.worktree, grafts, replace refs, attributes or hooks from the agent's repository.
            Directory.CreateDirectory(Path.Combine(isolated, "objects"));
            Directory.CreateDirectory(Path.Combine(isolated, "refs"));
            File.WriteAllText(Path.Combine(isolated, "HEAD"), "ref: refs/heads/unused\n");
            File.WriteAllText(Path.Combine(isolated, "config"), baseline!.Length == 64
                ? "[core]\nrepositoryformatversion = 1\nbare = true\n[extensions]\nobjectformat = sha256\n"
                : "[core]\nrepositoryformatversion = 0\nbare = true\n");
            File.WriteAllText(Path.Combine(isolated, "empty-config"), string.Empty);
            string[] commits = Git(["rev-list", "--max-count=10001", head, "^" + baseline, "--"]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (commits.Length > 10000 || commits.Any(c => !ObjectId(c)))
            {
                throw new IOException("The committed scope inventory exceeded its limit.");
            }

            // Compare the head even when it is an ancestor of the baseline, and every reachable new commit, including side
            // branches of merges. An intermediate outside change remains visible when a later commit restores the baseline.
            string comparisons = string.Concat(commits.Prepend(head).Distinct(StringComparer.Ordinal).Select(c => baseline + " " + c + "\n"));
            string paths = Git(["diff-tree", "--stdin", "--no-commit-id", "--name-only", "-r", "-z", "--no-renames", "--no-ext-diff", "--no-textconv"], comparisons);
            foreach (string path in paths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!WorkspaceScopeAudit.Relative(path) || path.Contains('\ufffd', StringComparison.Ordinal) || changed.Count >= 100_000)
                {
                    throw new IOException("The committed scope inventory contains an unreadable name or exceeded its limit.");
                }

                changed.Add(path);
            }
        }
        finally
        {
            if (Directory.Exists(isolated))
            {
                Directory.Delete(isolated, recursive: true);
            }
        }

        string Git(string[] arguments, string input = "")
        {
            var info = new ProcessStartInfo("git")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = isolated,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
            };
            info.ArgumentList.Add("--git-dir=" + isolated);
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment.Clear();
            foreach ((string name, string value) in WorkerEnvironment.Build(WorkerEnvironment.Current(), []))
            {
                if (!name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                {
                    info.Environment[name] = value;
                }
            }

            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(isolated, "empty-config");
            info.Environment["GIT_OBJECT_DIRECTORY"] = Path.Combine(directory, "objects");
            info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            info.Environment["GIT_NO_LAZY_FETCH"] = "1";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            try
            {
                using Process process = Process.Start(info) ?? throw new IOException("Git could not inspect the committed scope.");
                Task<string> output = ReadAsync(process.StandardOutput);
                Task<string> error = ReadAsync(process.StandardError);
                Task writing = WriteAsync(process.StandardInput, input);
                TimeSpan left = deadline - watch.Elapsed;
                if (left <= TimeSpan.Zero || !process.WaitForExit(left))
                {
                    process.Kill(entireProcessTree: true);
                    _ = process.WaitForExit(1000);
                    throw new IOException("The committed scope inventory reached its deadline.");
                }

                Task.WhenAll(output, error, writing).WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : throw new IOException("Git could not inspect the committed scope.");
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or TimeoutException or DecoderFallbackException)
            {
                throw new IOException("The committed scope inventory is unavailable.", exception);
            }
        }
    }

    private static string Head(string directory, string reference)
    {
        string path = Path.Combine(directory, reference.Replace('/', Path.DirectorySeparatorChar));
        string? head = File.Exists(path) ? Read(path, 4096).Trim() : null;
        if (head is null)
        {
            string packed = Read(Path.Combine(directory, "packed-refs"), 16 * 1024 * 1024);
            head = packed.Split('\n').FirstOrDefault(line => line.EndsWith(" " + reference, StringComparison.Ordinal))?.Split(' ')[0];
        }

        return ObjectId(head) ? head! : throw new IOException("The run branch's head is unavailable.");
    }

    private static bool ObjectId(string? value) => value is { Length: 40 or 64 } && value.All(char.IsAsciiHexDigit);

    private static string Read(string path, int limit)
    {
        using SafeFileHandle handle = WorkspaceScopeAudit.OpenRegular(path);
        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var buffer = new char[limit + 1];
        int count = reader.ReadBlock(buffer, 0, buffer.Length);
        return count <= limit ? new string(buffer, 0, count) : throw new IOException("Git metadata exceeded the scope limit.");
    }

    private static async Task<string> ReadAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > 32 * 1024 * 1024)
            {
                throw new IOException("Git output exceeded the scope limit.");
            }

            output.Append(buffer, 0, read);
        }

        return output.ToString();
    }

    private static async Task WriteAsync(StreamWriter writer, string input)
    {
        await writer.WriteAsync(input).ConfigureAwait(false);
        writer.Close();
    }
}
