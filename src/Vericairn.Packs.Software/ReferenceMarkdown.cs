// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Packs.Software;

internal static class ReferenceMarkdown
{
    // Selected tables and generated headings cannot be supplied by fenced examples.
    internal static IEnumerable<string> Lines(string text)
    {
        char fence = '\0';
        int length = 0;
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.TrimStart(' ');
            int indent = raw.Length - line.Length;
            char marker = line.Length > 0 ? line[0] : '\0';
            int run = marker is '`' or '~' ? line.TakeWhile(c => c == marker).Count() : 0;
            if (length > 0)
            {
                if (indent <= 3 && marker == fence && run >= length && string.IsNullOrWhiteSpace(line[run..])) length = 0;
                yield return "";
            }
            else if (indent <= 3 && run >= 3 && (marker == '~' || !line[run..].Contains('`', StringComparison.Ordinal)))
            {
                fence = marker; length = run;
                yield return "";
            }
            else yield return indent >= 4 || line.StartsWith('\t') ? "" : line;
        }
    }
}
