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
public class CommunicationQualificationTests
{
    private const string AssignmentBody = """{"outcome":"Qualify the communication journey.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["test results"],"reviewCriteria":["journeys pass"],"recovery":"Read the recorded result before continuing."}""";

    [Fact]
    public void D08A_TwoRecipientsResolveTheSameProfileThroughCliMcpAndExportedBriefs()
    {
        using var estate = new HandoffEstate();
        string other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Reviewer","roles":["contributor"]}""").Id;
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Communication journey","intent":"Qualify all surfaces.","scope":["tests"],"exitQuestion":"Consistent?","inputs":[],"deliverables":["results"]}""");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "short", "--decisions", "comprehensive", "--handoffs", "short");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", other, "--status", "comprehensive", "--decisions", "standard", "--handoffs", "comprehensive");
        using EstateService api = EstateService.Open(estate.Root);
        var mcp = new ToolRegistry(api, null);
        int records = estate.Load().Records.Count;
        foreach ((string recipient, string depth, string decision) in new[] { (estate.OperatorId, "short", "comprehensive"), (other, "comprehensive", "standard") })
        {
            CommunicationProfile expected = api.Communication(recipient);
            CommunicationProfile cli = JsonSerializer.Deserialize<CommunicationProfile>(CliHarness.Ok("--estate", estate.Root, "--recipient", recipient, "--json", "communication", "show").Out, ProtocolJson.Options)!;
            ToolCallResult read = mcp.Call("communication_get", new JsonObject { ["recipient"] = recipient });
            Assert.False(read.IsError);
            CommunicationProfile tool = JsonSerializer.Deserialize<CommunicationProfile>(read.Text, ProtocolJson.Options)!;
            foreach (CommunicationProfile profile in new[] { expected, cli, tool })
            {
                Assert.Equal(depth, profile.Fields["status"].Value);
                Assert.Equal(decision, profile.Fields["decision"].Value);
                Assert.Equal("person", profile.Fields["status"].Source);
                Assert.Equal(expected.RevisionHash, profile.RevisionHash);
                Assert.Equal(expected.Revisions.Single().Hash, profile.Revisions.Single().Hash);
                Assert.Equal(recipient, profile.Revisions.Single().By);
            }

            string brief = CliHarness.Ok("--estate", estate.Root, "--recipient", recipient, "work", "brief", "COMMS-J").Out;
            ToolCallResult exported = mcp.Call("work_brief", new JsonObject { ["workOrder"] = "COMMS-J", ["recipient"] = recipient });
            Assert.False(exported.IsError);
            foreach (string text in new[] { brief, exported.Text, api.BriefContext("COMMS-J", recipient: recipient)!.Markdown })
            {
                Assert.Contains($"handoff ({depth})", text, StringComparison.Ordinal);
                Assert.Contains($"decision: {decision} ← person", text, StringComparison.Ordinal);
                Assert.Contains(expected.Revisions.Single().Hash!, text, StringComparison.Ordinal);
                Assert.Contains("Qualify all surfaces.", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("short", api.Communication(estate.OperatorId).Fields["status"].Value);
    }

    [Fact]
    public void D08A_AssignmentAndViewOriginsAreVisibleWithoutChangingCanonicalPayloads()
    {
        using var estate = new HandoffEstate();
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Communication journey","intent":"Keep exact originals.","scope":["tests"],"exitQuestion":"Consistent?","inputs":[],"deliverables":["results"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        string assignment = Ok(api.Assign(new("COMMS-J", estate.BuilderId, estate.OperatorId, Json(AssignmentBody))));
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "short", "--handoffs", "comprehensive");
        CliHarness.Ok("--estate", estate.Root, "--for-assignment", assignment, "communication", "set", "--as", estate.OperatorId, "--status", "standard", "--handoffs", "short");
        CommunicationProfile scoped = api.Communication(estate.OperatorId, assignment);
        Assert.Equal("assignment", scoped.Fields["status"].Source);
        Assert.Equal("standard", scoped.Fields["status"].Value);
        var mcp = new ToolRegistry(api, null);
        string read = mcp.Call("communication_get", new JsonObject { ["recipient"] = estate.OperatorId, ["assignment"] = assignment }).Text;
        Assert.Contains(scoped.Revisions.Single(r => r.Scope == "assignment").Hash!, read, StringComparison.Ordinal);
        string[] prefix = ["--estate", estate.Root, "--recipient", estate.OperatorId, "--for-assignment", assignment];
        string status = CliHarness.Ok([.. prefix, "--json", "status"]).Out;
        string brief = CliHarness.Ok([.. prefix, "--json", "work", "brief", "COMMS-J"]).Out;
        string handoff = CliHarness.Ok([.. prefix, "--json", "handoff"]).Out;
        int records = estate.Load().Records.Count;
        foreach (string depth in CommunicationResolution.Depths)
        {
            string description = CliHarness.Ok([.. prefix, "--depth", depth, "communication", "show"]).Out;
            Assert.Contains($"status: {depth} ← view", description, StringComparison.Ordinal);
            Assert.Contains("assignment record " + scoped.Revisions.Single(r => r.Scope == "assignment").Record, description, StringComparison.Ordinal);
            Assert.Equal(status, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "status"]).Out);
            Assert.Equal(brief, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "work", "brief", "COMMS-J"]).Out);
            Assert.Equal(handoff, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "handoff"]).Out);
            Assert.Equal(api.Brief("COMMS-J")!.Json.GetRawText(), api.BriefFor("COMMS-J", estate.OperatorId, assignment, new(Handoff: depth))!.Json.GetRawText());
        }

        Assert.Equal(status, CanonicalJson.SerializePretty(api.Status().Json));
        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("standard", api.Communication(estate.OperatorId, assignment).Fields["status"].Value);
        Assert.Equal("short", api.Communication(estate.OperatorId).Fields["status"].Value);
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static string Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message + JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!.Id;
    }
}
