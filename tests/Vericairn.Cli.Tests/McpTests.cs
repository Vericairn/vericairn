// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>The MCP server: the protocol subset over streams, reads for every connection, writes only for an admitted device, descriptions as data.</summary>
[Collection("cli")]
public class McpTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void SpeaksTheProtocolSubsetAndListsOnlyReadsWithoutADevice()
    {
        string root = CliHarness.TempDirectory("mcp-read");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP pilot.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            using EstateService api = EstateService.Open(root);
            List<JsonObject> replies = Drive(api, null,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"ping"}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""",
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"estate_status","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"work_brief","arguments":{"workOrder":"NOPE"}}}""",
                """{"jsonrpc":"2.0","id":6,"method":"nothing/here"}""",
                """this is not json""",
                """{"jsonrpc":"2.0","id":"seven","method":"tools/call","params":{"name":"work_attempt","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":8,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

            Assert.Equal(9, replies.Count);
            JsonObject initialized = replies[0];
            Assert.Equal("2025-06-18", initialized["result"]!["protocolVersion"]!.GetValue<string>());
            Assert.Equal("vericairn", initialized["result"]!["serverInfo"]!["name"]!.GetValue<string>());
            Assert.DoesNotContain("you must", initialized["result"]!["instructions"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(replies[1]["result"]!.AsObject());

            List<string> names = replies[2]["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
            Assert.Contains("estate_status", names);
            Assert.Contains("work_brief", names);
            Assert.DoesNotContain("work_attempt", names);
            foreach (JsonNode? tool in replies[2]["result"]!["tools"]!.AsArray())
            {
                string description = tool!["description"]!.GetValue<string>();
                Assert.DoesNotContain("you must", description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("always ", description, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("object", tool["inputSchema"]!["type"]!.GetValue<string>());
            }

            JsonObject status = replies[3];
            Assert.False(status["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("\"workOrders\"", status["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.True(replies[4]["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("No work order 'NOPE'", replies[4]["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal(-32601, replies[5]["error"]!["code"]!.GetValue<int>());
            Assert.Equal(-32700, replies[6]["error"]!["code"]!.GetValue<int>());
            Assert.Equal("seven", replies[7]["id"]!.GetValue<string>());
            Assert.Equal(-32602, replies[7]["error"]!["code"]!.GetValue<int>());
            Assert.Equal("2025-06-18", replies[8]["result"]!["protocolVersion"]!.GetValue<string>());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public void WritesAppearForAnAdmittedDeviceAndAreSigned()
    {
        string root = CliHarness.TempDirectory("mcp-write");
        string scratch = CliHarness.TempDirectory("mcp-write-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP writes.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Through MCP.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["tests pass"],"recovery":"Record a result before stopping."}""")).Id;

            using EstateService api = EstateService.Open(root);
            Assert.DoesNotContain("work_attempt", ToolNames(Drive(api, builder, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")[0]));

            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Builder device").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "Test.");

            List<JsonObject> replies = Drive(api, builder,
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"work_attempt","arguments":{"assignment":"@ASSIGNMENT@","workspace":"example repository, main","note":"Start."}}}""".Replace("@ASSIGNMENT@", assignment, StringComparison.Ordinal));
            Assert.Contains("work_attempt", ToolNames(replies[0]));
            Assert.Contains("work_result", ToolNames(replies[0]));
            JsonObject attempt = replies[1];
            Assert.False(attempt["result"]!["isError"]!.GetValue<bool>(), attempt.ToJsonString());
            using JsonDocument outcome = JsonDocument.Parse(attempt["result"]!["content"]![0]!["text"]!.GetValue<string>());
            string attemptId = outcome.RootElement.GetProperty("written").GetProperty("id").GetString()!;

            JsonObject record = Drive(api, null, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"record_get","arguments":{"id":"@ATTEMPT@"}}}""".Replace("@ATTEMPT@", attemptId, StringComparison.Ordinal))[0];
            using JsonDocument envelope = JsonDocument.Parse(record["result"]!["content"]![0]!["text"]!.GetValue<string>());
            JsonElement signatures = envelope.RootElement.GetProperty("envelope").GetProperty("signatures");
            Assert.Equal(1, signatures.GetArrayLength());
            Assert.Equal(device, signatures[0].GetProperty("device").GetString());

            JsonObject resultReply = Drive(api, builder, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"work_result","arguments":{"attempt":"@ATTEMPT@","body":{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}}}}""".Replace("@ATTEMPT@", attemptId, StringComparison.Ordinal))[0];
            Assert.False(resultReply["result"]!["isError"]!.GetValue<bool>(), resultReply.ToJsonString());
            Assert.Contains("next step: none", CliHarness.Ok("--estate", root, "work", "brief", "W-1").Out, StringComparison.Ordinal);

            // A refused write is an error result with the runtime's findings, never an exception or a silent success.
            JsonObject refused = Drive(api, builder, """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"work_result","arguments":{"attempt":"@ATTEMPT@","body":{"outcome":"completed"}}}}""".Replace("@ATTEMPT@", attemptId, StringComparison.Ordinal))[0];
            Assert.True(refused["result"]!["isError"]!.GetValue<bool>());
        }
        finally
        {
            CliHarness.Delete(root);
            CliHarness.Delete(scratch);
        }
    }

    [Fact]
    public void OrientationFilesComeFromTheRecordsAndStateNonClaims()
    {
        string root = CliHarness.TempDirectory("mcp-orientation");
        string output = CliHarness.TempDirectory("mcp-orientation-out");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example Estate", "--slug", "example", "--purpose", "Orientation.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(output, "order.json", """{"key":"W-9","kind":"ordinary","title":"Orient the agents","intent":"Orientation.","scope":["files"],"exitQuestion":"Generated?","inputs":[],"deliverables":["AGENTS.md"]}"""));
            CliHarness.Ok("--estate", root, "--json", "mcp", "orientation", "--out", output);
            string agents = File.ReadAllText(Path.Combine(output, "AGENTS.md"));
            Assert.Contains("# Working in the Example Estate estate", agents, StringComparison.Ordinal);
            Assert.Contains("| W-9 |", agents, StringComparison.Ordinal);
            Assert.Contains("`estate_status`", agents, StringComparison.Ordinal);
            Assert.Contains("| Claude Code | non-claim: not observed |", agents, StringComparison.Ordinal);
            Assert.DoesNotContain(root, agents, StringComparison.Ordinal);
            Assert.Contains("Read `AGENTS.md`", File.ReadAllText(Path.Combine(output, "CLAUDE.md")), StringComparison.Ordinal);
            Assert.Contains("Read `AGENTS.md`", File.ReadAllText(Path.Combine(output, "GEMINI.md")), StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root);
            CliHarness.Delete(output);
        }
    }

    private static List<string> ToolNames(JsonObject listReply) => listReply["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();

    private static List<JsonObject> Drive(EstateService api, string? actingAs, params string[] messages)
    {
        var input = new StringReader(string.Join("\n", messages) + "\n");
        var output = new StringWriter { NewLine = "\n" };
        new McpServer(new ToolRegistry(api, actingAs), "test").Run(input, output);
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => (JsonObject)JsonNode.Parse(l)!).ToList();
    }
}
