// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// A plan's approval policy (the estate's design/goals.md §§ 4 and 6, FIX-117): when it says sponsor-approves, a work order proposed for
/// the goal joins its work only once who decides for the goal approves it; under self-approves-ordinary it joins at once.
/// </summary>
public class GoalProposalTests
{
    private const string Planned = """{"status":"planned","note":"Approved for the goal."}""";

    [Fact]
    public void UnderSponsorApprovesAPlannersWorkOrderWaitsUntilTheSponsorApprovesItsExactRevision()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, Goal(estate));
        RecordEnvelope own = estate.Add("work-order", estate.OperatorId, Order("W-1", Names(goal)));
        RecordEnvelope proposed = estate.Add("work-order", estate.BuilderId, Order("W-2", Names(goal)));

        // The sponsor's own work order joins at once; the planner's waits, and its own status approves nothing.
        estate.Add("work-status", estate.BuilderId, Planned, proposed.Reference);
        EstateModel model = estate.Load();
        Assert.Equal([own.Id], GoalRules.Work(model, goal).Select(o => o.Id));
        Assert.Equal([proposed.Id], GoalRules.Proposed(model, goal).Select(o => o.Id));

        // The sponsor approves the exact revision: it joins, and a judgment at a record before the approval still finds it waiting.
        RecordEnvelope before = estate.Add("work-status", estate.OperatorId, """{"status":"active","note":"Started."}""", own.Reference);
        estate.Add("work-status", estate.OperatorId, Planned, proposed.Reference);
        RecordEnvelope after = estate.Add("work-status", estate.OperatorId, """{"status":"paused","note":"Paused."}""", own.Reference);
        model = estate.Load();
        Assert.Equal([own.Id, proposed.Id], GoalRules.Work(model, goal).Select(o => o.Id));
        Assert.Empty(GoalRules.Proposed(model, goal));
        Assert.Equal([proposed.Id], GoalRules.Proposed(model, goal, before).Select(o => o.Id));
        Assert.Empty(GoalRules.Proposed(model, goal, after));

        // A revision the planner writes afterwards is proposed afresh.
        RecordEnvelope revised = estate.Add("work-order", estate.BuilderId, Order("W-2", Names(goal)).Replace("\"Work.\"", "\"More work.\"", StringComparison.Ordinal), supersedes: proposed.Id);
        model = estate.Load();
        Assert.Equal([revised.Id], GoalRules.Proposed(model, goal).Select(o => o.Id));
        Assert.Equal([own.Id], GoalRules.Work(model, goal).Select(o => o.Id));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void SelfApprovesOrdinaryKeepsItsMeaningAndThePlansOwnWorkOrdersNeverWait()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope self = estate.Add("goal", estate.OperatorId, Goal(estate, """{"policy":"proposed-by-planner","approval":"self-approves-ordinary"}"""));
        RecordEnvelope joined = estate.Add("work-order", estate.BuilderId, Order("W-1", Names(self)));
        RecordEnvelope listed = estate.Add("work-order", estate.BuilderId, Order("W-9", string.Empty));
        RecordEnvelope given = estate.Add("goal", estate.OperatorId, Goal(estate, $$"""{"policy":"given","approval":"sponsor-approves","workOrders":["{{listed.Id}}"]}"""));
        RecordEnvelope waiting = estate.Add("work-order", estate.BuilderId, Order("W-3", Names(given)));

        EstateModel model = estate.Load();
        Assert.Equal([joined.Id], GoalRules.Work(model, self).Select(o => o.Id));
        Assert.Empty(GoalRules.Proposed(model, self));
        Assert.False(GoalRules.SponsorApproves(self));
        Assert.Equal([listed.Id], GoalRules.Work(model, given).Select(o => o.Id));
        Assert.Equal([waiting.Id], GoalRules.Proposed(model, given).Select(o => o.Id));
    }

    [Fact]
    public void WhoApprovesIsWhoDecidesForTheGoal()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope sam = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Sam","roles":["contributor"]}""");
        RecordEnvelope persons = estate.Add("goal", estate.OperatorId, Goal(estate, sponsor: sam.Id));
        RecordEnvelope byOperator = estate.Add("work-order", estate.OperatorId, Order("P-1", Names(persons)));
        RecordEnvelope agents = estate.Add("goal", estate.BuilderId, Goal(estate, sponsor: estate.BuilderId));
        RecordEnvelope byAgent = estate.Add("work-order", estate.BuilderId, Order("A-1", Names(agents)));
        RecordEnvelope forAgent = estate.Add("work-order", estate.OperatorId, Order("A-2", Names(agents)));

        // A person's goal waits for that person, even on an operator's work order; an agent's goal for an operator, never the agent.
        EstateModel model = estate.Load();
        Assert.Equal([byOperator.Id], GoalRules.Proposed(model, persons).Select(o => o.Id));
        Assert.Equal([byAgent.Id], GoalRules.Proposed(model, agents).Select(o => o.Id));
        Assert.Equal([forAgent.Id], GoalRules.Work(model, agents).Select(o => o.Id));

        estate.Add("work-status", estate.OperatorId, Planned, byOperator.Reference);
        estate.Add("work-status", estate.BuilderId, Planned, byAgent.Reference);
        model = estate.Load();
        Assert.Single(GoalRules.Proposed(model, persons));
        Assert.Single(GoalRules.Proposed(model, agents));

        estate.Add("work-status", sam.Id, Planned, byOperator.Reference);
        estate.Add("work-status", estate.OperatorId, Planned, byAgent.Reference);
        model = estate.Load();
        Assert.Empty(GoalRules.Proposed(model, persons));
        Assert.Empty(GoalRules.Proposed(model, agents));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void AProposedWorkOrderStillCountsForIndependenceDelegationAndWhoMayAssignIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope sam = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Sam","roles":["contributor"]}""");
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, Goal(estate, sponsor: sam.Id));
        RecordEnvelope proposed = estate.Add("work-order", estate.OperatorId, Order("W-1", Names(goal)));
        RecordEnvelope milestone = estate.Add("work-order", estate.OperatorId, Order("M-1", Names(goal)).Replace("\"ordinary\"", "\"milestone\"", StringComparison.Ordinal));

        // Sam, who would approve it, assigns the proposed work order; the builder who works it is no independent evaluator.
        RecordEnvelope assignment = estate.Add("assignment", sam.Id, Assignment(estate.BuilderId), proposed.Reference);
        estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository, work branch","note":"Build it."}""", assignment.Reference);

        EstateModel model = estate.Load();
        Assert.Equal([milestone.Id, proposed.Id], GoalRules.Proposed(model, goal).Select(o => o.Id));
        Assert.Empty(GoalRules.Work(model, goal));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.True(Routing.DecidesForStep(model, sam.Id, proposed));
        Assert.Empty(Routing.GoalsOf(model, proposed));
        Assert.Contains(estate.BuilderId, GoalRules.StepAuthors(model, goal));
        Assert.Contains(GoalRules.ReservedActions(model, goal), r => r.Contains("M-1 is a milestone", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusAndTheGoalViewShowAProposedWorkOrderWaitingForTheSponsor()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, Goal(estate));
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", goal.Reference);
        RecordEnvelope proposed = estate.Add("work-order", estate.BuilderId, Order("W-2", Names(goal)));

        EstateModel model = estate.Load();
        EstateStatus.WaitingProposal waiting = Assert.Single(EstateStatus.WaitingProposals(model));
        Assert.Equal((goal.Id, proposed.Id, estate.OperatorId), (waiting.Goal.Id, waiting.Order.Id, waiting.Sponsor));
        Assert.Contains($"Waiting for a goal's sponsor to approve them:\n  W-2  Order W-2  proposed for goal {goal.Id} (Release the widget) by {estate.BuilderId}; approved by Test Operator ({estate.OperatorId})\n",
            EstateStatus.Render(model, new FindingList()), StringComparison.Ordinal);
        using (JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList())))
        {
            JsonElement row = Assert.Single(status.RootElement.GetProperty("proposals").EnumerateArray());
            Assert.Equal((goal.Id, "W-2", proposed.Id, proposed.ComputedHash, estate.BuilderId, estate.OperatorId),
                (row.GetProperty("goal").GetString(), row.GetProperty("key").GetString(), row.GetProperty("id").GetString(), row.GetProperty("hash").GetString(), row.GetProperty("by").GetString(), row.GetProperty("sponsor").GetString()));
        }

        string text = GoalView.RenderText(model, goal);
        Assert.Contains("Steps: none yet; the work orders its plan gives, and those that name it among their inputs once its sponsor approves them, are its steps", text, StringComparison.Ordinal);
        Assert.Contains($"Proposed, waiting for the sponsor's approval before they join the goal:\n  W-2  Order W-2  proposed by Test Builder ({estate.BuilderId}); approved by Test Operator ({estate.OperatorId}) with ", text, StringComparison.Ordinal);
        Assert.Contains($"--subject work-order:{proposed.Id}:{proposed.ComputedHash} --body -", text, StringComparison.Ordinal);
        using (JsonDocument view = JsonDocument.Parse(GoalView.RenderJson(model, goal)))
        {
            JsonElement row = Assert.Single(view.RootElement.GetProperty("proposed").EnumerateArray());
            Assert.Equal(("W-2", proposed.Id, "Test Builder"), (row.GetProperty("key").GetString(), row.GetProperty("id").GetString(), row.GetProperty("byName").GetString()));
            Assert.Empty(view.RootElement.GetProperty("steps").EnumerateArray());
        }

        // Approved, it is a step and waits for no one; nor does anything wait for an ended goal.
        estate.Add("work-status", estate.OperatorId, Planned, proposed.Reference);
        model = estate.Load();
        Assert.Empty(EstateStatus.WaitingProposals(model));
        Assert.DoesNotContain("Waiting for a goal's sponsor", EstateStatus.Render(model, new FindingList()), StringComparison.Ordinal);
        using (JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList())))
            Assert.False(status.RootElement.TryGetProperty("proposals", out _));
        using (JsonDocument view = JsonDocument.Parse(GoalView.RenderJson(model, goal)))
        {
            Assert.False(view.RootElement.TryGetProperty("proposed", out _));
            Assert.Equal("W-2", Assert.Single(view.RootElement.GetProperty("steps").EnumerateArray()).GetProperty("key").GetString());
        }

        // A retired or accepted proposal stays out of the goal's work, and nothing waits on the sponsor for it.
        RecordEnvelope retired = estate.Add("work-order", estate.BuilderId, Order("W-4", Names(goal)));
        estate.Add("work-status", estate.BuilderId, """{"status":"retired","note":"Not needed."}""", retired.Reference);
        RecordEnvelope accepted = estate.Add("work-order", estate.BuilderId, Order("W-5", Names(goal)));
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":true}""", accepted.Reference);
        model = estate.Load();
        Assert.Equal([retired.Id, accepted.Id], GoalRules.Proposed(model, goal).Select(o => o.Id));
        Assert.Empty(EstateStatus.WaitingProposals(model));
        using (JsonDocument view = JsonDocument.Parse(GoalView.RenderJson(model, goal)))
            Assert.False(view.RootElement.TryGetProperty("proposed", out _));

        estate.Add("work-order", estate.BuilderId, Order("W-3", Names(goal)));
        Assert.Single(EstateStatus.WaitingProposals(estate.Load()));
        estate.Add("goal-status", estate.OperatorId, """{"status":"abandoned","note":"No longer wanted."}""", goal.Reference);
        Assert.Empty(EstateStatus.WaitingProposals(estate.Load()));
    }

    private static string Names(RecordEnvelope goal) => $$"""{"type":"goal","id":"{{goal.Id}}"}""";
}
