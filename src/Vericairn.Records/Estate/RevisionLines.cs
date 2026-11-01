// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Revision history rendered from pins; only its payload is excluded from authored-content pins.</summary>
public static partial class RevisionLines
{
    /// <summary>The additive format-3 hash mode. An absent mode still means whole-file bytes.</summary>
    public const string HashMode = "authored-revisions-v1";

    /// <summary>The generated region immediately after the level-one title.</summary>
    public const string Region = "revisions";

    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Whether a pin uses the authored-content mode.</summary>
    public static bool IsAuthored(RecordEnvelope pin) => pin is { Type: "document-revision", Format: >= 3 }
        && EstateModel.BodyString(pin, "hashMode") == HashMode;

    /// <summary>All revisions of this path in recorded-time order, with identity breaking ties.</summary>
    public static IReadOnlyList<RecordEnvelope> History(EstateModel model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == path)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The pin's one-based position in the complete history, independent of its advisory revision field.</summary>
    public static long Ordinal(EstateModel model, RecordEnvelope pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        return History(model, EstateModel.BodyString(pin, "path")!).Select((r, i) => (r.Id, Number: (long)i + 1))
            .Single(r => r.Id == pin.Id).Number;
    }

    /// <summary>The exact bytes a pin hashes. Invalid generated markers never fall back to whole-file hashing.</summary>
    public static byte[] PinnedContent(byte[] bytes, RecordEnvelope pin) => IsAuthored(pin) ? AuthoredContent(bytes) : bytes;

    /// <summary>Preserves all bytes outside the payload, including both marker lines, BOM and line endings.</summary>
    public static byte[] AuthoredContent(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string text = Utf8.GetString(bytes);
        (int from, int to) = Bounds(text);
        return Utf8.GetBytes(string.Concat(text.AsSpan(0, from), text.AsSpan(to)));
    }

