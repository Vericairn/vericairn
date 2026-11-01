// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-82: a run whose supervising worker died is never observed exiting. Once that worker's process and the harness's boundary
/// are gone and nothing was observed for longer than the stall limit, worker stop, worker settle or a runtime start settles it as
/// exited without observation, once, releasing its branch, keeping its workspace and charging the usage observed up to its last
/// heartbeat. A run that may still be observed keeps today's answer. The supervisor here is a real process the test kills.
/// </summary>
[Collection("home")]
public sealed class LostSupervisorSettlementTests
{
    private const string Budget = """{"wallMinutes":60,"tokens":5000,"attemptsPerStep":3,"concurrency":2}""";
    private const string CurrencyBudget = """{"wallMinutes":60,"tokens":5000,"currency":{"amount":1000,"unit":"USD"},"attemptsPerStep":3,"concurrency":2}""";
    private const string Draft = "uncommitted work for the next attempt\n";

    [Theory]
    [InlineData("stop")]
    [InlineData("settle")]
    [InlineData("startup")]
    public void ARunWhoseSupervisorWasKilledSettlesOnceFreeingItsBranchKeepingItsWorkspaceAndChargingItsObservedUsage(string path)
    {
        using var run = new RunEstate(Budget);
        Lost lost = Abandon(run, "killed-supervisor");
        string draft = Path.Combine(lost.Workspace, "draft.txt");
        File.WriteAllText(draft, Draft);
        int harness = lost.State.ProcessId!.Value;

        // Evidence that the run may still be observed keeps today's answer on every path: the recorded worker running
        // (this process, started before the launch), the harness running, or a heartbeat within the stall limit.
        foreach ((string Case, int Supervisor, int Harness, TimeSpan Heartbeat) live in new[]
        {
            ("supervisor", Environment.ProcessId, harness, TimeSpan.FromMinutes(3)),
            ("harness", lost.Supervisor, Environment.ProcessId, TimeSpan.FromMinutes(3)),
            ("heartbeat", lost.Supervisor, harness, TimeSpan.FromHours(1) - TimeSpan.FromMinutes(1)),
        })
        {
            Write(lost, live.Supervisor, live.Harness, live.Heartbeat, tokens: 1200);
            string? message = Act(run, lost, path);
            Assert.True(run.Row(lost.Run).Status == "dispatched", live.Case);
            Assert.Equal(2, Revisions(run, lost.Run));
            if (message is not null)
                Assert.Contains(live.Case == "heartbeat" ? "within its stall limit" : live.Case == "harness" ? "harness is still running" : "worker process that supervised it is still running", message, StringComparison.Ordinal);
            if (path == "stop") Assert.Contains("stays in flight, charged at its cap", message, StringComparison.Ordinal);
        }

        Write(lost, lost.Supervisor, harness, TimeSpan.FromMinutes(3), tokens: 1200);
        string? settledMessage = Act(run, lost, path);
        if (settledMessage is not null) Assert.Contains("exited without observation", settledMessage, StringComparison.Ordinal);

        RunRow row = run.Row(lost.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal(3, Revisions(run, lost.Run));
        // Charged what was observed up to the last heartbeat, never the cap: three of ten wall minutes and 1200 of 5000 tokens.
        Assert.Equal(3, row.Charged.GetProperty("wallMinutes").GetInt64());
        Assert.Equal(1200, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Empty(row.Usage!.Value.GetProperty("unreported").EnumerateArray());
        JsonElement settlement = AgentRuns.Find(run.Estate.Load(), lost.Run)!.Latest.Body;
        Assert.StartsWith("Exited without observation.", settlement.GetProperty("observed").GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains(settlement.GetProperty("evidence").EnumerateArray(),
            e => e.GetProperty("summary").GetString()!.Contains("Its exit was not observed", StringComparison.Ordinal));
        Assert.Contains(row.Signals!, s => s.Code == "audit-unverified" && s.Summary.Contains("exited without observation", StringComparison.Ordinal));
        Assert.Contains("unverified", row.Scope!.Problem, StringComparison.Ordinal);
        if (path == "stop")
        {
            // The stop is recorded as requested: no worker transmitted it, and no containment was observed.
            Assert.Equal(run.Estate.OperatorId, row.Stop!.RequestedBy);
            Assert.Null(row.Stop.TransmittedAt);
            Assert.Null(row.Stop.ContainedAt);
        }
        else Assert.Null(row.Stop);
        Assert.Equal("known-failure", lost.Directory.Read()!.Settled);
        Assert.Equal(0, Assert.Single(run.Service.Runs().Goals).InFlight);
        Assert.Equal(Draft, File.ReadAllText(draft));

        // Settled exactly once: stop, settle and a runtime start leave the settlement as it is.
        Assert.False(run.Service.RunStop(lost.Run, new RunStopRequest(run.Estate.OperatorId)).Ok);
        Assert.Equal("already-settled", run.Service.RunSettle(lost.Run, new(run.Estate.OperatorId)).Problem!.Code);
        run.Restart(lost.Options);
        Assert.Equal(3, Revisions(run, lost.Run));

        // The branch is free, and the next attempt runs in the kept workspace with its uncommitted file.
        string next = run.Report("next-attempt");
        RunResponse again = run.Start("generic", "generic", next, wallMinutes: 1);
        Assert.True(again.Ok, again.Message);
        Assert.Equal("observed-success", run.Settled(again.Run).Status);
        Assert.Equal(lost.Workspace, Assert.Single(FakeHarness.Reports(next))["directory"]!.GetValue<string>());
        Assert.Equal(Draft, File.ReadAllText(draft));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(9000L)]
    [InlineData(null)]
    public void UsageObservedBeyondTheCapIsChargedAtMostTheCapAndUnreportedUsageAtTheCap(long? tokens)
    {
        using var run = new RunEstate(CurrencyBudget);
        Lost lost = Abandon(run, "beyond-the-cap");
        Write(lost, lost.Supervisor, lost.State.ProcessId!.Value, TimeSpan.FromMinutes(30), tokens);
        RunSettleResponse settled = run.Service.RunSettle(lost.Run, new(run.Estate.OperatorId));
        Assert.True(settled.Ok, settled.Message);
        Assert.Equal("known-failure", settled.Status);
        RunRow row = run.Row(lost.Run);
        Assert.Equal(10, row.Usage!.Value.GetProperty("wallMinutes").GetInt64());
        Assert.Equal(10, row.Charged.GetProperty("wallMinutes").GetInt64());
        Assert.Equal(5000, row.Charged.GetProperty("tokens").GetInt64());
        // No shipped launcher's price is retained while a run is observed, so currency is unknown and charged at its cap.
        Assert.Equal(1000, row.Charged.GetProperty("currency").GetProperty("amount").GetInt64());
        Assert.Equal(tokens is null ? ["tokens", "currency"] : ["currency"], row.Usage.Value.GetProperty("unreported").EnumerateArray().Select(u => u.GetString()));
        Assert.Equal(tokens is not null, row.Usage.Value.TryGetProperty("tokens", out _));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ProcessIdentityCountsOnlyAnObservedAbsenceOrAnIdReusedAfterTheLaunch()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-lost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            int killed = Killed(Path.Combine(scratch, "killed.jsonl"), condition =>
            {
                using var changed = new FileSignal(scratch);
                changed.Until(condition, "the stand-in supervisor", TestContext.Current.CancellationToken);
            });
            Assert.True(RunProcesses.Gone(killed, now));
            Assert.True(RunProcesses.BoundaryGone(killed, now));
            // This process started before now, so it may be the recorded one; one recorded a day before it started was another.
            Assert.False(RunProcesses.Gone(Environment.ProcessId, now));
            Assert.False(RunProcesses.BoundaryGone(Environment.ProcessId, now));
            using Process current = Process.GetCurrentProcess();
            Assert.True(RunProcesses.Gone(Environment.ProcessId, current.StartTime.ToUniversalTime().AddDays(-1)));
            // An unknown id is never an absence.
            Assert.False(RunProcesses.Gone(0, now));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private sealed record Lost(string Run, RunDirectory Directory, RunObservedState State, DateTimeOffset LaunchedAt, int Supervisor, string Workspace, WorkerOptions Options);

    // A run whose worker observed its exit is turned back into the snapshot its worker last wrote before it was killed: no exit,
    // no settlement, a supervisor process that is gone. Its harness really exited. The worker's clock then reads an hour after
    // the launch, beyond the stall limit, while the processes are the machine's own.
    private static Lost Abandon(RunEstate run, string name)
    {
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        string report = run.Report(name);
        RunResponse started = run.Start("generic", "generic", report, wallMinutes: 10);
        Assert.True(started.Ok, started.Message);
        Assert.Equal("dispatched", started.Status);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        run.WaitUntil(() => directory.Read() is { ExitedAt: not null, Problem: not null, Settled: null }, "the refused settlement");
        int supervisor = Killed(run.Report(name + "-supervisor"), condition => run.WaitUntil(condition, "the stand-in supervisor"));
        // Dispose joins the original supervision, which reaped the harness, before the snapshot is rewritten; the restart
        // below then recovers nothing from an exit.
        run.Service.Dispose();
        RunObservedState state = directory.Read()!;
        DateTimeOffset launched = DateTimeOffset.Parse(state.LaunchedAt!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        JsonObject reported = Assert.Single(FakeHarness.Reports(report));
        Assert.Equal(state.ProcessId, reported["process"]!.GetValue<int>());
        WorkerOptions options = run.Service.WorkerOptions with { Clock = new WorkerClock(launched.AddHours(1)) };
        var lost = new Lost(started.Run, directory, state, launched, supervisor, reported["directory"]!.GetValue<string>(), options);
        Write(lost, supervisor, state.ProcessId!.Value, TimeSpan.FromHours(1), tokens: null);
        run.Restart(options);
        return lost;
    }

    private static void Write(Lost lost, int supervisor, int harness, TimeSpan heartbeat, long? tokens)
    {
        RunObservedState state = lost.State;
        state.ExitCode = null;
        state.ExitedAt = null;
        state.Outcome = null;
        state.Scope = null;
        state.SettlementPrepared = false;
        state.Settled = null;
        state.Problem = null;
        state.Stop = null;
        state.Supervisor = supervisor;
        state.ProcessId = harness;
        state.Heartbeat = RecordParser.FormatTimestamp(lost.LaunchedAt + heartbeat);
        state.Heartbeats = 90;
        state.Tokens = tokens;
        lost.Directory.Write(state);
    }

    // The path under test: worker stop, worker settle, or a runtime start. Stop and settle report their message.
    private static string? Act(RunEstate run, Lost lost, string path)
    {
        if (path == "stop")
        {
            RunStopResponse asked = run.Service.RunStop(lost.Run, new RunStopRequest(run.Estate.OperatorId, "The shell that supervised it closed."));
            Assert.True(asked.Ok, asked.Message);
            Assert.Equal("requested", asked.State);
            return asked.Message;
        }

        if (path == "settle")
        {
            RunSettleResponse settled = run.Service.RunSettle(lost.Run, new(run.Estate.OperatorId));
            Assert.True(settled.Ok || settled.Problem!.Code == "in-flight", settled.Message);
            return settled.Message;
        }

        run.Restart(lost.Options);
        return null;
    }

    // A real supervisor stand-in: started, observed running, then killed and reaped.
    private static int Killed(string ready, Action<Func<bool>> waitUntil)
    {
        List<string> command = FakeHarness.Command("ignore-stop", ready);
        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in command.Skip(1)) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        waitUntil(() => File.Exists(ready));
        int id = process.Id;
        process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(TestTiming.HangTimeoutMilliseconds), "The killed stand-in supervisor did not exit.");
        return id;
    }

    private static int Revisions(RunEstate run, string id) => AgentRuns.Find(run.Estate.Load(), id)!.Revisions.Count;

    private static void RefuseSettlement(IReadOnlyList<AuthoredRecord> records)
    {
        if (records.Any(r => r.Envelope.Type == "operation" && EstateModel.BodyString(r.Envelope, "status") is "observed-success" or "known-failure"))
            throw new IOException("Synthetic settlement validation failure.");
    }
}
