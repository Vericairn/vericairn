// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Search;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[CollectionDefinition("search-performance", DisableParallelization = true)]
public sealed class SearchPerformanceIsolation;

/// <summary>The search design's budgets on a generated estate of realistic size, isolated from other collections' CPU and I/O load.</summary>
[Collection("search-performance")]
public sealed class SearchPerformanceTests
{
    [Fact]
    public void ARealisticallySizedEstateBuildsQueriesAndUpdatesWithinTheDesignsBudgets()
    {
        // About the first-party estate's size when the design was written: 5,000 records and 60 documents with catalog tables.
        // Like its records, about half of each record file is the text of its string fields, in words of English lengths drawn as
        // natural text draws them, a few common and most rare (roughly Zipf's law), and records refer to earlier records by id and hash.
        using var estate = new SearchEstate(content: false);
        var random = new Random(20261107);
        string[] vocabulary = Enumerable.Range(0, 6000).Select(i => new string(Enumerable.Range(0, 2 + (i * 31 % 9)).Select(j => (char)('a' + (i * 7 + j * 13 + j * j) % 26)).ToArray())).Distinct().ToArray();
        string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(_ => vocabulary[(int)(vocabulary.Length * Math.Pow(random.NextDouble(), 3))]));
        var earlier = new List<RecordEnvelope>();
        string Input()
        {
            if (earlier.Count == 0) return "";
            RecordEnvelope input = earlier[random.Next(earlier.Count)];
            return $$"""{"type":"{{input.Type}}","id":"{{input.Id}}","hash":"{{input.ComputedHash}}"}""";
        }

        for (int i = 0; i < 5000; i++)
        {
            string key = "P-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            earlier.Add(i % 3 == 0
                ? estate.Add("decision", $$"""{"title":"{{Words(8)}}","stake":"medium","statement":"{{Words(60)}}","summary":"{{Words(25)}}","synthesis":"{{Words(90)}}","effects":["{{Words(10)}}","{{Words(10)}}","{{Words(10)}}"],"notAuthorized":["{{Words(8)}}"],"affects":[],"decidedBy":"{{SearchEstate.Operator}}","decidedAt":"2026-11-01T12:00:00Z"}""")
                : estate.Add("work-order", $$"""{"key":"{{key}}","kind":"ordinary","title":"{{Words(8)}}","intent":"{{Words(70)}}","scope":["{{Words(15)}}","{{Words(15)}}","{{Words(15)}}"],"exitQuestion":"{{Words(20)}}","inputs":[{{Input()}}],"deliverables":["{{Words(8)}}","{{Words(8)}}","{{Words(8)}}"]}"""));
        }
        for (int d = 0; d < 60; d++)
        {
            var text = new StringBuilder("# Document ").Append(d).Append("\n\n").Append(Words(200)).Append('\n');
            for (int h = 0; h < 8; h++) text.Append("\n## ").Append(Words(3)).Append("\n\n").Append(Words(250)).Append('\n');
            text.Append("\n## Catalog\n\n| Entry | Holds |\n| --- | --- |\n");
            for (int r = 0; r < 50; r++) text.Append("| `E-").Append(d).Append('-').Append(r).Append("` | ").Append(Words(25)).Append(" |\n");
            estate.Document("design/generated-" + d.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".md", "Generated " + d, text.ToString());
        }

        using EstateService service = estate.Open();
        SearchIndexResponse built = service.SearchIndex(new SearchIndexRequest(Rebuild: true));
        Assert.Null(built.Problem);
        Assert.True(built.ElapsedMilliseconds <= 30_000, $"A full build took {built.ElapsedMilliseconds} ms.");
        Assert.True(built.IndexBytes <= built.SourceBytes / 2, $"The index takes {built.IndexBytes} bytes for {built.SourceBytes} source bytes.");

        // Query time is the index's own work for a query: matching, scoring, rechecking and excerpting, apart from loading the estate.
        KeywordIndex index = service.SearchStoreForTests();
        (EstateModel model, IReadOnlyDictionary<string, RecordFile> files) = service.Session.LoadIdentities();
        var snapshot = new SearchSnapshot(model, files, SearchSnapshot.CurrentPins(model), pin => ContextSelection.LocalOriginal(model.Root, pin.Path, pin.ByteHash, null, pin.HashMode));
        var reader = new SearchReader(null, ["Internal", "Members only", "Public"], true);
        // Each query's time is the least of three runs, so a test machine busy with other suites measures the search's own work.
        var times = new List<long>();
        for (int q = 0; q < 60; q++)
        {
            (SearchQuery? query, _) = SearchQuery.Parse(Words(3));
            long least = long.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var watch = Stopwatch.StartNew();
                SearchRunner.Found found = SearchRunner.Run(index, snapshot, query!, reader, 10);
                foreach (SearchRunner.Candidate candidate in found.Candidates) SearchRunner.Excerpt(candidate, query!);
                least = Math.Min(least, watch.ElapsedMilliseconds);
            }
            times.Add(least);
        }
        long p95 = times.Order().ElementAt((int)Math.Ceiling(times.Count * 0.95) - 1);
        Assert.True(p95 <= 250, $"95 percent of queries took at most {p95} ms.");

        // An update after one record write: the index's comparison and incremental update, the least of three writes.
        long updated = long.MaxValue;
        for (int write = 0; write < 3; write++)
        {
            estate.Add("decision", SearchEstate.Decision("A new decision", Words(60), Words(5)));
            (model, files) = service.Session.LoadIdentities();
            snapshot = new SearchSnapshot(model, files, SearchSnapshot.CurrentPins(model), pin => ContextSelection.LocalOriginal(model.Root, pin.Path, pin.ByteHash, null, pin.HashMode));
            var update = Stopwatch.StartNew();
            string fingerprint = snapshot.Fingerprint();
            Assert.False(index.Matches(fingerprint, model.Root));
            KeywordIndex.UpdateResult result = index.Update(model, files, snapshot.Pins, fingerprint, snapshot.Bytes, null);
            updated = Math.Min(updated, update.ElapsedMilliseconds);
            Assert.True(result.Current);
            Assert.Equal(1, result.RecordsAdded);
        }
        Assert.True(updated <= 100, $"The update after a write took {updated} ms.");
        TestContext.Current.SendDiagnosticMessage($"search budgets: build {built.ElapsedMilliseconds} ms; index {built.IndexBytes} of {built.SourceBytes} source bytes; query p95 {p95} ms; update {updated} ms");
    }
}
