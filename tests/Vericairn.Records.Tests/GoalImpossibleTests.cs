// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// Impossible is the evaluator's verdict, confirmed by the sponsor (the estate's design/goals.md § 4, FIX-116): a status of impossible
/// without that verdict is reported, and an earlier one keeps its reading, since the runtime refuses only new ones.
/// </summary>
public class GoalImpossibleTests
{
    private const string Impossible = """{"status":"impossible","note":"The widget cannot be built."}""";

    [Fact]
    public void AnImpossibleStatusOnTheEvaluatorsVerdictIsNotReported()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", work.Goal.Reference);
        estate.Add("review", work.Reviewer.Id, Evaluation("fail", NotYet(), """{"verdict":"impossible","reason":"The guide was withdrawn.","read":[]}"""), work.Goal.Reference);
        RecordEnvelope ended = estate.Add("goal-status", estate.OperatorId, Impossible, work.Goal.Reference);

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Location.StartsWith(ended.ExpectedPath, StringComparison.Ordinal));
        Assert.Null(GoalRules.ImpossibleVerdictMissing(model, work.Goal, ended));
        Assert.Equal("impossible", GoalRules.Status(model, work.Goal));
    }

    [Fact]
    public void AnEarlierImpossibleStatusWithoutAVerdictKeepsItsReadingAndIsReportedAsInformation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", work.Goal.Reference);
        RecordEnvelope ended = estate.Add("goal-status", estate.OperatorId, Impossible, work.Goal.Reference);

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity != Severity.Info && f.Location.StartsWith(ended.ExpectedPath, StringComparison.Ordinal));
        Finding reported = Assert.Single(model.Findings, f => f.Code == "goal-status.verdict");
        Assert.Equal(Severity.Info, reported.Severity);
        Assert.Equal(ended.ExpectedPath + "#body.status", reported.Location);
        Assert.Contains($"No evaluation of goal {work.Goal.Id}'s exact revision is recorded", reported.Message, StringComparison.Ordinal);
        Assert.Contains($"vericairn goal evaluate {work.Goal.Id} --by <evaluator> --verdict <n>:impossible:<reason>", reported.Message, StringComparison.Ordinal);
        Assert.Contains("a status already in the estate keeps its reading", reported.Message, StringComparison.Ordinal);
        Assert.Equal("impossible", GoalRules.Status(model, work.Goal));
    }

    [Fact]
    public void AnEvaluationWithoutAnImpossibleVerdictOrByAStepsAuthorIsNoVerdict()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", work.Goal.Reference);
        RecordEnvelope notYet = estate.Add("review", work.Reviewer.Id, Evaluation("findings", Met(work.Result), NotYet()), work.Goal.Reference);
        // The builder authored the step, so its verdict of impossible is no evaluation of the goal.
        estate.Add("review", estate.BuilderId, Evaluation("fail", Met(work.Result), """{"verdict":"impossible","reason":"I give up.","read":[]}"""), work.Goal.Reference);
        RecordEnvelope ended = estate.Add("goal-status", estate.OperatorId, Impossible, work.Goal.Reference);

        EstateModel model = estate.Load();
        Finding reported = Assert.Single(model.Findings, f => f.Code == "goal-status.verdict");
        Assert.Equal(ended.ExpectedPath + "#body.status", reported.Location);
        Assert.Contains($"The current evaluation of goal {work.Goal.Id}, {notYet.Id}, judges no condition impossible (condition 1 met, condition 2 not-yet)", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AVerdictRecordedAfterTheStatusDoesNotStandBehindIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", work.Goal.Reference);
        RecordEnvelope ended = estate.Add("goal-status", estate.OperatorId, Impossible, work.Goal.Reference);
        estate.Add("review", work.Reviewer.Id, Evaluation("fail", NotYet(), """{"verdict":"impossible","reason":"The guide was withdrawn.","read":[]}"""), work.Goal.Reference);

        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "goal-status.verdict" && f.Location == ended.ExpectedPath + "#body.status");
        Assert.Null(GoalRules.ImpossibleVerdictMissing(model, work.Goal));
    }
}
