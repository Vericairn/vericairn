// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class RemotePolicyJourneyTests
{
    [Fact]
    public async Task PhoneReadsTheMappedAttentionPolicyAndHandoffRoutesThenReopensOfflineWithoutSending()
    {
        DaemonTestHome.Ensure();
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using EstateService service = EstateService.Open(policy.Estate.Root);
        using var routes = new ObservedRoutes(new PolicyAdoptionApiTests.RouteHandler(service));
        using var client = new VericairnClient(new HttpClient(routes) { BaseAddress = new Uri("http://fixture.invalid") });
        using var phone = new Phone(client, policy);
        var clock = new JourneyClock();
        var remote = new RemoteViewModel(phone, clock);
        await remote.RefreshCommand.ExecuteAsync();
        Assert.Equal(ConnectionStates.Current, remote.ConnectionState);
        string[] records = policy.Estate.Load().Records.Select(r => r.ComputedHash).ToArray();
        var expected = service.Policies();
        foreach (string state in new[] { "unmet", "excepted", "due-within-grace", "discharged-late", "unavailable" })
            Assert.Contains(remote.Attention, a => a.Notice?.Policy is { } p && (p.Finding.Verdict == state || p.Finding.Disposition == state));
        Assert.Contains(remote.Attention, a => a.Kind == "failed check");
        Assert.Contains(remote.Attention, a => a.Kind == "blocked");
        Assert.Contains(remote.Attention, a => a.Kind == "information");
        foreach (var notice in remote.Attention.Where(a => a.Notice?.Policy is not null).Select(a => a.Notice!))
        {
            Assert.Equal(expected.Basis.Digest, notice.Policy!.Basis.Digest);
            Assert.Equal(expected.Basis.AsOf, notice.AsOf);
        }
        remote.SelectedAttention = Assert.Single(remote.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Late.Id);
        AttentionNotice original = remote.SelectedAttention.Notice!;
        await remote.OpenAttentionCommand.ExecuteAsync();
        await remote.PolicyHandoffCommand.ExecuteAsync();
        Assert.NotNull(remote.PolicyDetail!.Handoff!.Package);
        Assert.Contains(seed.Result.ComputedHash, remote.PolicyDetail.Material, StringComparison.Ordinal);
        Assert.Contains(seed.Review.ComputedHash, remote.PolicyDetail.Material, StringComparison.Ordinal);
        Assert.Contains(seed.Proof.ComputedHash, remote.PolicyDetail.Material, StringComparison.Ordinal);
        Assert.Contains(original.Target!.Hash!, remote.PolicyDetail.DesktopRoute, StringComparison.Ordinal);
        Assert.Contains("GET /v1/handoff", routes.Calls);
        Assert.Contains(routes.Calls, c => c.StartsWith("GET /v1/estate-policies/access", StringComparison.Ordinal));
        Assert.All(routes.Calls, c => Assert.StartsWith("GET ", c, StringComparison.Ordinal));
        Assert.False(remote.SubmitDecisionCommand.CanExecute(null));
        Assert.Equal(records, policy.Estate.Load().Records.Select(r => r.ComputedHash));

        await remote.ClosePolicyCommand.ExecuteAsync();
        remote.SelectedAttention = Assert.Single(remote.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Due.Id);
        await remote.AcknowledgeCommand.ExecuteAsync();
        Assert.Equal("acknowledged", remote.SelectedAttention!.AttentionState);
        Assert.False(remote.SelectedAttention.Notice!.Policy!.Finding.Duty!.Completed);
        Assert.Equal(JsonSerializer.Serialize(expected, ProtocolJson.Options), JsonSerializer.Serialize(service.Policies(), ProtocolJson.Options));
        Assert.Single(routes.Calls, c => c.StartsWith("POST ", StringComparison.Ordinal));
        Assert.Contains("POST /v1/attention/acknowledge", routes.Calls);
        Assert.Equal(records, policy.Estate.Load().Records.Select(r => r.ComputedHash));

        phone.Offline = true;
        clock.Advance(TimeSpan.FromMinutes(5));
        await remote.RefreshCommand.ExecuteAsync();
        int sent = routes.Calls.Count;
        var restarted = new RemoteViewModel(phone, clock);
        restarted.SelectedAttention = Assert.Single(restarted.Attention, a => a.Notice?.Key == original.Key);
        await restarted.OpenAttentionCommand.ExecuteAsync();
        await restarted.PolicyHandoffCommand.ExecuteAsync();
        await restarted.AcknowledgeCommand.ExecuteAsync();
        restarted.RefreshFreshness();
        Assert.Equal(sent, routes.Calls.Count);
        Assert.Contains("300 seconds old", restarted.PolicyFreshness, StringComparison.Ordinal);
        Assert.StartsWith("cached ", restarted.PolicyFreshness, StringComparison.Ordinal);
        Assert.Contains(seed.Review.ComputedHash, restarted.PolicyDetail!.Material, StringComparison.Ordinal);
        Assert.Contains(seed.Proof.ComputedHash, restarted.PolicyDetail.Material, StringComparison.Ordinal);
        Assert.Contains(restarted.Attention, a => a.Notice?.Policy?.Finding.Verdict == "unavailable");
        Assert.False(restarted.AttentionEmpty);
    }

    private sealed class ObservedRoutes(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Phone : IRemoteConnections, IDisposable
    {
        private readonly IEstateApi client;
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("vericairn-policy-journey-");
        private readonly LocalNetworkConnections storage;
        private readonly RuntimeConnection connection;

        internal Phone(IEstateApi client, PolicyFixture policy)
        {
            this.client = client;
            storage = new(directory.FullName, new FileKeyStore(directory.FullName));
            string publicKey = policy.OperatorDevice.Body.GetProperty("publicKey").GetString()!;
            connection = new("phone-connection", "fixture.invalid:7443", policy.Estate.Estate.Id, "Test estate", publicKey,
                policy.OperatorDevice.Id, publicKey, policy.Estate.OperatorId, "Test operator", ["local-network"], "2026-11-01T12:00:00Z");
        }

        internal bool Offline { get; set; }
        public IReadOnlyList<RuntimeConnection> List() => [connection];
        public Task<RuntimeConnection> PairAsync(string address, string code, string deviceName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConnectionStatus> ProbeAsync(RuntimeConnection selected, CancellationToken cancellationToken) => Task.FromResult(new ConnectionStatus(selected,
            Offline ? ConnectionStates.Cached : ConnectionStates.Current, Offline ? "Offline fixture." : "Mapped route fixture."));
        public IEstateApi Connect(RuntimeConnection selected) => client;
        public RemoteObservation? LoadObservation(RuntimeConnection selected) => storage.LoadObservation(selected);
        public void SaveObservation(RuntimeConnection selected, RemoteObservation observation) => storage.SaveObservation(selected, observation);
        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class JourneyClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}
