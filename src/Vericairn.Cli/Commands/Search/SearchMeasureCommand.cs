// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>
/// Runs a question set against the estate's search and reports the measures of the search design: recall at 5 and 10, mean
/// reciprocal rank, index size against the indexed source bytes, build time and query times. It records nothing; the figures
/// are what a release's verification record reports. A question set kept inside the estate is often a pinned document itself, so
/// its own passages are skipped: each of its rows would otherwise match its own query and outrank the real sources.
/// </summary>
internal sealed class SearchMeasureCommand : Command
{
    public SearchMeasureCommand(CommandContext context)
        : base("measure", "Run a question set against search and report recall at 5 and 10, mean reciprocal rank, index size and build and query times. Records nothing. When the question set lies inside the estate, candidates from that file itself are skipped.")
    {
        var questions = new Argument<string>("questions") { Description = "A question set: JSON (format vericairn.search-questions/1, questions of query and expected) or Markdown whose first table has Query (or Question) and Expected columns. Expected names record ids, keys, document revision ids or paths." };
        var rebuild = new Option<bool>("--rebuild") { Description = "Rebuild the index first and report the full build time." };
        Arguments.Add(questions);
        Options.Add(rebuild);
        SetAction(result => context.WithApi(result.GetValue(context.EstateOption)!, api =>
        {
            string path = result.GetValue(questions)!;
            List<SearchMeasures.Question> set;
            try { set = SearchMeasures.Read(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
            {
                return context.Fail("The question set could not be read: " + exception.Message);
            }

            if (set.Count == 0) return context.Fail("The question set holds no questions.");
            SearchIndexResponse index = api.SearchIndex(new SearchIndexRequest(result.GetValue(rebuild)));
            if (index.Problem is not null) return context.Fail(index.Problem.Message);
            string? own = SearchMeasures.EstatePath(result.GetValue(context.EstateOption)!, path);
            SearchMeasures.Report report = SearchMeasures.Run(api, set, index, result.GetValue(rebuild), own);
            context.Output.Write(result.GetValue(context.JsonOption) ? JsonSerializer.Serialize(report, ProtocolJson.Options) + "\n" : report.Text());
            return report.Failures == 0 ? CommandContext.Clean : CommandContext.Failed;
        }));
    }
}

/// <summary>The measures of a question set: pure computation over search responses, so tests and the command share it.</summary>
internal static class SearchMeasures
{
    internal sealed record Question(string Query, IReadOnlyList<string> Expected);

    internal sealed record Outcome(string Query, IReadOnlyList<string> Expected, IReadOnlyList<string> Found5, IReadOnlyList<string> Found10, int? FirstRank, long Milliseconds, string? Problem);

    internal sealed record Report(int Questions, double RecallAt5, double RecallAt10, double MeanReciprocalRank, long IndexBytes, long SourceBytes, double IndexToSource,
        long? BuildMilliseconds, long QueryMillisecondsP95, long QueryMillisecondsMax, int Failures, string? Skipped, IReadOnlyList<Outcome> Outcomes)
    {
        internal string Text()
        {
            var text = new StringBuilder();
            text.Append("Search measures over ").Append(Questions.ToString(CultureInfo.InvariantCulture)).Append(" questions (").Append(Records.Search.SearchSelections.KeywordMethod).Append(")\n")
                .Append("  recall at 5:  ").Append(RecallAt5.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n')
                .Append("  recall at 10: ").Append(RecallAt10.ToString("0.000", CultureInfo.InvariantCulture)).Append(" (budget: at least 0.8)\n")
                .Append("  mean reciprocal rank: ").Append(MeanReciprocalRank.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n')
                .Append("  index: ").Append(IndexBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes for ").Append(SourceBytes.ToString(CultureInfo.InvariantCulture))
                .Append(" source bytes (").Append(IndexToSource.ToString("0.000", CultureInfo.InvariantCulture)).Append("; budget: at most 0.5)\n");
            if (BuildMilliseconds is long build) text.Append("  full build: ").Append(build.ToString(CultureInfo.InvariantCulture)).Append(" ms (budget: 30 s on the slowest CI platform)\n");
            text.Append("  query time: ").Append(QueryMillisecondsP95.ToString(CultureInfo.InvariantCulture)).Append(" ms at the 95th percentile, ")
                .Append(QueryMillisecondsMax.ToString(CultureInfo.InvariantCulture)).Append(" ms at most (budget: 250 ms for 95 percent)\n");
            if (Skipped is not null) text.Append("  skipped: candidates from the question set itself, ").Append(Skipped).Append('\n');
            foreach (Outcome outcome in Outcomes.Where(o => o.Problem is not null || o.Found10.Count < o.Expected.Count))
            {
                text.Append("  ").Append(outcome.Problem is null ? "missed" : "refused").Append(": ").Append(outcome.Query);
                if (outcome.Problem is not null) text.Append(" (").Append(outcome.Problem).Append(')');
                else text.Append(" (expected ").Append(string.Join(", ", outcome.Expected.Except(outcome.Found10, StringComparer.Ordinal))).Append(')');
                text.Append('\n');
            }

            return text.ToString();
        }
    }

    /// <summary>Reads a question set from JSON or from a Markdown table.</summary>
    internal static List<Question> Read(string path)
    {
        string text = File.ReadAllText(path);
        if (text.TrimStart().StartsWith('{'))
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.GetProperty("format").GetString() != "vericairn.search-questions/1") throw new FormatException("The JSON question set's format is vericairn.search-questions/1.");
            return document.RootElement.GetProperty("questions").EnumerateArray()
                .Select(q => new Question(q.GetProperty("query").GetString()!, q.GetProperty("expected").EnumerateArray().Select(e => e.GetString()!).ToList())).ToList();
        }

        var questions = new List<Question>();
        int queryColumn = -1, expectedColumn = -1;
        bool inTable = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                if (inTable && questions.Count > 0) break;
                inTable = false;
                continue;
            }

            string[] cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (!inTable)
            {
                queryColumn = Array.FindIndex(cells, c => c.Equals("query", StringComparison.OrdinalIgnoreCase) || c.Equals("question", StringComparison.OrdinalIgnoreCase));
                expectedColumn = Array.FindIndex(cells, c => c.StartsWith("expected", StringComparison.OrdinalIgnoreCase));
                inTable = queryColumn >= 0 && expectedColumn >= 0;
                continue;
            }

            if (cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':'))) continue;
            if (Math.Max(queryColumn, expectedColumn) >= cells.Length) continue;
            string query = cells[queryColumn].Trim('`');
            List<string> expected = cells[expectedColumn].Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim('`', '.', ' ')).Where(e => e.Length > 0).ToList();
            if (query.Length > 0 && expected.Count > 0) questions.Add(new Question(query, expected));
        }

        return questions;
    }

    /// <summary>
    /// The question set's estate-relative path, with '/' separators, when the file lies inside the estate; otherwise null. Paths are
    /// compared as given after full-path resolution; a link elsewhere into the estate is not followed.
    /// </summary>
    internal static string? EstatePath(string root, string file)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
        if (relative == "." || relative == ".." || Path.IsPathRooted(relative) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal)) return null;
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Runs every question and computes the measures. Candidates from <paramref name="skip"/>, the question set's own estate path, are
    /// dropped before ranking; each query then asks for the most candidates a search returns, so the top ten are still filled.
    /// </summary>
    internal static Report Run(IEstateApi api, IReadOnlyList<Question> questions, SearchIndexResponse index, bool rebuilt, string? skip = null)
    {
        int limit = skip is null ? 10 : Records.Search.SearchSelections.MaxCandidates;
        StringComparison pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // One uncounted query first, so the times measure searching rather than the process's first run of its code.
        if (questions.Count > 0) api.Search(new SearchRequest(questions[0].Query, limit));
        var outcomes = new List<Outcome>();
        foreach (Question question in questions)
        {
            var watch = Stopwatch.StartNew();
            SearchResponse response = api.Search(new SearchRequest(question.Query, limit));
            long elapsed = watch.ElapsedMilliseconds;
            if (response.Problem is not null)
            {
                outcomes.Add(new Outcome(question.Query, question.Expected, [], [], null, elapsed, response.Problem.Message));
                continue;
            }

            IReadOnlyList<SearchCandidate> ranked = response.Candidates.Where(c => c.State != "index-fault" && !From(c, skip, pathComparison)).OrderBy(c => c.Rank).Take(10).ToList();
            List<string> Found(int k) => question.Expected.Where(e => ranked.Take(k).Any(c => Matches(c, e))).ToList();
            int? first = ranked.Select((c, i) => (c, i)).Where(p => question.Expected.Any(e => Matches(p.c, e))).Select(p => (int?)(p.i + 1)).FirstOrDefault();
            outcomes.Add(new Outcome(question.Query, question.Expected, Found(5), Found(10), first, elapsed, null));
        }

        double Recall(Func<Outcome, int> found) => outcomes.Average(o => o.Expected.Count == 0 ? 0 : (double)found(o) / o.Expected.Count);
        long[] times = outcomes.Select(o => o.Milliseconds).Order().ToArray();
        long p95 = times.Length == 0 ? 0 : times[Math.Min(times.Length - 1, (int)Math.Ceiling(times.Length * 0.95) - 1)];
        long size = index.IndexBytes ?? 0;
        long source = index.SourceBytes ?? 0;
        return new Report(outcomes.Count, Recall(o => o.Found5.Count), Recall(o => o.Found10.Count), outcomes.Average(o => o.FirstRank is int r ? 1.0 / r : 0),
            size, source, source == 0 ? 0 : (double)size / source, rebuilt ? index.ElapsedMilliseconds : null, p95, times.LastOrDefault(),
            outcomes.Count(o => o.Problem is not null), skip, outcomes);
    }

    // A document candidate from the question set's own file.
    private static bool From(SearchCandidate candidate, string? skip, StringComparison comparison) =>
        skip is not null && candidate.Kind != "record" && candidate.Source.ValueKind == JsonValueKind.Object &&
        candidate.Source.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String && string.Equals(path.GetString(), skip, comparison);

    // An expected source matches a candidate by record id or key, document revision id, or document path (a prefix when it ends in '/').
    internal static bool Matches(SearchCandidate candidate, string expected)
    {
        JsonElement source = candidate.Source;
        string? Value(string name) => source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (Value("id") == expected || Value("revision") == expected) return true;
        if (candidate.Key is string key && key.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        string? path = Value("path");
        if (path is null || candidate.Kind == "record") return false;
        return expected.EndsWith('/') ? path.StartsWith(expected, StringComparison.Ordinal) : path == expected.Split('#')[0];
    }
}
