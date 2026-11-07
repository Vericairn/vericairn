// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The software pack's release flow taken one step per command through the runtime (the estate's design/release-procedure.md §§ 6 and
/// 10): goal step puts acceptance, then publication, then a withdrawn candidate's draft deletion to the operator as requests carrying
/// each step's exact command only when it is next; the operator's own acts complete them, and an agent's close of a request never does.
/// </summary>
[Collection("home")]
public class SoftwareReleaseFlowRoutingTests
{
    private const string Version = "1.2.0";

    private const string Repository = "Example/example";

    [Fact]
    public void GoalStepPutsAcceptanceThenPublicationToTheOperatorEachWithItsCommandOnlyWhenNext()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        (string goal, RecordWritten candidate) = ToReview(run);
        string accept = $"vericairn records new acceptance --by <operator-id> --subject release:{candidate.Id}:{candidate.Hash} --body accept-release.json";
        string publish = $"gh release edit v{Version} -R {Repository} --draft=false --latest && vericairn release observe {Version} --by <operator-id>";

        // The review is the reviewer's; goal step records no request while it is pending, and no command is shown anywhere.
        Assert.DoesNotContain(accept, Shown(run, goal), StringComparison.Ordinal);
        Assert.Equal("none", run.Service.GoalStep(goal, new GoalStepRequest(op)).Outcome);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", run.Estate.BuilderId,
            RunEstate.Json("""{"independence":"independent-agent","outcome":"pass","summary":"Reviewed the exact candidate.","findings":[]}"""), $"release:{candidate.Id}:{candidate.Hash}")));

        // Acceptance is next: its request carries the candidate's exact hash; publication's command appears nowhere yet.
        GoalStepResponse accepting = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(accepting.Ok, accepting.Message);
        Assert.Equal(("request", "accept"), (accepting.Outcome, accepting.Step));
        RecordEnvelope request = run.Estate.Load().ById[accepting.Request!.Id];
        Assert.Equal(("action", accept), (EstateModel.BodyString(request, "kind"), request.Body.GetProperty("content")[0].GetProperty("text").GetString()));
        Assert.Contains(accept, run.Service.OperatorRequests().Text, StringComparison.Ordinal);
        Assert.DoesNotContain(publish, Shown(run, goal), StringComparison.Ordinal);

        // Closing the request is no acceptance: the step waits on the operator's acceptance of the exact candidate.
        RunEstate.Ok(run.Service.OperatorRequestDone(request.Id, new OperatorRequestCloseRequest(op)));
        Assert.Equal("stopped", Step(run, goal, "accept").GetProperty("state").GetString());
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("acceptance", op, RunEstate.Json("""{"statement":"Accept the exact candidate the review passed.","basis":[],"self":true}"""), $"release:{candidate.Id}:{candidate.Hash}")));
        Assert.Equal("done", Step(run, goal, "accept").GetProperty("state").GetString());

        // Publication is next: a publication request with the provider's stable-channel command.
        GoalStepResponse publishing = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("request", "publish"), (publishing.Outcome, publishing.Step));
        RecordEnvelope publication = run.Estate.Load().ById[publishing.Request!.Id];
        Assert.Equal(("publication", publish), (EstateModel.BodyString(publication, "kind"), publication.Body.GetProperty("content")[0].GetProperty("text").GetString()));

        // The operator's observation of the publication completes it; the documentation pass is next.
        JsonObject published = JsonNode.Parse(run.Estate.Load().ById[candidate.Id].Body.GetRawText())!.AsObject();
        published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(5));
        published["url"] = "https://example.invalid/releases/" + Version;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("release", op, JsonSerializer.SerializeToElement(published), $"product:{ProductOf(run)}", candidate.Id)));
        JsonElement flow = run.Service.Goal(goal)!.Json.GetProperty("flow");
        Assert.Equal(("done", "done", "done"), (State(flow, "publish"), State(flow, "observe"), State(flow, "accept")));
        Assert.Equal("off", State(flow, "upgrade-estate"));

        // goal show names each step's traps, with the ones its action checks.
        JsonElement gate = Assert.Single(flow.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == "gate");
        Assert.Contains(gate.GetProperty("traps").EnumerateArray(), t => t.GetProperty("name").GetString() == "client-build" && t.GetProperty("check").GetString() == "release.client-build" && !t.GetProperty("records").GetBoolean());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AWithdrawnCandidatesDraftDeletionWaitsOnTheOperatorWhoseOwnCloseCompletesIt()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        (string goal, RecordWritten candidate) = ToReview(run);
        ReleaseResponse withdrawn = run.Service.ReleaseWithdraw(new ReleaseWithdrawRequest(candidate.Id, op, candidate.Hash, "test-releases", "The candidate is replaced.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repository"] = Repository }));
        Assert.True(withdrawn.Ok, withdrawn.Message);

        // The branch is entered: the draft's deletion is a publication request carrying the provider's deletion command.
        GoalStepResponse deleting = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.True(deleting.Ok, deleting.Message);
        Assert.Equal(("request", "delete-draft"), (deleting.Outcome, deleting.Step));
        RecordEnvelope request = run.Estate.Load().ById[deleting.Request!.Id];
        Assert.Equal(("publication", $"gh release delete v{Version} -R {Repository} --yes"), (EstateModel.BodyString(request, "kind"), request.Body.GetProperty("content")[0].GetProperty("text").GetString()));

        // An agent's close citing the operator's words is kept and completes nothing.
        RunEstate.Ok(run.Service.OperatorRequestDone(request.Id, new OperatorRequestCloseRequest(run.Estate.BuilderId, Link: "https://example.invalid/draft",
            OperatorWords: new OperatorRequestWords(op, "I deleted the draft.", "Operator conversation, message 3", request.At))));
        Assert.Equal("stopped", Step(run, goal, "delete-draft").GetProperty("state").GetString());

        // goal step puts the step to the operator again; the operator's own close of that request, with the link, completes it.
        GoalStepResponse again = run.Service.GoalStep(goal, new GoalStepRequest(op));
        Assert.Equal(("request", "delete-draft"), (again.Outcome, again.Step));
        RunEstate.Ok(run.Service.OperatorRequestDone(again.Request!.Id, new OperatorRequestCloseRequest(op, Link: "https://example.invalid/releases")));
        JsonElement flow = run.Service.Goal(goal)!.Json.GetProperty("flow");
        Assert.Equal(("done", "next"), (State(flow, "delete-draft"), State(flow, "observe-withdrawal")));
        Assert.Contains(Step(run, goal, "review").GetProperty("reasons").EnumerateArray(), r => r.GetString()!.StartsWith("trap not-withdrawn:", StringComparison.Ordinal));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>A run of the release flow taken to its review: placed work accepted, the guides checked, the set and its merge handed on, the submission dispatched and the candidate observed.</summary>
    private static (string Goal, RecordWritten Candidate) ToReview(RunEstate run)
    {
        string op = run.Estate.OperatorId;
        string builder = run.Estate.BuilderId;
        string basis = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", op, RunEstate.Json($$"""{"title":"Delegate engineering","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"{{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(-1))}}"}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("authority", op, RunEstate.Json($$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":[],"basis":"{{basis}}"}"""))));
        RecordWritten product = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("product", op, JsonSerializer.SerializeToElement(ReleaseLineFixture.ProductBody()))));
        JsonElement plan = Order("REL-1", releasePlan: product.Id);
        RecordWritten carrier = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, plan, Expected: Vericairn.Runtime.Policies.PolicyBasis.Hash(plan))));
        foreach (string key in new[] { "WORK-1", "WORK-2" })
        {
            JsonElement placed = Order(key, placedIn: (carrier.Id, product.Id));
            RecordWritten member = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, placed, Expected: Vericairn.Runtime.Policies.PolicyBasis.Hash(placed))));
            RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("acceptance", op, RunEstate.Json("""{"statement":"Accept the work.","basis":[],"self":true}"""), $"work-order:{member.Id}:{member.Hash}")));
        }

        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, RunEstate.Json($$"""
            {"roles":[{"role":"release-manager","principals":["{{builder}}"]},{"role":"submitter","principals":["{{op}}"]},{"role":"reviewer","principals":["{{builder}}"]},{"role":"decider","principals":["{{op}}"]},{"role":"publisher","principals":["{{op}}"]}]}
            """))));
        var body = new JsonObject
        {
            ["title"] = "Release " + Version, ["objective"] = "Release the product.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = new JsonObject { ["type"] = "acceptance", ["subject"] = carrier.Id } }), ["constraints"] = new JsonArray(),
            ["kind"] = "finite", ["sponsor"] = op, ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 3, ["concurrency"] = 1 },
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(carrier.Id),
                ["flow"] = new JsonObject
                {
                    ["pin"] = FlowFixture.Pin(SoftwareFlows.ReleaseFlow),
                    ["subject"] = new JsonObject
                    {
                        ["carrier"] = new JsonObject { ["type"] = "work-order", ["id"] = carrier.Id }, ["product"] = new JsonObject { ["type"] = "product", ["id"] = product.Id },
                        ["version"] = Version, ["repository"] = Repository,
                    },
                },
            },
            ["inputs"] = new JsonArray(),
        };
        string goal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(op, JsonSerializer.SerializeToElement(body)))).Id;
        RunEstate.Ok(run.Service.GoalActivate(goal, new GoalActionRequest(op)));

        foreach (string method in new[] { "vericairn.software/prior-version-tokens", "vericairn.software/preparation-wording" })
        {
            RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("verification", builder, RunEstate.Json($$"""
                {"method":"{{method}}","commit":"{{new string('a', 40)}}","command":"vericairn docs pass {{Version}} --before","environment":{"release":"{{Version}}"},"role":"self","outcome":"passed","summary":"Checked the guides.","evidence":[],"limitations":[]}
                """), $"product:{product.Id}:{product.Hash}")));
        }

        WorkStep(run, carrier, "docs-set", ("manifest", FlowFixture.Hash('1'), "application/json"), ("notes", FlowFixture.Hash('2'), "text/markdown"));
        WorkStep(run, carrier, "release-commit", ("frozen-index", FlowFixture.Hash('3'), "application/json"));
        RecordWritten intent = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("operation", op, RunEstate.Json($$"""
            {"provider":"test-releases","action":"dispatch-release-workflow","identity":"test-releases:{{Repository}}:v{{Version}}","intent":"Build the release into a draft.","target":{"repository":"{{Repository}}","version":"{{Version}}","product":"{{product.Id}}"},"status":"not-started","evidence":[]}
            """))));
        JsonObject dispatched = JsonNode.Parse(run.Estate.Load().ById[intent.Id].Body.GetRawText())!.AsObject();
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("operation", op, JsonSerializer.SerializeToElement(dispatched), null, intent.Id)));
        RecordWritten candidate = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("release", op, JsonSerializer.SerializeToElement(ReleaseLineFixture.Body(Version)), $"product:{product.Id}")));
        Assert.Equal(0, run.Service.Check().Errors);
        Assert.Equal("next", Step(run, goal, "review").GetProperty("state").GetString());
        return (goal, candidate);
    }

    private static void WorkStep(RunEstate run, RecordWritten carrier, string step, params (string Name, string Hash, string MediaType)[] outputs)
    {
        string assignment = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, RunEstate.Json($$$"""
            {"contributor":"{{{run.Estate.BuilderId}}}","step":{"key":"{{{step}}}"},"brief":{"outcome":"Carry step {{{step}}}.","sources":[],"permittedChanges":["docs/"],"prohibitedEffects":["publication"],"workspace":["the product's repository"],"limits":[],"expectedOutputs":["the step's outputs"],"reviewCriteria":["the outputs exist"],"recovery":"Carry it again."}}
            """), $"work-order:{carrier.Id}:{carrier.Hash}"))).Id;
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "the product's repository, a work branch", "Carry the step."))).Id;
        var named = new JsonArray(outputs.Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["hash"] = o.Hash, ["mediaType"] = o.MediaType }).ToArray());
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, JsonSerializer.SerializeToElement(new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = named, ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Hand it on.",
        }))));
    }

    private static JsonElement Order(string key, string? releasePlan = null, (string Carrier, string Product)? placedIn = null)
    {
        var body = new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = "Order " + key, ["intent"] = "Carry " + key + ".", ["scope"] = new JsonArray("fixture"),
            ["exitQuestion"] = "Is " + key + " done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the work"),
        };
        if (releasePlan is not null) body["releasePlan"] = new JsonObject { ["product"] = releasePlan, ["version"] = Version };
        if (placedIn is { } placed) body["placement"] = new JsonObject { ["kind"] = "release", ["carrier"] = placed.Carrier, ["product"] = placed.Product, ["version"] = Version };
        return JsonSerializer.SerializeToElement(body);
    }

    private static string ProductOf(RunEstate run) => run.Estate.Load().Current("product").Single().Id;

    private static string Shown(RunEstate run, string goal) =>
        string.Join('\n', run.Service.Goal(goal)!.Text, run.Service.GoalNext(goal)!.Text, run.Service.Status().Text, run.Service.OperatorRequests().Text);

    private static JsonElement Step(RunEstate run, string goal, string key) =>
        Assert.Single(run.Service.Goal(goal)!.Json.GetProperty("flow").GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);

    private static string? State(JsonElement flow, string key) =>
        Assert.Single(flow.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key).GetProperty("state").GetString();
}
