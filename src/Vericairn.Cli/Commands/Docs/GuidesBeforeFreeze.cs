// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;

namespace Vericairn.Cli;

/// <summary>
/// The documentation pass before freezing (the estate's design/release-procedure.md § 7, step docs-before-freeze): the version-tracking
/// guides a release's documentation set declares, read as they stand, for two traps. A prior-version token is a version of one of the
/// releases before this one standing as the current release outside a history section or label, as 0.8.4's guides named 0.8.3 and its
/// notes-window sentence 0.8.2. Preparation wording is what only a set still being prepared says, such as notes that are a placeholder,
/// as 0.7.0's set did. The check reads text; it never judges whether a sentence is true, and it changes nothing.
/// </summary>
internal static partial class GuidesBeforeFreeze
{
    /// <summary>The verification method of the prior-version check.</summary>
    internal const string PriorVersionMethod = "vericairn.software/prior-version-tokens";

    /// <summary>The verification method of the preparation-wording check.</summary>
    internal const string PreparationMethod = "vericairn.software/preparation-wording";

    /// <summary>
    /// The marker that makes the section it stands in, or before any heading the whole document, a history section: a reviewed choice
    /// in the guide itself, for history whose heading does not say so.
    /// </summary>
    internal const string HistoryMarker = "<!-- vericairn:history -->";

    /// <summary>The largest guide read; a larger file is refused rather than read in part.</summary>
    internal const long MaximumBytes = 8 * 1024 * 1024;

    /// <summary>Words that, just before a version, say the sentence is about that release in the past.</summary>
    private static readonly string[] HistoricalMarkers =
    [
        "from", "since", "in", "until", "before", "after", "through", "during", "fixed in", "added in", "introduced in", "removed in", "earlier than", "older than", "up to",
    ];

    /// <summary>One location a check found.</summary>
    /// <param name="Check">The verification method of the check that found it.</param>
    /// <param name="Path">The guide, as the set names it under its guides directory.</param>
    /// <param name="Line">The line, from 1.</param>
    /// <param name="Found">The token or phrase.</param>
    /// <param name="Text">The line, trimmed and shortened.</param>
    internal sealed record Finding(string Check, string Path, int Line, string Found, string Text)
    {
        /// <summary>The location and what was found, in words.</summary>
        public override string ToString() => Check == PriorVersionMethod
            ? $"{Path}:{Line}: prior-version token {Found} outside a history section: {Text}"
            : $"{Path}:{Line}: preparation wording '{Found}': {Text}";
    }

    /// <summary>What the pass read and found.</summary>
    /// <param name="Read">The guides read, as the set names them.</param>
    /// <param name="History">The guides read only for preparation wording, since their name carries a version: a history document.</param>
    /// <param name="Skipped">The declared documents the guides directory does not hold, such as those the build tooling generates.</param>
    /// <param name="Findings">Every location found, in the order read.</param>
    internal sealed record Scan(IReadOnlyList<string> Read, IReadOnlyList<string> History, IReadOnlyList<string> Skipped, IReadOnlyList<Finding> Findings)
    {
        /// <summary>The locations one check found.</summary>
        public IReadOnlyList<Finding> Of(string check) => Findings.Where(f => f.Check == check).ToList();
    }

    /// <summary>
    /// Reads each declared document the guides directory holds as a plain file and finds every prior-version token and every phrase of
    /// preparation wording. A link, a directory or a file above the limit is refused.
    /// </summary>
    internal static Scan Run(string guides, string label, IReadOnlyList<string> documents, IReadOnlyList<string> prior)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(prior);
        var read = new List<string>();
        var history = new List<string>();
        var skipped = new List<string>();
        var findings = new List<Finding>();
        List<(string Version, Regex Pattern)> tokens = prior.Distinct(StringComparer.Ordinal).Select(v => (v, Token(v))).ToList();
        foreach (string name in documents.Distinct(StringComparer.Ordinal))
        {
            if (name.Length == 0 || Path.IsPathRooted(name) || name.Split('/', '\\').Any(part => part is "" or "." or ".."))
            {
                throw new InvalidOperationException($"The declaration names '{name}', which is not a relative path inside the guides directory.");
            }

            string path = Path.Combine(guides, name.Replace('/', Path.DirectorySeparatorChar));
            var file = new FileInfo(path);
            if (file.LinkTarget is not null || Directory.Exists(path))
            {
                throw new InvalidOperationException($"{label}/{name} is a link or a directory; the pass reads only plain files.");
            }

            if (!file.Exists)
            {
                skipped.Add(name);
                continue;
            }

            if (file.Length > MaximumBytes)
            {
                throw new InvalidOperationException($"{label}/{name} is larger than {MaximumBytes / (1024 * 1024)} MiB; the pass never reads a guide in part.");
            }

            string shown = label + "/" + name;
            bool versioned = VersionInName().IsMatch(Path.GetFileName(name));
            (versioned ? history : read).Add(name);
            string text = File.ReadAllText(path, new UTF8Encoding(false, true));
            findings.AddRange(ScanText(shown, text, versioned ? [] : tokens));
        }

