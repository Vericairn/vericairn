// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class CommunicationRunJourneyTests
{
    [Theory]
    [InlineData("codex", "short")]
    [InlineData("codex", "standard")]
    [InlineData("codex", "comprehensive")]
    [InlineData("claude", "short")]
    [InlineData("claude", "standard")]
    [InlineData("claude", "comprehensive")]
    public void D08D_BoundedNativeRunsPinSelectionAndContextBeforeLaunchAtEveryDepth(string harness, string depth)
    {
        RunEstate run = null!;
        string? intentId = null;
        byte[]? intentBytes = null;
        using var fixture = new NativeRun(harness, (probe, context) =>
        {
            Assert.False(File.Exists(probe.Report));
            AgentRuns.Run admitted = Assert.Single(AgentRuns.All(run.Estate.Load()));
            Assert.Equal("not-started", admitted.Status);
            Assert.Single(admitted.Revisions);
            Assert.False(admitted.Dispatched);
            intentId = admitted.Id;
            intentBytes = File.ReadAllBytes(Path.Combine(run.Estate.Root, admitted.Intent.ExpectedPath));
            JsonElement execution = admitted.Intent.Body.GetProperty("execution");
            Assert.Equal("model-b", execution.GetProperty("requested").GetProperty("model").GetString());
            Assert.Equal("low", execution.GetProperty("requested").GetProperty("effort").GetString());
            Assert.Equal("disabled", execution.GetProperty("adapter").GetString());
            Assert.Equal("unknown", execution.GetProperty("resolvedRevision").GetString());
            JsonElement source = Assert.Single(execution.GetProperty("provenance").EnumerateArray(), p => p.GetProperty("scope").GetString() == "run");
            Assert.Equal("model-b", source.GetProperty("policy").GetProperty("choices").GetProperty("model").GetString());
            Assert.Equal("low", source.GetProperty("policy").GetProperty("choices").GetProperty("effort").GetString());
            Assert.Equal(1000, admitted.Cap.GetProperty("tokens").GetInt64());
            Assert.Contains("low", probe.Launcher.ExecutionCapabilities.Efforts);
            Assert.Equal(CanonicalJson.Serialize(execution), CanonicalJson.Serialize(context.Execution!.Value));

            ContextPackage package = RetainedContext(run, admitted.Id);
            Assert.True(ContextAccounting.Verify(package));
            Assert.Equal(package.Hash, execution.GetProperty("contextPackage").GetString());
            JsonElement allocation = execution.GetProperty("allocation");
            Assert.Equal(600, allocation.GetProperty("inputTokens").GetInt64());
            Assert.Equal(300, allocation.GetProperty("outputTokens").GetInt64());
            Assert.Equal(package.Bytes, allocation.GetProperty("context").GetProperty("bytes").GetInt64());
            Assert.Equal(1_000_000, allocation.GetProperty("context").GetProperty("maxBytes").GetInt64());
            JsonElement accounting = package.Payload.GetProperty("accounting");
            Assert.Equal(JsonValueKind.Null, accounting.GetProperty("tokens").ValueKind);
            Assert.Equal("not claimed", accounting.GetProperty("modelFit").GetString());
            JsonElement environment = execution.GetProperty("environment");
            Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(environment.GetProperty("inventory"))), environment.GetProperty("inventoryHash").GetString());
        }, budget: """{"wallMinutes":60,"attemptsPerStep":3,"tokens":1000}""", keepOperatorKey: true);
        run = fixture.Run;
        RunEstate.Ok(run.Service.CommunicationSet(new(run.Estate.OperatorId, "person", new("none", depth, depth, depth, depth), run.Estate.OperatorId)));
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);
        fixture.Probe.Behavior("execution", "requested");
        RunResponse started = run.Service.RunStart(new(run.Assignment, fixture.Probe.Launcher.Launcher, run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: fixture.Probe.Context.Settings, Execution: RunEstate.Json("""{"model":"model-b","effort":"low","adapter":"disabled"}"""),
            Allocation: RunEstate.Json("""{"inputTokens":600,"outputTokens":300,"maxBytes":1000000}""")));
        Assert.True(started.Ok, started.Message);
        Assert.Equal(intentId, started.Run);
        RunRow settled = run.Settled(started.Run);
        WaitUntil(() => Assert.Single(run.Service.Runs().Goals).Status == "budget-limited");
        Assert.Equal("observed-success", settled.Status);
        JsonElement observation = settled.ExecutionObserved!.Value;
        JsonElement reported = Assert.Single(observation.GetProperty("reports").EnumerateArray());
        Assert.Equal("model-b", reported.GetProperty("model").GetString());
        Assert.Equal("low", reported.GetProperty("effort").GetString());
        Assert.Equal(["modelRevision", "provider", "destination", "harnessVersion", "tools", "inventoryHash"],
            observation.GetProperty("unknown").EnumerateArray().Select(v => v.GetString()));
        Assert.Empty(observation.GetProperty("discrepancies").EnumerateArray());
        Assert.False(settled.Usage!.Value.TryGetProperty("tokens", out _));
        Assert.Contains(settled.Usage.Value.GetProperty("unreported").EnumerateArray(), v => v.GetString() == "tokens");
        Assert.Equal(1000, settled.Charged.GetProperty("tokens").GetInt64());

        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(intentBytes, File.ReadAllBytes(Path.Combine(run.Estate.Root, recorded.Intent.ExpectedPath)));
        Assert.Equal(["not-started", "dispatched", "observed-success"], recorded.Revisions.Select(r => EstateModel.BodyString(r, "status")));
        Assert.All(recorded.Revisions, r => Assert.Equal(CanonicalJson.Serialize(recorded.Intent.Body.GetProperty("execution")), CanonicalJson.Serialize(r.Body.GetProperty("execution"))));
        Dictionary<string, byte[]> originals = recorded.Revisions.ToDictionary(r => r.ExpectedPath, r => File.ReadAllBytes(Path.Combine(run.Estate.Root, r.ExpectedPath)), StringComparer.Ordinal);
        byte[] canonicalRuns = Canonical(run.Service.Runs());
        byte[] canonicalStatus = CanonicalJson.SerializeUtf8(run.Service.Status().Json);
        byte[] canonicalProgress = CanonicalJson.SerializeUtf8(run.Service.Progress().Json);
        byte[] canonicalHandoff = Canonical(run.Service.Handoff().Package!);
        byte[] retained = File.ReadAllBytes(new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).ContextFile);
        int count = run.Estate.Load().Records.Count;
        Assert.Equal(depth, run.Service.Communication(run.Estate.OperatorId).Fields["status"].Value);
        foreach (string viewedDepth in CommunicationResolution.Depths)
        {
            CommunicationProfile profile = run.Service.Communication(run.Estate.OperatorId, view: new("none", viewedDepth, viewedDepth, viewedDepth, viewedDepth));
            Assert.Equal(viewedDepth, profile.Fields["result"].Value);
            Assert.Contains("view", profile.Fields["result"].Source, StringComparison.Ordinal);
            string original = JsonSerializer.Serialize(settled, ProtocolJson.Options);
            Assert.EndsWith(original, CommunicationPresentation.Protected(profile, "result", settled.Identity, "Observed run facts and unknowns follow.", original), StringComparison.Ordinal);
            _ = CommunicationPresentation.Status(run.Service.Status(), profile);
            Assert.Equal(canonicalRuns, Canonical(run.Service.Runs()));
            Assert.Equal(canonicalStatus, CanonicalJson.SerializeUtf8(run.Service.Status().Json));
            Assert.Equal(canonicalProgress, CanonicalJson.SerializeUtf8(run.Service.Progress().Json));
            Assert.Equal(canonicalHandoff, Canonical(run.Service.Handoff().Package!));
            Assert.Equal(retained, File.ReadAllBytes(new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).ContextFile));
            foreach ((string path, byte[] bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(run.Estate.Root, path)));
            Assert.Equal(count, run.Estate.Load().Records.Count);
        }
        Assert.Empty(run.Service.Objects());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("adapter", "claude", "{\"adapter\":\"local\"}", "EFF-01")]
    [InlineData("hook", "inventory-unexpected", "{}", "inventory differs")]
    [InlineData("tool", "inventory-tool", "{}", "inventory differs")]
    [InlineData("environment", "inventory-environment", "{}", "unadmitted environment need")]
    [InlineData("model-fallback", "claude", "{\"model\":\"ambient-model\"}", "ambient fallback is not permitted")]
    [InlineData("provider-fallback", "claude", "{\"provider\":\"other-provider\"}", "ambient fallback is not permitted")]
    [InlineData("route-fallback", "claude", "{\"destination\":\"other-inference\"}", "ambient fallback is not permitted")]
    [InlineData("unsupported-effort", "claude", "{\"effort\":\"xhigh\"}", "ambient fallback is not permitted")]
    public void D08D_UnadmittedChoicesAndComponentsRefuseBeforeAnyIntentOrDispatch(string name, string scenario, string execution, string reason)
    {
        using var run = new RunEstate();
        string report = run.Report(name);
        int count = run.Estate.Load().Records.Count;
        byte[] before = CanonicalJson.SerializeUtf8(run.Service.Status().Json);
        RunResponse refused = run.Service.RunStart(new(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting(scenario, report) }, Execution: RunEstate.Json(execution)));
        Assert.False(refused.Ok);
        Assert.Equal("not-started", refused.Status);
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(report));
        Assert.Empty(run.Service.Runs().Runs);
        Assert.Equal(count, run.Estate.Load().Records.Count);
        Assert.Equal(before, CanonicalJson.SerializeUtf8(run.Service.Status().Json));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    internal static ContextPackage RetainedContext(RunEstate run, string id)
    {
        using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(new RunDirectory(run.Service.Session.Manifest.Estate, id).ContextFile));
        return retained.RootElement.GetProperty("context").Deserialize<ContextPackage>(ProtocolJson.Options)!;
    }

    internal static byte[] Canonical<T>(T value) => CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(value, ProtocolJson.Options));

    internal static void WaitUntil(Func<bool> condition)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!condition() && DateTimeOffset.UtcNow < until) Thread.Sleep(50);
        Assert.True(condition(), "The worker did not reach the expected state within twenty seconds.");
    }
}
