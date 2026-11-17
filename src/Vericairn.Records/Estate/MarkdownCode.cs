// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-138), which characters of a Markdown text are code, as CommonMark reads them: fenced code blocks, indented code
/// blocks and inline code spans, at the top level, in list items and in block quotes. A link written inside code is shown, never
/// followed, so the links-resolve check passes over it. The text itself is never changed, so every position outside code keeps its
/// line and column.
/// </summary>
internal static class MarkdownCode
{
    /// <summary>One flag per character of the text: true where the character is code.</summary>
    internal static bool[] Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var code = new bool[text.Length];
        MarkBlocks(text, code);
        MarkSpans(text, code);
        return code;
    }

    private static void MarkBlocks(string text, bool[] code)
    {
        // The content column of each open list item, innermost last.
        var lists = new Stack<int>();
        char fence = '\0';
        int fenceLength = 0, fenceIndent = 0;
        bool paragraph = false, blank = true;
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            int length = end - start;
            if (length > 0 && text[end - 1] == '\r') length--;

            // A block quote's markers are a container, as a list item's indentation is; what follows them is read as a line.
            int content = start;
            while (true)
            {
                int spaces = 0;
                while (spaces < 3 && content + spaces < start + length && text[content + spaces] == ' ') spaces++;
                if (content + spaces < start + length && text[content + spaces] == '>')
                {
                    content += spaces + 1;
                    if (content < start + length && text[content] == ' ') content++;
                    continue;
                }

                break;
            }

            (int indent, int first) = Indentation(text, content, start + length);
            bool isBlank = first >= start + length;
            int listColumn = lists.Count == 0 ? 0 : lists.Peek();
            if (fenceLength > 0)
            {
                Mark(code, start, end);
                int run = Run(text, first, start + length, fence);
                if (indent - fenceIndent <= 3 && run >= fenceLength && IsBlank(text, first + run, start + length)) fenceLength = 0;
                paragraph = false;
                blank = false;
            }
            else if (isBlank)
            {
                paragraph = false;
                blank = true;
            }
            else if (indent - listColumn <= 3 && first < start + length && text[first] is '`' or '~' && Run(text, first, start + length, text[first]) is >= 3 and int opening
                && (text[first] == '~' || text.AsSpan(first + opening, start + length - first - opening).IndexOf('`') < 0))
            {
                // A fence opens, even where it interrupts a paragraph; an unclosed one runs to the end of the text.
                fence = text[first];
                fenceLength = opening;
                fenceIndent = indent;
                Mark(code, start, end);
                paragraph = false;
                blank = false;
            }
            else if (indent - listColumn >= 4 && !paragraph)
            {
                // An indented code block never interrupts a paragraph, and inside a list item it is indented past the item's content.
                Mark(code, start, end);
                blank = false;
            }
            else
            {
                if (blank || !paragraph)
                {
                    while (lists.Count > 0 && indent < lists.Peek()) lists.Pop();
                }

                if (ListItem(text, first, start + length) is int marker)
                {
                    while (lists.Count > 0 && indent < lists.Peek()) lists.Pop();
                    lists.Push(indent + marker);
                }

                paragraph = !IsHeading(text, first, start + length);
                blank = false;
            }

            start = end + 1;
        }
    }

    // Inline code: a run of backticks opens a span that the next run of exactly as many closes, within one paragraph and outside code
    // blocks. A backslash before a backtick outside a span makes it literal. A run with no closing run is literal text.
    private static void MarkSpans(string text, bool[] code)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (code[i])
            {
                i++;
                continue;
            }

            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '`' && !code[i + 1])
            {
                i += 2;
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
                if (text[j] == '\n' && IsBlankLine(text, j + 1))
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

    private static int SpanRun(string text, bool[] code, int at)
    {
        int run = 0;
        while (at + run < text.Length && text[at + run] == '`' && !code[at + run]) run++;
        return run;
    }

    // The width of a line's leading whitespace, a tab advancing to the next multiple of four, and where its first other character is.
    private static (int Indent, int First) Indentation(string text, int from, int to)
    {
        int width = 0, at = from;
        while (at < to && text[at] is ' ' or '\t')
        {
            width = text[at] == '\t' ? width + 4 - (width % 4) : width + 1;
            at++;
        }

        return (width, at);
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

    // A list item's marker and the spaces after it, whose width is where the item's content starts past its indentation: a bullet or
    // up to nine digits with a period or parenthesis, then one to four spaces, or one when more follow or the line ends there.
    private static int? ListItem(string text, int first, int to)
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

        if (at == to) return at - first + 1;
        int spaces = Run(text, at, to, ' ');
        if (spaces == 0) return null;
        return at - first + (spaces > 4 || at + spaces == to ? 1 : spaces);
    }

    private static bool IsHeading(string text, int first, int to)
    {
        int hashes = Run(text, first, to, '#');
        return hashes is >= 1 and <= 6 && (first + hashes == to || text[first + hashes] is ' ' or '\t');
    }

    private static void Mark(bool[] code, int from, int to)
    {
        for (int at = from; at < to && at < code.Length; at++) code[at] = true;
    }
}
