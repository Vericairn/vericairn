// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// A work order closed by operator close (FIX-128): since 0.8.4 operator close accepts each work order's current result, and status,
/// goal predicates, the progress report and next eligible work read such a work order as accepted. An acceptance of an earlier result,
/// of an earlier revision of a result, or recorded before the work order changed reads accepted (earlier revision); what read accepted
/// before still does.
/// </summary>
public sealed class OperatorCloseReadingTests
{
    [Fact]
    public void AWorkOrderClosedByOperatorCloseReadsAcceptedInStatusPredicatesProgressAndNextWork()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "WORK-1");
        (RecordEnvelope result, RecordEnvelope closing) = Close(f, order);
        f.AssertValid();
        EstateModel model = f.Estate.Load();
        Assert.Equal("accepted", Status(model, order));
        Assert.Equal(closing.Id, EstateStatus.StatusRecord(model, order)!.Id);
        Assert.Equal("accepted", WorkView.ResultState(model, result));

        using (JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList())))
        {
            JsonElement row = Assert.Single(status.RootElement.GetProperty("workOrders").EnumerateArray(), w => w.GetProperty("id").GetString() == order.Id);
            Assert.Equal("accepted", row.GetProperty("status").GetString());
        }

        GoalPredicates.Verdict accepted = GoalPredicates.EvaluatePredicate(model, Predicate("WORK-1", "accepted"));
        Assert.Equal("met", accepted.Value);
        Assert.Contains(closing.Reference, accepted.Read);
        Assert.Equal("met", GoalPredicates.EvaluatePredicate(model, Predicate("WORK-1", "planned", max: 0)).Value);

        using (JsonDocument progress = JsonDocument.Parse(ProgressReport.RenderJson(model, null)))
        {
            JsonElement line = progress.RootElement.GetProperty("workOrders").GetProperty("states").EnumerateArray()
                .SelectMany(s => s.GetProperty("workOrders").EnumerateArray()).Single(w => w.GetProperty("id").GetString() == order.Id);
            Assert.Equal(("accepted", closing.Id), (line.GetProperty("status").GetString(), line.GetProperty("basis").GetProperty("id").GetString()));
        }

        Assert.DoesNotContain(NextEligible.Derive(model), item => item.Order.Id == order.Id);
        RecordEnvelope later = f.Add("work-order", Body("WORK-2", after: order.Id));
        NextEligible.Item waiting = Assert.Single(NextEligible.Derive(f.Estate.Load()), item => item.Order.Id == later.Id);
        Assert.True(waiting.Eligible);
    }

    [Fact]
    public void APlacementChangeKeepsTheClosingAndAContentChangeLeavesItAtAnEarlierRevision()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "WORK-1");
        (_, RecordEnvelope closing) = Close(f, order);
        JsonObject moved = PolicyFixture.Body(order);
        moved["placement"] = new JsonObject { ["kind"] = "backlog", ["key"] = "next" };
        RecordEnvelope placed = f.Add("work-order", moved, supersedes: order.Id);
        EstateModel model = f.Estate.Load();
        Assert.Equal(("accepted", closing.Id), (Status(model, placed), EstateStatus.StatusRecord(model, placed)!.Id));

        JsonObject changed = PolicyFixture.Body(placed);
        changed["title"] = "Order WORK-1, revised";
        RecordEnvelope revised = f.Add("work-order", changed, supersedes: placed.Id);
        model = f.Estate.Load();
        Assert.Equal(("accepted (earlier revision)", closing.Id), (Status(model, revised), EstateStatus.StatusRecord(model, revised)!.Id));
        Assert.Equal("met", GoalPredicates.EvaluatePredicate(model, Predicate("WORK-1", "accepted (earlier revision)")).Value);
    }

    [Fact]
    public void AnAcceptanceOfAnEarlierResultOrOfAnEarlierRevisionOfTheResultReadsAcceptedAtAnEarlierRevision()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "WORK-1");
        (RecordEnvelope first, RecordEnvelope closing) = Close(f, order);
        RecordEnvelope attempt = f.Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock), ["workspace"] = "example repository", ["note"] = "Carry the work again." },
            builder: true, subject: f.Estate.Load().ById[first.Subject!.Id].Subject);
        f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        EstateModel model = f.Estate.Load();
        Assert.Equal(("accepted (earlier revision)", closing.Id), (Status(model, order), EstateStatus.StatusRecord(model, order)!.Id));

        using var g = new PolicyFixture();
        RecordEnvelope other = Order(g, "WORK-1");
        (RecordEnvelope result, RecordEnvelope accepted) = Close(g, other);
        JsonObject body = PolicyFixture.Body(result);
        body["summary"] = "Checked the inventory, and corrected the report.";
        g.Add("result", body, builder: true, subject: result.Subject, supersedes: result.Id);
        model = g.Estate.Load();
        Assert.Equal(("accepted (earlier revision)", accepted.Id), (Status(model, other), EstateStatus.StatusRecord(model, other)!.Id));
    }

    [Fact]
    public void WhatReadAcceptedBeforeStillDoes()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "WORK-1");
        RecordEnvelope attempt = f.BeginAttempt(order: order);
        RecordEnvelope first = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        RecordEnvelope review = f.Review(first);
        RecordEnvelope again = f.Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock), ["workspace"] = "example repository", ["note"] = "Carry the work again." },
            builder: true, subject: attempt.Subject);
        f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: again.Reference);
        RecordEnvelope direct = f.Add("acceptance", new JsonObject { ["statement"] = "Accept the exact work order.", ["basis"] = new JsonArray(), ["self"] = false }, subject: order.Reference);
        EstateModel model = f.Estate.Load();
        Assert.Equal(("accepted", direct.Id), (Status(model, order), EstateStatus.StatusRecord(model, order)!.Id));

        // A later acceptance of the earlier result does not take the acceptance of the work order's current revision away.
        f.Add("acceptance", new JsonObject { ["statement"] = "Accept the earlier result.", ["basis"] = new JsonArray(review.Id), ["self"] = false }, subject: first.Reference);
        model = f.Estate.Load();
        Assert.Equal(("accepted", direct.Id), (Status(model, order), EstateStatus.StatusRecord(model, order)!.Id));
    }

    [Fact]
    public void AMilestoneIsStillAcceptedOnlyByItsOwnAcceptance()
    {
        using var f = new PolicyFixture();
        JsonObject body = Body("M-1");
        body["kind"] = "milestone";
        RecordEnvelope milestone = f.Add("work-order", body);
        f.Add("work-status", new JsonObject { ["status"] = "submitted", ["note"] = "Ready." }, subject: milestone.Reference);
        (_, RecordEnvelope closing) = Close(f, milestone);
        f.AssertValid();
        EstateModel model = f.Estate.Load();
        Assert.Equal("submitted", Status(model, milestone));
        Assert.Equal("milestone-acceptance", Assert.Single(OperatorActions.Derive(model)["items"]!.AsArray(), i => i!["kind"]!.GetValue<string>() == "milestone-acceptance")!["kind"]!.GetValue<string>());
        Assert.NotEqual(closing.Id, EstateStatus.StatusRecord(model, milestone)!.Id);

        RecordEnvelope accepted = f.Add("acceptance", new JsonObject { ["statement"] = "Accept the milestone.", ["basis"] = new JsonArray(), ["self"] = false }, subject: milestone.Reference);
        model = f.Estate.Load();
        Assert.Equal(("accepted", accepted.Id), (Status(model, milestone), EstateStatus.StatusRecord(model, milestone)!.Id));
    }

    private static string Status(EstateModel model, RecordEnvelope order) => EstateStatus.WorkItems(model).Single(w => w.Id == model.Latest(order).Id).Status;

    private static RecordEnvelope Order(PolicyFixture f, string key) => f.Add("work-order", Body(key));

    private static JsonObject Body(string key, string? after = null)
    {
        var body = new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = "Order " + key, ["intent"] = "Carry " + key + ".", ["scope"] = new JsonArray("fixture"),
            ["exitQuestion"] = "Is " + key + " done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the work"),
        };
        if (after is not null) body["after"] = new JsonArray(after);
        return body;
    }

    /// <summary>
    /// A completed result toward a work order, the operator's passing review, and the acceptance operator close records for it: of the
    /// exact result, with the reviews as its basis.
    /// </summary>
    internal static (RecordEnvelope Result, RecordEnvelope Closing) Close(PolicyFixture f, RecordEnvelope order)
    {
        RecordEnvelope attempt = f.BeginAttempt(order: order);
        RecordEnvelope result = f.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        RecordEnvelope review = f.Review(result);
        RecordEnvelope closing = f.Add("acceptance", new JsonObject
        {
            ["statement"] = "Closed with operator close after inspecting the exact result and passing reviews.", ["basis"] = new JsonArray(review.Id), ["self"] = false,
        }, subject: result.Reference);
        return (result, closing);
    }

    private static JsonElement Predicate(string key, string state, int? max = null)
    {
        var predicate = new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = key }, ["state"] = state };
        if (max is not null) predicate["max"] = max.Value;
        return JsonSerializer.SerializeToElement(predicate);
    }
}