        return new Scan(read, history, skipped, findings);
    }

    /// <summary>The locations one guide's text holds, for the given prior versions.</summary>
    internal static IEnumerable<Finding> ScanText(string path, string text, IReadOnlyList<(string Version, Regex Pattern)> tokens)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sections = new List<(int Level, bool History)>();
        bool fenced = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && line.Trim() == HistoryMarker)
            {
                // A marked section, or before any heading the whole document, is history.
                if (sections.Count == 0)
                {
                    sections.Add((0, true));
                }
                else
                {
                    sections[^1] = (sections[^1].Level, true);
                }
            }
            else if (!fenced && Heading().Match(line) is { Success: true } heading)
            {
                int level = heading.Groups["level"].Value.Length;
                sections.RemoveAll(s => s.Level >= level);
                sections.Add((level, IsHistoryHeading(heading.Groups["title"].Value)));
            }

            foreach (Match phrase in Preparation().Matches(line).Concat(Unfinished().Matches(line)))
            {
                yield return new Finding(PreparationMethod, path, index + 1, phrase.Value, Shorten(line));
            }

            if (sections.Any(s => s.History) || RevisionLine().IsMatch(line))
            {
                continue;
            }

            foreach ((string version, Regex pattern) in tokens)
            {
                foreach (Match token in pattern.Matches(line))
                {
                    if (!Historical(line, token))
                    {
                        yield return new Finding(PriorVersionMethod, path, index + 1, version, Shorten(line));
                    }
                }
            }
        }
    }

    /// <summary>The pattern of one version as a whole token: not part of a longer version, with an optional leading v of a tag.</summary>
    internal static Regex Token(string version) =>
        new(@"(?<![0-9])(?<![0-9]\.)v?" + Regex.Escape(version) + @"(?![0-9]|\.[0-9])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>
    /// A history section's heading: one about history or changes, or one labelled with the release that added what it describes, such
    /// as "Withdrawal and moving planned work (0.8.3)".
    /// </summary>
    internal static bool IsHistoryHeading(string title)
    {
        string lower = title.ToLowerInvariant();
        return lower.Contains("history", StringComparison.Ordinal) || lower.Contains("changes", StringComparison.Ordinal) || lower.Contains("changelog", StringComparison.Ordinal)
            || lower.Contains("earlier release", StringComparison.Ordinal) || lower.Contains("previous release", StringComparison.Ordinal)
            || lower.Contains("release notes", StringComparison.Ordinal) || LabelledHeading().IsMatch(title);
    }

    /// <summary>
    /// Whether a token stands as history rather than as the current release: inside a parenthesized label such as "(0.9.1)" or "(FIX-90,
    /// 0.9.1)", just after a word such as "From" or "fixed in", just before "or earlier", or part of the name of a versioned page or set.
    /// </summary>
    private static bool Historical(string line, Match token)
    {
        int open = line.LastIndexOf('(', token.Index);
        int closeBefore = open < 0 ? -1 : line.IndexOf(')', open, token.Index - open);
        int close = line.IndexOf(')', token.Index + token.Length);
        int openAfter = close < 0 ? -1 : line.IndexOf('(', token.Index + token.Length, close - token.Index - token.Length);
        if (open >= 0 && closeBefore < 0 && close >= 0 && openAfter < 0)
        {
            return true;
        }

        string before = line[..token.Index].TrimEnd().ToLowerInvariant();

        // A list of earlier versions takes the word before the list: "upgrades from 0.8.0, 0.8.2 or 0.9.0".
        if (ListTail().Match(before) is { Success: true } list)
        {
            before = list.Groups["head"].Value.TrimEnd();
        }

        foreach (string filler in Fillers)
        {
            if (before.EndsWith(" " + filler, StringComparison.Ordinal) || before == filler)
            {
                before = before[..^filler.Length].TrimEnd();
            }
        }

        if (HistoricalMarkers.Any(m => before == m || before.EndsWith(" " + m, StringComparison.Ordinal) || before.EndsWith("(" + m, StringComparison.Ordinal)))
        {
            return true;
        }

        string after = line[(token.Index + token.Length)..].ToLowerInvariant();
        if (HistoricalTails.Any(t => after.StartsWith(t, StringComparison.Ordinal)))
        {
            return true;
        }

        // The name of a versioned page or set, or a link's text naming one: [for 0.9.0](api-compatibility-0.9.0.md).
        return after.StartsWith(".md", StringComparison.Ordinal) || after.StartsWith("/readme", StringComparison.Ordinal) || after.StartsWith("](", StringComparison.Ordinal)
            || line[..token.Index].EndsWith("api-compatibility-", StringComparison.Ordinal) || line[..token.Index].EndsWith("releases/", StringComparison.Ordinal);
    }

    /// <summary>Words between a marker and its version that leave the marker standing: "From Vericairn 0.9.1", "since release 0.8.3".</summary>
    private static readonly string[] Fillers = ["vericairn", "release", "version"];

    /// <summary>Words just after a version that say the sentence is about that release in the past.</summary>
    private static readonly string[] HistoricalTails = [" or earlier", " and earlier", " or older", " and older", " or later", " cycle", "'s release review", "'s release security review"];

    private static string Shorten(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length > 200 ? trimmed[..200] + "..." : trimmed;
    }

    [GeneratedRegex(@"^(?<level>#{1,6})\s+(?<title>.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\((?:[^()]*[\s,])?v?\d+\.\d+\.\d+(?:[\s,][^()]*)?\)\s*$")]
    private static partial Regex LabelledHeading();

    [GeneratedRegex(@"^(?<head>.*?)(?:v?\d+\.\d+\.\d+(?:'s)?\s*(?:,|\bor\b|\band\b)\s*)+$")]
    private static partial Regex ListTail();

    [GeneratedRegex(@"^\s*Revision \d+[,.]")]
    private static partial Regex RevisionLine();

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionInName();

    [GeneratedRegex(@"\b(?:under preparation|not yet rendered|pre-build|the builder must|(?:is|are) a placeholder|placeholder (?:release )?notes|notes placeholder)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preparation();

    [GeneratedRegex(@"\b(?:TODO|TBD)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Unfinished();
}
