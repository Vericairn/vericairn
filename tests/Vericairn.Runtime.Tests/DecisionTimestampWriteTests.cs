// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.DecisionTimestampHistoryTests;

namespace Vericairn.Runtime.Tests;

public sealed class DecisionTimestampWriteTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NewDecisionIsRefusedAgainstItsOwnRecordTimeInEveryFormat(long format)
    {
        using var estate = new TestEstate(format);
        using EstateService service = EstateService.Open(estate.Root);
        int before = service.Session.Load().Records.Count;
        WriteOutcome result = service.NewRecord(new("decision", estate.OperatorId, Body(estate.OperatorId, Later, format), At: RecordedAt));
        Assert.False(result.Ok);
        Assert.Null(result.Written);
        AssertTimeError(result.Problem!, Later, RecordedAt);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Empty(service.Records("decision"));
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Empty(reopened.Records("decision"));
    }

    [Theory]
    [InlineData(1, "2026-10-13T12:29:59Z")]
    [InlineData(1, RecordedAt)]
    [InlineData(2, "2026-10-13T12:29:59Z")]
    [InlineData(2, RecordedAt)]
    [InlineData(3, "2026-10-13T12:29:59Z")]
    [InlineData(3, RecordedAt)]
    public void NewDecisionsAllowEarlierAndEqualTimes(long format, string decidedAt)
    {
        using var estate = new TestEstate(format);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome result = service.NewRecord(new("decision", estate.OperatorId, Body(estate.OperatorId, decidedAt, format), At: RecordedAt));
        Assert.True(result.Ok, result.Problem?.Message);
        Assert.Equal(RecordedAt, service.Session.Load().ById[result.Written!.Id].At);
        Assert.DoesNotContain(service.Validate().Findings, f => f.Code == "decision.record-time");
    }

    [Fact]
    public void HistoricalWarningDoesNotBlockAValidWriteOrHideAnInvalidRevision()
    {
        using var estate = new TestEstate(3);
        estate.SetClock(RecordTime.AddSeconds(-1));
        RecordEnvelope previous = estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, Later).GetRawText());
        using EstateService service = EstateService.Open(estate.Root);

        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            FindingDto warning = Assert.Single(check.Findings, f => f.Code == "decision.record-time");
            Assert.Equal("warning", warning.Severity);
            Assert.Contains(RecordedAt, warning.Message, StringComparison.Ordinal);
            Assert.Contains(Later, warning.Message, StringComparison.Ordinal);
        }
        Assert.NotNull(service.Decision(previous.Id));
        Assert.Single(service.Records("decision"));
        WriteOutcome refused = service.NewRecord(new("decision", estate.OperatorId, Body(estate.OperatorId, "2026-10-13T12:30:02Z"),
            Supersedes: previous.Id, At: Later));
        Assert.False(refused.Ok);
        AssertTimeError(refused.Problem!, "2026-10-13T12:30:02Z", Later);
        Assert.Single(service.Records("decision"));

        WriteOutcome accepted = service.NewRecord(new("decision", estate.OperatorId, Body(estate.OperatorId, Later), Supersedes: previous.Id, At: Later));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        Assert.Equal(previous.ComputedHash, service.Session.Load().ById[previous.Id].ComputedHash);
        Assert.Equal(0, service.Validate().Errors);
        Assert.Equal("warning", Assert.Single(service.Validate().Findings, f => f.Code == "decision.record-time").Severity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidedDecisionUsesTheSharedFindingBeforeWritingAnyPin(bool defaultDecisionTime)
    {
        using var fixture = new DecisionFixture();
        string later = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
        DecisionNewRequest request = fixture.Request;
        request = defaultDecisionTime ? request with { AnsweredAt = later } : request with { DecidedAt = later };
        var before = fixture.Records();

        DecisionNewResponse result = fixture.Service.DecisionFromAsk(request);

        Assert.False(result.Ok);
        Assert.Null(result.Written);
        Assert.Empty(result.Pins);
        FindingDto finding = Assert.Single(result.Problem!.Findings, f => f.Code == "decision.record-time");
        Assert.Equal("error", finding.Severity);
        Assert.Contains(later, finding.Message, StringComparison.Ordinal);
        Assert.Matches("record's at [0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", finding.Message);
        Assert.Equal(before, fixture.Records());
        Assert.Equal("open", Asks.State(fixture.Service.Session.Load(), fixture.Ask).State);
    }

    public static FindingDto AssertTimeError(Problem problem, string decidedAt, string at)
    {
        FindingDto finding = Assert.Single(problem.Findings, f => f.Code == "decision.record-time");
        Assert.Equal("error", finding.Severity);
        Assert.Contains(decidedAt, finding.Message, StringComparison.Ordinal);
        Assert.Contains(at, finding.Message, StringComparison.Ordinal);
        return finding;
    }
}
