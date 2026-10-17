// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

/// <summary>
/// vericairn worker, the worker contract on the command line (the estate's design/goals.md § 7): the launchers listed, an assignment
/// carried by a coding agent through Claude Code's launcher toward its exit question, the agent recording its attempt and result
/// through the MCP server the worker configured for it, the run's cap and usage listed, and a claim of done that stays a claim. Every
/// harness is the tests' stand-in; no coding agent runs.
/// </summary>
[Collection("cli")]
public sealed class WorkerJourneyTests : IDisposable
{
    private readonly IReadOnlyList<Vericairn.Runtime.Workers.IAgentLauncher> originalLaunchers;

    public WorkerJourneyTests()
    {
        _ = CliHarness.Run("--help");
        var registry = Vericairn.Runtime.Providers.ProviderRegistry.Current;
        originalLaunchers = registry.Launchers.ToArray();
        foreach (var launcher in originalLaunchers) registry.Register(new ReportedLauncher(launcher));
    }

    public void Dispose()
    {
        foreach (var launcher in originalLaunchers) Vericairn.Runtime.Providers.ProviderRegistry.Current.Register(launcher);
    }

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void ExplicitExecutionChoicesReachTheIntentAndTheHarnessFromTheCommandLine()
    {
        string root = CliHarness.TempDirectory("worker-execution");
        string scratch = CliHarness.TempDirectory("worker-execution-scratch");
        try
        {
            (string op, string builder, _, string assignment, string resource) = Estate(root, scratch);
            CliHarness.Outcome ran = CliHarness.Ok("--estate", root, "worker", "run", assignment, "--launcher", "claude-code", "--agent", builder, "--by", op, "--repository", resource,
                "--configuration", "claude-code", "--execution", "model=model-b", "--execution", "effort=low", "--input-tokens", "200", "--output-tokens", "100", "--context-bytes", "1000000",
                "--setting", "command=" + FakeHarness.Setting("execution", Path.Combine(scratch, "report.jsonl")));
            Assert.Contains("settled without the agent's attempt", ran.Out, StringComparison.Ordinal);
            Assert.Contains("settled without the agent's attempt", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            JsonElement run = Assert.Single(CliHarness.Ok("--estate", root, "--json", "worker", "runs").Json.GetProperty("runs").EnumerateArray());
            using EstateService api = EstateService.Open(root);
            var custody = new RunDirectory(api.Session.Manifest.Estate, run.GetProperty("run").GetString()!);
            using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(custody.ContextFile));
            JsonElement package = retained.RootElement.GetProperty("context");
            Assert.Equal("model-b", run.GetProperty("execution").GetProperty("requested").GetProperty("model").GetString());
            Assert.Equal(package.GetProperty("hash").GetString(), run.GetProperty("execution").GetProperty("contextPackage").GetString());
            Assert.Equal(package.GetProperty("bytes").GetInt64(), run.GetProperty("execution").GetProperty("allocation").GetProperty("context").GetProperty("bytes").GetInt64());
            Assert.Equal(1_000_000, run.GetProperty("execution").GetProperty("allocation").GetProperty("context").GetProperty("maxBytes").GetInt64());
            Assert.Equal(CanonicalJson.Serialize(run.GetProperty("execution")), CanonicalJson.Serialize(Vericairn.Records.Estate.AgentRuns.Find(api.Session.Load(), run.GetProperty("run").GetString()!)!.Intent.Body.GetProperty("execution")));
            Assert.Empty(api.Objects());
            Assert.Equal(200, run.GetProperty("execution").GetProperty("allocation").GetProperty("inputTokens").GetInt64());
            Assert.Equal("low", Assert.Single(run.GetProperty("executionObserved").GetProperty("reports").EnumerateArray()).GetProperty("effort").GetString());
            Assert.Contains("efforts:", CliHarness.Ok("--estate", root, "worker", "launchers").Out, StringComparison.Ordinal);
            Assert.Contains("--execution", CliHarness.Ok("goal", "step", "--help").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void WorkerRunsAndProgressShowRecordedObservationsAndAttention()
    {
        string root = CliHarness.TempDirectory("worker-watch");
        string scratch = CliHarness.TempDirectory("worker-watch-scratch");
        try
        {
            (string op, string builder, _, string assignment, string resource) = Estate(root, scratch);
            CliHarness.Outcome ran = CliHarness.Run("--estate", root, "worker", "run", assignment, "--launcher", "codex", "--agent", builder, "--by", op, "--repository", resource,
                "--setting", "command=" + FakeHarness.Setting("watch-codex-audit", Path.Combine(scratch, "audit.jsonl")));
            Assert.Contains("known-failure", ran.Out, StringComparison.Ordinal);
            string listed = CliHarness.Ok("--estate", root, "worker", "runs").Out;
            Assert.Contains("worker critical credentials", listed, StringComparison.Ordinal);
            Assert.Contains("ask ", listed, StringComparison.Ordinal);
            Assert.Contains("Worker observations of agent runs", CliHarness.Ok("--estate", root, "progress").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void ACodingAgentCarriesAnAssignmentTowardItsExitQuestionAndItsClaimOfDoneStaysAClaim()
    {
        string root = CliHarness.TempDirectory("worker");
        string scratch = CliHarness.TempDirectory("worker-scratch");
        try
        {
            (string op, string builder, string goal, string assignment, string resource) = Estate(root, scratch);

            string launchers = CliHarness.Ok("--estate", root, "worker", "launchers").Out;
            Assert.Contains("claude-code  claude-code-cli  vericairn.launcher.agent/1  implemented", launchers, StringComparison.Ordinal);
            Assert.Contains("codex  codex-cli  vericairn.launcher.agent/1  implemented", launchers, StringComparison.Ordinal);
            Assert.Contains("generic  generic-process  vericairn.launcher.agent/1  implemented  (generic process launch is not complete agent support)", launchers, StringComparison.Ordinal);
            Assert.Contains("vericairn.launcher.agent/1", CliHarness.Ok("--estate", root, "release", "providers").Out, StringComparison.Ordinal);

            // Codex is refused before anything is recorded when its goals feature is off; the person learns why.
            CliHarness.Outcome off = CliHarness.Run("--estate", root, "worker", "run", assignment, "--launcher", "codex", "--agent", builder, "--by", op, "--repository", resource, "--setting", "command=" + FakeHarness.Setting("codex-goals-off", Path.Combine(scratch, "off.jsonl")));
            Assert.Equal(2, off.Code);
            Assert.Contains("Codex's goals feature is not enabled here", off.Err, StringComparison.Ordinal);
            Assert.Contains("--detach leaves the run to the daemon", CliHarness.Run("--estate", root, "worker", "run", assignment, "--launcher", "claude-code", "--agent", builder, "--by", op, "--repository", resource, "--detach").Err, StringComparison.Ordinal);

            // Through Claude Code's launcher the agent records its attempt and its result with the MCP server the worker configured.
            string report = Path.Combine(scratch, "claude.jsonl");
            CliHarness.Outcome ran = CliHarness.Run("--estate", root, "worker", "run", assignment, "--launcher", "claude-code", "--agent", builder, "--by", op, "--repository", resource, "--setting", "command=" + FakeHarness.Setting("mcp:" + assignment, report));
            Assert.True(ran.Code == 0, ran.Out + ran.Err + string.Join('\n', File.Exists(report) ? File.ReadAllLines(report).Select(l => l[..Math.Min(l.Length, 400)]) : []));
            Assert.Contains("dispatched  widget, branch work/w-1", ran.Out, StringComparison.Ordinal);
            Assert.Contains("cap wallMinutes 120, attemptsPerStep 2", ran.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("settled without the agent's attempt", ran.Out, StringComparison.Ordinal);
            Assert.Contains("observed-success  claude-code as " + builder, ran.Out, StringComparison.Ordinal);
            Assert.Contains("tokens 600", ran.Out, StringComparison.Ordinal);

            string observed = CliHarness.Ok("--estate", root, "work", "observe", "W-1").Out;
            Assert.Contains("result", observed, StringComparison.Ordinal);
            Assert.Contains("completed, awaiting review: The widget builds and its tests pass. Done.", observed, StringComparison.Ordinal);
            JsonElement shown = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json;
            Assert.Equal("active", shown.GetProperty("status").GetString());
            Assert.False(shown.GetProperty("ready").GetBoolean());

            // The runs list gives each run's cap, its usage and its charge, and the goal's ledger.
            JsonElement runs = CliHarness.Ok("--estate", root, "--json", "worker", "runs").Json;
            JsonElement run = Assert.Single(runs.GetProperty("runs").EnumerateArray());
            Assert.Equal("observed-success", run.GetProperty("status").GetString());
            Assert.Equal(120, run.GetProperty("cap").GetProperty("wallMinutes").GetInt64());
            Assert.Equal(600, run.GetProperty("usage").GetProperty("tokens").GetInt64());
            JsonElement ledger = Assert.Single(runs.GetProperty("goals").EnumerateArray());
            Assert.Equal(goal, ledger.GetProperty("goal").GetString());
            Assert.Equal(run.GetProperty("charged").GetProperty("wallMinutes").GetInt64(), ledger.GetProperty("units")[0].GetProperty("spent").GetInt64());
            string listed = CliHarness.Ok("--estate", root, "worker", "runs").Out;
            Assert.Contains("  cap wallMinutes 120, attemptsPerStep 2", listed, StringComparison.Ordinal);
            Assert.Contains($"goal {goal}  active  Build the widget", listed, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AStopNoWorkerObservesIsReportedAsRequestedOnTheCommandLine()
    {
        string root = CliHarness.TempDirectory("worker-stop");
        string scratch = CliHarness.TempDirectory("worker-stop-scratch");
        int harness = 0;
        try
        {
            (string op, string builder, _, string assignment, string resource) = Estate(root, scratch);
            string report = Path.Combine(scratch, "lingering.jsonl");
            string run;
            using (EstateService service = EstateService.Open(root))
            {
                service.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100) };
                RunResponse started = service.RunStart(new RunStartRequest(assignment, "generic", builder, op, resource, Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("ack-stop", report) }));
                Assert.True(started.Ok, started.Message);
                run = started.Run;
                DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
                while (!File.Exists(report))
                {
                    Assert.True(DateTimeOffset.UtcNow < until, "The stand-in did not start.");
                    Thread.Sleep(50);
                }

                harness = FakeHarness.Reports(report)[0]["process"]!.GetValue<int>();
            }

            using (EstateService observer = EstateService.Open(root))
            {
                DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
                while (Assert.Single(observer.Runs().Runs, row => row.Run == run).Observed)
                {
                    Assert.True(DateTimeOffset.UtcNow < until, "The disposed supervisor remained observed.");
                    Thread.Sleep(50);
                }
            }

            CliHarness.Outcome stopped = CliHarness.Ok("--estate", root, "worker", "stop", run, "--by", op, "--reason", "Nobody watches it.");
            Assert.Contains($"run {run}  stop requested", stopped.Out, StringComparison.Ordinal);
            Assert.Contains("transmitted: not observed", stopped.Out, StringComparison.Ordinal);
            Assert.Contains("A stop that is not observed stays requested", stopped.Out, StringComparison.Ordinal);
            Assert.Contains("stop requested at", CliHarness.Ok("--estate", root, "worker", "runs").Out, StringComparison.Ordinal);
            Assert.Equal(2, CliHarness.Run("--estate", root, "worker", "stop", "01M4TRNE1GYEK4P8MXQAB3C8Q4", "--by", op).Code);
        }
        finally
        {
            if (harness != 0)
            {
                try
                {
                    Process.GetProcessById(harness).Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                    // It ended already.
                }
            }

            CliHarness.Delete(root, scratch);
        }
    }

    /// <summary>An estate with the operator's key off this machine, the builder's admitted here, a goal activated, its work assigned to the builder, and the widget repository adopted with its clone registered.</summary>
    internal static (string Operator, string Builder, string Goal, string Assignment, string Resource) Estate(string root, string scratch)
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Workers.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
        string operatorDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator's laptop", "--custody", "file").Id;
        CliHarness.Ok("--estate", root, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
        string builderDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Builder's device", "--custody", "file").Id;
        CliHarness.Ok("--estate", root, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's.");
        new FileKeyStore(VericairnHome.Path()).Remove(operatorDevice);
        string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Build the widget", "--objective", "Ship the widget.", "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""", "--budget", "wallMinutes=120", "--budget", "attemptsPerStep=2").Id;
        CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
        CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json", $$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Does the widget build and pass its tests?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}"""));
        string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", builder, "--by", op, "--brief", CliHarness.WriteJson(scratch, "brief.json", """{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["widget, a work branch"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["the tests pass"],"recovery":"Record a result."}""")).Id;

        string clone = Path.Combine(scratch, "widget");
        Directory.CreateDirectory(clone);
        Git(clone, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(clone, "README.md"), "widget\n");
        Git(clone, "add", "README.md");
        Git(clone, "commit", "-q", "-m", "The first commit");
        string resource = CliHarness.Ok("--estate", root, "--json", "sources", "adopt", clone, "--by", op, "--name", "widget", "--role", "The widget's source.").Json.GetProperty("resource").GetProperty("id").GetString()!;
        CliHarness.Ok("--estate", root, "--json", "sources", "clone", clone, "--resource", resource);
        return (op, builder, goal, assignment, resource);
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
}
