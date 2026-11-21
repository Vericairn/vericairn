// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// The starter flow (from 0.9.4, TPL-13; the estate's design/flow.md §§ 5, 6 and 8): a built-in flow every estate lists, in which an
/// agent drafts a proposed appointment from a service request and a person in the approver role approves that exact result. The
/// approval names the result's hash, so a revised draft leaves it stale, validation says so, and the approval step reruns; findings
/// send the draft back to the agent; the agent never approves its own draft.
/// </summary>
public class StarterFlowTests
{
    private const string Request = "SR-0001: Sample Customer asks for a pool cleaning visit at 1 Sample Lane, Exampletown, any weekday morning in the week of 2026-12-07.";

    private static TemplateDefinition Starter => Assert.Single(StarterFlows.Definitions);

    /// <summary>A run of the starter flow: the builder agent holds the scheduler role and the operator, a person, the approver role.</summary>
    private static FlowFixture Run() => new(JsonNode.Parse(Starter.Content.GetRawText())!.AsObject(),
        subject: _ => new JsonObject { ["request"] = Request },
        rolesOf: f => new JsonArray(FlowFixture.Role("scheduler", f.Estate.BuilderId), FlowFixture.Role("approver", f.Estate.OperatorId)));

    [Fact]
    public void TheStarterFlowShipsBuiltInWithAnAgentsDraftAndAPersonsApproval()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition listed = Assert.Single(TemplateCatalog.All(estate.Load()), d => d.Id == StarterFlows.ServiceAppointment);
        Assert.Equal((StarterFlows.Version, Starter.Hash, true), (listed.Version, listed.Hash, listed.IsFlow));
        FlowDefinition flow = FlowDefinition.Resolve(estate.Load(), new TemplatePin(StarterFlows.ServiceAppointment, StarterFlows.Version, Starter.Hash));
        Assert.Equal([("scheduler", "agent"), ("approver", "person")], flow.Roles.Select(r => (r.Id, r.Holder)));
        Assert.Equal([("draft", "work", "scheduler"), ("approve", "review", "approver")], flow.Steps.Select(s => (s.Key, s.Kind, s.Role)));
        Assert.All(flow.Steps, s => Assert.Null(s.Reserved));
        Assert.Equal([("request", "text")], flow.SubjectFields.Select(f => (f.Key, f.Value)));

