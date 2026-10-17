// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class NativeSettlementTests
{
    [Theory]
    [InlineData("match")]
    [InlineData("model")]
    [InlineData("effort")]
    [InlineData("changed-back")]
    public void CodexRolloutReportsActualSelectionAndKeepsDiscrepancies(string scenario)
    {
        using var fixture = new NativeRun("codex");
        fixture.Probe.Behavior("rollout", scenario);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        JsonElement facts = settled.ExecutionObserved!.Value;
        JsonElement first = facts.GetProperty("reports").EnumerateArray().First(r => r.TryGetProperty("model", out _));
        Assert.Equal(scenario is "model" or "changed-back" ? "unexpected-model" : "model-a", first.GetProperty("model").GetString());
        Assert.Equal(scenario == "effort" ? "low" : "high", first.GetProperty("effort").GetString());
        string?[] discrepancies = facts.GetProperty("discrepancies").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal(scenario == "match" ? [] : new[] { scenario == "effort" ? "effort" : "model" }, discrepancies);
        Assert.DoesNotContain("model", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.DoesNotContain("effort", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("provider", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("unreadable")]
    public void AbsentOrUnreadableCodexRolloutLeavesSelectionUnknown(string scenario)
    {
        using var fixture = new NativeRun("codex");
        if (scenario != "missing") fixture.Probe.Behavior("rollout", scenario);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        JsonElement facts = fixture.Run.Settled(started.Run).ExecutionObserved!.Value;
        Assert.Empty(facts.GetProperty("reports").EnumerateArray());
        Assert.Contains("model", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("effort", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Empty(facts.GetProperty("discrepancies").EnumerateArray());
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Fact]
    public void ACodexRolloutThatCannotBeOpenedSuppliesNoFacts()
    {
        using var fixture = new NativeRun("codex");
        fixture.Probe.Behavior("rollout", "match");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        fixture.Run.Settled(started.Run);
        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string path = Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "codex", "sessions"), "rollout-*.jsonl", SearchOption.AllDirectories));
        Assert.Single(fixture.Probe.Launcher.ReadExecutionArtifacts(directory.Path));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Empty(fixture.Probe.Launcher.ReadExecutionArtifacts(directory.Path));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("prior")]
    [InlineData("during")]
    [InlineData("other-assignment")]
    public void SettlementCallsAttentionToMissingAgentAttemptDuringThisRun(string attempt)
    {
        using var fixture = new NativeRun("codex");
        var run = fixture.Run;
        fixture.Probe.Behavior("hold", "exit");
        if (attempt == "prior") RunEstate.Ok(run.Service.Attempt(new(run.Assignment, run.Estate.BuilderId, "a local work branch", "Earlier work.")));
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        try
        {
            if (attempt is "during" or "other-assignment")
            {
                string assignment = attempt == "during" ? run.Assignment : RunEstate.Ok(run.Service.Assign(new("W-1", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)))).Id;
                RunEstate.Ok(run.Service.Attempt(new(assignment, run.Estate.BuilderId, "a local work branch", "Inspect the brief.")));
            }
        }
        finally
        {
            File.WriteAllText(fixture.Probe.Report + ".continue", "exit");
        }
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        string evidence = string.Join(" ", AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Body.GetProperty("evidence").EnumerateArray().Select(e => e.GetProperty("summary").GetString()));
        const string missing = "settled without the agent's attempt";
        if (attempt == "during")
        {
            Assert.DoesNotContain(settled.Signals!, s => s.Code == "attempt-missing");
            Assert.DoesNotContain(missing, evidence, StringComparison.Ordinal);
        }
        else
        {
            RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "attempt-missing");
            Assert.Contains(missing, signal.Summary, StringComparison.Ordinal);
            Assert.NotNull(signal.Attention);
            EstateModel model = run.Estate.Load();
            var ask = model.ById[signal.Attention];
            Assert.Equal("open", Asks.State(model, ask).State);
            Assert.True(model.IsCurrent(ask));
            Assert.Equal(AgentRuns.Find(model, started.Run)!.Latest.Reference, ask.Subject);
            Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale" && f.Location.StartsWith("records/ask/", StringComparison.Ordinal));
            Assert.Contains(missing, evidence, StringComparison.Ordinal);
            Assert.Contains(CommunicationPresentation.Attention(run.Service), a => a.Kind == "incident" && a.Record == started.Run);
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
