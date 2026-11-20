// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (GOAL-07): a standing agent from the command line. goal propose --standing previews what would be due, goal due lists its
/// occurrences at an explicit time, goal step carries one occurrence per command (here a person's, assigned and asked) and a request on
/// demand, a time ahead of the clock is refused, status shows the agent, and the MCP read tool reads the same view.
/// </summary>
[Collection("cli")]
public class StandingAgentJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void AStandingAgentIsProposedAndCarriesOneOccurrencePerGoalStep()
    {
        string root = CliHarness.TempDirectory("standing");
        string scratch = CliHarness.TempDirectory("standing-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Standing agents.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Directory.CreateDirectory(Path.Combine(root, "docs", "templates"));
            File.WriteAllText(Path.Combine(root, "docs", "templates", "example-researcher-1.0.0.json"), StandingFlows.Researcher().ToJsonString());
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"researcher","principals":["{{op}}"]}]}"""));
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json",
                """{"key":"RES-1","kind":"ordinary","title":"Research the widget","intent":"Keep researching the widget.","scope":["path:research/"],"exitQuestion":"Is each occurrence researched?","inputs":[],"deliverables":["findings"]}""")).Id;
            string firstAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds()));
            string daily = $$"""{"key":"daily","kind":"schedule","unit":"days","every":1,"firstAt":"{{firstAt}}"}""";
            string[] propose = ["goal", "propose", "--by", op, "--title", "Research the widget", "--objective", "Keep researching the widget.", "--done", """{"type":"work-order","fields":{"key":"RESEARCHED"}}""",
                "--work-order", order, "--budget", "wallMinutes=30", "--budget", "attemptsPerStep=2"];

            // Standing options go with --standing, a standing agent is a stewardship goal, and its caps state every unit the budget caps.
            Refused(root, "configure a standing agent, and go with --standing", [.. propose, "--trigger", daily]);
            Refused(root, "give --kind stewardship with --standing", [.. propose, "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget", "--trigger", daily]);
            Refused(root, "at least one --trigger", [.. propose, "--kind", "stewardship", "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget"]);
            Refused(root, "wallMinutes", [.. propose, "--kind", "stewardship", "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget", "--trigger", daily, "--period", "runs=4"]);

            CliHarness.Outcome proposed = CliHarness.Ok(["--estate", root, .. propose, "--kind", "stewardship", "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget", "--trigger", daily,
                "--trigger", """{"key":"asked","kind":"request"}""", "--period", "runs=4", "--period", "wallMinutes=120"]);
            Assert.Contains("Standing agent Research the widget", proposed.Out, StringComparison.Ordinal);
            Assert.Contains("State: proposed", proposed.Out, StringComparison.Ordinal);
            string goal = proposed.Out.Split('\n')[0].Split("  ")[1];
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);

            // goal due: the latest daily tick is due; before the first tick nothing is.
            JsonElement due = CliHarness.Ok("--estate", root, "--json", "goal", "due", goal).Json;
            Assert.Equal(("vericairn.due/1", "active"), (due.GetProperty("format").GetString(), due.GetProperty("state").GetString()));
            string key = Assert.Single(due.GetProperty("occurrences").EnumerateArray()).GetProperty("key").GetString()!;
            Assert.EndsWith("/interval-3", key, StringComparison.Ordinal);
            Assert.Contains("Occurrences", CliHarness.Ok("--estate", root, "goal", "due", goal).Out, StringComparison.Ordinal);
            Assert.Empty(CliHarness.Ok("--estate", root, "--json", "goal", "due", goal, "--as-of", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(-4))).Json.GetProperty("occurrences").EnumerateArray());

            // goal step: the researcher is a person here, so the occurrence is assigned and asked; its assignment names the key.
            JsonElement first = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op, "--occurrence", key).Json.GetProperty("step");
            Assert.Equal(("ask", key, "person"), (first.GetProperty("outcome").GetString(), first.GetProperty("step").GetString(), first.GetProperty("route").GetString()));
            string assignment = first.GetProperty("assignment").GetProperty("id").GetString()!;
            Assert.Equal(key, CliHarness.Ok("--estate", root, "--json", "records", "show", assignment).Json.GetProperty("body").GetProperty("occurrence").GetProperty("key").GetString());
            Assert.Contains("Nothing to dispatch", CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op).Out, StringComparison.Ordinal);

            // A request is an occurrence of its own; a time ahead of the clock is refused.
            JsonElement requested = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op, "--occurrence", "request").Json.GetProperty("step");
            Assert.Equal("ask", requested.GetProperty("outcome").GetString());
            Assert.Contains("/asked/", requested.GetProperty("step").GetString(), StringComparison.Ordinal);
            CliHarness.Outcome ahead = CliHarness.Run("--estate", root, "goal", "step", goal, "--by", op, "--now", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(2)));
            Assert.NotEqual(0, ahead.Code);
            Assert.Contains("later than this runtime's clock", ahead.Err, StringComparison.Ordinal);

            // status shows the agent; the MCP read tool reads goal due's view.
            Assert.Contains("Standing agents: 1", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // goal pause --standing pauses every active standing agent the caller may pause, and names it; one goal or --standing, never both.
            Refused(root, "name no goal with it", ["goal", "pause", goal, "--standing", "--by", op]);
            Refused(root, "Name the goal to pause", ["goal", "pause", "--by", op]);
            CliHarness.Outcome paused = CliHarness.Ok("--estate", root, "goal", "pause", "--standing", "--stop-runs", "--by", op);
            Assert.Contains($"paused  {goal}  Research the widget", paused.Out, StringComparison.Ordinal);
            Assert.Contains("No run of the goal was in flight.", paused.Out, StringComparison.Ordinal);
            Assert.Equal("paused", CliHarness.Ok("--estate", root, "--json", "goal", "due", goal).Json.GetProperty("state").GetString());
            Assert.Contains("No standing agent is active.", CliHarness.Ok("--estate", root, "goal", "pause", "--standing", "--by", op).Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "goal", "resume", goal, "--by", op);
            using EstateService api = EstateService.Open(root);
            var registry = new ToolRegistry(api, null);
            ToolCallResult text = registry.Call("goal_due", new JsonObject { ["goal"] = goal });
            Assert.False(text.IsError);
            Assert.Contains("Standing agent Research the widget", text.Text, StringComparison.Ordinal);
            using JsonDocument json = JsonDocument.Parse(registry.Call("goal_due", new JsonObject { ["goal"] = goal, ["form"] = "json" }).Text);
            Assert.Equal(2, json.RootElement.GetProperty("occurrences").GetArrayLength());
            Assert.True(registry.Call("goal_due", new JsonObject { ["goal"] = goal, ["form"] = "yaml" }).IsError);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AStandingOutputIsRecordedAndItsAuthorCannotAdoptItWithWorkNew()
    {
        string root = CliHarness.TempDirectory("standing-outputs");
        string scratch = CliHarness.TempDirectory("standing-outputs-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Standing outputs.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Directory.CreateDirectory(Path.Combine(root, "docs", "templates"));
            File.WriteAllText(Path.Combine(root, "docs", "templates", "example-researcher-1.0.0.json"), StandingFlows.Researcher().ToJsonString());
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"researcher","principals":["{{op}}"]}]}"""));
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json",
                """{"key":"RES-1","kind":"ordinary","title":"Research the widget","intent":"Keep researching the widget.","scope":["path:research/"],"exitQuestion":"Is each occurrence researched?","inputs":[],"deliverables":["findings"]}""")).Id;
            string[] propose = ["goal", "propose", "--by", op, "--title", "Research the widget", "--objective", "Keep researching the widget.", "--done", """{"type":"work-order","fields":{"key":"RESEARCHED"}}""",
                "--work-order", order, "--budget", "wallMinutes=30", "--budget", "attemptsPerStep=2", "--kind", "stewardship", "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget",
                "--trigger", """{"key":"asked","kind":"request"}""", "--period", "runs=4", "--period", "wallMinutes=120"];

            // --outputs caps what each occurrence records and the open outputs; a malformed cap is refused.
            Refused(root, "An output cap is possibilities, drafts or open", [.. propose, "--outputs", "scores=3"]);
            CliHarness.Outcome proposed = CliHarness.Ok(["--estate", root, .. propose, "--outputs", "possibilities=2", "--outputs", "drafts=1", "--outputs", "open=4"]);
            string goal = proposed.Out.Split('\n')[0].Split("  ")[1];
            JsonElement outputs = CliHarness.Ok("--estate", root, "--json", "records", "show", goal).Json.GetProperty("body").GetProperty("plan").GetProperty("standing").GetProperty("outputs");
            Assert.Equal((2, 1, 4), (outputs.GetProperty("possibilities").GetInt32(), outputs.GetProperty("drafts").GetInt32(), outputs.GetProperty("open").GetInt32()));
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string assignment = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op, "--occurrence", "request").Json.GetProperty("step").GetProperty("assignment").GetProperty("id").GetString()!;

            // The operator carries the occurrence as a person here, and records a possibility with a draft through the API the MCP tool calls.
            string possibility;
            using (EstateService api = EstateService.Open(root))
            {
                var body = new JsonObject
                {
                    ["title"] = "Cache the index", ["idea"] = "Reading the index on every call costs a second.", ["fingerprint"] = "cache/index",
                    ["sources"] = new JsonArray(new JsonObject { ["address"] = "https://example.org/widget/index" }),
                    ["draft"] = new JsonObject { ["title"] = "Cache the index", ["intent"] = "Stop reading it on every call.", ["scope"] = new JsonArray("path:src/index/"), ["deliverables"] = new JsonArray("a cached index"), ["exitQuestion"] = "Is the index read once per change?" },
                };
                Vericairn.Protocol.WriteOutcome raised = api.StandingOutput(new Vericairn.Protocol.StandingOutputRequest(op, assignment, "possibility", JsonSerializer.SerializeToElement(body)));
                Assert.True(raised.Ok, raised.Problem?.Message);
                possibility = raised.Written!.Id;
            }

            // Its author does not adopt it: work new --from-possibility refuses, and nothing is written.
            CliHarness.Outcome own = CliHarness.Run("--estate", root, "work", "new", "--by", op, "--from-possibility", possibility, "--key", "IDX-1");
            Assert.NotEqual(0, own.Code);
            Assert.Contains("standing.own-output", own.Err + own.Out, StringComparison.Ordinal);
            Assert.Equal("open", CliHarness.Ok("--estate", root, "--json", "records", "show", possibility).Json.GetProperty("body").GetProperty("disposition").GetString());
            Assert.Contains("1 outputs open", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static void Refused(string root, string expected, string[] arguments)
    {
        CliHarness.Outcome refused = CliHarness.Run(["--estate", root, .. arguments]);
        Assert.NotEqual(0, refused.Code);
        Assert.Contains(expected, refused.Err + refused.Out, StringComparison.Ordinal);
    }
}
