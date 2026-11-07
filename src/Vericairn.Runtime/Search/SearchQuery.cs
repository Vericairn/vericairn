// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Vericairn.Runtime.Search;

/// <summary>
/// A parsed query. Queries are data: words, double-quoted phrases and filters from a fixed list (<c>type:</c>, <c>kind:</c>,
/// <c>path:</c>, <c>key:</c>, <c>since:</c>, <c>before:</c> and <c>history</c>), at most 1,024 UTF-8 bytes. Anything else,
/// including the engine's own operators and column syntax, is matched as text; the engine only ever receives analyzer output,
/// quoted.
/// </summary>
internal sealed record SearchQuery(string Text, IReadOnlyList<SearchQuery.Term> Terms, IReadOnlyList<IReadOnlyList<string>> Phrases, SearchQuery.Filter Filters)
{
    /// <summary>The largest query, in UTF-8 bytes.</summary>
    internal const int MaxBytes = 1024;

    /// <summary>The most words and phrases one query searches.</summary>
    internal const int MaxTerms = 32;

    /// <summary>The most filters one query applies.</summary>
    internal const int MaxFilters = 16;

    /// <summary>The filter names a query understands, besides the bare word <c>history</c>.</summary>
    internal static readonly IReadOnlyList<string> FilterNames = ["type", "kind", "path", "key", "since", "before"];

    /// <summary>One word: its analyzed parts and, for a connected identifier, the whole form.</summary>
    internal sealed record Term(string Normalized, IReadOnlyList<string> Parts, string? Whole);

    /// <summary>The filters, each applied before ranking.</summary>
    internal sealed record Filter(IReadOnlyList<string> Types, IReadOnlyList<string> Kinds, IReadOnlyList<string> Paths, IReadOnlyList<string> Keys, string? Since, string? Before, bool History);

    /// <summary>Why a query was refused, in a sentence that names no source.</summary>
    internal sealed record Refusal(string Code, string Message);

    /// <summary>Every term's normalized form: what exact identity matches compare.</summary>
    internal IEnumerable<string> Exact => Terms.Select(t => t.Whole ?? t.Normalized).Distinct(StringComparer.Ordinal);

