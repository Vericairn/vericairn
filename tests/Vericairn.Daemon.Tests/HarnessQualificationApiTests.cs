// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class HarnessQualificationApiTests
{
    [Theory]
    [InlineData("/v1/harness-qualifications")]
    [InlineData("/v1/harness-qualifications/")]
    [InlineData("/V1/HARNESS-QUALIFICATIONS")]
    [InlineData("/v1/harness-qualifications?local=true")]
    public async Task PairedNetworkDeviceCannotProbeButSignedLoopbackCan(string route)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var launcher = new ProbeLauncher();
        var providers = new ProviderRegistry();
        providers.Register(launcher);
        harness.Host.Service.Providers = providers;
        var request = new HarnessQualifyRequest("codex", harness.Estate.OperatorId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "synthetic-local-command" });
        RuntimeConnection paired = harness.Pair(harness.Estate.OperatorId);
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        (HttpStatusCode status, string body) = await lan.SendSignedAsync(signer, HttpMethod.Post, route, JsonSerializer.Serialize(request, ProtocolJson.Options));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("runtime's own machine", JsonSerializer.Deserialize<Problem>(body, ProtocolJson.Options)!.Message, StringComparison.Ordinal);
        Assert.Equal(0, launcher.Probes);
        Assert.Empty(harness.Socket.Records("harness-qualification"));

        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(harness.OperatorDevice)!);
        WriteOutcome qualified = loopback.QualifyHarness(request);
        Assert.True(qualified.Ok, qualified.Problem?.Message);
        Assert.Equal(1, launcher.Probes);
        Assert.Equal("synthetic-local-command", launcher.CommandOverride);
        Assert.Equal(qualified.Written!.Id, Assert.Single(harness.Socket.Records("harness-qualification")).Id);
    }

    /// <summary>Local synthetic probe evidence only; neither Probe nor Command starts a process.</summary>
    private sealed class ProbeLauncher : IAgentLauncher
    {
        public string Launcher => "codex";
        public ProviderDescriptor Descriptor { get; } = new("probe", IAgentLauncher.Contract, "implemented", ["probe"], ["Synthetic local probes."],
            [new ProviderSetting("command", "A synthetic local command override.", false)]);
        public int Probes { get; private set; }
        public string? CommandOverride { get; private set; }
        public RunPreflight Probe(LaunchContext context)
        {
            Probes++;
            CommandOverride = context.Settings["command"];
            return new([], true, true, "1.0.0", Probes: new("1.0.0", HarnessQualificationType.ProbeNames.ToDictionary(n => n, _ => "sha256:" + new string('a', 64), StringComparer.Ordinal)));
        }
        public IReadOnlyList<string> Command(LaunchContext context) => throw new InvalidOperationException("Qualification never dispatches a harness.");
    }
}
