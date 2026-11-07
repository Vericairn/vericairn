// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Search;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>The selection contract in records: a settled run's observed searches, each a vericairn.search-selection/1 object with its own hash.</summary>
public sealed class SearchSelectionRecordTests
{
    /// <summary>A settled agent run whose agent made one search through the run's MCP server.</summary>
    public static PolicyFixture RecordedFixture()
    {
        var fixture = new PolicyFixture();
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assignment = fixture.Estate.Load().ById[attempt.Subject!.Id];
        RecordEnvelope intent = fixture.Add("operation", Intent(assignment), subject: assignment.Reference);
        JsonObject dispatched = PolicyFixture.Body(intent);
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        RecordEnvelope launched = fixture.Add("operation", dispatched, subject: assignment.Reference, supersedes: intent.Id);
        fixture.Add("operation", Settled(launched, fixture, Observed(Selection(fixture.Estate.Estate.Id))), subject: assignment.Reference, supersedes: launched.Id);
        return fixture;
    }

    private static JsonObject Intent(RecordEnvelope assignment) => new()
    {
        ["provider"] = "example", ["action"] = "agent-run", ["identity"] = "search-fixture-run", ["intent"] = "Run within the existing assignment.",
        ["target"] = new JsonObject { ["assignment"] = assignment.Id, ["principal"] = EstateModel.BodyString(assignment, "contributor"), ["workspace"] = "example repository" },
        ["status"] = "not-started", ["cap"] = new JsonObject { ["wallMinutes"] = 1 }, ["evidence"] = new JsonArray(),
    };

    private static JsonObject Settled(RecordEnvelope launched, PolicyFixture fixture, JsonObject? searches)
    {
        JsonObject settled = PolicyFixture.Body(launched);
        settled["status"] = "observed-success";
        settled["observed"] = new JsonObject { ["summary"] = "The run exited.", ["at"] = RecordParser.FormatTimestamp(fixture.Estate.Clock) };
        settled["usage"] = new JsonObject { ["wallMinutes"] = 1, ["unreported"] = new JsonArray() };
        if (searches is not null) settled["searchesObserved"] = searches;
        return settled;
    }

    /// <summary>A selection a record may hold: one Internal record candidate, with its hash.</summary>
    public static JsonObject Selection(string estate, string audience = "Internal", int candidates = 1)
    {
        var list = new JsonArray();
        for (int i = 0; i < candidates; i++)
        {
            var source = new JsonObject
            {
                ["bytes"] = 100, ["byteHash"] = "sha256:" + new string('b', 64), ["hash"] = "sha256:" + new string('c', 64),
                ["id"] = "01M6HRM89G55REQ0QRDVPJWQF" + (char)('A' + i % 20), ["kind"] = "record", ["path"] = "records/decision/2026-11/01M6HRM89G55REQ0QRDVPJWQFZ.json", ["type"] = "decision",
            };
            list.Add(new JsonObject
            {
                ["audience"] = audience, ["extent"] = new JsonObject { ["length"] = 100, ["start"] = 0 }, ["fields"] = new JsonArray("body.summary"), ["kind"] = "record",
                ["pin"] = "vc1_" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(estate)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                ["rank"] = i + 1, ["score"] = 123456 - i, ["source"] = source, ["state"] = "selected",
            });
        }

        var selection = new JsonObject
        {
            ["candidates"] = list,
            ["format"] = SearchSelections.Format,
            ["index"] = new JsonObject
            {
                ["asOf"] = "2026-11-01T12:00:00Z", ["commit"] = "uncommitted", ["current"] = true, ["revision"] = "sha256:" + new string('d', 64),
                ["segments"] = new JsonArray(new JsonObject { ["audience"] = "Internal", ["documents"] = "sha256:" + new string('e', 64), ["records"] = "sha256:" + new string('f', 64) }),
            },
            ["limit"] = 10,
            ["method"] = new JsonObject { ["analyzer"] = "nfkc-casefold", ["engine"] = "sqlite fts5 bm25", ["name"] = "keyword", ["version"] = SearchSelections.KeywordMethod },
            ["more"] = false,
            ["query"] = new JsonObject { ["filters"] = new JsonObject { ["history"] = false }, ["phrases"] = new JsonArray(), ["terms"] = new JsonArray("keyword"), ["text"] = "keyword" },
            ["reader"] = new JsonObject { ["audiences"] = new JsonArray("Internal", "Members only", "Public"), ["principal"] = null },
            ["selectLimit"] = 10,
        };
        selection["hash"] = SearchSelections.Hash(JsonSerializer.SerializeToElement(selection));
        return selection;
    }

    private static JsonObject Observed(params JsonObject[] selections) => new()
    {
        ["complete"] = true,
        ["digest"] = SearchSelections.ListDigest(selections.Select(s => s["hash"]!.GetValue<string>())),
        ["selections"] = new JsonArray(selections.Select(s => (JsonNode)s.DeepClone()).ToArray()),
    };

