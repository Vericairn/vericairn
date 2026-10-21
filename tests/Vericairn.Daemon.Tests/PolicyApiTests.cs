// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class PolicyApiTests
{
    public PolicyApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData("kinds")]
    [InlineData("evaluate")]
    [InlineData("access")]
    [InlineData("hash")]
    public async Task PolicyKeysMayEqualNamedReadEndpoints(string key)
    {
        using var fixture = new PolicyFixture();
        var policy = fixture.Policy(fixture.PolicyBody(key));
        int before = fixture.Estate.Load().Records.Count;
        await using DaemonHost host = await DaemonTestHome.StartAsync(fixture.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(fixture.Estate.Root)!);
        Assert.Equal(policy.Id, Assert.Single(client.Policy(key)!.Policies).Id);
        Assert.Equal(policy.Id, Assert.Single((await client.PolicyAsync(key, TestContext.Current.CancellationToken))!.Policies).Id);
        Assert.Contains(client.PolicyKinds(), kind => kind.Kind == "vericairn.estate/recurring-duty");
        Assert.Contains("declare", client.PolicyAccess(fixture.Estate.OperatorId));
        Assert.Single(client.PolicyEvaluate().Policies);
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
    }

    [Fact]
    public async Task TheReadRoutesCarryPolicyDutyAndWritingEvidenceWithoutAuthoring()
    {
        using var fixture = new PolicyFixture();
        fixture.Policy();
        var entry = fixture.Add("writing-exception", PolicyFixture.WritingBody());
        int before = fixture.Estate.Load().Records.Count;
        await using DaemonHost host = await DaemonTestHome.StartAsync(fixture.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(fixture.Estate.Root)!);
        PolicyResponse response = await client.PolicyEvaluateAsync("2026-10-16T13:00:00Z", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, response.Holds);
        Assert.NotNull(Assert.Single(response.Findings, f => f.ReleaseHold).Duty);
        Assert.Equal("recorded", client.Policies().Basis.Origin);
        Assert.Single(client.Policy("maintenance")!.Policies);
        Assert.Null(client.Policy("absent"));
        Assert.Contains(client.PolicyKinds(), k => k.Kind == "vericairn.estate/recurring-duty");
        Assert.Single(client.WritingExceptions().Entries);
        Assert.Single(client.WritingException(entry.Id)!.Records);
        Assert.Null(client.WritingException("absent"));
        Assert.Equal("conflict", client.WritingExceptionAdd(new(fixture.Estate.OperatorId, PolicyFixture.Element(PolicyFixture.WritingBody()), "stale")).Problem!.Code);
        Assert.Equal("conflict", client.PolicyRevise("maintenance", new(fixture.Estate.OperatorId, fixture.Estate.Load().Current("estate-policy").Single().Body, "stale")).Problem!.Code);
        Assert.Contains("declare", client.PolicyAccess(fixture.Estate.OperatorId));
        Assert.Equal(client.Policies().Basis.Digest, client.Check().Policies!.Basis.Digest);
        Assert.Equal(client.Policies().Basis.Digest, client.Status().Json.GetProperty("policies").GetProperty("basis").GetProperty("digest").GetString());
        Assert.Contains("Writing exceptions\n", client.Progress().Text, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
    }
}
