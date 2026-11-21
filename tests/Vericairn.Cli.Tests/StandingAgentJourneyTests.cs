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

    [Fact]
    public void RecordsNewRefusesAStandingAgentsAnswerToItsOwnAskAndAnAuthorsAdoptionOfItsOwnPossibility()
    {
        string root = CliHarness.TempDirectory("standing-answers");
        string scratch = CliHarness.TempDirectory("standing-answers-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Standing answers.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Directory.CreateDirectory(Path.Combine(root, "docs", "templates"));
            File.WriteAllText(Path.Combine(root, "docs", "templates", "example-researcher-1.0.0.json"), StandingFlows.Researcher().ToJsonString());
            string pat = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "pat.json", """{"kind":"human","name":"Pat","roles":["contributor"]}""")).Id;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"researcher","principals":["{{pat}}"]}]}"""));
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json",
                """{"key":"RES-1","kind":"ordinary","title":"Research the widget","intent":"Keep researching the widget.","scope":["path:research/"],"exitQuestion":"Is each occurrence researched?","inputs":[],"deliverables":["findings"]}""")).Id;
            string goal = CliHarness.Ok("--estate", root, "goal", "propose", "--by", op, "--title", "Research the widget", "--objective", "Keep researching the widget.", "--done", """{"type":"work-order","fields":{"key":"RESEARCHED"}}""",
                "--work-order", order, "--budget", "wallMinutes=30", "--budget", "attemptsPerStep=2", "--kind", "stewardship", "--standing", "example/researcher@1.0.0", "--subject", "topic=the widget",
                "--trigger", """{"key":"asked","kind":"request"}""", "--period", "runs=4", "--period", "wallMinutes=120").Out.Split('\n')[0].Split("  ")[1];
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string assignment = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op, "--occurrence", "request").Json.GetProperty("step").GetProperty("assignment").GetProperty("id").GetString()!;

            // Pat carries the occurrence as a person and asks the operator, who decides for the goal, through the API the MCP tool calls.
            string ask;
            using (EstateService api = EstateService.Open(root))
            {
                Vericairn.Protocol.WriteOutcome asked = api.StandingOutput(new Vericairn.Protocol.StandingOutputRequest(pat, assignment, "ask", JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["question"] = "Is the legacy index still read?", ["recommendation"] = "Drop it if not.", ["consequence"] = "The research goes on.",
                })));
                Assert.True(asked.Ok, asked.Problem?.Message);
                ask = asked.Written!.Id;
            }

            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()));
            string answer = CliHarness.WriteJson(scratch, "answer.json", $$"""
                {"title":"Drop it","statement":"Drop the legacy index.","summary":"The legacy index goes.","synthesis":"The operator answers the standing agent's ask.","effects":["the legacy index is dropped"],"affects":[],"decidedBy":"{{op}}","decidedAt":"{{decidedAt}}","stake":"low","notAuthorized":["spending"],"answers":["{{ask}}"]}
                """);

            // records new refuses Pat's decision on its own ask, though it names the operator; the ask stays open and counted.
            CliHarness.Outcome own = CliHarness.Run("--estate", root, "records", "new", "decision", "--by", pat, "--body", answer);
            Assert.NotEqual(0, own.Code);
            Assert.Contains("standing.own-output", own.Err + own.Out, StringComparison.Ordinal);
            Assert.Equal(1, CliHarness.Ok("--estate", root, "--json", "goal", "due", goal).Json.GetProperty("outputs").GetProperty("open").GetInt32());

            // The builder, which neither raised the ask nor carried an occurrence, records the operator's decision as it always has.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", answer);
            Assert.Equal(0, CliHarness.Ok("--estate", root, "--json", "goal", "due", goal).Json.GetProperty("outputs").GetProperty("open").GetInt32());

            // A possibility's author does not adopt it by records new, whoever raised it: with a draft, an error of the record; without one, the
            // write boundary's refusal. The operator adopts each.
            string idea = CliHarness.Ok("--estate", root, "--json", "work", "new", "--by", op, "--key", "IDEA-1", "--title", "A books pack", "--intent", "Ship a books pack.", "--scope", "path:books/",
                "--deliverable", "a books pack", "--exit-question", "Is the books pack shipped?").Id;
            foreach ((string name, string draft, string code) in new[]
            {
                ("plain", string.Empty, "standing.output-now"),
                ("drafted", ""","draft":{"title":"A books pack","intent":"Ship a books pack.","scope":["path:books/"],"deliverables":["a books pack"],"exitQuestion":"Is the books pack shipped?"}""", "standing.own-output"),
            })
            {
                const string Idea = """{"title":"A books pack","idea":"Ship a books pack.","origin":"The builder's own note.",""";
                string possibility = CliHarness.Ok("--estate", root, "--json", "records", "new", "possibility", "--by", builder, "--body", CliHarness.WriteJson(scratch, name + ".json", Idea + "\"disposition\":\"open\"" + draft + "}")).Id;
                string adopting = CliHarness.WriteJson(scratch, name + "-adopted.json", Idea + $$"""
                    "disposition":"adopted","reason":"Planned.","adoptedAs":{"type":"work-order","id":"{{idea}}"}
                    """ + draft + "}");
                CliHarness.Outcome adopted = CliHarness.Run("--estate", root, "records", "new", "possibility", "--by", builder, "--supersedes", possibility, "--body", adopting);
                Assert.NotEqual(0, adopted.Code);
                Assert.Contains(code, adopted.Err + adopted.Out, StringComparison.Ordinal);
                CliHarness.Ok("--estate", root, "--json", "records", "new", "possibility", "--by", op, "--supersedes", possibility, "--body", adopting);
            }

            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void EachShippedStandingRoleFillsAStandingAgentWithoutCopyingItsFlow()
    {
        string root = CliHarness.TempDirectory("standing-roles");
        string scratch = CliHarness.TempDirectory("standing-roles-scratch");
        try
        {
            // From 0.9.4 (PROC-08) the duty carrier, the researcher and the bug hunter are built-in flows every estate lists.
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Standing roles.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string[] listed = [.. CliHarness.Ok("--estate", root, "--json", "templates", "list").Json.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("pin").GetProperty("id").GetString()!)];
            Assert.Contains("vericairn.estate/duty-carrier", listed);
            Assert.Contains("vericairn.software/researcher", listed);
            Assert.Contains("vericairn.software/bug-hunter", listed);
            Assert.Contains("never publish, post or reply outside the estate", CliHarness.Ok("--estate", root, "templates", "show", "vericairn.software/researcher", "--version", "1.0.0").Out, StringComparison.OrdinalIgnoreCase);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"researcher","principals":["{{op}}"]},{"role":"bug-hunter","principals":["{{op}}"]}]}"""));

            // A researcher and a bug hunter over the same subject are two agents of two roles; each previews what would be due and is activated.
            foreach ((string role, string key) in new[] { ("researcher", "RES-1"), ("bug-hunter", "BUG-1") })
            {
                string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, role + ".json",
                    $$"""{"key":"{{key}}","kind":"ordinary","title":"Keep the widget's {{role}} going","intent":"Standing work.","scope":["path:src/"],"exitQuestion":"Is each occurrence carried?","inputs":[],"deliverables":["outputs"]}""")).Id;
                CliHarness.Outcome proposed = CliHarness.Ok("--estate", root, "goal", "propose", "--by", op, "--title", "The widget's " + role, "--objective", "Keep it going.", "--done", """{"type":"work-order","fields":{"key":"DONE"}}""",
                    "--work-order", order, "--budget", "wallMinutes=30", "--budget", "attemptsPerStep=2", "--kind", "stewardship", "--standing", $"vericairn.software/{role}@1.0.0", "--subject", "topic=the widget",
                    "--trigger", """{"key":"asked","kind":"request"}""", "--period", "runs=4", "--period", "wallMinutes=120");
                Assert.Contains($"Flow: vericairn.software/{role} 1.0.0", proposed.Out, StringComparison.Ordinal);
                string goal = proposed.Out.Split('\n')[0].Split("  ")[1];
                CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
                JsonElement step = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op, "--occurrence", "request").Json.GetProperty("step");
                Assert.Equal(("ask", "person"), (step.GetProperty("outcome").GetString(), step.GetProperty("route").GetString()));
                string brief = CliHarness.Ok("--estate", root, "--json", "records", "show", step.GetProperty("assignment").GetProperty("id").GetString()!).Json.GetProperty("body").GetProperty("brief").ToString();
                Assert.Contains("authority each effect needs", brief, StringComparison.Ordinal);
                Assert.Contains("Fingerprint rule", brief, StringComparison.Ordinal);
            }

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
