// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// From 0.9.4 (GOAL-07): goal due through the SDK against a running daemon reads what the embedded runtime reads, at an explicit time,
/// and goal step carries the occurrence the request names, its key crossing the wire.
/// </summary>
[Collection("daemon")]
public class StandingApiTests
{
    public StandingApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task GoalDueReadsTheSameOccurrencesAsTheRuntimeAndGoalStepCarriesTheOneNamed()
    {
        using var estate = new TestEstate(recordFormat: 3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(estate.Root)!);
        string op = estate.OperatorId;
        Records.Templates.TemplateDefinition flow = FlowFixture.Install(estate, StandingFlows.Researcher());
        string order = Ok(api.NewRecord(new NewRecordRequest("work-order", op, Json("""{"key":"RES-1","kind":"ordinary","title":"Research","intent":"Keep researching.","scope":["path:research/"],"exitQuestion":"Researched?","inputs":[],"deliverables":["findings"]}""")))).Id;
        string firstAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-5).ToUnixTimeSeconds()));
        var body = new JsonObject
        {
            ["title"] = "Research", ["objective"] = "Keep researching.", ["completion"] = new JsonArray(new JsonObject { ["record"] = FlowFixture.Marker("RESEARCHED") }), ["constraints"] = new JsonArray(),
            ["kind"] = "stewardship", ["sponsor"] = op, ["budget"] = new JsonObject { ["wallMinutes"] = 30, ["attemptsPerStep"] = 2 }, ["roles"] = new JsonArray(FlowFixture.Role("researcher", op)),
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order),
                ["standing"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(flow), ["subject"] = new JsonObject { ["topic"] = "the widget" },
                    ["triggers"] = new JsonArray(new JsonObject { ["key"] = "daily", ["kind"] = "schedule", ["unit"] = "days", ["every"] = 1, ["firstAt"] = firstAt }),
                    ["period"] = new JsonObject { ["unit"] = "months", ["runs"] = 4, ["wallMinutes"] = 120 },
                    ["until"] = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds())),
                },
            },
            ["inputs"] = new JsonArray(),
        };
        string goal = Ok(api.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(body)))).Id;
        Ok(api.GoalActivate(goal, new GoalActionRequest(op)));
        string asOf = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        GoalDueResponse due = api.GoalDue(goal, asOf)!;
        Assert.Equal(CanonicalJson.SerializePretty(host.Service.GoalDue(goal, asOf)!.Json), CanonicalJson.SerializePretty(due.Json));
        string key = Assert.Single(due.Json.GetProperty("occurrences").EnumerateArray()).GetProperty("key").GetString()!;
        Assert.Equal("active", (await api.GoalDueAsync(goal, cancellationToken: TestContext.Current.CancellationToken))!.Json.GetProperty("state").GetString());
        Assert.Null(api.GoalDue("01M7ASMJ43WMQ5VDASDYVZ7BGV"));

        GoalStepResponse step = api.GoalStep(goal, new GoalStepRequest(op) { Occurrence = key });
        Assert.True(step.Ok, step.Message);
        Assert.Equal(("ask", key, "person"), (step.Outcome, step.Step, step.Route));
        Assert.Equal(key, estate.Load().ById[step.Assignment!.Id].Body.GetProperty("occurrence").GetProperty("key").GetString());
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