    /// <summary>Parses a query, or refuses it with a reason.</summary>
    internal static (SearchQuery? Query, Refusal? Refusal) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, new("search-query-empty", "A search needs at least one word or phrase."));
        int bytes;
        try { bytes = new UTF8Encoding(false, true).GetByteCount(text); }
        catch (EncoderFallbackException) { return (null, new("search-query-invalid", "A search query must be valid Unicode text.")); }
        if (bytes > MaxBytes) return (null, new("search-query-oversize", $"A search query is at most {MaxBytes} UTF-8 bytes; this one is {bytes.ToString(CultureInfo.InvariantCulture)}."));
        foreach (char c in text)
        {
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r'))
                return (null, new("search-query-invalid", "A search query holds no control characters."));
        }

        var terms = new List<Term>();
        var phrases = new List<IReadOnlyList<string>>();
        var types = new List<string>();
        var kinds = new List<string>();
        var paths = new List<string>();
        var keys = new List<string>();
        string? since = null, before = null;
        bool history = false;
        int filters = 0;
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text[i] == '"')
            {
                int close = text.IndexOf('"', i + 1);
                if (close > i)
                {
                    IReadOnlyList<string> parts = SearchAnalyzer.Analyze(text[(i + 1)..close]).Parts;
                    if (parts.Count == 1) terms.Add(new Term(parts[0], parts, null));
                    else if (parts.Count > 1) phrases.Add(parts);
                    i = close + 1;
                    continue;
                }

                // An unmatched quotation mark is only punctuation; the rest of the query is read as words.
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"') i++;
            string word = text[start..i];
            int colon = word.IndexOf(':', StringComparison.Ordinal);
            string name = colon > 0 ? word[..colon].ToLowerInvariant() : string.Empty;
            if (word.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                history = true;
                filters++;
            }
            else if (colon > 0 && FilterNames.Contains(name, StringComparer.Ordinal))
            {
                string value = word[(colon + 1)..];
                if (FilterValue(name, value) is { } refusal) return (null, refusal);
                filters++;
                switch (name)
                {
                    case "type": Add(types, value.ToLowerInvariant()); break;
                    case "kind": Add(kinds, value.ToLowerInvariant()); break;
                    case "path": Add(paths, value); break;
                    case "key": Add(keys, value.ToLowerInvariant()); break;
                    case "since": if (since is not null && since != value) return (null, Repeated(name)); since = value; break;
                    case "before": if (before is not null && before != value) return (null, Repeated(name)); before = value; break;
                }
            }
            else
            {
                // Punctuation the analyzer does not keep separates words, so engine syntax such as NEAR(a becomes the words near and a.
                foreach (SearchAnalyzer.Analysis analysis in SearchAnalyzer.Words(word))
                {
                    string? whole = analysis.Whole.Count > 0 ? analysis.Whole[0] : null;
                    terms.Add(new Term(whole ?? analysis.Parts[0], analysis.Parts, whole));
                }
            }
        }

        if (filters > MaxFilters) return (null, new("search-query-oversize", $"A search query applies at most {MaxFilters} filters."));
        if (terms.Count + phrases.Count == 0) return (null, new("search-query-empty", "A search needs at least one word or phrase; filters alone select nothing to rank."));
        if (terms.Count + phrases.Count > MaxTerms) return (null, new("search-query-oversize", $"A search query holds at most {MaxTerms} words and phrases."));
        if (since is not null && before is not null && string.CompareOrdinal(since, before) >= 0)
            return (null, new("search-query-invalid", "since: must be earlier than before:."));
        return (new SearchQuery(text, terms, phrases, new Filter(types, kinds, paths, keys, since, before, history)), null);
    }

    private static void Add(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
    }

    private static Refusal Repeated(string name) => new("search-query-invalid", $"{name}: is given once.");

    private static Refusal? FilterValue(string name, string value)
    {
        if (value.Length == 0) return new("search-query-invalid", $"{name}: needs a value.");
        switch (name)
        {
            case "type":
                return value.Length <= 64 && char.IsAsciiLetter(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                    ? null : new("search-query-invalid", "type: names a record type or knowledge kind: letters, digits and hyphens.");
            case "kind":
                return value.ToLowerInvariant() is "record" or "document" or "knowledge"
                    ? null : new("search-query-invalid", "kind: is record, document or knowledge.");
            case "key":
                return value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                    ? null : new("search-query-invalid", "key: names a record key: letters, digits, hyphens, underscores and dots.");
            case "path":
                string[] segments = value.Split('/');
                return value.Length <= 512 && !value.StartsWith('/') && !value.Contains('\\', StringComparison.Ordinal)
                    && segments.Take(segments.Length - 1).All(s => s.Length > 0) && segments.All(s => s is not ("." or ".."))
                    && value.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
                    ? null : new("search-query-invalid", "path: is a repository-relative path or path prefix, with no '.' or '..' segment.");
            default:
                return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null : new("search-query-invalid", $"{name}: is a date, yyyy-MM-dd.");
        }
    }

    /// <summary>
    /// The engine expression: every term and phrase joined by OR, each quoted. A connected identifier matches whole in the identifier
    /// column or as the phrase of its parts anywhere.
    /// </summary>
    internal string Expression()
    {
        var clauses = new List<string>();
        foreach (Term term in Terms)
        {
            string parts = SearchAnalyzer.Quote(string.Join(' ', term.Parts));
            clauses.Add(term.Whole is null ? parts : "(ident : " + SearchAnalyzer.Quote(term.Whole) + " OR " + parts + ")");
        }

        foreach (IReadOnlyList<string> phrase in Phrases) clauses.Add(SearchAnalyzer.Quote(string.Join(' ', phrase)));
        return string.Join(" OR ", clauses);
    }

    /// <summary>Every analyzed token the query searches, for matched-field and excerpt checks.</summary>
    internal IReadOnlySet<string> Tokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (Term term in Terms)
        {
            tokens.UnionWith(term.Parts);
            if (term.Whole is not null) tokens.Add(term.Whole);
        }

        foreach (IReadOnlyList<string> phrase in Phrases) tokens.UnionWith(phrase);
        return tokens;
    }

    /// <summary>The query as a selection records it: the exact text, parsed terms, phrases and filters.</summary>
    internal JsonObject ToJson()
    {
        var filters = new JsonObject();
        if (Filters.Types.Count > 0) filters["type"] = Array(Filters.Types);
        if (Filters.Kinds.Count > 0) filters["kind"] = Array(Filters.Kinds);
        if (Filters.Paths.Count > 0) filters["path"] = Array(Filters.Paths);
        if (Filters.Keys.Count > 0) filters["key"] = Array(Filters.Keys);
        if (Filters.Since is not null) filters["since"] = Filters.Since;
        if (Filters.Before is not null) filters["before"] = Filters.Before;
        filters["history"] = Filters.History;
        return new JsonObject
        {
            ["text"] = Text,
            ["terms"] = Array(Terms.Select(t => t.Normalized)),
            ["phrases"] = new JsonArray(Phrases.Select(p => (JsonNode)Array(p)).ToArray()),
            ["filters"] = filters,
        };
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());
}
