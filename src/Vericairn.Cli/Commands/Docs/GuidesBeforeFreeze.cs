// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;

namespace Vericairn.Cli;

/// <summary>
/// The documentation pass before freezing (the estate's design/release-procedure.md § 7, step docs-before-freeze): the version-tracking
/// guides a release's documentation set declares, read as they stand, for two traps. A prior-version token is a version of one of the
/// releases before this one named as the current release, as 0.8.4's guides named 0.8.3 and its notes-window sentence 0.8.2; one in a
/// history section or a label, or named as a source or predecessor, such as an upgrade's --from or "follows 0.9.1's recorded end",
/// passes. Preparation wording is what only a set still being prepared says, such as notes that are a placeholder, as 0.7.0's set did.
/// Each finding names the rule it matched. The check reads text; it never judges whether a sentence is true, and it changes nothing.
/// </summary>
internal static partial class GuidesBeforeFreeze
{
    /// <summary>The verification method of the prior-version check.</summary>
    internal const string PriorVersionMethod = "vericairn.software/prior-version-tokens";

    /// <summary>The verification method of the preparation-wording check.</summary>
    internal const string PreparationMethod = "vericairn.software/preparation-wording";

    /// <summary>
    /// The marker that makes the section it stands in a history section: a reviewed choice in the guide itself, for history whose heading
    /// does not say so. Before any heading it marks nothing.
    /// </summary>
    internal const string HistoryMarker = "<!-- vericairn:history -->";

    /// <summary>The largest guide read; a larger file is refused rather than read in part.</summary>
    internal const long MaximumBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Words that, just before a version, say the sentence is about that release in the past or name it as a source or predecessor: "since
    /// 0.9.1", "follows 0.9.1's recorded end", "replaces 0.9.0".
    /// </summary>
    private static readonly string[] HistoricalMarkers =
    [
        "from", "since", "in", "until", "before", "after", "through", "during", "fixed in", "added in", "introduced in", "removed in", "earlier than", "older than", "up to",
        "follows", "succeeds", "replaces", "supersedes",
    ];

    /// <summary>The rule a prior-version finding matched: a word such as current or latest, or a target such as --to, names it current.</summary>
    internal const string CurrentClaimRule = "current-claim";

    /// <summary>The rule a prior-version finding matched: it names that release's install, download, package or tag.</summary>
    internal const string ReleaseArtifactRule = "release-artifact";

    /// <summary>The rule a prior-version finding matched: a mention outside a history section and a label that names no source or predecessor.</summary>
    internal const string CurrentMentionRule = "current-mention";

    /// <summary>The rule a preparation finding matched: wording only a set still being prepared uses.</summary>
    internal const string PreparationPhraseRule = "preparation-phrase";

    /// <summary>The rule a preparation finding matched: a marker of unfinished work, TODO or TBD.</summary>
    internal const string UnfinishedMarkerRule = "unfinished-marker";

    /// <summary>What a rule finds, in words.</summary>
    internal static string Describe(string rule) => rule switch
    {
        CurrentClaimRule => "a word such as current or latest, or a target such as --to, names it the current release",
        ReleaseArtifactRule => "it names that release's install, download, package or tag",
        CurrentMentionRule => "it stands outside a history section and a label, and names no source or predecessor",
        PreparationPhraseRule => "wording only a set still being prepared uses",
        _ => "a marker of unfinished work",
    };

    /// <summary>One location a check found.</summary>
    /// <param name="Check">The verification method of the check that found it.</param>
    /// <param name="Path">The guide, as the set names it under its guides directory.</param>
    /// <param name="Line">The line, from 1.</param>
    /// <param name="Found">The token or phrase.</param>
    /// <param name="Text">The line, trimmed and shortened.</param>
    /// <param name="Rule">The rule it matched, such as current-claim or preparation-phrase.</param>
    internal sealed record Finding(string Check, string Path, int Line, string Found, string Text, string Rule)
    {
        /// <summary>The location, what was found and the rule it matched, in words.</summary>
        public override string ToString() => Check == PriorVersionMethod
            ? $"{Path}:{Line}: prior-version token {Found} named as the current release ({Rule}: {Describe(Rule)}): {Text}"
            : $"{Path}:{Line}: preparation wording '{Found}' ({Rule}: {Describe(Rule)}): {Text}";
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
            // Both separators divide a name on every platform, so the path read and the components checked for links are the same.
            string[] parts = name.Split('/', '\\');
            if (name.Length == 0 || Path.IsPathRooted(name) || parts.Any(part => part is "" or "." or ".."))
            {
                throw new InvalidOperationException($"The declaration names '{name}', which is not a relative path inside the guides directory.");
            }

