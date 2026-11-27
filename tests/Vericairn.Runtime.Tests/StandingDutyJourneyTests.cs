// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class StandingDutyJourneyTests
{
    [Fact]
    [Trait("Journey", "D07-B")]
    public async Task ACommunityMonthlyDutyKeepsOneAssignmentThroughDeliveryRestartAndReview()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        RecordEnvelope policy = DeclareDuty(run);
        string goal = StandingAgentRoutingTests.Standing(run, triggers: new JsonArray(DutyTrigger(policy)), role: "duty-carrier");
        StandingAgentRoutingTests.AllowStandInWithFixtureKey(run, goal);
        string key = $"duty/{policy.Id}:{run.Estate.Estate.Id}:{{\"kind\":\"interval\",\"number\":0}}";

        AssertOwed();
        Assert.Equal((key, "due"), Occurrence(run, goal, key));
        Assert.DoesNotContain(run.Estate.Load().OfType("assignment"), a => StandingAgents.KeyOf(a) == key);

        // Concurrent goal steps through the same runtime leave one run and one assignment. The record validator's
        // standing.duplicate refusal is tested separately by StandingRoleTests.ADutyCarrierCarriesTheDutyOccurrencesOwnKeyAndLeavesReservedActionsToTheOperator.
        using var start = new Barrier(2);
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        GoalStepResponse[] arrivals = await Task.WhenAll(Enumerable.Range(0, 2).Select(index =>
            Task.Factory.StartNew(() =>
            {
                Assert.True(start.SignalAndWait(TestTiming.HangTimeout, cancellation));
                return StandingAgentRoutingTests.Step(run, goal, $"duty-delivery-{index}", key, "ack-stop");
            }, cancellation, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        GoalStepResponse dispatched = Assert.Single(arrivals, response => response.Outcome == "run");
        Assert.Single(AgentRuns.All(run.Estate.Load()), candidate => candidate.Goal == goal);
        Assert.Equal(key, dispatched.Step);
        string assignment = Assert.Single(run.Estate.Load().OfType("assignment"), a => StandingAgents.KeyOf(a) == key).Id;
        Assert.Equal(assignment, dispatched.Assignment!.Id);
        Assert.Equal("none", StandingAgentRoutingTests.Step(run, goal, "repeated-delivery", key).Outcome);
        AssertOwed();

        string running = dispatched.Run!.Run;
        run.WaitUntil(() => run.Row(running).Observed && Enumerable.Range(0, 2).Any(i => File.Exists(run.Report($"duty-delivery-{i}"))));
        Assert.True(run.Service.RunStop(running, new RunStopRequest(op, "Stop this fixture run.", 5)).Ok);
        Assert.Equal("known-failure", run.Settled(running).Status);
        Assert.Equal((key, "due"), Occurrence(run, goal, key));
        AssertOwed();

        run.Reopen();
        GoalStepResponse failed = StandingAgentRoutingTests.Step(run, goal, "retry-that-fails", key, "claude-fails");
        Assert.True(failed.Ok, failed.Message);
        Assert.Equal("known-failure", run.Settled(failed.Run!.Run).Status);
        Assert.Equal((key, "due"), Occurrence(run, goal, key));
        AssertOwed();

        GoalStepResponse retried = StandingAgentRoutingTests.Step(run, goal, "retry-that-succeeds", key);
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal("run", retried.Outcome);
        Assert.Equal("observed-success", run.Settled(retried.Run!.Run).Status);
        Assert.Equal(assignment, Assert.Single(run.Estate.Load().OfType("assignment"), a => StandingAgents.KeyOf(a) == key).Id);
        AssertOwed();

        string builder = run.Estate.BuilderId;
        RecordWritten attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, builder, "Monthly maintenance fixture", "Check the inventory.")));
        var link = new JsonObject
        {
            ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "monthly", ["subject"] = PolicyFixture.Ref(run.Estate.Estate),
            ["occurrence"] = new JsonObject { ["kind"] = "interval", ["number"] = 0 },
            ["scope"] = new JsonObject { ["repositories"] = new JsonArray(), ["objects"] = new JsonArray(), ["records"] = new JsonArray() },
            ["coverage"] = "Inspected the declared inventory.", ["proofs"] = new JsonArray(),
        };
        JsonElement resultBody = PolicyFixture.Element(PolicyFixture.ResultBody(link));
        Assert.False(run.Service.Result(new ResultRequest(attempt.Id, builder, resultBody)).Ok);
        RecordWritten result = RunEstate.Ok(run.Service.Result(new ResultRequest(attempt.Id, builder, resultBody, attempt.Hash)));
        Assert.Equal((key, "settled"), Occurrence(run, goal, key));
        AssertOwed();
        JsonElement review = RunEstate.Json("""
            {"independence":"operator","outcome":"pass","summary":"Reviewed the exact inventory result.","findings":[]}
            """);
        Assert.False(run.Service.ReviewResult(new ResultReviewRequest(result.Id, op, review, attempt.Hash)).Ok);
        RunEstate.Ok(run.Service.ReviewResult(new ResultReviewRequest(result.Id, op, review, result.Hash)));
        Assert.Equal((key, "disposed"), Occurrence(run, goal, key));
        Assert.True(Duty().Completed);
        Assert.Equal("none", StandingAgentRoutingTests.Step(run, goal, "delivery-after-review", key).Outcome);
        Assert.Equal(0, run.Service.Check().Errors);

        DutyFindingDto Duty() => Assert.Single(run.Service.Policy("maintenance")!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!;
        void AssertOwed() => Assert.False(Duty().Completed);
    }

    // D07-B: a reserved publication reached by the duty carrier becomes an operator request.
    [Fact]
    [Trait("Journey", "D07-B")]
    public void ACommunityDutyCarrierAsksTheOperatorToPublishItsDraft()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        RecordEnvelope policy = DeclareDuty(run);
        string goal = StandingAgentRoutingTests.Standing(run, triggers: new JsonArray(DutyTrigger(policy)), role: "duty-carrier");
        StandingAgentRoutingTests.AllowStandInWithFixtureKey(run, goal);

        const string report = "duty-publication";
        const string destination = "the estate's monthly notes";
        const string text = "This month's inventory was checked.";
        File.WriteAllText(run.Report(report) + ".call", new JsonObject
        {
            ["name"] = "standing_publication",
            ["arguments"] = new JsonObject
            {
                ["body"] = new JsonObject
                {
                    ["destination"] = destination, ["text"] = text, ["why"] = "The inventory report is public.",
                    ["recommendation"] = "Publish the draft.", ["consequence"] = "The draft waits for the operator.",
                },
            },
        }.ToJsonString());
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Vericairn = FakeHarness.Command("mcp-server", run.Report("duty-mcp")),
        };
        Assert.Empty(run.Estate.Load().OfType("operator-request"));

        string key = $"duty/{policy.Id}:{run.Estate.Estate.Id}:{{\"kind\":\"interval\",\"number\":0}}";
        GoalStepResponse step = StandingAgentRoutingTests.Step(run, goal, report, key, "mcp-tool");
        Assert.True(step.Ok, step.Message);
        Assert.Equal("run", step.Outcome);
        string assignment = step.Assignment!.Id;
        string running = step.Run!.Run;
        Assert.Equal("observed-success", run.Settled(running).Status);

        EstateModel model = run.Estate.Load();
        RecordEnvelope request = Assert.Single(model.OfType("operator-request"));
        Assert.Equal(new StandingOutputs.RaisedBy(goal, key, assignment, running), StandingOutputs.Of(request));
        Assert.Equal(model.ById[assignment].Subject, request.Subject);
        Assert.Equal(run.Estate.BuilderId, request.By);
        Assert.Equal((run.Estate.OperatorId, run.Estate.BuilderId, "publication", destination),
            (EstateModel.BodyString(request, "to"), EstateModel.BodyString(request, "askedBy"),
                EstateModel.BodyString(request, "kind"), EstateModel.BodyString(request, "destination")));
        JsonElement content = Assert.Single(request.Body.GetProperty("content").EnumerateArray());
        Assert.Equal(("text", text, ContentHash.Sha256(Encoding.UTF8.GetBytes(text))),
            (content.GetProperty("kind").GetString(), content.GetProperty("text").GetString(), content.GetProperty("hash").GetString()));
        JsonElement delivered = Assert.Single(run.Service.OperatorRequests(run.Estate.OperatorId).Json.GetProperty("delivered").EnumerateArray());
        Assert.Equal(request.Id, delivered.GetProperty("revision").GetString());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    internal static (string? Key, string? State) Occurrence(RunEstate run, string goal, string key)
    {
        JsonElement row = Assert.Single(run.Service.GoalDue(goal)!.Json.GetProperty("occurrences").EnumerateArray(),
            row => row.GetProperty("key").GetString() == key);
        return (row.GetProperty("key").GetString(), row.GetProperty("state").GetString());
    }

    internal static JsonObject DutyTrigger(RecordEnvelope policy) => new()
    {
        ["key"] = "maintenance", ["kind"] = "duty", ["policy"] = policy.Id, ["rule"] = "monthly",
    };

    internal static RecordEnvelope DeclareDuty(RunEstate run)
    {
        string op = run.Estate.OperatorId;
        DateTimeOffset declared = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        string first = RecordParser.FormatTimestamp(declared);
        var definition = new JsonObject
        {
            ["key"] = "maintenance", ["title"] = "Test maintenance", ["owner"] = op, ["reason"] = "Keep an inventory history.",
            ["state"] = "active", ["appliesTo"] = new JsonObject { ["kind"] = "estate" }, ["level"] = "refuse",
            ["startsAt"] = first, ["maxExceptionDays"] = 30,
            ["rules"] = new JsonArray(new JsonObject
            {
                ["key"] = "monthly", ["kind"] = DutyRecords.Kind, ["version"] = 1,
                ["parameters"] = new JsonObject
                {
                    ["subject"] = PolicyFixture.Ref(run.Estate.Estate, false),
                    ["schedule"] = new JsonObject { ["kind"] = "interval", ["unit"] = "months", ["every"] = 1, ["firstDueAt"] = first },
                    ["graceDays"] = 3, ["renewal"] = "none",
                    ["discharge"] = new JsonObject { ["checks"] = new JsonArray("inventory"), ["review"] = "independent", ["records"] = new JsonArray() },
                },
            }),
        };
        JsonObject decision = RunEstate.Json("""
            {"title":"Approve monthly maintenance","statement":"Approve the fixture policy.","summary":"Keep inventory.",
             "synthesis":"Use the recorded inventory.","effects":["Apply the approved policy."],"affects":[],"stake":"medium","notAuthorized":["publication"]}
            """).Deserialize<JsonObject>()!;
        decision["decidedBy"] = op;
        decision["decidedAt"] = first;
        decision["policies"] = new JsonArray(new JsonObject
        {
            ["key"] = "definition", ["action"] = "declare", ["policy"] = "maintenance", ["change"] = "Approve the exact policy.",
            ["definitionHash"] = PolicyRequirements.DefinitionHash(PolicyFixture.Element(definition)),
        });
        RecordWritten approved = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, PolicyFixture.Element(decision), At: first)));
        definition["basis"] = new JsonArray(new JsonObject
        {
            ["decision"] = new JsonObject { ["type"] = "decision", ["id"] = approved.Id, ["hash"] = approved.Hash }, ["requirement"] = "definition",
        });
        JsonElement body = PolicyFixture.Element(definition);
        TimeProvider previousClock = run.Service.PolicyClock;
        run.Service.PolicyClock = new WorkerClock(declared);
        try
        {
            RecordWritten policy = RunEstate.Ok(run.Service.PolicyDeclare(new PolicyChangeRequest(op, body, PolicyBasis.Hash(body))));
            return run.Estate.Load().ById[policy.Id];
        }
        finally
        {
            run.Service.PolicyClock = previousClock;
        }
    }
}
