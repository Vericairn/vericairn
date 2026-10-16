// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunExecutionTests
{
    [Fact]
    public void AReportedInventoryChangeRemainsADiscrepancyWhenTheOriginalInventoryReturns()
    {
        string admitted = Vericairn.Records.Hashing.ContentHash.Sha256("admitted"u8);
        string changed = Vericairn.Records.Hashing.ContentHash.Sha256("changed"u8);
        var observation = new RunExecutionObservation(JsonSerializer.SerializeToElement(new { environment = new { inventoryHash = admitted } }));
        observation.Add(new HarnessExecution(InventoryHash: changed));
        observation.Add(new HarnessExecution(InventoryHash: admitted));
        Assert.Equal("inventoryHash", Assert.Single(observation.Snapshot()["discrepancies"]!.AsArray())!.GetValue<string>());
        Assert.Equal(2, observation.Snapshot()["reports"]!.AsArray().Count);
    }

    [Fact]
    public void ReportedResolvedAliasIsNotAChangeAndAnActualChangeIsNeverErased()
    {
        var observation = new RunExecutionObservation(RunEstate.Json("""{"requested":{"model":"alias","effort":"high"},"resolvedRevision":"revision-1"}"""));
        observation.Add(new HarnessExecution(Model: "revision-1", Effort: "high"));
        Assert.Empty(observation.Snapshot()["discrepancies"]!.AsArray());
        observation.Add(new HarnessExecution(Model: "model-b", Effort: "low"));
        observation.Add(new HarnessExecution(Model: "revision-1", Effort: "high"));
        Assert.Equal(2, observation.Snapshot()["discrepancies"]!.AsArray().Count);
        Assert.Equal(2, observation.Snapshot()["reports"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("execution", "model-a", "high", 0)]
    [InlineData("execution-different", "model-b", "low", 2)]
    public void SettlementRecordsOnlyHarnessReportsAndEveryDiscrepancy(string scenario, string model, string effort, int discrepancies)
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", scenario, run.Report(scenario));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        JsonElement facts = settled.ExecutionObserved!.Value;
        JsonElement report = Assert.Single(facts.GetProperty("reports").EnumerateArray());
        Assert.Equal(model, report.GetProperty("model").GetString());
        Assert.Equal(effort, report.GetProperty("effort").GetString());
        Assert.Equal(discrepancies, facts.GetProperty("discrepancies").GetArrayLength());
        Assert.Contains("provider", facts.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void ContextIdentityAndAllocationAreRecordedWithoutInventingMeasurement()
    {
        using var run = new RunEstate(budget: """{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        string hash = Vericairn.Records.Hashing.ContentHash.Sha256("synthetic-package"u8);
        RunResponse started = run.Service.RunStart(new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude-silent", run.Report("package")) },
            Allocation: RunEstate.Json("""{"inputTokens":600,"outputTokens":300}"""), ContextPackage: hash));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(hash, settled.Execution!.Value.GetProperty("contextPackage").GetString());
        Assert.Equal(600, settled.Execution.Value.GetProperty("allocation").GetProperty("inputTokens").GetInt64());
        Assert.Equal(1000, settled.Charged.GetProperty("tokens").GetInt64());
    }

    [Fact]
    public void InvalidReportsStayUnknownWithoutPersistingUnsafeValues()
    {
        var observation = new RunExecutionObservation(RunEstate.Json("""{"requested":{"model":"model-a","effort":"provider-default"}}"""));
        observation.Add(new HarnessExecution(Model: "../unsafe", Effort: "provider-default", Tools: ["../unsafe"]));
        JsonObject snapshot = observation.Snapshot();
        Assert.Empty(snapshot["reports"]!.AsArray());
        Assert.DoesNotContain("../unsafe", snapshot.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(snapshot["unknown"]!.AsArray(), n => n!.GetValue<string>() == "effort");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntentSelectionCannotBeReplacedOrRemovedAtSettlement(bool remove)
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "claude", run.Report("immutable"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        AgentRuns.Run found = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonObject changed = JsonNode.Parse(found.Latest.Body.GetRawText())!.AsObject();
        if (remove) changed.Remove("execution");
        else changed["execution"]!["requested"]!["model"] = "model-b";
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("operation", run.Estate.OperatorId, RunEstate.Json(changed.ToJsonString()),
            Subject: "assignment:" + run.Assignment, Supersedes: found.Latest.Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "operation.execution-kept");
    }

    [Fact]
    public void AllocationOverTheReservationAndUnknownConfigurationRefuseBeforeDispatch()
    {
        using var run = new RunEstate(budget: """{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""");
        var request = new RunStartRequest(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report("refused")) },
            Allocation: RunEstate.Json("""{"inputTokens":700,"outputTokens":400}"""));
        Assert.False(run.Service.RunStart(request).Ok);
        Assert.False(run.Service.RunStart(request with { Allocation = null, Configuration = "unconfigured" }).Ok);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.False(File.Exists(run.Report("refused")));
    }

    [Fact]
    public void IntentPinsSelectionBeforeDispatchAndSettlementKeepsUnknownFactsExplicit()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("claude-code", "claude", run.Report("selection"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        AgentRuns.Run found = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonElement selection = found.Intent.Body.GetProperty("execution");
        Assert.Equal("model-a", selection.GetProperty("requested").GetProperty("model").GetString());
        Assert.Equal("disabled", selection.GetProperty("adapter").GetString());
        Assert.Equal("unknown", selection.GetProperty("contextPackage").GetString());
        Assert.Equal(selection.GetRawText(), found.Latest.Body.GetProperty("execution").GetRawText());
        Assert.Contains("model", found.Latest.Body.GetProperty("executionObserved").GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
