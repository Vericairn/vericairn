// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Hashing;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Templates;

/// <summary>The exact UTF-8 payload of one located section or field, without its heading or section markers.</summary>
public sealed record TemplateSectionContent(string Id, string Content, int Start, int Length)
{
    /// <summary>The identity of the payload, preserving whitespace and line endings.</summary>
    public string Hash => ContentHash.Sha256(Encoding.UTF8.GetBytes(Content));
    /// <summary>Whether only whitespace and scaffold purpose comments remain.</summary>
    public bool Empty => string.IsNullOrWhiteSpace(Regex.Replace(Content, @"<!-- vericairn:purpose .*? -->", "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
}

/// <summary>Finds sections by ATX headings or balanced explicit markers. Code fences never introduce a section.</summary>
public static partial class TemplateSections
{
    /// <summary>Extracts exact payloads, refusing duplicate, crossing or ambiguous locators.</summary>
    public static IReadOnlyDictionary<string, TemplateSectionContent> Read(string text, IReadOnlyList<JsonElement> elements)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(elements);
        var headings = new List<(string Title, int Level, int Start, int End)>();
        var markers = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        var stack = new Stack<(string Id, int Start)>();
        var starts = new HashSet<string>(StringComparer.Ordinal);
        char fence = '\0';
        int fenceLength = 0;
        for (int offset = 0; offset < text.Length;)
        {
            int newline = text.IndexOf('\n', offset);
            int end = newline < 0 ? text.Length : newline + 1;
            string line = text[offset..(newline < 0 ? text.Length : newline)].TrimEnd('\r');
            Match boundary = Fence().Match(line);
            if (boundary.Success)
            {
                string run = boundary.Groups[1].Value;
                if (fence == '\0') { fence = run[0]; fenceLength = run.Length; }
                else if (run[0] == fence && run.Length >= fenceLength && string.IsNullOrWhiteSpace(line[(boundary.Index + boundary.Length)..])) fence = '\0';
                offset = end;
                continue;
            }
            if (fence == '\0')
            {
                Match heading = Heading().Match(line.TrimStart('\uFEFF'));
                if (heading.Success) headings.Add((ClosingHashes().Replace(heading.Groups[2].Value, "").TrimEnd(), heading.Groups[1].Length, offset, end));
                foreach (Match marker in Marker().Matches(line))
                {
                    string id = marker.Groups[2].Value;
                    if (marker.Groups[1].Value.Length == 0)
                    {
                        if (!starts.Add(id)) throw new InvalidOperationException("A section marker occurs more than once: " + id);
                        stack.Push((id, offset + marker.Index + marker.Length));
                    }
                    else
                    {
                        if (stack.Count == 0 || stack.Peek().Id != id) throw new InvalidOperationException("Section markers cross or close without an opening: " + id);
                        markers.Add(id, (stack.Pop().Start, offset + marker.Index));
                    }
                }
            }
            offset = end;
        }
        if (stack.Count != 0) throw new InvalidOperationException("A section marker has no closing marker.");
        var result = new Dictionary<string, TemplateSectionContent>(StringComparer.Ordinal);
        foreach (JsonElement element in elements)
        {
            string id = TJson.Text(element, "id")!;
            if (TJson.Text(TJson.Object(element, "synthesis"), "mode") == "generated")
            {
                if (GeneratedRegions.Find(text, id) is { } region)
                    result.Add(id, new(id, text[region.From..region.To], region.From, region.To - region.From));
                else if (!GeneratedRegions.ValidMarkers(text) || text.Contains(GeneratedRegions.Start(id), StringComparison.Ordinal)
                    || text.Contains(GeneratedRegions.End(id), StringComparison.Ordinal))
                    throw new InvalidOperationException("A generated section has invalid or incomplete generated-region markers.");
            }
            else if (TJson.Flag(element, "marker"))
            {
                if (markers.TryGetValue(id, out var range)) result.Add(id, new(id, text[range.Start..range.End], range.Start, range.End - range.Start));
            }
            else
            {
                string title = TJson.Text(element, "heading")!;
                var matches = headings.Where(h => h.Title == title).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("A section heading occurs more than once: " + title);
                if (matches.Length == 0) continue;
                var match = matches[0];
                int end = headings.Where(h => h.Start > match.Start && h.Level <= match.Level).Select(h => h.Start).DefaultIfEmpty(text.Length).First();
                result.Add(id, new(id, text[match.End..end], match.End, end - match.End));
            }
        }
        foreach (JsonElement element in elements)
        {
            if (!result.TryGetValue(TJson.Text(element, "id")!, out TemplateSectionContent? parent)) continue;
            foreach (JsonElement child in TJson.Array(element, "sections").Concat(TJson.Array(element, "fields")))
                if (result.TryGetValue(TJson.Text(child, "id")!, out TemplateSectionContent? part)
                    && (part.Start < parent.Start || part.Start + part.Length > parent.Start + parent.Length))
                    throw new InvalidOperationException("A nested section or field lies outside its parent.");
        }
        return result;
    }

    /// <summary>Lays out an empty instance with its purposes. Generated and quoted payloads are filled by the runtime.</summary>
    public static string Skeleton(TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var text = new StringBuilder();
        void Write(JsonElement container, int depth)
        {
            foreach (JsonElement element in TJson.Array(container, "sections").Concat(TJson.Array(container, "fields")))
            {
                string id = TJson.Text(element, "id")!;
                bool marker = TJson.Flag(element, "marker");
                bool generated = TJson.Text(TJson.Object(element, "synthesis"), "mode") == "generated";
                if (marker) text.Append("<!-- vericairn:section ").Append(id).Append(" -->");
                else text.Append(new string('#', Math.Min(6, depth))).Append(' ').Append(TJson.Text(element, "heading")).Append('\n');
                if (generated) text.Append('\n').Append(GeneratedRegions.Start(id)).Append('\n');
                text.Append("<!-- vericairn:purpose ").Append(TJson.Text(element, "purpose")!.Replace("-->", "", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ')).Append(" -->\n");
                if (generated) text.Append(GeneratedRegions.End(id)).Append('\n');
                else Write(element, depth + 1);
                if (marker) text.Append("<!-- vericairn:section-end ").Append(id).Append(" -->\n");
            }
        }
        Write(definition.Content, 2);
        return text.ToString();
    }

    [GeneratedRegex(@"^ {0,3}(#{1,6})[ \t]+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();
    [GeneratedRegex(@"[ \t]+#+[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ClosingHashes();
    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();
    [GeneratedRegex(@"<!-- vericairn:section(-end)? ([a-z0-9]+(?:-[a-z0-9]+)*) -->", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