    /// <summary>Produces every line, newest first. Conversion metadata retains legacy wording bound to exact old records.</summary>
    public static string Render(EstateModel model, string path)
    {
        IReadOnlyList<RecordEnvelope> history = History(model, path);
        var preserved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in history)
        {
            if (!record.Body.TryGetProperty("revisionHistory", out JsonElement entries)) continue;
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                string id = entry.GetProperty("id").GetString()!;
                string line = entry.GetProperty("line").GetString()!;
                if (preserved.TryGetValue(id, out string? prior) && prior != line)
                    throw new InvalidOperationException("Conversion records disagree about a historical revision line.");
                preserved[id] = line;
            }
        }
        return GeneratedRegions.Normalize(string.Join("\n\n", history.Select((record, index) =>
        {
            string number = (index + 1).ToString(CultureInfo.InvariantCulture);
            if (preserved.TryGetValue(record.Id, out string? original))
            {
                Match line = LegacyLine().Match(original);
                if (!line.Success) throw new InvalidOperationException("A preserved revision line has an invalid shape.");
                Group ordinal = line.Groups["number"];
                return original[..ordinal.Index] + number + original[(ordinal.Index + ordinal.Length)..];
            }
            string decision = EstateModel.BodyString(record, "decision") is { } id ? "carries decision `" + id + "`: " : "";
            return "Revision " + number + ", " + record.AtTime.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
                + ": " + decision + System.Net.WebUtility.HtmlEncode(EstateModel.BodyString(record, "summary")!.ReplaceLineEndings(" "));
        }).Reverse()));
    }

    /// <summary>Refreshes the generated payload without changing the authored bytes.</summary>
    public static byte[] Refresh(EstateModel model, string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string text = Utf8.GetString(bytes);
        (int from, int to) = Bounds(text);
        return Utf8.GetBytes(string.Concat(text.AsSpan(0, from), Render(model, path), text.AsSpan(to)));
    }

    /// <summary>Converts the opening hand-written history, refusing any line without one unambiguous record.</summary>
    public static (byte[] Content, JsonArray History) Convert(EstateModel model, string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string text = Utf8.GetString(bytes);
        var lines = Lines(text);
        var visible = WritingScanner.LinesOutsideFences(text).Select(l => l.Line - 1).ToHashSet();
        int title = Title(lines, visible);
        if (visible.Any(i => lines[i].Text == GeneratedRegions.Start(Region) || lines[i].Text == GeneratedRegions.End(Region)))
            throw new InvalidOperationException("The document already has revision markers; refresh its existing region instead.");
        int first = title + 1;
        while (first < lines.Count && string.IsNullOrWhiteSpace(lines[first].Text)) first++;
        int cursor = first;
        var entries = new JsonArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<RecordEnvelope> pins = History(model, path);
        while (cursor < lines.Count && visible.Contains(cursor) && IsLegacyStart(lines[cursor].Text))
        {
            int start = cursor++;
            while (cursor < lines.Count && visible.Contains(cursor) && !string.IsNullOrWhiteSpace(lines[cursor].Text)
                && !IsLegacyStart(lines[cursor].Text) && !lines[cursor].Text.TrimStart(' ').StartsWith('#')) cursor++;
            int end = cursor < lines.Count ? lines[cursor].Start : text.Length;
            string line = text[lines[start].Start..end].TrimEnd('\r', '\n');
            Match match = LegacyLine().Match(line);
            if (!match.Success || !long.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out long number))
                throw new InvalidOperationException("A hand-written revision line cannot be matched to a record.");
            RecordEnvelope[] matches = pins.Where(p => p.Body.GetProperty("revision").GetInt64() == number).ToArray();
            if (matches.Length != 1 || !used.Add(matches[0].Id))
                throw new InvalidOperationException("Every hand-written revision line needs one distinct existing record; a line has no record or is ambiguous.");
            entries.Add(new JsonObject { ["id"] = matches[0].Id, ["hash"] = matches[0].ComputedHash, ["line"] = line });
            while (cursor < lines.Count && string.IsNullOrWhiteSpace(lines[cursor].Text)) cursor++;
        }
        if (visible.Any(i => i >= cursor && IsLegacyStart(lines[i].Text)))
            throw new InvalidOperationException("All hand-written revision lines must be in the opening history before conversion.");
        int from = title + 1 < lines.Count ? lines[title + 1].Start : text.Length;
        int to = cursor < lines.Count ? lines[cursor].Start : text.Length;
        string newline = lines[title].Text.Length + lines[title].Start < text.Length && text[lines[title].Start + lines[title].Text.Length] == '\r' ? "\r\n" : "\n";
        string prefix = text[..from];
        if (!prefix.EndsWith('\n')) prefix += newline;
        string converted = prefix + newline + GeneratedRegions.Start(Region) + newline + GeneratedRegions.End(Region) + newline + newline + text[to..];
        return (Utf8.GetBytes(converted), entries);
    }

    /// <summary>The policy's first prose line, admitting only the correctly placed revision marker as an intervening line.</summary>
    public static string? FirstProseLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] prose = WritingScanner.LinesOutsideFences(text).Select(l => l.Text.TrimStart('\uFEFF'))
            .Where(l => !l.StartsWith("    ", StringComparison.Ordinal) && !l.StartsWith('\t'))
            .SkipWhile(l => !l.TrimStart(' ').StartsWith("# ", StringComparison.Ordinal)).Skip(1)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (prose.FirstOrDefault() != GeneratedRegions.Start(Region)) return prose.FirstOrDefault();
        try { _ = Bounds(text); }
        catch (InvalidOperationException) { return null; }
        return prose.Skip(1).FirstOrDefault();
    }

    internal static bool ValidLegacyLine(string line, long revision) => LegacyLine().Match(line) is { Success: true } match
        && match.Groups["number"].Value == revision.ToString(CultureInfo.InvariantCulture);

    private static bool IsLegacyStart(string line) => !line.StartsWith("    ", StringComparison.Ordinal)
        && line.TrimStart(' ').StartsWith("Revision ", StringComparison.Ordinal);

    private static (int From, int To) Bounds(string text)
    {
        var lines = Lines(text);
        var visible = WritingScanner.LinesOutsideFences(text).Select(l => l.Line - 1).ToHashSet();
        int title = Title(lines, visible);
        int[] starts = visible.Where(i => lines[i].Text == GeneratedRegions.Start(Region)).ToArray();
        int[] ends = visible.Where(i => lines[i].Text == GeneratedRegions.End(Region)).ToArray();
        if (starts.Length != 1 || ends.Length != 1 || starts[0] <= title || ends[0] <= starts[0]
            || lines.Skip(title + 1).Take(starts[0] - title - 1).Any(l => !string.IsNullOrWhiteSpace(l.Text))
            || visible.Any(i => i > starts[0] && i < ends[0] && (lines[i].Text.StartsWith("<!-- vericairn:generated ", StringComparison.Ordinal)
                || lines[i].Text.StartsWith("<!-- vericairn:end ", StringComparison.Ordinal))))
            throw new InvalidOperationException("Place one balanced, unnested revisions region immediately after the document title.");
        return (lines[starts[0] + 1].Start, lines[ends[0]].Start);
    }

    private static int Title(List<(int Start, string Text)> lines, HashSet<int> visible)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!visible.Contains(i) || string.IsNullOrWhiteSpace(lines[i].Text)) continue;
            string heading = lines[i].Text.TrimStart('\uFEFF');
            if (!heading.StartsWith("    ", StringComparison.Ordinal) && !heading.StartsWith('\t') && heading.TrimStart(' ').StartsWith("# ", StringComparison.Ordinal)) return i;
            break;
        }
        throw new InvalidOperationException("A generated revision history requires an opening level-one title.");
    }

    private static List<(int Start, string Text)> Lines(string text)
    {
        var lines = new List<(int, string)>();
        int offset = 0;
        foreach (string line in text.Split('\n')) { lines.Add((offset, line.TrimEnd('\r'))); offset += line.Length + 1; }
        return lines;
    }

    [GeneratedRegex(@"\A {0,3}Revision (?<number>[1-9][0-9]*), (?:January|February|March|April|May|June|July|August|September|October|November|December) [0-9]{1,2}, [0-9]{4}\D[^\0]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyLine();
}
