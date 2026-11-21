// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// An acceptance of a work order's current revision, the form acceptances took before they named a result's exact hash (FIX-141): from
/// 0.9.4 it accepts the work order's current result when that result was recorded before it, in status, goal views and what reads a
/// result's state; a result recorded after it, or an acceptance of an earlier revision, accepts nothing more than before.
/// </summary>
public sealed class RevisionAcceptanceReadingTests
{
    [Fact]
    public void AnAcceptanceOfTheCurrentRevisionAcceptsTheCurrentResultRecordedBeforeItAndNoLaterOne()
    {
        using var f = new PolicyFixture();
        RecordEnvelope goal = f.Add("goal", Goal(f));
        RecordEnvelope order = f.Add("work-order", Order("WORK-1", goal));
        RecordEnvelope attempt = f.BeginAttempt(order: order);
        RecordEnvelope earlier = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        RecordEnvelope current = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: Again(f, attempt).Reference);
        f.Review(current);
        RecordEnvelope accepted = f.Add("acceptance", new JsonObject { ["statement"] = "I accept WORK-1.", ["basis"] = new JsonArray(), ["self"] = true }, subject: order.Reference);
        f.AssertValid();

        EstateModel model = f.Estate.Load();
        Assert.Equal("accepted", WorkView.ResultState(model, current));
        Assert.Equal(accepted.Id, EstateStatus.RevisionAcceptance(model, current)?.Id);
        // Only the work order's current result: an earlier attempt's result still has no acceptance of its own.
        Assert.Equal("awaiting review", WorkView.ResultState(model, earlier));
        Assert.Null(EstateStatus.RevisionAcceptance(model, earlier));
        Assert.Equal(("accepted", "accepted"), Read(model, goal, order));

        // A revision that changes only the work order's placement keeps it; a result recorded after the acceptance needs its own.
        JsonObject moved = PolicyFixture.Body(order);
        moved["placement"] = new JsonObject { ["kind"] = "backlog", ["key"] = "next" };
        RecordEnvelope placed = f.Add("work-order", moved, supersedes: order.Id);
        model = f.Estate.Load();
        Assert.Equal(("accepted", "accepted"), Read(model, goal, placed));
        RecordEnvelope later = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: Again(f, attempt).Reference);
        f.Review(later);
        model = f.Estate.Load();
        Assert.Equal("awaiting review", WorkView.ResultState(model, later));
        Assert.Null(EstateStatus.RevisionAcceptance(model, later));
        Assert.Equal(("awaiting review", "awaiting review"), Read(model, goal, placed));
        Assert.Equal("accepted", EstateStatus.WorkItems(model).Single(w => w.Id == placed.Id).Status);
    }

    [Fact]
    public void AnAcceptanceOfAnEarlierRevisionAcceptsNoResult()
    {
        using var f = new PolicyFixture();
        RecordEnvelope goal = f.Add("goal", Goal(f));
        RecordEnvelope order = f.Add("work-order", Order("WORK-1", goal));
        RecordEnvelope result = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: f.BeginAttempt(order: order).Reference);
        f.Add("acceptance", new JsonObject { ["statement"] = "I accept WORK-1.", ["basis"] = new JsonArray(), ["self"] = true }, subject: order.Reference);
        JsonObject changed = PolicyFixture.Body(order);
        changed["title"] = "Order WORK-1, revised";
        RecordEnvelope revised = f.Add("work-order", changed, supersedes: order.Id);

        EstateModel model = f.Estate.Load();
        Assert.Equal("accepted (earlier revision)", EstateStatus.WorkItems(model).Single(w => w.Id == revised.Id).Status);
        Assert.Null(EstateStatus.RevisionAcceptance(model, result));
        Assert.Equal(("awaiting review", "awaiting review"), Read(model, goal, revised));
    }

    [Fact]
    public void AMilestonesAcceptanceAcceptsItsResultRecordedBeforeItSoNothingMoreWaitsOnTheOperator()
    {
        using var f = new PolicyFixture();
        JsonObject body = Order("M-1", null);
        body["kind"] = "milestone";
        RecordEnvelope milestone = f.Add("work-order", body);
        f.Add("work-status", new JsonObject { ["status"] = "submitted", ["note"] = "Ready." }, subject: milestone.Reference);
        RecordEnvelope result = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: f.BeginAttempt(order: milestone).Reference);
        f.Review(result);
        EstateModel model = f.Estate.Load();
        Assert.Equal("awaiting review", WorkView.ResultState(model, result));
        Assert.Contains(OperatorActions.Derive(model)["items"]!.AsArray(), i => i!["kind"]!.GetValue<string>() == "milestone-result-acceptance");

        f.Add("acceptance", new JsonObject { ["statement"] = "I accept the milestone.", ["basis"] = new JsonArray(), ["self"] = true }, subject: milestone.Reference);
        f.AssertValid();
        model = f.Estate.Load();
        Assert.Equal("accepted", WorkView.ResultState(model, result));
        Assert.DoesNotContain(OperatorActions.Derive(model)["items"]!.AsArray(), i => i!["kind"]!.GetValue<string>() is "milestone-result-acceptance" or "milestone-acceptance");
        using JsonDocument progress = JsonDocument.Parse(ProgressReport.RenderJson(model, null));
        Assert.DoesNotContain(result.Id, progress.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>The state of the work order's assignment as status's JSON and the goal's view give it.</summary>
    private static (string Status, string Goal) Read(EstateModel model, RecordEnvelope goal, RecordEnvelope order)
    {
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        string key = EstateModel.BodyString(order, "key")!;
        string inStatus = Assert.Single(status.RootElement.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("key").GetString() == key).GetProperty("state").GetString()!;
        using JsonDocument view = JsonDocument.Parse(GoalView.RenderJson(model, goal));
        JsonElement step = Assert.Single(view.RootElement.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);
        return (inStatus, Assert.Single(step.GetProperty("assignments").EnumerateArray()).GetProperty("state").GetString()!);
    }

    private static RecordEnvelope Again(PolicyFixture f, RecordEnvelope attempt) => f.Add("attempt", new JsonObject
    {
        ["startedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock), ["workspace"] = "example repository", ["note"] = "Carry the work again.",
    }, builder: true, subject: attempt.Subject);

    private static JsonObject Goal(PolicyFixture f) => new()
    {
        ["title"] = "Finish the work", ["objective"] = "Finish the work.",
        ["completion"] = new JsonArray(new JsonObject { ["evidence"] = "The work is accepted.", ["check"] = "Read the acceptance." }),
        ["constraints"] = new JsonArray(), ["kind"] = "finite", ["sponsor"] = f.Estate.OperatorId, ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 5 },
        ["plan"] = new JsonObject { ["policy"] = "proposed-by-planner", ["approval"] = "self-approves-ordinary" }, ["inputs"] = new JsonArray(),
    };

    private static JsonObject Order(string key, RecordEnvelope? goal) => new()
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Order " + key, ["intent"] = "Carry " + key + ".", ["scope"] = new JsonArray("fixture"),
        ["exitQuestion"] = "Is " + key + " done?",
        ["inputs"] = goal is null ? new JsonArray() : new JsonArray(new JsonObject { ["type"] = "goal", ["id"] = goal.Id }),
        ["deliverables"] = new JsonArray("the work"),
    };
}
