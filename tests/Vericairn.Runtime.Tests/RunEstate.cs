// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// An estate ready for an agent run: at record format 2, an operator whose device admitted itself and was then taken off this machine,
/// the builder agent with an admitted device held here, a goal the operator sponsors and activated, a work order of the goal assigned
/// to the builder, and a repository adopted as a resource with its clone registered here. The service has the shipped launchers and
/// workspaces, a quick heartbeat and a short grace period; every harness is the stand-in.
/// </summary>
internal sealed class RunEstate : IDisposable
{
    public const string Brief = """{"outcome":"A widget.","sources":[],"permittedChanges":["path:widget/"],"prohibitedEffects":["no publication"],"workspace":["widget repository, a work branch"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["the tests pass"],"recovery":"Record a result."}""";

    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    public RunEstate(string budget = """{"wallMinutes":60,"attemptsPerStep":3}""", bool keepOperatorKey = false, WorkerOptions? options = null, string? brief = null)
    {
        TestPrerequisites.RequireGit();
        Estate = new TestEstate(recordFormat: 2);
        Scratch = Path.Combine(Path.GetTempPath(), "vericairn-run-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(Scratch);
        string temporary = Path.Combine(Scratch, "harness-temporary");
        Directory.CreateDirectory(temporary);
        Service = EstateService.Open(Estate.Root);
        var registry = new ProviderRegistry();
        registry.Register(new ReportedLauncher(new ClaudeCodeLauncher()));
        registry.Register(new ReportedLauncher(new CodexLauncher()));
        registry.Register(new ReportedLauncher(new GenericLauncher()));
        registry.Register(new GitWorktreeWorkspaces(FakeHarness.ScopeCommand()));
        Service.Providers = registry;
        Service.WorkerOptions = (options ?? new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(1) }) with
        {
            Environment = options?.Environment ?? new Dictionary<string, string>(WorkerEnvironment.Current(), WorkerEnvironment.Names)
            {
                ["CODEX_HOME"] = Path.Combine(Scratch, "stand-in-login"),
                // Outside-workspace fixtures must also be outside the harness's explicitly supplied temporary boundary.
                ["TMPDIR"] = temporary, ["TEMP"] = temporary, ["TMP"] = temporary,
            },
        };

        OperatorDevice = Ok(Service.DeviceCreate(new DeviceCreateRequest(Estate.OperatorId, "Operator's laptop", "file"))).Id;
        Ok(Service.DeviceAdmit(new AdmitRequest(OperatorDevice, Estate.OperatorId, "The first authority.")));
        BuilderDevice = Ok(Service.DeviceCreate(new DeviceCreateRequest(Estate.BuilderId, "Builder's device", "file"))).Id;
        Ok(Service.DeviceAdmit(new AdmitRequest(BuilderDevice, Estate.OperatorId, "The builder signs its attempts and results.")));
        if (!keepOperatorKey)
        {
            new FileKeyStore(VericairnHome.Path()).Remove(OperatorDevice);
        }

        Goal = Ok(Service.GoalPropose(new GoalProposeRequest(Estate.OperatorId, Json($$$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}}],"constraints":["no change outside the widget"],"kind":"finite","sponsor":"{{{Estate.OperatorId}}}","budget":{{{budget}}},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""")))).Id;
        Ok(Service.GoalActivate(Goal, new GoalActionRequest(Estate.OperatorId)));
        Ok(Service.NewRecord(new NewRecordRequest("work-order", Estate.OperatorId, Json($$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build the widget.","scope":["widget"],"exitQuestion":"Does the widget build and pass its tests?","inputs":[{"type":"goal","id":"{{Goal}}"}],"deliverables":["widget"]}"""))));
        Assignment = Ok(Service.Assign(new AssignRequest("W-1", Estate.BuilderId, Estate.OperatorId, Json(brief ?? Brief)))).Id;

        Clone = Path.Combine(Scratch, "widget");
        Directory.CreateDirectory(Clone);
        Git(Clone, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(Clone, "README.md"), "widget\n");
        Git(Clone, "add", "README.md");
        Git(Clone, "commit", "-q", "-m", "The first commit");
        string root = Git(Clone, "rev-parse", "HEAD");
        Resource = Ok(Service.NewRecord(new NewRecordRequest("resource", Estate.OperatorId, Json($$"""{"kind":"repository","name":"widget","role":"The widget's source.","location":{"rootCommit":"{{root}}"},"visibility":"private"}""")))).Id;
        RepositoryClones.Register(Service.Session.Manifest.Estate, Resource, Clone);
    }

    public TestEstate Estate { get; }

    public EstateService Service { get; private set; }

    public string Scratch { get; }

    public string OperatorDevice { get; }

    public string BuilderDevice { get; }

    public string Goal { get; }

    public string Assignment { get; }

    public string Clone { get; }

    public string Resource { get; }

    /// <summary>A report file for the stand-in.</summary>
    public string Report(string name) => Path.Combine(Scratch, name + ".jsonl");

    /// <summary>Starts a run through a launcher with the stand-in in a scenario.</summary>
    public RunResponse Start(string launcher, string scenario, string report, long? wallMinutes = null, IReadOnlyDictionary<string, string>? settings = null, string? by = null, string? branch = null)
    {
        var all = new Dictionary<string, string>(settings ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, report) };
        return Service.RunStart(new RunStartRequest(Assignment, launcher, Estate.BuilderId, by ?? Estate.OperatorId, Resource, Branch: branch, WallMinutes: wallMinutes, Settings: all));
    }

    /// <summary>Waits until a run is no longer in flight, and returns its row.</summary>
    public RunRow Settled(string run)
    {
        WaitUntil(() => Row(run).Status is "observed-success" or "known-failure" or "canceled", $"run {run} to settle");
        return Row(run);
    }

    public void WaitUntil(Func<bool> condition, string because = "the worker's expected state")
    {
        string[] directories = [Estate.Root, Scratch, .. Service.Runs().Runs
            .Select(row => new RunDirectory(Service.Session.Manifest.Estate, row.Run).Path).Where(Directory.Exists)];
        using var changed = new FileSignal(directories);
        changed.Until(condition, because, TestContext.Current.CancellationToken);
        Assert.True(condition(), because);
    }

    public RunRow Row(string run) => Assert.Single(Service.Runs().Runs, r => r.Run == run);

    /// <summary>Opens the estate again, as another process would after this one's supervision ended.</summary>
    public void Reopen()
    {
        ProviderRegistry providers = Service.Providers;
        WorkerOptions options = Service.WorkerOptions;
        Service.Dispose();
        Service = EstateService.Open(Estate.Root);
        Service.Providers = providers;
        Service.WorkerOptions = options;
    }

    public static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem is null ? "no problem reported" : outcome.Problem.Message + "\n" + string.Join('\n', outcome.Problem.Findings.Select(f => $"{f.Severity} {f.Code} {f.Location}: {f.Message}")));
        return outcome.Written!;
    }

    public static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static string Git(string directory, params string[] arguments)
    {
        TestPrerequisites.RequireGit();
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in GitIdentity.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(TestTiming.HangTimeoutMilliseconds), "The fixture process did not exit.");
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }

    public void Dispose()
    {
        Service.Dispose();
        Estate.Dispose();
        try
        {
            WorkspaceCleanup.Delete(Scratch);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
