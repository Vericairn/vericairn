// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class ReviewObservationTests
{
    [Theory]
    [InlineData("short")]
    [InlineData("standard")]
    [InlineData("comprehensive")]
    public void WorkObservationKeepsEveryReviewFindingAndTheReasonsForUnperformedChecks(string depth)
    {
        using var estate = new HandoffEstate();
        using EstateService api = EstateService.Open(estate.Root);
        Ok(api.NewRecord(new("work-order", estate.OperatorId, Json("""{"key":"COMMS-REVIEW","kind":"ordinary","title":"Review the result","intent":"Keep every finding visible.","scope":["tests"],"exitQuestion":"Reviewed?","inputs":[],"deliverables":["result"]}"""))));
        string assignment = Ok(api.Assign(new("COMMS-REVIEW", estate.BuilderId, estate.OperatorId, Json("""{"outcome":"Inspect the result.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["all findings visible"],"recovery":"Inspect the checks."}"""))));
        string attempt = Ok(api.Attempt(new(assignment, estate.BuilderId, "local clone", "Inspect.")));
        string result = Ok(api.Result(new(attempt, estate.BuilderId, Json("""{"outcome":"partial","summary":"Ready for review.","outputs":[],"evidence":[],"checks":[{"name":"platform matrix","outcome":"not-performed","summary":"The other platforms have not run."},{"name":"live harness","outcome":"skipped","summary":"No real coding agent was launched."}],"limitations":[],"unresolvedEffects":[],"nextStep":"Inspect all findings."}"""))));
        string review = Ok(api.NewRecord(new("review", estate.OperatorId, Json("""{"independence":"operator","outcome":"findings","summary":"Two issues remain.","findings":[{"severity":"error","summary":"The returned identity is wrong.","location":"src/identity.cs"},{"severity":"warning","summary":"The platform evidence is incomplete.","location":"tests/platform.cs"}]}"""), $"result:{result}:{api.Record(result)!.Hash}")));
        Ok(api.CommunicationSet(new(estate.OperatorId, "person", new(Progress: "none", Result: depth), estate.OperatorId)));
        string cli = CliHarness.Ok("--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", depth, "work", "observe", "COMMS-REVIEW").Out;
        Assert.Contains($"result ({depth})", cli, StringComparison.Ordinal);
        var mcp = new ToolRegistry(api, null);
        ToolCallResult observed = mcp.Call("work_observe", new JsonObject { ["workOrder"] = "COMMS-REVIEW" });
        Assert.False(observed.IsError);
        foreach (string text in new[] { cli, observed.Text, api.Observe("COMMS-REVIEW")!.Text })
        {
            foreach (string required in new[] { review, "Two issues remain.", "The returned identity is wrong.", "src/identity.cs", "The platform evidence is incomplete.", "tests/platform.cs", "platform matrix: not-performed", "The other platforms have not run.", "live harness: skipped", "No real coding agent was launched." })
            {
                Assert.Contains(required, text, StringComparison.Ordinal);
            }
        }

        Assert.Empty(api.Records("acceptance"));
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    [Fact]
    public void D08F_ConciseDecisionAndReviewArtifactsKeepTheirCanonicalOriginals()
    {
        using var estate = new HandoffEstate();
        using EstateService api = EstateService.Open(estate.Root);
        string decision = Ok(api.NewRecord(new("decision", estate.OperatorId, Json($$"""{"title":"Review before accepting","statement":"Inspect every finding; no acceptance is authorized.","summary":"Review the original.","synthesis":"Short presentation preserves the original obligations.","effects":["Inspect every finding."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["No acceptance."]}"""))));
        string review = Ok(api.NewRecord(new("review", estate.OperatorId, Json("""{"independence":"self","outcome":"findings","summary":"One issue remains.","findings":[{"severity":"warning","summary":"Independent review has not been performed.","location":"docs/review.md"}]}"""), $"decision:{decision}:{api.Record(decision)!.Hash}")));
        string canonicalDecision = CliHarness.Ok("--estate", estate.Root, "--json", "decisions", decision).Out;
        byte[] canonicalReview = CanonicalJson.SerializePrettyUtf8(api.Record(review)!.Envelope);
        var mcp = new ToolRegistry(api, null);
        foreach (string depth in CommunicationResolution.Depths)
        {
            string human = CliHarness.Ok("--estate", estate.Root, "--depth", depth, "decisions", decision).Out;
            Assert.Contains($"decision ({depth})", human, StringComparison.Ordinal);
            Assert.Contains("Inspect every finding; no acceptance is authorized.", human, StringComparison.Ordinal);
            Assert.Contains("No acceptance.", human, StringComparison.Ordinal);
            Assert.Equal(canonicalDecision, CliHarness.Ok("--estate", estate.Root, "--depth", depth, "--json", "decisions", decision).Out);
            Assert.Equal(canonicalReview, System.Text.Encoding.UTF8.GetBytes(CliHarness.Ok("--estate", estate.Root, "--depth", depth, "records", "show", review).Out));
        }

        ToolCallResult fetched = mcp.Call("record_get", new JsonObject { ["id"] = review });
        Assert.False(fetched.IsError);
        Assert.Equal(canonicalReview, CanonicalJson.SerializePrettyUtf8(Json(fetched.Text).GetProperty("envelope")));
        Assert.Contains("Independent review has not been performed.", fetched.Text, StringComparison.Ordinal);
        Assert.Empty(api.Records("acceptance"));
    }

    private static string Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!.Id;
    }
}
