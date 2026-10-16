// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class CommunicationRunRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void D08E_InterruptedDispatchIsReconciledUnderItsOriginalIdentityBeforeAnyRetry(bool dispatchRecorded)
    {
        using var run = new RunEstate("""{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""");
        if (!dispatchRecorded)
        {
            run.Service.WorkerOptions = run.Service.WorkerOptions with { BeforeDispatchRecord = () => throw new IOException("The stand-in dispatch write was interrupted.") };
        }
        string report = run.Report("interrupted");
        RunResponse started = run.Start("claude-code", "ack-stop", report, wallMinutes: 5);
        Assert.True(started.Ok, started.Message);
        Assert.Equal(dispatchRecorded ? "dispatched" : "not-started", started.Status);
        CommunicationRunJourneyTests.WaitUntil(() => File.Exists(report));
        using Process harness = Process.GetProcessById(Assert.Single(FakeHarness.Reports(report))["process"]!.GetValue<int>());
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        string launchedAt = directory.Read()!.LaunchedAt!;
        byte[] retained = File.ReadAllBytes(directory.ContextFile);
        AgentRuns.Run original = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        byte[] intent = File.ReadAllBytes(Path.Combine(run.Estate.Root, original.Intent.ExpectedPath));
        string identity = run.Row(started.Run).Identity;
        try
        {
            // Dispose abandons observation without claiming the stand-in stopped. A successor only has the retained facts.
            run.Reopen();
            CommunicationRunJourneyTests.WaitUntil(() => !run.Row(started.Run).Observed);
            Assert.False(harness.HasExited);
            Assert.Null(run.Row(started.Run).Usage);
            Assert.Equal(5, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
            Assert.Equal(retained, File.ReadAllBytes(directory.ContextFile));
            Assert.Equal(original.Intent.Body.GetProperty("execution").GetProperty("contextPackage").GetString(), CommunicationRunJourneyTests.RetainedContext(run, started.Run).Hash);

            // Record that dispatch occurred but its effect/usage are unknown, under the original operation, not a new identity.
            JsonObject uncertain = JsonNode.Parse(original.Latest.Body.GetRawText())!.AsObject();
            uncertain["status"] = "uncertain";
            uncertain["dispatchedAt"] = launchedAt;
            string uncertainId = Revise(run, original.Latest, uncertain);
            HandoffPackage handoff = run.Service.Handoff().Package!;
            Assert.Contains(identity, run.Service.Handoff().Text, StringComparison.Ordinal);
            Assert.Contains(CommunicationPresentation.Attention(run.Service, run.Estate.OperatorId), a => a.Kind == "uncertain-effect");
            int count = run.Estate.Load().Records.Count;
            foreach (string branch in new[] { "work/w-1", "work/retry-on-another-branch" })
            {
                string retryReport = run.Report(branch.Replace('/', '-'));
                RunResponse refused = run.Start("claude-code", "claude", retryReport, wallMinutes: 5, branch: branch);
                Assert.False(refused.Ok, refused.Message);
                Assert.Contains(started.Run, refused.Message, StringComparison.Ordinal);
                Assert.Contains("reconcile its effect under its original identity", refused.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(retryReport));
                Assert.Single(run.Service.Runs().Runs);
                Assert.Equal(count, run.Estate.Load().Records.Count);
            }

            // Only this synthetic process is terminated and observed. No provider conversation or external effect is inferred.
            harness.Kill(entireProcessTree: true);
            Assert.True(harness.WaitForExit(10000));
            JsonObject reconciled = (JsonObject)uncertain.DeepClone();
            reconciled["status"] = "known-failure";
            reconciled["reconciliation"] = "The stand-in process was terminated and its exit observed. No usage or provider conversation was recovered.";
            reconciled["observed"] = new JsonObject { ["summary"] = "The interrupted stand-in exited; its work did not complete.", ["at"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
            reconciled["usage"] = new JsonObject { ["unreported"] = new JsonArray("wallMinutes") };
            reconciled["executionObserved"] = new RunExecutionObservation(original.Intent.Body.GetProperty("execution")).Snapshot();
            string settlement = Revise(run, run.Estate.Load().ById[uncertainId], reconciled);
            Assert.Equal(identity, run.Row(started.Run).Identity);
            Assert.Equal(5, run.Row(started.Run).Charged.GetProperty("wallMinutes").GetInt64());
            Assert.False(run.Row(started.Run).Usage!.Value.TryGetProperty("wallMinutes", out _));
            Assert.Empty(run.Row(started.Run).ExecutionObserved!.Value.GetProperty("reports").EnumerateArray());
            Assert.Contains(run.Row(started.Run).ExecutionObserved!.Value.GetProperty("unknown").EnumerateArray(), v => v.GetString() == "model");
            ResumeResponse resumed = run.Service.ResumeHandoff(handoff);
            Assert.True(resumed.Json!.Value.GetProperty("changed").GetBoolean());
            Assert.Contains(resumed.Json.Value.GetProperty("superseded").EnumerateArray(), r => r.GetProperty("previous").GetString() == uncertainId && r.GetProperty("successor").GetString() == settlement);
            Assert.Contains("Nothing is restarted or accepted", resumed.Text, StringComparison.Ordinal);

            run.Service.WorkerOptions = run.Service.WorkerOptions with { BeforeDispatchRecord = null };
            RunResponse retried = run.Start("claude-code", "claude", run.Report("reconciled-retry"), wallMinutes: 5, branch: "work/retry-on-another-branch");
            Assert.True(retried.Ok, retried.Message);
            Assert.Equal("observed-success", run.Settled(retried.Run).Status);
            Assert.NotEqual(started.Run, retried.Run);
            Assert.NotEqual(identity, run.Row(retried.Run).Identity);
            AgentRuns.Run kept = AgentRuns.Find(run.Estate.Load(), started.Run)!;
            Assert.Equal(settlement, kept.Latest.Id);
            Assert.Equal(intent, File.ReadAllBytes(Path.Combine(run.Estate.Root, kept.Intent.ExpectedPath)));
            Assert.Equal(retained, File.ReadAllBytes(directory.ContextFile));
            Assert.All(kept.Revisions, r => Assert.Equal(CanonicalJson.Serialize(original.Intent.Body.GetProperty("execution")), CanonicalJson.Serialize(r.Body.GetProperty("execution"))));
            Assert.Empty(run.Service.Records("acceptance"));
            Assert.Equal(0, run.Service.Check().Errors);
        }
        finally
        {
            if (!harness.HasExited)
            {
                harness.Kill(entireProcessTree: true);
                Assert.True(harness.WaitForExit(10000));
            }
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void D08E_ChangedNativeInventoryRefusesDispatchAndRetainsTheAdmittedIntent(string harness)
    {
        RunEstate run = null!;
        string? changedSource = null;
        string? admittedExecution = null;
        string? admittedId = null;
        bool change = true;
        using var fixture = new NativeRun(harness, (probe, context) =>
        {
            if (!change) return;
            Assert.False(File.Exists(probe.Report));
            AgentRuns.Run admitted = Assert.Single(AgentRuns.All(run.Estate.Load()));
            Assert.Equal("not-started", admitted.Status);
            admittedId = admitted.Id;
            admittedExecution = CanonicalJson.Serialize(admitted.Intent.Body.GetProperty("execution"));
            changedSource = Path.Combine(context.Workspace, "AGENTS.md");
            Assert.False(File.Exists(changedSource));
            File.WriteAllText(changedSource, "Instructions changed after inventory admission.\n");
        });
        run = fixture.Run;
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok, refused.Message);
        Assert.Equal("known-failure", refused.Status);
        Assert.Equal(admittedId, refused.Run);
        Assert.Contains("inventory", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Probe.Report));
        AgentRuns.Run failed = AgentRuns.Find(run.Estate.Load(), refused.Run)!;
        Assert.Equal(["not-started", "known-failure"], failed.Revisions.Select(r => EstateModel.BodyString(r, "status")));
        Assert.False(failed.Dispatched);
        Assert.All(failed.Revisions, r => Assert.Equal(admittedExecution, CanonicalJson.Serialize(r.Body.GetProperty("execution"))));
        RunRow row = run.Row(refused.Run);
        Assert.Empty(row.Charged.EnumerateObject());
        Assert.Equal(0, row.Usage!.Value.GetProperty("wallMinutes").GetInt64());
        Assert.Empty(row.ExecutionObserved!.Value.GetProperty("reports").EnumerateArray());
        Assert.Contains(row.ExecutionObserved.Value.GetProperty("unknown").EnumerateArray(), v => v.GetString() == "inventoryHash");
        Assert.Equal(CommunicationRunJourneyTests.RetainedContext(run, refused.Run).Hash, row.Execution!.Value.GetProperty("contextPackage").GetString());

        // Restore the source, then require fresh admission. The failed intent's inventory is never relabeled.
        File.Delete(changedSource!);
        change = false;
        run.Reopen();
        RunResponse retried = fixture.Start();
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal("observed-success", run.Settled(retried.Run).Status);
        Assert.NotEqual(refused.Run, retried.Run);
        Assert.True(File.Exists(fixture.Probe.Report));
        Assert.Equal(admittedExecution, CanonicalJson.Serialize(run.Row(refused.Run).Execution!.Value));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void D08E_AReportedModelMismatchKeepsTheRequestAndUnknownModelFitHonest(string harness)
    {
        using var fixture = new NativeRun(harness);
        fixture.Probe.Behavior("execution", "different");
        RunEstate run = fixture.Run;
        RunResponse started = run.Service.RunStart(new(run.Assignment, fixture.Probe.Launcher.Launcher, run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: fixture.Probe.Context.Settings, Execution: RunEstate.Json("""{"model":"model-b","effort":"low"}""")));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal("model-b", settled.Execution!.Value.GetProperty("requested").GetProperty("model").GetString());
        JsonElement facts = settled.ExecutionObserved!.Value;
        Assert.Equal("model-a", Assert.Single(facts.GetProperty("reports").EnumerateArray()).GetProperty("model").GetString());
        Assert.Equal("model", Assert.Single(facts.GetProperty("discrepancies").EnumerateArray()).GetString());
        Assert.Contains(facts.GetProperty("unknown").EnumerateArray(), v => v.GetString() == "modelRevision");
        RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "execution");
        Assert.Equal("critical", signal.Severity);
        Assert.NotNull(signal.Attention);
        ContextPackage package = CommunicationRunJourneyTests.RetainedContext(run, started.Run);
        Assert.Equal(package.Hash, settled.Execution.Value.GetProperty("contextPackage").GetString());
        Assert.Equal(JsonValueKind.Null, package.Payload.GetProperty("accounting").GetProperty("tokens").ValueKind);
        Assert.Equal("not claimed", package.Payload.GetProperty("accounting").GetProperty("modelFit").GetString());
        byte[] canonical = CommunicationRunJourneyTests.Canonical(settled);
        foreach (string depth in CommunicationResolution.Depths)
        {
            CommunicationProfile profile = run.Service.Communication(run.Estate.OperatorId, view: new("none", depth, depth, depth, depth));
            string progress = run.Service.Progress().Text;
            Assert.Contains(signal.Summary, CommunicationPresentation.Protected(profile, "result", settled.Identity, "Model discrepancy.", progress), StringComparison.Ordinal);
            Assert.Equal(canonical, CommunicationRunJourneyTests.Canonical(run.Row(started.Run)));
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string Revise(RunEstate run, RecordEnvelope prior, JsonObject body) => RunEstate.Ok(run.Service.NewRecord(new("operation", run.Estate.OperatorId,
        RunEstate.Json(body.ToJsonString()), Subject: $"assignment:{prior.Subject!.Id}:{prior.Subject.Hash}", Supersedes: prior.Id))).Id;
}
