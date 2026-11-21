// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Presentation;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// The starter flow on Workbench's read model (from 0.9.4, TPL-13; the estate's design/flow.md § 8): the home page's run row names the
/// flow and its version and the run's position, and the Goals workspace's stops show the approval waiting on its person, before the
/// approval and again once a revised draft left it stale and goal step assigned it afresh, as status and the goal view give them.
/// </summary>
public class StarterFlowSurfaceTests
{
    [Fact]
    public void TheRunRowNamesTheFlowAndTheStopsFollowTheApprovalAndItsStaleness()
    {
        using var run = new FlowFixture(JsonNode.Parse(Assert.Single(StarterFlows.Definitions).Content.GetRawText())!.AsObject(),
            subject: _ => new JsonObject { ["request"] = "SR-0001: Sample Customer asks for a pool cleaning visit at 1 Sample Lane, Exampletown." },
            rolesOf: f => new JsonArray(FlowFixture.Role("scheduler", f.Estate.BuilderId), FlowFixture.Role("approver", f.Estate.OperatorId)));
        RecordEnvelope draft = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("appointment", FlowFixture.Hash('a'), "application/json"));
        (FlowRunRow row, IReadOnlyList<StopRow> stops) = Read(run);
        Assert.Equal(("flow vericairn.starter/service-appointment 1.0.0", 1, 2), (row.Flow, row.Done, row.Steps));
        StopRow waiting = Assert.Single(stops);
        Assert.Equal(("person", "approve"), (waiting.Kind, waiting.Name));
        Assert.Equal(stops, row.Stops);

        // The approval is assigned to its person, as goal step assigns it, and approved.
        run.Assign("approve", run.Estate.OperatorId, inputs: new JsonArray(FlowFixture.PinOf(draft, "appointment")));
        (_, stops) = Read(run);
        Assert.Equal(("approve", "delivered"), (Assert.Single(stops).Name, stops[0].Delivery));
        run.Review(draft);
        (row, stops) = Read(run);
        Assert.Equal((2, 2), (row.Done, row.Steps));
        Assert.DoesNotContain(stops, s => s.Name == "approve");

        // A revised draft: the approval no longer counts, the run is one step short again, and the approval waits on its person.
        JsonObject body = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        body["outputs"] = new JsonArray(new JsonObject { ["name"] = "appointment", ["hash"] = FlowFixture.Hash('b'), ["mediaType"] = "application/json" });
        RecordEnvelope revised = run.Add("result", body, builder: true, subject: draft.Subject, supersedes: draft.Id);
        (row, _) = Read(run);
        Assert.Equal((1, 2), (row.Done, row.Steps));

        // Once goal step assigns the approval again, pinning the revised result, it waits on its person as it did the first time.
        run.Assign("approve", run.Estate.OperatorId, inputs: new JsonArray(FlowFixture.PinOf(revised, "appointment")));
        (row, stops) = Read(run);
        StopRow again = Assert.Single(stops);
        Assert.Equal(("person", "approve", "delivered"), (again.Kind, again.Name, again.Delivery));
        Assert.Equal(stops, row.Stops);
    }

    private static (FlowRunRow Row, IReadOnlyList<StopRow> Stops) Read(FlowFixture run)
    {
        using EstateService service = EstateService.Open(run.Estate.Root);
        FlowRunRow row = Assert.Single(EstateView.From(service.Status(), "2026-11-21T00:00:00Z").Runs);
        IReadOnlyList<StopRow> stops = GoalPresentation.From(service.Goal(run.Goal.Id)!.Json, service.GoalNext(run.Goal.Id)!.Json).Stops;
        return (row, stops);
    }
}
