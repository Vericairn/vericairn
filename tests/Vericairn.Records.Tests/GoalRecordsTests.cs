// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>The records goals need, in record format 2: goal, goal-status, roster, ask, work-order relations, grant expiry and agent runs.</summary>
public class GoalRecordsTests
{
    [Fact]
    public void AGoalStatesWhatDoneLooksLikeItsBudgetAndItsSponsor()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("goal", estate.BuilderId, Goal(estate));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void AGoalIsAFormatTwoRecord()
    {
        using var estate = new TestEstate();
        RecordEnvelope goal = estate.Add("goal", estate.BuilderId, Goal(estate));
        Assert.Contains(estate.Load().Findings, f => f.Code == "record.type-format" && f.Location == goal.ExpectedPath);
    }

    [Fact]
    public void AGoalsConditionsBudgetAndPlanAreChecked()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope both = estate.Add("goal", estate.BuilderId, Goal(estate, completion: """[{"record":{"type":"release"},"evidence":"x","check":"y"}]"""));
        RecordEnvelope unknown = estate.Add("goal", estate.BuilderId, Goal(estate, completion: """[{"record":{"type":"widget"}}]"""));
        RecordEnvelope budget = estate.Add("goal", estate.BuilderId, Goal(estate, budget: """{"wallMinutes":60}"""));
        RecordEnvelope given = estate.Add("goal", estate.BuilderId, Goal(estate, plan: """{"policy":"given","approval":"sponsor-approves"}"""));
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "goal.condition" && f.Location.StartsWith(both.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "goal.predicate" && f.Location.StartsWith(unknown.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location == budget.ExpectedPath + "#body.budget.attemptsPerStep");
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location == given.ExpectedPath + "#body.plan.workOrders");
    }

    [Fact]
    public void AGoalStartsWithItsSponsorsActivationAndEndsOnce()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.BuilderId, Goal(estate));
        var subject = new RecordReference("goal", goal.Id, goal.ComputedHash);
        RecordEnvelope early = estate.Add("goal-status", estate.OperatorId, """{"status":"paused","note":"Not started yet."}""", subject);
        RecordEnvelope usurped = estate.Add("goal-status", estate.BuilderId, """{"status":"active","note":"The builder activates it."}""", subject);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", subject);
        estate.Add("goal-status", estate.BuilderId, """{"status":"paused","note":"Waiting on an ask."}""", subject);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Resumed."}""", subject);
        estate.Add("goal-status", estate.OperatorId, """{"status":"abandoned","note":"No longer wanted."}""", subject);
        RecordEnvelope after = estate.Add("goal-status", estate.BuilderId, """{"status":"paused","note":"After the end."}""", subject);
        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.Contains(findings, f => f.Code == "goal-status.first" && f.Location == early.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "goal-status.sponsor" && f.Location == usurped.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "goal-status.ended" && f.Location == after.ExpectedPath);
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void TheRosterNamesWhoFillsEachRoleAndTheDeciderIsAnOperator()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("roster", estate.OperatorId, Roster(estate, estate.OperatorId));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        using var wrong = new TestEstate(recordFormat: 2);
        RecordEnvelope agentDecides = wrong.Add("roster", wrong.OperatorId, Roster(wrong, wrong.BuilderId));
        RecordEnvelope twice = wrong.Add("roster", wrong.OperatorId, $$"""{"roles":[{"role":"builder","principals":["{{wrong.BuilderId}}"]},{"role":"builder","principals":["{{wrong.OperatorId}}"]}]}""");
        RecordEnvelope byAgent = wrong.Add("roster", wrong.BuilderId, Roster(wrong, wrong.OperatorId));
        List<Finding> findings = wrong.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "roster.decider" && f.Location.StartsWith(agentDecides.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "roster.duplicate-role" && f.Location.StartsWith(twice.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "roster.author" && f.Location == byAgent.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "roster.multiple");
    }

    [Fact]
    public void AnAskIsRevisedOnlyByItsAskerAndAnsweredOnlyByWhomItWasPutTo()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, Ask(estate, estate.OperatorId, "open"));
        estate.Add("ask", estate.BuilderId, Ask(estate, estate.OperatorId, "notified"), supersedes: ask.Id);
        estate.Add("decision", estate.BuilderId, Answer(estate, ask.Id));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        using var wrong = new TestEstate(recordFormat: 2);
        RecordEnvelope first = wrong.Add("ask", wrong.BuilderId, Ask(wrong, wrong.OperatorId, "notified"));
        RecordEnvelope asked = wrong.Add("ask", wrong.BuilderId, Ask(wrong, wrong.OperatorId, "open"));
        RecordEnvelope hijacked = wrong.Add("ask", wrong.OperatorId, Ask(wrong, wrong.OperatorId, "withdrawn"), supersedes: asked.Id);
        RecordEnvelope reworded = wrong.Add("ask", wrong.BuilderId, Ask(wrong, wrong.OperatorId, "open", question: "Something else?"));
        RecordEnvelope rewordedRevision = wrong.Add("ask", wrong.BuilderId, Ask(wrong, wrong.OperatorId, "notified"), supersedes: reworded.Id);
        RecordEnvelope toBuilder = wrong.Add("ask", wrong.OperatorId, Ask(wrong, wrong.BuilderId, "open"));
        RecordEnvelope misanswered = wrong.Add("decision", wrong.BuilderId, Answer(wrong, toBuilder.Id));
        List<Finding> findings = wrong.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "ask.first" && f.Location == first.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "ask.revision" && f.Location == hijacked.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "ask.revision" && f.Location == rewordedRevision.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "decision.answers" && f.Location == misanswered.ExpectedPath);

        using var withdrawn = new TestEstate(recordFormat: 2);
        RecordEnvelope open = withdrawn.Add("ask", withdrawn.BuilderId, Ask(withdrawn, withdrawn.OperatorId, "open"));
        withdrawn.Add("ask", withdrawn.BuilderId, Ask(withdrawn, withdrawn.OperatorId, "withdrawn"), supersedes: open.Id);
        RecordEnvelope tooLate = withdrawn.Add("decision", withdrawn.BuilderId, Answer(withdrawn, open.Id));
        RecordEnvelope other = withdrawn.Add("ask", withdrawn.BuilderId, Ask(withdrawn, withdrawn.OperatorId, "open", question: "Another?"));
        RecordEnvelope gone = withdrawn.Add("ask", withdrawn.BuilderId, Ask(withdrawn, withdrawn.OperatorId, "withdrawn", question: "Another?"), supersedes: other.Id);
        RecordEnvelope late = withdrawn.Add("ask", withdrawn.BuilderId, Ask(withdrawn, withdrawn.OperatorId, "notified", question: "Another?"), supersedes: gone.Id);
        findings = withdrawn.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "ask.withdrawn" && f.Location == late.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "decision.answers" && f.Location == tooLate.ExpectedPath);
    }

    [Fact]
    public void AWorkOrderStartsAfterOthersWithoutACycleAndNamesWhatHoldsIt()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope a = estate.Add("work-order", estate.OperatorId, Order("A"));
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, Ask(estate, estate.OperatorId, "open"));
        RecordEnvelope b = estate.Add("work-order", estate.OperatorId, Order("B", $$""","after":["{{a.Id}}"],"blockedBy":[{"type":"ask","id":"{{ask.Id}}"}]"""));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        RecordEnvelope wrongBlocker = estate.Add("work-order", estate.OperatorId, Order("C", $$""","blockedBy":[{"type":"principal","id":"{{estate.OperatorId}}"}]"""));
        RecordEnvelope d = estate.Add("work-order", estate.OperatorId, Order("D"));
        RecordEnvelope selfish = estate.Add("work-order", estate.OperatorId, Order("D", $$""","after":["{{d.Id}}"]"""), supersedes: d.Id);
        RecordEnvelope circular = estate.Add("work-order", estate.OperatorId, Order("A", $$""","after":["{{b.Id}}"]"""), supersedes: a.Id);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "work.blocked-by" && f.Location.StartsWith(wrongBlocker.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "work.after" && f.Location == selfish.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "work.after-cycle" && f.Location == circular.ExpectedPath);
    }

    [Fact]
    public void AGrantExpiresAfterItIsMadeAndAuthorizesNothingFromThenOn()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope basis = estate.Add("decision", estate.BuilderId, Answer(estate, null));
        DateTimeOffset made = estate.Clock.AddSeconds(1);
        string expires = RecordParser.FormatTimestamp(made.AddHours(1));
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering"],"reserved":[],"conditions":[],"basis":"{{basis.Id}}","expires":"{{expires}}"}""");
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        Assert.True(model.HasScope(estate.BuilderId, "local-engineering", made.AddMinutes(30)));
        Assert.False(model.HasScope(estate.BuilderId, "local-engineering", made.AddHours(2)));

        RecordEnvelope backwards = estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{basis.Id}}","expires":"2026-10-01T00:00:00Z"}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "authority.expires" && f.Location == backwards.ExpectedPath + "#body.expires");
    }

    [Fact]
    public void AnAgentRunStatesItsCapBeforeItStartsAndItsUsageOnceSettled()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string target = $$"""{"assignment":"{{estate.BuilderId}}","principal":"{{estate.BuilderId}}","workspace":"vericairn repository, work branch"}""";
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Run("not-started", target, ""","cap":{"wallMinutes":30}"""));
        RecordEnvelope dispatched = estate.Add("operation", estate.OperatorId, Run("dispatched", target, ""","dispatchedAt":"2026-10-13T12:00:30Z","cap":{"wallMinutes":30}"""), supersedes: intent.Id);
        estate.Add("operation", estate.OperatorId, Run("observed-success", target, ""","cap":{"wallMinutes":30},"dispatchedAt":"2026-10-13T12:00:30Z","observed":{"summary":"The run ended.","at":"2026-10-13T12:20:00Z"},"usage":{"wallMinutes":19,"unreported":["tokens"]}"""), supersedes: dispatched.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        using var wrong = new TestEstate(recordFormat: 2);
        RecordEnvelope uncapped = wrong.Add("operation", wrong.OperatorId, Run("not-started", target, string.Empty, identity: "run-a"));
        RecordEnvelope placeless = wrong.Add("operation", wrong.OperatorId, Run("not-started", """{"assignment":"x","principal":"y"}""", ""","cap":{"wallMinutes":30}""", identity: "run-b"));
        RecordEnvelope capped = wrong.Add("operation", wrong.OperatorId, Run("not-started", """{"repository":"Example/example"}""", ""","cap":{"wallMinutes":30}""", identity: "release-c", action: "dispatch-release-workflow"));
        RecordEnvelope start = wrong.Add("operation", wrong.OperatorId, Run("not-started", target, ""","cap":{"wallMinutes":30}""", identity: "run-d"));
        RecordEnvelope sent = wrong.Add("operation", wrong.OperatorId, Run("dispatched", target, ""","dispatchedAt":"2026-10-13T12:00:30Z","cap":{"wallMinutes":30}""", identity: "run-d"), supersedes: start.Id);
        RecordEnvelope silent = wrong.Add("operation", wrong.OperatorId, Run("observed-success", target, ""","cap":{"wallMinutes":30},"dispatchedAt":"2026-10-13T12:00:30Z","observed":{"summary":"Done.","at":"2026-10-13T12:20:00Z"}""", identity: "run-d"), supersedes: sent.Id);
        List<Finding> findings = wrong.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "operation.cap" && f.Location == uncapped.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "operation.agent-run" && f.Location == placeless.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "operation.cap" && f.Location == capped.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "operation.usage" && f.Location == silent.ExpectedPath);
    }

    private static string Goal(TestEstate estate, string? completion = null, string? budget = null, string? plan = null) =>
        $$"""{"title":"Release 0.5.0","objective":"Ship release 0.5.0 with goals.","completion":{{completion ?? """[{"record":{"type":"release","fields":{"version":"0.5.0","status":"published"}}},{"evidence":"The operator's guide describes every new command.","check":"An evaluator reads the guide against the command tree."}]"""}},"constraints":["no publication without the operator's acceptance"],"kind":"finite","sponsor":"{{estate.OperatorId}}","budget":{{budget ?? """{"wallMinutes":600,"attemptsPerStep":3}"""}},"plan":{{plan ?? """{"policy":"proposed-by-planner","approval":"sponsor-approves"}"""}},"inputs":[]}""";

    private static string Roster(TestEstate estate, string decider) =>
        $$"""{"roles":[{"role":"builder","principals":["{{estate.BuilderId}}"]},{"role":"decider","principals":["{{decider}}"],"note":"Decisions and acceptances."}]}""";

    private static string Ask(TestEstate estate, string to, string state, string question = "Publish 0.5.0?") =>
        $$"""{"to":"{{to}}","question":"{{question}}","recommendation":"Yes, after the review passes.","consequence":"The release waits.","state":"{{state}}"}""";

    private static string Answer(TestEstate estate, string? ask)
    {
        string answers = ask is null ? string.Empty : $$""","answers":["{{ask}}"]""";
        return $$"""{"title":"Publish","statement":"Yes.","summary":"Publish after the review.","synthesis":"The operator answers.","effects":["publication follows the review"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["spending"]{{answers}}}""";
    }

    private static string Order(string key, string extra = "") =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]{{extra}}}""";

    private static string Run(string status, string target, string extra, string identity = "run-1", string action = "agent-run") =>
        $$"""{"provider":"claude-code","action":"{{action}}","identity":"{{identity}}","intent":"Carry the assignment to its exit question.","target":{{target}},"status":"{{status}}","evidence":[]{{extra}}}""";
}
