// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The worker contract, vericairn.worker/1 (the estate's design/goals.md § 7), through each launcher with the stand-in harness: the
/// workspace prepared on a work branch, never the shared clone; the brief exported into it; the MCP server configured for the agent;
/// an environment with no ambient credential; the cap recorded before the run and the observed usage after; nothing accepted.
/// </summary>
[Collection("home")]
public class WorkerTests
{
    [Fact]
    public void TheMachineWorkspaceRootAlsoAppliesToAHostSuppliedLauncher()
    {
        using var run = new RunEstate();
        var store = new WorkerConfigurationStore();
        string? previous = store.ReadWorkspaceRoot();
        try
        {
            string root = Path.Combine(run.Scratch, "runs");
            store.WriteWorkspaceRoot(root);
            string report = run.Report("configured-generic");
            RunResponse started = run.Start("generic", "claude", report);
            Assert.True(started.Ok, started.Message);
            Assert.Equal("observed-success", run.Settled(started.Run).Status);
            string workspace = Assert.Single(FakeHarness.Reports(report))["directory"]!.GetValue<string>();
            Assert.Equal(Path.Combine(Vericairn.Packs.Software.HarnessWorkspace.Canonical(root), run.Service.Session.Manifest.Estate, "work-w-1"), workspace);
        }
        finally
        {
            if (previous is not null) store.WriteWorkspaceRoot(previous);
            else File.Delete(Path.Combine(VericairnHome.Path(), "worker", "workspaces.json"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWindowsJobThatFinishesDrainingAfterLeaderExitDoesNotTurnASuccessfulRunIntoAFailure(bool inventoryUnavailable)
    {
        int inventories = 0;
        using var run = new RunEstate(options: new WorkerOptions
        {
            Clock = new WorkerClock(),
            Heartbeat = TimeSpan.FromMilliseconds(20),
            WindowsJobInventory = () => Interlocked.Increment(ref inventories) <= 2
                ? inventoryUnavailable ? throw new System.ComponentModel.Win32Exception(5) : 1u
                : 0u,
        });
        RunResponse started = run.Start("generic", "claude", run.Report("pending-job-exit"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        string summary = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetProperty("observed").GetProperty("summary").GetString()!;
        Assert.True(settled.Status == "observed-success", summary);
        Assert.Null(settled.Stop);
        Assert.Contains("exited with code 0", summary, StringComparison.Ordinal);
        Assert.True(inventories >= 3);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AWindowsJobThatOutlivesItsLeadersShutdownGraceIsStoppedAndReservedUntilObservedEmpty()
    {
        var clock = new WorkerClock();
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        using var repeated = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        int inventories = 0;
        int empty = 0;
        using var run = new RunEstate(options: new WorkerOptions
        {
            Clock = clock,
            Heartbeat = TimeSpan.FromMilliseconds(20),
            Grace = TimeSpan.FromSeconds(1),
            WindowsJobInventory = () =>
            {
                if (Interlocked.Increment(ref inventories) == 2)
                {
                    repeated.Set();
                    Assert.True(resume.Wait(TimeSpan.FromSeconds(20), cancellation), "The boundary inspection was not released.");
                }

                return Volatile.Read(ref empty) == 0 ? 1u : 0u;
            },
        });
        string report = run.Report("surviving-job");
        RunResponse started = run.Start("generic", "child-survives-root", report);
        System.Diagnostics.Process? child = null;
        try
        {
            Assert.True(started.Ok, started.Message);
            WaitUntil(() => File.Exists(report) && File.Exists(report + ".child"));
            child = System.Diagnostics.Process.GetProcessById(Assert.Single(FakeHarness.Reports(report + ".child"))["process"]!.GetValue<int>());
            // End only the ready leader. Its child ignores this file and remains in the actual native boundary to signal.
            new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).TransmitStop(new StopRequest("2026-10-15T12:00:00Z", "test"));
            Assert.True(repeated.Wait(TimeSpan.FromSeconds(20), cancellation), "The boundary inventory was not retried.");
            Assert.Null(run.Row(started.Run).Stop);
            Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
            clock.Advance(TimeSpan.FromSeconds(1));
            resume.Set();
            WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
            clock.Advance(TimeSpan.FromSeconds(1));
            WaitUntil(() => run.Row(started.Run).Stop?.EscalatedAt is not null);
            Assert.Null(run.Row(started.Run).Stop!.ContainedAt);
            Assert.Equal("dispatched", run.Row(started.Run).Status);
            Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
            WaitUntil(() => child.HasExited);
            Volatile.Write(ref empty, 1);
            RunRow settled = run.Settled(started.Run);
            Assert.Equal("known-failure", settled.Status);
            Assert.NotNull(settled.Stop!.ContainedAt);
            Assert.Equal(0, run.Service.Check().Errors);
        }
        finally
        {
            Volatile.Write(ref empty, 1);
            resume.Set();
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                }

                child.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStopRechecksTheRequestingDeviceEvenWhenItsPrincipalStillHasAnotherAdmittedDevice(bool rotate)
    {
        using var run = new RunEstate(keepOperatorKey: true);
        string backup = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(run.Estate.OperatorId, "Other operator device", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(backup, run.Estate.OperatorId, "The operator still has authority.")));
        Except(run, DateTimeOffset.UtcNow.AddHours(1), "Exercise a device-signed stop with a stand-in.");
        string report = run.Report("device-stop");
        RunResponse started = run.Start("generic", "ack-stop", report);
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(report));
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        try
        {
            DeviceStanding device = run.Service.Standing(run.OperatorDevice)!;
            using EstateService.RequestDeviceScope request = run.Service.ForDeviceRequest(device.Identity, device.PublicKey);
            RunEstate.Ok(rotate
                ? run.Service.DeviceRotate(new DeviceRotateRequest(device.Identity))
                : run.Service.DeviceRevoke(new RevokeRequest(device.Identity, run.Estate.OperatorId, "Revoked after authentication.")));
            RunStopResponse refused = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "The old request must not queue this stop.", 0));
            Assert.False(refused.Ok);
            Assert.Equal("denied", refused.Problem!.Code);
            Assert.Equal(request.Refusal!.Message, refused.Problem.Message);
            Assert.Null(directory.ReadStop());
            Assert.False(File.Exists(directory.StopFile));
            Assert.Equal("dispatched", run.Row(started.Run).Status);
            Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
        }
        finally
        {
            DeviceStanding device = run.Service.Standing(backup)!;
            using EstateService.RequestDeviceScope request = run.Service.ForDeviceRequest(device.Identity, device.PublicKey);
            Assert.True(run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Stop with the other admitted device.", 0)).Ok);
            Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        }

        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void DispatchAndStallWritesRecheckAnActiveRequestWithoutLosingTheWorkersReservationOrOwnership()
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(keepOperatorKey: true, options: new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(50), Grace = TimeSpan.FromSeconds(1) });
        string backup = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(run.Estate.OperatorId, "Other operator device", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new AdmitRequest(backup, run.Estate.OperatorId, "The runtime can still record its observations.")));
        Except(run, DateTimeOffset.UtcNow.AddHours(1), "Exercise guarded worker writes with a stand-in.");
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            BeforeDispatchRecord = () => RunEstate.Ok(run.Service.DeviceRevoke(new RevokeRequest(run.OperatorDevice, run.Estate.OperatorId, "Revoked after launch and before dispatch storage."))),
        };
        DeviceStanding device = run.Service.Standing(run.OperatorDevice)!;
        RunResponse started;
        using (EstateService.RequestDeviceScope request = run.Service.ForDeviceRequest(device.Identity, device.PublicKey))
        {
            started = run.Start("generic", "silent-ignore", run.Report("guarded-writes"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "60" });
            Assert.True(started.Ok, started.Message); // A live launch stays owned and reserved even when its record write is denied.
            Assert.Equal("not-started", started.Status);
            Assert.Equal("denied", request.Refusal!.Code);
            WaitUntil(() => File.Exists(run.Report("guarded-writes")));
            clock.Advance(TimeSpan.FromSeconds(60));
            WaitUntil(() => run.Row(started.Run).Liveness?.AttentionAt is not null);
            Assert.Empty(run.Service.Records("ask"));
            Assert.Single(AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions);
            Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
        }

        // Once the request ends, continuing supervision is the runtime's work, not another use of the revoked credential.
        WaitUntil(() => run.Row(started.Run).Liveness?.Attention is not null);
        clock.Advance(TimeSpan.FromSeconds(60));
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        clock.Advance(TimeSpan.FromSeconds(1));
        RunRow settled = run.Settled(started.Run);
        Assert.NotNull(settled.Stop!.ContainedAt);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(["not-started", "dispatched", "known-failure"], recorded.Revisions.Select(r => EstateModel.BodyString(r, "status")));
        Assert.Single(run.Service.Records("ask"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex", "codex-incomplete")]
    [InlineData("codex", "codex-overflow")]
    [InlineData("codex", "codex-unfinished")]
    [InlineData("claude-code", "claude-incomplete")]
    public void AnyUnaccountedUsageChargesTheWholeTokenCap(string launcher, string scenario)
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":5000}""");
        RunResponse started = run.Start(launcher, scenario, run.Report("unknown-usage"));
        Assert.True(started.Ok, started.Message);
        RunRow row = run.Settled(started.Run);
        Assert.False(row.Usage!.Value.TryGetProperty("tokens", out _));
        Assert.Contains(row.Usage.Value.GetProperty("unreported").EnumerateArray(), u => u.GetString() == "tokens");
        Assert.Equal(5000, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("claude", "observed-success", 1734)]
    [InlineData("claude-fails", "known-failure", 55)]
    public void AFailedDispatchWriteIsRecoveredBeforeSettlementSoUsageIsNeverReleasedAsUnstarted(string scenario, string status, long tokens)
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":5000}""");
        run.Service.WorkerOptions = run.Service.WorkerOptions with { BeforeDispatchRecord = () => throw new IOException("The stand-in dispatch write failed.") };
        RunResponse started = run.Start("claude-code", scenario, run.Report("dispatch-recovery"), by: run.Estate.OperatorId);
        Assert.True(started.Ok, started.Message);
        Assert.Contains("dispatch could not be recorded", started.Message, StringComparison.Ordinal);
        RunRow row = run.Settled(started.Run);
        Assert.Equal(status, row.Status);
        Assert.Equal(tokens, row.Charged.GetProperty("tokens").GetInt64());
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(["not-started", "dispatched", status], recorded.Revisions.Select(r => EstateModel.BodyString(r, "status")));
        Assert.True(recorded.Dispatched);
        Assert.All(recorded.Revisions, r => Assert.Equal(run.Estate.OperatorId, r.By));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TransmissionIsTimestampedAfterItsActualTransportAttemptOnEveryPlatform()
    {
        using var run = new RunEstate();
        string? attemptedAt = null;
        string? signalAttemptedAt = null;
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            BeforeTransmission = () =>
            {
                string before = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                WaitUntil(() => RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) != before);
                attemptedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
            },
            BeforeSignal = force =>
            {
                if (!force)
                {
                    string before = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                    WaitUntil(() => RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) != before);
                    signalAttemptedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
                }
            },
        };
        RunResponse started = run.Start("generic", "ack-stop", run.Report("transmission-time"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("transmission-time")));
        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Observe transmission after the attempt.", 5));
        Assert.True(stopped.Ok, stopped.Message);
        RunStop stop = run.Settled(started.Run).Stop!;
        Assert.NotNull(attemptedAt);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        Assert.True(File.Exists(files.StopFile));
        Assert.True(string.CompareOrdinal(stop.TransmittedAt, RecordParser.FormatTimestamp(File.GetLastWriteTimeUtc(files.StopFile))) >= 0);
        Assert.True(string.CompareOrdinal(stop.TransmittedAt, attemptedAt) >= 0);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(signalAttemptedAt); // Windows transmits the stop file; it has no cooperative process-group signal.
        }
        else
        {
            Assert.NotNull(signalAttemptedAt);
            Assert.True(string.CompareOrdinal(stop.TransmittedAt, signalAttemptedAt) >= 0);
        }

        Assert.True(string.CompareOrdinal(stop.AcknowledgedAt, stop.TransmittedAt) >= 0);
        Assert.Equal("known-failure", run.Settled(started.Run).Status);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheStallLimitIsPinnedFromTheGoalCurrentWhenTheIntentIsRecorded()
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(options: new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(50), Grace = TimeSpan.FromSeconds(1) });
        HookedLauncher.Install(run, verify: () =>
        {
            var goal = JsonNode.Parse(run.Estate.Load().ById[run.Goal].Body.GetRawText())!.AsObject();
            goal["limits"] = new JsonObject { ["stallSeconds"] = 60 };
            string revision = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(goal.ToJsonString()), run.Goal))).Id;
            RunEstate.Ok(run.Service.GoalActivate(revision, new GoalActionRequest(run.Estate.OperatorId)));
            return null;
        });
        RunResponse started = run.Start("claude-code", "silent-ignore", run.Report("revised-stall"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("revised-stall")));
        clock.Advance(TimeSpan.FromSeconds(120));
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        clock.Advance(TimeSpan.FromSeconds(1));
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(60, settled.Liveness!.StallSeconds);
        Assert.Equal("60", AgentRuns.Find(run.Estate.Load(), started.Run)!.Target("stallSeconds"));
        Assert.NotNull(settled.Stop!.ContainedAt);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void SettlementFlagsCommittedRenamedUntrackedAndIgnoredChangesOutsidePermittedPaths()
    {
        using var run = new RunEstate(brief: RunEstate.Brief.Replace("\"the widget\"", "\"src/**\",\"docs/*.md\"", StringComparison.Ordinal));
        RunResponse started = run.Start("generic", "scope-edits", run.Report("scope"));
        Assert.True(started.Ok, started.Message);
        RunRow row = run.Settled(started.Run);
        Assert.Equal("observed-success", row.Status);
        Assert.Equal(8, row.Scope!.Changed);
        Assert.Equal(6, row.Scope.OutsideCount);
        RunSignal signal = Assert.Single(row.Signals!, s => s.Code == "scope-settlement");
        Assert.Equal("warning", signal.Severity);
        Assert.NotNull(signal.Attention);
        Assert.Equal([".gitignore", "README-old.md", "README.md", "committed.txt", "ignored/outside.txt", "outside.txt"], row.Scope.Outside);
        Assert.Null(row.Scope.Problem);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(6, recorded.Latest.Body.GetProperty("scope").GetProperty("outsideCount").GetInt32());
        Assert.Contains("Scope attention: 6", recorded.Latest.Body.GetProperty("observed").GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.All(recorded.Revisions, revision => Assert.Equal(run.Estate.OperatorId, revision.By));
        Assert.Empty(run.Service.Records("acceptance"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerObservedSilenceRaisesAttentionThenStopsAndContainsAtTwiceTheGoalLimit(bool answered)
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(options: new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(50), Grace = TimeSpan.FromSeconds(1) });
        var goal = JsonNode.Parse(run.Estate.Load().ById[run.Goal].Body.GetRawText())!.AsObject();
        goal["limits"] = new JsonObject { ["stallSeconds"] = 60 };
        string revised = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(goal.ToJsonString()), run.Goal))).Id;
        RunEstate.Ok(run.Service.GoalActivate(revised, new GoalActionRequest(run.Estate.OperatorId)));
        RunResponse started = run.Start("generic", "silent-ignore", run.Report("stalled"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "3" });
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("stalled")));
        clock.Advance(TimeSpan.FromSeconds(60));
        WaitUntil(() => run.Row(started.Run).Liveness?.Attention is not null);
        RunRow attention = run.Row(started.Run);
        Assert.Equal("dispatched", attention.Status);
        Assert.Equal(60, attention.Liveness!.StallSeconds);
        Assert.Null(attention.Stop);
        Assert.Equal(60, attention.Charged.GetProperty("wallMinutes").GetInt64());
        RecordEnvelope ask = Assert.Single(run.Estate.Load().Current("ask"));
        Assert.Equal(started.Run, ask.Subject!.Id);
        Assert.Equal(run.Estate.OperatorId, EstateModel.BodyString(ask, "to"));
        Assert.Contains("worker observed", EstateModel.BodyString(ask, "question"), StringComparison.Ordinal);
        if (answered)
        {
            RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", run.Estate.OperatorId, RunEstate.Json($$"""{"title":"Inspected the run","statement":"Let the worker contain it.","summary":"Inspected.","synthesis":"The worker will contain it.","effects":["wait for settlement"],"affects":[],"decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"{{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)}}","stake":"low","notAuthorized":[],"answers":["{{ask.Id}}"]}"""))));
        }

        RecordWritten unrelated = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("ask", ask.By, RunEstate.Json($$"""{"to":"{{run.Estate.OperatorId}}","question":"Should we change the next assignment?","recommendation":"Review the assignment.","consequence":"The next assignment stays unchanged.","state":"open"}"""), $"operation:{ask.Subject.Id}:{ask.Subject.Hash}")));

        clock.Advance(TimeSpan.FromSeconds(60));
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        clock.Advance(TimeSpan.FromSeconds(1));
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.NotNull(settled.Liveness!.AttentionAt);
        Assert.NotNull(settled.Liveness.StoppedAt);
        Assert.Contains("twice the stall limit of 60 seconds", settled.Stop!.Reason, StringComparison.Ordinal);
        Assert.NotNull(settled.Stop.EscalatedAt);
        Assert.NotNull(settled.Stop.ContainedAt);
        Assert.Equal(ask.Id, settled.Liveness.Attention);
        EstateModel model = run.Estate.Load();
        RecordEnvelope currentAsk = model.Latest(ask);
        Assert.Equal(answered ? "answered" : "withdrawn", Asks.State(model, currentAsk).State);
        if (answered)
        {
            Assert.Equal(ask.Id, currentAsk.Id);
            Assert.Single(Asks.State(model, currentAsk).Answers);
        }
        else
        {
            RecordEnvelope settlement = AgentRuns.Find(model, started.Run)!.Latest;
            Assert.Equal(settlement.Reference, currentAsk.Subject);
            Assert.Equal(ask.By, currentAsk.By);
            Assert.Equal(ask.Id, currentAsk.Supersedes);
            Assert.Contains("no longer needed", EstateModel.BodyString(currentAsk, "recommendation"), StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale" && f.Location == currentAsk.ExpectedPath);
        Assert.DoesNotContain(Asks.Deliveries(model).SelectMany(d => d.Asks), a => model.Chain(a).Contains(ask.Id));
        Assert.Equal("open", Asks.State(model, model.ById[unrelated.Id]).State);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void NewEventsResetSilenceWithoutErasingTheAttentionAlreadyObserved()
    {
        var clock = new WorkerClock();
        using var eventSeen = new ManualResetEventSlim();
        using var run = new RunEstate(options: new WorkerOptions { Clock = clock, AfterEvent = eventSeen.Set, Heartbeat = TimeSpan.FromMilliseconds(50), Grace = TimeSpan.FromSeconds(1) });
        RunResponse started = run.Start("generic", "silent-recovery", run.Report("recovered"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "60" });
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("recovered")));
        clock.Advance(TimeSpan.FromSeconds(60));
        WaitUntil(() => run.Row(started.Run).Liveness?.Attention is not null);
        clock.Advance(TimeSpan.FromSeconds(30));
        File.WriteAllText(run.Report("recovered") + ".recover", "release");
        WaitUntil(() => eventSeen.IsSet);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        long heartbeat = files.Read()!.Heartbeats;
        clock.Advance(TimeSpan.FromSeconds(30)); // The old silence deadline; the new event reset it.
        WaitUntil(() => files.Read()?.Heartbeats is long beats && beats > heartbeat);
        Assert.Null(run.Row(started.Run).Stop);
        File.WriteAllText(run.Report("recovered") + ".finish", "release");
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        Assert.NotNull(settled.Liveness!.AttentionAt);
        Assert.NotNull(settled.Liveness.Attention);
        Assert.Null(settled.Liveness.StoppedAt);
        Assert.Single(run.Service.Records("ask"));
        EstateModel model = run.Estate.Load();
        Assert.Equal("withdrawn", Asks.State(model, model.ById[settled.Liveness.Attention!]).State);
        Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale" && f.Location.StartsWith("records/ask/", StringComparison.Ordinal));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheDefaultStallLimitIsTenMinutesAndAnInvalidOverrideLaunchesNothing()
    {
        using var run = new RunEstate();
        RunResponse invalid = run.Start("generic", "generic", run.Report("invalid-stall"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "0" });
        Assert.False(invalid.Ok);
        Assert.Empty(run.Service.Records("operation"));
        RunResponse started = run.Start("generic", "generic", run.Report("default-stall"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal(600, run.Settled(started.Run).Liveness!.StallSeconds);
    }

    [Fact]
    public void NativeCurrencyLimitAndSoftCapDisclosureAreRecordedBeforeLaunch()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":5000,"currency":{"amount":1000,"unit":"USD"}}""");
        string report = run.Report("native-budget");
        RunResponse started = run.Start("claude-code", "claude", report);
        Assert.True(started.Ok, started.Message);
        RunRow row = run.Settled(started.Run);
        List<string> args = Assert.Single(FakeHarness.Reports(report))["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Equal("9.00", args[args.IndexOf("--max-budget-usd") + 1]);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal("soft", recorded.Target("capMode"));
        Assert.Contains(recorded.Intent.Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.Contains("token threshold 4500 of 5000; currency threshold 900 of 1000", StringComparison.Ordinal));
        Assert.Contains(recorded.Intent.Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.Contains("no native total-token cap", StringComparison.Ordinal));
        Assert.Equal(1000, row.Charged.GetProperty("currency").GetProperty("amount").GetInt64());
    }

    [Fact]
    public void TokenHeadroomStopsBelowTheReservationAndPassesTheNativeTrackingLimit()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        string report = run.Report("headroom");
        RunResponse started = run.Start("codex", "codex-headroom", report);
        Assert.True(started.Ok, started.Message);
        RunRow row = run.Settled(started.Run);
        Assert.Equal(1000, row.Cap.GetProperty("tokens").GetInt64());
        Assert.Equal(950, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Contains("threshold of 900", row.Stop!.Reason, StringComparison.Ordinal);
        List<string> args = FakeHarness.Reports(report)[2]["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Contains("features.rollout_budget.enabled=true", args);
        Assert.Contains("features.rollout_budget.limit_tokens=900", args);
        Assert.Equal("active", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
    }

    [Fact]
    public void ClaudeGetsFreshLoginOnlyConfigurationAndExcludesEveryOrdinarySettingsSource()
    {
        using var run = new RunEstate();
        string original = Path.Combine(run.Scratch, "claude-user");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "settings.json"), """{"env":{"ANTHROPIC_API_KEY":"synthetic-filtered-value"},"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"nonexistent-hook-sentinel"}]}]},"enabledPlugins":{"ambient":true}}""");
        File.WriteAllText(Path.Combine(original, ".credentials.json"), "{\"claudeAiOauth\":{\"synthetic-login\":true},\"mcpOAuth\":{\"ambient\":true}}");
        var source = new Dictionary<string, string>(WorkerEnvironment.Current(), WorkerEnvironment.Names) { ["CLAUDE_CONFIG_DIR"] = original };
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = source };
        string report = run.Report("isolated-claude");
        RunResponse started = run.Start("claude-code", "claude", report);
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        JsonObject given = Assert.Single(FakeHarness.Reports(report));
        string home = given["environment"]!["CLAUDE_CONFIG_DIR"]!.GetValue<string>();
        Assert.NotEqual(original, home);
        Assert.False(File.Exists(Path.Combine(home, "settings.json")));
        string login = File.ReadAllText(Path.Combine(home, ".credentials.json"));
        Assert.Contains("claudeAiOauth", login, StringComparison.Ordinal);
        Assert.DoesNotContain("mcpOAuth", login, StringComparison.Ordinal);
        Assert.Null(given["environment"]!["ANTHROPIC_API_KEY"]);
        List<string> args = given["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Equal("", args[args.IndexOf("--setting-sources") + 1]);
        Assert.Equal("{\"hooks\":{},\"enabledPlugins\":{}}", args[args.IndexOf("--settings") + 1]);
    }

    [Fact]
    public void CodexRefusesAnEffectiveAmbientServerBeforeStartingAnyAgent()
    {
        using var run = new RunEstate();
        string report = run.Report("ambient-codex");
        RunResponse started = run.Start("codex", "codex-ambient", report);
        Assert.False(started.Ok);
        Assert.Contains("effective MCP configuration", started.Message, StringComparison.Ordinal);
        Assert.Equal("not-started", started.Status);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.All(FakeHarness.Reports(report), given => Assert.DoesNotContain("exec", given["arguments"]!.AsArray().Select(a => a!.GetValue<string>())));
    }

    [Fact]
    public void CodexGetsAFreshConfigurationWithoutAmbientServersPluginsOrHooks()
    {
        using var run = new RunEstate();
        string original = Path.Combine(run.Scratch, "codex-user");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "config.toml"), "[mcp_servers.ambient]\ncommand = \"unapproved\"\n");
        File.WriteAllText(Path.Combine(original, "auth.json"), "{\"stand-in-login\":true}");
        File.WriteAllText(Path.Combine(original, "hooks.json"), "{}");
        var source = new Dictionary<string, string>(WorkerEnvironment.Current(), WorkerEnvironment.Names) { ["CODEX_HOME"] = original };
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = source };
        string report = run.Report("isolated-codex");
        RunResponse started = run.Start("codex", "codex", report);
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        JsonObject given = FakeHarness.Reports(report)[2];
        string home = given["environment"]!["CODEX_HOME"]!.GetValue<string>();
        Assert.NotEqual(original, home);
        string configuration = File.ReadAllText(Path.Combine(home, "config.toml"));
        Assert.StartsWith("mcp_servers={vericairn=", configuration, StringComparison.Ordinal);
        Assert.Contains("[features]\ngoals = true", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("ambient", configuration, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(Path.Combine(original, "auth.json")), File.ReadAllText(Path.Combine(home, "auth.json")));
        Assert.False(File.Exists(Path.Combine(home, "hooks.json")));
        List<string> args = given["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Single(args, a => a.StartsWith("mcp_servers=", StringComparison.Ordinal));
        Assert.DoesNotContain("ambient", string.Join(' ', args), StringComparison.Ordinal);
        Assert.Contains("trust_level=\"untrusted\"", string.Join(' ', args), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedEscalationIsKeptAndRetriedWithoutClaimingAKill()
    {
        using var run = new RunEstate();
        int escalations = 0;
        bool failureObserved = false;
        RunDirectory? files = null;
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            BeforeSignal = force =>
            {
                if (force && Interlocked.Increment(ref escalations) == 1)
                {
                    throw new System.ComponentModel.Win32Exception("A stand-in signal failure.");
                }

                if (force)
                {
                    RunObservedState? state = files!.Read();
                    failureObserved = state?.Stop?.EscalatedAt is null && state?.EscalationFailure?.Contains("will be retried", StringComparison.Ordinal) == true;
                }
            },
        };
        RunResponse started = run.Start("generic", "ignore-stop", run.Report("retry-signal"));
        Assert.True(started.Ok, started.Message);
        files = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        WaitUntil(() => File.Exists(run.Report("retry-signal")));
        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Stop.", 0));
        Assert.True(stopped.Ok, stopped.Message);
        _ = run.Settled(started.Run);
        Assert.True(escalations >= 2);
        Assert.True(failureObserved);
        Assert.Contains("failed", files.Read()!.EscalationFailure, StringComparison.Ordinal);
        Assert.Equal("known-failure", run.Settled(started.Run).Status);
    }

    [Fact]
    public void ContainmentWaitsForTheChildEvenWhenTheRootAcknowledgesAndExits()
    {
        using var run = new RunEstate();
        string report = run.Report("descendants");
        RunResponse started = run.Start("generic", "child-survives-root", report);
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(report) && File.Exists(report + ".child"));
        int child = Assert.Single(FakeHarness.Reports(report + ".child"))["process"]!.GetValue<int>();
        Assert.Equal(Assert.Single(FakeHarness.Reports(report))["group"]!.GetValue<int>(), Assert.Single(FakeHarness.Reports(report + ".child"))["group"]!.GetValue<int>());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(Assert.Single(FakeHarness.Reports(report))["process"]!.GetValue<int>(), Assert.Single(FakeHarness.Reports(report))["group"]!.GetValue<int>());
        }

        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Stop the whole boundary.", 1));
        Assert.True(stopped.Ok, stopped.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.NotNull(settled.Stop!.ContainedAt);
        Assert.NotNull(settled.Stop!.AcknowledgedAt);
        Assert.NotNull(settled.Stop.EscalatedAt);
        Assert.NotNull(settled.Stop.ContainedAt);
        try
        {
            Assert.True(System.Diagnostics.Process.GetProcessById(child).HasExited);
        }
        catch (ArgumentException)
        {
            // The operating system already reaped it.
        }

        Assert.Equal("known-failure", run.Settled(started.Run).Status);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AFailedInitialStateWriteKeepsTheLaunchedRunsAllowanceAndSupervision()
    {
        using var run = new RunEstate();
        HookedLauncher.Install(run, command: () =>
        {
            string id = Assert.Single(run.Service.Records("operation")).Id;
            var directory = new RunDirectory(run.Service.Session.Manifest.Estate, id);
            Directory.CreateDirectory(directory.StateFile);
        });
        RunResponse started = run.Start("claude-code", "ignore-stop", run.Report("state-failure"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("dispatched", run.Row(started.Run).Status);
        Assert.Equal(60, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
        Assert.Equal(run.Estate.OperatorId, AgentRuns.Find(run.Estate.Load(), started.Run)!.Intent.By);
        var files = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        Directory.Delete(files.StateFile);
        WaitUntil(() => File.Exists(run.Report("state-failure")) && run.Row(started.Run).Observed);
        Assert.True(run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "End the stand-in.", 0)).Ok);
        Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        Assert.Equal("known-failure", run.Settled(started.Run).Status);
    }

    [Fact]
    public void ClaudeCodeCarriesTheAssignmentWithItsCapRecordedBeforeItStartsAndItsUsageAfter()
    {
        using var run = new RunEstate();
        IReadOnlyDictionary<string, string> current = WorkerEnvironment.Current();
        var environment = new Dictionary<string, string>(current, WorkerEnvironment.Names)
        {
            ["GITHUB_TOKEN"] = "ambient",
            ["GH_TOKEN"] = "ambient",
            ["AWS_SECRET_ACCESS_KEY"] = "ambient",
            ["NPM_TOKEN"] = "ambient",
            ["ANTHROPIC_API_KEY"] = "ambient",
            ["OPENAI_API_KEY"] = "ambient",
            ["CLAUDE_CONFIG_DIR"] = Path.Combine(run.Scratch, "claude"),
        };
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = environment, Vericairn = ["vericairn-under-test"] };
        string report = run.Report("claude");

        RunResponse started = run.Start("claude-code", "claude", report);
        Assert.True(started.Ok, started.Message);
        Assert.Equal("dispatched", started.Status);
        Assert.Equal("widget, branch work/w-1", started.Workspace);
        RunRow row = run.Settled(started.Run);

        // The cap was recorded before the run started, from the goal's budget, and kept; the usage the stand-in reported came after.
        EstateModel model = run.Estate.Load();
        AgentRuns.Run recorded = AgentRuns.Find(model, started.Run)!;
        Assert.Equal(["not-started", "dispatched", "observed-success"], recorded.Revisions.Select(r => EstateModel.BodyString(r, "status")));
        Assert.All(recorded.Revisions, r => Assert.Equal("""{"attemptsPerStep":3,"wallMinutes":60}""", Records.Canonical.CanonicalJson.Serialize(r.Body.GetProperty("cap"))));
        JsonElement target = recorded.Intent.Body.GetProperty("target");
        Assert.Equal(run.Assignment, target.GetProperty("assignment").GetString());
        Assert.Equal(run.Estate.BuilderId, target.GetProperty("principal").GetString());
        Assert.Equal("widget, branch work/w-1", target.GetProperty("workspace").GetString());
        Assert.Equal(run.Goal, target.GetProperty("goal").GetString());
        Assert.Equal("vericairn.worker/1", target.GetProperty("worker").GetString());
        Assert.Equal("claude-code", EstateModel.BodyString(recorded.Intent, "provider"));
        Assert.Equal("agent-run", EstateModel.BodyString(recorded.Intent, "action"));
        Assert.Equal(1734, row.Usage!.Value.GetProperty("tokens").GetInt64());
        Assert.Empty(row.Usage.Value.GetProperty("unreported").EnumerateArray());
        Assert.True(row.Usage.Value.GetProperty("wallMinutes").GetInt64() >= 1);
        Assert.Equal(row.Usage.Value.GetProperty("wallMinutes").GetInt64(), row.Charged.GetProperty("wallMinutes").GetInt64());
        Assert.DoesNotContain(run.Scratch, File.ReadAllText(Path.Combine(run.Estate.Root, recorded.Latest.ExpectedPath)), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);

        // The harness was Claude Code's own /goal, in the workspace, with the brief there and the MCP server for the agent.
        JsonObject given = Assert.Single(FakeHarness.Reports(report));
        List<string> arguments = given["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        string workspace = given["directory"]!.GetValue<string>();
        string mcp = arguments[arguments.IndexOf("--mcp-config") + 1];
        Assert.Equal(["-p", "/goal Does the widget build and pass its tests? Checks: the tests pass; an attempt and its result recorded with the vericairn MCP server's work_attempt and work_result tools. Constraints: no publication; no change outside the widget; only the changes the brief in .vericairn/brief.md permits.", "--output-format", "stream-json", "--verbose", "--permission-mode", "auto", "--mcp-config", mcp, "--strict-mcp-config", "--setting-sources", "", "--settings", "{\"hooks\":{},\"enabledPlugins\":{}}", "--tools", "Bash,Read,Write,Edit,Glob,Grep,TaskOutput,TaskStop,EndConversation", "--model", "model-a", "--effort", "high"], arguments);
        Assert.Equal("work/w-1", RunEstate.Git(workspace, "symbolic-ref", "--short", "HEAD"));
        Assert.Equal("main", RunEstate.Git(run.Clone, "symbolic-ref", "--short", "HEAD"));
        Assert.NotEqual(Path.GetFullPath(run.Clone), Path.GetFullPath(workspace));
        string exportedBrief = File.ReadAllText(Path.Combine(workspace, ".vericairn", "brief.md"));
        Assert.Contains(WorkView.RenderBrief(model, WorkView.FindOrder(model, "W-1")!, includeDocumentCatalogue: false), exportedBrief, StringComparison.Ordinal);
        Assert.Contains("## Context package", exportedBrief, StringComparison.Ordinal);
        Assert.Contains("The worker must admit it together with its run intent before dispatch", exportedBrief, StringComparison.Ordinal);
        Assert.Equal(Records.Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(exportedBrief)), recorded.Intent.Body.GetProperty("evidence")[0].GetProperty("hash").GetString());
        var custody = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        Assert.Equal(exportedBrief, File.ReadAllText(custody.BriefFile));
        using JsonDocument retainedContext = JsonDocument.Parse(File.ReadAllBytes(custody.ContextFile));
        Assert.Equal(retainedContext.RootElement.GetProperty("context").GetProperty("hash").GetString(), recorded.Intent.Body.GetProperty("execution").GetProperty("contextPackage").GetString());
        JsonElement retained = retainedContext.RootElement.GetProperty("context").GetProperty("payload");
        Assert.NotEmpty(retained.GetProperty("originals").EnumerateArray());
        Assert.Equal("vericairn.context/1", retained.GetProperty("format").GetString());
        Assert.False(custody.ContextFile.StartsWith(run.Estate.Root, StringComparison.Ordinal));
        Assert.Empty(run.Service.Objects());
        Assert.Contains("assignment " + run.Assignment, exportedBrief, StringComparison.Ordinal);
        Assert.Contains("Communication for " + run.Estate.OperatorId, exportedBrief, StringComparison.Ordinal);
        Assert.Contains("Freshness:", exportedBrief, StringComparison.Ordinal);
        Assert.Equal("*\n", File.ReadAllText(Path.Combine(workspace, ".vericairn", ".gitignore")));
        Assert.Empty(RunEstate.Git(workspace, "status", "--porcelain"));
        JsonObject server = JsonNode.Parse(File.ReadAllText(mcp))!["mcpServers"]!["vericairn"]!.AsObject();
        Assert.Equal("vericairn-under-test", server["command"]!.GetValue<string>());
        Assert.Equal(["--estate", run.Service.Root, "mcp", "serve", "--as", run.Estate.BuilderId], server["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.False(mcp.StartsWith(workspace, StringComparison.Ordinal));

        // Its environment came from the allowlist: no ambient credential, the harness's own configuration, and the run's files.
        JsonObject env = given["environment"]!.AsObject();
        foreach (string credential in new[] { "GITHUB_TOKEN", "GH_TOKEN", "AWS_SECRET_ACCESS_KEY", "NPM_TOKEN", "ANTHROPIC_API_KEY", "OPENAI_API_KEY" })
        {
            Assert.False(env.ContainsKey(credential), credential + " reached the harness.");
        }

        Assert.NotEqual(environment["CLAUDE_CONFIG_DIR"], env["CLAUDE_CONFIG_DIR"]!.GetValue<string>());
        Assert.Equal(environment[VericairnHome.EnvironmentVariable], env[VericairnHome.EnvironmentVariable]!.GetValue<string>());
        Assert.Equal(started.Run, env["VERICAIRN_RUN"]!.GetValue<string>());
        Assert.True(env.ContainsKey("PATH") || env.ContainsKey("Path"));

        // The worker accepted nothing: the goal is active, and nothing was accepted or reviewed.
        Assert.Equal("active", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
        Assert.Empty(run.Service.Records("acceptance"));
        Assert.Empty(run.Service.Records("review"));
    }

    [Fact]
    public void StandInFeatureRefusalsLeaveNoIntentAndCodexEventsAddUpEachTurnsTokens()
    {
        using var run = new RunEstate();
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Vericairn = ["vericairn-under-test"] };
        foreach ((string scenario, string because) in new[]
        {
            ("codex-goals-off", "Codex's goals feature is not enabled here (codex features list shows goals false); enable it with codex features enable goals"),
            ("codex-no-goals", "Codex lists no goals feature in codex features list"),
            ("codex-broken", "Codex's goals feature could not be verified: codex features list exited 2: error: unrecognized subcommand 'features'"),
        })
        {
            RunResponse refused = run.Start("codex", scenario, run.Report(scenario));
            Assert.False(refused.Ok);
            Assert.Equal("refused", refused.Problem!.Code);
            Assert.Contains(because, refused.Message, StringComparison.Ordinal);
            Assert.EndsWith("Nothing was recorded.", refused.Message, StringComparison.Ordinal);
            Assert.Equal(["features", "list"], Assert.Single(FakeHarness.Reports(run.Report(scenario)))["arguments"]!.AsArray().Select(a => a!.GetValue<string>()));
        }

        Assert.Empty(run.Service.Records("operation"));

        string report = run.Report("codex");
        RunResponse started = run.Start("codex", "codex", report);
        Assert.True(started.Ok, started.Message);
        RunRow row = run.Settled(started.Run);
        Assert.Equal("observed-success", row.Status);
        Assert.Equal(1400, row.Usage!.Value.GetProperty("tokens").GetInt64());
        List<JsonObject> given = FakeHarness.Reports(report);
        Assert.Equal(3, given.Count);
        Assert.Equal(["features", "list"], given[0]["arguments"]!.AsArray().Select(a => a!.GetValue<string>()));
        List<string> arguments = given[2]["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        string workspace = given[2]["directory"]!.GetValue<string>();
        string home = WorkerEnvironment.Current()[VericairnHome.EnvironmentVariable];
        string cd = arguments[3];
        Assert.EndsWith(Path.Combine("workspaces", "work-w-1"), cd, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("workspaces", "work-w-1"), workspace, StringComparison.Ordinal);
        Assert.Equal(["exec", "--json", "--cd", cd, "--sandbox", "workspace-write", "-c"], arguments.Take(7));
        Assert.StartsWith("mcp_servers={vericairn={command=\"vericairn-under-test\"", arguments[7], StringComparison.Ordinal);
        Assert.Contains($"\"VERICAIRN_HOME\"={Toml(home)}", arguments[7], StringComparison.Ordinal);
        Assert.Equal("projects={" + Toml(workspace) + "={trust_level=\"untrusted\"}}", arguments[9]);
        Assert.StartsWith("/goal Does the widget build and pass its tests? Checks: the tests pass;", arguments[^1], StringComparison.Ordinal);
        Assert.Equal("work/w-1", RunEstate.Git(workspace, "symbolic-ref", "--short", "HEAD"));
    }

    [Fact]
    public void TheGenericLauncherIsLabeledAndUsageItCannotReadIsChargedAtTheCap()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":5000}""");
        ProviderRow listed = Assert.Single(run.Service.Launchers(), l => l.Provider == "generic");
        Assert.StartsWith("Generic process launch is not complete agent support", listed.Limitations[0], StringComparison.Ordinal);
        Assert.Equal(["claude-code", "codex", "generic"], run.Service.Launchers().Select(l => l.Provider));
        Assert.All(run.Service.Launchers(), l => Assert.Equal(IAgentLauncher.Contract, l.Contract));
        Assert.Contains(run.Service.ProviderRows(), p => p.Kind == "workspace" && p.Contract == IWorkspaceProvider.Contract);

        string report = run.Report("generic");
        RunResponse started = run.Start("generic", "generic", report, settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["arguments"] = """["--goal","{condition}","--brief","{brief}","--mcp","{mcp-config}"]""" });
        Assert.True(started.Ok, started.Message);
        Assert.Contains("labeled: generic process launch is not complete agent support", started.Message, StringComparison.Ordinal);
        RunRow row = run.Settled(started.Run);
        Assert.True(row.Generic);
        Assert.Equal(["tokens"], row.Usage!.Value.GetProperty("unreported").EnumerateArray().Select(u => u.GetString()));
        Assert.Equal(5000, row.Cap.GetProperty("tokens").GetInt64());
        Assert.Equal(5000, row.Charged.GetProperty("tokens").GetInt64());
        JsonObject given = Assert.Single(FakeHarness.Reports(report));
        List<string> arguments = given["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Equal("--goal", arguments[0]);
        Assert.StartsWith("Does the widget build", arguments[1], StringComparison.Ordinal);
        Assert.Equal(given["environment"]!["VERICAIRN_BRIEF"]!.GetValue<string>(), arguments[3]);
        Assert.Equal(given["environment"]!["VERICAIRN_MCP_CONFIG"]!.GetValue<string>(), arguments[5]);

        // Unknown usage is never zero: the whole token cap was charged, so the goal is budget-limited and no further run starts.
        // The goal-status record follows the run's settlement in a separate write.
        WaitUntil(() => run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString() == "budget-limited");
        Assert.Equal("budget-limited", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
        LedgerRow ledger = Assert.Single(run.Service.Runs().Goals);
        Assert.Equal(0, Assert.Single(ledger.Units, u => u.Unit == "tokens").Remaining);
        Assert.NotEmpty(ledger.Fenced);
        RunResponse fenced = run.Start("generic", "generic", run.Report("again"));
        Assert.False(fenced.Ok);
        Assert.Contains("is budget-limited", fenced.Message, StringComparison.Ordinal);
        Assert.Single(run.Service.Records("operation"));
    }

    [Fact]
    public void AStopToAHarnessThatIgnoresItIsEscalatedAfterTheGracePeriodAndEachFactIsKept()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "ignore-stop", run.Report("ignore"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("ignore")) && run.Row(started.Run).Observed);

        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "The widget is cancelled.", 1));
        Assert.True(stopped.Ok, stopped.Message);
        RunStop stop = run.Settled(started.Run).Stop!;
        Assert.Equal(run.Estate.OperatorId, stop.RequestedBy);
        Assert.NotNull(stop.TransmittedAt);
        Assert.Contains("VERICAIRN_STOP_FILE", stop.Transmission, StringComparison.Ordinal);
        Assert.Null(stop.AcknowledgedAt);
        Assert.NotNull(stop.EscalatedAt);
        Assert.Contains("containment boundary", stop.Escalation, StringComparison.Ordinal);
        Assert.NotNull(stop.ContainedAt);
        Assert.True(string.CompareOrdinal(stop.RequestedAt, stop.ContainedAt) <= 0);

        // The settlement records the same facts as observed, the run a known failure charged for the time it ran.
        RunRow row = run.Settled(started.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal(stop with { }, row.Stop);
        JsonElement recorded = Assert.Single(run.Service.Records("operation")).Envelope.GetProperty("body");
        Assert.Equal(stop.EscalatedAt, recorded.GetProperty("stop").GetProperty("escalatedAt").GetString());
        Assert.False(recorded.GetProperty("stop").TryGetProperty("acknowledgedAt", out _));
        Assert.Contains("not acknowledged", recorded.GetProperty("observed").GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AStopIsTransmittedThoughTheRunsStateIsBeingReplacedWhenItIsAsked()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("generic", "ack-stop", run.Report("replaced"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("replaced")) && run.Row(started.Run).Observed);

        // A state file caught half written reads as nothing for a moment; the worker writes it whole again at its next heartbeat.
        File.WriteAllText(new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).StateFile, "{");
        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Replaced.", 5));
        Assert.True(stopped.Ok, stopped.Message);
        RunStop stop = run.Settled(started.Run).Stop!;
        Assert.NotNull(stop.TransmittedAt);
    }

    [Fact]
    public void AStopTheHarnessAcknowledgesIsContainedWithoutEscalation()
    {
        using var run = new RunEstate(options: new WorkerOptions { Clock = new WorkerClock(), Heartbeat = TimeSpan.FromMilliseconds(50) });
        RunResponse started = run.Start("generic", "ack-stop", run.Report("ack"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("ack")) && run.Row(started.Run).Observed);
        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, null, 5));
        Assert.True(stopped.Ok, stopped.Message);
        RunStop stop = run.Settled(started.Run).Stop!;
        Assert.NotNull(stop.TransmittedAt);
        Assert.NotNull(stop.AcknowledgedAt);
        Assert.Contains("acknowledgment", stop.Acknowledgment, StringComparison.Ordinal);
        Assert.Null(stop.EscalatedAt);
        Assert.NotNull(stop.ContainedAt);
        Assert.Equal("known-failure", run.Settled(started.Run).Status);
        RunStopResponse again = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId));
        Assert.False(again.Ok);
        Assert.Contains("there is nothing to stop", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStopNoWorkerObservesIsReportedAsRequestedAndALostWorkerIsNotAStoppedRun()
    {
        using var run = new RunEstate();
        string report = run.Report("lingering");
        RunResponse started = run.Start("generic", "ack-stop", report);
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(report));
        int harness = Assert.Single(FakeHarness.Reports(report))["process"]!.GetValue<int>();
        try
        {
            // The worker that supervised the run lets go of it; the harness runs on, and nothing calls it stopped.
            run.Reopen();
            WaitUntil(() => !run.Row(started.Run).Observed);
            RunRow lost = run.Row(started.Run);
            Assert.Equal("dispatched", lost.Status);
            Assert.Null(lost.Usage);
            Assert.Equal(60, lost.Charged.GetProperty("wallMinutes").GetInt64());

            RunStopResponse asked = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Nobody watches it."));
            Assert.True(asked.Ok);
            Assert.Equal("requested", asked.State);
            Assert.Null(asked.Stop!.TransmittedAt);
            Assert.Contains("no worker on this machine transmitted it: none observes the run", asked.Message, StringComparison.Ordinal);
            Assert.Contains("A stop that is not observed stays requested", asked.Message, StringComparison.Ordinal);
            Assert.Equal("requested", run.Row(started.Run).Stop!.State);
            Assert.Equal("dispatched", run.Row(started.Run).Status);
        }
        finally
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(harness).Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
                // It ended already.
            }
        }
    }

    [Fact]
    public void OutputThatClosesIsNotAnEndOnlyTheExitSettlesTheRun()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "close-output", run.Report("closing"));
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("closing")));
        WaitUntil(() => File.Exists(run.Report("closing") + ".closed"));
        Assert.Equal("dispatched", run.Row(started.Run).Status);
        File.WriteAllText(run.Report("closing") + ".finish", "release");
        RunRow row = run.Settled(started.Run);
        Assert.Equal("observed-success", row.Status);
        Assert.Empty(row.Usage!.Value.GetProperty("unreported").EnumerateArray());
        Assert.False(row.Usage.Value.TryGetProperty("tokens", out _));
    }

    [Fact]
    public void TheWorkerReservesShutdownHeadroomBeforeTheWallMinutesAllowanceEnds()
    {
        var clock = new WorkerClock();
        var options = new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(1), Minute = TimeSpan.FromMilliseconds(400) };
        using var run = new RunEstate("""{"wallMinutes":3,"attemptsPerStep":3}""", options: options);
        RunResponse started = run.Start("generic", "ack-stop", run.Report("capped"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal(3, started.Cap!.Value.GetProperty("wallMinutes").GetInt64());
        WaitUntil(() => File.Exists(run.Report("capped")));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        RunRow row = run.Settled(started.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal("worker", row.Stop!.RequestedBy);
        Assert.Contains("shutdown headroom", row.Stop.Reason, StringComparison.Ordinal);
        Assert.NotNull(row.Stop.ContainedAt);
        Assert.Equal(1, row.Usage!.Value.GetProperty("wallMinutes").GetInt64());

        Assert.Equal("active", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
        Assert.True(Assert.Single(Assert.Single(run.Service.Runs().Goals).Units).Remaining > 0);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ARunWhoseReportedTokensCrossItsCapIsStoppedAndChargedWhatItReported()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        RunResponse started = run.Start("codex", "codex-spender", run.Report("spender"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal(1000, started.Cap!.Value.GetProperty("tokens").GetInt64());
        RunRow row = run.Settled(started.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal("worker", row.Stop!.RequestedBy);
        Assert.Contains("threshold of 900, below the reserved cap of 1000", row.Stop.Reason, StringComparison.Ordinal);
        Assert.NotNull(row.Stop.AcknowledgedAt);
        Assert.Equal(1050, row.Usage!.Value.GetProperty("tokens").GetInt64());
        Assert.Equal(1050, row.Charged.GetProperty("tokens").GetInt64());
        // The goal-status record follows the run's settlement in a separate write.
        WaitUntil(() => run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString() == "budget-limited");
        Assert.Equal("budget-limited", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());
    }

    [Fact]
    public void ConcurrentRunsNeverSpendTheSameAllowanceTwice()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""");
        RunResponse first = run.Start("generic", "ack-stop", run.Report("first"), wallMinutes: 40);
        Assert.True(first.Ok, first.Message);
        Assert.Equal(40, first.Cap!.Value.GetProperty("wallMinutes").GetInt64());

        // One run works in a workspace at a time; the second run, on its own branch, takes only what the first does not hold.
        Assert.Contains($"Run {first.Run} works on branch work/w-1 of widget and is in flight", run.Start("generic", "ack-stop", run.Report("shared")).Message, StringComparison.Ordinal);
        RunResponse second = run.Start("generic", "ack-stop", run.Report("second"), wallMinutes: 40, branch: "work/w-1-second");
        Assert.True(second.Ok, second.Message);
        Assert.Equal(20, second.Cap!.Value.GetProperty("wallMinutes").GetInt64());
        Assert.Equal("widget, branch work/w-1-second", second.Workspace);
        RunResponse third = run.Start("generic", "ack-stop", run.Report("third"), branch: "work/w-1-third");
        Assert.False(third.Ok);
        Assert.Contains("The goal's budget is spent: wallMinutes 60 of 60 charged to its runs", third.Message, StringComparison.Ordinal);
        Assert.Contains("2 of the goal's runs are in flight, and its budget allows 2 at a time", third.Message, StringComparison.Ordinal);
        Assert.Equal("active", run.Service.Goal(run.Goal)!.Json.GetProperty("status").GetString());

        foreach ((RunResponse started, string report) in new[] { (first, "first"), (second, "second") })
        {
            WaitUntil(() => File.Exists(run.Report(report)) && run.Row(started.Run).Observed);
            Assert.True(run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, "Enough.", 5)).Ok);
            Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        }

        // Settled, each is charged what it used, and the allowance they held is free again.
        LedgerRow ledger = Assert.Single(run.Service.Runs().Goals);
        LedgerUnit wall = Assert.Single(ledger.Units);
        Assert.Equal(wall.Spent, run.Service.Runs().Runs.Sum(r => r.Charged.GetProperty("wallMinutes").GetInt64()));
        Assert.True(wall.Remaining > 0);
        Assert.Equal(0, ledger.InFlight);
    }

    [Fact]
    public void AHarnessThatCannotStartIsAKnownFailureThatSpentNothing()
    {
        using var run = new RunEstate();
        HookedLauncher.Install(run, command: () => throw new Vericairn.Runtime.Providers.ProviderRefusedException("The synthetic executable disappeared after preflight."));
        RunResponse failed = run.Start("claude-code", "claude", run.Report("never-launched"));
        Assert.False(failed.Ok);
        Assert.Equal("provider", failed.Problem!.Code);
        Assert.Equal("known-failure", failed.Status);
        RunRow row = run.Row(failed.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Empty(row.Charged.EnumerateObject());
        string recorded = File.ReadAllText(Path.Combine(run.Estate.Root, run.Estate.Load().ById[failed.Operation!.Id].ExpectedPath));
        Assert.DoesNotContain(run.Scratch, recorded, StringComparison.Ordinal);
        Assert.Contains("could not be started on the machine that ran the worker, so nothing ran and nothing is charged", recorded, StringComparison.Ordinal);
        Assert.Equal(60, Assert.Single(Assert.Single(run.Service.Runs().Goals).Units).Remaining);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ARunIsRefusedBeforeAnythingIsRecordedWhenAPreconditionFails()
    {
        using var run = new RunEstate();
        string setting = FakeHarness.Setting("claude", run.Report("never"));
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = setting };
        (RunStartRequest Request, string Because)[] cases =
        [
            (new RunStartRequest(run.Assignment, "claude-code", run.Estate.OperatorId, run.Estate.OperatorId, run.Resource, Settings: settings), "a run carries an assignment as its contributor"),
            (new RunStartRequest(run.Assignment, "gemini", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource), "The launcher 'gemini' has no implementation"),
            (new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource, Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["model"] = "x" }), "it takes no model"),
            (new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource), "this run names no command"),
            (new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.BuilderId, run.Resource, Settings: settings), "or by the sponsor of the goal it is charged to when the sponsor is a person, and Test Builder"),
            (new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Goal, Settings: settings), "No repository resource"),
            (new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource, WallMinutes: 0, Settings: settings), "at least one wall minute"),
        ];
        foreach ((RunStartRequest request, string because) in cases)
        {
            RunResponse refused = run.Service.RunStart(request);
            Assert.False(refused.Ok, because);
            Assert.Contains(because, refused.Message, StringComparison.Ordinal);
        }

        // A goal that is paused fences its runs; resumed, a run starts.
        RunEstate.Ok(run.Service.GoalPause(run.Goal, new GoalActionRequest(run.Estate.OperatorId)));
        Assert.Contains("is paused: a run starts only while its goal is active", run.Start("claude-code", "claude", run.Report("paused")).Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("never")));
        Assert.False(File.Exists(run.Report("paused")));
    }

    [Fact]
    public void TheWorkerLaunchesNothingWhileTheOperatorsKeySignsHereWithoutPresence()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RunResponse refused = run.Start("claude-code", "claude", run.Report("guarded"));
        Assert.False(refused.Ok);
        Assert.Contains("The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"Device {run.OperatorDevice} (Operator's laptop, admitted) of Test Operator ({run.Estate.OperatorId}), an operator: this machine holds its key in file-owner-only custody without its owner's presence", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"the operating-system user {Environment.UserName}", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(run.Report("guarded")));

        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);
        RunResponse started = run.Start("claude-code", "claude", run.Report("unguarded"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
    }

    [Fact]
    public void TheSponsorsCustodyExceptionLetsARunLaunchUnattendedAndTheRunNamesIt()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));
        Assert.False(refused.Ok);
        Assert.Contains($"Or the goal's sponsor records a custody exception for the goal, with its reason and its expiry: vericairn goal custody-exception {run.Goal} --by <sponsor> --reason <why> --expires <time>.", refused.Message, StringComparison.Ordinal);
        JsonElement custody = Route(run, "W-1").GetProperty("custody");
        Assert.False(custody.GetProperty("allows").GetBoolean());
        Assert.Equal(JsonValueKind.Null, custody.GetProperty("exception").ValueKind);

        // An agent never excepts its own runs.
        string hash = run.Service.Goal(run.Goal)!.Json.GetProperty("goal").GetProperty("hash").GetString()!;
        string expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
        WriteOutcome byAgent = run.Service.NewRecord(new NewRecordRequest("custody-exception", run.Estate.BuilderId, RunEstate.Json($$"""{"reason":"I may.","expires":"{{expires}}"}"""), $"goal:{run.Goal}:{hash}"));
        Assert.Equal("rejected", byAgent.Problem!.Code);
        Assert.Contains(byAgent.Problem.Findings, f => f.Code == "custody-exception.sponsor");

        // The sponsor's exception in force lifts the refusal: the view shows it, and the run launches unattended, names it and says what it lifted.
        string exception = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("custody-exception", run.Estate.OperatorId, RunEstate.Json($$"""{"reason":"The build loop runs overnight on this machine.","expires":"{{expires}}"}"""), $"goal:{run.Goal}:{hash}"))).Id;
        custody = Route(run, "W-1").GetProperty("custody");
        Assert.True(custody.GetProperty("allows").GetBoolean());
        Assert.Equal(exception, custody.GetProperty("exception").GetProperty("id").GetString());
        Assert.Contains($"custody here: an unattended run is allowed under the custody exception {exception} that Test Operator recorded for the goal, in force until {expires}: The build loop runs overnight on this machine.; without it: Device {run.OperatorDevice}", run.Service.GoalNext(run.Goal)!.Text, StringComparison.Ordinal);
        RunResponse started = run.Start("claude-code", "claude", run.Report("excepted"));
        Assert.True(started.Ok, started.Message);
        Assert.Contains($"It was launched under the custody exception {exception} that Test Operator recorded for the goal, in force until {expires}", started.Message, StringComparison.Ordinal);
        Assert.Contains($"without it the worker would refuse: Device {run.OperatorDevice}", started.Message, StringComparison.Ordinal);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(exception, recorded.Target("custodyException"));
        Assert.Contains(recorded.Intent.Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.StartsWith($"Launched unattended under the sponsor's custody exception {exception}, in force until {expires}, while device {run.OperatorDevice} of an operator", StringComparison.Ordinal));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ARunNamesTheCustodyExceptionInForceWhenItsIntentIsRecordedNotTheOneItsPreflightFound()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        string first = Except(run, DateTimeOffset.UtcNow.AddHours(1), "The first window.");
        string? second = null;

        // While the worker verifies its launcher, after the preflight found the first exception, the sponsor replaces it with a second.
        HookedLauncher.Install(run, () =>
        {
            second ??= Except(run, DateTimeOffset.UtcNow.AddHours(2), "A narrower window.", first);
            return null;
        });
        RunResponse started = run.Start("claude-code", "claude", run.Report("replaced"));
        Assert.True(started.Ok, started.Message);
        Assert.NotNull(second);
        Assert.Contains($"It was launched under the custody exception {second} that Test Operator recorded for the goal", started.Message, StringComparison.Ordinal);
        Assert.Contains("A narrower window.", started.Message, StringComparison.Ordinal);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(second, recorded.Target("custodyException"));
        Assert.Contains(recorded.Intent.Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.StartsWith($"Launched unattended under the sponsor's custody exception {second},", StringComparison.Ordinal));
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnExceptionThatLapsesWhileARunIsPreparedLaunchesNothing()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        string first = Except(run, DateTimeOffset.UtcNow.AddHours(1), "The first window.");

        // The sponsor replaces the exception with one that expires seconds later, and the preparation outlasts it.
        Func<string?> replace = () =>
        {
            string expires = Except(run, Soon(), "Only a moment.", first);
            WaitPast(run, expires);
            return null;
        };
        HookedLauncher.Install(run, replace);
        RunResponse replaced = run.Start("claude-code", "claude", run.Report("replaced"));
        Assert.False(replaced.Ok, replaced.Message);
        Assert.StartsWith("The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine", replaced.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", replaced.Message, StringComparison.Ordinal);

        // An exception in force when the preflight read it, and expired before the intent, launches nothing either.
        string brief = Except(run, Soon(), "A short window.");
        HookedLauncher.Install(run, () =>
        {
            WaitPast(run, brief);
            return null;
        });
        RunResponse expired = run.Start("claude-code", "claude", run.Report("expired"));
        Assert.False(expired.Ok, expired.Message);
        Assert.Contains($"Device {run.OperatorDevice} (Operator's laptop, admitted) of Test Operator", expired.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was recorded.", expired.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("replaced")));
        Assert.False(File.Exists(run.Report("expired")));
        Assert.Empty(run.Service.Records("operation"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnExceptionThatLapsesAfterTheIntentCancelsTheRunBeforeItsDispatch()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        string first = Except(run, DateTimeOffset.UtcNow.AddHours(1), "The first window.");

        // The intent names the exception in force; then, before the launch, the sponsor replaces it with one that expires within seconds.
        HookedLauncher.Install(run, command: () => WaitPast(run, Except(run, Soon(), "Only a moment.", first)));
        RunResponse canceled = run.Start("claude-code", "claude", run.Report("lapsed"));
        Assert.False(canceled.Ok, canceled.Message);
        Assert.Equal("canceled", canceled.Status);
        Assert.StartsWith($"Run {canceled.Run} was cancelled before dispatch: The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine", canceled.Message, StringComparison.Ordinal);
        Assert.EndsWith("Its intent is recorded as canceled; nothing was launched and nothing is charged.", canceled.Message, StringComparison.Ordinal);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), canceled.Run)!;
        Assert.Equal(first, recorded.Target("custodyException"));
        Assert.Equal(("canceled", false, false), (recorded.Status, recorded.InFlight, recorded.Charged));
        string summary = recorded.Latest.Body.GetProperty("observed").GetProperty("summary").GetString()!;
        Assert.Equal($"Cancelled before dispatch: when the harness was about to start, device {run.OperatorDevice} of an operator or of the goal's sponsor signed without its owner's presence on the machine that ran the worker, with no custody exception in force for the goal, and the custody exception {first} its intent named was no longer in force. Nothing was launched and nothing is charged.", summary);
        Assert.False(File.Exists(run.Report("lapsed")));
        Assert.Empty(run.Row(canceled.Run).Charged.EnumerateObject());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AKeyThatArrivesAfterTheIntentCancelsTheRunBeforeItsDispatch()
    {
        using var run = new RunEstate();
        string? arrived = null;

        // Nothing stood in the way when the intent was recorded; then, before the launch, an operator's key arrives on this machine.
        HookedLauncher.Install(run, command: () => arrived = RunEstate.Ok(run.Service.DeviceCreate(new DeviceCreateRequest(run.Estate.OperatorId, "Spare laptop", "file"))).Id);
        RunResponse canceled = run.Start("claude-code", "claude", run.Report("arrived"));
        Assert.False(canceled.Ok, canceled.Message);
        Assert.Equal("canceled", canceled.Status);
        Assert.Contains($"Device {arrived} (Spare laptop, not admitted) of Test Operator", canceled.Message, StringComparison.Ordinal);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), canceled.Run)!;
        Assert.Null(recorded.Target("custodyException"));
        Assert.Equal($"Cancelled before dispatch: when the harness was about to start, device {arrived} of an operator or of the goal's sponsor signed without its owner's presence on the machine that ran the worker, with no custody exception in force for the goal. Nothing was launched and nothing is charged.", recorded.Latest.Body.GetProperty("observed").GetProperty("summary").GetString());
        Assert.False(File.Exists(run.Report("arrived")));
        Assert.Equal(0, Assert.Single(run.Service.Runs().Goals).InFlight);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    /// <summary>Records the operator's custody exception for the run estate's goal, or a revision of one; returns its id.</summary>
    private static string Except(RunEstate run, DateTimeOffset expires, string reason, string? replaces = null)
    {
        string hash = run.Service.Goal(run.Goal)!.Json.GetProperty("goal").GetProperty("hash").GetString()!;
        string until = RecordParser.FormatTimestamp(expires);
        return RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("custody-exception", run.Estate.OperatorId, RunEstate.Json($$"""{"reason":"{{reason}}","expires":"{{until}}"}"""), $"goal:{run.Goal}:{hash}", replaces))).Id;
    }

    /// <summary>
    /// An expiry a few seconds away. Records keep whole seconds, so an expiry one second away can fall in the same second as the
    /// record that makes it when the runner is slow, and the estate rightly refuses an exception that expires when it is made.
    /// </summary>
    private static DateTimeOffset Soon() => DateTimeOffset.UtcNow.AddSeconds(3);

    /// <summary>Waits until a custody exception has expired.</summary>
    private static void WaitPast(RunEstate run, string exception)
    {
        DateTimeOffset expires = CustodyExceptions.Expires(run.Estate.Load().ById[exception])!.Value;
        while (DateTimeOffset.UtcNow <= expires)
        {
            Thread.Sleep(50);
        }
    }

    private static JsonElement Route(RunEstate run, string key) =>
        Assert.Single(run.Service.GoalNext(run.Goal)!.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key).GetProperty("route");

    private static void WaitUntil(Func<bool> condition, int seconds = 30)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < until, "The condition did not hold in time.");
            Thread.Sleep(50);
        }
    }

    private static string Toml(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
