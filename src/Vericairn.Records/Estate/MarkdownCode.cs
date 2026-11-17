// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-138), which characters of a Markdown text are code, as CommonMark reads them: fenced code blocks, indented code
/// blocks and inline code spans, at the top level, in list items and in block quotes. A link written inside code is shown, never
/// followed, so the links-resolve check passes over it. The text itself is never changed, so every position outside code keeps its
/// line and column. Where the reading is uncertain it errs toward prose, so a link is checked rather than hidden.
/// </summary>
internal static class MarkdownCode
{
    /// <summary>One flag per character of the text: true where the character is code.</summary>
    internal static bool[] Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var code = new bool[text.Length];
        // A line feed after which an inline span cannot continue, because a new block starts on the next line.
        var barrier = new bool[text.Length];
        MarkBlocks(text, code, barrier);
        MarkSpans(text, code, barrier);
        return code;
    }

    // One line as its containers leave it: how many block-quote markers it opens with, the column where their content starts, and its
    // first character that is not a space or tab, with that character's column. Columns count a tab to the next multiple of four.
    private readonly record struct Line(int Depth, int ContentColumn, int First, int FirstColumn, bool Blank);

    private static void MarkBlocks(string text, bool[] code, bool[] barrier)
    {
        // The content column of each open list item, innermost last.
        var lists = new List<int>();
        // An open fence: its marker, its length, and the quote depth and content column of the container it opened in.
        char fence = '\0';
        int fenceLength = 0, fenceDepth = 0, fenceBase = 0;
        bool paragraph = false, blank = true;
        int previousDepth = 0;
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            int to = end > start && text[end - 1] == '\r' ? end - 1 : end;

            if (fenceLength > 0)
            {
                // Inside a fence only the markers of the fence's own containers are read, so an example's own "> ```" stays code. The
                // fence ends with its container: a quote with fewer markers, or a line left of the list item's content.
                Line inside = Read(text, start, to, fenceDepth);
                if (inside.Depth == fenceDepth && (inside.Blank || inside.FirstColumn >= fenceBase))
                {
                    Mark(code, start, end);
                    int run = Run(text, inside.First, to, fence);
                    if (inside.FirstColumn - fenceBase <= 3 && run >= fenceLength && IsBlank(text, inside.First + run, to)) fenceLength = 0;
                    paragraph = false;
                    blank = false;
                    previousDepth = inside.Depth;
                    start = end + 1;
                    continue;
                }

                fenceLength = 0;
                Barrier(barrier, start);
            }

            Line line = Read(text, start, to, int.MaxValue);
            bool block = line.Depth != previousDepth, single = false;

            // The list items whose content this line stands left of. It leaves them, unless it only continues their paragraph lazily:
            // after a blank line or another block it leaves them at once, and a line that opens a block of its own leaves them too.
            int keep = lists.Count;
            while (keep > 0 && line.FirstColumn < lists[keep - 1]) keep--;
            int outerColumn = Math.Max(keep == 0 ? 0 : lists[keep - 1], line.ContentColumn);
            int outerIndent = line.FirstColumn - outerColumn;
            if (!line.Blank && (blank || !paragraph)) lists.RemoveRange(keep, lists.Count - keep);
            int baseColumn = Math.Max(lists.Count == 0 ? 0 : lists[^1], line.ContentColumn);
            int indent = line.FirstColumn - baseColumn;
            if (line.Blank)
            {
                paragraph = false;
                blank = true;
                block = true;
            }
            else if (outerIndent <= 3 && text[line.First] is '`' or '~' && Run(text, line.First, to, text[line.First]) is >= 3 and int opening
                && (text[line.First] == '~' || text.AsSpan(line.First + opening, to - line.First - opening).IndexOf('`') < 0))
            {
                // A fence opens, even where it interrupts a paragraph, in the container the line stands in; one never closed runs to the
                // end of that container.
                lists.RemoveRange(keep, lists.Count - keep);
                fence = text[line.First];
                fenceLength = opening;
                fenceDepth = line.Depth;
                fenceBase = outerColumn;
                Mark(code, start, end);
                paragraph = false;
                blank = false;
                block = true;
            }
            else if (indent >= 4 && !paragraph)
            {
                // An indented code block never interrupts a paragraph, and inside a list item it is indented past the item's content.
                Mark(code, start, end);
                blank = false;
                block = true;
            }
            else
            {
                if (indent <= 3 && paragraph && IsSetextUnderline(text, line.First, to))
                {
                    paragraph = false;
                    single = true;
                }
                else if (outerIndent <= 3 && (IsThematicBreak(text, line.First, to) || IsHeading(text, line.First, to)))
                {
                    // A thematic break or a heading is a block of its own, never a lazy continuation, so it leaves the items it stands
                    // left of.
                    lists.RemoveRange(keep, lists.Count - keep);
                    paragraph = false;
                    single = true;
                }
                else
                {
                    paragraph = true;
                    if (indent <= 3 && ListItem(text, line.First, line.FirstColumn, to) is (int content, int item))
                    {
                        lists.RemoveRange(keep, lists.Count - keep);
                        lists.Add(content);
                        block = true;

                        // An item whose content opens with a fence, such as "1. ```sh", holds that fence.
                        if (item < to && text[item] is '`' or '~' && Run(text, item, to, text[item]) is >= 3 and int run
                            && (text[item] == '~' || text.AsSpan(item + run, to - item - run).IndexOf('`') < 0))
                        {
                            fence = text[item];
                            fenceLength = run;
                            fenceDepth = line.Depth;
                            fenceBase = content;
                            Mark(code, start, end);
                            paragraph = false;
                        }
                    }
                }

                blank = false;
            }

            if (block || single) Barrier(barrier, start);
            if (single && end < text.Length) barrier[end] = true;
            previousDepth = line.Depth;
            start = end + 1;
        }
    }

    // Reads a line's block-quote markers, at most the given number, then its first other character. A tab after a marker counts one
    // column as the marker's space; the rest of the tab is indentation of the quote's content.
    private static Line Read(string text, int start, int to, int markers)
    {
        int at = start, column = 0, depth = 0, contentColumn = 0;
        while (depth < markers)
        {
            int probe = at, probeColumn = column, spaces = 0;
            while (spaces < 3 && probe < to && text[probe] == ' ') { probe++; probeColumn++; spaces++; }
            if (probe >= to || text[probe] != '>') break;
            probe++;
            probeColumn++;
            depth++;
            if (probe < to && text[probe] == ' ') { probe++; probeColumn++; contentColumn = probeColumn; }
            else if (probe < to && text[probe] == '\t') contentColumn = probeColumn + 1;
            else contentColumn = probeColumn;
            at = probe;
            column = probeColumn;
        }

        int first = at, firstColumn = column;
        while (first < to && text[first] is ' ' or '\t')
        {
            firstColumn = text[first] == '\t' ? firstColumn + 4 - (firstColumn % 4) : firstColumn + 1;
            first++;
        }

        return new Line(depth, contentColumn, first, Math.Max(firstColumn, contentColumn), first >= to);
    }

    // Inline code: a run of backticks opens a span that the next run of exactly as many closes, within one block and outside code
    // blocks. A backslash before punctuation makes that character literal, an escaped backslash included, and an autolink is read
    // whole, so neither a backtick it holds nor an escaped one opens a span. A run with no closing run is literal text.
    private static void MarkSpans(string text, bool[] code, bool[] barrier)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (code[i])
            {
                i++;
                continue;
            }

            if (text[i] == '\\' && i + 1 < text.Length && !code[i + 1] && IsAsciiPunctuation(text[i + 1]))
            {
                i += 2;
                continue;
            }

            if (text[i] == '<' && Autolink(text, code, i) is int after)
            {
                i = after;
                continue;
            }

            if (text[i] != '`')
            {
                i++;
                continue;
            }

            int open = SpanRun(text, code, i);
            int close = -1;
            int j = i + open;
            while (j < text.Length && !code[j])
            {
                if (text[j] == '\n' && (barrier[j] || IsBlankLine(text, j + 1)))
                {
                    break;
                }

                if (text[j] == '`')
                {
                    int run = SpanRun(text, code, j);
                    if (run == open)
                    {
                        close = j + run;
                        break;
                    }

                    j += run;
                    continue;
                }

                j++;
            }

            if (close < 0)
            {
                i += open;
                continue;
            }

            Mark(code, i, close);
            i = close;
        }
    }

    // An autolink, <scheme:target> or <name@domain>, with no space, line break or angle bracket inside; the position after it, or null.
    private static int? Autolink(string text, bool[] code, int at)
    {
        int end = at + 1;
        while (end < text.Length && !code[end] && text[end] is not ('>' or '<' or ' ' or '\t' or '\r' or '\n')) end++;
        if (end >= text.Length || code[end] || text[end] != '>' || end == at + 1) return null;
        ReadOnlySpan<char> inside = text.AsSpan(at + 1, end - at - 1);
        int colon = inside.IndexOf(':');
        bool uri = colon is >= 2 and <= 32 && char.IsAsciiLetter(inside[0]) && !inside[..colon].ContainsAnyExcept(Scheme);
        int sign = inside.IndexOf('@');
        bool email = sign > 0 && sign < inside.Length - 1 && inside[(sign + 1)..].IndexOf('.') > 0;
        return uri || email ? end + 1 : null;
    }

    private static readonly System.Buffers.SearchValues<char> Scheme = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+.-");

    private static bool IsAsciiPunctuation(char c) => c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    private static int SpanRun(string text, bool[] code, int at)
    {
        int run = 0;
        while (at + run < text.Length && text[at + run] == '`' && !code[at + run]) run++;
        return run;
    }

    private static void Barrier(bool[] barrier, int lineStart)
    {
        if (lineStart > 0) barrier[lineStart - 1] = true;
    }

    private static int Run(string text, int from, int to, char marker)
    {
        int run = 0;
        while (from + run < to && text[from + run] == marker) run++;
        return run;
    }

    private static bool IsBlank(string text, int from, int to)
    {
        for (int at = from; at < to; at++)
        {
            if (text[at] is not (' ' or '\t')) return false;
        }

        return true;
    }

    private static bool IsBlankLine(string text, int from)
    {
        for (int at = from; at < text.Length && text[at] != '\n'; at++)
        {
            if (text[at] is not (' ' or '\t' or '\r')) return false;
        }

        return true;
    }

    // A list item's marker and the whitespace after it, giving the column where the item's content starts, and where its first character
    // is: a bullet or up to nine digits with a period or parenthesis, then one to four columns of spaces or tabs, or one column when there
    // are more or the line ends.
    private static (int Column, int First)? ListItem(string text, int first, int firstColumn, int to)
    {
        int at = first;
        if (at < to && text[at] is '-' or '*' or '+')
        {
            at++;
        }
        else
        {
            int digits = 0;
            while (at < to && digits < 10 && char.IsAsciiDigit(text[at])) { at++; digits++; }
            if (digits is 0 or > 9 || at >= to || text[at] is not ('.' or ')')) return null;
            at++;
        }

        int markerEnd = firstColumn + (at - first);
        if (at == to) return (markerEnd + 1, to);
        if (text[at] is not (' ' or '\t')) return null;
        int column = markerEnd;
        while (at < to && text[at] is ' ' or '\t')
        {
            column = text[at] == '\t' ? column + 4 - (column % 4) : column + 1;
            at++;
        }

        int width = column - markerEnd;
        return at == to || width > 4 ? (markerEnd + 1, to) : (column, at);
    }

    private static bool IsHeading(string text, int first, int to)
    {
        int hashes = Run(text, first, to, '#');
        return hashes is >= 1 and <= 6 && (first + hashes == to || text[first + hashes] is ' ' or '\t');
    }

    // Three or more of one of *, - or _, with only spaces or tabs between them.
    private static bool IsThematicBreak(string text, int first, int to)
    {
        char marker = text[first];
        if (marker is not ('*' or '-' or '_')) return false;
        int count = 0;
        for (int at = first; at < to; at++)
        {
            if (text[at] == marker) count++;
            else if (text[at] is not (' ' or '\t')) return false;
        }

        return count >= 3;
    }

    // A run of = or of - with only trailing spaces, under a paragraph line.
    private static bool IsSetextUnderline(string text, int first, int to)
    {
        char marker = text[first];
        if (marker is not ('=' or '-')) return false;
        int run = Run(text, first, to, marker);
        return IsBlank(text, first + run, to);
    }

    private static void Mark(bool[] code, int from, int to)
    {
        for (int at = from; at < to && at < code.Length; at++) code[at] = true;
    }
}
