// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Guides;

/// <summary>Reads owning tables by column contracts; names elsewhere in a catalog never become entries.</summary>
public static partial class GuideMarkdown
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] NameColumns = ["Identifier", "Foundry", "Provider", "Capability", "Area"];

    /// <summary>Parses actual catalog column variants, including planned additions and grouped Providers.</summary>
    public static GuideInventory Read(string source, ReadOnlySpan<byte> bytes)
    {
        var rows = new List<GuideCatalogRow>();
        var findings = new List<GuideFinding>();
        if (bytes.Length > GuideGenerator.MaximumSourceBytes)
            return new(rows, [new(source, "input-limit", "The catalog source exceeds the byte bound.")]);
        string text;
        try { text = Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { return new(rows, [new(source, "invalid-utf8", "The source must be valid UTF-8.")]); }
        string[] lines = Lines(text);
        var headings = new SortedDictionary<int, string>();
        string? fence = null;
        var tableLocations = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (Fence().IsMatch(line))
            {
                string marker = Fence().Match(line).Value;
                if (fence is null) fence = marker;
                else if (marker[0] == fence[0] && marker.Length >= fence.Length && line[marker.Length..].Trim().Length == 0) fence = null;
                continue;
            }
            if (fence is not null) continue;
            Match heading = Heading().Match(line);
            if (heading.Success)
            {
                int level = heading.Groups[1].Length;
                foreach (int old in headings.Keys.Where(k => k >= level).ToArray()) headings.Remove(old);
                headings[level] = heading.Groups[2].Value.TrimEnd();
                continue;
            }
            if (!line.StartsWith('|') || i + 1 >= lines.Length) continue;
            string[] columns = Cells(line);
            string? nameColumn = NameColumn(columns);
            if (nameColumn is null) continue;
            string[] separator = Cells(lines[i + 1].Trim());
            string trail = string.Join(" / ", headings.Values);
            if (separator.Length != columns.Length || !separator.All(c => Separator().IsMatch(c)))
            {
                if (separator.Any(c => c.Length > 0 && c.All(ch => ch is ':' or '-' or ' ' or '\t')))
                    findings.Add(new(source, "malformed-table", "The owning table under " + trail + " at line " +
                        (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " has an invalid delimiter at line " +
                        (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + "; cell count and separators must match the header."));
                continue;
            }
            if (!tableLocations.Add(trail)) findings.Add(new(source, "ambiguous-table", "More than one owning table has heading: " + trail));
            if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
                findings.Add(new(source, "duplicate-column", "Duplicate columns in: " + trail));
            int nameIndex = Array.IndexOf(columns, nameColumn);
            i += 2;
            for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
            {
                string[] cells = Cells(lines[i].Trim());
                if (cells.Length != columns.Length)
                {
                    findings.Add(new(source, "malformed-row", "Cell count differs from the owning table at line " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    continue;
                }
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (int c = 0; c < columns.Length; c++) values[columns[c]] = cells[c];
                rows.Add(new(source, trail, DisplayName(cells[nameIndex]), nameColumn, values, i + 1,
                    GuideGenerator.Hash(Utf8.GetBytes(lines[i]))));
            }
            i--;
        }
        return new(rows, findings);
    }

    internal static string[] Lines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { lines.Add(text[start..(i + 1)]); start = i + 1; }
        if (start < text.Length) lines.Add(text[start..]);
        return [.. lines];
    }

    // A pipe escaped with a backslash or inside a code span belongs to that cell.
    internal static string[] Cells(string line)
    {
        var cells = new List<string>();
        int start = line.StartsWith('|') ? 1 : 0;
        int codeTicks = 0;
        for (int i = start; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length) { i++; continue; }
            if (line[i] == '`')
            {
                int run = 1;
                while (i + run < line.Length && line[i + run] == '`') run++;
                if (codeTicks == 0) codeTicks = run;
                else if (codeTicks == run) codeTicks = 0;
                i += run - 1;
            }
            else if (line[i] == '|' && codeTicks == 0) { cells.Add(line[start..i].Trim()); start = i + 1; }
        }
        if (start < line.Length) cells.Add(line[start..].Trim());
        return [.. cells];
    }

    private static string? NameColumn(string[] columns)
    {
        // Entry/Assembly/Status tables report implementation observations, not owning scope.
        if (columns.Contains("Entry", StringComparer.Ordinal) && columns.Contains("Status", StringComparer.Ordinal) &&
            columns.Contains("Area", StringComparer.Ordinal) && columns.Contains("Where", StringComparer.Ordinal)) return "Entry";
        if (!columns.Contains("Caveat", StringComparer.Ordinal) && !columns.Contains("Boundary", StringComparer.Ordinal)) return null;
        return NameColumns.FirstOrDefault(c => columns.Contains(c, StringComparer.Ordinal));
    }

    internal static string DisplayName(string cell)
    {
        if (cell.StartsWith("**", StringComparison.Ordinal) && cell.EndsWith("**", StringComparison.Ordinal)) return cell[2..^2];
        if (cell.Length >= 2 && cell.StartsWith('`') && cell.EndsWith('`')) return cell[1..^1];
        return cell;
    }

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)(?:[ \t]+#+[ \t]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();
    [GeneratedRegex(@"^:?-{3,}:?$", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();
    [GeneratedRegex(@"^(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();
}
