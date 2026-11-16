// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// Decision transitions (the estate's design/flow.md § 5, decision 01M75YYK958AFMS24W71BHRH26; PROC-19): a step ending in named paths,
/// decided by a rule, an agent in a role, a person in a role, or a rule then a role; one path or several with a join; a rule's default; a
/// reserved act still stopping for the operator after an agent's choice; a decision the flow requires of a person; an agent's decision
/// reviewed before it takes effect; a deadline taking the default path or raising attention; a path back past its limit raising attention;
/// a venture's configuration of the decider, the narrowest winning; the phase gate as one set of paths; and what the contract refuses.
/// Every time is a controlled clock.
/// </summary>
public class FlowDecisionTests
{
    /// <summary>A Friday afternoon, after the run's activation, from which the tests count.</summary>
    private static readonly DateTimeOffset Friday = new(2026, 10, 16, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AnAgentInTheRoleDecidesAndRecordsItsPathReasonsAndEvidenceAndAReservedActStillStops()
    {
        using var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: true));
        AssertNoErrors(run);
        Assert.Equal("blocked", run.Step("gate").State);
        RecordEnvelope lyrics = Draft(run);

        // Ready: routed by the approver role to the agent, which decides; nothing after it runs yet.
        FlowPosition.StepInstance pending = run.Step("gate");
        Assert.Equal(("next", "pending", run.Estate.BuilderId), (pending.State, pending.Decision!.State, pending.Principal));
        Assert.True(pending.Step.IsRouted);
        Assert.Equal("blocked", run.Step("release").State);

