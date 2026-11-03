// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A run of a flow from the command line (the estate's design/flow.md §§ 6, 10 and 12): a flow installed in the estate, a run
/// proposed with its subject, its own configuration and the step it ends at, and moved one goal step per command through people,
/// each step's state shown by goal show from the records alone, until the run is complete and its acceptance remains.
/// </summary>
[Collection("cli")]
public class FlowCliJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ARunIsProposedConfiguredAndMovedOneStepPerCommand()
    {
        string root = CliHarness.TempDirectory("flow-run");
        string scratch = CliHarness.TempDirectory("flow-run-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Runs.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string reviewer = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "reviewer.json", """{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            Directory.CreateDirectory(Path.Combine(root, "docs", "templates"));
            File.WriteAllText(Path.Combine(root, "docs", "templates", "example-song-1.0.0.json"), FlowFixture.Song().ToJsonString());
            JsonElement listed = CliHarness.Ok("--estate", root, "--json", "templates", "list").Json;
            Assert.Contains(listed.GetProperty("templates").EnumerateArray(), t => t.GetProperty("pin").GetProperty("id").GetString() == "example/song");
            Assert.Contains("example/song 1.0.0 at sha256:", CliHarness.Ok("--estate", root, "templates", "list").Out, StringComparison.Ordinal);
            Assert.Contains(", a flow", CliHarness.Ok("--estate", root, "templates", "list").Out, StringComparison.Ordinal);
            Refused(root, "A flow is a role process a goal runs", ["docs", "new", "example/song", "docs/song.md", "--version", "1.0.0", "--by", op, "--title", "Song"]);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json",
                $$$"""{"roles":[{"role":"writer","principals":["{{{op}}}"]},{"role":"reviewer","principals":["{{{reviewer}}}"]},{"role":"listener","principals":["{{{op}}}"]},{"role":"decider","principals":["{{{op}}}"]}]}"""));
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json",
                """{"key":"SONG-1","kind":"ordinary","title":"Produce the song","intent":"Produce a song and release it.","scope":["path:song/"],"exitQuestion":"Is the song released?","inputs":[],"deliverables":["the song"]}""")).Id;

            // Run options belong to a run; the flow's subject, the goal's own configuration and the step it ends at are checked.
            string[] propose = ["goal", "propose", "--by", op, "--title", "Master the song", "--objective", "Produce the master.", "--done", """{"type":"work-order","fields":{"key":"SONG-1"}}""",
                "--work-order", order, "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2"];
            Refused(root, "go with --flow", [.. propose, "--until", "master"]);
            Refused(root, "--subject is name=value with a field of the flow's subject", [.. propose, "--flow", "example/song@1.0.0", "--subject", "carrier=x"]);
            Refused(root, "Step master is required", [.. propose, "--flow", "example/song@1.0.0", "--subject", "recording=" + order, "--subject", "tracks=intro", "--flow-option", "step.master=off"]);
            string goal = CliHarness.Ok(["--estate", root, "--json", .. propose, "--flow", "example/song@1.0.0", "--subject", "recording=" + order, "--subject", "tracks=intro,outro",
                "--flow-option", "step.shorts=off", "--until", "master"]).Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string shown = CliHarness.Ok("--estate", root, "goal", "show", goal).Out;
            Assert.Contains("Flow: example/song 1.0.0", shown, StringComparison.Ordinal);
            Assert.Contains("0 of 3 steps done", shown, StringComparison.Ordinal);
            Assert.Contains("Ends at: master", shown, StringComparison.Ordinal);
            Assert.Contains("Configuration: step.shorts off, from the goal's plan", shown, StringComparison.Ordinal);
            Assert.Contains("shorts[intro]  off", shown, StringComparison.Ordinal);
            JsonElement flow = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("flow");
            Assert.Equal(["draft stopped", "review-lyrics blocked", "master blocked", "shorts off", "shorts off", "listen off", "accept off", "publish off"],
                flow.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("key").GetString() + " " + s.GetProperty("state").GetString()));
            Assert.Contains("draft", CliHarness.Ok("--estate", root, "goal", "next", goal).Out, StringComparison.Ordinal);

            // One step per command: draft is the operator's, as the writer; its assignment names the step.
            JsonElement first = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("ask", "draft", "person"), (first.GetProperty("outcome").GetString(), first.GetProperty("step").GetString(), first.GetProperty("route").GetString()));
            string drafting = first.GetProperty("assignment").GetProperty("id").GetString()!;
            JsonElement lyrics = Carry(root, scratch, op, drafting, "lyrics", "text/markdown", 'a');

            // The review is the reviewer's, an agent other than the writer, which no launcher here starts: goal step says so and records
            // nothing, and the reviewer records its review of the exact lyrics.
            Assert.Equal("next", CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("flow").GetProperty("steps")[1].GetProperty("state").GetString());
            string held = CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op).Out;
            Assert.Contains("review-lyrics is next, unavailable: no principal of the reviewer role is available here", held, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", reviewer, "--subject", $"result:{lyrics.GetProperty("id").GetString()}:{lyrics.GetProperty("hash").GetString()}",
                "--body", CliHarness.WriteJson(scratch, "review.json", """{"independence":"independent-agent","outcome":"pass","summary":"Read the lyrics.","findings":[]}"""));

            JsonElement third = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal("master", third.GetProperty("step").GetString());
            Carry(root, scratch, op, third.GetProperty("assignment").GetProperty("id").GetString()!, "master", null, 'b');

            // The run is complete at its until step; nothing more is taken, and its evaluation and acceptance remain the goal's.
            Assert.Contains("the run is complete, and the goal's evaluation and acceptance remain", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);
            Assert.Contains("every step of goal", CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op).Out, StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    /// <summary>The person's attempt and completed result on a step's assignment, naming its output by hash; returns the result's receipt.</summary>
    private static JsonElement Carry(string root, string scratch, string by, string assignment, string output, string? media, char hash)
    {
        string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", by, "--workspace", "song repository, work branch", "--note", "Carry the step.").Id;
        var named = new JsonObject { ["name"] = output, ["hash"] = "sha256:" + new string(hash, 64) };
        if (media is not null) named["mediaType"] = media;
        var body = new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = new JsonArray(named), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Hand it on.",
        };
        return CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", by, "--body", CliHarness.WriteJson(scratch, output + "-result.json", body.ToJsonString())).Json;
    }

    private static void Refused(string root, string because, string[] args)
    {
        CliHarness.Outcome outcome = CliHarness.Run(["--estate", root, .. args]);
        Assert.True(outcome.Code != 0, $"vericairn {string.Join(' ', args)} exited {outcome.Code}, not refused\n{outcome.Out}\n{outcome.Err}");
        Assert.Contains(because, outcome.Err + outcome.Out, StringComparison.Ordinal);
    }
}
