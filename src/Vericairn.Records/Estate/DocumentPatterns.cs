// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>The fixed, portable mapping from an immutable source revision to a register member.</summary>
public static class DocumentPatterns
{
    private const string Suffix = "/{month}/{date}-{slug}-{id}.md";

    /// <summary>Whether a pattern owns a literal, bounded directory and uses exactly the supported placeholders.</summary>
    public static bool IsValid(string pattern) => pattern.EndsWith(Suffix, StringComparison.Ordinal)
        && IsSafePath(pattern[..^Suffix.Length]);

    /// <summary>A portable path with no traversal, platform aliases, reserved names or hidden directories.</summary>
    public static bool IsSafePath(string path)
    {
        if (!Patterns.RelativePath.IsMatch(path) || path.Length > 500) return false;
        return path.Split('/').All(p => p.Length > 0 && !p.StartsWith('.') && !p.EndsWith('.')
            && p.Split('.')[0].ToUpperInvariant() is not ("CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9"));
    }

    /// <summary>The owned directory, including its final slash.</summary>
    public static string Prefix(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (!IsValid(pattern)) throw new ArgumentException("Unsupported document pattern.", nameof(pattern));
        return pattern[..^Suffix.Length] + "/";
    }

    /// <summary>The declaration's concrete path or pattern.</summary>
    public static string? Destination(RecordEnvelope declaration) => EstateModel.BodyString(declaration, "path") ?? EstateModel.BodyString(declaration, "pathPattern");

    /// <summary>Conservative, case-insensitive ownership overlap, including authored files within a register.</summary>
    public static bool Overlaps(RecordEnvelope left, RecordEnvelope right)
    {
        string? a = Destination(left);
        string? b = Destination(right);
        if (a is null || b is null) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        string? ap = EstateModel.BodyString(left, "pathPattern");
        string? bp = EstateModel.BodyString(right, "pathPattern");
        return (ap is not null && IsValid(ap) && Within(b, Prefix(ap)))
            || (bp is not null && IsValid(bp) && Within(a, Prefix(bp)));

        static bool Within(string path, string prefix) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps a source revision's UTC time, title and full identity without consulting current state.</summary>
    public static string Member(string pattern, string id, DateTimeOffset at, string title)
    {
        if (!Ulid.IsValid(id)) throw new ArgumentException("A member requires an exact source revision identity.", nameof(id));
        DateTimeOffset utc = at.ToUniversalTime();
        return Prefix(pattern) + utc.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "/"
            + utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-" + Slug(title) + "-" + id + ".md";
    }

    /// <summary>A bounded ASCII slug; the full title stays in the content, and the full id prevents collisions.</summary>
    public static string Slug(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var result = new StringBuilder();
        foreach (char c in title)
        {
            if (result.Length >= 60) break;
            if (c is >= 'A' and <= 'Z') result.Append((char)(c + ('a' - 'A')));
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9') result.Append(c);
            else if (result.Length > 0 && result[^1] != '-') result.Append('-');
        }

        string slug = result.ToString().TrimEnd('-');
        return slug.Length == 0 ? "item" : slug;
    }
}
