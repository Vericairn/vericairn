// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// One routed step at a time, the smallest form of Community's conductor (the estate's design/goals.md §§ 6, 8 and 11): a goal's next
/// eligible step goes to the principal its roster names, an agent's through the worker within the custody precondition, a person's as
/// an assignment and an ask, and a step no principal is available for as an ask to the sponsor; and a person takes over a step an
/// agent holds, the hand-over being a record. Every harness is the stand-in.
/// </summary>
[Collection("home")]
public class GoalStepTests
{
    [Fact]
    public void ARevokedRequestCannotStoreARoutedAssignmentAndAsk()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        Order(run, "W-0", ""","role":"publisher" """);
        DeviceStanding device = run.Service.Standing(run.OperatorDevice)!;
        using (EstateService.RequestDeviceScope request = run.Service.ForDeviceRequest(device.Identity, device.PublicKey))
        {
            RunEstate.Ok(run.Service.DeviceRevoke(new RevokeRequest(device.Identity, run.Estate.OperatorId, "Revoked after authentication.")));
            GoalStepResponse refused = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
            Assert.False(refused.Ok);
            Assert.Equal("W-0", refused.Step);
            Assert.Equal("denied", refused.Problem!.Code);
            Assert.Equal(refused.Problem, request.Refusal);
            Assert.Null(refused.Assignment);
            Assert.Null(refused.Ask);
            Assert.Single(run.Service.Records("assignment"));
            Assert.Empty(run.Service.Records("ask"));
        }

        // The embedded caller has no device-authenticated request; ending the scope restores that caller.
        GoalStepResponse asked = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
        Assert.True(asked.Ok, asked.Message);
        Assert.NotNull(asked.Assignment);
        Assert.NotNull(asked.Ask);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void OneStepAtATimeRunsAnAgentAndAsksAPersonOrTheSponsor()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        Order(run, "W-3", ""","role":"publisher" """);
        Order(run, "W-4", ""","role":"planner" """);

        // W-1 is held by the builder's assignment: its run starts through Claude Code's launcher, and nothing new is assigned.
        GoalStepResponse first = Step(run, "first");
        Assert.True(first.Ok, first.Message);
        Assert.Equal(("run", "W-1", "agent", builder), (first.Outcome, first.Step, first.Route, first.Principal));
        Assert.StartsWith($"W-1 is routed to the agent Test Builder [principal {builder}] through claude-code: ", first.Message, StringComparison.Ordinal);
        Assert.Null(first.Assignment);
        Assert.Equal("observed-success", run.Settled(first.Run!.Run).Status);
        Assert.Equal(run.Assignment, AgentRuns.Find(run.Estate.Load(), first.Run.Run)!.Assignment);
        Done(run, run.Assignment);

