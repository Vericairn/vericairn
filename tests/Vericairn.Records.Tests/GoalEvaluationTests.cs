// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The evaluation of a goal (the estate's design/goals.md § 5): a review of its exact revision, condition by condition, with the
/// records each verdict read, by a principal other than any step's author.
/// </summary>
public class GoalEvaluationTests
{
    [Fact]
    public void AnEvaluationGivesAVerdictPerConditionWithTheRecordsItRead()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(evaluation.Id, GoalRules.CurrentEvaluation(model, work.Goal)?.Id);
        Assert.Equal(["met", "met"], GoalRules.Verdicts(evaluation));
    }

    [Fact]
    public void NoAuthorOfAStepEvaluatesTheGoal()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);

        // The builder holds the assignment and recorded the attempt and the result; the operator only assigned the work.
        RecordEnvelope byBuilder = estate.Add("review", estate.BuilderId, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope byOperator = estate.Add("review", estate.OperatorId, Evaluation("pass", Met(work.Result), Met(work.Result), independence: "self"), work.Goal.Reference);

        // A contributor delegated to who has not started is a step's author already; so is anyone who recorded an attempt under it.
        RecordEnvelope helper = estate.Add("principal", estate.OperatorId, """{"kind":"agent","name":"Helper","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        estate.Add("assignment", estate.OperatorId, Assignment(helper.Id), work.Order.Reference);
        RecordEnvelope byHelper = estate.Add("review", helper.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);

        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "review.evaluator" && f.Location == byBuilder.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "review.evaluator" && f.Location == byHelper.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location == byOperator.ExpectedPath);
    }

    [Fact]
    public void TheGoalsWorkIsWhatItsPlanGivesAndWhatNamesIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope given = estate.Add("work-order", estate.OperatorId, Order("W-9", string.Empty));
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, Goal(estate, $$"""{"policy":"given","approval":"sponsor-approves","workOrders":["{{given.Id}}"]}"""));
        RecordEnvelope naming = estate.Add("work-order", estate.OperatorId, Order("W-1", $$"""{"type":"goal","id":"{{goal.Id}}"}"""));
        estate.Add("work-order", estate.OperatorId, Order("W-2", string.Empty));
        estate.Add("assignment", estate.OperatorId, Assignment(estate.BuilderId), given.Reference);

        EstateModel model = estate.Load();
        Assert.Equal([naming.Id, given.Id], GoalRules.Work(model, goal).Select(o => o.Id));
        Assert.Equal([estate.BuilderId], GoalRules.StepAuthors(model, goal));
    }

    [Fact]
    public void AnEvaluationGivesOneVerdictPerConditionAndItsOutcomeFollowsThem()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        string reviewer = work.Reviewer.Id;
        RecordEnvelope missing = estate.Add("review", reviewer, """{"independence":"independent-agent","outcome":"pass","summary":"Read it.","findings":[]}""", work.Goal.Reference);
        RecordEnvelope shortOne = estate.Add("review", reviewer, Evaluation("pass", Met(work.Result)), work.Goal.Reference);
        RecordEnvelope passing = estate.Add("review", reviewer, Evaluation("pass", Met(work.Result), NotYet()), work.Goal.Reference);
        RecordEnvelope failing = estate.Add("review", reviewer, Evaluation("findings", NotYet(), """{"verdict":"impossible","reason":"The guide was withdrawn.","read":[]}"""), work.Goal.Reference);
        RecordEnvelope unread = estate.Add("review", reviewer, Evaluation("pass", Met(work.Result), """{"verdict":"met","reason":"I believe it.","read":[]}"""), work.Goal.Reference);
        RecordEnvelope stale = estate.Add("review", reviewer, Evaluation("pass", Met(work.Result), $$"""{"verdict":"met","reason":"Read it.","read":[{"type":"result","id":"{{work.Result.Id}}","hash":"sha256:{{new string('2', 64)}}"}]}"""), work.Goal.Reference);
        RecordEnvelope notGoal = estate.Add("review", reviewer, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Result.Reference);

        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "review.conditions" && f.Location == missing.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "review.conditions" && f.Location == shortOne.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "review.outcome" && f.Location == passing.ExpectedPath && f.Message.Contains("findings, not pass", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "review.outcome" && f.Location == failing.ExpectedPath && f.Message.Contains("fail, not findings", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "review.read" && f.Location == unread.ExpectedPath + "#body.conditions[1].read");
        Assert.Contains(findings, f => f.Code == "review.read" && f.Location == stale.ExpectedPath + "#body.conditions[1].read[0].hash");
        Assert.Contains(findings, f => f.Code == "review.conditions" && f.Location == notGoal.ExpectedPath);
    }

    [Fact]
    public void TheCurrentEvaluationIsTheLatestOfTheExactRevision()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        RecordEnvelope first = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope second = estate.Add("review", work.Reviewer.Id, Evaluation("findings", Met(work.Result), NotYet()), work.Goal.Reference);
        Assert.Equal(second.Id, GoalRules.CurrentEvaluation(estate.Load(), work.Goal)?.Id);
        Assert.Equal(first.Id, GoalRules.CurrentEvaluation(estate.Load(), work.Goal, second)?.Id);

        // A revision of the goal is evaluated afresh: the earlier revision's evaluations are not its own.
        RecordEnvelope revised = estate.Add("goal", estate.OperatorId, work.Goal.Body.GetRawText().Replace("Ship the widget.", "Ship the widget, revised.", StringComparison.Ordinal), supersedes: work.Goal.Id);
        Assert.Null(GoalRules.CurrentEvaluation(estate.Load(), revised));
    }

    [Fact]
    public void AStepAuthoredAfterAnEvaluationLeavesItStanding()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        estate.Add("assignment", estate.OperatorId, Assignment(work.Reviewer.Id), work.Order.Reference);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location == evaluation.ExpectedPath);
    }

    [Fact]
    public void AWorkOrderRevisedAfterAnEvaluationKeepsTheAuthorsOfItsSteps()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        RecordEnvelope byBuilder = estate.Add("review", estate.BuilderId, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope revised = estate.Add("work-order", estate.OperatorId, work.Order.Body.GetRawText().Replace("\"Work.\"", "\"Work, revised.\"", StringComparison.Ordinal), supersedes: work.Order.Id);

        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "review.evaluator" && f.Location == byBuilder.ExpectedPath);
        Assert.Equal([revised.Id], GoalRules.Work(model, work.Goal, byBuilder).Select(o => o.Id));
        Assert.Null(GoalRules.CurrentEvaluation(model, work.Goal));
    }

    internal static string Evaluation(string outcome, params string[] conditions) => Evaluation(outcome, conditions, "independent-agent");

    internal static string Evaluation(string outcome, string first, string second, string independence) => Evaluation(outcome, [first, second], independence);

    internal static string Met(RecordEnvelope read) =>
        $$"""{"verdict":"met","reason":"Read it.","read":[{"type":"{{read.Type}}","id":"{{read.Id}}","hash":"{{read.ComputedHash}}"}]}""";

    internal static string NotYet() => """{"verdict":"not-yet","reason":"Not yet shown.","read":[]}""";

    internal static string Assignment(string contributor) =>
        $$$"""{"contributor":"{{{contributor}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["tests pass"],"recovery":"Record a result before stopping."}}""";

    internal static string Order(string key, string input) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{{input}}],"deliverables":["work"]}""";

    internal static string Goal(TestEstate estate, string plan = """{"policy":"proposed-by-planner","approval":"sponsor-approves"}""", string? sponsor = null, string? delegated = null) =>
        """{"title":"Release the widget","objective":"Ship the widget.","completion":[{"record":{"type":"work-order","fields":{"key":"W-1"}}},{"evidence":"The guide describes the widget.","check":"An evaluator reads the guide."}],"constraints":[],"kind":"finite","sponsor":"@SPONSOR@"@DELEGATE@,"budget":{"wallMinutes":600,"attemptsPerStep":3},"plan":@PLAN@,"inputs":[]}"""
            .Replace("@SPONSOR@", sponsor ?? estate.OperatorId, StringComparison.Ordinal).Replace("@PLAN@", plan, StringComparison.Ordinal)
            .Replace("@DELEGATE@", delegated is null ? string.Empty : $",\"delegate\":\"{delegated}\"", StringComparison.Ordinal);

    private static string Evaluation(string outcome, string[] conditions, string independence) =>
        $$"""{"independence":"{{independence}}","outcome":"{{outcome}}","summary":"Read the records the conditions name.","findings":[],"conditions":[{{string.Join(",", conditions)}}]}""";

    /// <summary>A goal its operator sponsors, the work order that names it, the builder's assignment, attempt and result, and a reviewer.</summary>
    internal sealed record Work(RecordEnvelope Goal, RecordEnvelope Order, RecordEnvelope Assignment, RecordEnvelope Attempt, RecordEnvelope Result, RecordEnvelope Reviewer)
    {
        public static Work Write(TestEstate estate, string? sponsor = null, string? delegated = null)
        {
            RecordEnvelope reviewer = estate.Add("principal", estate.OperatorId, """{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Reviewer"}}""");
            RecordEnvelope goal = estate.Add("goal", estate.OperatorId, GoalEvaluationTests.Goal(estate, sponsor: sponsor, delegated: delegated));
            RecordEnvelope order = estate.Add("work-order", estate.OperatorId, GoalEvaluationTests.Order("W-1", $$"""{"type":"goal","id":"{{goal.Id}}"}"""));
            RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, GoalEvaluationTests.Assignment(estate.BuilderId), order.Reference);
            RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository, work branch","note":"Build the widget."}""", assignment.Reference);
            RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Built the widget and its guide.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
            return new Work(goal, order, assignment, attempt, result, reviewer);
        }
    }
}