            string path = Path.Combine([guides, .. parts]);
            if (LinkedOnTheWay(guides, parts) is string linked)
            {
                throw new InvalidOperationException($"{label}/{linked} is a link; the pass reads only plain files inside the guides directory and follows no link out of it.");
            }

            var file = new FileInfo(path);
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"{label}/{name} is a directory; the pass reads only plain files.");
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

    /// <summary>
    /// The first component of a declared name, from the guides directory down to the file itself, that is a link, such as a directory
    /// linked to one outside the guides; null when none is, and the resolved file lies inside the guides directory.
    /// </summary>
    private static string? LinkedOnTheWay(string guides, string[] parts)
    {
        string root = Path.GetFullPath(guides);
        string current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
            {
                return string.Join('/', parts.Take(i + 1));
            }
        }

        string full = Path.GetFullPath(current);
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? null : string.Join('/', parts);
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
            else if (!fenced && line.Trim() == HistoryMarker && sections.Count > 0)
            {
                // A marked section is history; a marker before any heading marks nothing, so no guide exempts itself whole.
                sections[^1] = (sections[^1].Level, true);
            }
            else if (!fenced && Heading().Match(line) is { Success: true } heading)
            {
                int level = heading.Groups["level"].Value.Length;
                sections.RemoveAll(s => s.Level >= level);
                sections.Add((level, IsHistoryHeading(heading.Groups["title"].Value)));
            }

