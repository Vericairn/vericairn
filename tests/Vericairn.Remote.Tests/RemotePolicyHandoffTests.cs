// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemotePolicyHandoffTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task DesktopHandoffRefreshesAuthorityAndBasisAndRetainsTheExactOriginalSubject()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Due.Id);
            AttentionNotice original = model.SelectedAttention.Notice!;
            await model.OpenAttentionCommand.ExecuteAsync();
            var changed = PolicyFixture.Body(seed.Due);
            changed["reason"] = "Keep the updated explanation beside the original selection.";
            var revised = policy.Policy(changed, seed.Due);
            int records = policy.Estate.Load().Records.Count;
            var calls = new List<string>();
            connections.BeforeApiCall = calls.Add;
            await model.PolicyHandoffCommand.ExecuteAsync();
            Assert.Equal(2, calls.Count(n => n == nameof(IEstateApi.PolicyAccessAsync)));
            Assert.Equal(2, calls.Count(n => n == nameof(IEstateApi.PolicyAsync)));
            Assert.Single(calls, n => n == nameof(IEstateApi.HandoffAsync));
            Assert.NotNull(model.PolicyDetail!.Handoff!.Package);
            Assert.Null(model.PolicyDetail.Handoff.Problem);
            Assert.Equal(original, model.PolicyDetail.Notice);
            Assert.Contains(seed.Due.ComputedHash, model.PolicyDetail.DesktopRoute, StringComparison.Ordinal);
            Assert.Contains(original.Target!.Hash!, model.PolicyDetail.DesktopRoute, StringComparison.Ordinal);
            Assert.Contains(original.Policy!.Finding.Duty!.Occurrence.GetRawText(), model.PolicyDetail.DesktopRoute, StringComparison.Ordinal);
            Assert.Contains(revised.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains("basis changed", model.PolicyMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Exact bounds", model.PolicyAuthority, StringComparison.Ordinal);
            Assert.Contains("No desktop opening", model.PolicyHandoff, StringComparison.Ordinal);
            Assert.Contains(model.Selected!.Connection.Estate, model.PolicyDestination, StringComparison.Ordinal);
            Assert.Null(model.Decision);
            model.MaterialReviewed = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.False(model.MaterialReviewed);
            Assert.Equal(records, policy.Estate.Load().Records.Count);
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Equal(0, connections.SignedWrites);
            return true;
        });
    }

    [Fact]
    public async Task ChangedBasisDuringHandoffDiscardsThePackageWithoutChangingTheSelection()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Missed.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            AttentionNotice original = model.PolicyDetail!.Notice;
            connections.BeforeApiCall = name =>
            {
                if (name == nameof(IEstateApi.HandoffAsync)) policy.Add("decision", policy.DecisionBody());
            };
            await model.PolicyHandoffCommand.ExecuteAsync();
            Assert.Null(model.PolicyDetail.Handoff);
            Assert.Equal(original, model.PolicyDetail.Notice);
            Assert.Contains("changed while preparing", model.PolicyMessage, StringComparison.Ordinal);
            Assert.Equal(0, connections.PreparedCalls);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostAccessOrUnavailableAuthorityCannotProduceCurrentHandoff(bool unavailable)
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Missed.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            RemotePolicyDetail original = model.PolicyDetail!;
            var calls = new List<string>();
            connections.BeforeApiCall = name =>
            {
                calls.Add(name);
                if (unavailable && name == nameof(IEstateApi.PolicyAccessAsync)) throw new NotSupportedException("Authority source unavailable.");
            };
            if (!unavailable) connections.Checks.Enqueue((ConnectionStates.Denied, "Device access changed."));
            await model.PolicyHandoffCommand.ExecuteAsync();
            Assert.Same(original, model.PolicyDetail);
            Assert.Null(model.PolicyDetail!.Handoff);
            Assert.StartsWith("cached ", model.PolicyFreshness, StringComparison.Ordinal);
            Assert.Contains("unavailable", model.PolicyMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(nameof(IEstateApi.HandoffAsync), calls);
            Assert.False(model.PolicyHandoffCommand.CanExecute(null));
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            if (!unavailable) Assert.Empty(calls);
            return true;
        });
    }
}
