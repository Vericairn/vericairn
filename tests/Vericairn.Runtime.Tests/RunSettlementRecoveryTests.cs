// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RunSettlementRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedSettlementIsRecoveredWithItsOriginalFactsAndReleasesTheBranch(bool startup)
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""");
        JsonElement? proposed = null;
        run.Service.BeforeValidatingRecords = records =>
        {
            if (Settlement(records) is RecordEnvelope settlement)
            {
                proposed = settlement.Body.Clone();
                throw new IOException("Synthetic settlement validation failure.");
            }
        };
        RunResponse started = run.Start("claude-code", "claude", run.Report("failed-settlement"), wallMinutes: 10);
        Assert.True(started.Ok, started.Message);
        RunDirectory directory = Failed(run, started.Run);
        RunObservedState retained = directory.Read()!;
        Assert.True(retained.SettlementPrepared);
        Assert.NotNull(retained.Outcome!.Elapsed);
        Assert.Equal(1734, retained.Outcome.Tokens);
        Assert.Null(retained.Settled);
        Assert.Equal("dispatched", run.Row(started.Run).Status);
        Assert.Equal(10, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
        Assert.NotNull(proposed);
        RunResponse busy = run.Start("generic", "generic", run.Report("held-branch"), wallMinutes: 1);
        Assert.False(busy.Ok);
        Assert.Contains("branch", busy.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Report("held-branch")));

        // A failed retry keeps the reservation and the same observation. Neither retry time nor the new
        // worker's heartbeat/accounting settings are the original run's usage or evidence.
        RunSettleResponse failed = run.Service.RunSettle(started.Run, new(run.Estate.OperatorId));
        Assert.False(failed.Ok);
        Assert.Equal("unresolved", failed.Problem!.Code);
        Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
        run.Service.BeforeValidatingRecords = null;
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Heartbeat = TimeSpan.FromSeconds(7), Minute = TimeSpan.FromSeconds(1) };
        if (startup) run.Reopen();
        else
        {
            RunSettleResponse recovered = run.Service.RunSettle(started.Run, new(run.Estate.OperatorId));
            Assert.True(recovered.Ok, recovered.Message);
            Assert.NotNull(recovered.Operation);
        }

        RunRow row = run.Row(started.Run);
        Assert.Equal("observed-success", row.Status);
        Assert.Equal(1734, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Equal(proposed.Value.GetProperty("usage").GetProperty("wallMinutes").GetInt64(), row.Charged.GetProperty("wallMinutes").GetInt64());
        JsonElement recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body;
        foreach (string field in new[] { "observed", "usage", "scope", "liveness", "executionObserved", "dispatchedAt", "evidence" })
            Assert.True(JsonElement.DeepEquals(proposed.Value.GetProperty(field), recorded.GetProperty(field)), field);
        Assert.Equal(retained.Outcome.Signals!.Select(s => s with { Attention = null }), row.Signals!.Select(s => s with { Attention = null }));
        Assert.NotNull(Assert.Single(row.Signals!, s => s.Code == "attempt-missing").Attention);
        Assert.Equal("observed-success", directory.Read()!.Settled);
        Assert.Null(directory.Read()!.Problem);
        Assert.Equal(0, Assert.Single(run.Service.Runs().Goals).InFlight);
        int revisions = AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count;
        Assert.Equal("already-settled", run.Service.RunSettle(started.Run, new(run.Estate.OperatorId)).Problem!.Code);
        run.Reopen();
        Assert.Equal(revisions, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);

        run.Service.WorkerOptions = run.Service.WorkerOptions with { Heartbeat = TimeSpan.FromMilliseconds(100), Minute = TimeSpan.FromMinutes(1) };
        RunResponse next = run.Start("generic", "generic", run.Report("released-branch"), wallMinutes: 1);
        Assert.True(next.Ok, next.Message);
        Assert.Equal("observed-success", run.Settled(next.Run).Status);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void RecoveryPreservesTheObservedStopAndUnreportedUnits()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"tokens":5000,"currency":{"amount":1000,"unit":"USD"},"attemptsPerStep":3}""");
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        string report = run.Report("stopped-settlement");
        RunResponse started = run.Start("generic", "ack-stop", report, wallMinutes: 10);
        Assert.True(started.Ok, started.Message);
        run.WaitUntil(() => File.Exists(report));
        Assert.Equal("in-flight", run.Service.RunSettle(started.Run, new(run.Estate.OperatorId)).Problem!.Code);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        directory.RequestStop(new StopRequest(RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), run.Estate.OperatorId, "Preserve the observed stop.", 1));
        _ = Failed(run, started.Run);
        RunOutcome outcome = directory.Read()!.Outcome!;
        Assert.NotNull(outcome.Stop!.ContainedAt);
        Assert.NotNull(outcome.Stop.AcknowledgedAt);
        run.Service.BeforeValidatingRecords = null;
        RunSettleResponse recovered = run.Service.RunSettle(started.Run, new(run.Estate.OperatorId));
        Assert.True(recovered.Ok, recovered.Message);
        RunRow row = run.Row(started.Run);
        Assert.Equal("known-failure", row.Status);
        Assert.Equal(outcome.Stop, row.Stop);
        Assert.Equal(outcome.Signals!.Select(s => s with { Attention = null }), row.Signals!.Select(s => s with { Attention = null }));
        Assert.Equal(["tokens", "currency"], row.Usage!.Value.GetProperty("unreported").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(5000, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Equal(1000, row.Charged.GetProperty("currency").GetProperty("amount").GetInt64());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ReportedCurrencySurvivesASettlementFailureAndRuntimeRestart()
    {
        using var run = new RunEstate("""{"wallMinutes":60,"tokens":5000,"currency":{"amount":1000,"unit":"USD"},"attemptsPerStep":3}""");
        run.Service.Providers.Register(new ReportedLauncher(new PriceReadingLauncher()));
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        RunResponse started = run.Start("generic", "claude", run.Report("priced-settlement"));
        Assert.True(started.Ok, started.Message);
        RunDirectory directory = Failed(run, started.Run);
        Assert.Equal(2, directory.Read()!.Outcome!.Price);
        Assert.Equal("USD", directory.Read()!.Outcome!.PriceUnit);
        run.Reopen();
        RunRow row = run.Row(started.Run);
        Assert.Equal("observed-success", row.Status);
        Assert.Equal(1734, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Equal(2, row.Charged.GetProperty("currency").GetProperty("amount").GetInt64());
        Assert.Equal("USD", row.Charged.GetProperty("currency").GetProperty("unit").GetString());
        Assert.Empty(row.Usage!.Value.GetProperty("unreported").EnumerateArray());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("json")]
    [InlineData("null")]
    [InlineData("identity")]
    [InlineData("exit")]
    [InlineData("time")]
    [InlineData("outcome")]
    [InlineData("heartbeats")]
    [InlineData("null-signal")]
    [InlineData("duplicate")]
    public void UnavailableOrIncompleteObservationsStayUnresolvedOnRequestAndAtStartup(string damage)
    {
        using var run = new RunEstate();
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        RunResponse started = run.Start("generic", "generic", run.Report("unreadable-settlement"));
        Assert.True(started.Ok, started.Message);
        RunDirectory directory = Failed(run, started.Run);
        // Dispose joins the original supervisor before damaging its test-owned snapshot.
        run.Service.Dispose();
        RunObservedState state = directory.Read()!;
        if (damage == "missing") File.Delete(directory.StateFile);
        else if (damage == "json") File.WriteAllText(directory.StateFile, "{");
        else if (damage == "null") File.WriteAllText(directory.StateFile, "null");
        else if (damage == "duplicate") File.WriteAllText(directory.StateFile, "{\"run\":\"first\",\"run\":\"second\"}");
        else if (damage == "heartbeats")
        {
            JsonObject json = JsonNode.Parse(File.ReadAllText(directory.StateFile))!.AsObject();
            json.Remove("heartbeats");
            File.WriteAllText(directory.StateFile, json.ToJsonString());
        }
        else
        {
            if (damage == "identity") state.Run = Ulid.NewUlid().Value;
            if (damage == "exit") state.ExitCode = null;
            if (damage == "time") state.LaunchedAt = "unreadable";
            if (damage == "outcome") state.Outcome = state.Outcome! with { ExitCode = 7 };
            if (damage == "null-signal") state.Signals = [null!];
            directory.Write(state);
        }
        using EstateService restarted = EstateService.Open(run.Estate.Root);
        RunSettleResponse response = restarted.RunSettle(started.Run, new(run.Estate.OperatorId));
        Assert.False(response.Ok);
        Assert.Equal("unresolved", response.Problem!.Code);
        Assert.NotEmpty(response.Message);
        RunRow row = Assert.Single(restarted.Runs().Runs);
        Assert.Equal("dispatched", row.Status);
        Assert.Null(row.Usage);
        Assert.Equal(60, row.Charged.GetProperty("wallMinutes").GetInt64());
        Assert.Contains(row.Signals!, s => s.Code == "audit-unverified");
        Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderExitObservationsRecoverOnlyTheFactsTheyRetained(bool existingAudit)
    {
        using var run = new RunEstate("""{"wallMinutes":60,"tokens":5000,"currency":{"amount":1000,"unit":"USD"},"attemptsPerStep":3}""");
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        RunResponse started = run.Start("claude-code", "claude", run.Report("older-settlement"));
        Assert.True(started.Ok, started.Message);
        RunDirectory directory = Failed(run, started.Run);
        run.Service.Dispose();
        JsonObject state = JsonNode.Parse(File.ReadAllText(directory.StateFile))!.AsObject();
        foreach (string field in new[] { "outcome", "scope", "minuteTicks", "settlementPrepared" }) state.Remove(field);
        // Older workers retained event-stream signals, but not settlement-only attempt/scope audits.
        var original = new RunSignal("audit-unverified", "warning", state["exitedAt"]!.GetValue<string>(), new string('a', 2000));
        state["signals"] = existingAudit ? JsonSerializer.SerializeToNode(new[] { original }, ProtocolJson.Options) : new JsonArray();
        File.WriteAllText(directory.StateFile, state.ToJsonString());
        using EstateService restarted = EstateService.Open(run.Estate.Root);
        RunRow row = Assert.Single(restarted.Runs().Runs);
        Assert.Equal("observed-success", row.Status);
        Assert.Equal(1734, row.Charged.GetProperty("tokens").GetInt64());
        Assert.Equal(1000, row.Charged.GetProperty("currency").GetProperty("amount").GetInt64());
        Assert.Equal(["currency"], row.Usage!.Value.GetProperty("unreported").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("not retained", row.Scope!.Problem, StringComparison.Ordinal);
        if (existingAudit) Assert.Equal(original, Assert.Single(row.Signals!));
        else Assert.Contains("UTC launch and exit", Assert.Single(row.Signals!).Summary, StringComparison.Ordinal);
        Assert.Contains(AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("summary").GetString()!.Contains("UTC launch and exit", StringComparison.Ordinal));
        Assert.Null(directory.Read()!.Problem);
        Assert.Equal(0, restarted.Check().Errors);
    }

    [Fact]
    public void RecoveryUsesStopAuthorityAndRefusesUnknownRuns()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        string goalHash = run.Service.Goal(run.Goal)!.Json.GetProperty("goal").GetProperty("hash").GetString()!;
        string expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
        RunEstate.Ok(run.Service.NewRecord(new("custody-exception", run.Estate.OperatorId,
            RunEstate.Json($$"""{"reason":"Exercise settlement with a revocable requesting device and a stand-in.","expires":"{{expires}}"}"""),
            $"goal:{run.Goal}:{goalHash}")));
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        RunResponse started = run.Start("generic", "generic", run.Report("settlement-authority"));
        Assert.True(started.Ok, started.Message);
        _ = Failed(run, started.Run);
        run.Service.BeforeValidatingRecords = null;
        Assert.Equal("refused", run.Service.RunSettle(started.Run, new(run.Estate.BuilderId)).Problem!.Code);
        Assert.Equal("refused", run.Service.RunSettle(started.Run, new("unknown")).Problem!.Code);
        Assert.Equal("not-found", run.Service.RunSettle(Ulid.NewUlid().Value, new(run.Estate.OperatorId)).Problem!.Code);
        Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
        string device = RunEstate.Ok(run.Service.DeviceCreate(new(run.Estate.OperatorId, "Settlement requester", "file"))).Id;
        RunEstate.Ok(run.Service.DeviceAdmit(new(device, run.Estate.OperatorId, "Admit the requesting device.")));
        DeviceStanding standing = run.Service.Standing(device)!;
        RunEstate.Ok(run.Service.DeviceRevoke(new(device, run.Estate.OperatorId, "Revoked before settlement.")));
        using (run.Service.ForDeviceRequest(standing.Identity, standing.PublicKey))
            Assert.Equal("denied", run.Service.RunSettle(started.Run, new(run.Estate.OperatorId)).Problem!.Code);
        Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
        RunSettleResponse recovered = run.Service.RunSettle(started.Run, new(run.Estate.OperatorId));
        Assert.True(recovered.Ok, recovered.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnObservationMarkedSettledWithoutARecordSettlesOnlyOnceTheRecordsAreChecked(bool missingFact)
    {
        using var run = new RunEstate();
        run.Service.BeforeValidatingRecords = RefuseSettlement;
        RunResponse started = run.Start("generic", "generic", run.Report("marked-settlement"));
        Assert.True(started.Ok, started.Message);
        RunDirectory directory = Failed(run, started.Run);
        // Dispose joins the original supervisor before marking its test-owned snapshot as an earlier worker did.
        run.Service.Dispose();
        RunObservedState state = directory.Read()!;
        state.Settled = "observed-success";
        if (missingFact) state.ExitCode = null;
        directory.Write(state);

        // Startup never settles a marked observation: its settlement may sit on a branch not yet merged.
        using EstateService restarted = EstateService.Open(run.Estate.Root);
        Assert.Equal("dispatched", Assert.Single(restarted.Runs().Runs).Status);
        RunSettleResponse withoutCheck = restarted.RunSettle(started.Run, new(run.Estate.OperatorId));
        Assert.False(withoutCheck.Ok);
        Assert.Equal("unresolved", withoutCheck.Problem!.Code);
        Assert.Contains("--records-checked", withoutCheck.Message, StringComparison.Ordinal);
        Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);

        RunSettleResponse checkedSettlement = restarted.RunSettle(started.Run, new(run.Estate.OperatorId) { RecordsChecked = true });
        if (missingFact)
        {
            // Confirmation never invents a fact the observation lacks.
            Assert.False(checkedSettlement.Ok);
            Assert.Equal("unresolved", checkedSettlement.Problem!.Code);
            Assert.Equal("dispatched", Assert.Single(restarted.Runs().Runs).Status);
            Assert.Equal(2, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
            return;
        }

        Assert.True(checkedSettlement.Ok, checkedSettlement.Message);
        RunRow row = Assert.Single(restarted.Runs().Runs);
        Assert.Equal("observed-success", row.Status);
        JsonElement recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body;
        Assert.Contains(recorded.GetProperty("evidence").EnumerateArray(),
            e => e.GetProperty("summary").GetString()!.Contains("marked settled as observed-success", StringComparison.Ordinal));
        int revisions = AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count;
        Assert.Equal(3, revisions);
        // A recorded settlement is never written twice, with or without the confirmation, on request or at startup.
        Assert.Equal("already-settled", restarted.RunSettle(started.Run, new(run.Estate.OperatorId) { RecordsChecked = true }).Problem!.Code);
        restarted.Dispose();
        using EstateService again = EstateService.Open(run.Estate.Root);
        Assert.Equal(revisions, AgentRuns.Find(run.Estate.Load(), started.Run)!.Revisions.Count);
        Assert.Equal(0, again.Check().Errors);
    }

    private static RunDirectory Failed(RunEstate run, string id)
    {
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, id);
        run.WaitUntil(() => directory.Read() is { ExitedAt: not null, Problem: not null, Settled: null }, "the failed settlement write");
        return directory;
    }

    private static RecordEnvelope? Settlement(IReadOnlyList<AuthoredRecord> records) => records
        .Select(r => r.Envelope).FirstOrDefault(r => r.Type == "operation" && EstateModel.BodyString(r, "status") is "observed-success" or "known-failure");

    private static void RefuseSettlement(IReadOnlyList<AuthoredRecord> records)
    {
        if (Settlement(records) is not null) throw new IOException("Synthetic settlement validation failure.");
    }

    // This test launcher explicitly reads the stand-in's price; shipped launchers may leave it unreported.
    private sealed class PriceReadingLauncher : IAgentLauncher
    {
        private readonly GenericLauncher process = new();
        private readonly ClaudeCodeLauncher usage = new();
        public string Launcher => process.Launcher;
        public bool Generic => true;
        public ProviderDescriptor Descriptor => process.Descriptor;
        public IReadOnlyList<string> Command(LaunchContext context) => process.Command(context);
        public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => process.Verify(settings, environment);
        public HarnessSignal? Read(string line)
        {
            HarnessSignal? signal = usage.Read(line);
            if (signal is null) return null;
            using JsonDocument json = JsonDocument.Parse(line);
            return json.RootElement.TryGetProperty("total_cost_usd", out JsonElement price)
                ? signal with { Price = (long)Math.Ceiling(price.GetDecimal() * 100), PriceUnit = "USD" } : signal;
        }
    }
}