            foreach ((Match phrase, string rule) in Preparation().Matches(line).Select(m => (m, PreparationPhraseRule)).Concat(Unfinished().Matches(line).Select(m => (m, UnfinishedMarkerRule))))
            {
                yield return new Finding(PreparationMethod, path, index + 1, phrase.Value, Shorten(line), rule);
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
                        yield return new Finding(PriorVersionMethod, path, index + 1, version, Shorten(line), CurrentRule(line, token));
                    }
                }
            }
        }
    }

    /// <summary>The pattern of one version as a whole token: not part of a longer version, with an optional leading v of a tag.</summary>
    internal static Regex Token(string version) =>
        new(@"(?<![0-9])(?<![0-9]\.)v?" + Regex.Escape(version) + @"(?![0-9]|\.[0-9])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>
    /// A history section's heading: one about history or changes. A heading labelled with the release that added what it describes, such
    /// as "Withdrawal and moving planned work (0.8.3)", describes current behaviour and is read like any other.
    /// </summary>
    internal static bool IsHistoryHeading(string title)
    {
        string lower = title.ToLowerInvariant();
        return lower.Contains("history", StringComparison.Ordinal) || lower.Contains("changes", StringComparison.Ordinal) || lower.Contains("changelog", StringComparison.Ordinal)
            || lower.Contains("earlier release", StringComparison.Ordinal) || lower.Contains("previous release", StringComparison.Ordinal)
            || lower.Contains("release notes", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a token stands as history, a source or a predecessor rather than as the current release: inside a parenthesized label such
    /// as "(0.9.1)" or "(FIX-90, 0.9.1)", the value of a source option such as an upgrade's --from, just after a word such as "From",
    /// "fixed in" or "follows", just before "or earlier", or part of the name of a versioned page or set.
    /// </summary>
    private static bool Historical(string line, Match token)
    {
        // A source or an earlier release a command reads: "upgrade --from 0.9.1", "--previous 0.9.0".
        if (SourceOption().IsMatch(line[..token.Index]))
        {
            return true;
        }

        // A release label in parentheses: "(0.9.1)", "(FIX-90, 0.9.1)" or "(from 0.9.1)", and nothing else inside them, so that
        // "(the current release, 0.9.1)" is read as a claim.
        int open = line.LastIndexOf('(', token.Index);
        int closeBefore = open < 0 ? -1 : line.IndexOf(')', open, token.Index - open);
        int close = line.IndexOf(')', token.Index + token.Length);
        int openAfter = close < 0 ? -1 : line.IndexOf('(', token.Index + token.Length, close - token.Index - token.Length);
        if (open >= 0 && closeBefore < 0 && close >= 0 && openAfter < 0
            && Label().IsMatch(line[(open + 1)..token.Index] + "VERSION" + line[(token.Index + token.Length)..close])
            && !ClaimBefore().IsMatch(line[..open]))
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

        // A marker counts as a word of its own: after a space, a parenthesis or a quotation mark, as in "follows 0.9.1's recorded end".
        if (HistoricalMarkers.Any(m => before.EndsWith(m, StringComparison.Ordinal) && (before.Length == m.Length || !char.IsLetterOrDigit(before[^(m.Length + 1)]) && before[^(m.Length + 1)] is not ('-' or '_'))))
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

    /// <summary>
    /// The rule by which a token not standing as history names its release as the current one: a word such as current or latest, or a
    /// target such as --to; the release's install, download, package or tag; or else a plain mention.
    /// </summary>
    private static string CurrentRule(string line, Match token)
    {
        string before = line[..token.Index];
        string after = line[(token.Index + token.Length)..];
        if (CurrentBefore().IsMatch(before) || CurrentAfter().IsMatch(after))
        {
            return CurrentClaimRule;
        }

        return ArtifactBefore().IsMatch(before) || ArtifactAfter().IsMatch(after) || InstallWords().IsMatch(line) ? ReleaseArtifactRule : CurrentMentionRule;
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

    [GeneratedRegex(@"^\s*(?:[A-Z][A-Z0-9]*-\d+,\s*)*(?:(?:from|since|in|until|fixed in|added in)\s+)?VERSION(?:,\s*[A-Z][A-Z0-9]*-\d+)*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Label();

    // A label after a word that names the current release, such as "the current release (0.9.1)" or "Vericairn (0.9.1)", is a claim.
    [GeneratedRegex(@"\b(?:release|version|vericairn|current|latest|now|is|the)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaimBefore();

    [GeneratedRegex(@"^(?<head>.*?)(?:v?\d+\.\d+\.\d+(?:'s)?\s*(?:,|\bor\b|\band\b)\s*)+$")]
    private static partial Regex ListTail();

    // The value of an option that names a source or an earlier release: --from 0.9.1, --from=0.9.1, --previous 0.9.0.
    [GeneratedRegex(@"--(?:from|previous)(?:=|\s+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceOption();

    // A word that names the release current, or a target: "the current release, 0.9.1", "latest version 0.9.1", "--to 0.9.1", "upgrade to 0.9.1".
    [GeneratedRegex(@"(?:\b(?:current|latest|newest)\b[^.;:]{0,40}|--to(?:=|\s+)|\b(?:upgrade|upgrades|upgraded|upgrading|update|updates|move|moves)\s+to\s+(?:vericairn\s+|release\s+|version\s+)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrentBefore();

    // A sentence that goes on to call it current: "0.9.1 is the current release", "the 0.9.1 command line is current", "0.9.1 (latest)".
    [GeneratedRegex(@"^(?:[^.;:]{0,40}\b(?:is|are|stays|remains)\s+(?:the\s+)?(?:current|latest|newest)\b|\s*\((?:current|latest|newest)\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrentAfter();

    // Part of an install, a package or a tag: "--version 0.9.1", "vericairn-0.9.1-osx-arm64", "Records.0.9.1.nupkg", ".../tag/v0.9.1", "@0.9.1".
    [GeneratedRegex(@"(?:--version(?:=|\s+)|/tag/|/download/|@|[A-Za-z][-_.])$", RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactBefore();

    [GeneratedRegex(@"^(?:[-_][A-Za-z<]|\.(?:nupkg|zip|tar|tgz|gz|msi|pkg|dmg|exe|deb|rpm)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactAfter();

    [GeneratedRegex(@"\b(?:install(?:s|ed|ing)?|download(?:s|ed|ing)?|unpack(?:s|ed|ing)?|add\s+package|tool\s+update)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallWords();

    [GeneratedRegex(@"^\s*Revision \d+[,.]")]
    private static partial Regex RevisionLine();

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionInName();

    [GeneratedRegex(@"\b(?:under preparation|not yet rendered|pre-build|the builder must|(?:is|are) a placeholder|placeholder (?:release )?notes|notes placeholder)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preparation();

    [GeneratedRegex(@"\b(?:TODO|TBD)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Unfinished();
}
