// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Search;

/// <summary>The audiences a passage can belong to: one index segment each.</summary>
internal static class SearchAudiences
{
    internal const string Internal = "Internal";
    internal const string Members = "Members only";
    internal const string Public = "Public";

    /// <summary>The standard audiences, in their fixed order.</summary>
    internal static readonly IReadOnlyList<string> Standard = [Internal, Members, Public];

    /// <summary>A knowledge draft's front-matter audience as a segment label; a value this release does not know is its own restriction.</summary>
    internal static string FromFrontMatter(string? value)
    {
        string label = (value ?? string.Empty).Trim().Trim('"', '\'').Trim();
        return label.ToLowerInvariant() switch
        {
            "" or "internal" => Internal,
            "members" or "members-only" or "members only" => Members,
            "public" => Public,
            _ => "restricted: " + label,
        };
    }

    /// <summary>Whether a passage of one audience may appear in a record every reader of the estate's records can read.</summary>
    internal static bool CoversRecordReaders(string audience) => audience is Internal or Public;

    /// <summary>The engine table suffix of a segment: fixed for the standard audiences, a digest for any other.</summary>
    internal static string Suffix(string audience) => audience switch
    {
        Internal => "internal",
        Members => "members",
        Public => "public",
        _ => "x" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(audience)))[..16],
    };

    /// <summary>Audiences in their fixed order: the standard ones first, then others ordinally.</summary>
    internal static IEnumerable<string> Ordered(IEnumerable<string> audiences) => audiences.Distinct(StringComparer.Ordinal)
        .OrderBy(a => Standard.Contains(a) ? Standard.ToList().IndexOf(a) : Standard.Count).ThenBy(a => a, StringComparer.Ordinal);
}

/// <summary>One ranked unit: a record, a document section or table row, or a knowledge entry, with its source's identity and extent.</summary>
internal sealed record SearchPassage(string Audience, string Kind, long Start, long Length, string? Type, string? Key, string Ident, string Title, string Body)
{
    /// <summary>A labelled field's text, kept for matched-field reports; never indexed separately.</summary>
    internal IReadOnlyList<(string Field, string Text)> Fields { get; init; } = [];
}

/// <summary>Splits the estate's records, pinned documents and knowledge drafts into passages, deterministically.</summary>
internal static class SearchPassages
{
    private static readonly string[] TitleFields = ["key", "title", "name", "summary"];
    private static readonly string[] KnowledgeTitle = ["title", "summary"];
    private static readonly HashSet<string> KnowledgeExcluded = new(StringComparer.Ordinal) { "sources", "drafted-by", "status", "supersedes", "audience", "release" };

    /// <summary>A record's passage: its type, id and key as identifiers, its title-like fields, and every other string field with its path.</summary>
    internal static SearchPassage Record(RecordEnvelope record)
    {
        var identParts = new List<string> { record.Id.ToLowerInvariant() };
        var identWhole = new List<string>();
        var titleParts = new List<string>();
        var bodyParts = new List<string>();
        var fields = new List<(string, string)>();
        SearchAnalyzer.Analyze(record.Type, bodyParts, identWhole);
        if (record.Subject is { } subject) identParts.Add(subject.Id.ToLowerInvariant());
        if (record.Supersedes is { } prior) identParts.Add(prior.ToLowerInvariant());
        foreach ((string path, string value) in Strings(record.Body, "body"))
        {
            fields.Add((path, value));
            bool title = path.Count(c => c == '.') == 1 && TitleFields.Contains(path["body.".Length..], StringComparer.Ordinal);
            SearchAnalyzer.Analyze(value, title ? titleParts : bodyParts, identWhole);
        }

        string? key = EstateModel.BodyString(record, "key");
        return new SearchPassage(SearchAudiences.Internal, "record", 0, 0, record.Type, key?.ToLowerInvariant(),
            Join(identParts.Concat(identWhole)), Join(titleParts), Join(bodyParts)) { Fields = fields };
    }

    /// <summary>A record's display title: its type with its key and title-like field.</summary>
    internal static string RecordTitle(RecordEnvelope record)
    {
        string? key = EstateModel.BodyString(record, "key");
        string? name = EstateModel.BodyString(record, "title") ?? EstateModel.BodyString(record, "name") ?? EstateModel.BodyString(record, "summary")
            ?? EstateModel.BodyString(record, "intent") ?? EstateModel.BodyString(record, "path");
        string title = record.Type + (key is null ? string.Empty : " " + key) + (name is null ? string.Empty : ": " + name);
        return Clip(title, 200);
    }

    /// <summary>Every string value in a JSON element with its dotted path; array items share their array's path.</summary>
    internal static IEnumerable<(string Path, string Value)> Strings(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return (path, element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    foreach ((string Path, string Value) item in Strings(property.Value, path + "." + property.Name)) yield return item;
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    foreach ((string Path, string Value) value in Strings(item, path)) yield return value;
                break;
        }
    }

