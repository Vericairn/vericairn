// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>Public retrieval in records (RUN-04): a brief that permits it, a run's execution that admits it, and the sources it retained.</summary>
public sealed class RetrievalRecordTests
{
    private static readonly string[] LocalOnly = ["local"];
    private static readonly string[] PublicWeb = ["public-web"];

    /// <summary>A settled retrieval run whose assignment's brief permits retrieval, with two recorded sources.</summary>
    public static PolicyFixture RecordedFixture()
    {
        var fixture = new PolicyFixture();
        (RecordEnvelope assignment, _) = Permitting(fixture);
        RecordEnvelope intent = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: true), "retrieval-fixture-run"), subject: assignment.Reference);
        Settle(fixture, assignment, intent, settled => settled["sourcesObserved"] = Sources());
        return fixture;
    }

    private static (RecordEnvelope Assignment, RecordEnvelope Original) Permitting(PolicyFixture fixture, bool permit = true)
    {
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope original = fixture.Estate.Load().ById[attempt.Subject!.Id];
        if (!permit) return (original, original);
        JsonObject body = PolicyFixture.Body(original);
        body["brief"]!["retrieval"] = "public";
        RecordEnvelope revised = fixture.Add("assignment", body, subject: original.Subject, supersedes: original.Id);
        return (revised, original);
    }

    private static JsonObject Execution(bool retrieval, bool webTool = true)
    {
        JsonObject execution = webTool
            ? RunReachRecordTests.Execution(("stand-in", "harness", LocalOnly), ("WebFetch", "tool", PublicWeb))
            : RunReachRecordTests.Execution();
        if (retrieval) execution["retrieval"] = "public";
        return execution;
    }

    // The intent's dispatch, then its settlement as the change makes it; returns the settlement.
    private static RecordEnvelope Settle(PolicyFixture fixture, RecordEnvelope assignment, RecordEnvelope intent, Action<JsonObject>? change = null)
    {
        JsonObject dispatched = PolicyFixture.Body(intent);
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        RecordEnvelope launched = fixture.Add("operation", dispatched, subject: assignment.Reference, supersedes: intent.Id);
        JsonObject settled = PolicyFixture.Body(launched);
        settled["status"] = "observed-success";
        settled["observed"] = new JsonObject { ["summary"] = "The run exited.", ["at"] = RecordParser.FormatTimestamp(fixture.Estate.Clock) };
        settled["usage"] = new JsonObject { ["wallMinutes"] = 1, ["unreported"] = new JsonArray() };
        settled["executionObserved"] = new JsonObject { ["reports"] = new JsonArray(), ["unknown"] = new JsonArray("model"), ["discrepancies"] = new JsonArray() };
        change?.Invoke(settled);
        return fixture.Add("operation", settled, subject: assignment.Reference, supersedes: launched.Id);
    }

    private static JsonObject Sources(params JsonObject[] sources) => new()
    {
        ["sources"] = new JsonArray([.. (sources.Length == 0 ? [Source(), Source(query: "widget calibration", retained: false)] : sources).Select(s => (JsonNode)s)]),
        ["count"] = sources.Length == 0 ? 2 : sources.Length,
        ["complete"] = true,
        ["digest"] = "sha256:" + new string('e', 64),
    };

    private static JsonObject Source(string? address = "https://example.org/guide", string? query = null, bool retained = true, bool hashed = true)
    {
        var source = new JsonObject { ["tool"] = query is null ? "WebFetch" : "WebSearch" };
        if (query is not null) source["query"] = query;
        else if (address is not null) source["address"] = address;
        else source["withheld"] = true;
        source["at"] = "2026-11-17T12:00:00Z";
        source["retained"] = retained;
        if (hashed && (retained || query is null))
        {
            source["hash"] = "sha256:" + new string('f', 64);
            source["bytes"] = 120;
        }

        return source;
    }

    private static List<Finding> Errors(PolicyFixture fixture, RecordEnvelope record) =>
        [.. fixture.Estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location.Contains(record.Id, StringComparison.Ordinal))];

    [Fact]
    public void ARetrievalRunWhoseBriefPermitsItRecordsItsSources()
    {
        using PolicyFixture fixture = RecordedFixture();
        fixture.AssertValid();
    }

    [Theory]
    [InlineData(3, "public", null)]
    [InlineData(3, "private", "body.enum")]
    [InlineData(3, "unrestricted", "body.enum")]
    [InlineData(2, "public", "body.format")]
    public void ABriefPermitsOnlyPublicRetrievalFromFormatThree(int format, string value, string? code)
    {
        using var fixture = new PolicyFixture(format);
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope original = fixture.Estate.Load().ById[attempt.Subject!.Id];
        JsonObject body = PolicyFixture.Body(original);
        body["brief"]!["retrieval"] = value;
        RecordEnvelope revised = fixture.Add("assignment", body, subject: original.Subject, supersedes: original.Id);
        List<Finding> errors = Errors(fixture, revised);
        if (code is null) Assert.Empty(errors);
        else Assert.Contains(errors, f => f.Code == code);
        Assert.Equal(code is null, AssignmentType.PermitsRetrieval(fixture.Estate.Load().ById[revised.Id]));
    }

    [Fact]
    public void ARunAdmitsRetrievalOnlyWhenTheAssignmentRevisionItRunsPermitsIt()
    {
        using var fixture = new PolicyFixture();
        (RecordEnvelope assignment, _) = Permitting(fixture, permit: false);
        RecordEnvelope intent = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: true)), subject: assignment.Reference);
        Assert.Contains(Errors(fixture, intent), f => f.Code == "operation.retrieval");
    }

    [Fact]
    public void AWebToolsPublicRouteNeedsRetrieval()
    {
        using var fixture = new PolicyFixture();
        (RecordEnvelope assignment, _) = Permitting(fixture);
        RecordEnvelope unadmitted = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: false)), subject: assignment.Reference);
        Assert.Contains(Errors(fixture, unadmitted), f => f.Code == "operation.execution");
        RecordEnvelope admitted = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: true), "second-run"), subject: assignment.Reference);
        Assert.Empty(Errors(fixture, admitted));
    }

    [Theory]
    [InlineData("both")]
    [InlineData("neither")]
    [InlineData("credential-address")]
    [InlineData("file-address")]
    [InlineData("retained-without-hash")]
    [InlineData("hash-without-bytes")]
    [InlineData("count-below-list")]
    [InlineData("unknown-field")]
    public void EachRecordedSourceNamesOneAddressQueryOrWithholdingAndItsContentIdentity(string defect)
    {
        using var fixture = new PolicyFixture();
        (RecordEnvelope assignment, _) = Permitting(fixture);
        RecordEnvelope intent = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: true)), subject: assignment.Reference);
        JsonObject source = Source();
        JsonObject sources = Sources(source);
        switch (defect)
        {
            case "both": source["query"] = "also a query"; break;
            case "neither": source.Remove("address"); break;
            case "credential-address": source["address"] = "https://user:secret@example.org/"; break;
            case "file-address": source["address"] = "file:///etc/hosts"; break;
            case "retained-without-hash": source.Remove("hash"); source.Remove("bytes"); break;
            case "hash-without-bytes": source.Remove("bytes"); break;
            case "count-below-list": sources["count"] = 0; break;
            case "unknown-field": source["content"] = "page text"; break;
        }

        RecordEnvelope recorded = Settle(fixture, assignment, intent, settled => settled["sourcesObserved"] = sources);
        Assert.Contains(Errors(fixture, recorded), f => f.Code is "operation.sources-observed" or "body.range" or "body.unknown-key");
    }

    [Fact]
    public void SourcesBelongToASettledRetrievalRunAndAreRecordedAtSettlementOnly()
    {
        using var fixture = new PolicyFixture();
        (RecordEnvelope assignment, _) = Permitting(fixture);
        // A run whose execution admitted no retrieval records no sources.
        RecordEnvelope plain = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: false, webTool: false), "plain-run"), subject: assignment.Reference);
        RecordEnvelope plainRecorded = Settle(fixture, assignment, plain, settled => settled["sourcesObserved"] = Sources());
        Assert.Contains(Errors(fixture, plainRecorded), f => f.Code == "operation.sources-observed");

        // A settled retrieval run cannot add sources in a later revision.
        RecordEnvelope intent = fixture.Add("operation", RunReachRecordTests.Intent(assignment, Execution(retrieval: true)), subject: assignment.Reference);
        RecordEnvelope settled = Settle(fixture, assignment, intent);
        JsonObject later = PolicyFixture.Body(settled);
        later["sourcesObserved"] = Sources();
        RecordEnvelope added = fixture.Add("operation", later, subject: assignment.Reference, supersedes: settled.Id);
        Assert.Contains(Errors(fixture, added), f => f.Code == "operation.sources-observed");
    }
}
