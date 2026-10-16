// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class PersonObservationTests
{
    private static readonly string Hash = "sha256:" + new string('a', 64);

    [Fact]
    public void StatusStartsUnknownAndUsesTheLastPersonsEstateObservationNotAutomation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using (JsonDocument empty = JsonDocument.Parse(EstateStatus.RenderJson(estate.Load(), new FindingList())))
        {
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("lastObservedByPerson").ValueKind);
        }

        RecordEnvelope first = Observe(estate, estate.OperatorId, "2026-10-13T12:00:02Z");
        // A later record reporting an older observation does not advance freshness.
        Observe(estate, estate.OperatorId, "2026-10-13T12:00:01Z");
        estate.Add("observation", estate.BuilderId, """{"kind":"repository","asOf":"2026-10-13T12:00:05Z","summary":"Automation ran.","facts":{},"limitations":[]}""", estate.Estate.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        JsonElement observed = status.RootElement.GetProperty("lastObservedByPerson");
        Assert.Equal(first.Id, observed.GetProperty("record").GetString());
        Assert.Equal(first.ComputedHash, observed.GetProperty("hash").GetString());
        Assert.Equal("2026-10-13T12:00:02Z", observed.GetProperty("at").GetString());
        Assert.Equal(estate.OperatorId, observed.GetProperty("by").GetString());
        Assert.Contains("Last observed by a person: 2026-10-13T12:00:02Z", EstateStatus.Render(model, new FindingList()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("subject")]
    [InlineData("future")]
    [InlineData("format")]
    [InlineData("facts")]
    public void AnEstateObservationCannotInventAPersonASubjectOrFutureFreshness(string invalid)
    {
        using var estate = new TestEstate(recordFormat: invalid == "format" ? 1 : 2);
        Observe(estate, invalid == "agent" ? estate.BuilderId : estate.OperatorId,
            invalid == "future" ? "2099-01-01T00:00:00Z" : "2026-10-13T12:00:02Z",
            invalid == "subject" ? estate.Builder.Reference : estate.Estate.Reference,
            invalid == "facts" ? "{}" : null);
        Assert.True(estate.Load().Findings.HasErrors);
    }

    private static RecordEnvelope Observe(TestEstate estate, string by, string asOf, RecordReference? subject = null, string? facts = null) =>
        estate.Add("observation", by, $$$"""{"kind":"estate","asOf":"{{{asOf}}}","summary":"A person observed status.","facts":{{{facts ?? $$$"""{"records":3,"recordSetHash":"{{{Hash}}}","pinHash":"{{{Hash}}}","statusHash":"{{{Hash}}}"}"""}}},"limitations":["Not live health."]}""", subject ?? estate.Estate.Reference);
}