    /// <summary>
    /// A pinned document's passages: each heading's section and each table body row on its own. A knowledge draft also gives an
    /// entry passage of its front matter, and every passage of it belongs to its front matter's audience.
    /// </summary>
    internal static IReadOnlyList<SearchPassage> Document(string path, string documentTitle, byte[] bytes)
    {
        bool knowledge = path.StartsWith("docs/knowledge/", StringComparison.Ordinal);
        List<Line> lines = Lines(bytes);
        var passages = new List<SearchPassage>();
        int first = 0;
        string audience = SearchAudiences.Internal;
        string? knowledgeKind = null;
        if (knowledge && FrontMatter(lines) is int end)
        {
            Dictionary<string, string> values = FrontMatterValues(lines, 1, end);
            audience = SearchAudiences.FromFrontMatter(values.GetValueOrDefault("audience"));
            knowledgeKind = values.TryGetValue("kind", out string? declared) ? Scalar(declared).ToLowerInvariant() : null;
            if (knowledgeKind is not null && !(knowledgeKind.Length <= 64 && knowledgeKind.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) knowledgeKind = null;
            var identWhole = new List<string> { path.ToLowerInvariant() };
            var titleParts = new List<string>();
            var bodyParts = new List<string>();
            var fields = new List<(string, string)>();
            foreach ((string name, string value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                if (KnowledgeExcluded.Contains(name)) continue;
                fields.Add(("front-matter." + name, value));
                SearchAnalyzer.Analyze(value, KnowledgeTitle.Contains(name, StringComparer.Ordinal) ? titleParts : bodyParts, identWhole);
            }

            long length = lines[end].End - lines[0].Start;
            passages.Add(new SearchPassage(audience, "knowledge", 0, length, knowledgeKind, null, Join(identWhole), Join(titleParts), Join(bodyParts)) { Fields = fields });
            first = end + 1;
        }

        string kind = knowledge ? "knowledge" : "document";
        var headings = new List<(int Level, string Text)>();
        int sectionStart = first;
        bool fence = false;
        char fenceChar = '\0';
        int fenceLength = 0;
        string[]? header = null;
        var sectionText = new List<string>();
        void Flush(int endLine)
        {
            if (endLine <= sectionStart && sectionText.Count == 0 && headings.Count == 0) return;
            long start = sectionStart < lines.Count ? lines[sectionStart].Start : bytes.LongLength;
            long stop = endLine < lines.Count ? lines[endLine].Start : bytes.LongLength;
            if (stop <= start) return;
            string heading = string.Join(" › ", headings.Select(h => h.Text));
            var identWhole = new List<string> { path.ToLowerInvariant() };
            var titleParts = new List<string>();
            var bodyParts = new List<string>();
            SearchAnalyzer.Analyze(documentTitle, titleParts, identWhole);
            SearchAnalyzer.Analyze(heading, titleParts, identWhole);
            string text = string.Join('\n', sectionText);
            SearchAnalyzer.Analyze(text, bodyParts, identWhole);
            passages.Add(new SearchPassage(audience, kind, start, stop - start, knowledgeKind, null, Join(identWhole), Join(titleParts), Join(bodyParts))
            { Fields = [("heading", heading), ("text", text)] });
        }

        for (int i = first; i < lines.Count; i++)
        {
            string text = lines[i].Text;
            string trimmed = text.TrimStart(' ');
            int indent = text.Length - trimmed.Length;
            if (indent <= 3 && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                char c = trimmed[0];
                int run = trimmed.TakeWhile(x => x == c).Count();
                if (!fence) { fence = true; fenceChar = c; fenceLength = run; }
                else if (c == fenceChar && run >= fenceLength && trimmed[run..].Trim().Length == 0) fence = false;
                sectionText.Add(text);
                header = null;
                continue;
            }

            if (!fence && indent <= 3 && Heading(trimmed) is (int level, string title))
            {
                Flush(i);
                sectionText.Clear();
                headings.RemoveAll(h => h.Level >= level);
                headings.Add((level, title));
                sectionStart = i;
                header = null;
                continue;
            }

            if (!fence && trimmed.StartsWith('|'))
            {
                if (header is null && i + 1 < lines.Count && Separator(lines[i + 1].Text))
                {
                    header = Cells(trimmed);
                    sectionText.Add(text);
                    i++; // The separator row carries no text.
                    continue;
                }

                if (header is not null)
                {
                    string[] cells = Cells(trimmed);
                    string heading = string.Join(" › ", headings.Select(h => h.Text));
                    var identWhole = new List<string> { path.ToLowerInvariant() };
                    var titleParts = new List<string>();
                    var bodyParts = new List<string>();
                    var fields = new List<(string, string)>();
                    SearchAnalyzer.Analyze(documentTitle, titleParts, identWhole);
                    SearchAnalyzer.Analyze(heading, titleParts, identWhole);
                    for (int c = 0; c < cells.Length; c++)
                    {
                        string label = c < header.Length && header[c].Length > 0 ? header[c] : "column " + (c + 1).ToString(CultureInfo.InvariantCulture);
                        fields.Add(("row." + Clip(label, 60), cells[c]));
                        SearchAnalyzer.Analyze(cells[c], bodyParts, identWhole);
                    }

                    passages.Add(new SearchPassage(audience, kind, lines[i].Start, lines[i].ContentEnd - lines[i].Start, knowledgeKind, null,
                        Join(identWhole), Join(titleParts), Join(bodyParts)) { Fields = fields });
                    continue;
                }
            }
            else header = null;

            sectionText.Add(text);
        }

        Flush(lines.Count);
        return passages;
    }

    /// <summary>A document passage's display title.</summary>
    internal static string DocumentTitle(string documentTitle, SearchPassage passage, string path)
    {
        string heading = passage.Fields.FirstOrDefault(f => f.Field == "heading").Text;
        if (passage.Kind == "knowledge" && passage.Fields.FirstOrDefault(f => f.Field == "front-matter.title").Text is { Length: > 0 } entry)
            return Clip(Scalar(entry), 200);
        string first = passage.Fields.FirstOrDefault(f => f.Field.StartsWith("row.", StringComparison.Ordinal)).Text ?? string.Empty;
        string title = (documentTitle.Length > 0 ? documentTitle : path) + (string.IsNullOrEmpty(heading) ? string.Empty : " › " + heading)
            + (passage.Fields.Any(f => f.Field.StartsWith("row.", StringComparison.Ordinal)) ? " › row " + first : string.Empty);
        return Clip(title, 200);
    }

    internal static string Clip(string value, int max)
    {
        if (value.Length <= max) return value;
        int end = max - 1;
        if (char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end] + "…";
    }

    private static string Join(IEnumerable<string> tokens) => string.Join(' ', tokens);

    private sealed record Line(long Start, long ContentEnd, long End, string Text);

    // Lines of the exact bytes, with their byte extents; each line is decoded on its own, so offsets stay exact for any bytes.
    private static List<Line> Lines(byte[] bytes)
    {
        var lines = new List<Line>();
        int start = 0;
        var decoder = new UTF8Encoding(false, false);
        while (start < bytes.Length)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', start);
            int end = newline < 0 ? bytes.Length : newline + 1;
            int content = newline < 0 ? bytes.Length : newline;
            if (content > start && bytes[content - 1] == '\r') content--;
            lines.Add(new Line(start, content, end, decoder.GetString(bytes, start, content - start)));
            start = end;
        }

        return lines;
    }

    private static int? FrontMatter(List<Line> lines)
    {
        if (lines.Count < 2 || lines[0].Text != "---") return null;
        for (int i = 1; i < lines.Count; i++)
            if (lines[i].Text is "---" or "...") return i;
        return null;
    }

    // The front matter's top-level keys, each with its value and any indented continuation, as text; enough for analysis.
    private static Dictionary<string, string> FrontMatterValues(List<Line> lines, int from, int to)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var text = new StringBuilder();
        void Keep()
        {
            if (current is not null && !values.ContainsKey(current)) values[current] = text.ToString().Trim();
            text.Clear();
        }

        for (int i = from; i < to; i++)
        {
            string line = lines[i].Text;
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (line.Length > 0 && char.IsAsciiLetter(line[0]) && colon > 0 && line[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                Keep();
                current = line[..colon];
                string rest = line[(colon + 1)..].Trim();
                if (rest is not (">-" or ">" or "|" or "|-")) text.Append(rest);
            }
            else if (current is not null)
            {
                text.Append('\n').Append(line);
            }
        }

        Keep();
        return values;
    }

    private static string Scalar(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && (trimmed[0] == '"' && trimmed[^1] == '"' || trimmed[0] == '\'' && trimmed[^1] == '\'') ? trimmed[1..^1] : trimmed;
    }

    private static (int Level, string Text)? Heading(string trimmed)
    {
        int level = trimmed.TakeWhile(c => c == '#').Count();
        if (level is < 1 or > 6 || (trimmed.Length > level && trimmed[level] is not (' ' or '\t'))) return null;
        string text = trimmed[level..].Trim();
        text = text.TrimEnd('#').TrimEnd();
        return (level, text);
    }

    private static bool Separator(string line)
    {
        string trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.Contains('-', StringComparison.Ordinal) && trimmed.All(c => c is '|' or '-' or ':' or ' ' or '\t');
    }

    private static string[] Cells(string row)
    {
        string trimmed = row.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal)) trimmed = trimmed[..^1];
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|') { cell.Append('|'); i++; continue; }
            if (trimmed[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); continue; }
            cell.Append(trimmed[i]);
        }

        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }
}
