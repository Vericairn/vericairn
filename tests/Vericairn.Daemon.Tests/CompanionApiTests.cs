// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
[Trait("Journey", "D19-A")]
[Trait("Journey", "D19-E")]
public sealed class CompanionApiTests
{
    public CompanionApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task RoutesCarryExactRevisionsAndSourceBasesWithoutAuthoring()
    {
        using var fixture = new PolicyFixture();
        int count = fixture.Estate.Load().Records.Count;
        await using DaemonHost host = await DaemonHost.StartAsync(fixture.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(fixture.Estate.Root)!);
        CompanionsResponse coverage = await client.CompanionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("success", coverage.Status);
        var request = new CompanionRequest("vericairn.records/items", "item", fixture.Decision.Id);
        CompanionResponse rendered = await client.CompanionRenderAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("success", rendered.Status);
        Assert.Equal(fixture.Decision.ComputedHash, Assert.Single(rendered.Sources).Hash);
        Assert.Equal(rendered.InputDigest, client.CompanionSources(request).InputDigest);
        Assert.Equal("success", client.CompanionRender(new("vericairn.records/decisions", "current", Snapshot: coverage.Snapshot)).Status);
        Assert.Equal("unavailable", client.CompanionRender(request with { Audience = "Public" }).Status);
        Assert.Equal("unavailable", client.CompanionRender(request with { Generator = coverage.Generators.First(g => g.LocalOnly).Id }).Status);
        Assert.Equal("error", client.CompanionCompare(new("unavailable")).Status);
        Assert.Equal(count, fixture.Estate.Load().Records.Count);
    }
}
