// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Vericairn.Runtime.Search;

/// <summary>
/// The analyzer of <c>vericairn.search.keyword/1</c>: Unicode NFKC normalization and invariant case folding, then words split into
/// their letter and digit runs. A word joined by connectors (an id, key, version, hash or path, such as <c>CONTEXT-02</c>,
/// <c>0.9.2</c> or <c>design/search.md</c>) is also kept whole. No language-specific stemming or stop words. The same analysis
/// serves indexing and queries, so the engine only ever sees analyzer output, quoted.
/// </summary>
internal static class SearchAnalyzer
{
    /// <summary>The analyzer's identity, recorded in every selection.</summary>
    internal const string Identity = "nfkc-casefold; letter-digit runs; connected identifiers also whole; no stemming";

    /// <summary>Characters that join an identifier's parts. They never start or end a whole token.</summary>
    internal const string Connectors = "-_./:@#+";

    private static readonly SearchValues<char> ConnectorValues = SearchValues.Create(Connectors);

    // Bounds keep a pathological value (a base64 blob, a long run of digits) from becoming one enormous token.
    private const int MaxPart = 128;
    private const int MaxWhole = 256;

    internal readonly record struct Analysis(IReadOnlyList<string> Parts, IReadOnlyList<string> Whole);

    /// <summary>NFKC, then invariant lower case.</summary>
    internal static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalized;
        try { normalized = text.IsNormalized(NormalizationForm.FormKC) ? text : text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { normalized = text; } // Invalid UTF-16 is analyzed as it is; queries refuse it earlier.
        return normalized.ToLowerInvariant();
    }

    /// <summary>Analyzes text into its parts, in order, and the whole connected identifiers it holds.</summary>
    internal static Analysis Analyze(string text)
    {
        var parts = new List<string>();
        var whole = new List<string>();
        Analyze(text, parts, whole);
        return new(parts, whole);
    }

    /// <summary>Each word of the text on its own: its parts and, for a connected identifier, its whole form.</summary>
    internal static IReadOnlyList<Analysis> Words(string text)
    {
        var words = new List<Analysis>();
        string normalized = Normalize(text);
        int i = 0;
        while (i < normalized.Length)
        {
            if (!IsWordCharacter(normalized, i, out int width))
            {
                i += width;
                continue;
            }

            int start = i;
            while (i < normalized.Length && IsWordCharacter(normalized, i, out width)) i += width;
            var parts = new List<string>();
            var whole = new List<string>();
            Word(normalized.AsSpan(start, i - start), parts, whole);
            if (parts.Count > 0) words.Add(new Analysis(parts, whole));
        }

        return words;
    }

    /// <summary>Appends the analysis of text to existing lists.</summary>
    internal static void Analyze(string text, List<string> parts, List<string> whole)
    {
        string normalized = Normalize(text);
        int i = 0;
        while (i < normalized.Length)
        {
            if (!IsWordCharacter(normalized, i, out int width))
            {
                i += width;
                continue;
            }

            int start = i;
            while (i < normalized.Length && IsWordCharacter(normalized, i, out width)) i += width;
            Word(normalized.AsSpan(start, i - start), parts, whole);
        }
    }

    /// <summary>True when a position starts a letter, digit, combining mark or connector.</summary>
    private static bool IsWordCharacter(string text, int index, out int width)
    {
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            width = 2;
            return IsTokenCategory(CharUnicodeInfo.GetUnicodeCategory(text, index));
        }

        width = 1;
        char c = text[index];
        return ConnectorValues.Contains(c) || IsTokenCategory(CharUnicodeInfo.GetUnicodeCategory(c));
    }

    private static bool IsTokenCategory(UnicodeCategory category) => category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
        or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
        or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
        or UnicodeCategory.EnclosingMark or UnicodeCategory.PrivateUse;

    private static void Word(ReadOnlySpan<char> word, List<string> parts, List<string> whole)
    {
        // Connectors at either end are punctuation ("end." or "--flag"), not part of an identifier.
        int first = 0;
        int last = word.Length;
        while (first < last && ConnectorValues.Contains(word[first])) first++;
        while (last > first && ConnectorValues.Contains(word[last - 1])) last--;
        if (first == last) return;
        ReadOnlySpan<char> trimmed = word[first..last];
        int before = parts.Count;
        int start = -1;
        for (int i = 0; i <= trimmed.Length; i++)
        {
            bool connector = i == trimmed.Length || ConnectorValues.Contains(trimmed[i]);
            if (!connector && start < 0) start = i;
            if (connector && start >= 0)
            {
                parts.Add(Bound(trimmed[start..i], MaxPart));
                start = -1;
            }
        }

        if (parts.Count - before > 1 || trimmed.IndexOfAny(ConnectorValues) >= 0) whole.Add(Bound(trimmed, MaxWhole));
    }

    private static string Bound(ReadOnlySpan<char> value, int max)
    {
        if (value.Length <= max) return value.ToString();
        int end = max;
        if (char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end].ToString();
    }

    /// <summary>
    /// A value quoted for the engine: always a double-quoted string with its quotes doubled, so engine syntax in it is only text.
    /// Analyzer output holds no quote, but the escape does not depend on that.
    /// </summary>
    internal static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
