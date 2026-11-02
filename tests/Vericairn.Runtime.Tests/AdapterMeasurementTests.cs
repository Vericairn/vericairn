// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Representation;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Representation;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class AdapterMeasurementTests
{
    [Fact]
    public async Task ForwardedCountsSettleAndDisplayWithoutRetainingAuthentication()
    {
        using var upstream = new CountingAdapterUpstream();
        string brief = RunEstate.Brief.Replace("A widget.", "A widget using docs/eligible.md as optional context.", StringComparison.Ordinal);
        using var run = new RunEstate(brief: brief, recordFormat: 3);
        byte[] original = Encoding.UTF8.GetBytes(AdapterRepresentationTests.Eligible);
        ContextTests.AddDocument(run.Estate, "docs/eligible.md", original);
        var launcher = new MeasurementLauncher(new ReportedLauncher(new ClaudeCodeLauncher()), upstream.Address);
        run.Service.Providers.Register(launcher);
        string report = run.Report("adapter-counts");
        RunResponse started = run.Service.RunStart(new RunStartRequest(run.Assignment, launcher.Launcher, run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string> { ["command"] = FakeHarness.Setting("silent-recovery", report) },
            Execution: JsonSerializer.SerializeToElement(new { adapter = RepresentationContract.LocalLossless })));
        Assert.True(started.Ok, started.Message);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        using JsonDocument context = JsonDocument.Parse(File.ReadAllBytes(directory.ContextFile));
        string pin = context.RootElement.GetProperty("context").GetProperty("payload").GetProperty("originals").EnumerateArray()
            .Single(o => o.GetProperty("path").GetString() == "docs/eligible.md").GetProperty("pin").GetString()!;
        byte[] body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(AdapterRepresentationTests.Request("messages-array"))
            .Replace(AdapterRepresentationTests.Pin(AdapterRepresentationTests.Eligible), pin, StringComparison.Ordinal));
        RequestReduction.Result expected = RequestReduction.Reduce(body, p => p == pin ? original : null);
        string authentication = Guid.NewGuid().ToString("N");
        try
        {
            Assert.NotNull(launcher.Address);
            using HttpClient client = AdapterTransportTests.Client();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authentication);
            using HttpResponseMessage response = await client.PostAsync(new Uri(launcher.Address!, "messages"), AdapterTransportTests.Json(body), TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(expected.Body, Assert.Single(upstream.Requests).Body);
        }
        finally
        {
            File.WriteAllText(report + ".recover", "continue");
            File.WriteAllText(report + ".finish", "exit");
        }
        RunRow settled = run.Settled(started.Run);
        JsonElement measurement = settled.ExecutionObserved!.Value.GetProperty("adapter");
        Assert.Equal(1, measurement.GetProperty("requests").GetInt64());
        Assert.Equal(expected.BeforeBytes, measurement.GetProperty("eligibleBeforeBytes").GetInt64());
        Assert.Equal(expected.AfterBytes, measurement.GetProperty("eligibleAfterBytes").GetInt64());
        Assert.True(expected.AfterBytes < expected.BeforeBytes);
        Assert.Contains(AdapterPresentation.Describe(measurement), run.Service.Status().Text, StringComparison.Ordinal);
        Assert.Null(launcher.Address);
        Assert.Equal(original, run.Service.ContextRead(pin).Content);
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(directory.ContextFile)!, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(authentication, Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
        Assert.DoesNotContain(authentication, settled.ExecutionObserved.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void SettlementAndStatusCarryMeasurementsAndRemovalKeepsOriginalRetrieval()
    {
        string brief = RunEstate.Brief.Replace("A widget.", "A widget using docs/eligible.md as optional context.", StringComparison.Ordinal);
        using var run = new RunEstate(brief: brief, recordFormat: 3);
        byte[] original = Encoding.UTF8.GetBytes(AdapterRepresentationTests.Eligible);
        ContextTests.AddDocument(run.Estate, "docs/eligible.md", original);
        var launcher = new MeasurementLauncher(new ReportedLauncher(new ClaudeCodeLauncher()));
        run.Service.Providers.Register(launcher);
        RunResponse started = run.Service.RunStart(new RunStartRequest(run.Assignment, launcher.Launcher, run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string> { ["command"] = FakeHarness.Setting("claude", run.Report("adapter")) },
            Execution: JsonSerializer.SerializeToElement(new { adapter = RepresentationContract.LocalLossless })));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        JsonElement measurement = settled.ExecutionObserved!.Value.GetProperty("adapter");
        Assert.Equal(RepresentationContract.LocalLossless, measurement.GetProperty("name").GetString());
        Assert.Equal(0, measurement.GetProperty("eligibleBeforeBytes").GetInt64());
        Assert.Equal(0, measurement.GetProperty("eligibleAfterBytes").GetInt64());
        Assert.Equal("unknown", measurement.GetProperty("tokens").GetString());
        Assert.Null(launcher.Address);
        Assert.Contains("eligible bytes 0 -> 0", run.Service.Status().Text, StringComparison.Ordinal);
        Assert.Equal(CanonicalJson.Serialize(measurement), CanonicalJson.Serialize(run.Service.Status().Json.GetProperty("efficiencyAdapters")[0].GetProperty("measurement")));
        Assert.Equal(0, run.Service.Check().Errors);

        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        using JsonDocument context = JsonDocument.Parse(File.ReadAllBytes(directory.ContextFile));
        JsonElement source = context.RootElement.GetProperty("context").GetProperty("payload").GetProperty("originals").EnumerateArray()
            .Single(o => o.GetProperty("path").GetString() == "docs/eligible.md");
        Assert.False(source.GetProperty("mandatory").GetBoolean());
        string pin = source.GetProperty("pin").GetString()!;
        // Removing the implementation and changing the working copy cannot erase the run's exact original.
        run.Service.Providers.Register(new ReportedLauncher(new ClaudeCodeLauncher()));
        File.WriteAllText(Path.Combine(run.Estate.Root, "docs", "eligible.md"), "A later revision.");
        Assert.Equal(original, run.Service.ContextRead(pin).Content);
        File.Delete(Path.Combine(run.Estate.Root, "docs", "eligible.md"));
        Assert.Equal(original, run.Service.ContextRead(pin).Content);
        string retained = File.ReadAllText(directory.ContextFile);
        File.WriteAllText(directory.ContextFile, retained.Replace(Convert.ToBase64String(original), Convert.ToBase64String("tampered"u8), StringComparison.Ordinal));
        Assert.Null(run.Service.ContextRead(pin).Content);
        File.WriteAllText(directory.ContextFile, retained);
        Assert.Equal(original, run.Service.ContextRead(pin).Content);

        AgentRuns.Run found = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonObject invalid = JsonNode.Parse(found.Latest.Body.GetRawText())!.AsObject();
        invalid["executionObserved"]!["adapter"]!["eligibleBeforeBytes"] = -1;
        WriteOutcome refused = run.Service.NewRecord(new("operation", run.Estate.OperatorId, JsonSerializer.SerializeToElement(invalid),
            Subject: "assignment:" + run.Assignment, Supersedes: found.Latest.Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Location.Contains("eligibleBeforeBytes", StringComparison.Ordinal));

        invalid = JsonNode.Parse(found.Latest.Body.GetRawText())!.AsObject();
        invalid["executionObserved"]!["adapter"]!["address"] = "http://127.0.0.1:1";
        refused = run.Service.NewRecord(new("operation", run.Estate.OperatorId, JsonSerializer.SerializeToElement(invalid),
            Subject: "assignment:" + run.Assignment, Supersedes: found.Latest.Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "operation.execution");

        invalid["execution"]!["adapterAddress"] = "http://identity@127.0.0.1:1";
        invalid["executionObserved"]!["adapter"]!["address"] = "http://identity@127.0.0.1:1";
        refused = run.Service.NewRecord(new("operation", run.Estate.OperatorId, JsonSerializer.SerializeToElement(invalid),
            Subject: "assignment:" + run.Assignment, Supersedes: found.Latest.Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "execution.adapter-address");
    }

    private sealed class MeasurementLauncher(IAgentLauncher inner, Uri? upstream = null) : IAgentLauncher
    {
        public string Launcher => inner.Launcher;
        public ProviderDescriptor Descriptor => inner.Descriptor;
        public string? Harness => inner.Harness;
        public IReadOnlyList<string> EnvironmentNames => inner.EnvironmentNames;
        public RunCapabilities ExecutionCapabilities => inner.ExecutionCapabilities;
        public RunConfiguration? Configuration => inner.Configuration! with
        { AdapterPermission = new() { Adapter = RepresentationContract.LocalLossless, Launchers = [inner.Launcher] } };
        public LaunchContext Prepare(LaunchContext context) => inner.Prepare(context);
        public RunPreflight Probe(LaunchContext context) => inner.Probe(context);
        public IReadOnlyList<string> Command(LaunchContext context) => inner.Command(context);
        public HarnessSignal? Read(string line) => inner.Read(line);
        public Uri EfficiencyUpstream(LaunchContext context) => upstream ?? new("https://provider.invalid/api/");
        public Uri? Address { get; private set; }
        public LaunchContext ConfigureEfficiencyHook(LaunchContext context, Uri? address)
        {
            Address = address;
            return context with { AdapterAddress = address };
        }
    }
}