        RecordEnvelope assignment = run.Assign("gate", run.Estate.BuilderId);
        RecordEnvelope decided = Decide(run, assignment, ["ship"], "The lyrics are ready to release.", lyrics);
        AssertNoErrors(run);
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("done", "taken", "agent", run.Estate.BuilderId), (gate.State, gate.Decision!.State, gate.Decision.Decider, gate.Decision.By));
        Assert.Equal(["ship"], gate.Decision.Paths);
        Assert.Equal((decided.Id, "The lyrics are ready to release."), (gate.Decision.Record!.Id, gate.Decision.Reasons));
        Assert.Equal((lyrics.Id, lyrics.ComputedHash), (gate.Decision.Evidence[0].Id, gate.Decision.Evidence[0].Hash));
        Assert.Equal(decided.AtTime, gate.DoneAt);
        Assert.StartsWith("decided: path ship (step release)", gate.Reasons[0], StringComparison.Ordinal);

        // The chosen path makes release eligible; the step it would rework is untouched; and publication, a reserved act, still stops for
        // the operator once release is done: the agent's choice performed nothing reserved.
        Assert.Equal("next", run.Step("release").State);
        run.Marked("RELEASED");
        FlowPosition.StepInstance publish = run.Step("publish");
        Assert.Equal(("stopped", "reserved"), (publish.State, publish.Stop));

        // Shown: goal show's text and JSON, and status's runs.
        EstateModel model = run.Estate.Load();
        FlowPosition.Position position = FlowPosition.Derive(model, run.Goal, run.Estate.Clock);
        Assert.Contains($"decision: taken, decided by role in this run; path ship by {Routing.NameOf(model, run.Estate.BuilderId)}, an agent", FlowPosition.RenderText(model, position), StringComparison.Ordinal);
        JsonObject step = FlowPosition.RenderJson(model, position)["steps"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "gate")!.AsObject();
        Assert.Equal(("taken", "agent", decided.Id), (step["decision"]!["state"]!.GetValue<string>(), step["decision"]!["decidedBy"]!.GetValue<string>(), step["decision"]!["record"]!["id"]!.GetValue<string>()));
        Assert.Contains("Decision: gate taken", FlowStops.RunsText(model, [position]), StringComparison.Ordinal);
        Assert.Equal("gate", FlowStops.RunsJson(model, [position])[0]!["decisions"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void APersonInTheRoleDecidesFromTheirAssignment()
    {
        using var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: false));
        Draft(run);
        FlowPosition.StepInstance asked = run.Step("gate");
        Assert.Equal(("stopped", "person", run.Estate.OperatorId), (asked.State, asked.Stop, asked.Principal));
        FlowStops.RunStop stop = Assert.Single(run.Position().Stops, s => s.Step?.Key == "gate");
        Assert.Equal(("person", "due"), (stop.Kind, stop.Delivery));

        RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
        Assert.Equal(("stopped", "person"), (run.Step("gate").State, run.Step("gate").Stop));
        Decide(run, assignment, ["drop"], "The song is shelved.");
        AssertNoErrors(run);
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("done", "person"), (gate.State, gate.Decision!.Decider));

        // Drop ends the run at the gate: every step after it is off, and the run is complete.
        Assert.Equal("off", run.Step("release").State);
        Assert.StartsWith("its run ends at gate, whose decision took path drop", run.Step("publish").Reasons[0], StringComparison.Ordinal);
        Assert.True(run.Position().Complete);
    }

    [Fact]
    public void ARuleDecidesAtOnceReadingTheRecordsAsTheyStoodWhenTheStepWasReady()
    {
        using var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule" }, rolesOf: f => Approvers(f, agent: true));
        AssertNoErrors(run);
        RecordEnvelope approved = run.Marked("APPROVED");
        Draft(run);

        // Until it is recorded, the rule's decision shows pending with its path, and nothing after the gate runs.
        FlowPosition.StepInstance pending = run.Step("gate");
        Assert.Equal(("next", "pending", "rule", "1"), (pending.State, pending.Decision!.State, pending.Decision.Decider, pending.Decision.Rule));
        Assert.Equal(["ship"], pending.Decision.Paths);
        Assert.StartsWith("rule 1 holds as the records stood at", pending.Decision.Reasons, StringComparison.Ordinal);
        Assert.Contains(pending.Decision.Evidence, e => e.Id == approved.Id && e.Hash == approved.ComputedHash);
        Assert.Equal("blocked", run.Step("release").State);

        // Recorded as the runtime evaluated it, by who takes the step: the path, the rule that matched and the records it read.
        RecordEnvelope ruled = RecordRule(run);
        AssertNoErrors(run);
        Assert.Contains($"work-order {approved.Id} {approved.ComputedHash}", ruled.Body.GetProperty("facts").GetProperty("read").EnumerateArray().Select(r => r.GetString()));
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("done", "taken", "rule", ruled.Id), (gate.State, gate.Decision!.State, gate.Decision.Decider, gate.Decision.Record!.Id));
        Assert.Equal("next", run.Step("release").State);
    }

    [Fact]
    public void ARecordedRuleDecisionIsNeverEvaluatedAgain()
    {
        // Review finding 5: an unchanged revision of a marker, a marker dated before the round, or a changed state never flips the path.
        using var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule" }, rolesOf: f => Approvers(f, agent: true));
        RecordEnvelope approved = run.Marked("APPROVED");
        RecordEnvelope drafted = Draft(run);
        RecordRule(run);
        run.Add("work-order", PolicyFixture.Body(approved), supersedes: approved.Id);
        run.Estate.SetClock(drafted.AtTime.AddMinutes(-5));
        run.Marked("REWORK");
        run.Estate.SetClock(drafted.AtTime.AddHours(1));
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("done", "ship"), (gate.State, gate.Decision!.Paths[0]));
        Assert.Equal("next", run.Step("release").State);

        // A second rule decision for the same round is refused by validation and counts for nothing.
        RecordEnvelope again = run.Add("observation", FlowDecisions.RuleBody(run.Step("gate") with { Decision = gate.Decision with { Paths = ["rework"], Rule = "2", At = gate.Decision.Since } }), subject: run.Goal.Reference);
        Assert.Contains(Errors(run.Estate.Load(), again), f => f.Code == "observation.flow-decision");
        Assert.Equal("ship", run.Step("gate").Decision!.Paths[0]);
    }

    [Fact]
    public void ARulesDefaultPathIsTakenWhenNoRuleHolds()
    {
        using var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule" }, rolesOf: f => Approvers(f, agent: true));
        Draft(run);
        FlowPosition.StepInstance pending = run.Step("gate");
        Assert.Equal(("next", "default", "default"), (pending.State, pending.Decision!.Decider, pending.Decision.Rule));
        Assert.Equal(["drop"], pending.Decision.Paths);
        Assert.StartsWith("no rule holds as the records stood at", pending.Decision.Reasons, StringComparison.Ordinal);
        RecordRule(run);
        AssertNoErrors(run);
        Assert.Equal(("done", "default"), (run.Step("gate").State, run.Step("gate").Decision!.Decider));
        Assert.Equal(("off", "off"), (run.Step("release").State, run.Step("publish").State));
    }

    [Fact]
    public void ARuleThenTheRoleDecidesByTheRuleWhenOneHoldsAndOtherwiseByTheRole()
    {
        using (var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule-then-role" }, rolesOf: f => Approvers(f, agent: false)))
        {
            run.Marked("APPROVED");
            Draft(run);
            Assert.Equal(("next", "rule"), (run.Step("gate").State, run.Step("gate").Decision!.Decider));
            RecordRule(run);
            AssertNoErrors(run);
            Assert.Equal(("done", "rule"), (run.Step("gate").State, run.Step("gate").Decision!.Decider));
        }

        using (var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule-then-role" }, rolesOf: f => Approvers(f, agent: false)))
        {
            Draft(run);
            FlowPosition.StepInstance gate = run.Step("gate");
            Assert.Equal(("stopped", "person", "pending"), (gate.State, gate.Stop, gate.Decision!.State));
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["ship"], "No rule settled it; ship.");
            AssertNoErrors(run);
            Assert.Equal(("done", "person"), (run.Step("gate").State, run.Step("gate").Decision!.Decider));
        }
    }

    [Fact]
    public void OneDecisionTakesOnePathOrSeveralThatMeetAtTheirJoin()
    {
        // Several at once: both paths' steps run, and the join waits on both.
        using (var run = new FlowFixture(Fork(), rolesOf: f => Approvers(f, agent: false)))
        {
            AssertNoErrors(run);
            Draft(run);
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["clips", "notes"], "Both.");
            AssertNoErrors(run);
            Assert.Equal(("next", "next", "blocked"), (run.Step("clips").State, run.Step("notes").State, run.Step("merge").State));
            run.Marked("CLIPS");
            Assert.Equal("blocked", run.Step("merge").State);
            run.Marked("NOTES");
            Assert.Equal("next", run.Step("merge").State);
        }

        // One: the other path's step is off, and the join waits only on the path taken.
        using (var run = new FlowFixture(Fork(), rolesOf: f => Approvers(f, agent: false)))
        {
            Draft(run);
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["clips"], "Clips only.");
            Assert.Equal(("next", "off"), (run.Step("clips").State, run.Step("notes").State));
            Assert.StartsWith("the decision at gate took path clips, not notes", run.Step("notes").Reasons[0], StringComparison.Ordinal);
            run.Marked("CLIPS");
            Assert.Equal("next", run.Step("merge").State);
        }

        // An exclusive transition refuses a decision naming two paths, and the position counts none.
        using (var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: false)))
        {
            Draft(run);
            RecordEnvelope both = Decide(run, run.Assign("gate", run.Estate.OperatorId), ["ship", "drop"], "Both.");
            Assert.Contains(Errors(run.Estate.Load(), both), f => f.Code == "result.decision");
            Assert.Equal("pending", run.Step("gate").Decision!.State);
        }
    }

    [Fact]
    public void ADecisionTheFlowRequiresOfAPersonIsNeverAnAgents()
    {
        JsonObject flow = Gate(transition => transition["person"] = true, configurable: false);
        using var run = new FlowFixture(flow, rolesOf: f => new JsonArray(FlowFixture.Role("writer", f.Estate.BuilderId), FlowFixture.Role("approver", f.Estate.BuilderId, f.Estate.OperatorId)));
        AssertNoErrors(run);
        Draft(run);

        // Routing passes over the agent the role names first and reaches the person.
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("stopped", "person", run.Estate.OperatorId), (gate.State, gate.Stop, gate.Principal));
        Assert.Contains("the decision is a person's, as the flow requires", gate.Reasons[0], StringComparison.Ordinal);

        // An agent's assignment of it, and an agent's decision, are refused and count for nothing.
        RecordEnvelope assignment = run.Assign("gate", run.Estate.BuilderId);
        Assert.Contains(Errors(run.Estate.Load(), assignment), f => f.Code == "assignment.step" && f.Message.Contains("person's", StringComparison.Ordinal));
        RecordEnvelope decided = Decide(run, assignment, ["ship"], "An agent's choice.");
        Assert.Contains(Errors(run.Estate.Load(), decided), f => f.Code == "result.decision");
        Assert.NotEqual("done", run.Step("gate").State);

        // An act never delegated is a person's whatever the flow's holder says, and its decider is never a rule.
        JsonObject undelegable = Gate(transition => transition["undelegable"] = "legal-commitment", configurable: false);
        Assert.DoesNotContain(TemplateDefinition.Validate(FlowFixture.Element(undelegable)), f => f.Severity == Severity.Error);
        Assert.True(FlowDefinition.Resolve(TemplateDefinition.Read(FlowFixture.Element(undelegable)), _ => throw new InvalidOperationException()).Step("gate")!.Transition!.PersonOnly);
    }

    [Fact]
    public void AnAgentsDecisionTakesEffectOnlyOnceItsReviewPasses()
    {
        JsonObject flow = Gate(transition => transition["review"] = "independent", configurable: false);
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: true));
        AssertNoErrors(run);
        Draft(run);
        RecordEnvelope assignment = run.Assign("gate", run.Estate.BuilderId);
        RecordEnvelope first = Decide(run, assignment, ["ship"], "Ready.");
        FlowPosition.StepInstance waiting = run.Step("gate");
        Assert.Equal(("in-review", "in-review"), (waiting.State, waiting.Decision!.State));
        FlowPosition.PendingReview review = Assert.Single(waiting.Reviews);
        Assert.Equal((first.Id, "independent"), (review.Subject.Id, review.Requirement.Independence));
        Assert.Equal("blocked", run.Step("release").State);

        // Findings send it back: the agent decides again, and the first decision never takes effect.
        run.Review(first, "findings");
        FlowPosition.StepInstance sentBack = run.Step("gate");
        Assert.Equal(("next", "pending", 0), (sentBack.State, sentBack.Decision!.State, sentBack.Decision.Paths.Count));
        Assert.Equal("blocked", run.Step("release").State);

        RecordEnvelope second = Decide(run, assignment, ["ship"], "Ready, with the fix.");
        Assert.Equal("in-review", run.Step("gate").State);
        RecordEnvelope passed = run.Review(second);
        FlowPosition.StepInstance gate = run.Step("gate");
        Assert.Equal(("done", passed.Id), (gate.State, gate.Decision!.Review!.Id));
        Assert.Equal(passed.AtTime, gate.DoneAt);

        // A person's decision takes no review.
        using var person = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: false));
        Draft(person);
        Decide(person, person.Assign("gate", person.Estate.OperatorId), ["ship"], "Ready.");
        Assert.Equal("done", person.Step("gate").State);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("attention")]
    public void ADeadlineTakesTheDefaultPathOrRaisesAttention(string rule)
    {
        JsonObject flow = Gate(transition => transition["deadline"] = rule, configurable: true);
        StepOf(flow, "gate")["when"] = new JsonObject { ["instant"] = true, ["deadline"] = "PT2H" };
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: false));
        AssertNoErrors(run);
        run.Estate.SetClock(Friday.AddSeconds(-1));
        DateTimeOffset deadline = Draft(run).AtTime.AddHours(2);
        Assert.Equal(("stopped", "person"), (Gate(run, Friday.AddHours(1)).State, Gate(run, Friday.AddHours(1)).Stop));
        Assert.Equal(deadline, Gate(run, Friday.AddHours(1)).Due!.Deadline);

        FlowPosition.StepInstance missed = Gate(run, Friday.AddHours(3));
        if (rule == "default")
        {
            // The deadline takes the default path, which goal step records; a decision recorded after the deadline counts for nothing.
            Assert.Equal(("next", "deadline", "deadline"), (missed.State, missed.Decision!.Decider, missed.Decision.Rule));
            Assert.Equal(["drop"], missed.Decision.Paths);
            run.Estate.SetClock(Friday.AddHours(3));
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["ship"], "Too late.");
            Assert.Equal("deadline", Gate(run, Friday.AddHours(3).AddMinutes(1)).Decision!.Decider);
            RecordEnvelope recorded = run.Add("observation", FlowDecisions.RuleBody(Gate(run, Friday.AddHours(3).AddMinutes(1))), subject: run.Goal.Reference);
            FlowPosition.StepInstance defaulted = Gate(run, Friday.AddHours(3).AddMinutes(2));
            Assert.Equal(("done", "deadline", recorded.Id), (defaulted.State, defaulted.Decision!.Decider, defaulted.Decision.Record!.Id));
            Assert.Equal(["drop"], defaulted.Decision.Paths);
            return;
        }

        Assert.Equal(("stopped", "attention", "missed", "attention"), (missed.State, missed.Stop, missed.Attention!.Kind, missed.Decision!.State));
        FlowStops.RunStop stop = Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, Friday.AddHours(3)).Stops, s => s.Step?.Key == "gate");
        Assert.Equal(("attention", run.Estate.OperatorId), (stop.Kind, stop.Principal));

        // A decision recorded after the deadline waits for the sponsor's answer before it takes effect.
        RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
        run.Estate.SetClock(Friday.AddHours(3).AddMinutes(1));
        Decide(run, assignment, ["ship"], "Late, but ready.");
        FlowPosition.StepInstance waiting = Gate(run, Friday.AddHours(3).AddMinutes(10));
        Assert.Equal(("stopped", "attention", "attention"), (waiting.State, waiting.Stop, waiting.Decision!.State));
        Assert.Equal("blocked", Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, Friday.AddHours(3).AddMinutes(10)).Steps, s => s.Step.Key == "release").State);

        // Once the sponsor answers, the decision takes effect.
        Answer(run, missed.Attention.Trigger, Friday.AddHours(3).AddMinutes(20));
        FlowPosition.StepInstance late = Gate(run, Friday.AddHours(4));
        Assert.Equal(("done", "taken"), (late.State, late.Decision!.State));
    }

    [Fact]
    public void APathBackCarriesItsMaximumCountPastWhichAttentionIsRaised()
    {
        using var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: true));
        Draft(run);
        for (int round = 0; round < 2; round++)
        {
            RecordEnvelope assignment = run.Assign("gate", run.Estate.BuilderId);
            Decide(run, assignment, ["rework"], $"Round {round + 1}: rework the lyrics.");
            AssertNoErrors(run);

            // The run goes back to draft, which runs again from a fresh assignment; the gate decides again once it is done again.
            FlowPosition.StepInstance back = run.Step("gate");
            Assert.Equal(("blocked", "returning"), (back.State, back.Decision!.State));
            FlowPosition.StepInstance draft = run.Step("draft");
            Assert.Equal(("next", true), (draft.State, draft.Rerun));
            Assert.StartsWith("reopened: the decision at gate, by Test Builder in result ", draft.Reasons[0], StringComparison.Ordinal);
            Assert.Contains("took path rework back to draft", draft.Reasons[0], StringComparison.Ordinal);
            Draft(run);
            FlowPosition.StepInstance again = run.Step("gate");
            Assert.Equal(("next", "pending", true), (again.State, again.Decision!.State, again.Rerun));
        }

        // The third time past its limit of two: attention, asked of the sponsor, and draft is not reopened.
        RecordEnvelope third = Decide(run, run.Assign("gate", run.Estate.BuilderId), ["rework"], "Round 3: rework again.");
        FlowPosition.StepInstance looped = run.Step("gate");
        Assert.Equal(("stopped", "attention", "loop", 2), (looped.State, looped.Stop, looped.Attention!.Kind, looped.Decision!.Taken));
        Assert.Equal(third.Id, looped.Attention.Trigger.Id);
        Assert.Equal("done", run.Step("draft").State);
        FlowStops.RunStop stop = Assert.Single(run.Position().Stops, s => s.Step?.Key == "gate");
        Assert.Equal(("attention", run.Estate.OperatorId), (stop.Kind, stop.Principal));

        // Once the sponsor answers, the path is taken once more.
        Answer(run, third, run.Estate.Clock);
        Assert.Equal("returning", run.Step("gate").Decision!.State);
    }

    [Fact]
    public void AVentureChoosesTheDeciderWhereTheFlowAllowsTheNarrowestWinning()
    {
        // The estate's defaults choose the rule; the goal's own options choose the role and win.
        using (var run = new FlowFixture(Gate(), run: false))
        {
            Defaults(run, new JsonObject { ["decider.gate"] = "rule" });
            RecordEnvelope goal = run.Add("goal", run.GoalBody(roles: Approvers(run, agent: true)));
            run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);
            EstateModel model = run.Estate.Load();
            FlowDefinition flow = FlowRuns.Resolve(model, goal).Flow!;
            FlowRuns.Configuration configured = FlowRuns.Configure(model, goal, flow);
            Assert.Equal("rule", configured.Decider(flow.Step("gate")!));
            Assert.StartsWith("the estate defaults", configured.Values["decider.gate"].Source, StringComparison.Ordinal);

            RecordEnvelope own = run.Add("goal", run.GoalBody(new JsonObject { ["decider.gate"] = "role" }, roles: Approvers(run, agent: true)));
            model = run.Estate.Load();
            Assert.Equal("role", FlowRuns.Configure(model, own, flow).Decider(flow.Step("gate")!));
            Assert.Equal("the goal's plan", FlowRuns.Configure(model, own, flow).Values["decider.gate"].Source);
        }

        // A decider the flow does not let a venture choose, and a value it does not allow, are refused.
        using (var run = new FlowFixture(Gate(configurable: false), run: false))
        {
            RecordEnvelope fixedDecider = run.Add("goal", run.GoalBody(new JsonObject { ["decider.gate"] = "rule" }));
            Assert.Contains(Errors(run.Estate.Load(), fixedDecider), f => f.Code == "flow.decider");
        }

        using (var run = new FlowFixture(Gate(), run: false))
        {
            RecordEnvelope unknown = run.Add("goal", run.GoalBody(new JsonObject { ["decider.gate"] = "coin" }));
            Assert.Contains(Errors(run.Estate.Load(), unknown), f => f.Code == "flow.option");
        }
    }

    [Fact]
    public void ThePhaseGateIsOnePredefinedSetOfPaths()
    {
        JsonObject flow = Gate(configurable: false);
        JsonObject transition = StepOf(flow, "gate")["transition"]!.AsObject();
        transition.Remove("paths");
        transition.Remove("rules");
        transition.Remove("default");
        transition["gate"] = new JsonObject { ["go"] = "release", ["recycle"] = "draft", ["limit"] = 2 };
        TemplateDefinition read = TemplateDefinition.Read(FlowFixture.Element(flow));
        FlowTransition gate = FlowDefinition.Resolve(read, _ => throw new InvalidOperationException()).Step("gate")!.Transition!;
        Assert.Equal(["go", "recycle", "hold", "kill"], gate.Paths.Select(p => p.Name));
        Assert.Equal(("release", "draft", "gate", true), (gate.Path("go")!.Step, gate.Path("recycle")!.Step, gate.Path("hold")!.Step, gate.Path("kill")!.End));

        // Hold keeps the step with its decider, who decides again; go then takes it on.
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: false));
        AssertNoErrors(run);
        Draft(run);
        RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
        Decide(run, assignment, ["hold"], "Not yet.");
        FlowPosition.StepInstance held = run.Step("gate");
        Assert.Equal(("stopped", "person", "held", false), (held.State, held.Stop, held.Decision!.State, held.Rerun));
        Decide(run, assignment, ["go"], "Now.");
        Assert.Equal(("done", "next"), (run.Step("gate").State, run.Step("release").State));
    }

    [Fact]
    public void TheFirstDecisionThatStandsIsFinalForItsRound()
    {
        // Review finding 2: a later decision on the same assignment never replaces one in effect, and validation reports it.
        using var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: false));
        Draft(run);
        RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
        Decide(run, assignment, ["ship"], "Ship it.");
        run.Marked("RELEASED");
        RecordEnvelope again = Decide(run, assignment, ["drop"], "Changed my mind.");
        Assert.Contains(Errors(run.Estate.Load(), again), f => f.Code == "result.decision" && f.Message.Contains("stands and is final", StringComparison.Ordinal));
        Assert.Equal(("done", "ship"), (run.Step("gate").State, run.Step("gate").Decision!.Paths[0]));
        Assert.Equal(("done", "stopped"), (run.Step("release").State, run.Step("publish").State));
    }

    [Fact]
    public void OnlyTheStepsCurrentAssignmentDecides()
    {
        // Review finding 3: a displaced holder's decision counts for nothing, and validation reports it.
        using var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: false));
        Draft(run);
        RecordEnvelope displaced = run.Assign("gate", run.Estate.OperatorId);
        run.Assign("gate", run.Estate.OperatorId);
        RecordEnvelope decided = Decide(run, displaced, ["ship"], "From the old assignment.");
        Assert.Contains(Errors(run.Estate.Load(), decided), f => f.Code == "result.decision" && f.Message.Contains("no longer holds step gate", StringComparison.Ordinal));
        Assert.Equal("pending", run.Step("gate").Decision!.State);
        Assert.Equal("blocked", run.Step("release").State);
    }

    [Fact]
    public void APathBackCountsTheDecisionsThatTookItOneARoundNeverFreshAssignments()
    {
        // Review finding 6: assignments made for other reasons never count, and a decision sent back and decided again counts once.
        using (var run = new FlowFixture(Gate(), options: new JsonObject { ["decider.gate"] = "rule" }, rolesOf: f => Approvers(f, agent: true)))
        {
            run.Marked("REWORK");
            Draft(run);
            RecordRule(run);
            Assert.Equal("returning", run.Step("gate").Decision!.State);
            run.Assign("draft", run.Estate.BuilderId);
            run.Assign("draft", run.Estate.BuilderId);
            Draft(run);
            FlowPosition.StepInstance second = run.Step("gate");
            Assert.Equal(("next", "rework"), (second.State, second.Decision!.Paths[0]));
            RecordRule(run);
            Assert.Equal(("returning", 1), (run.Step("gate").Decision!.State, run.Step("gate").Decision!.Taken));
        }

        using (var run = new FlowFixture(Gate(transition => transition["review"] = "independent", configurable: false), rolesOf: f => Approvers(f, agent: true)))
        {
            Draft(run);
            RecordEnvelope assignment = run.Assign("gate", run.Estate.BuilderId);
            run.Review(Decide(run, assignment, ["rework"], "Rework."), "findings");
            run.Review(Decide(run, assignment, ["rework"], "Rework, as reviewed."));
            Assert.Equal("returning", run.Step("gate").Decision!.State);
            Draft(run);
            RecordEnvelope next = run.Assign("gate", run.Estate.BuilderId);
            run.Review(Decide(run, next, ["rework"], "Rework again."));
            Assert.Equal(("returning", 1), (run.Step("gate").Decision!.State, run.Step("gate").Decision!.Taken));
        }
    }

    [Fact]
    public void AReturnMadeStaysMadeWhenTheDecidingStepIsBlockedLater()
    {
        // Review finding 7: once a decision returned the run to draft, a later block of the gate does not undo the return.
        JsonObject flow = Gate();
        StepOf(flow, "gate")["requires"] = new JsonArray(new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = "HOLD" }, ["max"] = 0 });
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: false));
        AssertNoErrors(run);
        Draft(run);
        Decide(run, run.Assign("gate", run.Estate.OperatorId), ["rework"], "Rework.");
        Assert.Equal(("next", true), (run.Step("draft").State, run.Step("draft").Rerun));
        run.Marked("HOLD");
        Assert.Equal("blocked", run.Step("gate").State);
        Assert.Equal(("next", true), (run.Step("draft").State, run.Step("draft").Rerun));
    }

    [Fact]
    public void AJoinIsOffWhenEveryPathItJoinsIsOffAndABranchNotTakenIsOffWhateverItsPredicates()
    {
        // Review findings 8 and 9: a path into a branch turns the joined paths off, the join with them; a branch not taken stays off.
        using (var run = new FlowFixture(Fork(withdrawal: true), rolesOf: f => Approvers(f, agent: false)))
        {
            AssertNoErrors(run);
            Draft(run);
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["withdraw"], "Withdraw instead.");
            Assert.Equal(("off", "off", "off"), (run.Step("clips").State, run.Step("notes").State, run.Step("merge").State));
            Assert.Equal("next", run.Step("retract").State);
        }

        using (var run = new FlowFixture(Fork(withdrawal: true), rolesOf: f => Approvers(f, agent: false)))
        {
            run.Marked("WITHDRAW");
            Draft(run);
            Decide(run, run.Assign("gate", run.Estate.OperatorId), ["clips"], "Clips only.");
            FlowPosition.StepInstance retract = run.Step("retract");
            Assert.Equal("off", retract.State);
            Assert.StartsWith("the decision at gate took path clips, not withdraw", retract.Reasons[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEndPathTurnsOffEveryStepNotDoneWhateverItsOrder()
    {
        // Review finding 10: a step declared before the gate that is not done is off too, and the run reads complete.
        JsonObject flow = Gate();
        JsonObject notes = Operation("notes", "NOTES");
        flow["steps"]!.AsArray().Insert(1, notes);
        StepOf(flow, "gate")["after"] = new JsonArray("draft");
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: false));
        AssertNoErrors(run);
        Draft(run);
        Assert.Equal("next", run.Step("notes").State);
        Decide(run, run.Assign("gate", run.Estate.OperatorId), ["drop"], "Shelved.");
        Assert.Equal(("off", "off", "off"), (run.Step("notes").State, run.Step("release").State, run.Step("publish").State));
        Assert.True(run.Position().Complete);
    }

    [Fact]
    public void ADecisionNeverTurnsOffARequiredReservedStepOrOneAPolicyRequires()
    {
        // Review finding 11: goal decide refuses such a path, naming the step; validation reports a recorded one; the step stays on.
        using (var run = new FlowFixture(Gate(requiredPublish: true), rolesOf: f => Approvers(f, agent: false)))
        {
            AssertNoErrors(run);
            Draft(run);
            RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
            FlowDecisions.Plan refused = FlowDecisions.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "gate", ["drop"], "Shelved.", [], run.Estate.Clock);
            Assert.Contains(refused.Refusals, r => r.StartsWith($"{FlowDecisions.ProtectedCode}: path drop would turn off step publish: it is a required reserved step (publish)", StringComparison.Ordinal));
            RecordEnvelope dropped = Decide(run, assignment, ["drop"], "Shelved anyway.");
            Assert.Contains(Errors(run.Estate.Load(), dropped), f => f.Code == "result.decision-protected" && f.Message.Contains("step publish", StringComparison.Ordinal));
            Assert.Equal(("off", "stopped"), (run.Step("release").State, run.Step("publish").State));
            Assert.False(run.Position().Complete);
        }

        using (var run = new FlowFixture(Gate(), rolesOf: f => Approvers(f, agent: false)))
        {
            JsonObject body = run.Policies.PolicyBody("gate-publish", duty: false);
            body["rules"] = new JsonArray(new JsonObject { ["key"] = "publish", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1, ["parameters"] = new JsonObject { ["flow"] = "example/gate", ["steps"] = new JsonArray("publish") } });
            run.Policies.Policy(body);
            Draft(run);
            RecordEnvelope assignment = run.Assign("gate", run.Estate.OperatorId);
            FlowDecisions.Plan refused = FlowDecisions.Prepare(run.Estate.Load(), run.Goal, run.Estate.OperatorId, "gate", ["drop"], "Shelved.", [], run.Estate.Clock);
            Assert.Contains(refused.Refusals, r => r.StartsWith($"{FlowDecisions.ProtectedCode}: path drop would turn off step publish: estate policy gate-publish requires it", StringComparison.Ordinal));
            RecordEnvelope dropped = Decide(run, assignment, ["drop"], "Shelved anyway.");
            Assert.Contains(Errors(run.Estate.Load(), dropped), f => f.Code == "result.decision-protected");
            Assert.Equal("stopped", run.Step("publish").State);
        }
    }

    [Fact]
    public void AStepOfARoleTheFlowMarksAPersonsIsDecidedOnlyByAPerson()
    {
        JsonObject flow = Gate(configurable: false);
        flow["roles"]![1]!["holder"] = "person";
        using var run = new FlowFixture(flow, rolesOf: f => Approvers(f, agent: true));
        Draft(run);
        RecordEnvelope decided = Decide(run, run.Assign("gate", run.Estate.BuilderId), ["ship"], "An agent's choice.");
        Assert.Contains(Errors(run.Estate.Load(), decided), f => f.Code == "result.decision" && f.Message.Contains("marks the approver role a person's", StringComparison.Ordinal));
        Assert.NotEqual("done", run.Step("gate").State);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "one-path", "flow.transition" },
        { "path-to-two-places", "flow.transition" },
        { "rule-without-default", "flow.transition" },
        { "rule-naming-an-unknown-path", "flow.transition" },
        { "default-back-to-itself", "flow.transition" },
        { "person-decided-by-a-rule", "flow.transition" },
        { "role-on-a-work-step", "flow.transition" },
        { "join-without-several", "flow.transition" },
        { "deadline-default-without-default", "flow.transition" },
        { "forward-step-not-waiting", "flow.transition" },
        { "back-without-limit", "flow.transition" },
        { "back-to-an-operation", "flow.transition" },
        { "step-waiting-outside-the-paths", "flow.transition" },
        { "optional-decision-step", "flow.transition" },
        { "decision-in-a-branch", "flow.transition" },
        { "rule-transition-on-an-operation", "flow.transition" },
        { "transition-on-a-reserved-step", "flow.transition" },
        { "failure-route-to-a-decision-step", "flow.failure" },
        { "unknown-decider", "body.enum" },
        { "unknown-undelegable", "body.enum" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void TheContractRefusesATransitionThatDoesNotHold(string change, string code)
    {
        JsonObject flow = Gate();
        JsonObject transition = StepOf(flow, "gate")["transition"]!.AsObject();
        JsonArray paths = transition["paths"]!.AsArray();
        switch (change)
        {
            case "one-path": transition["paths"] = new JsonArray(paths[0]!.DeepClone()); break;
            case "path-to-two-places": paths[0]!["end"] = true; break;
            case "rule-without-default": transition.Remove("default"); break;
            case "rule-naming-an-unknown-path": transition["rules"]![0]!["path"] = "elsewhere"; break;
            case "default-back-to-itself": paths.Add(new JsonObject { ["name"] = "again", ["step"] = "gate", ["limit"] = 1 }); transition["default"] = "again"; break;
            case "person-decided-by-a-rule": transition["person"] = true; break;
            case "role-on-a-work-step": StepOf(flow, "draft")["transition"] = transition.DeepClone(); break;
            case "join-without-several": transition["join"] = "publish"; break;
            case "deadline-default-without-default": transition["deadline"] = "default"; transition.Remove("default"); transition.Remove("rules"); transition.Remove("configurable"); break;
            case "forward-step-not-waiting": StepOf(flow, "release")["after"] = new JsonArray("draft"); break;
            case "back-without-limit": paths[1]!.AsObject().Remove("limit"); break;
            case "back-to-an-operation": paths[1]!["step"] = "prepare"; flow["steps"]!.AsArray().Insert(0, FlowFixture.Step("prepare", "operation", "writer", done: new JsonArray(FlowFixture.Marker("PREPARED")))); StepOf(flow, "draft")["after"] = new JsonArray("prepare"); break;
            case "step-waiting-outside-the-paths": flow["steps"]!.AsArray().Add(Operation("aside", "ASIDE", "gate")); break;
            case "optional-decision-step": StepOf(flow, "gate").Remove("required"); StepOf(flow, "gate")["optional"] = true; StepOf(flow, "gate")["default"] = "on"; break;
            case "decision-in-a-branch": flow["branches"] = new JsonArray(new JsonObject { ["name"] = "aside", ["when"] = new JsonArray(FlowFixture.Marker("ASIDE")), ["steps"] = new JsonArray("gate") }); break;
            case "unknown-decider": transition["decider"] = "coin"; break;
            case "unknown-undelegable": transition["undelegable"] = "lunch"; break;
            case "rule-transition-on-an-operation":
                // Review finding 1: a path back from a step that is not a decision step is never left undecided.
                JsonObject check = Operation("check", "CHECKED", "draft");
                check["transition"] = new JsonObject
                {
                    ["paths"] = new JsonArray(new JsonObject { ["name"] = "rework", ["step"] = "draft", ["limit"] = 2 }, new JsonObject { ["name"] = "on", ["step"] = "gate" }),
                    ["decider"] = "rule", ["rules"] = new JsonArray(new JsonObject { ["path"] = "on", ["when"] = new JsonArray(FlowFixture.Marker("ON")) }), ["default"] = "rework",
                };
                flow["steps"]!.AsArray().Insert(1, check);
                StepOf(flow, "gate")["after"] = new JsonArray("check");
                break;
            case "transition-on-a-reserved-step": StepOf(flow, "gate")["reserved"] = "publish"; StepOf(flow, "gate")["action"] = FlowFixture.PublishCommand; transition["decider"] = "rule"; transition.Remove("configurable"); break;
            case "failure-route-to-a-decision-step":
                // Review finding 4: a failure route never returns the run to a decision step.
                JsonObject polish = FlowFixture.Step("polish", "work", "writer", outputs: new JsonArray(FlowFixture.FileItem("polished")));
                polish["after"] = new JsonArray("release");
                polish["failure"] = new JsonObject { ["route"] = "step", ["step"] = "gate" };
                flow["steps"]!.AsArray().Add(polish);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        IReadOnlyList<Finding> findings = TemplateDefinition.Validate(FlowFixture.Element(flow));
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Code == code);
    }

    /// <summary>
    /// A release gate: draft the lyrics; the gate, decided by the approver role, ships (release, then the operator's publication), reworks
    /// (back to draft, at most twice) or drops (ends the run); a venture may choose a rule (approved ships, rework reworks, else drop) or a
    /// rule then the role. Publication is optional unless asked required, so that drop may turn it off.
    /// </summary>
    private static JsonObject Gate(Action<JsonObject>? change = null, bool configurable = true, bool requiredPublish = false)
    {
        var transition = new JsonObject
        {
            ["paths"] = new JsonArray(
                new JsonObject { ["name"] = "ship", ["step"] = "release" },
                new JsonObject { ["name"] = "rework", ["step"] = "draft", ["limit"] = 2 },
                new JsonObject { ["name"] = "drop", ["end"] = true }),
            ["decider"] = "role",
        };
        if (configurable)
        {
            transition["configurable"] = new JsonArray("rule", "rule-then-role");
            transition["rules"] = new JsonArray(
                new JsonObject { ["path"] = "ship", ["when"] = new JsonArray(FlowFixture.Marker("APPROVED")) },
                new JsonObject { ["path"] = "rework", ["when"] = new JsonArray(FlowFixture.Marker("REWORK")) });
            transition["default"] = "drop";
        }

        change?.Invoke(transition);
        if (!configurable && transition["deadline"]?.GetValue<string>() == "default")
        {
            transition["default"] = "drop";
        }

        JsonObject gate = FlowFixture.Step("gate", "decision", "approver");
        gate["transition"] = transition;
        JsonObject release = Operation("release", "RELEASED", "gate");
        JsonObject publish = FlowFixture.Step("publish", "operation", "decider", optional: requiredPublish ? null : "on", reserved: "publish", done: new JsonArray(FlowFixture.Marker("PUBLISHED")), action: FlowFixture.PublishCommand);
        return Flow("example/gate", gate, release, publish);
    }

    /// <summary>A fork: draft; the gate takes clips, notes or both at once; merge joins them; with a withdrawal, a path into its branch.</summary>
    private static JsonObject Fork(bool withdrawal = false)
    {
        JsonObject gate = FlowFixture.Step("gate", "decision", "approver");
        var paths = new JsonArray(new JsonObject { ["name"] = "clips", ["step"] = "clips" }, new JsonObject { ["name"] = "notes", ["step"] = "notes" });
        if (withdrawal)
        {
            paths.Add(new JsonObject { ["name"] = "withdraw", ["branch"] = "withdrawal" });
        }

        gate["transition"] = new JsonObject { ["paths"] = paths, ["decider"] = "role", ["several"] = true, ["join"] = "merge" };
        JsonObject flow = Flow("example/fork", gate, Operation("clips", "CLIPS", "gate"), Operation("notes", "NOTES", "gate"), Operation("merge", "MERGED", "clips", "notes"));
        if (withdrawal)
        {
            JsonObject retract = FlowFixture.Step("retract", "operation", "writer", optional: "on", done: new JsonArray(FlowFixture.Marker("RETRACTED")));
            retract["after"] = new JsonArray("draft");
            flow["steps"]!.AsArray().Add(retract);
            flow["branches"] = new JsonArray(new JsonObject { ["name"] = "withdrawal", ["when"] = new JsonArray(FlowFixture.Marker("WITHDRAW")), ["steps"] = new JsonArray("retract") });
        }

        return flow;
    }

    private static JsonObject Flow(string id, params JsonObject[] after) => new()
    {
        ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = id, ["version"] = "1.0.0", ["purpose"] = "Draft, then decide what runs next.",
        ["subject"] = new JsonObject
        {
            ["summary"] = "a recording",
            ["fields"] = new JsonArray(
                new JsonObject { ["name"] = "recording", ["type"] = "record", ["recordType"] = "work-order" },
                new JsonObject { ["name"] = "tracks", ["type"] = "list" }),
        },
        ["roles"] = new JsonArray(
            new JsonObject { ["id"] = "writer", ["holder"] = "either", ["brief"] = "Draft the material." },
            new JsonObject { ["id"] = "approver", ["holder"] = "either", ["brief"] = "Decide what runs next." },
            new JsonObject { ["id"] = "decider", ["holder"] = "person", ["brief"] = "Publish." }),
        ["never"] = new JsonArray("never publish without the operator"),
        ["steps"] = new JsonArray([FlowFixture.Step("draft", "work", "writer", outputs: new JsonArray(FlowFixture.FileItem("lyrics"))), .. after]),
    };

    private static JsonObject Operation(string key, string marker, params string[] after)
    {
        JsonObject step = FlowFixture.Step(key, "operation", "writer", done: new JsonArray(FlowFixture.Marker(marker)));
        step["after"] = new JsonArray(after.Select(a => (JsonNode)a).ToArray());
        return step;
    }

    private static JsonObject StepOf(JsonObject flow, string key) => flow["steps"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == key)!.AsObject();

    /// <summary>The goal's roles: the writer is the builder agent; the approver is the agent, or the operator, a person.</summary>
    private static JsonArray Approvers(FlowFixture run, bool agent) =>
        new(FlowFixture.Role("writer", run.Estate.BuilderId), FlowFixture.Role("approver", agent ? run.Estate.BuilderId : run.Estate.OperatorId));

    /// <summary>The draft step done: the builder's assignment and its completed result naming the lyrics.</summary>
    private static RecordEnvelope Draft(FlowFixture run) => run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash((char)('a' + (run.Estate.Clock.Second % 6))), null));

    /// <summary>The decider's attempt and its completed result recording a decision on an assignment of the gate.</summary>
    private static RecordEnvelope Decide(FlowFixture run, RecordEnvelope assignment, string[] paths, string reasons, params RecordEnvelope[] evidence) =>
        run.Decide(assignment, paths, reasons, evidence);

    /// <summary>The rule's decision of the gate, recorded by the operator as goal step records it, from the position as it reads now.</summary>
    private static RecordEnvelope RecordRule(FlowFixture run) =>
        run.Add("observation", FlowDecisions.RuleBody(run.Step("gate")), subject: run.Goal.Reference);

    private static FlowPosition.StepInstance Gate(FlowFixture run, DateTimeOffset at) =>
        Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, at).Steps, s => s.Step.Key == "gate");

    /// <summary>The sponsor's answer to an ask about a record that raised attention.</summary>
    private static void Answer(FlowFixture run, RecordEnvelope trigger, DateTimeOffset at)
    {
        run.Estate.SetClock(at);
        RecordEnvelope ask = run.Add("ask", new JsonObject
        {
            ["to"] = run.Estate.OperatorId, ["question"] = "How does the run go on?", ["recommendation"] = "Let it go on.",
            ["consequence"] = "It waits.", ["goal"] = run.Goal.Id, ["state"] = "open",
        }, builder: true, subject: trigger.Reference);
        JsonObject answer = run.Policies.DecisionBody();
        answer["answers"] = new JsonArray(ask.Id);
        run.Add("decision", answer);
    }

    private static RecordEnvelope Defaults(FlowFixture run, JsonObject options) => run.Add("template-defaults", new JsonObject
    {
        ["template"] = FlowFixture.Pin(run.Flow), ["scope"] = new JsonObject { ["kind"] = "estate" }, ["options"] = options, ["reason"] = "The venture's decider.",
    });

    private static void AssertNoErrors(FlowFixture run) =>
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));

    private static List<Finding> Errors(EstateModel model, RecordEnvelope record) =>
        model.Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToList();
}
