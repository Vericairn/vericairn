// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
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
    private static string Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!.Id;
    }
}
