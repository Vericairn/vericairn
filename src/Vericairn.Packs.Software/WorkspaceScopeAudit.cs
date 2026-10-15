// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
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
    public static WorkspaceSnapshot Capture(string workspace)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string? problem = null;
        try
        {
            // No exclusions: ignoring a file must not hide a change outside the assignment. NUL delimiters preserve rename and odd names.
            (int code, string output, _) = HarnessProcess.Run(["git", "-c", "core.fsmonitor=false", "-C", workspace, "ls-files", "--cached", "--others", "--deduplicate", "-z"], WorkerEnvironment.Current(), TimeSpan.FromMinutes(2));
            if (code != 0)
            {
                return new WorkspaceSnapshot(files, "Git could not inventory the workspace; assignment scope is unverified.");
            }

            if (output.Contains('\ufffd', StringComparison.Ordinal))
            {
                problem = "An inventory name could not be decoded losslessly; assignment scope is incomplete.";
            }

            foreach (string name in output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
            {
                if (!Relative(name))
                {
                    problem = "The inventory contained an unsafe path; assignment scope is incomplete.";
                    continue;
                }

                string path = Path.Combine(workspace, name);
                try
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        files[name] = "link:" + Hash(Encoding.UTF8.GetBytes(new FileInfo(path).LinkTarget ?? "unreadable-link"));
                    }
                    else if ((attributes & FileAttributes.Directory) != 0)
                    {
                        problem = "A submodule or nested repository needs a separate scope audit; assignment scope is incomplete.";
                        files[name] = "directory";
                    }
                    else
                    {
                        using FileStream stream = File.OpenRead(path);
                        string mode = OperatingSystem.IsWindows() ? string.Empty : ((int)File.GetUnixFileMode(path)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        files[name] = mode + ":" + Convert.ToHexString(SHA256.HashData(stream));
                    }
                }
                catch (FileNotFoundException)
                {
                    // A tracked deletion is represented by absence from this inventory.
                }
                catch (DirectoryNotFoundException)
                {
                    // A tracked deletion is represented by absence from this inventory.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException)
        {
            problem = "The worker could not read the whole workspace inventory; assignment scope is incomplete.";
        }

        return new WorkspaceSnapshot(files, problem);
    }

    public static RunScope Compare(WorkspaceSnapshot before, WorkspaceSnapshot after, IReadOnlyList<string> permitted)
    {
        string[] changes = before.Files.Keys.Union(after.Files.Keys, StringComparer.Ordinal)
            .Where(path => before.Files.GetValueOrDefault(path) != after.Files.GetValueOrDefault(path)).Order(StringComparer.Ordinal).ToArray();
        string[] patterns = permitted.Select(p => p.StartsWith("path:", StringComparison.Ordinal) ? p[5..].Trim() : p.Trim())
            .Where(Relative).ToArray();
        string[] outside = changes.Where(path => !patterns.Any(pattern => Matches(path, pattern))).ToArray();
        string? problem = before.Problem ?? after.Problem;
        if (patterns.Length == 0)
        {
            problem ??= "The assignment declares no interpretable repository-relative permitted paths; no changed path is authorized by this audit.";
        }

        return new RunScope(changes.Length, outside.Length, outside.Take(1000).Select(Display).ToArray(), problem);
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
        return Regex.IsMatch(path, "\\A" + expression + "\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool Relative(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2000
        && !Path.IsPathRooted(path) && !path.Contains(':', StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl) && path.Split('/').All(part => part is not ("." or ".."));

    private static string Display(string path) => path.Length <= 500 && Regex.IsMatch(path, @"\A[A-Za-z0-9_./ @()+,=\-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && !EstateChecks.Content("scope.txt", Encoding.UTF8.GetBytes(path)).HasErrors
        ? path : "path with unportable name, sha256:" + Hash(Encoding.UTF8.GetBytes(path));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
