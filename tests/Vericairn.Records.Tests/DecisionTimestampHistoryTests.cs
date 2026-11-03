// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class DecisionTimestampHistoryTests
{
    public const string RecordedAt = "2026-10-13T12:30:00Z";
    public const string Later = "2026-10-13T12:30:01Z";
    public static readonly DateTimeOffset RecordTime = new(2026, 10, 13, 12, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingDecisionWarnsWithBothTimesWithoutChangingItsBytes(long format)
    {
        using var estate = new TestEstate(format);
        estate.SetClock(RecordTime.AddSeconds(-1));
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, Later, format).GetRawText());
        string path = Path.Combine(estate.Root, decision.ExpectedPath);
        byte[] before = File.ReadAllBytes(path);

        EstateModel loaded = estate.Load();

        Assert.False(loaded.Findings.HasErrors, string.Join("\n", loaded.Findings));
        Finding finding = Assert.Single(loaded.Findings, f => f.Code == "decision.record-time");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(decision.ExpectedPath + "#body.decidedAt", finding.Location);
        Assert.Contains(RecordedAt, finding.Message, StringComparison.Ordinal);
        Assert.Contains(Later, finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(decision.ComputedHash, loaded.ById[decision.Id].ComputedHash);
    }

    [Theory]
    [InlineData(1, "2026-10-13T12:29:59Z")]
    [InlineData(1, RecordedAt)]
    [InlineData(2, "2026-10-13T12:29:59Z")]
    [InlineData(2, RecordedAt)]
    [InlineData(3, "2026-10-13T12:29:59Z")]
    [InlineData(3, RecordedAt)]
    public void EarlierAndEqualTimesHaveNoChronologyFinding(long format, string decidedAt)
    {
        using var estate = new TestEstate(format);
        estate.SetClock(RecordTime.AddSeconds(-1));
        estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, decidedAt, format).GetRawText());
        EstateModel loaded = estate.Load();
        Assert.False(loaded.Findings.HasErrors, string.Join("\n", loaded.Findings));
        Assert.DoesNotContain(loaded.Findings, f => f.Code == "decision.record-time");
    }

    [Fact]
    public void SupersededDecisionsStillReportTheirOwnTimeAsAWarning()
    {
        using var estate = new TestEstate(3);
        estate.SetClock(RecordTime.AddSeconds(-1));
        string body = Body(estate.OperatorId, Later).GetRawText();
        RecordEnvelope previous = estate.Add("decision", estate.OperatorId, body);
        estate.Add("decision", estate.OperatorId, body, supersedes: previous.Id);
        EstateModel loaded = estate.Load();
        Assert.False(loaded.Findings.HasErrors, string.Join("\n", loaded.Findings));
        Finding finding = Assert.Single(loaded.Findings, f => f.Code == "decision.record-time");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(previous.ExpectedPath + "#body.decidedAt", finding.Location);
    }

    [Fact]
    public void MalformedTimesKeepTheirTimestampError()
    {
        using var estate = new TestEstate(3);
        estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, "tomorrow").GetRawText());
        EstateModel loaded = estate.Load();
        Assert.Contains(loaded.Findings, f => f.Severity == Severity.Error && f.Code == "body.timestamp");
        Assert.DoesNotContain(loaded.Findings, f => f.Code == "decision.record-time");
    }

    [Theory]
    [InlineData(Later, RecordedAt, true)]
    [InlineData(Later, Later, true)]
    [InlineData(RecordedAt, "2026-10-13T12:29:59Z", false)]
    [InlineData(RecordedAt, Later, false)]
    public void GuidedHistoryWarnsForRecordChronologyAndKeepsIndependentTimeErrors(string decidedAt, string answeredAt, bool warningOnly)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, JsonSerializer.Serialize(new
        {
            to = estate.OperatorId, question = "Proceed?", recommendation = "Proceed.",
            consequence = "Work waits.", state = "open",
        }));
        JsonObject body = JsonNode.Parse(Body(estate.OperatorId, decidedAt).GetRawText())!.AsObject();
        body["answers"] = new JsonArray(ask.Id);
        body["answer"] = new JsonObject { ["ask"] = ask.Id, ["hash"] = ask.ComputedHash, ["at"] = answeredAt };
        body["changeSet"] = new JsonObject
        {
            ["baseCommit"] = new string('a', 40), ["head"] = new string('a', 40), ["pins"] = new JsonArray(),
        };
        estate.SetClock(RecordTime.AddSeconds(-1));
        estate.Add("decision", estate.OperatorId, body.ToJsonString());

        EstateModel loaded = estate.Load();

        if (!warningOnly)
        {
            Assert.Contains(loaded.Findings, f => f.Code == "decision.time" && f.Severity == Severity.Error);
            Assert.DoesNotContain(loaded.Findings, f => f.Code == "decision.record-time");
            return;
        }
        Assert.False(loaded.Findings.HasErrors, string.Join("\n", loaded.Findings));
        Finding warning = Assert.Single(loaded.Findings, f => f.Code == "decision.record-time");
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Contains(RecordedAt, warning.Message, StringComparison.Ordinal);
        Assert.Contains(Later, warning.Message, StringComparison.Ordinal);
    }

    public static JsonElement Body(string decider, string decidedAt, long format = 3)
    {
        var body = new JsonObject
        {
            ["title"] = "Record an answer", ["statement"] = "Proceed with the work.",
            ["summary"] = "Proceed.", ["synthesis"] = "The operator approved the work.",
            ["effects"] = new JsonArray("Proceed with the work."), ["affects"] = new JsonArray(),
            ["decidedBy"] = decider, ["decidedAt"] = decidedAt,
        };
        if (format >= 2)
        {
            body["stake"] = "low";
            body["notAuthorized"] = new JsonArray();
        }
        return JsonSerializer.SerializeToElement(body);
    }
}
