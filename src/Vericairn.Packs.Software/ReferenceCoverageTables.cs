// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using Vericairn.Runtime.Policies;

namespace Vericairn.Packs.Software;

internal sealed record ReferenceMapping(ReferenceItem Item, string Term);
internal sealed record RegisteredTerm(string Name, string Meaning, string Owner, string Status);
internal sealed record ReferenceCoverageTables(ImmutableArray<ReferenceMapping> Mappings, ImmutableArray<RegisteredTerm> Terms)
{
    private static readonly string[] CoverageSchema = ["Category", "Reference", "Term"];
    private static readonly string[] TermSchema = ["Term", "Meaning", "Not to be confused with", "Owning document and section", "Status"];

    internal static ReferenceCoverageTables Read(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 16 * 1024 * 1024) throw Bad("The register byte bound was exhausted.");
        string[] lines = ReferenceMarkdown.Lines(text).ToArray();
        var mappings = ImmutableArray.CreateBuilder<ReferenceMapping>();
        var terms = ImmutableArray.CreateBuilder<RegisteredTerm>();
        int coverageHeading = 0, coverageTables = 0, sectionLevel = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int level = line.TakeWhile(c => c == '#').Count();
            if (level is >= 1 and <= 6 && line.Length > level && line[level] == ' ')
            {
                if (sectionLevel > 0 && level <= sectionLevel) sectionLevel = 0;
                if (line[(level + 1)..].Trim() == "Generated reference coverage") { coverageHeading++; sectionLevel = level; }
                continue;
            }
            if (!line.Contains('|', StringComparison.Ordinal)) continue;
            string firstCell = line.TrimStart('|', ' ').Split('|')[0].Trim().Trim('`').Trim();
            if (firstCell is not ("Category" or "Term")) continue;
            // Inspect only the declared table schemas. Incidental tables are not terminology.
            string[] cells = Cells(line);
            bool coverage = cells.FirstOrDefault() == "Category";
            bool terminology = cells.FirstOrDefault() == "Term";
            if (!coverage && !terminology) continue;
            string[] schema = coverage ? CoverageSchema : TermSchema;
            if (!cells.SequenceEqual(schema, StringComparer.Ordinal) || i + 1 >= lines.Length) throw Bad("Malformed selected table header.");
            if (coverage && (sectionLevel == 0 || ++coverageTables != 1)) throw Bad("The coverage table must appear once in its named section.");
            string[] delimiter = Cells(lines[++i]);
            if (delimiter.Length != schema.Length || delimiter.Any(c => !Delimiter(c))) throw Bad("Malformed selected table delimiter.");
            while (i + 1 < lines.Length && lines[i + 1].Contains('|', StringComparison.Ordinal))
            {
                cells = Cells(lines[++i]);
                if (cells.Length != schema.Length) throw Bad("A selected table row has the wrong cell count.");
                if (coverage)
                {
                    if (!ReferenceInventory.ReferencePaths.ContainsKey(cells[0]) || string.IsNullOrWhiteSpace(cells[1])) throw Bad("A coverage row needs an exact category and reference.");
                    mappings.Add(new(new(cells[0], cells[1]), cells[2]));
                }
                else terms.Add(new(cells[0], cells[1], cells[3], cells[4]));
                if (mappings.Count + terms.Count > ReferenceInventory.MaximumItems) throw Bad("The register row bound was exhausted.");
            }
        }
        if (coverageHeading != 1 || coverageTables != 1) throw Bad("The Generated reference coverage section must contain exactly one declared table.");
        if (mappings.GroupBy(m => m).Any(g => g.Count() > 1)) throw Bad("A coverage row is duplicated.");
        return new(mappings.ToImmutable(), terms.ToImmutable());
    }

    internal static bool HasOwner(RegisteredTerm term)
    {
        int extension = term.Owner.IndexOf(".md", StringComparison.Ordinal);
        if (extension < 0) return false;
        string path = term.Owner[..(extension + 3)];
        string section = term.Owner[(extension + 3)..].Trim();
        if (section.StartsWith("(product)", StringComparison.Ordinal)) section = section[9..].TrimStart();
        return PolicyInputs.Path(path) && !path.Contains('*', StringComparison.Ordinal) && !path.Contains('?', StringComparison.Ordinal) &&
            section.Length > 1 && section[0] is ',' or '§' or '#' && !string.IsNullOrWhiteSpace(section[1..]);
    }

    private static string[] Cells(string line)
    {
        line = line.Trim();
        var cells = new List<string>();
        var cell = new StringBuilder();
        int code = 0, lastSeparator = -1;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && (code == 0 || line[i + 1] == '|'))
            {
                char escaped = line[i + 1];
                if (Punctuation(escaped)) { cell.Append(c).Append(escaped); i++; continue; }
            }
            if (c == '`')
            {
                int run = 1;
                while (i + run < line.Length && line[i + run] == '`') run++;
                if (code == 0) code = run;
                else if (code == run) code = 0;
                cell.Append('`', run);
                i += run - 1;
                continue;
            }
            if (c == '|' && code == 0) { cells.Add(Cell(cell.ToString())); cell.Clear(); lastSeparator = i; }
            else cell.Append(c);
        }
        if (code != 0) throw Bad("A selected table contains an unclosed code span.");
        cells.Add(Cell(cell.ToString()));
        if (line.StartsWith('|')) cells.RemoveAt(0);
        if (lastSeparator == line.Length - 1) cells.RemoveAt(cells.Count - 1);
        return cells.ToArray();
    }

    private static string Cell(string text)
    {
        text = text.Trim();
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && Punctuation(text[i + 1])) { result.Append(text[++i]); continue; }
            if (text[i] != '`') { result.Append(text[i]); continue; }
            int run = 1;
            while (i + run < text.Length && text[i + run] == '`') run++;
            int end = i + run;
            while (end < text.Length)
            {
                if (text[end] != '`') { end++; continue; }
                int count = 1;
                while (end + count < text.Length && text[end + count] == '`') count++;
                if (count == run) break;
                end += count;
            }
            if (end == text.Length) throw Bad("Unclosed code span.");
            string content = text[(i + run)..end].Replace("\\|", "|", StringComparison.Ordinal);
            if (content.StartsWith(' ') && content.EndsWith(' ') && content.Any(c => c != ' ')) content = content[1..^1];
            result.Append(content);
            i = end + run - 1;
        }
        return result.ToString();
    }

    private static bool Punctuation(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    private static bool Delimiter(string text)
    {
        if (text.StartsWith(':')) text = text[1..];
        if (text.EndsWith(':')) text = text[..^1];
        return text.Length >= 3 && text.All(c => c == '-');
    }

    private static PolicyInputException Bad(string message) => new("Reference coverage table: " + message);
}