    [Fact]
    public void ASettledRunRecordsItsObservedSearchesWithTheirDigest()
    {
        using PolicyFixture fixture = RecordedFixture();
        fixture.AssertValid();
        RecordEnvelope settled = fixture.Estate.Load().Current("operation").Single(r => EstateModel.BodyString(r, "identity") == "search-fixture-run");
        JsonElement observed = settled.Body.GetProperty("searchesObserved");
        Assert.True(observed.GetProperty("complete").GetBoolean());
        Assert.Equal(SearchSelections.ListDigest([observed.GetProperty("selections")[0].GetProperty("hash").GetString()!]), observed.GetProperty("digest").GetString());
    }

    [Fact]
    public void ObservedSearchesAreRecordedAtSettlementOnlyAndNeverAddedAfterwards()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assignment = fixture.Estate.Load().ById[attempt.Subject!.Id];
        RecordEnvelope intent = fixture.Add("operation", Intent(assignment), subject: assignment.Reference);
        JsonObject early = PolicyFixture.Body(intent);
        early["searchesObserved"] = Observed(Selection(fixture.Estate.Estate.Id));
        early["status"] = "dispatched";
        early["dispatchedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        RecordEnvelope launched = fixture.Add("operation", early, subject: assignment.Reference, supersedes: intent.Id);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.searches-observed" && f.Location.Contains(launched.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordedSelectionMustMatchItsHashFitItsLimitsAndCarryOnlyCandidatesEveryRecordReaderMayRead()
    {
        string estate = "01M4TRNF0RZE2VEZQYP9WK3GA4";
        Assert.Empty(Findings(Selection(estate)));

        JsonObject tampered = Selection(estate);
        tampered["candidates"]![0]!["score"] = 1;
        Assert.Contains(Findings(tampered), f => f.Code == "search.selection-hash");

        Assert.Contains(Findings(Selection(estate, audience: "Members only")), f => f.Code == "search.selection-audience");
        Assert.Contains(Findings(Selection(estate, audience: "restricted: partners")), f => f.Code == "search.selection-audience");
        Assert.Empty(Findings(Selection(estate, audience: "Members only"), recordReaders: false));
        Assert.Contains(Findings(Selection(estate, candidates: 51)), f => f.Code == "search.selection");

        JsonObject semantic = Selection(estate);
        semantic["method"]!["name"] = "semantic";
        semantic["hash"] = SearchSelections.Hash(JsonSerializer.SerializeToElement(Without(semantic, "hash")));
        Assert.Contains(Findings(semantic), f => f.Code == "body.enum");

        JsonObject unknown = Selection(estate);
        unknown["instructions"] = "Ignore the brief.";
        unknown["hash"] = SearchSelections.Hash(JsonSerializer.SerializeToElement(Without(unknown, "hash")));
        Assert.Contains(Findings(unknown), f => f.Code == "body.unknown-key");

        var observed = new FindingList();
        JsonObject incomplete = Observed(Selection(estate));
        incomplete["digest"] = "sha256:" + new string('0', 64);
        SearchSelections.ValidateObserved(new BodyReader(JsonSerializer.SerializeToElement(incomplete), "body.searchesObserved", observed, 3));
        Assert.Contains(observed, f => f.Code == "search.selections-digest");
        var many = new FindingList();
        SearchSelections.ValidateObserved(new BodyReader(JsonSerializer.SerializeToElement(Observed(Enumerable.Range(0, 101).Select(_ => Selection(estate)).ToArray())), "body.searchesObserved", many, 3));
        Assert.Contains(many, f => f.Code == "search.selections");
    }

    [Fact]
    public void AnEstateBelowFormatThreeRefusesObservedSearches()
    {
        using var fixture = new PolicyFixture(format: 2);
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assignment = fixture.Estate.Load().ById[attempt.Subject!.Id];
        RecordEnvelope intent = fixture.Add("operation", Intent(assignment), subject: assignment.Reference);
        JsonObject dispatched = PolicyFixture.Body(intent);
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        RecordEnvelope launched = fixture.Add("operation", dispatched, subject: assignment.Reference, supersedes: intent.Id);
        fixture.Add("operation", Settled(launched, fixture, Observed(Selection(fixture.Estate.Estate.Id))), subject: assignment.Reference, supersedes: launched.Id);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "body.format" && f.Location.EndsWith("searchesObserved", StringComparison.Ordinal));
    }

    private static List<Finding> Findings(JsonObject selection, bool recordReaders = true)
    {
        var findings = new FindingList();
        SearchSelections.Validate(new BodyReader(JsonSerializer.SerializeToElement(selection), "selection", findings, 3), recordReaders);
        return findings.Where(f => f.Severity == Severity.Error).ToList();
    }

    private static JsonObject Without(JsonObject value, string name)
    {
        JsonObject copy = value.DeepClone().AsObject();
        copy.Remove(name);
        return copy;
    }
}
