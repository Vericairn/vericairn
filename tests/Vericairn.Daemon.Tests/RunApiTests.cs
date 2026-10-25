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
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        var registry = new ProviderRegistry();
        registry.Register(new ReportedLauncher(new StandInLauncher()));
        registry.Register(new DirectoryWorkspaces());
        host.Service.Providers = registry;
        host.Service.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(1) };
        ClientClock clock;
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
            // The controlled clock starts once setup has recorded the goal, so the run is never stamped before the records it relies on.
            clock = new ClientClock(DateTimeOffset.UtcNow);
            host.Service.WorkerOptions = host.Service.WorkerOptions with { Clock = clock };

            // The daemon stamps each record on the wall clock, and a settlement cannot record a containment after its own time,
            // so the controlled clock never runs ahead of the wall clock: an advance first waits until the wall clock has passed it.
            async Task AdvanceAsync(TimeSpan by)
            {
                DateTimeOffset target = clock.GetUtcNow() + by;
                TimeSpan ahead = target - DateTimeOffset.UtcNow;
                if (ahead > TimeSpan.Zero) await Task.Delay(ahead, TestContext.Current.CancellationToken);
                clock.Elapse(target - clock.GetUtcNow());
            }

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
            RunRow row = await Until(host, scratch, api, started.Run, r => r.Observed && File.Exists(report));
            Assert.Equal("dispatched", row.Status);
            Assert.Equal(60, row.Charged.GetProperty("wallMinutes").GetInt64());
            LedgerRow ledger = Assert.Single(api.Runs().Goals);
            Assert.Equal((60L, 60L, 0L), (ledger.Units[0].Budget, ledger.Units[0].Spent, ledger.Units[0].Remaining));
            Assert.Equal(1, ledger.InFlight);
            Assert.Equal("in-flight", api.RunSettle(started.Run, new(op)).Problem!.Code);

            Task<RunStopResponse> stopping = Task.Run(() => api.RunStop(started.Run, new RunStopRequest(op, "Through the API.", 1)), TestContext.Current.CancellationToken);
            await Until(host, scratch, api, started.Run, r => r.Stop?.TransmittedAt is not null);
            await AdvanceAsync(TimeSpan.FromSeconds(1));
            RunStopResponse stopped = await stopping.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Assert.True(stopped.Ok, stopped.Message);
            row = await Until(host, scratch, api, started.Run, r => r.Status == "known-failure");
            Assert.NotNull(row.Stop!.ContainedAt);
            Assert.NotNull(row.Stop.TransmittedAt);
            Assert.Null(row.Stop.AcknowledgedAt);
            Assert.NotNull(row.Stop.EscalatedAt);
            Assert.Equal("contained", row.Stop!.State);
            Assert.NotNull(row.Usage);
            Assert.Equal("not-found", api.RunStop("01M4TRNE1GYEK4P8MXQAB3C8Q4", new RunStopRequest(op)).Problem!.Code);
            Assert.Equal("already-settled", (await api.RunSettleAsync(started.Run, new(op), TestContext.Current.CancellationToken)).Problem!.Code);
            Assert.Equal("refused", api.RunStart(new RunStartRequest(assignment, "claude-code", builder, op, resource)).Problem!.Code);

            // The first run's settlement was recorded on the wall clock; the controlled clock catches up before the next run starts.
            TimeSpan behind = DateTimeOffset.UtcNow - clock.GetUtcNow();
            if (behind > TimeSpan.Zero) clock.Elapse(behind);
            RunResponse audited = api.RunStart(new RunStartRequest(assignment, "stand-in", builder, op, resource, Branch: "work/audit", Settings:
                new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("watch-codex-audit", Path.Combine(scratch, "audit.jsonl")) }));
            Assert.True(audited.Ok, audited.Message);
            await Until(host, scratch, api, audited.Run, r => r.Stop?.TransmittedAt is not null);
            await AdvanceAsync(TimeSpan.FromSeconds(1));
            RunRow watched = await Until(host, scratch, api, audited.Run, r => r.Status == "known-failure");
            Assert.Contains(watched.Signals!, s => s.Code == "credentials" && s.Severity == "critical" && s.Attention is not null);
            Assert.NotNull(watched.Stop!.ContainedAt);
            Assert.Contains(api.Progress().Json.GetProperty("runSignals").EnumerateArray(), r => r.GetProperty("run").GetString() == audited.Run);
            Assert.Equal(0, api.Check().Errors);

            host.Service.BeforeValidatingRecords = records =>
            {
                if (records.Any(r => r.Envelope.Type == "operation" && EstateModel.BodyString(r.Envelope, "status") == "observed-success"))
                    throw new IOException("Synthetic failed settlement.");
            };
            behind = DateTimeOffset.UtcNow - clock.GetUtcNow();
            if (behind > TimeSpan.Zero) clock.Elapse(behind);
            RunResponse pending = api.RunStart(new(assignment, "stand-in", builder, op, resource, Branch: "work/recovery",
                Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", Path.Combine(scratch, "recover.jsonl")) }));
            Assert.True(pending.Ok, pending.Message);
            var directory = new RunDirectory(host.Service.Session.Manifest.Estate, pending.Run);
            await Until(host, scratch, api, pending.Run, _ => directory.Read() is { ExitedAt: not null, Problem: not null });
            Assert.Equal("unresolved", api.RunSettle(pending.Run, new(op)).Problem!.Code);
            host.Service.BeforeValidatingRecords = null;
            var bound = new CancellationBoundApi(api, TestContext.Current.CancellationToken);
            RunSettleResponse recovered = await bound.RunSettleAsync(pending.Run, new(op), TestContext.Current.CancellationToken);
            Assert.True(recovered.Ok, recovered.Message);
            Assert.NotNull(recovered.Operation);
            Assert.Equal("observed-success", Assert.Single(api.Runs().Runs, r => r.Run == pending.Run).Status);
            Assert.Null(directory.Read()!.Problem);
            Assert.Equal(0, api.Check().Errors);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<RunRow> Until(DaemonHost host, string scratch, VericairnClient api, string run, Func<RunRow, bool> condition)
    {
        using var changed = new FileSignal(host.Service.Session.Root, scratch, new RunDirectory(host.Service.Session.Manifest.Estate, run).Path);
        RunRow? row = null;
        await changed.UntilAsync(() => condition(row = Assert.Single(api.Runs().Runs, r => r.Run == run)),
            "the run's expected observation", TestContext.Current.CancellationToken);
        Assert.True(condition(row!));
        return row!;
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
