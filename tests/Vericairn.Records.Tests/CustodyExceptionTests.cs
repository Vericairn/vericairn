// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The sponsor's exception to the custody precondition (the estate's design/goals.md § 10): a record with its reason and its expiry,
/// about a goal's exact revision, made only by who decides for the goal, in force until it expires; and a run launched under one names
/// one that covered its goal when it was recorded.
/// </summary>
public class CustodyExceptionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyWhoDecidesForTheGoalExceptsItAndOnlyUntilItsExpiry()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = Goal(estate, estate.OperatorId);
        RecordEnvelope excepted = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-14T12:00:00Z"), goal.Reference);
        RecordEnvelope byAgent = estate.Add("custody-exception", estate.BuilderId, Exception("2026-10-14T12:00:00Z"), goal.Reference);
        RecordEnvelope backwards = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-13T11:00:00Z"), goal.Reference);
        RecordEnvelope hijacked = estate.Add("custody-exception", estate.BuilderId, Exception("2026-10-13T18:00:00Z"), goal.Reference, supersedes: excepted.Id);
        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.DoesNotContain(findings, f => f.Location.StartsWith(excepted.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "custody-exception.sponsor" && f.Location == byAgent.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "custody-exception.expires" && f.Location == backwards.ExpectedPath + "#body.expires");
        Assert.Contains(findings, f => f.Code == "custody-exception.revision" && f.Location == hijacked.ExpectedPath);

        // A goal a person sponsors who is not an operator is excepted by that person alone; one an agent sponsors by an operator alone.
        using var people = new TestEstate(recordFormat: 2);
        string pat = people.Add("principal", people.OperatorId, """{"kind":"human","name":"Pat","roles":["contributor"]}""").Id;
        RecordEnvelope patsGoal = Goal(people, pat);
        RecordEnvelope agentsGoal = Goal(people, people.BuilderId);
        RecordEnvelope byOperator = people.Add("custody-exception", people.OperatorId, Exception("2026-10-14T12:00:00Z"), patsGoal.Reference);
        RecordEnvelope bySponsor = people.Add("custody-exception", pat, Exception("2026-10-14T12:00:00Z"), patsGoal.Reference);
        RecordEnvelope forAgent = people.Add("custody-exception", people.OperatorId, Exception("2026-10-14T12:00:00Z"), agentsGoal.Reference);
        RecordEnvelope byItsAgent = people.Add("custody-exception", people.BuilderId, Exception("2026-10-14T12:00:00Z"), agentsGoal.Reference);
        findings = people.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "custody-exception.sponsor" && f.Location == byOperator.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "custody-exception.sponsor" && f.Location == byItsAgent.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location.StartsWith(bySponsor.ExpectedPath, StringComparison.Ordinal) || f.Location.StartsWith(forAgent.ExpectedPath, StringComparison.Ordinal));

        // A format-1 estate has no such record.
        using var early = new TestEstate();
        RecordEnvelope formatOne = early.Add("custody-exception", early.OperatorId, Exception("2026-10-14T12:00:00Z"), new RecordReference("goal", early.Estate.Id, early.Estate.ComputedHash));
        Assert.Contains(early.Load().Findings, f => f.Code == "record.type-format" && f.Location == formatOne.ExpectedPath);
    }

    [Fact]
    public void AnExceptionIsInForceForItsGoalsExactRevisionUntilItExpires()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = Goal(estate, estate.OperatorId);
        Assert.Null(CustodyExceptions.InForce(estate.Load(), goal, Noon));
        RecordEnvelope excepted = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-13T18:00:00Z"), goal.Reference);
        EstateModel model = estate.Load();
        Assert.Equal(excepted.Id, CustodyExceptions.InForce(model, goal, Noon.AddHours(1))?.Id);
        Assert.Null(CustodyExceptions.InForce(model, goal, Noon.AddHours(6)));
        Assert.Contains($"the custody exception {excepted.Id} that Test Operator recorded for the goal, in force until 2026-10-13T18:00:00Z: The machine runs the build loop overnight.", CustodyExceptions.Describe(model, excepted), StringComparison.Ordinal);

        // A revision of the goal needs a fresh exception, whatever the earlier one said.
        RecordEnvelope revised = estate.Add("goal", estate.OperatorId, GoalBody(estate.OperatorId, "Build the widget, revised"), supersedes: goal.Id);
        Assert.Null(CustodyExceptions.InForce(estate.Load(), revised, Noon.AddHours(1)));
    }

    [Fact]
    public void ARunNamesOnlyAnExceptionThatCoveredItsGoalWhenItWasRecorded()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = Goal(estate, estate.OperatorId);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", goal.Reference);
        RecordEnvelope other = Goal(estate, estate.OperatorId);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, $$"""{"key":"W-1","kind":"ordinary","title":"Build it","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal.Id}}"}],"deliverables":["widget"]}""");
        string assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."}}""", order.Reference).Id;
        RecordEnvelope excepted = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-14T12:00:00Z"), goal.Reference);
        RecordEnvelope elsewhere = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-14T12:00:00Z"), other.Reference);
        RecordEnvelope covered = estate.Add("operation", estate.OperatorId, Run(estate, goal.Id, assignment, "agent-run:covered", excepted.Id));
        RecordEnvelope wrongGoal = estate.Add("operation", estate.OperatorId, Run(estate, goal.Id, assignment, "agent-run:wrong-goal", elsewhere.Id));
        RecordEnvelope notOne = estate.Add("operation", estate.OperatorId, Run(estate, goal.Id, assignment, "agent-run:not-one", order.Id));
        List<Finding> findings = estate.Load().Findings.Where(f => f.Code == "operation.custody").ToList();
        Assert.DoesNotContain(findings, f => f.Location == covered.ExpectedPath);
        Assert.Contains(findings, f => f.Location == wrongGoal.ExpectedPath);
        Assert.Contains(findings, f => f.Location == notOne.ExpectedPath);

        // A run names the exception as it stood when the run was recorded: a revision recorded before the run replaces what it names,
        // and one recorded after the run leaves that run as valid as it was.
        RecordEnvelope revised = estate.Add("custody-exception", estate.OperatorId, Exception("2026-10-13T18:00:00Z"), goal.Reference, supersedes: excepted.Id);
        RecordEnvelope replaced = estate.Add("operation", estate.OperatorId, Run(estate, goal.Id, assignment, "agent-run:replaced", excepted.Id));
        RecordEnvelope current = estate.Add("operation", estate.OperatorId, Run(estate, goal.Id, assignment, "agent-run:current", revised.Id));
        findings = [.. estate.Load().Findings.Where(f => f.Code == "operation.custody")];
        Assert.DoesNotContain(findings, f => f.Location == covered.ExpectedPath);
        Assert.Contains(findings, f => f.Location == replaced.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location == current.ExpectedPath);
    }

    private static RecordEnvelope Goal(TestEstate estate, string sponsor) => estate.Add("goal", estate.OperatorId, GoalBody(sponsor, "Build the widget"));

    private static string GoalBody(string sponsor, string title) =>
        $$"""{"title":"{{title}}","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{sponsor}}","budget":{"wallMinutes":600,"attemptsPerStep":5,"concurrency":5},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""";

    private static string Exception(string expires) => $$"""{"reason":"The machine runs the build loop overnight.","expires":"{{expires}}"}""";

    private static string Run(TestEstate estate, string goal, string assignment, string identity, string exception) =>
        $$$"""{"provider":"claude-code","action":"agent-run","identity":"{{{identity}}}","intent":"Run the builder.","target":{"assignment":"{{{assignment}}}","principal":"{{{estate.BuilderId}}}","workspace":"widget, branch work/w-1","goal":"{{{goal}}}","custodyException":"{{{exception}}}"},"status":"not-started","evidence":[],"cap":{"wallMinutes":10,"attemptsPerStep":5}}""";
}
