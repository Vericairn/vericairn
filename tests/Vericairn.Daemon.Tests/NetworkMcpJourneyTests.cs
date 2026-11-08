// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// MCP-03's exit question, as section 12 of the network design journeys it: an admitted agent on another machine works an estate's
/// MCP tools over Streamable HTTP, through the bridge a stdio client uses, each call judged as its API operation for its device, its
/// records signed by that device alone, and nothing wider than section 8 offered. The local-network profile listens on 127.0.0.1;
/// the agent's machine is the harness's second home.
/// </summary>
[Collection("daemon")]
public class NetworkMcpJourneyTests
{
    internal static readonly string[] Writes = ["work_attempt", "work_progress", "work_result", "work_amend", "document_review"];

    internal static readonly string[] Hidden = ["work_review", "policy_declare", "policy_revise", "writing_exception_add", "writing_exception_remove", "prompt_use", "prompt_save", "prompt_copy", "prompt_preference"];

    [Fact]
    public async Task AnAgentElsewhereWorksItsAssignmentThroughTheBridgeItsRecordsSignedByItsDevice()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string agent = harness.Estate.BuilderId;
        RuntimeConnection paired = harness.Pair(agent, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        using HttpClient http = VericairnClient.SignedHttp(paired, signer);
        using var bridge = new McpBridge(http, paired.Principal, digest => signer.Sign(digest));
        JsonObject attempt;
        await using (var client = new BridgeDriver(bridge))
        {
            JsonObject initialized = await client.RequestAsync(McpWire.Request("initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "harness", ["version"] = "1" },
            }, 1));
            Assert.Equal("2025-06-18", initialized["result"]!["protocolVersion"]!.GetValue<string>());
            Assert.Contains("two calls", initialized["result"]!["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.NotNull(bridge.Session);
            await client.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });

            // Section 8's set: every read and the contributor writes, and none of the operator's judgments or policy writes.
            JsonObject listed = await client.RequestAsync(McpWire.Request("tools/list", id: 2));
            List<string> names = [.. listed["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>())];
            Assert.Contains("estate_status", names);
            Assert.Contains("work_brief", names);
            Assert.All(Writes, write => Assert.Contains(write, names));
            Assert.All(Hidden, hidden => Assert.DoesNotContain(hidden, names));
            Assert.True(listed["result"]!["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "work_attempt")!["_meta"]!["vericairn/prepared"]!.GetValue<bool>());

            JsonObject brief = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = "work_brief", ["arguments"] = new JsonObject { ["workOrder"] = "W-1" } }, 3));
            Assert.False(brief["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("Build the widget", McpWire.Text(brief["result"]!.AsObject()), StringComparison.Ordinal);

            // A write: the bridge prepares it, checks the prepared record, signs it with the device's key and repeats the call.
            attempt = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject
            {
                ["name"] = "work_attempt", ["arguments"] = NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository, branch work/widget"), ("note", "Build it.")),
            }, 4));
            WriteOutcome attempted = McpWire.Outcome(attempt["result"]!.AsObject());
            Assert.True(attempted.Ok, attempted.Problem?.Message);
            string attemptId = attempted.Written!.Id;

            JsonObject active = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject
            {
                ["name"] = "work_progress", ["arguments"] = NetworkMcp.Args(("attempt", attemptId), ("phase", "build"), ("note", "Compiling."), ("state", "active")),
            }, 5));
            Assert.False(active["result"]!["isError"]!.GetValue<bool>(), McpWire.Text(active["result"]!.AsObject()));
            JsonObject completed = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject
            {
                ["name"] = "work_progress", ["arguments"] = NetworkMcp.Args(("attempt", attemptId), ("phase", "build"), ("note", "It compiles."), ("state", "completed")),
            }, 6));
            Assert.False(completed["result"]!["isError"]!.GetValue<bool>(), McpWire.Text(completed["result"]!.AsObject()));
            JsonObject result = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject
            {
                ["name"] = "work_result", ["arguments"] = NetworkMcp.Args(("attempt", attemptId), ("body", NetworkMcp.ResultBody())),
            }, 7));
            WriteOutcome resulted = McpWire.Outcome(result["result"]!.AsObject());
            Assert.True(resulted.Ok, resulted.Problem?.Message);

            // An operator's judgment is unknown to the device, naming no hidden tool.
            JsonObject review = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = "work_review", ["arguments"] = new JsonObject() }, 8));
            Assert.Equal(-32602, review["error"]!["code"]!.GetValue<int>());
            JsonObject prompt = await client.RequestAsync(McpWire.Request("prompts/get", new JsonObject { ["name"] = "anything" }, 9));
            Assert.Equal(-32601, prompt["error"]!["code"]!.GetValue<int>());
        }

        // The bridge ended its session when its input ended.
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);

        // Every record the agent wrote carries its paired device's signature alone; no key on the runtime's machine signed.
        Records.Estate.EstateModel model = harness.Estate.Load();
        List<RecordEnvelope> written = [.. model.Records.Where(r => r.By == agent && r.Type is "attempt" or "work-progress" or "result")];
        Assert.Equal(3, written.Count);
        Assert.All(written, record => Assert.Equal(paired.Device, Assert.Single(record.Signatures).Device));
        Assert.Equal(0, harness.Host.Service.Check().Errors);

        // Each write is kept in the local-network ledger with the device that asked; preparing wrote nothing and kept nothing.
        IReadOnlyList<LocalNetworkWrite> ledger = LocalNetworkLedger.Read(directory);
        Assert.Equal(["POST /v1/work/attempt", "POST /v1/work/progress", "POST /v1/work/progress", "POST /v1/work/result"], ledger.Select(w => w.Operation));
        Assert.All(ledger, w => Assert.Equal((paired.Device, agent, "written"), (w.Device, w.Principal, w.State)));
        Assert.Equal(written.Select(r => r.Id).Order(StringComparer.Ordinal), ledger.Where(w => w.Record is not null).Select(w => w.Record!).Order(StringComparer.Ordinal));
    }
}
