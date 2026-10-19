// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

public sealed class DutyTransportCommandsTests
{
    [Fact]
    public void ResultAndReviewCommandsRetainExactDutyEvidenceAndRequirePreparedHashes()
    {
        CliHarness.Ok("work", "review", "--help");
        using var fixture = new PolicyFixture();
        var policy = fixture.Policy();
        var attempt = fixture.BeginAttempt();
        using var api = EstateService.Open(fixture.Estate.Root);
        foreach (string principal in new[] { fixture.Estate.OperatorId, fixture.Estate.BuilderId })
        {
            WriteOutcome device = api.DeviceCreate(new(principal, "Fixture transport signer", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            var record = api.Session.Load().ById[device.Written!.Id];
            fixture.Estate.SetClock(record.AtTime.AddSeconds(-1));
            fixture.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Fixture transport admission." }, subject: record.Reference);
        }
        string scratch = CliHarness.TempDirectory("duty-transport");
        try
        {
            string root = fixture.Estate.Root;
            string body = CliHarness.WriteJson(scratch, "result.json", PolicyFixture.ResultBody(fixture.DutyLink(policy)).ToJsonString());
            int before = api.Session.Load().Records.Count;
            Assert.Equal(2, CliHarness.Run("--estate", root, "work", "result", attempt.Id, "--by", attempt.By, "--expected", "stale", "--body", body).Code);
            Assert.Equal(before, api.Session.Load().Records.Count);
            CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt.Id, "--by", attempt.By, "--expected", attempt.ComputedHash, "--body", body);
            var written = api.Session.Load().ById[result.Id];
            Assert.Equal(policy.ComputedHash, written.Body.GetProperty("duties")[0].GetProperty("policy").GetProperty("hash").GetString());
            Assert.True(SignatureChecks.IsAuthentic(api.Session.Load(), written));
            string review = CliHarness.WriteJson(scratch, "review.json", JsonSerializer.Serialize(new ResultReviewBody("operator", "pass", "Reviewed the exact occurrence.", [],
                [new("estate-policy", policy.Id, policy.ComputedHash)]), ProtocolJson.Options));
            CliHarness.Outcome reviewed = CliHarness.Ok("--estate", root, "--json", "work", "review", result.Id, "--by", fixture.Estate.OperatorId, "--expected", result.Hash, "--body", review);
            Assert.Equal(policy.ComputedHash, api.Session.Load().ById[reviewed.Id].Body.GetProperty("read")[0].GetProperty("hash").GetString());
            Assert.True(Assert.Single(api.Policy("maintenance")!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
            CliHarness.Ok("release", "basis", "--help");
            Assert.Equal(2, CliHarness.Run("--estate", root, "release", "basis", "missing").Code);
        }
        finally { Directory.Delete(scratch, true); }
    }
}
