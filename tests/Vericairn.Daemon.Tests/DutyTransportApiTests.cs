// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DutyTransportApiTests
{
    public DutyTransportApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task DutyResultAndReviewRoutesUseExactHashesAndTheActingPrincipal()
    {
        using var fixture = new PolicyFixture();
        var policy = fixture.Policy();
        var attempt = fixture.BeginAttempt();
        using (var api = EstateService.Open(fixture.Estate.Root))
        foreach (string principal in new[] { fixture.Estate.OperatorId, fixture.Estate.BuilderId })
        {
            WriteOutcome device = api.DeviceCreate(new(principal, "Fixture transport signer", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            var record = api.Session.Load().ById[device.Written!.Id];
            fixture.Estate.SetClock(record.AtTime.AddSeconds(-1));
            fixture.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Fixture transport admission." }, subject: record.Reference);
        }
        await using DaemonHost host = await DaemonHost.StartAsync(fixture.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(fixture.Estate.Root)!);
        var request = new ResultRequest(attempt.Id, attempt.By, PolicyFixture.Element(PolicyFixture.ResultBody(fixture.DutyLink(policy))), attempt.ComputedHash);
        int before = fixture.Estate.Load().Records.Count;
        Assert.Equal("conflict", (await client.ResultAsync(request with { Expected = "stale" }, TestContext.Current.CancellationToken)).Problem!.Code);
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
        WriteOutcome result = await client.ResultAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.Ok, result.Problem?.Message);
        var resultRecord = fixture.Estate.Load().ById[result.Written!.Id];
        var review = new ResultReviewRequest(resultRecord.Id, fixture.Estate.OperatorId,
            JsonSerializer.SerializeToElement(new ResultReviewBody("operator", "pass", "Reviewed the exact duty evidence.", [], [new("estate-policy", policy.Id, policy.ComputedHash)]), ProtocolJson.Options), resultRecord.ComputedHash);
        WriteOutcome reviewed = await client.ReviewResultAsync(review, TestContext.Current.CancellationToken);
        Assert.True(reviewed.Ok, reviewed.Problem?.Message);
        var model = fixture.Estate.Load();
        Assert.True(SignatureChecks.IsAuthentic(model, model.ById[reviewed.Written!.Id]));
        Assert.Equal(policy.ComputedHash, model.ById[reviewed.Written.Id].Body.GetProperty("read")[0].GetProperty("hash").GetString());
        Assert.True(Assert.Single(client.Policy("maintenance")!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
        Assert.Null(await client.ReleaseBasisAsync("missing", TestContext.Current.CancellationToken));
        Assert.Null(client.ReleaseBasis("missing"));
    }
}
