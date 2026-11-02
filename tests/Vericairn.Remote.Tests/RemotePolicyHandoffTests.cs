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
            Assert.Contains(original.Target.Hash!, model.PolicyHandoff, StringComparison.Ordinal);
            Assert.Contains(original.Policy.Finding.Basis, model.PolicyHandoff, StringComparison.Ordinal);
            Assert.Contains(model.PolicyAuthority, model.PolicyHandoff, StringComparison.Ordinal);
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
    public void FileConditionContextKeepsTheOriginalContentHashBesideANewerObservation()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        AttentionNotice notice = Assert.Single(connections.Service.AttentionGet(policy.Estate.OperatorId).Items, a => a.Policy?.Finding.Policy.Id == seed.Unavailable.Id);
        string originalHash = "sha256:" + new string('a', 64);
        string changedHash = "sha256:" + new string('b', 64);
        PolicyFindingDto original = notice.Policy!.Finding with
        {
            Target = new("file", "repository:docs/guide.md", null, "repository", "docs/guide.md", originalHash),
        };
        notice = notice with { Policy = notice.Policy with { Finding = original } };
        PolicyResponse fresh = connections.Service.Policy(seed.Unavailable.Id)! with
        {
            Findings = [original with { Target = original.Target with { Hash = changedHash } }],
        };
        var detail = new RemotePolicyDetail(notice, "2026-11-01T12:00:00Z", fresh, []);
        Assert.Contains(originalHash, detail.DesktopRoute, StringComparison.Ordinal);
        Assert.DoesNotContain(changedHash, detail.DesktopRoute, StringComparison.Ordinal);
        Assert.Contains("docs/guide.md", detail.DesktopRoute, StringComparison.Ordinal);
        Assert.Contains("repository repository", detail.DesktopRoute, StringComparison.Ordinal);
        Assert.Contains(notice.Policy.Finding.Basis, detail.DesktopRoute, StringComparison.Ordinal);
        Assert.Contains(originalHash, detail.Material, StringComparison.Ordinal);
        Assert.Contains(changedHash, detail.Material, StringComparison.Ordinal);
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
    public async Task LeavingTheSelectionDuringRefreshCannotPublishAHandoffOrRetainFalseCurrentness(bool failedRead)
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Missed.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            RemoteEstate selected = model.Selected!;
            var calls = new List<string>();
            connections.BeforeApiCall = name =>
            {
                calls.Add(name);
                if (name != nameof(IEstateApi.PolicyAccessAsync)) return;
                model.Selected = null;
                model.Selected = selected;
                if (failedRead) throw new IOException("The policy authority read did not finish.");
            };
            await model.PolicyHandoffCommand.ExecuteAsync();
            Assert.Null(model.PolicyDetail);
            Assert.False(model.HasPolicyDetail);
            Assert.DoesNotContain(nameof(IEstateApi.HandoffAsync), calls);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            if (failedRead)
            {
                Assert.Equal(ConnectionStates.Cached, model.ConnectionState);
                Assert.StartsWith("cached from ", model.Freshness, StringComparison.Ordinal);
                Assert.All(model.Attention, item => Assert.StartsWith("cached ", item.AsOf, StringComparison.Ordinal));
            }
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
