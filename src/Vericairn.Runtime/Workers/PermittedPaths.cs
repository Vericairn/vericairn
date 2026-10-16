// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

/// <summary>The assignment's repository-relative path patterns, shared by the event and settlement audits.</summary>
public static class PermittedPaths
{
    /// <summary>Whether text is an interpretable repository-relative path or pattern.</summary>
    public static bool Relative(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2000
        && !Path.IsPathRooted(path) && !path.Contains(':', StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl) && path.Split('/').All(part => part is not ("." or ".."));

    /// <summary>Whether a normalized relative path matches a literal path, directory, or glob.</summary>
    public static bool Matches(string path, string pattern)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pattern);
        string plain = pattern.TrimEnd('/');
        if (!plain.Contains('*', StringComparison.Ordinal) && !plain.Contains('?', StringComparison.Ordinal))
        {
            return path == plain || path.StartsWith(plain + "/", StringComparison.Ordinal);
        }

        string expression = Regex.Escape(plain).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(path, "\\A" + expression + "\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Checks an invocation path within the physical workspace; expansion and links are not assumed safe.</summary>
    public static bool Allows(string path, string workspace, IReadOnlyList<string> permitted)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(permitted);
        if (path.IndexOfAny(['~', '$', '`', '\0']) >= 0 || path.Contains(':', StringComparison.Ordinal) && !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(workspace);
            string full = Path.GetFullPath(path.Replace('\\', '/'), root);
            string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (!Relative(relative))
            {
                return false;
            }

            string current = root;
            foreach (string part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // A path the invocation proposes creating need not exist yet.
                }
            }

            return permitted.Select(p => p.StartsWith("path:", StringComparison.Ordinal) ? p[5..].Trim() : p.Trim())
                .Where(Relative).Any(pattern => Matches(relative, pattern));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
