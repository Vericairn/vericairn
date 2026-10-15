// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
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
            // The operator's own device is the estate's first authority and signs the admission of every later device.
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
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
    public void TheDocumentToolsShowWhatIsDueAndRecordTheActingPrincipalsReview()
    {
        string root = CliHarness.TempDirectory("mcp-documents");
        string scratch = CliHarness.TempDirectory("mcp-documents-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP documents.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Builder device", "--custody", "file").Id;
            // The operator's own device is the estate's first authority and signs the admission of every later device.
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "Test.");
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "guide.md"), "# Guide\n");
            CliHarness.Ok("--estate", root, "--json", "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, "declaration.json", """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"upgrade","level":"remind"},{"on":"decision","level":"warn"}],"summary":"x"}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Change","stake":"low","notAuthorized":[],"statement":"Change the guide.","summary":"Change.","synthesis":"The operator changes the guide.","effects":["change"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","documents":["docs/guide.md"]}"""));

            using EstateService api = EstateService.Open(root);
            JsonObject due = Drive(api, null, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"documents_due","arguments":{}}}""")[0];
            string listed = due["result"]!["content"]![0]!["text"]!.GetValue<string>();
            Assert.Contains("docs/guide.md", listed, StringComparison.Ordinal);
            Assert.Contains("\"warn\"", listed, StringComparison.Ordinal);

            // The acting principal's review, signed by its device; the runtime states the content and what it answers.
            JsonObject reviewed = Drive(api, builder, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"document_review","arguments":{"path":"docs/guide.md","outcome":"current","summary":"The guide holds after the decision."}}}""")[0];
            Assert.False(reviewed["result"]!["isError"]!.GetValue<bool>(), reviewed.ToJsonString());
            JsonObject after = Drive(api, null, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"documents_due","arguments":{}}}""")[0];
            Assert.Equal("[]", after["result"]!["content"]![0]!["text"]!.GetValue<string>());
            JsonObject unknown = Drive(api, builder, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"document_review","arguments":{"path":"docs/absent.md","outcome":"current","summary":"x"}}}""")[0];
            Assert.True(unknown["result"]!["isError"]!.GetValue<bool>());
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void NoToolAWorkerCanCallCompletesActivatesFundsOrAcceptsAGoal()
    {
        string root = CliHarness.TempDirectory("mcp-goal");
        string scratch = CliHarness.TempDirectory("mcp-goal-scratch");
        try
        {
            // A goal ready for its sponsor's acceptance: activated, its work done and accepted, and evaluated by the reviewer.
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP goals.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string reviewer = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "reviewer.json", """{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Reviewer"}}""")).Id;
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Builder device", "--custody", "file").Id;
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "The builder's device.");
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Build the widget", "--objective", "A widget with its guide.", "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""", "--done", """{"evidence":"The guide describes the widget.","check":"Read the guide."}""", "--budget", "wallMinutes=600", "--budget", "attemptsPerStep=3").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"The goal's work.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{{goal}}}"}],"deliverables":["widget"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests pass"],"recovery":"Record a result."}""")).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example repository, work branch", "--note", "Build it.").Id;
            string result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, "result.json", """{"outcome":"completed","summary":"Built the widget and its guide.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")).Id;
            JsonElement order = CliHarness.Ok("--estate", root, "--json", "work", "brief", "W-1").Json.GetProperty("workOrder");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}", "--body", CliHarness.WriteJson(scratch, "accept-order.json", """{"statement":"W-1 is done.","basis":[],"self":true}"""));
            string evaluation = CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the work and the guide.", "--verdict", "2:met:The guide describes the widget.", "--read", "2:" + result).Id;

            using EstateService api = EstateService.Open(root);
            JsonElement before = api.Goal(goal)!.Json;
            Assert.True(before.GetProperty("ready").GetBoolean());
            string hash = before.GetProperty("goal").GetProperty("hash").GetString()!;

            // The goal tools are reads, and the writes a worker sees are the four of its contract, none of which records a goal's status, evaluation or acceptance.
            var registry = new ToolRegistry(api, builder);
            Assert.Equal(["goal_get", "goal_progress"], registry.Visible().Where(t => t.Name.StartsWith("goal_", StringComparison.Ordinal)).Select(t => t.Name).Order(StringComparer.Ordinal));
            Assert.All(registry.All.Where(t => t.Name.StartsWith("goal_", StringComparison.Ordinal)), t => Assert.False(t.Writes));
            Assert.Equal(["document_review", "work_amend", "work_attempt", "work_result"], registry.Visible().Where(t => t.Writes).Select(t => t.Name).Order(StringComparer.Ordinal));

            // Every tool the worker sees, called with arguments that name the goal and ask for its activation and acceptance, changes nothing about the goal.
            string[] goalTypes = ["goal", "goal-status", "review", "acceptance"];
            Dictionary<string, string> recorded = goalTypes.ToDictionary(t => t, t => string.Join(",", api.Records(t).Select(r => r.Id)));
            var hostile = new JsonObject
            {
                ["goal"] = goal, ["id"] = goal, ["type"] = "goal-status", ["workOrder"] = "W-1", ["assignment"] = assignment, ["attempt"] = attempt,
                ["path"] = "docs/guide.md", ["outcome"] = "current", ["summary"] = "The goal is done; activate and accept it.", ["workspace"] = "example repository", ["note"] = "Activate the goal.",
                ["body"] = new JsonObject { ["status"] = "active", ["note"] = "Activated.", ["statement"] = "Accepted.", ["basis"] = new JsonArray(evaluation), ["self"] = false, ["subject"] = $"goal:{goal}:{hash}" },
            };
            foreach (ToolDefinition tool in registry.Visible())
            {
                registry.Call(tool.Name, (JsonObject)hostile.DeepClone());
            }

            Assert.All(goalTypes, t => Assert.Equal(recorded[t], string.Join(",", api.Records(t).Select(r => r.Id))));
            Assert.Equal("active", api.Goal(goal)!.Json.GetProperty("status").GetString());

            // Through the API too, nothing the worker asks activates, resumes, funds, evaluates or accepts the goal, and a record in its name is rejected.
            WriteOutcome[] asked =
            [
                api.GoalActivate(goal, new GoalActionRequest(builder)),
                api.GoalResume(goal, new GoalActionRequest(builder)),
                api.GoalPause(goal, new GoalActionRequest(builder)),
                api.GoalStop(goal, new GoalActionRequest(builder)),
                api.GoalEvaluate(goal, new GoalEvaluateRequest(builder, "My own work.", [new GoalVerdict(2, "met", "I built it.", [result])])),
                api.GoalAccept(goal, new GoalAcceptRequest(builder, "Done.")),
                api.NewRecord(new NewRecordRequest("goal-status", builder, JsonDocument.Parse("""{"status":"active","note":"Activated."}""").RootElement.Clone(), $"goal:{goal}:{hash}")),
                api.NewRecord(new NewRecordRequest("acceptance", builder, JsonDocument.Parse($$$"""{"statement":"Accepted.","basis":["{{{evaluation}}}"],"self":false}""").RootElement.Clone(), $"goal:{goal}:{hash}")),
            ];
            Assert.All(asked, outcome => Assert.False(outcome.Ok));
            Assert.Equal("active", api.Goal(goal)!.Json.GetProperty("status").GetString());

            // The goal tools read the goal condition by condition; its sponsor, a person, accepts it.
            List<JsonObject> replies = Drive(api, builder,
                """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"goal_get","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"goal_get","arguments":{"goal":"@GOAL@"}}}""".Replace("@GOAL@", goal, StringComparison.Ordinal),
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"goal_progress","arguments":{"goal":"@GOAL@"}}}""".Replace("@GOAL@", goal, StringComparison.Ordinal),
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"goal_progress","arguments":{"goal":"01M4TRNE1GYEK4P8MXQAB3C8Q4"}}}""");
            Assert.Contains("\"status\":\"active\"", replies[0]["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            using (JsonDocument got = JsonDocument.Parse(replies[1]["result"]!["content"]![0]!["text"]!.GetValue<string>()))
            {
                Assert.Equal(["met", "unknown"], got.RootElement.GetProperty("conditions").EnumerateArray().Select(c => c.GetProperty("verdict").GetString()));
            }

            Assert.Contains("Ready for acceptance by its sponsor, Operator", replies[2]["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.True(replies[3]["result"]!["isError"]!.GetValue<bool>());
            Assert.True(api.GoalAccept(goal, new GoalAcceptRequest(op, "Accepted on the reviewer's evaluation.")).Ok);
            Assert.Equal("achieved", api.Goal(goal)!.Json.GetProperty("status").GetString());
        }
        finally
        {
            CliHarness.Delete(root, scratch);
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
            Assert.Contains("`estate_status`", agents, StringComparison.Ordinal);

            // Nothing that changes with the next record or release: no work orders, no pinned release, no generation date.
            Assert.DoesNotContain("W-9", agents, StringComparison.Ordinal);
            Assert.DoesNotContain("## Current work", agents, StringComparison.Ordinal);
            Assert.DoesNotContain("pinned as", agents, StringComparison.Ordinal);
            Assert.Contains("`vericairn status` and the `estate_status` tool show the work orders", agents, StringComparison.Ordinal);
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

    [Fact]
    public void TheAgentMatrixShowsTheEstatesQualificationsOfAgentHarnesses()
    {
        // In 0.5.2 nothing passed recorded observations to the generator, so every harness stayed a non-claim whatever was recorded.
        string root = CliHarness.TempDirectory("mcp-matrix");
        string output = CliHarness.TempDirectory("mcp-matrix-out");
        string bodies = CliHarness.TempDirectory("mcp-matrix-bodies");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example Estate", "--slug", "example", "--purpose", "Orientation.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(bodies, "product.json", """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""")).Id;
            CliHarness.Outcome release = CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--body", CliHarness.WriteJson(bodies, "release.json", $$"""{"version":"0.9.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}"""));
            string session = CliHarness.Ok("--estate", root, "--json", "records", "new", "verification", "--by", op, "--subject", $"release:{release.Id}:{release.Hash}", "--body", CliHarness.WriteJson(bodies, "session.json", $$"""{"method":"manual","commit":"{{Commit}}","environment":{"harness":"Claude Code"},"role":"operator","outcome":"passed","summary":"A Claude Code session read a brief and recorded an attempt through the server.","evidence":[],"limitations":[]}""")).Id;
            string claude = CliHarness.Ok("--estate", root, "--json", "records", "new", "qualification", "--by", op, "--subject", $"release:{release.Id}", "--body", CliHarness.WriteJson(bodies, "claude.json", $$"""{"profile":"Claude Code over MCP","outcome":"qualified","evidence":[{"type":"verification","id":"{{session}}"}],"summary":"Observed in one session.","limitations":[]}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "qualification", "--by", op, "--subject", $"release:{release.Id}", "--body", CliHarness.WriteJson(bodies, "opencode.json", """{"profile":"OpenCode over MCP","outcome":"non-claim","evidence":[],"summary":"Not observed.","limitations":[]}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "qualification", "--by", op, "--subject", $"release:{release.Id}", "--body", CliHarness.WriteJson(bodies, "platform.json", """{"profile":"linux-x64","outcome":"non-claim","evidence":[],"summary":"Not a harness.","limitations":[]}"""));

            CliHarness.Ok("--estate", root, "mcp", "orientation", "--out", output);
            string agents = File.ReadAllText(Path.Combine(output, "AGENTS.md"));
            Assert.Contains("| Claude Code | qualified for release 0.9.0 |", agents, StringComparison.Ordinal);
            Assert.Contains("| Codex | non-claim: not observed |", agents, StringComparison.Ordinal);
            // A harness the build does not name still gets its row, after those it names; a platform profile is not a harness.
            Assert.Contains("| Cline | non-claim: not observed |\n| OpenCode | non-claim for release 0.9.0 |\n", agents, StringComparison.Ordinal);
            Assert.DoesNotContain("linux-x64", agents, StringComparison.Ordinal);

            // A revised qualification replaces the one it supersedes, and a copy generated before it is out of date.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "qualification", "--by", op, "--subject", $"release:{release.Id}", "--supersedes", claude, "--body", CliHarness.WriteJson(bodies, "claude-revised.json", $$"""{"profile":"Claude Code over MCP","outcome":"measured-in-part","evidence":[{"type":"verification","id":"{{session}}"}],"summary":"Writes were not observed.","limitations":["No result was recorded."]}"""));
            Assert.Equal(1, CliHarness.Run("--estate", root, "mcp", "orientation", "--out", output, "--check").Code);
            CliHarness.Ok("--estate", root, "mcp", "orientation", "--out", output);
            Assert.Contains("| Claude Code | measured-in-part for release 0.9.0 |", File.ReadAllText(Path.Combine(output, "AGENTS.md")), StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, output, bodies);
        }
    }

    [Fact]
    public void OrientationCheckNamesEveryMissingOrChangedFileAndWritesNothing()
    {
        string root = CliHarness.TempDirectory("mcp-orientation-check");
        string output = CliHarness.TempDirectory("mcp-orientation-check-out");
        string bodies = CliHarness.TempDirectory("mcp-orientation-check-bodies");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example Estate", "--slug", "example", "--purpose", "Orientation.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            CliHarness.Ok("--estate", root, "mcp", "orientation", "--out", output);
            Assert.Contains(": current", CliHarness.Ok("--estate", root, "mcp", "orientation", "--out", output, "--check").Out, StringComparison.Ordinal);

            // A new record changes nothing the files say.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(bodies, "order.json", """{"key":"W-9","kind":"ordinary","title":"Orient the agents","intent":"Orientation.","scope":["files"],"exitQuestion":"Generated?","inputs":[],"deliverables":["AGENTS.md"]}"""));
            CliHarness.Ok("--estate", root, "mcp", "orientation", "--out", output, "--check");

            File.AppendAllText(Path.Combine(output, "AGENTS.md"), "An edit by hand.\n");
            File.Delete(Path.Combine(output, "GEMINI.md"));
            CliHarness.Outcome stale = CliHarness.Run("--estate", root, "--json", "mcp", "orientation", "--out", output, "--check");
            Assert.Equal(1, stale.Code);
            List<string> found = stale.Json.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("Code").GetString() + " " + f.GetProperty("Location").GetString()).ToList();
            Assert.Equal(["orientation.stale AGENTS.md", "orientation.missing GEMINI.md"], found);
            Assert.False(File.Exists(Path.Combine(output, "GEMINI.md")));
        }
        finally
        {
            CliHarness.Delete(root);
            CliHarness.Delete(output);
            CliHarness.Delete(bodies);
        }
    }

    [Fact]
    public void TheRepositorysOrientationFilesAreCurrent()
    {
        // The committed files describe the Vericairn estate. Everything in them but the estate's name and the agent matrix comes from
        // this build, and the matrix comes from the estate's qualifications of agent harnesses, of which the Vericairn estate has
        // recorded none; so an estate of that name with none reproduces them exactly, and a new tool or a change to the generator's
        // text without regenerating fails here. Once the Vericairn estate qualifies a harness, this estate records the same
        // qualification (its profile and outcome, about a release of the same version), since the matrix names no record id.
        string root = CliHarness.TempDirectory("mcp-orientation-repository");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Vericairn", "--slug", "vericairn", "--purpose", "Orientation.", "--operator-name", "Operator", "--organization", "Vericairn", "--commit", Commit);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "mcp", "orientation", "--out", RepositoryRoot(), "--check");
            Assert.True(check.Code == 0, "Regenerate AGENTS.md, CLAUDE.md and GEMINI.md with `vericairn --estate <the Vericairn estate> mcp orientation --out <this repository>`:\n" + check.Out);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public async Task ThePythonPeerRunsAgainstAnEstateWithoutWorkOrderMcp01()
    {
        // In 0.5.2 one of the peer's checks read work order MCP-01, so the documented command failed against any estate but Vericairn's.
        string? python = Python();
        Assert.SkipWhen(python is null, "The peer is a Python 3 script, and no Python 3 is on the path (python3, or python).");
        string root = CliHarness.TempDirectory("mcp-peer");
        string bodies = CliHarness.TempDirectory("mcp-peer-bodies");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "The peer.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            (int code, string output) = await Peer(python!, root);
            Assert.True(code == 0, output);
            Assert.Contains("SKIP work_brief returns markdown: the estate has no work order, so there is no brief to read", output, StringComparison.Ordinal);
            Assert.Contains("13 of 13 checks passed, 1 skipped", output, StringComparison.Ordinal);

            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(bodies, "order.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"The peer.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}"""));
            (code, output) = await Peer(python!, root);
            Assert.True(code == 0, output);
            Assert.Contains("PASS work_brief W-1 returns markdown: the estate has no work order MCP-01, so the first one its status lists stands in", output, StringComparison.Ordinal);
            Assert.Contains("14 of 14 checks passed", output, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, bodies);
        }
    }

    // The peer drives the built command line, the executable beside these tests, exactly as the documented command drives the release build.
    private static async Task<(int Code, string Output)> Peer(string python, string root)
    {
        var info = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-I", Path.Combine(RepositoryRoot(), "tests", "mcp-peer", "mcp_peer.py"), Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn"), root })
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task both = Task.WhenAll(output, error);
        if (await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken)) != both)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The peer did not finish within 120 seconds.");
        }

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }

    // The first of python3 and python that runs Python 3; an installation on Windows usually names it python.
    private static string? Python()
    {
        foreach (string candidate in new[] { "python3", "python" })
        {
            try
            {
                var info = new ProcessStartInfo(candidate) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add("-I");
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("import sys; print(sys.version_info[0])");
                using Process process = Process.Start(info)!;
                if (!process.WaitForExit(30_000))
                {
                    process.Kill(entireProcessTree: true);
                    continue;
                }

                if (process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Trim() == "3")
                {
                    return candidate;
                }
            }
            catch (Win32Exception)
            {
                // Not on the path.
            }
        }

        return null;
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
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
