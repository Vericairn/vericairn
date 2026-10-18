// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;

namespace Vericairn.Records.Estate;

/// <summary>Portable Markdown bytes and literal values for installed generators; no ambient culture, clock or filesystem.</summary>
public static class DocumentMarkdown
{
    /// <summary>UTF-8 without BOM, LF line endings, no trailing horizontal whitespace, and exactly one final newline.</summary>
    public static byte[] Bytes(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string text = content.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = string.Join('\n', text.Split('\n').Select(line => line.TrimEnd(' ', '\t'))).TrimEnd('\n') + "\n";
        return new UTF8Encoding(false, true).GetBytes(text);
    }

    /// <summary>A literal inline value, safe in headings, tables and link labels; source markup cannot become markup.</summary>
    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = new StringBuilder();
        foreach (Rune rune in value.Replace("\r\n", "\n", StringComparison.Ordinal).EnumerateRunes())
        {
            int c = rune.Value;
            if (Rune.IsControl(rune)) text.Append(' ');
            else if (c == '&') text.Append("&amp;");
            else if (c == '<') text.Append("&lt;");
            else if (c == '>') text.Append("&gt;");
            else if (c < 128 && !char.IsAsciiLetterOrDigit((char)c) && c != ' ')
                text.Append("&#").Append(c.ToString(CultureInfo.InvariantCulture)).Append(';');
            else text.Append(rune.ToString());
        }
        return text.ToString();
    }

    /// <summary>A link to an explicitly selected repository path. Arbitrary URI schemes and executable destinations are refused.</summary>
    public static string Link(string label, string path)
    {
        if (!DocumentPatterns.IsSafePath(path)) throw new ArgumentException("A source link requires a portable repository path.", nameof(path));
        return "[" + Text(label) + "](" + path + ")";
    }

    /// <summary>A fixed English UTC timestamp; the local time zone and current culture cannot affect it.</summary>
    public static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
