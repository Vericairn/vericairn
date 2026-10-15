// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// Who records a goal's steps (the estate's design/goals.md § 6): a person takes over a step an agent holds, and the hand-over is a
/// record a person makes, the one taking the step over, the sponsor of its goal or an operator, never an agent, whatever scope the
/// agent holds; and a goal's sponsor, when a person, assigns the goal's steps and records its agents' runs under its activation.
/// </summary>
public class StepAuthorityTests
{
    [Fact]
    public void AHandOverIsRecordedByAPersonAndNeverByAnAgent()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        string pat = Person(estate, "Pat");
        string quinn = Person(estate, "Quinn");
        string sam = Person(estate, "Sam");
        Grant(estate, builder, "local-engineering");
        Grant(estate, quinn, "local-engineering");
        RecordEnvelope goal = estate.Add("goal", op, Goal(sam));
        RecordEnvelope[] orders = [.. Enumerable.Range(1, 5).Select(i => estate.Add("work-order", op, Order($"W-{i}", goal.Id)))];
        foreach (RecordEnvelope order in orders)
        {
            estate.Add("assignment", op, Assignment(builder), order.Reference);
        }

        // Each step the builder holds goes over to Pat: recorded by the builder, by Quinn, by Pat, by Sam the sponsor, and by the operator.
        RecordEnvelope byAgent = estate.Add("assignment", builder, Assignment(pat), orders[0].Reference);
        RecordEnvelope byEngineer = estate.Add("assignment", quinn, Assignment(pat), orders[1].Reference);
        RecordEnvelope byTaker = estate.Add("assignment", pat, Assignment(pat), orders[2].Reference);
        RecordEnvelope bySponsor = estate.Add("assignment", sam, Assignment(pat), orders[3].Reference);
        RecordEnvelope byOperator = estate.Add("assignment", op, Assignment(pat), orders[4].Reference);

        // Once a person holds a step, assigning it again is ordinary work: the builder, under local-engineering, gives W-3 back to itself.
        RecordEnvelope back = estate.Add("assignment", builder, Assignment(builder), orders[2].Reference);
        List<Finding> errors = [.. estate.Load().Findings.Where(f => f.Severity == Severity.Error)];
        Assert.Contains(errors, f => f.Code == "assignment.handover" && f.Location == byAgent.ExpectedPath);
        Assert.Contains(errors, f => f.Code == "assignment.handover" && f.Location == byEngineer.ExpectedPath);
        Assert.Contains("a hand-over is recorded by a person, the one taking the step over, the sponsor of its goal or an operator, and never by an agent", Assert.Single(errors, f => f.Location == byAgent.ExpectedPath).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(errors, f => new[] { byTaker, bySponsor, byOperator, back }.Any(r => f.Location.StartsWith(r.ExpectedPath, StringComparison.Ordinal)));
    }

    [Fact]
    public void AGoalsSponsorWhenAPersonAssignsItsStepsAndRecordsItsAgentsRuns()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        string sam = Person(estate, "Sam");
        string pat = Person(estate, "Pat");
        Grant(estate, sam, "spending");
        RecordEnvelope goal = estate.Add("goal", op, Goal(sam));
        estate.Add("goal-status", sam, """{"status":"active","note":"Sam funds it."}""", goal.Reference);
        RecordEnvelope order = estate.Add("work-order", op, Order("W-1", goal.Id));
        RecordEnvelope elsewhere = estate.Add("work-order", op, Order("X-1", null));

        // Sam assigns the goal's step and records its agent's run; Sam assigns nothing outside the goal, and Pat nothing at all.
        RecordEnvelope assigned = estate.Add("assignment", sam, Assignment(builder), order.Reference);
        RecordEnvelope outside = estate.Add("assignment", sam, Assignment(builder), elsewhere.Reference);
        RecordEnvelope byPat = estate.Add("assignment", pat, Assignment(builder), order.Reference);
        RecordEnvelope run = estate.Add("operation", sam, Run(goal.Id, assigned.Id, builder, "agent-run:sam"));
        RecordEnvelope runByPat = estate.Add("operation", pat, Run(goal.Id, assigned.Id, builder, "agent-run:pat"));
        List<Finding> errors = [.. estate.Load().Findings.Where(f => f.Severity == Severity.Error)];
        Assert.DoesNotContain(errors, f => f.Location.StartsWith(assigned.ExpectedPath, StringComparison.Ordinal) || f.Location.StartsWith(run.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(errors, f => f.Code == "assignment.author" && f.Location == outside.ExpectedPath);
        Assert.Contains(errors, f => f.Code == "assignment.author" && f.Location == byPat.ExpectedPath);
        Assert.Contains(errors, f => f.Code == "operation.author" && f.Location == runByPat.ExpectedPath);

        // A goal an agent sponsors gives the agent nothing: an operator decides for it, and the agent needs local-engineering like any other.
        RecordEnvelope agentsGoal = estate.Add("goal", op, Goal(builder));
        RecordEnvelope agentsOrder = estate.Add("work-order", op, Order("A-1", agentsGoal.Id));
        RecordEnvelope byItsAgent = estate.Add("assignment", builder, Assignment(builder), agentsOrder.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "assignment.author" && f.Location == byItsAgent.ExpectedPath);
    }

    private static string Person(TestEstate estate, string name) => estate.Add("principal", estate.OperatorId, $$"""{"kind":"human","name":"{{name}}","roles":["contributor"]}""").Id;

    private static void Grant(TestEstate estate, string grantee, string scope)
    {
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"A grant","statement":"Grant {{scope}}.","summary":"A grant.","synthesis":"A grant of {{scope}}.","effects":["{{scope}}"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[]}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{grantee}}","scope":["{{scope}}"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""");
    }

    private static string Goal(string sponsor) =>
        $$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{sponsor}}","budget":{"wallMinutes":600,"attemptsPerStep":5,"concurrency":5},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""";

    private static string Order(string key, string? goal) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Build {{key}}","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{{(goal is null ? string.Empty : $$"""{"type":"goal","id":"{{goal}}"}""")}}],"deliverables":["widget"]}""";

    private static string Assignment(string contributor) =>
        $$$"""{"contributor":"{{{contributor}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."}}""";

    private static string Run(string goal, string assignment, string agent, string identity) =>
        $$$"""{"provider":"claude-code","action":"agent-run","identity":"{{{identity}}}","intent":"Run the builder.","target":{"assignment":"{{{assignment}}}","principal":"{{{agent}}}","workspace":"widget, branch work/w-1","goal":"{{{goal}}}"},"status":"not-started","evidence":[],"cap":{"wallMinutes":10,"attemptsPerStep":5}}""";
}
