// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Journey J49's cases on the command line that need no run in flight (the estate's design/goals.md § 14): a goal resumed on another
/// machine, whose state is its records while its steps are routed from the machine that takes them, and an evaluator's verdict of
/// impossible that ends the goal only once who decides for it confirms it. The cases with a run in flight are journeyed through the
/// API by GoalRunJourneyTests.
/// </summary>
public partial class GoalJourneyTests
{
    [Fact]
    public void J49_AGoalPausedOnOneMachineIsResumedOnAnotherAndItsStepsAreRoutedFromThere()
    {
        string a = CliHarness.TempDirectory("j49-laptop");
        string homeA = CliHarness.TempDirectory("j49-laptop-home");
        string homeB = CliHarness.TempDirectory("j49-build-home");
        string scratch = CliHarness.TempDirectory("j49-machines-scratch");
        string b = string.Empty;
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        _ = CliHarness.Run("--help");
        var registry = Vericairn.Runtime.Providers.ProviderRegistry.Current;
        Vericairn.Runtime.Workers.IAgentLauncher original = registry.Launcher("claude-code")!;
        registry.Register(new ReportedLauncher(original));
        try
        {
            // On the operator's laptop: the estate, the operator's device, the builder (Claude Code), the roster, the widget's repository
            // and the goal, with a step of the sponsor's and one the planner proposes, both for the builder, whose keys are on no machine yet.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            string op = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49, on two machines.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", a, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")).Id;
            string laptop = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator's laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", laptop, "--by", op, "--note", "The first authority.");
            string clone = Path.Combine(scratch, "widget");
            Directory.CreateDirectory(clone);
            Git(clone, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(clone, "README.md"), "widget\n");
            Git(clone, "add", "README.md");
            Git(clone, "commit", "-q", "-m", "The first commit");
            string resource = CliHarness.Ok("--estate", a, "--json", "sources", "adopt", clone, "--by", op, "--name", "widget", "--role", "The widget's source.").Json.GetProperty("resource").GetProperty("id").GetString()!;
            CliHarness.Ok("--estate", a, "--json", "sources", "clone", clone, "--resource", resource);
            string planner = Principal(a, scratch, op, "Planner", "contributor");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json", $$"""{"roles":[{"role":"planner","principals":["{{planner}}"]},{"role":"builder","principals":["{{builder}}"]},{"role":"decider","principals":["{{op}}"]}]}"""));
            string goal = CliHarness.Ok("--estate", a, "--json", "goal", "propose", "--by", op, "--title", "Release the widget's next version", "--objective", "Release widget 1.0.0.", "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""", "--done", """{"evidence":"Widget 1.0.0 builds.","check":"Read the result."}""", "--budget", "wallMinutes=120", "--budget", "attemptsPerStep=2").Id;
            CliHarness.Ok("--estate", a, "--json", "goal", "activate", goal, "--by", op);
            foreach ((string key, string by) in new[] { ("W-1", op), ("W-2", planner) })
            {
                CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", by, "--body", CliHarness.WriteJson(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Build part {{key}}","intent":"The goal's work.","scope":["widget"],"exitQuestion":"Does part {{key}} build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget 1.0.0"]}"""));
            }

            // The builder can carry nothing from the laptop, so the sponsor pauses the goal until the build machine is ready.
            Assert.Contains("      candidate Builder (agent): not available: it has no admitted device, so it could sign no attempt and no result\n", CliHarness.Ok("--estate", a, "goal", "next").Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", a, "--json", "goal", "pause", goal, "--by", op, "--note", "Until the build machine is ready.");

            // The build machine holds a copy of the estate; its own keys are made there and admitted from the laptop, records travelling both ways.
            b = CliHarness.Clone(a, "j49-build");
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            string desktop = CliHarness.Ok("--estate", b, "--json", "devices", "create", "--principal", op, "--name", "Operator's build machine", "--custody", "file").Id;
            string builderDevice = CliHarness.Ok("--estate", b, "--json", "devices", "create", "--principal", builder, "--name", "Builder's device", "--custody", "file").Id;
            Assert.Equal(2, CliHarness.MergeRecords(b, a));
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", desktop, "--by", op, "--note", "The operator's build machine.");
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's, on the build machine.");
            Assert.Equal(2, CliHarness.MergeRecords(a, b));
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            string cloneB = Path.Combine(scratch, "widget-b");
            Git(scratch, "clone", "-q", clone, cloneB);
            CliHarness.Ok("--estate", b, "--json", "sources", "clone", cloneB, "--resource", resource);

            // There the goal is paused, as the laptop left it, and takes no step; its sponsor resumes it there, signed by that machine's device.
            JsonElement paused = CliHarness.Ok("--estate", b, "--json", "goal", "show", goal).Json;
            Assert.Equal("paused", paused.GetProperty("status").GetString());
            Refused(b, "is paused: a step is taken only for an active goal", "goal", "step", goal, "--by", op, "--repository", resource);
            CliHarness.Outcome resumed = CliHarness.Ok("--estate", b, "--json", "goal", "resume", goal, "--by", op);
            Assert.Contains(desktop, File.ReadAllText(Path.Combine(b, resumed.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))), StringComparison.Ordinal);
            Assert.Contains("Status: active since", CliHarness.Ok("--estate", b, "goal", "show", goal).Out, StringComparison.Ordinal);

            // Routed from the build machine, W-1 reaches the builder there; the operator's key there signs without presence, so the sponsor
            // records a custody exception for the goal, and the step's run starts there.
            string next = CliHarness.Ok("--estate", b, "goal", "next").Out;
            Assert.Contains($"    builder: agent, Builder [principal {builder}] through claude-code\n", next, StringComparison.Ordinal);
            Assert.Contains($"      custody here: an unattended run is refused: Device {desktop} (Operator's build machine, admitted) of Operator ({op})", next, StringComparison.Ordinal);
            CliHarness.Ok("--estate", b, "--json", "goal", "custody-exception", goal, "--by", op, "--reason", "The operator watches the build machine through the release.", "--expires", Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(1)));
            string report = Path.Combine(scratch, "stand-in.jsonl");
            CliHarness.Outcome ran = CliHarness.Run("--estate", b, "goal", "step", goal, "--by", op, "--repository", resource, "--setting", "command=" + FakeHarness.Setting("mcp", report));
            Assert.True(ran.Code == 0, ran.Out + ran.Err + string.Join('\n', File.Exists(report) ? File.ReadAllLines(report).Select(l => l[..Math.Min(l.Length, 400)]) : []));
            Assert.StartsWith($"step W-1  agent  run  {builder}\n", ran.Out, StringComparison.Ordinal);
            Assert.Contains("observed-success  claude-code as " + builder, ran.Out, StringComparison.Ordinal);
            Assert.Contains("completed, awaiting review: The widget builds and its tests pass. Done.", CliHarness.Ok("--estate", b, "work", "observe", "W-1").Out, StringComparison.Ordinal);

            // Back on the laptop the same records give the same goal, while its steps are routed from there: W-1 awaits review, and the
            // builder, whose key is on the build machine, cannot carry W-2 from the laptop.
            Assert.True(CliHarness.MergeRecords(b, a) >= 5);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            JsonElement onLaptop = CliHarness.Ok("--estate", a, "--json", "goal", "show", goal).Json;
            JsonElement onBuild = Shown(b, homeB, goal);
            Assert.Equal(("active", onBuild.GetProperty("goal").GetProperty("hash").GetString()), (onLaptop.GetProperty("status").GetString(), onLaptop.GetProperty("goal").GetProperty("hash").GetString()));
            Assert.Equal(onBuild.GetProperty("conditions").GetRawText(), onLaptop.GetProperty("conditions").GetRawText());
            string fromLaptop = CliHarness.Ok("--estate", a, "goal", "next").Out;
            Assert.Contains("awaiting review; a principal other than its author reviews it", fromLaptop, StringComparison.Ordinal);
            Assert.Contains("  W-2  Build part W-2 [work-order ", fromLaptop, StringComparison.Ordinal);
            Assert.Contains("      candidate Builder (agent): not available: no key of its admitted devices is held on this machine", fromLaptop, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            Assert.Equal(0, CliHarness.Run("--estate", b, "estate", "check").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            registry.Register(original);
            CliHarness.Delete(a, b, homeA, homeB, scratch);
        }
    }

    [Fact]
    public void J49_AnImpossibleVerdictEndsTheGoalOnceWhoDecidesForItConfirmsIt()
    {
        string root = CliHarness.TempDirectory("j49-impossible");
        string scratch = CliHarness.TempDirectory("j49-impossible-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "J49.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, scratch, op, "Builder", "builder");
            string reviewer = Principal(root, scratch, op, "Reviewer", "reviewer");
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Describe the widget's plug-ins", "--objective", "A guide to the widget's plug-ins.", "--done", """{"evidence":"The guide describes every plug-in of the widget.","check":"Read the guide against the widget."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=1").Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);

            // The evaluator judges the condition impossible: acceptance waits, and the goal stays active until who decides for it answers.
            CliHarness.Ok("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the widget.", "--verdict", "1:impossible:The widget has no plug-ins to describe.");
            JsonElement shown = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
            Assert.Equal(("active", "impossible"), (shown.GetProperty("status").GetString(), shown.GetProperty("conditions")[0].GetProperty("evaluation").GetProperty("verdict").GetString()));
            Assert.False(shown.GetProperty("ready").GetBoolean());
            Refused(root, "condition 1 is impossible", "goal", "accept", goal, "--by", op, "--statement", "Done.");

            // No agent confirms it, the evaluator included; the sponsor does, and the goal ends as impossible.
            string subject = $"goal:{goal}:{shown.GetProperty("goal").GetProperty("hash").GetString()}";
            string confirm = CliHarness.WriteJson(scratch, "impossible.json", """{"status":"impossible","note":"Confirmed: the widget has no plug-ins."}""");
            foreach (string agent in new[] { builder, reviewer })
            {
                CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "records", "new", "goal-status", "--by", agent, "--subject", subject, "--body", confirm);
                Assert.Equal(1, refused.Code);
                Assert.Contains("goal-status.sponsor", refused.Out, StringComparison.Ordinal);
            }

            CliHarness.Ok("--estate", root, "--json", "records", "new", "goal-status", "--by", op, "--subject", subject, "--body", confirm);
            Assert.Contains("Status: impossible since", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);
            Assert.Contains($"{goal}  impossible  Describe the widget's plug-ins  sponsor Operator", CliHarness.Ok("--estate", root, "goal").Out, StringComparison.Ordinal);
            Refused(root, "ended as impossible", "goal", "resume", goal, "--by", op);
            Refused(root, "The goal ended as impossible; an ended goal is not accepted.", "goal", "accept", goal, "--by", op, "--statement", "Done.");
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    /// <summary>A goal as another machine shows it, under that machine's home.</summary>
    private static JsonElement Shown(string root, string home, string goal)
    {
        string? previous = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
        try
        {
            return CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previous);
        }
    }
}