        // W-2, with its result awaiting review behind it: routing assigns W-2 to the builder, from the work order and the goal, and its run starts.
        GoalStepResponse second = Step(run, "second");
        Assert.True(second.Ok, second.Message);
        Assert.Equal(("run", "W-2"), (second.Outcome, second.Step));
        RecordEnvelope assigned = run.Estate.Load().ById[second.Assignment!.Id];
        Assert.Equal("observed-success", run.Settled(second.Run!.Run).Status);
        JsonObject given = Assert.Single(FakeHarness.Reports(run.Report("second")));
        string exported = File.ReadAllText(Path.Combine(given["directory"]!.GetValue<string>(), ".vericairn", "brief.md"));
        Assert.Contains("## Context package", exported, StringComparison.Ordinal);
        Assert.Contains(assigned.ComputedHash, exported, StringComparison.Ordinal);
        Assert.Contains("## Assignment " + assigned.Id, exported, StringComparison.Ordinal);
        var custody = new Workers.RunDirectory(run.Service.Session.Manifest.Estate, second.Run.Run);
        using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(custody.ContextFile));
        string package = retained.RootElement.GetProperty("context").GetProperty("hash").GetString()!;
        Assert.Equal(package, AgentRuns.Find(run.Estate.Load(), second.Run.Run)!.Intent.Body.GetProperty("execution").GetProperty("contextPackage").GetString());
        Assert.Contains(package, exported, StringComparison.Ordinal);
        Assert.Empty(run.Service.Objects());
        Assert.Equal(builder, EstateModel.BodyString(assigned, "contributor"));
        Assert.StartsWith("Routed by roster (builder, from the estate's roster): the first available of the builder role's principals", EstateModel.BodyString(assigned, "note"), StringComparison.Ordinal);
        JsonElement brief = assigned.Body.GetProperty("brief");
        Assert.Equal(["work-order", "goal"], brief.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("type").GetString()));
        Assert.Equal(["widget, a work branch"], brief.GetProperty("workspace").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal("The exit question is answered yes: Does the guide describe the widget?", brief.GetProperty("reviewCriteria")[0].GetString());
        Assert.Contains(brief.GetProperty("prohibitedEffects").EnumerateArray(), p => p.GetString() == "no change outside the widget");
        Assert.Contains(brief.GetProperty("prohibitedEffects").EnumerateArray(), p => p.GetString() == ReservedEffects);
        Done(run, second.Assignment.Id);

        // W-3 is a reserved action: it is assigned to the operator, who holds publication, and asked of them, the recommendation first.
        GoalStepResponse third = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(third.Ok, third.Message);
        Assert.Equal(("ask", "W-3", "reserved", op), (third.Outcome, third.Step, third.Route, third.Principal));
        RecordEnvelope ask = run.Estate.Load().ById[third.Ask!.Id];
        Assert.Equal((op, run.Goal, "open"), (EstateModel.BodyString(ask, "to"), EstateModel.BodyString(ask, "goal"), EstateModel.BodyString(ask, "state")));
        Assert.Equal("work-order", ask.Subject!.Type);
        Assert.StartsWith("Carry it yourself, from vericairn work brief W-3: no agent carries a reserved action without a grant in force", EstateModel.BodyString(ask, "recommendation"), StringComparison.Ordinal);
        Assert.Equal("W-3 waits until you act, and nothing reserved is done for you; the goal's other steps go on.", EstateModel.BodyString(ask, "consequence"));
        Assert.Equal(op, EstateModel.BodyString(run.Estate.Load().ById[third.Assignment!.Id], "contributor"));

        // W-4's role has no principal: the sponsor is asked to assign someone or change the roster, and no one is assigned it.
        GoalStepResponse fourth = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.Equal(("ask", "W-4", "unavailable", op), (fourth.Outcome, fourth.Step, fourth.Route, fourth.Principal));
        Assert.Null(fourth.Assignment);
        RecordEnvelope sponsorAsk = run.Estate.Load().ById[fourth.Ask!.Id];
        Assert.Equal(["assign someone", "change the roster"], sponsorAsk.Body.GetProperty("options").EnumerateArray().Select(o => o.GetString()));
        Assert.Contains("the estate's roster names no principal for the planner role", EstateModel.BodyString(sponsorAsk, "question"), StringComparison.Ordinal);
        Assert.StartsWith("Name a principal for the planner role in the roster", EstateModel.BodyString(sponsorAsk, "recommendation"), StringComparison.Ordinal);

        // Every eligible step is held now; both routing asks and the two missing-attempt warnings wait on the operator.
        GoalStepResponse fifth = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(fifth.Ok);
        Assert.Equal("none", fifth.Outcome);
        Assert.Contains("W-1 is review", fifth.Message, StringComparison.Ordinal);
        Assert.Contains("W-3 is assigned", fifth.Message, StringComparison.Ordinal);
        Assert.Contains("W-4 is asked", fifth.Message, StringComparison.Ordinal);
        Assert.Equal(4, Assert.Single(run.Service.Asks(op).Json.GetProperty("principals").EnumerateArray()).GetProperty("waiting").GetInt32());
        Assert.Empty(run.Service.Records("acceptance"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepIsRefusedBeforeAnythingIsRecordedWhenItsRunCouldNotStart()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        int assignments = run.Service.Records("assignment").Count;

        GoalStepResponse placeless = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
        Assert.False(placeless.Ok);
        Assert.Contains("An agent's run is made in a workspace of a repository", placeless.Message, StringComparison.Ordinal);

        // While the operator's key signs here without presence and no custody exception is in force, the agent's step is refused.
        GoalStepResponse guarded = Step(run, "guarded");
        Assert.False(guarded.Ok);
        Assert.Equal("refused", guarded.Problem!.Code);
        Assert.Equal(("W-1", "agent"), (guarded.Step, guarded.Route));
        Assert.Contains($"Device {run.OperatorDevice} (Operator's laptop, admitted) of Test Operator", guarded.Message, StringComparison.Ordinal);
        Assert.Contains($"vericairn goal custody-exception {run.Goal} --by <sponsor>", guarded.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", guarded.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("guarded")));

        RunEstate.Ok(run.Service.GoalPause(run.Goal, new GoalActionRequest(run.Estate.OperatorId)));
        Assert.Contains("is paused: a step is taken only for an active goal", run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource)).Message, StringComparison.Ordinal);
        Assert.Equal("not-found", run.Service.GoalStep("01M4TRNE1GYEK4P8MXQAB3C8Q4", new GoalStepRequest(run.Estate.OperatorId)).Problem!.Code);
        Assert.Empty(run.Service.Records("operation"));
        Assert.Empty(run.Service.Records("ask"));
        Assert.Equal(assignments, run.Service.Records("assignment").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitRunOfAHandedOverAssignmentRecordsAndChargesNothing(bool duringPreflight)
    {
        using var run = new RunEstate();
        TakeoverResponse? taken = null;
        int records = 0;
        void TakeOver()
        {
            taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, run.Estate.OperatorId));
            Assert.True(taken.Ok, taken.Message);
            records = run.Estate.Load().Records.Count;
        }

        if (duringPreflight) HookedLauncher.Install(run, verify: () => { TakeOver(); return null; });
        else TakeOver();
        RunResponse refused = run.Start("claude-code", "claude", run.Report("handed-over"));
        Assert.False(refused.Ok, refused.Message);
        Assert.Contains(taken!.Assignment!.Id, refused.Message, StringComparison.Ordinal);
        Assert.Contains("its step goes on from that assignment", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().Records.Count);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("handed-over")));
        AgentRuns.Ledger ledger = AgentRuns.Of(run.Estate.Load(), run.Estate.Load().ById[run.Goal]);
        Assert.Empty(ledger.Runs);
        Assert.All(ledger.Spent.Values, spent => Assert.Equal(0, spent));
    }

    [Fact]
    public void APersonTakesOverAStepAnAgentHoldsAndItsRunInFlightIsAskedToStop()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        GoalStepResponse step = Step(run, "lingering", "ack-stop");
        Assert.True(step.Ok, step.Message);
        run.WaitUntil(() => run.Row(step.Run!.Run).Observed && File.Exists(run.Report("lingering")), "the run and its stand-in to be ready");

        // The operator takes W-1 over: a new assignment carries the builder's brief and names its assignment, and the run is stopped.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op));
        Assert.True(taken.Ok, taken.Message);
        RunStopResponse stop = Assert.Single(taken.Stops!);
        Assert.True(stop.Ok, stop.Message);
        RunStop observedStop = run.Settled(step.Run!.Run).Stop!;
        Assert.NotNull(observedStop.ContainedAt);
        Assert.Equal(op, observedStop.RequestedBy);
        Assert.Equal("the step was taken over by Test Operator", observedStop.Reason);
        RecordEnvelope handover = run.Estate.Load().ById[taken.Assignment!.Id];
        Assert.Equal(op, EstateModel.BodyString(handover, "contributor"));
        Assert.Contains(handover.Body.GetProperty("brief").GetProperty("sources").EnumerateArray(), s => s.GetProperty("type").GetString() == "assignment" && s.GetProperty("id").GetString() == run.Assignment);
        Assert.Equal($"Takes the step over from Test Builder's assignment {run.Assignment}: the hand-over to Test Operator.", EstateModel.BodyString(handover, "note"));
        Assert.Equal("known-failure", run.Settled(step.Run.Run).Status);
        JsonElement route = Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "W-1").GetProperty("route");
        Assert.Equal(("assigned", op), (route.GetProperty("state").GetString(), route.GetProperty("principal").GetString()));
        Assert.EndsWith($"which takes it over from Test Builder's assignment {run.Assignment}", route.GetProperty("reason").GetString(), StringComparison.Ordinal);

        // The agent's assignment holds the step no longer, a person's is not taken over, and a step goes over to a person only.
        Assert.Contains("no longer holds work order W-1", run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, op)).Message, StringComparison.Ordinal);
        Assert.Contains("not an agent's", run.Service.WorkTakeover(new TakeoverRequest(taken.Assignment.Id, op)).Message, StringComparison.Ordinal);
        string second = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-2", run.Estate.BuilderId, op, RunEstate.Json(RunEstate.Brief)))).Id;
        Assert.Contains("is not a person", run.Service.WorkTakeover(new TakeoverRequest(second, op, run.Estate.BuilderId)).Message, StringComparison.Ordinal);
        Assert.Equal("not-found", run.Service.WorkTakeover(new TakeoverRequest("01M4TRNE1GYEK4P8MXQAB3C8Q4", op)).Problem!.Code);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepNoAssignmentHeldIsAssignedOnlyWithItsRunsIntent()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        Done(run, run.Assignment);
        int assignments = run.Service.Records("assignment").Count;
        string? unverified = null;
        HookedLauncher.Install(run, () => unverified);

        // W-1's result awaits review, so the next step is W-2, which no assignment holds: each refusal of the worker's preflight leaves
        // it so, and records nothing.
        Dictionary<string, string> command = Command(run, "never");
        var unknown = new Dictionary<string, string>(command, StringComparer.Ordinal) { ["model"] = "x" };
        (GoalStepRequest Request, string Because, string? Unverified)[] cases =
        [
            (new GoalStepRequest(op), "An agent's run is made in a workspace of a repository", null),
            (new GoalStepRequest(op, "01M4TRNE1GYEK4P8MXQAB3C8Q4", command), "No repository resource has id 01M4TRNE1GYEK4P8MXQAB3C8Q4.", null),
            (new GoalStepRequest(op, run.Resource, command, WallMinutes: 0), "A run's cap is at least one wall minute", null),
            (new GoalStepRequest(op, run.Resource, unknown), "it takes no model", null),
            (new GoalStepRequest(op, run.Resource, command), "The claude-code launcher cannot run here: Claude Code is not signed in.", "Claude Code is not signed in."),
            (new GoalStepRequest(op, run.Resource, command, Branch: "bad..branch"), "The workspace could not be prepared: 'bad..branch' is not a branch name git accepts.", null),
        ];
        foreach ((GoalStepRequest request, string because, string? refusal) in cases)
        {
            unverified = refusal;
            GoalStepResponse refused = run.Service.GoalStep(run.Goal, request);
            Assert.False(refused.Ok, because);
            Assert.Equal(("none", "W-2", "agent"), (refused.Outcome, refused.Step, refused.Route));
            Assert.Null(refused.Assignment);
            Assert.Contains(because, refused.Message, StringComparison.Ordinal);
            Assert.EndsWith("nothing was recorded.", refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Routing assigned", refused.Message, StringComparison.Ordinal);
            Assert.Equal(assignments, run.Service.Records("assignment").Count);
            Assert.Empty(run.Service.Records("operation"));
        }

        unverified = null;
        JsonElement unassigned = Next(run, "W-2");
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("assignment").ValueKind);
        Assert.Equal("agent", unassigned.GetProperty("route").GetProperty("state").GetString());
        Assert.False(File.Exists(run.Report("never")));

        // Taken: routing's assignment of W-2 and the run's intent are recorded together.
        GoalStepResponse taken = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "taken")));
        Assert.True(taken.Ok, taken.Message);
        Assert.StartsWith($"Routing assigned W-2 to its agent (assignment {taken.Assignment!.Id}), recorded with the run's intent.", taken.Message, StringComparison.Ordinal);
        AgentRuns.Run carried = AgentRuns.Find(run.Estate.Load(), taken.Run!.Run)!;
        Assert.Equal(taken.Assignment.Id, carried.Assignment);
        Assert.Equal("observed-success", run.Settled(taken.Run.Run).Status);
        Done(run, taken.Assignment.Id);

        // A harness that cannot start once the intent is recorded: W-3's assignment and its run's intent were recorded together, and the
        // run is recorded as the known failure it is, which spent nothing.
        Order(run, "W-3", string.Empty);
        HookedLauncher.Install(run, command: () => throw new Vericairn.Runtime.Providers.ProviderRefusedException("The synthetic executable disappeared after preflight."));
        GoalStepResponse failed = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "not-started")));
        Assert.False(failed.Ok);
        Assert.Equal(("none", "W-3"), (failed.Outcome, failed.Step));
        Assert.StartsWith($"Routing assigned W-3 to its agent (assignment {failed.Assignment!.Id}), recorded with the run's intent.", failed.Message, StringComparison.Ordinal);
        Assert.Contains("The run is recorded as a known failure; nothing ran and nothing is charged.", failed.Message, StringComparison.Ordinal);
        Assert.Equal("known-failure", run.Row(failed.Run!.Run).Status);
        Assert.Equal(failed.Assignment.Id, AgentRuns.Find(run.Estate.Load(), failed.Run.Run)!.Assignment);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void RecordsStoredTogetherGetTheContentChecksASingleRecordGets()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        Done(run, run.Assignment);
        int assignments = run.Service.Records("assignment").Count;
        string branch = "work/ghp_" + new string('a', 36);

        // A credential-shaped branch name would enter the run's intent: routing's assignment of W-2 and the intent are both kept out.
        GoalStepResponse refused = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "never"), Branch: branch));
        Assert.False(refused.Ok, refused.Message);
        Assert.True(refused.Problem?.Code == "rejected", refused.Message); // Include the full preparation refusal if the content check was never reached.
        Assert.Contains("Not written: the estate would be invalid with these records (", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        string workspace = Path.Combine(Vericairn.Records.Signing.VericairnHome.Path(), "estates", run.Service.Session.Manifest.Estate,
            "workspaces", branch.Replace('/', '-'));
        Assert.False(Directory.Exists(workspace), refused.Message);
        Assert.DoesNotContain("Routing assigned", refused.Message, StringComparison.Ordinal);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "secrets.pattern" && f.Location.StartsWith("records/operation/", StringComparison.Ordinal));
        Assert.Null(refused.Assignment);
        Assert.Equal(assignments, run.Service.Records("assignment").Count);
        Assert.Empty(run.Service.Records("operation"));

        // A single record gets the same checks on its unpublished bytes: the run of W-1's assignment on that branch is refused too.
        RunResponse single = run.Start("claude-code", "claude", run.Report("never"), branch: branch);
        Assert.False(single.Ok, single.Message);
        Assert.Contains(single.Problem!.Findings, f => f.Code == "secrets.pattern");
        Assert.False(Directory.Exists(workspace), single.Message);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("never")));
    }

    [Fact]
    public void ABatchThatStopsBetweenItsRecordsLeavesAStepNothingHoldsAndAReconcileTask()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        Done(run, run.Assignment);
        HandoffPackage beforeBatch = run.Service.Handoff().Package!;

        // The store fails after routing's assignment of W-2, before the run's intent: the assignment stays, holding nothing.
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "operation")
            {
                throw new IOException("The disk is full.");
            }
        };
        GoalStepResponse stopped = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "stopped")));
        Assert.False(stopped.Ok, stopped.Message);
        Assert.DoesNotContain("Routing assigned", stopped.Message, StringComparison.Ordinal);
        string stranded = stopped.Assignment!.Id;
        Assert.Contains($"Not written: The disk is full. Routing's assignment {stranded} was stored and the run's intent was not, so nothing was launched", stopped.Message, StringComparison.Ordinal);
        Assert.Equal("run", EstateModel.BodyString(run.Estate.Load().ById[stranded], "routedWith"));
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("stopped")));
        JsonElement next = Next(run, "W-2");
        Assert.Equal(JsonValueKind.Null, next.GetProperty("assignment").ValueKind);
        Assert.Equal("agent", next.GetProperty("route").GetProperty("state").GetString());

        // The batch is a reconcile task naming what was stored, and stays one when the estate is opened again.
        ReconcileTask task = Assert.Single(run.Service.Reconcile());
        Assert.Equal("batch.incomplete", task.Code);
        Assert.Equal(run.Estate.Load().ById[stranded].ExpectedPath, task.Location);
        Assert.Contains($"Stored: assignment {stranded}. Not stored: operation ", task.Message, StringComparison.Ordinal);
        run.Reopen();
        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);
        Assert.Contains("batch.incomplete", run.Service.Status().Text, StringComparison.Ordinal);
        Assert.Equal("incomplete-handoff", run.Service.Handoff().Problem!.Code);
        Assert.Contains(task.Message, run.Service.Handoff().Text, StringComparison.Ordinal);
        ResumeResponse resumed = run.Service.ResumeHandoff(beforeBatch);
        Assert.True(resumed.HasErrors);
        Assert.Contains("batch.incomplete", resumed.Text, StringComparison.Ordinal);
        Assert.Contains(task.Message, resumed.Text, StringComparison.Ordinal);

        // Taking the step again assigns W-2 afresh with its run's intent; the task ends, and the stranded assignment stays a record.
        GoalStepResponse taken = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, Command(run, "taken")));
        Assert.True(taken.Ok, taken.Message);
        Assert.NotEqual(stranded, taken.Assignment!.Id);
        Assert.Equal("observed-success", run.Settled(taken.Run!.Run).Status);
        Assert.Empty(run.Service.Reconcile());
        Assert.True(run.Estate.Load().ById.ContainsKey(stranded));
        HandoffPackage afterRun = run.Service.Handoff().Package!;
        Assert.Contains(afterRun.Payload.GetProperty("originals").EnumerateArray(), source => source.GetProperty("id").GetString() == run.Goal);
        Assert.Contains(afterRun.Payload.GetProperty("originals").EnumerateArray(), source => source.GetProperty("id").GetString() == taken.Run.Run);
        Assert.NotEmpty(afterRun.Payload.GetProperty("observedState").GetProperty("goals").EnumerateArray());
        Assert.Contains(afterRun.Payload.GetProperty("observedState").GetProperty("operations").EnumerateArray(), operation => operation.GetProperty("body").GetProperty("status").GetString() == "observed-success");

        // A person's step likewise: an ask that could not be stored leaves the operator's assignment of W-3 holding nothing.
        Order(run, "W-3", ""","role":"publisher" """);
        Done(run, taken.Assignment.Id);
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "ask")
            {
                throw new IOException("The disk is full.");
            }
        };
        GoalStepResponse unasked = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.False(unasked.Ok, unasked.Message);
        Assert.Contains("was stored and its ask was not: the assignment holds nothing of its step without its ask", unasked.Message, StringComparison.Ordinal);
        Assert.Equal("reserved", Next(run, "W-3").GetProperty("route").GetProperty("state").GetString());
        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);
        run.Service.BeforeStoringTogether = null;
        GoalStepResponse asked = run.Service.GoalStep(run.Goal, new GoalStepRequest(op));
        Assert.True(asked.Ok, asked.Message);
        Assert.Equal(asked.Assignment!.Id, EstateModel.BodyString(run.Estate.Load().ById[asked.Ask!.Id], "assignment"));
        Assert.Equal("assigned", Next(run, "W-3").GetProperty("route").GetProperty("state").GetString());
        Assert.Empty(run.Service.Reconcile());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedTemporaryWritesDoNotObstructReopenAndRoutingRetry(bool beforeRename)
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        Done(run, run.Assignment);
        string? staging = null;
        byte[] residue = [];
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "operation")
            {
                RecordAuthor.Write(run.Estate.Root, record, (path, bytes) =>
                {
                    staging = path;
                    residue = beforeRename ? bytes : bytes[..(bytes.Length / 2)];
                    File.WriteAllBytes(path, residue);
                    // Inspect while the unpublished bytes still exist, as they would after process termination.
                    Assert.False(run.Estate.Load().Findings.HasErrors);
                    Assert.Equal(0, run.Service.Check().Errors);
                    throw new IOException("Interrupted temporary write.");
                });
            }
        };
        GoalStepResponse stopped = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, Command(run, "interrupted")));
        Assert.False(stopped.Ok, stopped.Message);
        Assert.NotNull(staging);
        Assert.False(File.Exists(staging));
        string stranded = stopped.Assignment!.Id;

        // Leave the same residue as an abrupt process death, when finally cannot run.
        File.WriteAllBytes(staging, residue);
        RunEstate.Git(run.Estate.Root, "init", "-q");
        Assert.Equal(".vericairn/record-staging/" + Path.GetFileName(staging), RunEstate.Git(run.Estate.Root, "check-ignore", "--", EstateLayout.Relative(run.Estate.Root, staging)).Trim());
        run.Reopen();
        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);
        Assert.Equal(0, run.Service.Check().Errors);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(run.Estate.Root, "records"), "*.tmp", SearchOption.AllDirectories));
        GoalStepResponse retried = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, Command(run, "recovered")));
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal("observed-success", run.Settled(retried.Run!.Run).Status);
        Assert.True(run.Estate.Load().ById.ContainsKey(stranded));
        Assert.Empty(run.Service.Reconcile());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOlderCloneCannotSettleTheWritingClonesBatch(bool beforeFirstWrite)
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        Done(run, run.Assignment);
        string older = Path.Combine(run.Scratch, "older-estate");
        foreach (string directory in new[] { "estate", "records" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(run.Estate.Root, directory), "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(older, Path.GetRelativePath(run.Estate.Root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        bool opened = false;
        run.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == (beforeFirstWrite ? "assignment" : "operation"))
            {
                // B is a snapshot of A before the batch: opening and reading B must not close A's journal.
                using EstateService other = EstateService.Open(older);
                Assert.Empty(other.Reconcile());
                Assert.Equal(0, other.Check().Errors);
                opened = true;
            }

            if (record.Envelope.Type == "operation")
            {
                throw new IOException("Interrupted before the intent.");
            }
        };
        GoalStepResponse stopped = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, Command(run, "stopped")));
        Assert.False(stopped.Ok, stopped.Message);
        Assert.True(opened);
        string stranded = stopped.Assignment!.Id;
        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);
        run.Reopen();
        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);

        // B still cannot settle A's interrupted batch after A reopens.
        using (EstateService other = EstateService.Open(older))
        {
            Assert.Empty(other.Reconcile());
        }

        Assert.Equal("batch.incomplete", Assert.Single(run.Service.Reconcile()).Code);
        GoalStepResponse retried = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, Command(run, "retried")));
        Assert.True(retried.Ok, retried.Message);
        RunRow settled = run.Settled(retried.Run!.Run);
        RecordEnvelope observed = AgentRuns.Find(run.Estate.Load(), retried.Run.Run)!.Latest;
        Assert.True(settled.Status == "observed-success", observed.Body.GetProperty("observed").GetProperty("summary").GetString());
        Assert.True(run.Estate.Load().ById.ContainsKey(stranded));
        Assert.Empty(run.Service.Reconcile());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void APublisherAgentsBriefProhibitsEveryReservedEffectAndNamesItsGrant()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;

        // The roster names the builder for publication, and a grant of publication in force lets it carry W-0, a publisher's step.
        string roster = run.Estate.Load().Current("roster").Single().Id;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, RunEstate.Json($$"""{"roles":[{"role":"builder","principals":["{{builder}}"]},{"role":"publisher","principals":["{{builder}}"]},{"role":"decider","principals":["{{op}}"]}]}"""), Supersedes: roster)));
        string expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(1));
        string grant = Grant(run, builder, "publication", expires);
        Order(run, "W-0", ""","role":"publisher" """);

        GoalStepResponse step = Step(run, "publisher");
        Assert.True(step.Ok, step.Message);
        Assert.Equal(("run", "W-0", "agent", builder), (step.Outcome, step.Step, step.Route, step.Principal));
        string[] prohibited = [.. run.Estate.Load().ById[step.Assignment!.Id].Body.GetProperty("brief").GetProperty("prohibitedEffects").EnumerateArray().Select(p => p.GetString()!)];
        Assert.Contains("no change outside the widget", prohibited);
        Assert.Contains(ReservedEffects, prohibited);
        Assert.Contains($"no publication except under grant {grant}, which gives it to Test Builder until {expires}", prohibited);
        Assert.Equal("observed-success", run.Settled(step.Run!.Run).Status);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStepIsTakenByAnOperatorTheGoalsSponsorOrAPrincipalHoldingLocalEngineering()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string pat = Person(run, "Pat");
        string helper = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Helper","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")))).Id;
        int assignments = run.Service.Records("assignment").Count;

        // A person who is neither an operator nor the sponsor, and an agent without local-engineering, take no step, and nothing is recorded.
        foreach (string by in new[] { pat, helper, builder })
        {
            GoalStepResponse refused = run.Service.GoalStep(run.Goal, new GoalStepRequest(by, run.Resource, Command(run, "never")));
            Assert.False(refused.Ok, by);
            Assert.Equal("refused", refused.Problem!.Code);
            Assert.EndsWith($"does not take steps of goal {run.Goal}: a step is taken by an operator, by the goal's sponsor when the sponsor is a person, or by a principal holding the 'local-engineering' scope, as the builder dispatches agents under the operator's authority. Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(assignments, run.Service.Records("assignment").Count);
        Assert.Empty(run.Service.Records("operation"));

        // Under local-engineering the builder dispatches W-1's run, as it does under the operator's authority.
        Grant(run, builder, "local-engineering");
        GoalStepResponse engineered = run.Service.GoalStep(run.Goal, new GoalStepRequest(builder, run.Resource, Command(run, "engineered")));
        Assert.True(engineered.Ok, engineered.Message);
        Assert.Equal(builder, AgentRuns.Find(run.Estate.Load(), engineered.Run!.Run)!.Intent.By);
        Assert.Equal("observed-success", run.Settled(engineered.Run.Run).Status);

        // Sam, a person who funds a goal of their own, takes its step: routing's assignment and the run's intent are Sam's records.
        (string sam, string goal) = SamsGoal(run);
        GoalStepResponse own = run.Service.GoalStep(goal, new GoalStepRequest(sam, run.Resource, Command(run, "sam")));
        Assert.True(own.Ok, own.Message);
        Assert.Equal(("run", "S-1", builder), (own.Outcome, own.Step, own.Principal));
        Assert.Equal(sam, run.Estate.Load().ById[own.Assignment!.Id].By);
        Assert.Equal(sam, AgentRuns.Find(run.Estate.Load(), own.Run!.Run)!.Intent.By);
        Assert.Equal("observed-success", run.Settled(own.Run.Run).Status);

        // Sam takes no step of a goal Sam does not sponsor.
        Assert.Contains($"does not take steps of goal {run.Goal}", run.Service.GoalStep(run.Goal, new GoalStepRequest(sam, run.Resource)).Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AHandOverIsRecordedByThePersonTakingOverTheSponsorOrAnOperatorAndNeverByAnAgent()
    {
        using var run = new RunEstate();
        RoutingServiceTests.Routed(run);
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string pat = Person(run, "Pat");
        string quinn = Person(run, "Quinn");
        Grant(run, builder, "local-engineering");
        Grant(run, quinn, "local-engineering");
        int assignments = run.Service.Records("assignment").Count;

        // The builder, whatever its scope, and Quinn, who is neither Pat, nor the sponsor, nor an operator, record no hand-over of W-1 to Pat.
        foreach (string by in new[] { builder, quinn })
        {
            TakeoverResponse refused = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, by, pat));
            Assert.False(refused.Ok, by);
            Assert.EndsWith("does not record this hand-over: a hand-over is recorded by a person, the one taking the step over, the sponsor of its goal or an operator, and never by an agent. Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(assignments, run.Service.Records("assignment").Count);

        // Pat records it, taking W-1 over with no scope at all.
        TakeoverResponse taken = run.Service.WorkTakeover(new TakeoverRequest(run.Assignment, pat));
        Assert.True(taken.Ok, taken.Message);
        RecordEnvelope handover = run.Estate.Load().ById[taken.Assignment!.Id];
        Assert.Equal((pat, pat), (handover.By, EstateModel.BodyString(handover, "contributor")));

        // The sponsor of a goal, a person, records the hand-over of its step from the builder to Quinn.
        (string sam, _) = SamsGoal(run);
        string held = RunEstate.Ok(run.Service.Assign(new AssignRequest("S-1", builder, op, RunEstate.Json(RunEstate.Brief)))).Id;
        TakeoverResponse bySponsor = run.Service.WorkTakeover(new TakeoverRequest(held, sam, quinn));
        Assert.True(bySponsor.Ok, bySponsor.Message);
        RecordEnvelope sponsored = run.Estate.Load().ById[bySponsor.Assignment!.Id];
        Assert.Equal((sam, quinn), (sponsored.By, EstateModel.BodyString(sponsored, "contributor")));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>The reserved effects every routed brief prohibits unless a grant in force covers one.</summary>
    private const string ReservedEffects = "nothing reserved to the operator (publication, spending, credentials, DNS, destructive operations, material product changes, milestone and release acceptance) unless a grant in force covers it";

    private static Dictionary<string, string> Command(RunEstate run, string report) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report(report)) };

    private static JsonElement Next(RunEstate run, string key) =>
        Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);

    private static string Person(RunEstate run, string name) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", run.Estate.OperatorId, RunEstate.Json($$"""{"kind":"human","name":"{{name}}","roles":["contributor"]}""")))).Id;

    /// <summary>The operator's grant of a scope to a principal, on a decision of the operator's; returns the grant's id.</summary>
    private static string Grant(RunEstate run, string grantee, string scope, string? expires = null)
    {
        string op = run.Estate.OperatorId;
        string decision = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json($$"""{"title":"A grant","statement":"Grant {{scope}}.","summary":"A grant.","synthesis":"A grant of {{scope}}.","effects":["{{scope}}"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T12:00:00Z","stake":"medium","notAuthorized":[]}""")))).Id;
        string until = expires is null ? string.Empty : ",\"expires\":\"" + expires + "\"";
        return RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("authority", op, RunEstate.Json($$"""{"grantee":"{{grantee}}","scope":["{{scope}}"],"reserved":[],"conditions":[],"basis":"{{decision}}"{{until}}}""")))).Id;
    }

    /// <summary>A goal Sam sponsors and funds, a person with a grant of spending, with one step, S-1, for the builder.</summary>
    private static (string Sam, string Goal) SamsGoal(RunEstate run)
    {
        string sam = Person(run, "Sam");
        Grant(run, sam, "spending");
        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(sam, RunEstate.Json($$$"""{"title":"Describe the widget","objective":"A guide to the widget.","completion":[{"record":{"type":"work-order","fields":{"key":"S-1"},"state":"accepted"}}],"constraints":[],"kind":"finite","sponsor":"{{{sam}}}","budget":{"wallMinutes":60,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""")))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(sam)));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"S-1","kind":"ordinary","title":"Describe the widget","intent":"Describe it.","scope":["widget guide","path:docs/"],"exitQuestion":"Does the guide describe the widget?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["guide"]}"""))));
        return (sam, goal);
    }

    private static GoalStepResponse Step(RunEstate run, string report, string scenario = "claude") =>
        run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, run.Report(report)) }));

    private static void Order(RunEstate run, string key, string extra) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["work"]{{extra}}}"""))));

    /// <summary>The builder's attempt and result on an assignment, as its MCP tools would record them: its result then awaits review.</summary>
    private static void Done(RunEstate run, string assignment)
    {
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "widget, a work branch", "Do the work."))).Id;
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))));
    }
}
