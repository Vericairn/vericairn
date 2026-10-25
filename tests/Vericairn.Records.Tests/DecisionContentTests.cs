// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Tests;

public sealed class DecisionContentTests
{
    [Theory]
    [InlineData("requested")]
    [InlineData("resolvedRevision")]
    [InlineData("reported")]
    [InlineData("reportedRevision")]
    [InlineData("qualification")]
    public void DerivesEveryIdentityFromRecordedFieldsWithoutACompiledNameList(string source)
    {
        using var estate = new TestEstate(3);
        string term = Guid.NewGuid().ToString("N");
        object facts = source switch
        {
            "requested" => new { action = "agent-run", execution = new { requested = new { model = term } } },
            "resolvedRevision" => new { action = "agent-run", execution = new { resolvedRevision = term } },
            "reported" => new { action = "agent-run", executionObserved = new { reports = new[] { new { model = term } } } },
            "reportedRevision" => new { action = "agent-run", executionObserved = new { reports = new[] { new { modelRevision = term } } } },
            "qualification" => new { evidence = new[] { new { id = estate.Add("observation", estate.OperatorId,
                JsonSerializer.Serialize(new { facts = new { model = term } })).Id } } },
            _ => throw new InvalidOperationException(),
        };
        // These tests isolate extraction from record validation; the runtime journey also exercises a valid run history.
        estate.Add(source == "qualification" ? "qualification" : "operation", estate.OperatorId, JsonSerializer.Serialize(facts));
        foreach (string field in new[] { "title", "statement", "summary", "synthesis", "effects", "notAuthorized", "obligations" })
        {
            var prose = JsonSerializer.SerializeToElement(new Dictionary<string, object> { [field] = field is "effects" or "notAuthorized"
                ? new[] { "Recorded (" + term.ToUpperInvariant() + ")." } : "Recorded (" + term.ToUpperInvariant() + ")." });
            Assert.Equal(field, DecisionContent.BlockedProseField(estate.Load(), prose));
        }
        Assert.Null(DecisionContent.BlockedProseField(estate.Load(), JsonSerializer.SerializeToElement(new { summary = "An ordinary decision." })));
    }

    [Fact]
    public void UnknownMarkersAndUnrelatedRecordFieldsDoNotBecomeBlockedTerms()
    {
        using var estate = new TestEstate(3);
        estate.Add("operation", estate.OperatorId, """{"action":"agent-run","execution":{"resolvedRevision":"unknown","requested":{"model":"provider-default"}}}""");
        string arbitrary = Guid.NewGuid().ToString("N");
        estate.Add("observation", estate.OperatorId, JsonSerializer.Serialize(new { facts = new { model = arbitrary } }));
        Assert.Null(DecisionContent.BlockedProseField(estate.Load(), JsonSerializer.SerializeToElement(new { summary = "unknown provider-default " + arbitrary })));
    }
}
