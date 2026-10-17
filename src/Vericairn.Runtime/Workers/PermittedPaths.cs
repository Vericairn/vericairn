// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

/// <summary>Repository-relative patterns and workspace-boundary checks used by the worker audits.</summary>
public static class PermittedPaths
{
    /// <summary>
    /// Interprets a brief entry as a relative pattern. Bare patterns contain no whitespace;
    /// an explicit path: prefix distinguishes a literal path containing spaces from prose.
    /// </summary>
    public static string? Pattern(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool explicitPath = entry.StartsWith("path:", StringComparison.Ordinal);
        string pattern = (explicitPath ? entry[5..] : entry).Trim();
        return Relative(pattern) && (explicitPath || !pattern.Any(char.IsWhiteSpace))
            && pattern.IndexOfAny(['~', '$', '`']) < 0 ? pattern : null;
    }

    /// <summary>Whether text has a repository-relative path shape, independent of a brief entry's prose or prefix.</summary>
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

        // Each row records which path prefixes the pattern so far can match. No backtracking or clock:
        // O(pattern.Length * path.Length) work and O(path.Length) memory, including repeated wildcards.
        var current = new bool[path.Length + 1];
        var next = new bool[path.Length + 1];
        current[0] = true;
        for (int i = 0; i < plain.Length; i++)
        {
            char token = plain[i];
            if (token == '*')
            {
                // **/ takes priority over **, so ***/ is a segment star followed by a directory star.
                bool recursive = i + 1 < plain.Length && plain[i + 1] == '*'
                    && !(i + 3 < plain.Length && plain[i + 2] == '*' && plain[i + 3] == '/');
                if (recursive)
                {
                    i++;
                }

                bool directory = recursive && i + 1 < plain.Length && plain[i + 1] == '/';
                if (directory)
                {
                    i++;
                }

                next[0] = current[0];
                bool spanning = current[0];
                for (int j = 1; j <= path.Length; j++)
                {
                    // **/ may end only after a slash, or consume nothing. Like the former regex's dot,
                    // recursive stars do not consume LF; * and ? exclude only the segment separator.
                    next[j] = current[j] || (directory ? path[j - 1] == '/' && spanning
                        : next[j - 1] && path[j - 1] != (recursive ? '\n' : '/'));
                    spanning = current[j] || spanning && path[j - 1] != '\n';
                }
            }
            else
            {
                next[0] = false;
                for (int j = 1; j <= path.Length; j++)
                {
                    next[j] = current[j - 1] && (token == '?' ? path[j - 1] != '/' : path[j - 1] == token);
                }
            }

            (current, next) = (next, current);
        }

        return current[path.Length];
    }

    /// <summary>
    /// Checks an invocation path within the physical workspace; expansion and links are not assumed safe.
    /// Inspection failures propagate to the caller, never becoming a false verdict.
    /// </summary>
    public static bool Allows(string path, string workspace, IReadOnlyList<string> permitted)
    {
        ArgumentNullException.ThrowIfNull(permitted);
        string? relative = WithinWorkspace(path, workspace);
        return relative is not null && relative != "." && permitted.Select(Pattern).Any(pattern => pattern is not null && Matches(relative, pattern));
    }

    internal static bool Contains(string path, string workspace) => WithinWorkspace(path, workspace) is not null;

    private static string? WithinWorkspace(string path, string workspace)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(workspace);
        if (path.IndexOfAny(['~', '$', '`', '\0']) >= 0 || path.Contains(':', StringComparison.Ordinal) && !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        string root = Path.GetFullPath(workspace);
        string full = Path.GetFullPath(path.Replace('\\', '/'), root);
        string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (relative != "." && !Relative(relative))
        {
            return null;
        }

        string current = root;
        foreach (string part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // A path the invocation proposes creating need not exist yet.
            }
        }

        return relative;
    }
}
