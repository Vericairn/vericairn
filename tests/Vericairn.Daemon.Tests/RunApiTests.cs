// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The worker's operations through the API against a running daemon, which supervises the run after the request returns: the
/// launchers listed, a run started, its cap and usage listed, and a stop to a harness that ignores it escalated and observed contained.
/// A client's absence is not a stop: the daemon keeps observing the run.
/// </summary>
[Collection("daemon")]
public class RunApiTests
{
    public RunApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public void OpenApiDescribesTheAdditiveExecutionContract()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (string request in new[] { "RunStartRequest", "GoalStepRequest" })
        {
            JsonElement properties = schemas.GetProperty(request).GetProperty("properties");
            foreach (string name in new[] { "configuration", "execution", "allocation", "contextPackage" })
            {
                Assert.True(properties.TryGetProperty(name, out _), request + "." + name);
            }
        }
        Assert.True(schemas.GetProperty("ProviderRow").GetProperty("properties").TryGetProperty("execution", out _));
        Assert.True(schemas.GetProperty("RunRow").GetProperty("properties").TryGetProperty("executionObserved", out _));
    }

    [Fact]
    public async Task ADaemonRunsTheWorkerAndTheApiListsLaunchersRunsAndStops()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-run-api-" + Records.Identity.Ulid.NewUlid().Value);
        Directory.CreateDirectory(scratch);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        var registry = new ProviderRegistry();
        registry.Register(new ReportedLauncher(new StandInLauncher()));
        registry.Register(new DirectoryWorkspaces());
        host.Service.Providers = registry;
        host.Service.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(1) };
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        string report = Path.Combine(scratch, "report.jsonl");
        try
        {
            using var api = new VericairnClient(host.Endpoint);
            string operatorDevice = Ok(api.DeviceCreate(new DeviceCreateRequest(op, "Operator's laptop", "file"))).Id;
            Ok(api.DeviceAdmit(new AdmitRequest(operatorDevice, op, "The first authority.")));
            Ok(api.DeviceAdmit(new AdmitRequest(Ok(api.DeviceCreate(new DeviceCreateRequest(builder, "Builder's device", "file"))).Id, op, "The builder's.")));
            new FileKeyStore(VericairnHome.Path()).Remove(operatorDevice);
            string goal = Ok(api.GoalPropose(new GoalProposeRequest(op, Json($$$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{{op}}}","budget":{"wallMinutes":60,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""")))).Id;
            Ok(api.GoalActivate(goal, new GoalActionRequest(op)));
            Ok(api.NewRecord(new NewRecordRequest("work-order", op, Json($$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}"""))));
            string assignment = Ok(api.Assign(new AssignRequest("W-1", builder, op, Json("""{"outcome":"A widget.","sources":[],"permittedChanges":["path:widget/"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."}""")))).Id;
            string resource = Ok(api.NewRecord(new NewRecordRequest("resource", op, Json("""{"kind":"repository","name":"widget","role":"Its source.","location":{"github":"Example/widget"},"visibility":"private"}""")))).Id;
            RepositoryClones.Register(host.Service.Session.Manifest.Estate, resource, scratch);

            ProviderRow launcher = Assert.Single(api.Launchers());
            Assert.Equal(("stand-in", "launcher", IAgentLauncher.Contract), (launcher.Provider, launcher.Kind, launcher.Contract));
            Assert.Contains(api.Providers(), p => p.Kind == "workspace");

            // The request returns once the harness is launched; the daemon observes it from then on.
            RunResponse started = api.RunStart(new RunStartRequest(assignment, "stand-in", builder, op, resource, Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("ignore-stop", report) }));
            Assert.True(started.Ok, started.Message);
            Assert.Equal("dispatched", started.Status);
            Assert.Equal("Example/widget, branch work/w-1", started.Workspace);
            Assert.Equal(60, started.Cap!.Value.GetProperty("wallMinutes").GetInt64());
            // The stand-in holds SIGTERM off once it has reported, so the stop waits for the report.
            RunRow row = await Until(api, started.Run, r => r.Observed && File.Exists(report));
            Assert.Equal("dispatched", row.Status);
            Assert.Equal(60, row.Charged.GetProperty("wallMinutes").GetInt64());
            LedgerRow ledger = Assert.Single(api.Runs().Goals);
            Assert.Equal((60L, 60L, 0L), (ledger.Units[0].Budget, ledger.Units[0].Spent, ledger.Units[0].Remaining));
            Assert.Equal(1, ledger.InFlight);

            RunStopResponse stopped = api.RunStop(started.Run, new RunStopRequest(op, "Through the API.", 1));
            Assert.True(stopped.Ok, stopped.Message);
            row = await Until(api, started.Run, r => r.Status == "known-failure");
            Assert.NotNull(row.Stop!.ContainedAt);
            Assert.NotNull(row.Stop.TransmittedAt);
            Assert.Null(row.Stop.AcknowledgedAt);
            Assert.NotNull(row.Stop.EscalatedAt);
            Assert.Equal("contained", row.Stop!.State);
            Assert.NotNull(row.Usage);
            Assert.Equal("not-found", api.RunStop("01M4TRNE1GYEK4P8MXQAB3C8Q4", new RunStopRequest(op)).Problem!.Code);
            Assert.Equal("refused", api.RunStart(new RunStartRequest(assignment, "claude-code", builder, op, resource)).Problem!.Code);

            RunResponse audited = api.RunStart(new RunStartRequest(assignment, "stand-in", builder, op, resource, Branch: "work/audit", Settings:
                new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("watch-codex-audit", Path.Combine(scratch, "audit.jsonl")) }));
            Assert.True(audited.Ok, audited.Message);
            RunRow watched = await Until(api, audited.Run, r => r.Status == "known-failure");
            Assert.Contains(watched.Signals!, s => s.Code == "credentials" && s.Severity == "critical" && s.Attention is not null);
            Assert.NotNull(watched.Stop!.ContainedAt);
            Assert.Contains(api.Progress().Json.GetProperty("runSignals").EnumerateArray(), r => r.GetProperty("run").GetString() == audited.Run);
            Assert.Equal(0, api.Check().Errors);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<RunRow> Until(VericairnClient api, string run, Func<RunRow, bool> condition)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            RunRow row = Assert.Single(api.Runs().Runs, r => r.Run == run);
            if (condition(row))
            {
                return row;
            }

            Assert.True(DateTimeOffset.UtcNow < until, $"Run {run} is {row.Status}.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem is null ? "no problem reported" : outcome.Problem.Message + "\n" + string.Join('\n', outcome.Problem.Findings.Select(f => $"{f.Severity} {f.Code} {f.Location}: {f.Message}")));
        return outcome.Written!;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>A launcher that starts the command its run names, as the stand-in harness.</summary>
    internal sealed class StandInLauncher : IAgentLauncher
    {
        public string Launcher => "stand-in";

        public ProviderDescriptor Descriptor { get; } = new("stand-in", IAgentLauncher.Contract, "implemented", ["launch"], ["Starts the tests' stand-in harness."], [new ProviderSetting("command", "The stand-in's command line, a JSON array.", true)]);

        public IReadOnlyList<string> Command(LaunchContext context) => JsonSerializer.Deserialize<List<string>>(context.Settings["command"])!;

        public IReadOnlyList<HarnessAction> ReadActions(string line)
        {
            if (!line.StartsWith('{'))
            {
                return [];
            }

            using JsonDocument document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("item", out JsonElement item))
            {
                return [];
            }

            // The API test observes synthetic invocations; production stream adapters are exercised by the runtime tests.
            return [new HarnessAction(item.GetProperty("id").GetString()!, "stand-in-tool",
                item.TryGetProperty("arguments", out JsonElement arguments) ? arguments.Clone() : item.Clone())];
        }
    }

    /// <summary>A workspace that is a directory: no repository is needed to start the stand-in.</summary>
    internal sealed class DirectoryWorkspaces : IWorkspaceProvider
    {
        public ProviderDescriptor Descriptor { get; } = new("directory", IWorkspaceProvider.Contract, "implemented", ["prepare"], ["A directory, for tests."]);

        public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory)
        {
            Directory.CreateDirectory(directory);
            return new PreparedWorkspace(directory, branch, "0123456789abcdef0123456789abcdef01234567", false);
        }
    }
}
