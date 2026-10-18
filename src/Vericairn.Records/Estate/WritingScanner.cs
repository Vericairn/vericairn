// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

internal sealed record SpacingOccurrence(string Token, int Line, int ByteOffset, int ByteLength, Finding Diagnostic);

// A finite line grammar, shared by the existing check and the snapshot evaluator. No Markdown rendering,
// environment, clock or source access occurs here. Offsets always refer to the original UTF-8 text.
internal static partial class WritingScanner
{
    internal const string Contract = "vericairn.writing-spacing/2";
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "http", "https", "html", "python", "sqlite", "float", "uint", "utf", "unicode", "windows", "macos", "ipv", "mp", "ecdsa", "sha",
    };

    internal static IReadOnlyList<SpacingOccurrence> Scan(string text, string location)
    {
        List<SpacingOccurrence> occurrences = [];
        int lineNumber = 0, byteOffset = 0, fenceLength = 0;
        char fence = '\0';
        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            ReadOnlySpan<char> fenceLine = rawLine.AsSpan().TrimEnd('\r');
            int spaces = 0;
            while (spaces < fenceLine.Length && fenceLine[spaces] == ' ') spaces++;
            int run = 0;
            char marker = spaces < fenceLine.Length ? fenceLine[spaces] : '\0';
            if (spaces <= 3 && marker is '`' or '~')
                while (spaces + run < fenceLine.Length && fenceLine[spaces + run] == marker) run++;
            bool skip = fenceLength > 0;
            if (fenceLength > 0)
            {
                if (marker == fence && run >= fenceLength && fenceLine[(spaces + run)..].IndexOfAnyExcept(' ', '\t') < 0) fenceLength = 0;
            }
            else if (run >= 3 && (marker == '~' || !fenceLine[(spaces + run)..].Contains('`')))
            {
                fence = marker; fenceLength = run; skip = true;
            }
            if (!skip)
            {
                string line = CodeSpan().Replace(rawLine, m => new string(' ', m.Length));
                foreach (Match match in WordRunIntoNumber().Matches(line))
                {
                    if (Allowlist.Contains(match.Groups["word"].Value)) continue;
                    occurrences.Add(new(match.Value, lineNumber, byteOffset + Encoding.UTF8.GetByteCount(rawLine.AsSpan(0, match.Index)),
                        Encoding.UTF8.GetByteCount(rawLine.AsSpan(match.Index, match.Length)),
                        new(Severity.Warning, "writing.spacing", location + ":" + lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "'" + match.Value + "': put a space between the word and the number, or write an identifier as code.")));
                }
            }
            byteOffset += Encoding.UTF8.GetByteCount(rawLine) + 1;
        }
        return occurrences;
    }

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"(?<![A-Za-z0-9_./:\\-])(?<word>[A-Za-z][a-z]{3,})(?<number>[0-9]+)(?![A-Za-z0-9_/:-]|\.[0-9A-Za-z])")]
    private static partial Regex WordRunIntoNumber();
}