        // The agent's brief says it runs in the operator's own harness under the operator's own account; nothing names a hosted service.
        string brief = flow.Role("scheduler")!.Brief;
        Assert.Contains("the operator's own coding-agent harness, under the operator's own account", brief, StringComparison.Ordinal);
        Assert.Contains("approved only when a person in the approver role approves your exact result", brief, StringComparison.Ordinal);
        Assert.Contains("an approval covers only the result it names", flow.Role("approver")!.Brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(flow.Never, n => n.StartsWith("Never contact the customer", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAgentsDraftIsApprovedOnlyByAPersonInTheApproverRoleAtItsExactResult()
    {
        using FlowFixture run = Run();
        Assert.Equal(("next", "blocked"), (run.Step("draft").State, run.Step("approve").State));

        // The agent drafts the appointment as its result; the run stops for the person who holds the approver role.
        RecordEnvelope draft = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("appointment", FlowFixture.Hash('a'), "application/json"));
        FlowPosition.StepInstance drafted = run.Step("draft");
        Assert.Equal("done", drafted.State);
        Assert.Equal(("satisfied", FlowFixture.Hash('a')), (drafted.Items[0].State, drafted.Items[0].Value!.Hash));
        FlowPosition.StepInstance approving = run.Step("approve");
        Assert.Equal(("stopped", "person"), (approving.State, approving.Stop));

        // The agent's own pass approves nothing: the approval is a person's review of the exact result.
        run.Estate.Add("review", run.Estate.BuilderId, new JsonObject { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "My own draft.", ["findings"] = new JsonArray() }.ToJsonString(), draft.Reference);
        Assert.NotEqual("done", run.Step("approve").State);
        Assert.False(run.Position().Complete);

        RecordEnvelope approval = run.Review(draft);
        approving = run.Step("approve");
        Assert.Equal(("done", approval.Id), (approving.State, approving.Review!.Id));
        Assert.Equal(("result", draft.Id, draft.ComputedHash), (approval.Subject!.Type, approval.Subject.Id, approval.Subject.Hash));
        Assert.True(run.Position().Complete);
        Assert.DoesNotContain(run.Estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void ARevisedDraftLeavesTheApprovalStaleAndTheApprovalStepAsksAgain()
    {
        using FlowFixture run = Run();
        RecordEnvelope draft = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("appointment", FlowFixture.Hash('a'), "application/json"));
        RecordEnvelope first = run.Assign("approve", run.Estate.OperatorId, inputs: new JsonArray(FlowFixture.PinOf(draft, "appointment")));
        RecordEnvelope approval = run.Review(draft);
        Assert.True(run.Position().Complete);

        // The agent revises its result with a different appointment: the approval names the earlier revision's hash and covers nothing now.
        JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        body["outputs"] = new JsonArray(new JsonObject { ["name"] = "appointment", ["hash"] = FlowFixture.Hash('b'), ["mediaType"] = "application/json" });
        body["summary"] = "Moved the appointment to the afternoon.";
        RecordEnvelope revised = run.Add("result", body, builder: true, subject: draft.Subject, supersedes: draft.Id);
        Assert.Equal("done", run.Step("draft").State);
        FlowPosition.StepInstance approving = run.Step("approve");
        Assert.Equal("stale", approving.State);
        Assert.True(approving.Rerun);
        Assert.Contains(approving.Reasons, r => r.Contains($"its brief pinned result {draft.Id} at {FlowFixture.Hash('a')}", StringComparison.Ordinal) && r.Contains($"is now {FlowFixture.Hash('b')} in result {revised.Id}", StringComparison.Ordinal));
        Assert.Equal("missing", approving.Items[0].State);
        Assert.False(run.Position().Complete);
        Assert.Contains(run.Estate.Load().Findings, f => f.Code == "record.subject-stale" && f.Location.StartsWith(approval.ExpectedPath, StringComparison.Ordinal));

        // Routing gives the approval to the approver again, a fresh assignment pinning the revised result; the earlier one stays recorded.
        NextEligible.FlowStepRoute route = Assert.Single(NextEligible.FlowRoutes(run.Estate.Load(), run.Goal, new Routing.Machine([], _ => false, (_, _) => [], run.Estate.Clock)).Steps, r => r.Name == "approve");
        Assert.True(route.Dispatches);
        Assert.Equal(("person", run.Estate.OperatorId), (route.Route!.State, route.Route.Principal));
        run.Assign("approve", run.Estate.OperatorId, inputs: new JsonArray(FlowFixture.PinOf(revised, "appointment")));
        RecordEnvelope again = run.Review(revised);
        Assert.Equal(("done", again.Id), (run.Step("approve").State, run.Step("approve").Review!.Id));
        Assert.True(run.Position().Complete);
        Assert.Contains(first.Id, run.Estate.Load().Current("assignment").Where(a => TJson.Text(TJson.Object(a.Body, "step"), "key") == "approve").Select(a => a.Id));
    }

    [Fact]
    public void TheApproversFindingsSendTheDraftBackToTheAgent()
    {
        using FlowFixture run = Run();
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope draft = run.Result(drafting, "completed", ("appointment", FlowFixture.Hash('a'), "application/json"));
        run.Review(draft, "findings");
        Assert.NotEqual("done", run.Step("draft").State);
        Assert.NotEqual("done", run.Step("approve").State);

        // The agent drafts again under the same assignment, and the approver approves the new result.
        RecordEnvelope redraft = run.Result(drafting, "completed", ("appointment", FlowFixture.Hash('c'), "application/json"));
        Assert.Equal("done", run.Step("draft").State);
        run.Review(redraft);
        Assert.True(run.Position().Complete);
    }
}
