// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RunFileFailureTests
{
    [Theory]
    [InlineData("sharing", false)]
    [InlineData("access", false)]
    [InlineData("json", false)]
    [InlineData("sharing", true)]
    public async Task AnUnreadableStopRequestContainsTheHarnessInsteadOfDetachingIt(string failure, bool writesFail)
    {
        var retryClock = new WorkerClock();
        using var run = new SupervisedRun((estate, id, _) => new RunDirectory(estate, id)
        {
            BeforeReadOpen = () => throw Failure(failure),
            BeforeMove = () => { if (writesFail) throw new UnauthorizedAccessException("Synthetic write failure."); },
            RetryClock = retryClock,
            RetryWait = retryClock.Advance,
        }, "silent-ignore");
        run.Files.RequestStop(new StopRequest("2026-10-15T12:00:00Z", "test", "Keep this request."));
        RunOutcome outcome = Assert.IsType<RunOutcome>(await run.Observe().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken));

        Assert.Equal("worker", outcome.Stop!.RequestedBy);
        Assert.Contains("could not read the stop request", outcome.Stop.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic", outcome.Stop.Reason, StringComparison.Ordinal);
        Assert.NotNull(outcome.Stop.TransmittedAt);
        Assert.NotNull(outcome.Stop.EscalatedAt);
        Assert.NotNull(outcome.Stop.ContainedAt);
        if (writesFail)
        {
            Assert.Null(run.Files.Read());
            Assert.Contains("no stop file", outcome.Stop.Transmission, StringComparison.Ordinal);
        }
        else Assert.Equal(outcome.Stop, run.Files.Read()!.Stop);
        Assert.Equal("Keep this request.", run.Files.ReadStop()!.Reason);
        Assert.True(run.Supervisor.NativeResourcesClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistentStateWriteFailuresRaiseAttentionAndKeepSupervising(bool permanent)
    {
        var retryClock = new WorkerClock();
        int attentionAttempts = 0;
        var heartbeats = new HashSet<long>();
        using var run = new SupervisedRun((estate, id, report) => new RunDirectory(estate, id)
        {
            BeforeMove = () =>
            {
                string staged = Assert.Single(Directory.EnumerateFiles(new RunDirectory(estate, id).Path, "*.tmp"));
                using JsonDocument snapshot = JsonDocument.Parse(File.ReadAllText(staged));
                long heartbeat = snapshot.RootElement.GetProperty("heartbeats").GetInt64();
                heartbeats.Add(heartbeat);
                if (heartbeat >= 2) File.WriteAllText(report + ".finish", "release");
                if (permanent || heartbeat < 2) throw new UnauthorizedAccessException("Synthetic write failure.");
            },
            RetryClock = retryClock,
            RetryWait = retryClock.Advance,
        }, "close-output", signal =>
        {
            Assert.Contains("could not persist", signal.Summary, StringComparison.Ordinal);
            if (++attentionAttempts == 1) throw new IOException("Synthetic attention failure.");
            return "01M4TRNE1GYEK4P8MXQAB3C8Q4";
        });
        RunOutcome outcome = Assert.IsType<RunOutcome>(await run.Observe().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Null(outcome.Stop);
        Assert.Contains(0L, heartbeats); // The initial write failed after launch, before the readers started.
        Assert.Contains(1L, heartbeats); // The in-memory observation survived to the next heartbeat.
        Assert.Contains(2L, heartbeats);
        RunSignal warning = Assert.Single(outcome.Signals!);
        Assert.Equal("audit-unverified", warning.Code);
        Assert.Equal("warning", warning.Severity);
        Assert.NotNull(warning.Attention);
        Assert.True(attentionAttempts >= 2);
        Assert.DoesNotContain("Synthetic", warning.Summary, StringComparison.Ordinal);
        Assert.True(run.Supervisor.NativeResourcesClosed);
        if (permanent) Assert.Null(run.Files.Read());
        else Assert.Equal(warning, Assert.Single(run.Files.Read()!.Signals));
    }

    [Fact]
    public void AnUnreadableObservationAffectsOnlyItsOwnRunRow()
    {
        using var run = new RunEstate();
        string id = UnsupervisedIntent(run);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, id);
        files.Create();
        var stop = new StopRequest("2026-10-15T12:00:00Z", "test", "Retained stop.");
        files.RequestStop(stop);
        RunRow healthy = Assert.Single(run.Service.Runs().Runs, row => row.Run != id);
        // Corruption in state must not hide a separately readable, completely published request.
        File.WriteAllText(files.StateFile, "{");
        RunRow corrupt = run.Row(id);
        Assert.Equal(new RunStop(stop.RequestedAt, stop.RequestedBy, stop.Reason), corrupt.Stop);
        Assert.Contains(corrupt.Signals!, signal => signal.Code == "audit-unverified");
        foreach (bool request in new[] { false, true })
        foreach (string failure in new[] { "sharing", "access", "json" })
        {
            files.Write(new RunObservedState { Run = id, Signals = [new RunSignal("scope-read", "warning", stop.RequestedAt, "An earlier observation.")] });
            int reads = 0;
            var retryClock = new WorkerClock();
            run.Service.WorkerOptions = run.Service.WorkerOptions with
            {
                OpenRunDirectory = (estate, candidate) => candidate != id ? new RunDirectory(estate, candidate) : new RunDirectory(estate, candidate)
                {
                    BeforeReadOpen = () => { if (++reads > (request ? 1 : 0)) throw Failure(failure); },
                    RetryClock = retryClock,
                    RetryWait = retryClock.Advance,
                },
            };

            RunsResponse listed = run.Service.Runs();
            RunRow row = Assert.Single(listed.Runs, row => row.Run == id);
            Assert.Equal(JsonSerializer.Serialize(healthy, ProtocolJson.Options), JsonSerializer.Serialize(Assert.Single(listed.Runs, row => row.Run == healthy.Run), ProtocolJson.Options));
            Assert.False(row.Observed);
            RunSignal warning = Assert.Single(row.Signals!, signal => signal.Code == "audit-unverified");
            Assert.Equal("warning", warning.Severity);
            Assert.Contains(request ? "stop request" : "observation", warning.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic", warning.Summary, StringComparison.Ordinal);
            if (request) Assert.Contains(row.Signals!, signal => signal.Code == "scope-read");
            Assert.Equal("not-started", row.Status);
            Assert.Null(row.Usage);
            Assert.Equal(1, row.Charged.GetProperty("wallMinutes").GetInt64());
            // Canonical status never depends on local observation files, including unreadable ones.
            Assert.NotNull(run.Service.Status());
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStopWhoseObservationFailsKeepsTheRequestAndTheLastConfirmedFacts()
    {
        using var run = new RunEstate();
        string id = UnsupervisedIntent(run);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, id);
        files.Create();
        foreach (int failureAt in new[] { 1, 2, 3 })
        foreach (string failure in new[] { "sharing", "access", "json" })
        {
            var state = new RunObservedState { Run = id, Heartbeat = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), HeartbeatSeconds = 1 };
            files.Write(state);
            File.Delete(files.StopRequestFile);
            int reads = 0;
            var retryClock = new WorkerClock();
            run.Service.WorkerOptions = run.Service.WorkerOptions with
            {
                OpenRunDirectory = (estate, candidate) => new RunDirectory(estate, candidate)
                {
                    BeforeReadOpen = () =>
                    {
                        if (++reads >= failureAt) throw Failure(failure);
                        if (reads == 2)
                        {
                            StopRequest asked = files.ReadStop()!;
                            state.ExitedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                            state.Stop = new RunStop(asked.RequestedAt, asked.RequestedBy, asked.Reason, TransmittedAt: asked.RequestedAt, ContainedAt: state.ExitedAt);
                            files.Write(state);
                        }
                    },
                    RetryClock = retryClock,
                    RetryWait = retryClock.Advance,
                },
            };

            RunStopResponse response = run.Service.RunStop(id, new RunStopRequest(run.Estate.OperatorId, "Keep this request."));
            Assert.True(response.Ok, response.Message);
            Assert.Equal("Keep this request.", files.ReadStop()!.Reason);
            Assert.Equal(failureAt == 3 ? "contained" : "requested", response.State);
            Assert.Equal(failureAt == 3, response.Stop!.ContainedAt is not null);
            Assert.Contains("observation could not be read", response.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("no worker on this machine transmitted", response.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic", response.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnUnreadableWinningStopIsReportedAsUnavailableAndNeverOverwritten()
    {
        using var run = new RunEstate();
        string id = UnsupervisedIntent(run);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, id);
        files.Create();
        foreach (string failure in new[] { "sharing", "access", "json" })
        {
            files.RequestStop(new StopRequest("2026-10-15T12:00:00Z", "test", "First request."));
            var retryClock = new WorkerClock();
            run.Service.WorkerOptions = run.Service.WorkerOptions with
            {
                OpenRunDirectory = (estate, candidate) => new RunDirectory(estate, candidate)
                {
                    BeforeReadOpen = () => throw Failure(failure),
                    RetryClock = retryClock,
                    RetryWait = retryClock.Advance,
                },
            };
            RunStopResponse response = run.Service.RunStop(id, new RunStopRequest(run.Estate.OperatorId, "Later request."));
            Assert.False(response.Ok);
            Assert.Equal("unavailable", response.Problem!.Code);
            Assert.Null(response.Stop);
            Assert.Equal("First request.", files.ReadStop()!.Reason);
            Assert.DoesNotContain("Synthetic", response.Message, StringComparison.Ordinal);
        }
    }

    private static Exception Failure(string failure) => failure switch
    {
        "sharing" => new IOException("Synthetic sharing failure.", unchecked((int)0x80070020)),
        "access" => new UnauthorizedAccessException("Synthetic access failure."),
        _ => new JsonException("Synthetic malformed snapshot."),
    };

    private static string UnsupervisedIntent(RunEstate run)
    {
        RunResponse started = run.Start("generic", "claude", run.Report("original"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        run.WaitUntil(() => files.Read()?.Settled is not null, "the original run to finish settlement");
        AgentRuns.Run original = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonObject body = JsonNode.Parse(original.Intent.Body.GetRawText())!.AsObject();
        body["identity"] = "agent-run:" + Ulid.NewUlid().Value;
        body["cap"]!["wallMinutes"] = 1;
        return RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("operation", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), Subject: "assignment:" + run.Assignment))).Id;
    }

    private sealed class SupervisedRun : IDisposable
    {
        private readonly string scratch = Path.Combine(Path.GetTempPath(), "vericairn-file-failure-" + Ulid.NewUlid().Value);
        private readonly CancellationTokenSource abandon = new();
        private Task<RunOutcome?>? observation;

        internal SupervisedRun(Func<string, string, string, RunDirectory> directory, string scenario, Func<RunSignal, string?>? attention = null)
        {
            Directory.CreateDirectory(scratch);
            string estate = Ulid.NewUlid().Value;
            string id = Ulid.NewUlid().Value;
            string report = Path.Combine(scratch, "harness.jsonl");
            Files = new RunDirectory(estate, id);
            Files.Create();
            Supervisor = RunSupervisor.Start(id, FakeHarness.Command(scenario, report), scratch,
                new Dictionary<string, string>(WorkerEnvironment.Current(), WorkerEnvironment.Names) { ["VERICAIRN_STOP_FILE"] = Files.StopFile },
                new GenericLauncher(), directory(estate, id, report),
                new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(10), Grace = TimeSpan.Zero, Clock = new WorkerClock() },
                60, null, signalAttention: attention);
            using var changed = new FileSignal(scratch);
            changed.Until(() => File.Exists(report), "the harness readiness barrier", TestContext.Current.CancellationToken);
        }

        internal RunDirectory Files { get; }
        internal RunSupervisor Supervisor { get; }
        internal Task<RunOutcome?> Observe() => observation = Task.Run(() => Supervisor.Supervise(abandon.Token));

        public void Dispose()
        {
            abandon.Cancel();
            try
            {
                using Process process = Process.GetProcessById(Supervisor.ProcessId);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { }
            try { observation?.GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
            abandon.Dispose();
            Directory.Delete(Files.Path, recursive: true);
            Directory.Delete(scratch, recursive: true);
        }
    }
}
