// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// Activation and acceptance of a goal (the estate's design/goals.md §§ 3 to 5): its sponsor, a person, activates it within the
/// sponsor's authority to spend, and its sponsor accepts it when the sponsor is a person, an operator when an agent sponsors it,
/// on a current evaluation that finds every condition met, never its evaluator or the author of a step.
/// </summary>
public class GoalAcceptanceTests
{
    [Fact]
    public void ActivationIsTheSponsorsAPersonsAndWithinTheAuthorityToSpend()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Sponsor","roles":["contributor"]}""");
        RecordEnvelope personal = estate.Add("goal", estate.OperatorId, Goal(estate, sponsor: person.Id));
        RecordEnvelope agents = estate.Add("goal", estate.BuilderId, Goal(estate, sponsor: estate.BuilderId));
        RecordEnvelope operators = estate.Add("goal", estate.BuilderId, Goal(estate));

        RecordEnvelope unfunded = estate.Add("goal-status", person.Id, Status("active"), personal.Reference);
        RecordEnvelope byAgent = estate.Add("goal-status", estate.BuilderId, Status("active"), agents.Reference);
        RecordEnvelope byOther = estate.Add("goal-status", person.Id, Status("active"), operators.Reference);
        RecordEnvelope forAgent = estate.Add("goal-status", estate.OperatorId, Status("active"), agents.Reference);
        RecordEnvelope byOperator = estate.Add("goal-status", estate.OperatorId, Status("active"), operators.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "goal-status.authority" && f.Location == unfunded.ExpectedPath);

        // A grant of the spending scope lets a person who is not an operator fund the goals they sponsor, while it is in force; as
        // for every grant, one recorded later authorizes what came before it, and only its expiry is judged at each record's time.
        RecordEnvelope basis = estate.Add("decision", estate.BuilderId, $$"""{"title":"Spending","statement":"Fund your goals.","summary":"The sponsor may fund goals.","synthesis":"A grant to spend.","effects":["spending"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[]}""");
        string expires = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(10));
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{person.Id}}","scope":["spending"],"reserved":[],"conditions":[],"basis":"{{basis.Id}}","expires":"{{expires}}"}""");
        RecordEnvelope funded = estate.Add("goal-status", person.Id, Status("active"), personal.Reference);
        estate.Add("goal-status", person.Id, Status("paused"), personal.Reference);
        estate.SetClock(estate.Clock.AddMinutes(1));
        RecordEnvelope expired = estate.Add("goal-status", person.Id, Status("active"), personal.Reference);

        // An agent never activates a goal, even one it sponsors; an operator activates a goal an agent sponsors (decision 01M507XV4VBFYWMRN8WCXMXHX4).
        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "goal-status.operator" && f.Location == byAgent.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "goal-status.sponsor" && f.Location == byOther.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "goal-status.authority" && f.Location == expired.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location == byOperator.ExpectedPath || f.Location == forAgent.ExpectedPath || f.Location == funded.ExpectedPath || f.Location == unfunded.ExpectedPath);
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void ThePersonWhoSponsorsAGoalAcceptsItOnACurrentEvaluationThatFindsEveryConditionMet()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        RecordEnvelope early = estate.Add("acceptance", estate.OperatorId, Accept([]), work.Goal.Reference);
        estate.Add("goal-status", estate.OperatorId, Status("active"), work.Goal.Reference);
        RecordEnvelope unevaluated = estate.Add("acceptance", estate.OperatorId, Accept([]), work.Goal.Reference);
        RecordEnvelope partly = estate.Add("review", work.Reviewer.Id, Evaluation("findings", Met(work.Result), NotYet()), work.Goal.Reference);
        RecordEnvelope onPartly = estate.Add("acceptance", estate.OperatorId, Accept([partly.Id]), work.Goal.Reference);
        Assert.Equal("active", GoalRules.Status(estate.Load(), work.Goal));

        // An evaluation a step's author wrote around the runtime is refused, and it is never the current evaluation.
        RecordEnvelope byStepAuthor = estate.Add("review", estate.BuilderId, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope onStepAuthors = estate.Add("acceptance", estate.OperatorId, Accept([byStepAuthor.Id], self: true), work.Goal.Reference);

        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope unnamed = estate.Add("acceptance", estate.OperatorId, Accept([partly.Id]), work.Goal.Reference);
        RecordEnvelope byEvaluator = estate.Add("acceptance", work.Reviewer.Id, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope byWorker = estate.Add("acceptance", estate.BuilderId, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope second = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Second Operator","roles":["operator"]}""");
        RecordEnvelope byAnotherOperator = estate.Add("acceptance", second.Id, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope accepted = estate.Add("acceptance", estate.OperatorId, Accept([evaluation.Id], self: true), work.Goal.Reference);

        EstateModel model = estate.Load();
        List<Finding> findings = model.Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "acceptance.activation" && f.Location == early.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.evaluation" && f.Location == unevaluated.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.evaluation" && f.Location == onPartly.ExpectedPath && f.Message.Contains("condition 2 is not-yet", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "review.evaluator" && f.Location == byStepAuthor.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.evaluation" && f.Location == onStepAuthors.ExpectedPath && f.Message.Contains(partly.Id, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.evaluation" && f.Location == unnamed.ExpectedPath && f.Message.Contains(evaluation.Id, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.independence" && f.Location == byEvaluator.ExpectedPath && f.Message.Contains("evaluator", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.policy" && f.Location == byEvaluator.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.independence" && f.Location == byWorker.ExpectedPath && f.Message.Contains("steps", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.policy" && f.Location == byAnotherOperator.ExpectedPath && f.Message.Contains("its sponsor", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Location == accepted.ExpectedPath);
        Assert.Equal("achieved", GoalRules.Status(model, work.Goal));
        Assert.Equal(accepted.Id, GoalRules.Achievement(model, work.Goal)?.Id);
    }

    [Fact]
    public void ALaterEvaluationThatFindsAConditionUnmetAndAnEndedGoalRefuseAcceptance()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate);
        estate.Add("goal-status", estate.OperatorId, Status("active"), work.Goal.Reference);
        RecordEnvelope first = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope second = estate.Add("review", work.Reviewer.Id, Evaluation("fail", Met(work.Result), """{"verdict":"impossible","reason":"The guide was withdrawn.","read":[]}"""), work.Goal.Reference);
        RecordEnvelope onFirst = estate.Add("acceptance", estate.OperatorId, Accept([first.Id, second.Id], self: true), work.Goal.Reference);
        RecordEnvelope third = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        estate.Add("goal-status", estate.OperatorId, Status("abandoned"), work.Goal.Reference);
        RecordEnvelope afterEnd = estate.Add("acceptance", estate.OperatorId, Accept([third.Id], self: true), work.Goal.Reference);

        EstateModel model = estate.Load();
        List<Finding> findings = model.Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "acceptance.evaluation" && f.Location == onFirst.ExpectedPath && f.Message.Contains("condition 2 is impossible", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.activation" && f.Location == afterEnd.ExpectedPath && f.Message.Contains("abandoned", StringComparison.Ordinal));
        Assert.Equal("abandoned", GoalRules.Status(model, work.Goal));
    }

    [Fact]
    public void AGoalAnAgentSponsorsIsActivatedAndAcceptedByAnOperatorOnly()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Work work = Work.Write(estate, sponsor: estate.BuilderId);
        EstateModel model = estate.Load();
        GoalRules.Accepter accepter = GoalRules.AccepterOf(model, work.Goal);
        Assert.Null(accepter.Principal);
        Assert.Equal("an operator, and only an operator, since its sponsor is an agent", accepter.Description);
        Assert.True(accepter.Allows(model, estate.OperatorId, DateTimeOffset.UtcNow));
        Assert.False(accepter.Allows(model, estate.BuilderId, DateTimeOffset.UtcNow));
        RecordEnvelope personal = estate.Add("goal", estate.BuilderId, Goal(estate));
        Assert.Equal(estate.OperatorId, GoalRules.AccepterOf(estate.Load(), personal).Principal);

        RecordEnvelope byAgent = estate.Add("goal-status", estate.BuilderId, Status("active"), work.Goal.Reference);
        RecordEnvelope early = estate.Add("acceptance", estate.OperatorId, Accept([], self: true), work.Goal.Reference);
        estate.Add("goal-status", estate.OperatorId, Status("active"), work.Goal.Reference);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope bySponsor = estate.Add("acceptance", estate.BuilderId, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope person = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Contributor","roles":["contributor"]}""");
        RecordEnvelope byPerson = estate.Add("acceptance", person.Id, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope byOperator = estate.Add("acceptance", estate.OperatorId, Accept([evaluation.Id], self: true), work.Goal.Reference);

        model = estate.Load();
        List<Finding> findings = model.Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "goal-status.operator" && f.Location == byAgent.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.activation" && f.Location == early.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "acceptance.policy" && f.Location == bySponsor.ExpectedPath && f.Message.Contains("only an operator", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "acceptance.policy" && f.Location == byPerson.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location == byOperator.ExpectedPath);
        Assert.Equal(byOperator.Id, GoalRules.Achievement(model, work.Goal)?.Id);
        Assert.Equal("achieved", GoalRules.Status(model, work.Goal));
    }

    [Fact]
    public void ThePersonWhoSponsorsAnOrdinaryGoalMayDelegateItsAcceptanceAndNeverForAReservedAction()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope delegated = estate.Add("principal", estate.OperatorId, """{"kind":"agent","name":"Acceptor","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Acceptor"}}""");
        RecordEnvelope basis = estate.Add("decision", estate.BuilderId, $$"""{"title":"Accepting work","statement":"Accept others' ordinary work.","summary":"Accept ordinary work.","synthesis":"A grant to accept.","effects":["accept-work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{delegated.Id}}","scope":["accept-work"],"reserved":[],"conditions":[],"basis":"{{basis.Id}}"}""");
        RecordEnvelope stranger = estate.Add("principal", estate.OperatorId, """{"kind":"agent","name":"Stranger","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        Work work = Work.Write(estate, delegated: delegated.Id);
        Assert.Equal(delegated.Id, GoalRules.AccepterOf(estate.Load(), work.Goal).Delegate);
        estate.Add("goal-status", estate.OperatorId, Status("active"), work.Goal.Reference);
        RecordEnvelope evaluation = estate.Add("review", work.Reviewer.Id, Evaluation("pass", Met(work.Result), Met(work.Result)), work.Goal.Reference);
        RecordEnvelope byStranger = estate.Add("acceptance", stranger.Id, Accept([evaluation.Id]), work.Goal.Reference);
        RecordEnvelope byDelegate = estate.Add("acceptance", delegated.Id, Accept([evaluation.Id]), work.Goal.Reference);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "acceptance.policy" && f.Location == byStranger.ExpectedPath && f.Message.Contains("to whom the sponsor delegated it", StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Location == byDelegate.ExpectedPath);
        Assert.Equal(byDelegate.Id, GoalRules.Achievement(model, work.Goal)?.Id);

        // Never the sponsor itself, never for a goal an agent sponsors, never for a goal that includes a reserved action.
        RecordEnvelope product = estate.Add("product", estate.OperatorId, """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        RecordEnvelope itself = estate.Add("goal", estate.OperatorId, Goal(estate, delegated: estate.OperatorId));
        RecordEnvelope agents = estate.Add("goal", estate.BuilderId, Goal(estate, sponsor: estate.BuilderId, delegated: delegated.Id));
        RecordEnvelope released = estate.Add("goal", estate.OperatorId, GoalReleasing(estate, product.Id, delegated.Id));
        RecordEnvelope milestone = estate.Add("work-order", estate.OperatorId, """{"key":"M-1","kind":"milestone","title":"Milestone","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""");
        RecordEnvelope given = estate.Add("goal", estate.OperatorId, Goal(estate, plan: $$"""{"policy":"given","approval":"sponsor-approves","workOrders":["{{milestone.Id}}"]}""", delegated: delegated.Id));
        List<Finding> findings = estate.Load().Findings.Where(f => f.Code == "goal.delegate").ToList();
        Assert.Contains(findings, f => f.Location == itself.ExpectedPath && f.Message.Contains("another principal", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == agents.ExpectedPath && f.Message.Contains("only an operator", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == released.ExpectedPath && f.Message.Contains("about a release", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Location == given.ExpectedPath && f.Message.Contains("M-1 is a milestone", StringComparison.Ordinal));

        // A milestone that joins an ordinary goal's work withdraws the delegation from then on.
        using var later = new TestEstate(recordFormat: 2);
        RecordEnvelope laterDelegate = later.Add("principal", later.OperatorId, """{"kind":"agent","name":"Acceptor","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Acceptor"}}""");
        RecordEnvelope laterBasis = later.Add("decision", later.BuilderId, $$"""{"title":"Accepting work","statement":"Accept others' ordinary work.","summary":"Accept ordinary work.","synthesis":"A grant to accept.","effects":["accept-work"],"affects":[],"decidedBy":"{{later.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]}""");
        later.Add("authority", later.OperatorId, $$"""{"grantee":"{{laterDelegate.Id}}","scope":["accept-work"],"reserved":[],"conditions":[],"basis":"{{laterBasis.Id}}"}""");
        Work laterWork = Work.Write(later, delegated: laterDelegate.Id);
        later.Add("goal-status", later.OperatorId, Status("active"), laterWork.Goal.Reference);
        later.Add("work-order", later.OperatorId, $$"""{"key":"M-2","kind":"milestone","title":"Milestone","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{laterWork.Goal.Id}}"}],"deliverables":["work"]}""");
        RecordEnvelope laterEvaluation = later.Add("review", laterWork.Reviewer.Id, Evaluation("pass", Met(laterWork.Result), Met(laterWork.Result)), laterWork.Goal.Reference);
        RecordEnvelope withdrawn = later.Add("acceptance", laterDelegate.Id, Accept([laterEvaluation.Id]), laterWork.Goal.Reference);
        Assert.Contains(later.Load().Findings, f => f.Code == "acceptance.policy" && f.Location == withdrawn.ExpectedPath && f.Message.Contains("not delegated", StringComparison.Ordinal));
    }

    [Fact]
    public void AStatusItsAuthorCouldNotRecordChangesNothing()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.BuilderId, Goal(estate));
        RecordEnvelope ended = estate.Add("goal-status", estate.BuilderId, Status("abandoned"), goal.Reference);
        RecordEnvelope started = estate.Add("goal-status", estate.OperatorId, Status("active"), goal.Reference);
        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "goal-status.sponsor" && f.Location == ended.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location == started.ExpectedPath);
        Assert.Equal("active", GoalRules.Status(estate.Load(), goal));
    }

    private static string Status(string status) => $$"""{"status":"{{status}}","note":"The sponsor's call."}""";

    private static string GoalReleasing(TestEstate estate, string product, string delegated) =>
        """{"title":"Release the widget","objective":"Publish it.","completion":[{"record":{"type":"release","subject":"@P@","fields":{"status":"published"}}}],"constraints":[],"kind":"finite","sponsor":"@S@","delegate":"@D@","budget":{"wallMinutes":600,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}"""
            .Replace("@P@", product, StringComparison.Ordinal).Replace("@S@", estate.OperatorId, StringComparison.Ordinal).Replace("@D@", delegated, StringComparison.Ordinal);

    private static string Accept(string[] basis, bool self = false) =>
        $$"""{"statement":"Accepted on the evaluation.","basis":[{{string.Join(",", basis.Select(b => $"\"{b}\""))}}],"self":{{(self ? "true" : "false")}}}""";
}
