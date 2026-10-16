// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Journey J49, carry an objective to completion with agents and people (the estate's design/goals.md § 14): the sponsor proposes and
/// activates a goal, a builder agent does its work and reports done while a predicate fails, a reviewer agent evaluates it condition
/// by condition, and the sponsor, a person, accepts it; neither agent can activate, evaluate its own work, or accept. With GOAL-04's
/// conductor, one step at a time: each step reaches the principal the roster names, a step routed to the builder runs through the
/// stand-in harness, a reserved step becomes an ask the sponsor answers, and no run starts while the sponsor's key signs here without
/// the sponsor's presence and no exception is recorded.
/// </summary>
[Collection("cli")]
public class GoalJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void J49_CarryAnObjectiveToItsSponsorsAcceptanceWithAgentsAndAPerson()
    {
        string root = CliHarness.TempDirectory("j49");
        string scratch = CliHarness.TempDirectory("j49-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string reviewer = Principal(root, scratch, op, "Reviewer", "reviewer");
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""")).Id;
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "guide.md"), "# The widget\n\nEvery command of the widget.\n");
            CliHarness.Ok("--estate", root, "--json", "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, "guide.json", $$$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{{product}}}"}],"summary":"Follows the widget's releases."}"""));

            // The sponsor proposes the goal and activates it; the builder can do neither for it.
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Release the widget's next version", "--objective", "Release widget 1.0.0 with its guide current.",
                "--done", $$$"""{"type":"release","subject":"{{{product}}}","fields":{"version":"1.0.0","status":"published"}}""",
                "--done", $$$"""{"type":"living-document","product":"{{{product}}}","state":"due","max":0}""",
                "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""",
                "--done", """{"evidence":"The guide describes every command of the widget.","check":"An evaluator reads docs/guide.md against the widget's commands."}""",
                "--constraint", "no publication without the operator's acceptance", "--budget", "wallMinutes=600", "--budget", "attemptsPerStep=3").Id;
            CliHarness.Outcome byBuilder = CliHarness.Run("--estate", root, "goal", "activate", goal, "--by", builder);
            Assert.Equal(2, byBuilder.Code);
            Assert.Contains("Only the goal's sponsor records it as active.", byBuilder.Err, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string shown = CliHarness.Ok("--estate", root, "goal").Out;
            Assert.Contains("Status: active since", shown, StringComparison.Ordinal);
            Assert.Contains("Budget its activation grants: wallMinutes 600, attemptsPerStep 3; not granted: tokens, concurrency, currency", shown, StringComparison.Ordinal);
            Assert.Contains("accepted by its sponsor, Operator", shown, StringComparison.Ordinal);

            // The builder does the work and reports done, while the goal's predicates still fail.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$$"""{"key":"W-1","kind":"ordinary","title":"Prepare widget 1.0.0","intent":"The goal's work.","scope":["widget"],"exitQuestion":"Is widget 1.0.0 ready to release?","inputs":[{"type":"goal","id":"{{{goal}}}"}],"deliverables":["widget 1.0.0"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"Widget 1.0.0 ready to release.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget 1.0.0"],"reviewCriteria":["tests pass"],"recovery":"Record a result."}""")).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example repository, work branch", "--note", "Prepare the release.").Id;
            CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, "result.json", """{"outcome":"completed","summary":"Widget 1.0.0 is ready and its guide describes every command. Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""));
            JsonElement progress = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
            Assert.Equal("active", progress.GetProperty("status").GetString());
            Assert.Equal(["not-met", "met", "not-met", "unknown"], progress.GetProperty("conditions").EnumerateArray().Select(c => c.GetProperty("verdict").GetString()));
            Assert.False(progress.GetProperty("ready").GetBoolean());
            Refused(root, "other than any step's author", "goal", "evaluate", goal, "--by", builder, "--summary", "My own work.", "--verdict", "4:met:I wrote it.", "--read", "4:" + result.Id);
            Refused(root, "A goal is accepted by its sponsor", "goal", "accept", goal, "--by", builder, "--statement", "Done.");

            // The reviewer reviews the result, the sponsor accepts the work, and the release is accepted and published: the guide is due.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", reviewer, "--subject", $"result:{result.Id}:{result.Hash}", "--body", CliHarness.WriteJson(scratch, "review.json", """{"independence":"independent-agent","outcome":"pass","summary":"Read the result.","findings":[]}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{result.Id}:{result.Hash}", "--body", CliHarness.WriteJson(scratch, "accept-result.json", """{"statement":"Accepted the result.","basis":[],"self":false}"""));
            JsonElement order = CliHarness.Ok("--estate", root, "--json", "work", "brief", "W-1").Json.GetProperty("workOrder");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}", "--body", CliHarness.WriteJson(scratch, "accept-order.json", """{"statement":"W-1 is done.","basis":[],"self":true}"""));
            CliHarness.Outcome candidate = CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--body", CliHarness.WriteJson(scratch, "candidate.json", Release("candidate")));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.Id}:{candidate.Hash}", "--body", CliHarness.WriteJson(scratch, "accept-release.json", """{"statement":"Accepted the candidate.","basis":[],"self":true}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--body", CliHarness.WriteJson(scratch, "published.json", Release("published")));

            // The evaluator finds the guide due; the sponsor cannot accept while it is.
            CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the records and the guide.", "--verdict", "4:met:The guide describes every command.", "--read", "4:" + result.Id);
            progress = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
            Assert.Equal(["met", "not-met", "met", "unknown"], progress.GetProperty("conditions").EnumerateArray().Select(c => c.GetProperty("verdict").GetString()));
            Assert.Equal("not-yet", progress.GetProperty("conditions")[1].GetProperty("evaluation").GetProperty("verdict").GetString());
            Assert.Contains("docs/guide.md: due at refuse", progress.GetProperty("conditions")[1].GetProperty("reason").GetString(), StringComparison.Ordinal);
            Refused(root, "condition 2 is not-yet", "goal", "accept", goal, "--by", op, "--statement", "Released.");

            // The reviewer clears the guide and evaluates again; only the sponsor accepts, on that evaluation.
            CliHarness.Ok("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", "current", "--summary", "The guide holds for 1.0.0.");
            CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the records and the guide again.", "--verdict", "4:met:The guide describes every command.", "--read", "4:" + result.Id);
            Refused(root, "never accepted by its evaluator", "goal", "accept", goal, "--by", reviewer, "--statement", "Released.");
            Assert.True(CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("ready").GetBoolean());
            CliHarness.Ok("--estate", root, "--json", "goal", "accept", goal, "--by", op, "--statement", "Widget 1.0.0 is released with its guide current.");

            Assert.Contains("Achieved: acceptance", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);
            Assert.Contains($"{goal}  achieved  Release the widget's next version  sponsor Operator", CliHarness.Ok("--estate", root, "goal").Out, StringComparison.Ordinal);
            Refused(root, "was achieved", "goal", "pause", goal, "--by", op);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void J49_AStopEndsAGoalWhileAStepRunsAndAnImpossibleVerdictHoldsItsAcceptance()
    {
        string root = CliHarness.TempDirectory("j49-stop");
        string scratch = CliHarness.TempDirectory("j49-stop-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string reviewer = Principal(root, scratch, op, "Reviewer", "reviewer");

            // An agent proposes a goal it sponsors: it stays proposed, and the goal list says so.
            string own = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", builder, "--title", "The builder's own goal", "--objective", "Build more.", "--done", """{"type":"work-order","min":1}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=1").Id;
            Refused(root, "never activate or fund one", "goal", "activate", own, "--by", builder);

            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Write the guide", "--objective", "A guide for the widget.", "--done", """{"evidence":"The guide exists.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=1", "--budget", "currency=5000:USD").Id;
            Assert.Contains("2 goals are open; name one:", CliHarness.Ok("--estate", root, "goal").Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            Assert.Contains("currency 5000 minor units of USD; not granted: tokens, concurrency", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);

            // The evaluator judges the condition impossible: acceptance waits, and the sponsor stops the goal while its step still runs.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$$"""{"key":"W-2","kind":"ordinary","title":"Write the guide","intent":"The goal's work.","scope":["guide"],"exitQuestion":"Is the guide written?","inputs":[{"type":"goal","id":"{{{goal}}}"}],"deliverables":["guide"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-2", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"A guide.","sources":[],"permittedChanges":["the guide"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it reads well"],"recovery":"Record a result."}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example repository, work branch", "--note", "Write it.");
            CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "The widget was withdrawn.", "--verdict", "1:impossible:There is no widget left to describe.");
            Refused(root, "condition 1 is impossible", "goal", "accept", goal, "--by", op, "--statement", "Done.");
            Refused(root, "Only the goal's sponsor", "goal", "stop", goal, "--by", builder);
            CliHarness.Ok("--estate", root, "--json", "goal", "stop", goal, "--by", op, "--note", "The widget was withdrawn.");
            Assert.Equal("abandoned", CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("status").GetString());
            Refused(root, "ended as abandoned", "goal", "resume", goal, "--by", op);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void J49_AGoalAnAgentSponsorsIsActivatedAndAcceptedOnlyByAnOperator()
    {
        string root = CliHarness.TempDirectory("j49-agent");
        string scratch = CliHarness.TempDirectory("j49-agent-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string reviewer = Principal(root, scratch, op, "Reviewer", "reviewer");
            string person = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "person.json", """{"kind":"human","name":"Contributor","roles":["contributor"]}""")).Id;
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$$"""{"title":"Accepting work","stake":"low","notAuthorized":[],"statement":"Accept others' ordinary work.","summary":"The contributor accepts ordinary work.","synthesis":"A grant to accept.","effects":["accept-work"],"affects":[],"decidedBy":"{{{op}}}","decidedAt":"2026-10-15T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "grant.json", $$$"""{"grantee":"{{{person}}}","scope":["accept-work"],"reserved":[],"conditions":[],"basis":"{{{decision}}}"}"""));

            // The builder proposes a goal it sponsors; it may not activate it, and may not name a delegate for it.
            string[] conditions = ["--done", """{"type":"work-order","fields":{"key":"W-3"},"state":"accepted"}""", "--done", """{"evidence":"The guide describes the widget.","check":"Read the guide."}"""];
            CliHarness.Outcome delegated = CliHarness.Run(["--estate", root, "--json", "goal", "propose", "--by", builder, "--title", "Keep the guide current", "--objective", "The guide describes the widget.", "--delegate", person, "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2", .. conditions]);
            Assert.Equal(1, delegated.Code);
            Assert.Contains("goal.delegate", delegated.Out, StringComparison.Ordinal);
            string goal = CliHarness.Ok(["--estate", root, "--json", "goal", "propose", "--by", builder, "--title", "Keep the guide current", "--objective", "The guide describes the widget.", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2", .. conditions]).Id;
            Assert.Contains("accepted by an operator, and only an operator, since its sponsor is an agent", CliHarness.Ok("--estate", root, "goal").Out, StringComparison.Ordinal);
            Refused(root, "never activate or fund one", "goal", "activate", goal, "--by", builder);
            Refused(root, "activated by an operator", "goal", "activate", goal, "--by", person);
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);

            // The builder does the work, the reviewer evaluates it, and neither they nor a person with accept-work may accept it.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$$"""{"key":"W-3","kind":"ordinary","title":"Revise the guide","intent":"The goal's work.","scope":["guide"],"exitQuestion":"Does the guide describe the widget?","inputs":[{"type":"goal","id":"{{{goal}}}"}],"deliverables":["guide"]}"""));
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-3", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"A current guide.","sources":[],"permittedChanges":["the guide"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it describes the widget"],"recovery":"Record a result."}""")).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example repository, work branch", "--note", "Revise the guide.").Id;
            string result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, "result.json", """{"outcome":"completed","summary":"The guide describes the widget.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")).Id;
            JsonElement order = CliHarness.Ok("--estate", root, "--json", "work", "brief", "W-3").Json.GetProperty("workOrder");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}", "--body", CliHarness.WriteJson(scratch, "accept-order.json", """{"statement":"W-3 is done.","basis":[],"self":true}"""));
            CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the work and the guide.", "--verdict", "2:met:The guide describes the widget.", "--read", "2:" + result);
            Refused(root, "only an operator", "goal", "accept", goal, "--by", builder, "--statement", "My goal is done.");
            Refused(root, "never accepted by its evaluator", "goal", "accept", goal, "--by", reviewer, "--statement", "Done.");
            Refused(root, "only an operator", "goal", "accept", goal, "--by", person, "--statement", "Done.");
            CliHarness.Ok("--estate", root, "--json", "goal", "accept", goal, "--by", op, "--statement", "Accepted for the estate, on the reviewer's evaluation.");
            Assert.Equal("achieved", CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("status").GetString());

            // A person who sponsors an ordinary goal may delegate its acceptance, and the goal says to whom.
            string ordinary = CliHarness.Ok(["--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Keep the guide current, again", "--objective", "The guide describes the widget.", "--delegate", person, "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2", .. conditions]).Id;
            Assert.Contains($"or Contributor ({person}), to whom the sponsor delegated it", CliHarness.Ok("--estate", root, "goal", "show", ordinary).Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AGoalIsRevisedOnlyByWhoDecidesForItByEveryRoute()
    {
        string root = CliHarness.TempDirectory("goal-revise");
        string scratch = CliHarness.TempDirectory("goal-revise-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Revisions.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string[] goal = ["--title", "Keep the guide current", "--objective", "The guide describes the widget.", "--done", """{"evidence":"The guide describes the widget.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2"];

            // The builder proposes a goal for the operator: anyone may propose one. Revising it is the operator's, its sponsor's, alone.
            string proposed = CliHarness.Ok(["--estate", root, "--json", "goal", "propose", "--by", builder, "--sponsor", op, .. goal]).Id;
            Refused(root, "A goal is revised only by its sponsor, Operator", ["goal", "propose", "--by", builder, "--sponsor", builder, "--supersedes", proposed, .. goal]);
            JsonElement body = CliHarness.Ok("--estate", root, "--json", "goal", "show", proposed).Json.GetProperty("goal").GetProperty("body");
            CliHarness.Outcome around = CliHarness.Run("--estate", root, "--json", "records", "new", "goal", "--by", builder, "--supersedes", proposed, "--body", CliHarness.WriteJson(scratch, "revision.json", body.GetRawText()));
            Assert.Equal(1, around.Code);
            Assert.Contains("goal.revision", around.Out, StringComparison.Ordinal);
            string revised = CliHarness.Ok(["--estate", root, "--json", "goal", "propose", "--by", op, "--supersedes", proposed, .. goal]).Id;
            Assert.Equal(2, CliHarness.Ok("--estate", root, "--json", "goal", "show", revised).Json.GetProperty("goal").GetProperty("revision").GetInt32());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void J49_EachStepReachesItsRostersPrincipalAReservedOneAsksTheSponsorAndNoRunStartsWhileTheSponsorsKeySignsUnattended()
    {
        string root = CliHarness.TempDirectory("j49-routed");
        string scratch = CliHarness.TempDirectory("j49-routed-scratch");
        _ = CliHarness.Run("--help");
        var registry = Vericairn.Runtime.Providers.ProviderRegistry.Current;
        Vericairn.Runtime.Workers.IAgentLauncher original = registry.Launcher("claude-code")!;
        registry.Register(new ReportedLauncher(original));
        try
        {
            // The operator, the goal's sponsor, signs with a file key held here; the builder is Claude Code with an admitted device here.
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49, routed.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")).Id;
            string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator's laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
            string builderDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Builder's device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's.");
            string clone = Path.Combine(scratch, "widget");
            Directory.CreateDirectory(clone);
            Git(clone, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(clone, "README.md"), "widget\n");
            Git(clone, "add", "README.md");
            Git(clone, "commit", "-q", "-m", "The first commit");
            string resource = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", clone, "--by", op, "--name", "widget", "--role", "The widget's source.").Json.GetProperty("resource").GetProperty("id").GetString()!;
            CliHarness.Ok("--estate", root, "--json", "sources", "clone", clone, "--resource", resource);

            // The roster names the builder for building, and the operator for publication and decisions; the sponsor activates the goal.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"builder","principals":["{{builder}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}"""));
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Release the widget's next version", "--objective", "Release widget 1.0.0.", "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""", "--done", """{"evidence":"Widget 1.0.0 is published.","check":"Read the release."}""", "--constraint", "no publication without the operator's acceptance", "--budget", "wallMinutes=120", "--budget", "attemptsPerStep=2").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "w1.json", $$"""{"key":"W-1","kind":"ordinary","title":"Build widget 1.0.0","intent":"The goal's work.","scope":["widget"],"exitQuestion":"Does widget 1.0.0 build and pass its tests?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget 1.0.0"]}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "w2.json", $$"""{"key":"W-2","kind":"ordinary","title":"Publish widget 1.0.0","intent":"The goal's publication.","scope":["the widget's release"],"exitQuestion":"Is widget 1.0.0 published?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["the published release"],"role":"publisher"}"""));

            // W-1 reaches the builder through Claude Code's launcher; W-2, publication, reaches the operator, who holds it.
            string next = CliHarness.Ok("--estate", root, "goal", "next").Out;
            Assert.Contains($"    builder: agent, Builder [principal {builder}] through claude-code\n", next, StringComparison.Ordinal);
            Assert.Contains($"    publisher, a reserved action (publication): reserved, Operator [principal {op}]\n", next, StringComparison.Ordinal);
            Assert.Contains($"      custody here: an unattended run is refused: Device {operatorDevice} (Operator's laptop, admitted) of Operator ({op})", next, StringComparison.Ordinal);

            // While the sponsor's key signs here without the sponsor's presence, the builder's run is refused and nothing is recorded.
            string report = Path.Combine(scratch, "stand-in.jsonl");
            string[] step = ["--estate", root, "goal", "step", goal, "--by", op, "--repository", resource,
                "--execution", "model=model-b", "--execution", "effort=low", "--setting", "command=" + FakeHarness.Setting("mcp", report)];
            CliHarness.Outcome refused = CliHarness.Run(step);
            Assert.Equal(2, refused.Code);
            Assert.Contains($"W-1 is routed to Builder through claude-code, and its run cannot start: The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine (design/goals.md § 10): Device {operatorDevice}", refused.Err, StringComparison.Ordinal);
            Assert.Contains("Nothing was recorded.", refused.Err, StringComparison.Ordinal);
            Assert.False(File.Exists(report));
            Assert.DoesNotContain("assignment ", CliHarness.Ok("--estate", root, "work", "observe", "W-1").Out, StringComparison.Ordinal);

            // The sponsor records an exception for the goal, with its reason and expiry; the step is routed to the builder and run by the stand-in.
            string exception = CliHarness.Ok("--estate", root, "--json", "goal", "custody-exception", goal, "--by", op, "--reason", "The operator watches this machine through the release.", "--expires", Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(1))).Id;
            Assert.Contains($"custody here: an unattended run is allowed under the custody exception {exception}", CliHarness.Ok("--estate", root, "goal", "next").Out, StringComparison.Ordinal);
            CliHarness.Outcome ran = CliHarness.Run(step);
            Assert.True(ran.Code == 0, ran.Out + ran.Err + string.Join('\n', File.Exists(report) ? File.ReadAllLines(report).Select(l => l[..Math.Min(l.Length, 400)]) : []));
            Assert.StartsWith($"step W-1  agent  run  {builder}\n  Routing assigned W-1 to its agent (assignment ", ran.Out, StringComparison.Ordinal);
            Assert.Contains($"It was launched under the custody exception {exception}", ran.Out, StringComparison.Ordinal);
            Assert.Contains("observed-success  claude-code as " + builder, ran.Out, StringComparison.Ordinal);
            JsonElement execution = Assert.Single(CliHarness.Ok("--estate", root, "--json", "worker", "runs").Json.GetProperty("runs").EnumerateArray()).GetProperty("execution");
            Assert.Equal("model-b", execution.GetProperty("requested").GetProperty("model").GetString());
            Assert.Equal("low", execution.GetProperty("requested").GetProperty("effort").GetString());
            Assert.Contains("completed, awaiting review: The widget builds and its tests pass. Done.", CliHarness.Ok("--estate", root, "work", "observe", "W-1").Out, StringComparison.Ordinal);

            // W-1 awaits review; the next step, W-2, a reserved action, becomes an ask to the operator with its recommendation first.
            CliHarness.Outcome reserved = CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op);
            Assert.StartsWith($"step W-2  reserved  ask  {op}\n  W-2 is a reserved action: it is assigned to Operator, who holds it", reserved.Out, StringComparison.Ordinal);
            JsonElement asked = Assert.Single(CliHarness.Ok("--estate", root, "--json", "ask", "--to", op).Json.GetProperty("principals").EnumerateArray()).GetProperty("delivered")[0];
            Assert.StartsWith("Carry it yourself, from vericairn work brief W-2: no agent carries a reserved action without a grant in force", asked.GetProperty("recommendation").GetString(), StringComparison.Ordinal);
            Assert.Equal("W-2 waits until you act, and nothing reserved is done for you; the goal's other steps go on.", asked.GetProperty("consequence").GetString());
            string ask = asked.GetProperty("id").GetString()!;
            Assert.Contains("  1. Recommendation: Carry it yourself", CliHarness.Ok("--estate", root, "ask").Out, StringComparison.Ordinal);

            // The sponsor answers it with a decision; nothing then waits on anyone, and every eligible step is held.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", op, "--body", CliHarness.WriteJson(scratch, "answer.json", $$"""{"title":"Publish widget 1.0.0","statement":"I publish 1.0.0 myself once the build is accepted.","summary":"The operator carries W-2.","synthesis":"The operator answers the ask: the operator publishes widget 1.0.0 by hand after accepting the build.","effects":["W-2 stays with the operator"],"affects":[],"decidedBy":"{{op}}","decidedAt":"{{Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)}}","stake":"medium","notAuthorized":["publication by an agent"],"answers":["{{ask}}"]}"""));
            Assert.Equal("Nothing waits on anyone: no ask is open or notified.\n", CliHarness.Ok("--estate", root, "ask").Out);
            Assert.Contains($"    answered, to Operator: W-2 Publish widget 1.0.0, a step of goal Release the widget's next version, is a reserved action, publication: will you carry it?", CliHarness.Ok("--estate", root, "goal", "next").Out, StringComparison.Ordinal);
            Assert.Contains("Nothing to dispatch: every eligible step of goal", CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op).Out, StringComparison.Ordinal);
            JsonElement shown = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
            Assert.Equal("active", shown.GetProperty("status").GetString());
            Assert.False(shown.GetProperty("ready").GetBoolean());
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            Assert.True(new FileKeyStore(VericairnHome.Path()).Has(operatorDevice));
        }
        finally
        {
            registry.Register(original);
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void ReconcileShowsABatchStoppedPartWayWithItsOwnGuidance()
    {
        string root = CliHarness.TempDirectory("batch");
        string scratch = CliHarness.TempDirectory("batch-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Batches.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")).Id;
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Build the widget", "--objective", "A widget.", "--done", """{"evidence":"It builds.","check":"Build it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            CliHarness.Outcome order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "w1.json", $$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}"""));

            // Routing's assignment of W-1 was stored and the intent of its run was not: the batch's journal stays on this machine.
            string stranded = CliHarness.Ok("--estate", root, "--json", "records", "new", "assignment", "--by", op, "--subject", $"work-order:{order.Id}:{order.Hash}", "--body", CliHarness.WriteJson(scratch, "assignment.json", $$$"""{"contributor":"{{{builder}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."},"routedWith":"run"}""")).Id;
            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope assignment = model.ById[stranded];
            var stored = new AuthoredRecord(assignment.ExpectedPath, File.ReadAllBytes(Path.Combine(root, assignment.ExpectedPath)), assignment);
            using JsonDocument nothing = JsonDocument.Parse("{}");
            AuthoredRecord intent = RecordAuthor.Create(new RecordDraft("operation", op, nothing.RootElement));
            using EstateSession session = EstateSession.Open(root);
            BatchJournal journal = BatchJournal.Begin(session, "routing's assignment of work order W-1 to Builder and the intent of its run", order.Id, [stored, intent]);

            CliHarness.Outcome tasks = CliHarness.Run("--estate", root, "estate", "reconcile");
            Assert.NotEqual(0, tasks.Code);
            Assert.Contains($"batch.incomplete  {assignment.ExpectedPath}: A batch this machine began at ", tasks.Out, StringComparison.Ordinal);
            Assert.Contains($"Stored: assignment {stranded}. Not stored: operation {intent.Envelope.Id}.", tasks.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("Settle divergent revisions", tasks.Out, StringComparison.Ordinal);
            Assert.Contains("batch.incomplete", Assert.Single(CliHarness.Ok("--estate", root, "--json", "status").Json.GetProperty("reconcile").EnumerateArray()).GetRawText(), StringComparison.Ordinal);
            journal.Close();
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "reconcile").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void GoalNextRoutesTheOpenGoalsStepsAndRecordsNothing()
    {
        string root = CliHarness.TempDirectory("goal-next");
        string scratch = CliHarness.TempDirectory("goal-next-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Routing.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Assert.Contains("No goals; propose one with vericairn goal propose.", CliHarness.Ok("--estate", root, "goal", "next").Out, StringComparison.Ordinal);
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"builder","principals":["{{builder}}"]},{"role":"decider","principals":["{{op}}"]}]}"""));
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Build the widget", "--objective", "A widget.", "--done", """{"evidence":"It ships.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}""")).Id;
            int records = Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories).Count();

            // The builder has no admitted device, so it is not available: the sponsor would be asked, never another contributor.
            string text = CliHarness.Ok("--estate", root, "goal", "next").Out;
            Assert.StartsWith($"Next steps of goal Build the widget [goal {goal}], active, routed from this machine\n", text, StringComparison.Ordinal);
            Assert.Contains($"  W-1  Build the widget [work-order {order}]\n    builder: unavailable, Operator [principal {op}]\n", text, StringComparison.Ordinal);
            Assert.Contains("      candidate Builder (agent): not available: it has no admitted device, so it could sign no attempt and no result\n", text, StringComparison.Ordinal);
            Assert.Contains($"  Next: W-1, unavailable; vericairn goal step {goal} takes it\n", text, StringComparison.Ordinal);
            JsonElement json = CliHarness.Ok("--estate", root, "--json", "goal", "next", goal).Json;
            Assert.Equal("vericairn.next/1", json.GetProperty("format").GetString());
            Assert.Equal("unavailable", Assert.Single(json.GetProperty("steps").EnumerateArray()).GetProperty("route").GetProperty("state").GetString());
            Assert.Equal(2, CliHarness.Run("--estate", root, "goal", "next", "01M4TRNE1GYEK4P8MXQAB3C8Q4").Code);
            Assert.Equal(records, Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories).Count());
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AskShowsWhatWaitsWithTheRecommendationFirstAndAtMostThreeAtATime()
    {
        string root = CliHarness.TempDirectory("ask");
        string scratch = CliHarness.TempDirectory("ask-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Asks.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            Assert.Equal("Nothing waits on anyone: no ask is open or notified.\n", CliHarness.Ok("--estate", root, "ask").Out);
            var asks = new List<string>();
            foreach (string question in new[] { "Publish the guide?", "Raise the budget?", "Name a planner?", "Stop the goal?" })
            {
                asks.Add(CliHarness.Ok("--estate", root, "--json", "records", "new", "ask", "--by", builder, "--body", CliHarness.WriteJson(scratch, $"ask-{asks.Count}.json", $$"""{"to":"{{op}}","question":"{{question}}","recommendation":"Yes: {{question}}","consequence":"It waits.","state":"open"}""")).Id);
            }

            string text = CliHarness.Ok("--estate", root, "ask").Out;
            Assert.StartsWith($"What waits on Operator [principal {op}]: 4 asks, 3 in this delivery and the rest in order behind them\n  1. Recommendation: Yes: Publish the guide?\n     Asked: Publish the guide? [ask {asks[0]}], by Builder, open\n     If unanswered: It waits.\n", text, StringComparison.Ordinal);
            Assert.Contains($"  Waiting in order: 1 more ask [ask {asks[3]}]", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Stop the goal?", text, StringComparison.Ordinal);
            Assert.Equal(text, CliHarness.Ok("--estate", root, "ask", "--to", op).Out);
            Assert.StartsWith($"Nothing waits on Builder [principal {builder}]", CliHarness.Ok("--estate", root, "ask", "--to", builder).Out, StringComparison.Ordinal);
            JsonElement json = CliHarness.Ok("--estate", root, "--json", "ask").Json;
            Assert.Equal("vericairn.asks/1", json.GetProperty("format").GetString());
            Assert.Equal(3, Assert.Single(json.GetProperty("principals").EnumerateArray()).GetProperty("delivered").GetArrayLength());
            Refused(root, "--to names a principal by its ULID", "ask", "--to", "operator");

            // Reading what waits answers nothing; a decision of the principal asked does, and the fourth follows into the delivery.
            CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "answer.json", $$"""{"title":"Publish","statement":"Publish the guide.","summary":"Publish it.","synthesis":"The operator answers.","effects":["the guide is published"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T12:00:00Z","stake":"low","notAuthorized":[],"answers":["{{asks[0]}}"]}"""));
            text = CliHarness.Ok("--estate", root, "ask").Out;
            Assert.StartsWith($"What waits on Operator [principal {op}]: 3 asks\n  1. Recommendation: Yes: Raise the budget?", text, StringComparison.Ordinal);
            Assert.Contains($"[ask {asks[3]}]", text, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void ACustodyExceptionIsRecordedForTheGoalsCurrentRevisionByItsSponsorAlone()
    {
        string root = CliHarness.TempDirectory("custody-exception");
        string scratch = CliHarness.TempDirectory("custody-exception-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Custody.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Build the widget", "--objective", "A widget.", "--done", """{"evidence":"It ships.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2").Id;
            string expires = Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(1));
            CliHarness.Outcome byAgent = CliHarness.Run("--estate", root, "--json", "goal", "custody-exception", goal, "--by", builder, "--reason", "My own runs.", "--expires", expires);
            Assert.Equal(1, byAgent.Code);
            Assert.Contains("custody-exception.sponsor", byAgent.Out, StringComparison.Ordinal);
            Refused(root, "--expires is a time such as", "goal", "custody-exception", goal, "--by", op, "--reason", "Overnight.", "--expires", "tomorrow");
            string exception = CliHarness.Ok("--estate", root, "--json", "goal", "custody-exception", goal, "--by", op, "--reason", "The build loop runs overnight.", "--expires", expires).Id;
            JsonElement shown = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("goal");
            using (Vericairn.Runtime.Service.EstateService service = Vericairn.Runtime.Service.EstateService.Open(root))
            {
                JsonElement subject = service.Record(exception)!.Envelope.GetProperty("subject");
                Assert.Equal((goal, shown.GetProperty("hash").GetString()), (subject.GetProperty("id").GetString(), subject.GetProperty("hash").GetString()));
            }

            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AGoalsStepsAreTakenOneAtATimeAndAPersonTakesOverAnAgentsStep()
    {
        string root = CliHarness.TempDirectory("goal-step");
        string scratch = CliHarness.TempDirectory("goal-step-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Steps.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"builder","principals":["{{builder}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}"""));
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Release the guide", "--objective", "A published guide.", "--done", """{"evidence":"It is published.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            foreach ((string key, string role) in new[] { ("W-1", "builder"), ("W-2", "publisher") })
            {
                CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["guide"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["guide"],"role":"{{role}}"}"""));
            }

            // The builder has no device here, so W-1 is asked of the sponsor; W-2, a reserved action, is assigned to the operator and asked.
            Assert.StartsWith($"step W-1  unavailable  ask  {op}\n  No principal is available for W-1: Operator is asked to assign someone or change the roster", CliHarness.Ok("--estate", root, "goal", "step", "--by", op).Out, StringComparison.Ordinal);
            JsonElement reserved = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("W-2", "reserved", "ask"), (reserved.GetProperty("step").GetString(), reserved.GetProperty("route").GetString(), reserved.GetProperty("outcome").GetString()));
            Assert.Contains("Nothing to dispatch: every eligible step of goal", CliHarness.Ok("--estate", root, "goal", "step", "--by", op).Out, StringComparison.Ordinal);
            Assert.StartsWith($"What waits on Operator [principal {op}]: 2 asks\n  1. Recommendation: Make Builder available: it has no admitted device", CliHarness.Ok("--estate", root, "ask").Out, StringComparison.Ordinal);

            // A person takes over the step the builder holds: the hand-over is the operator's assignment, which then holds W-1.
            string held = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"The guide.","sources":[],"permittedChanges":["the guide"],"prohibitedEffects":[],"workspace":["example repository"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it reads well"],"recovery":"Record a result."}""")).Id;
            CliHarness.Outcome taken = CliHarness.Ok("--estate", root, "work", "takeover", held, "--by", op);
            Assert.Contains($"Operator takes the step over by assignment", taken.Out, StringComparison.Ordinal);
            Assert.Contains("No run of the agent's assignment was in flight.", taken.Out, StringComparison.Ordinal);
            Assert.Contains("    builder: assigned, Operator [principal " + op + "]\n      assigned to Operator by assignment ", CliHarness.Ok("--estate", root, "goal", "next").Out, StringComparison.Ordinal);
            Refused(root, "no longer holds work order W-1", "work", "takeover", held, "--by", op);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static void Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in GitIdentity.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
    }

    private static string Principal(string root, string scratch, string op, string name, string role) =>
        CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, name + ".json", $$$"""{"kind":"agent","name":"{{{name}}}","roles":["{{{role}}}"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;

    private static void Refused(string root, string because, params string[] args)
    {
        CliHarness.Outcome outcome = CliHarness.Run(["--estate", root, .. args]);
        Assert.True(outcome.Code == 2, $"vericairn {string.Join(' ', args)} exited {outcome.Code}, not refused\n{outcome.Out}\n{outcome.Err}");
        Assert.Contains(because, outcome.Err, StringComparison.Ordinal);
    }

    private static string Release(string status)
    {
        // Published when observed, which is after the candidate was recorded.
        string published = status == "published" ? $$$""","publishedAt":"{{{Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)}}}","url":"https://example.invalid/releases/1.0.0" """ : string.Empty;
        return $$$"""{"version":"1.0.0","repository":"Example/widget","commit":"{{{Commit}}}","status":"{{{status}}}","artifacts":[{"name":"widget-1.0.0.zip","hash":"{{{Hash}}}","bytes":1,"platform":"any"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]{{{published}}}}""";
    }
}
