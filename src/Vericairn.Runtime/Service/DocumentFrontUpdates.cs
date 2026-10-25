// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

internal static partial class DocumentFrontUpdates
{
    private const string Version = @"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?";

    internal static (DocumentPassUpdate? Update, string? Note) Prepare(EstateModel model, LivingDocuments.State state, RecordEnvelope release)
    {
        if (state.Path is not ("README.md" or "ROADMAP.md" or "docs/releases/README.md")) return (null, null);
        try
        {
            byte[]? bytes = state.Repository is null
                ? File.ReadAllBytes(GeneratedDocumentVerification.SafeFile(model.Root, state.Path))
                : state.ComparisonCommit is { } commit
                    ? DocumentSources.Find(EstateModel.BodyString(state.Declaration, "source"))?.Read(model, state.Declaration, commit) : null;
            if (bytes is null || bytes.Length > 1048576) return (null, "The front document is unavailable or exceeds the patch size limit.");
            string before = new UTF8Encoding(false, true).GetString(bytes);
            string version = EstateModel.BodyString(release, "version")!;
            string newline = before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            DateTimeOffset published = DateTimeOffset.Parse(EstateModel.BodyString(release, "publishedAt") ?? release.At, CultureInfo.InvariantCulture);
            string date = published.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            string? after = state.Path switch
            {
                "README.md" => Readme(before, version, date, newline),
                "ROADMAP.md" => Roadmap(before, version, newline),
                _ => Index(before, version, date, release, newline),
            };
            if (after is null) return (null, "The front document has no unique supported current-release marker; prepare its update by hand.");
            if (after == before) return (null, null);
            return (new(state.Declaration.Id, state.Path, state.Repository, state.ComparisonCommit, ContentHash.Sha256(bytes),
                ContentHash.Sha256(Encoding.UTF8.GetBytes(after)), Patch(state.Path, before, after)), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException or FormatException)
        {
            return (null, "The front document could not be read as a bounded UTF-8 document; no patch was prepared.");
        }
    }

    private static string? Readme(string text, string version, string date, string newline)
    {
        MatchCollection matches = ReadmeMarker().Matches(text);
        MatchCollection shortMatches = ShortReadmeMarker().Matches(text);
        if (matches.Count + shortMatches.Count != 1) return null;
        if (matches.Count == 1)
            return ReadmeMarker().Replace(text, _ => "The current published release is " + version + ", from" + newline + date + ".");
        return ShortReadmeMarker().Replace(text, m => m.Groups["label"].Value + version);
    }

    private static string? Roadmap(string text, string version, string newline)
    {
        MatchCollection current = RoadmapCurrent().Matches(text);
        if (current.Count != 1) return null;
        int headings = RoadmapVersion().Matches(text).Count(m => m.Groups["version"].Value == version);
        if (headings > 1) return null;
        if (current[0].Groups["version"].Value == version) return text;
        Match[] planned = [.. RoadmapPlanned().Matches(text).Where(m => m.Groups["version"].Value == version)];
        if (headings != planned.Length) return null;
        string result = RoadmapCurrent().Replace(text, m => "## " + m.Groups["version"].Value + " — previous published release");
        if (planned.Length == 1)
            return RoadmapPlanned().Replace(result, m => m.Groups["version"].Value == version ? "## " + version + " — current published release" : m.Value);
        string section = "## " + version + " — current published release" + newline + newline
            + "See the [release notes](docs/releases/" + version + "/release-notes.md) and [documentation set](docs/releases/" + version + "/README.md)." + newline + newline;
        return result.Insert(current[0].Index, section);
    }

    private static string? Index(string text, string version, string date, RecordEnvelope release, string newline)
    {
        MatchCollection markers = IndexMarker().Matches(text);
        if (markers.Count != 1) return null;
        if (markers[0].Groups["version"].Value == version) return text;
        string summary = "The current published release is **" + version + "**, published on " + date
            + ", at commit `" + EstateModel.BodyString(release, "commit") + "` (estate release record `" + release.Id + "`)." + newline
            + "[Documentation set](" + version + "/README.md) · [Release notes](" + version + "/release-notes.md)." + newline + newline;
        string historical = IndexMarker().Replace(text, m => "Previously published release: **" + m.Groups["version"].Value + "**");
        return historical.Insert(markers[0].Index, summary);
    }

    // A single contextual hunk preserves every untouched byte, including CRLF and final-newline state.
    private static string Patch(string path, string before, string after)
    {
        string[] oldLines = Lines(before), newLines = Lines(after);
        int prefix = 0;
        while (prefix < Math.Min(oldLines.Length, newLines.Length) && oldLines[prefix] == newLines[prefix]) prefix++;
        int suffix = 0;
        while (suffix < Math.Min(oldLines.Length, newLines.Length) - prefix && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;
        int start = Math.Max(0, prefix - 3), endOld = Math.Min(oldLines.Length, oldLines.Length - suffix + 3), endNew = Math.Min(newLines.Length, newLines.Length - suffix + 3);
        var patch = new StringBuilder().Append("--- a/").Append(path).Append('\n').Append("+++ b/").Append(path).Append('\n')
            .Append("@@ -").Append((start + 1).ToString(CultureInfo.InvariantCulture)).Append(',').Append((endOld - start).ToString(CultureInfo.InvariantCulture))
            .Append(" +").Append((start + 1).ToString(CultureInfo.InvariantCulture)).Append(',').Append((endNew - start).ToString(CultureInfo.InvariantCulture)).Append(" @@\n");
        for (int i = start; i < prefix; i++) Line(' ', oldLines[i]);
        for (int i = prefix; i < oldLines.Length - suffix; i++) Line('-', oldLines[i]);
        for (int i = prefix; i < newLines.Length - suffix; i++) Line('+', newLines[i]);
        for (int i = oldLines.Length - suffix; i < endOld; i++) Line(' ', oldLines[i]);
        return patch.ToString();

        void Line(char marker, string line)
        {
            patch.Append(marker).Append(line);
            if (!line.EndsWith('\n')) patch.Append("\n\\ No newline at end of file\n");
        }
        static string[] Lines(string value)
        {
            string[] lines = value.Split('\n');
            return lines.Select((line, i) => i < lines.Length - 1 ? line + "\n" : line).Where(line => line.Length > 0).ToArray();
        }
    }

    [GeneratedRegex("The current published release is (?<version>" + Version + @"), from\r?\n(?:January|February|March|April|May|June|July|August|September|October|November|December) [0-9]{1,2}, [0-9]{4}\.", RegexOptions.CultureInvariant)]
    private static partial Regex ReadmeMarker();
    [GeneratedRegex(@"(?m)^(?<label>Current release: )(?<version>" + Version + @")(?=\.?\r?$)", RegexOptions.CultureInvariant)]
    private static partial Regex ShortReadmeMarker();
    [GeneratedRegex(@"(?m)^## (?<version>" + Version + @") — current published release(?=\r?$)", RegexOptions.CultureInvariant)]
    private static partial Regex RoadmapCurrent();
    [GeneratedRegex(@"(?m)^## (?<version>" + Version + @") — planned(?=\r?$)", RegexOptions.CultureInvariant)]
    private static partial Regex RoadmapPlanned();
    [GeneratedRegex(@"(?m)^## (?<version>" + Version + @")(?= —|\r?$)", RegexOptions.CultureInvariant)]
    private static partial Regex RoadmapVersion();
    [GeneratedRegex(@"The current published release is \*\*(?<version>" + Version + @")\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex IndexMarker();
}
