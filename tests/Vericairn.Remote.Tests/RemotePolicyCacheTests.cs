// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemotePolicyCacheTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AChangedNoticeCannotBorrowThePreviousRevisionsCachedHistory()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Excepted.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            string original = model.PolicyDetail!.Notice.Revision;
            await model.ClosePolicyCommand.ExecuteAsync();
            policy.Add("decision", policy.DecisionBody());
            await model.RefreshCommand.ExecuteAsync();
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Excepted.Id);
            Assert.NotEqual(original, model.SelectedAttention.Notice!.Revision);
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            await model.RefreshCommand.ExecuteAsync();
            int calls = connections.ApiCalls;
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Null(model.PolicyDetail!.Policy);
            Assert.Contains("not observed", model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Equal(calls, connections.ApiCalls);
            return true;
        });
    }

    [Fact]
    public async Task OfflineHistoryAndHandoffKeepTheirOwnAgeAndNeverQueueRequests()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Late.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            await model.PolicyHandoffCommand.ExecuteAsync();
            string observed = model.PolicyDetail!.AsOf;
            string basis = model.PolicyDetail.Policy!.Basis.Digest;
            string material = model.PolicyDetail.Material;
            string package = model.PolicyDetail.Handoff!.Package!.Hash;
            clock.Advance(TimeSpan.FromMinutes(1));
            await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("0 seconds old", model.ObservationAge);
            Assert.StartsWith("cached ", model.PolicyFreshness, StringComparison.Ordinal);
            Assert.Contains("60 seconds old", model.PolicyFreshness, StringComparison.Ordinal);
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            clock.Advance(TimeSpan.FromMinutes(2));
            await model.RefreshCommand.ExecuteAsync();
            var reopened = new RemoteViewModel(connections, clock);
            reopened.SelectedAttention = Assert.Single(reopened.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Late.Id);
            int calls = connections.ApiCalls;
            await reopened.OpenAttentionCommand.ExecuteAsync();
            await reopened.PolicyHandoffCommand.ExecuteAsync();
            await reopened.RefreshPolicyCommand.ExecuteAsync();
            await reopened.AcknowledgeCommand.ExecuteAsync();
            clock.Advance(TimeSpan.FromSeconds(7));
            reopened.RefreshFreshness();
            Assert.Equal(calls, connections.ApiCalls);
            Assert.Equal(observed, reopened.PolicyDetail!.AsOf);
            Assert.Equal(basis, reopened.PolicyDetail.Policy!.Basis.Digest);
            Assert.Equal(material, reopened.PolicyDetail.Material);
            Assert.Equal(package, reopened.PolicyDetail.Handoff!.Package!.Hash);
            Assert.Contains("187 seconds old", reopened.PolicyFreshness, StringComparison.Ordinal);
            Assert.Contains("127 seconds old", reopened.ObservationAge, StringComparison.Ordinal);
            Assert.All(reopened.Attention, a => Assert.StartsWith("cached ", a.AsOf, StringComparison.Ordinal));
            Assert.False(reopened.SubmitDecisionCommand.CanExecute(null));
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Equal(0, connections.SignedWrites);
            return true;
        });
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("excepted")]
    public async Task AppStorageRetainsPolicyProofsOnlyForTheExactConnectionAndUnavailableIsNotEmpty(string key)
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        DirectoryInfo storage = Directory.CreateTempSubdirectory("vericairn-policy-phone-");
        try
        {
            await fixture.UiAsync(async () =>
            {
                RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
                model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.PolicyKey == key);
                await model.OpenAttentionCommand.ExecuteAsync();
                var store = new LocalNetworkConnections(storage.FullName, new FileKeyStore(storage.FullName));
                var connection = model.Selected!.Connection;
                store.SaveObservation(connection, model.Selected.Observation!);
                var restored = store.LoadObservation(connection)!;
                var detail = Assert.Single(restored.PolicyDetails).Value;
                Assert.Equal(model.PolicyDetail!.Material, detail.Material);
                Assert.Equal(model.PolicyDetail.EvaluationBasis, detail.EvaluationBasis);
                Assert.Contains(key, detail.Material, StringComparison.Ordinal);
                Assert.Equal(model.PolicyDetail.AsOf, detail.AsOf);
                Assert.Null(store.LoadObservation(connection with { Principal = policy.Estate.BuilderId }));
                Assert.Null(store.LoadObservation(connection with { Identity = "different-runtime" }));
                var old = restored with { PolicyDetails = new Dictionary<string, RemotePolicyDetail>() };
                connections.SaveObservation(connection, old);
                var reopened = new RemoteViewModel(connections);
                reopened.SelectedAttention = Assert.Single(reopened.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Excepted.Id);
                int calls = connections.ApiCalls;
                await reopened.OpenAttentionCommand.ExecuteAsync();
                Assert.Null(reopened.PolicyDetail!.Policy);
                Assert.Contains("not observed", reopened.PolicyDetail.Material, StringComparison.Ordinal);
                Assert.Contains("excepted", reopened.PolicyDetail.Material, StringComparison.Ordinal);
                Assert.Equal(calls, connections.ApiCalls);
                return true;
            });
        }
        finally { storage.Delete(recursive: true); }
    }
}
