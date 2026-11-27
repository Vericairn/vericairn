// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

public sealed class SettledSubjectTests
{
    [Theory]
    [InlineData("answered")]
    [InlineData("withdrawn")]
    [InlineData("open")]
    public void StaleAsk(string state)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = NewOrder(estate);
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId,
            $$"""{"to":"{{estate.OperatorId}}","question":"Inspect?","recommendation":"Inspect.","consequence":"Work waits.","state":"open"}""", order.Reference);
        RecordEnvelope? settlement = null;
        if (state == "withdrawn")
        {
            JsonObject body = HistoryFixture.Body(ask);
            body["state"] = "withdrawn";
            settlement = ask = estate.Add("ask", ask.By, body.ToJsonString(), order.Reference, ask.Id);
        }
        else if (state == "answered")
        {
            settlement = estate.Add("decision", estate.OperatorId,
                $$"""{"title":"Inspection","statement":"Reviewed.","summary":"The work was reviewed.","synthesis":"Proceed.","effects":["Continue."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"{{ask.At}}","stake":"low","notAuthorized":[],"answers":["{{ask.Id}}"]}""");
        }
        AssertSubject(estate, ask, ReviseOrder(estate, order), settlement);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("observation")]
    [InlineData("open")]
    public void StaleOperatorRequest(string state)
    {
        using var history = new HistoryFixture();
        TestEstate estate = history.Estate;
        RecordEnvelope order = NewOrder(estate);
        RecordEnvelope request = estate.Add("operator-request", estate.BuilderId, OperatorRequestTests.Body(estate).ToJsonString(), order.Reference);
        RecordEnvelope? settlement = state switch
        {
            "close" => estate.Add("operator-request-close", estate.OperatorId, """{"outcome":"dismissed","reason":"No further action."}""", request.Reference, signer: history.OperatorSigner),
            "observation" => estate.Add("observation", estate.BuilderId, OperatorRequestTests.Observation(estate).ToJsonString(), request.Reference, signer: history.BuilderSigner),
            _ => null,
        };
        AssertSubject(estate, request, ReviseOrder(estate, order), settlement);
    }

    [Theory]
    [InlineData("observed-success")]
    [InlineData("known-failure")]
    [InlineData("canceled")]
    [InlineData("dispatched")]
    public void StaleRunOperation(string state)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = NewOrder(estate);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), order.Reference);
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId,
            $$"""{"provider":"generic","action":"agent-run","identity":"probe","intent":"Exercise attention.","target":{"assignment":"{{assignment.Id}}","principal":"{{estate.BuilderId}}","workspace":"fixture repository"},"status":"not-started","cap":{"wallMinutes":5},"evidence":[]}""", assignment.Reference);
        JsonObject body = HistoryFixture.Body(intent);
        RecordEnvelope previous = intent;
        if (state != "canceled")
        {
            body["status"] = "dispatched";
            body["dispatchedAt"] = intent.At;
            previous = estate.Add("operation", estate.OperatorId, body.ToJsonString(), assignment.Reference, intent.Id);
        }
        RecordEnvelope latest = previous;
        if (state != "dispatched")
        {
            body["status"] = state;
            if (state != "canceled")
            {
                body["observed"] = new JsonObject { ["summary"] = "The run exited.", ["at"] = previous.At };
                body["usage"] = new JsonObject { ["wallMinutes"] = 1, ["unreported"] = new JsonArray() };
            }
            latest = estate.Add("operation", estate.OperatorId, body.ToJsonString(), assignment.Reference, previous.Id);
        }
        RecordEnvelope revised = estate.Add("assignment", assignment.By, assignment.Body.GetRawText(), order.Reference, assignment.Id);
        AssertSubject(estate, latest, revised, state == "dispatched" ? null : latest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleAttemptOnAnEarlierAssignment(bool newerAttempt)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = NewOrder(estate);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), order.Reference);
        RecordEnvelope attempt = Start(estate, assignment);
        RecordEnvelope revised = estate.Add("assignment", assignment.By, assignment.Body.GetRawText(), order.Reference, assignment.Id);
        RecordEnvelope? settlement = newerAttempt ? Start(estate, revised) : null;
        AssertSubject(estate, attempt, revised, settlement);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleGoalStatus(bool accepted)
    {
        using var estate = new TestEstate(3);
        Work work = Work.Write(estate);
        const string active = """{"status":"active","note":"Activated."}""";
        RecordEnvelope status = estate.Add("goal-status", estate.OperatorId, active, work.Goal.Reference);
        RecordEnvelope revised = estate.Add("goal", estate.OperatorId, work.Goal.Body.GetRawText()
            .Replace("Ship the widget.", "Ship the revised widget.", StringComparison.Ordinal), supersedes: work.Goal.Id);
        estate.Add("goal-status", estate.OperatorId, active, revised.Reference);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), revised.Reference);
        RecordEnvelope? settlement = accepted ? estate.Add("acceptance", estate.OperatorId,
            $$"""{"statement":"Accepted.","basis":["{{evaluation.Id}}"],"self":true}""", revised.Reference) : null;
        AssertSubject(estate, status, revised, settlement);
    }

    [Fact]
    public void AGoalStatusIsSettledByTheCurrentAcceptanceNeverASupersededOne()
    {
        using var estate = new TestEstate(3);
        Work work = Work.Write(estate);
        const string active = """{"status":"active","note":"Activated."}""";
        RecordEnvelope status = estate.Add("goal-status", estate.OperatorId, active, work.Goal.Reference);
        RecordEnvelope revised = estate.Add("goal", estate.OperatorId, work.Goal.Body.GetRawText()
            .Replace("Ship the widget.", "Ship the revised widget.", StringComparison.Ordinal), supersedes: work.Goal.Id);
        estate.Add("goal-status", estate.OperatorId, active, revised.Reference);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), revised.Reference);
        string accepted = $$"""{"statement":"Accepted.","basis":["{{evaluation.Id}}"],"self":true}""";
        RecordEnvelope superseded = estate.Add("acceptance", estate.OperatorId, accepted, revised.Reference);
        RecordEnvelope current = estate.Add("acceptance", estate.OperatorId, accepted.Replace("Accepted.", "Accepted again.", StringComparison.Ordinal), revised.Reference, superseded.Id);
        AssertSubject(estate, status, revised, current);
        Assert.DoesNotContain(superseded.Id, Assert.Single(estate.Load().Findings, f => f.Location == status.ExpectedPath).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleAssignmentOnAcceptedWork(bool acceptedCurrent)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = NewOrder(estate);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), order.Reference);
        const string acceptance = """{"statement":"Accepted.","basis":[],"self":true}""";
        RecordEnvelope priorAcceptance = estate.Add("acceptance", estate.OperatorId, acceptance, order.Reference);
        RecordEnvelope revised = ReviseOrder(estate, order);
        RecordEnvelope? settlement = acceptedCurrent ? estate.Add("acceptance", estate.OperatorId, acceptance, revised.Reference, priorAcceptance.Id) : null;
        AssertSubject(estate, assignment, revised, settlement);
    }

    private static RecordEnvelope NewOrder(TestEstate estate) => estate.Add("work-order", estate.OperatorId, Order("W-1", string.Empty));

    private static RecordEnvelope ReviseOrder(TestEstate estate, RecordEnvelope order)
    {
        JsonObject body = HistoryFixture.Body(order);
        body["title"] = "Revised scope";
        return estate.Add("work-order", order.By, body.ToJsonString(), supersedes: order.Id);
    }

    private static RecordEnvelope Start(TestEstate estate, RecordEnvelope assignment) => estate.Add("attempt", estate.BuilderId,
        $$"""{"startedAt":"{{RecordParser.FormatTimestamp(estate.Clock)}}","workspace":"fixture repository","note":"Start work."}""", assignment.Reference);

    private static void AssertSubject(TestEstate estate, RecordEnvelope record, RecordEnvelope latest, RecordEnvelope? settlement)
    {
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Location == record.ExpectedPath && f.Code.StartsWith("record.subject-", StringComparison.Ordinal));
        if (settlement is null)
        {
            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal("record.subject-stale", finding.Code);
            Assert.Equal($"The subject has been superseded by {latest.Id}; this record refers to an earlier revision and needs reconsideration.", finding.Message);
        }
        else
        {
            Assert.Equal(Severity.Info, finding.Severity);
            Assert.Contains(settlement.Id, finding.Message, StringComparison.Ordinal);
        }
    }
}
