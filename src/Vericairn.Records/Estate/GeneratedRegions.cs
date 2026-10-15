// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Estate;

/// <summary>
/// Generated regions of an authored document: the content between <c>&lt;!-- vericairn:generated id --&gt;</c> and
/// <c>&lt;!-- vericairn:end id --&gt;</c>, each on its own line, belongs to a generator; everything outside them to the author.
/// </summary>
public static class GeneratedRegions
{
    /// <summary>The line that opens a region.</summary>
    public static string Start(string id) => $"<!-- vericairn:generated {id} -->";

    /// <summary>The line that closes a region.</summary>
    public static string End(string id) => $"<!-- vericairn:end {id} -->";

    /// <summary>The content between a region's markers, from the line after the opening one to the closing line; null when a marker is missing, repeated or out of order.</summary>
    public static string? Content(string text, string id)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Find(text, id) is (int from, int to) ? text[from..to] : null;
    }

    /// <summary>The text with a region's content replaced and nothing else changed; null when its markers are not in place.</summary>
    public static string? Replace(string text, string id, string content)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(content);
        return Find(text, id) is (int from, int to) ? string.Concat(text.AsSpan(0, from), Normalize(content), text.AsSpan(to)) : null;
    }

    /// <summary>A generator's output as region content: line feeds only, ending in one, or empty.</summary>
    public static string Normalize(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string text = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";
    }

    private static (int From, int To)? Find(string text, string id)
    {
        string start = Start(id);
        string end = End(id);
        int opening = Line(text, start, 0);
        if (opening < 0 || Line(text, start, opening + start.Length) >= 0)
        {
            return null;
        }

        int from = text.IndexOf('\n', opening);
        if (from < 0)
        {
            return null;
        }

        int closing = Line(text, end, from + 1);
        if (closing < 0 || Line(text, end, closing + end.Length) >= 0)
        {
            return null;
        }

        return (from + 1, closing);
    }

    /// <summary>Where a marker stands alone on a line, from an offset; -1 when it does not.</summary>
    private static int Line(string text, string marker, int offset)
    {
        for (int at = text.IndexOf(marker, offset, StringComparison.Ordinal); at >= 0; at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            bool starts = at == 0 || text[at - 1] == '\n';
            int after = at + marker.Length;
            bool ends = after == text.Length || text[after] == '\n' || (text[after] == '\r' && after + 1 < text.Length && text[after + 1] == '\n');
            if (starts && ends)
            {
                return at;
            }
        }

        return -1;
    }
}
