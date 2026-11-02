// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

public sealed class RemotePolicyAttentionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AttentionRetainsRuntimeFindingsBasisProofsAndOperatorPreparation()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            string[] before = policy.Estate.Load().Records.Select(r => r.ComputedHash).ToArray();
            PolicyResponse evaluated = connections.Service.Policies();
            var notices = model.Attention.Where(a => a.Notice?.Policy is not null).ToArray();
            Assert.NotEmpty(notices);
            foreach (var item in notices)
            {
                PolicyAttention source = item.Notice!.Policy!;
                PolicyFindingDto exact = Assert.Single(evaluated.Findings, f => f.Policy.Id == source.Finding.Policy.Id &&
                    f.Rule == source.Finding.Rule && f.Target.Identity == source.Finding.Target.Identity && f.Item == source.Finding.Item);
                Assert.Equal(JsonSerializer.Serialize(exact, ProtocolJson.Options), JsonSerializer.Serialize(source.Finding, ProtocolJson.Options));
                Assert.Equal(evaluated.Basis.Digest, source.Basis.Digest);
                Assert.Contains(source.Finding.Basis, item.Why, StringComparison.Ordinal);
                Assert.Contains(source.Finding.AsOf!, item.AsOf, StringComparison.Ordinal);
                Assert.Contains(source.Finding.Owner, item.Why, StringComparison.Ordinal);
                Assert.Equal(source.Finding.NextAction, item.NextAction);
            }
            Assert.Contains(notices, a => a.Notice!.Policy!.Finding.Policy.Id == seed.Stale.Id && a.Kind == "failed check");
            Assert.Contains(notices, a => a.Notice!.Policy!.Finding.Policy.Id == seed.Missed.Id && a.Kind == "blocked");
            Assert.Contains(notices, a => a.Notice!.Policy!.Finding.Policy.Id == seed.Unavailable.Id && a.Notice.Policy.Finding.Verdict == "unavailable");
            Assert.False(model.AttentionEmpty);
            var due = Assert.Single(notices, a => a.Notice!.Policy!.Finding.Policy.Id == seed.Due.Id);
            Assert.Equal("information", due.Kind);
            Assert.Equal("due-within-grace", due.Notice!.Policy!.Finding.Disposition);
            Assert.False(due.Notice.Policy.Finding.Duty!.Completed);
            Assert.Contains("work incomplete", due.Why, StringComparison.Ordinal);
            OperatorDutyDto preparation = Assert.Single(due.Notice.Policy.Operator);
            Assert.Equal(policy.Estate.OperatorId, preparation.Owner);
            Assert.Equal(seed.Ask.Id, Assert.Single(preparation.Asks).Id);
            Assert.Contains(seed.Ask.ComputedHash, due.Why, StringComparison.Ordinal);
            model.SelectedAttention = due;
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Contains(seed.Ask.ComputedHash, model.PolicyDetail!.Material, StringComparison.Ordinal);
            Assert.Contains(preparation.Preparation, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Null(model.Decision);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            await model.ClosePolicyCommand.ExecuteAsync();

            model.SelectedAttention = Assert.Single(notices, a => a.Notice!.Policy!.Finding.Policy.Id == seed.Late.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Contains("discharged-late", model.PolicyDetail!.Material, StringComparison.Ordinal);
            Assert.Contains("missed True", model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(seed.Result.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(seed.Review.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(seed.Proof.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(seed.Proof.ComputedHash, Assert.Single(model.PolicyDetail.Policy!.Findings, f => f.Disposition == "discharged-late").Duty!.Completions.Single().Evidence.Select(e => e.Hash));
            Assert.Contains("Qualified from", model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Equal(before, policy.Estate.Load().Records.Select(r => r.ComputedHash));
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Equal(0, connections.SignedWrites);
            return true;
        });
    }

    [Fact]
    public async Task ExceptionRevocationAndWithdrawnCompletionRemainVisibleBesideTheOriginalCondition()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Excepted.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            string original = model.PolicyDetail!.Notice.Revision;
            Assert.Contains("excepted", model.PolicyDetail.Material, StringComparison.Ordinal);
            JsonObject revoked = PolicyFixture.Body(seed.Exception);
            revoked["state"] = "revoked";
            var revision = policy.Add("policy-exception", revoked, subject: seed.Excepted.Reference, supersedes: seed.Exception.Id);
            await model.RefreshPolicyCommand.ExecuteAsync();
            Assert.Equal(original, model.PolicyDetail.Notice.Revision);
            Assert.Contains(seed.Exception.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(revision.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains("revoked", model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains("basis changed", model.PolicyMessage, StringComparison.OrdinalIgnoreCase);
            await model.ClosePolicyCommand.ExecuteAsync();
            model.SelectedAttention = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == seed.Late.Id);
            await model.OpenAttentionCommand.ExecuteAsync();
            var adverse = PolicyFixture.Body(seed.Review);
            adverse["outcome"] = "fail";
            policy.Add("review", adverse, subject: seed.Result.Reference, supersedes: seed.Review.Id);
            await model.RefreshPolicyCommand.ExecuteAsync();
            DutyFindingDto duty = Assert.Single(model.PolicyDetail!.Policy!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt32() == 0).Duty!;
            Assert.False(duty.Completed);
            Assert.NotNull(Assert.Single(duty.Completions).Until);
            Assert.Contains(seed.Review.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.Contains(seed.Proof.ComputedHash, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            return true;
        });
    }

    [Fact]
    public async Task AcknowledgmentKeepsEveryMissedOccurrenceAndWritesNoRecord()
    {
        using var policy = new PolicyFixture();
        var seed = new RemotePolicySeed(policy);
        policy.Estate.SetClock(new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero));
        policy.Add("decision", policy.DecisionBody());
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            var missed = model.Attention.Where(a => a.Notice?.Policy?.Finding.Policy.Id == seed.Missed.Id).ToArray();
            Assert.Equal(3, missed.Length);
            Assert.Equal(3, missed.Select(a => a.Notice!.Key).Distinct(StringComparer.Ordinal).Count());
            string before = JsonSerializer.Serialize(connections.Service.Policies(), ProtocolJson.Options);
            int records = policy.Estate.Load().Records.Count;
            model.SelectedAttention = missed[0];
            string key = missed[0].Notice!.Key;
            await model.AcknowledgeCommand.ExecuteAsync();
            var acknowledged = Assert.Single(model.Attention, a => a.Notice?.Key == key);
            Assert.Equal("acknowledged", acknowledged.AttentionState);
            Assert.False(acknowledged.Notice!.Policy!.Finding.Duty!.Completed);
            Assert.Equal(2, model.Attention.Count(a => a.Notice?.Policy?.Finding.Policy.Id == seed.Missed.Id && a.AttentionState == "unacknowledged"));
            Assert.Equal(before, JsonSerializer.Serialize(connections.Service.Policies(), ProtocolJson.Options));
            Assert.Equal(records, policy.Estate.Load().Records.Count);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndedDebtRetainsUnmetHistoryAsInformationWithoutInventingCompletion(bool retired)
    {
        using var policy = new PolicyFixture();
        var declared = policy.Policy();
        policy.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        JsonObject body = PolicyFixture.Body(declared);
        if (retired)
        {
            body["state"] = "retired";
            body["retirement"] = new JsonObject { ["reason"] = "End the requirement.", ["findings"] = "Retain the uncompleted occurrence." };
        }
        body["dutyDispositions"] = new JsonArray(policy.Disposition(declared));
        var revised = policy.Policy(body, declared);
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            var item = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Disposition == "ended-unmet");
            Assert.Equal("information", item.Kind);
            Assert.False(item.Notice!.Policy!.Finding.ReleaseHold);
            Assert.Equal("unmet", item.Notice.Policy.Finding.Verdict);
            Assert.False(item.Notice.Policy.Finding.Duty!.Completed);
            Assert.Equal(revised.Id, item.Notice.Policy.Finding.Policy.Id);
            Assert.Contains("work incomplete", item.Why, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task MissingExactDutyProofBytesRemainUnavailableWithTheirBasisAndDebt()
    {
        using var policy = new PolicyFixture();
        JsonObject declaration = policy.PolicyBody();
        declaration["rules"]![0]!["parameters"]!["graceDays"] = 0;
        var duty = policy.Policy(declaration);
        policy.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var attempt = policy.BeginAttempt();
        JsonObject resultBody = PolicyFixture.ResultBody(policy.DutyLink(duty));
        string absent = "sha256:" + new string('e', 64);
        resultBody["outputs"] = new JsonArray(new JsonObject { ["name"] = "Inventory proof", ["hash"] = absent });
        var result = policy.Add("result", resultBody, builder: true, subject: attempt.Reference);
        policy.Review(result);
        policy.AssertValid();
        using var connections = new FakeConnections(policy.Estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            var item = Assert.Single(model.Attention, a => a.Notice?.Policy?.Finding.Policy.Id == duty.Id);
            Assert.Equal("unavailable", item.Notice!.Policy!.Finding.Verdict);
            Assert.NotNull(item.Notice.Policy.Finding.Duty);
            Assert.False(item.Notice.Policy.Finding.Duty.Completed);
            Assert.True(item.Notice.Policy.Finding.ReleaseHold);
            Assert.Equal("blocked", item.Kind);
            Assert.Contains(item.Notice.Policy.Basis.AsOf!, item.AsOf, StringComparison.Ordinal);
            model.SelectedAttention = item;
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Contains("unavailable", model.PolicyDetail!.Material, StringComparison.Ordinal);
            Assert.Contains(absent, model.PolicyDetail.Material, StringComparison.Ordinal);
            Assert.False(model.AttentionEmpty);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            Assert.Equal(0, connections.PreparedCalls);
            return true;
        });
    }
}
