// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Workers;

/// <summary>Repository-relative patterns and workspace-boundary checks used by the worker audits.</summary>
public static class PermittedPaths
{
    /// <summary>
    /// Interprets a brief entry as a relative pattern. Bare patterns contain no whitespace;
    /// an explicit path: prefix distinguishes a literal path containing spaces from prose.
    /// </summary>
    public static string? Pattern(string entry) => PathPatterns.Pattern(entry);

    /// <summary>Whether text has a repository-relative path shape, independent of a brief entry's prose or prefix.</summary>
    public static bool Relative(string path) => PathPatterns.Relative(path);

    /// <summary>Whether a normalized relative path matches a literal path, directory, or glob.</summary>
    public static bool Matches(string path, string pattern) => PathPatterns.Matches(path, pattern);

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
