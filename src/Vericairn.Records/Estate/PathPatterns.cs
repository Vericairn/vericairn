// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Estate;

/// <summary>
/// The two path grammars an estate reads, kept in one place so that every reader matches alike: the worker's grammar of a brief's
/// permitted changes (a literal matches the path and everything below it, <c>*</c> and <c>?</c> match within one component and
/// <c>**</c> across components, without backtracking), which the runtime's PermittedPaths exposes, and the policy grammar of a
/// <c>repository</c> selection's selectors (a literal matches that exact path, <c>*</c> matches within one component and <c>**</c>
/// only as a whole component). From 0.9.4 (CODE-73) the overlap check compiles both exactly (<see cref="PathIntersection"/>).
/// </summary>
internal static class PathPatterns
{
    /// <summary>
    /// Interprets a brief entry as a relative pattern. Bare patterns contain no whitespace; an explicit path: prefix distinguishes a
    /// literal path containing spaces from prose. Null for prose, which grants nothing.
    /// </summary>
    internal static string? Pattern(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool explicitPath = entry.StartsWith("path:", StringComparison.Ordinal);
        string pattern = (explicitPath ? entry[5..] : entry).Trim();
        return Relative(pattern) && (explicitPath || !pattern.Any(char.IsWhiteSpace))
            && pattern.IndexOfAny(['~', '$', '`']) < 0 ? pattern : null;
    }

    /// <summary>Whether text has a repository-relative path shape, independent of a brief entry's prose or prefix.</summary>
    internal static bool Relative(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2000
        && !Path.IsPathRooted(path) && !path.Contains(':', StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Any(char.IsControl) && path.Split('/').All(part => part is not ("." or ".."));

    /// <summary>
    /// A valid path, the language the overlap check searches: <see cref="Relative"/>'s rules, with nonempty components and no leading or
    /// trailing slash added (the estate's design/measured-work.md § 5).
    /// </summary>
    internal static bool Valid(string path) => Relative(path) && path.Split('/').All(part => part.Length > 0);

    /// <summary>Whether a normalized relative path matches a literal path, directory, or glob of the worker's grammar.</summary>
    internal static bool Matches(string path, string pattern)
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

    /// <summary>Whether a repository selection's selector, in the policy grammar, matches a path.</summary>
    internal static bool SelectorMatches(string selector, string path)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(path);
        string[] pattern = selector.Split('/');
        string[] parts = path.Split('/');
        var memo = new Dictionary<(int, int), bool>();
        return Match(0, 0);
        bool Match(int p, int s)
        {
            if (memo.TryGetValue((p, s), out bool cached)) return cached;
            bool result = p == pattern.Length ? s == parts.Length : pattern[p] == "**"
                ? Match(p + 1, s) || s < parts.Length && Match(p, s + 1)
                : s < parts.Length && Component(pattern[p], parts[s]) && Match(p + 1, s + 1);
            memo[(p, s)] = result;
            return result;
        }
    }

    private static bool Component(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, start = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == '*') { star = p++; start = v; }
            else if (p < pattern.Length && pattern[p] == value[v]) { p++; v++; }
            else if (star >= 0) { p = star + 1; v = ++start; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
