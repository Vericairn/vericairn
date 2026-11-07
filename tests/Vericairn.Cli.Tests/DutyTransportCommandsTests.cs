// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
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
            CliHarness.Outcome missing = CliHarness.Run("--estate", root, "release", "basis", "missing");
            Assert.Equal(2, missing.Code);
            Assert.Contains("No supported retained release basis has this hash: missing.", missing.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(scratch, true); }
    }

    [Fact]
    public void AnOrdinaryReviewWithReadNeedsNoDutyPrerequisitesAndADutyReviewNamesWhatItLacks()
    {
        // The estate report's cases (#299), on an estate whose runtime holds no device key.
        Assert.Contains("admitted device", CliHarness.Ok("records", "new", "--help").Out, StringComparison.Ordinal);
        Assert.Contains("admitted device", CliHarness.Ok("work", "review", "--help").Out, StringComparison.Ordinal);
        Assert.Contains("needs neither", CliHarness.Ok("records", "describe", "review").Out, StringComparison.Ordinal);
        using var fixture = new PolicyFixture();
        var policy = fixture.Policy();
        var ordinary = fixture.Result(sign: false);
        fixture.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        var duty = fixture.Result(fixture.DutyLink(policy));
        string scratch = CliHarness.TempDirectory("review-read");
        try
        {
            string root = fixture.Estate.Root;
            string operatorId = fixture.Estate.OperatorId;
            string plain = CliHarness.WriteJson(scratch, "plain.json", """{"independence":"operator","outcome":"pass","summary":"Reviewed the exact result.","findings":[]}""");
            string read = CliHarness.WriteJson(scratch, "read.json", $$"""{"independence":"operator","outcome":"pass","summary":"Reviewed the exact result.","findings":[],"read":[{"type":"result","id":"{{ordinary.Id}}","hash":"{{ordinary.ComputedHash}}"}]}""");
            string ordinarySubject = $"result:{ordinary.Id}:{ordinary.ComputedHash}";
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", operatorId, "--subject", ordinarySubject, "--body", plain);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", operatorId, "--subject", ordinarySubject, "--body", read);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", operatorId, "--subject", ordinarySubject, "--body", read, "--expected", ordinary.ComputedHash);
            string dutySubject = $"result:{duty.Id}:{duty.ComputedHash}";
            using var api = EstateService.Open(root);
            int before = api.Session.Load().Records.Count;
            CliHarness.Outcome unexpected = CliHarness.Run("--estate", root, "records", "new", "review", "--by", operatorId, "--subject", dutySubject, "--body", plain);
            Assert.Equal(2, unexpected.Code);
            Assert.Contains("A review of a result with duties is signed duty evidence", unexpected.Err, StringComparison.Ordinal);
            Assert.Contains("--expected", unexpected.Err, StringComparison.Ordinal);
            Assert.Contains("admitted device", unexpected.Err, StringComparison.Ordinal);
            CliHarness.Outcome deviceless = CliHarness.Run("--estate", root, "records", "new", "review", "--by", operatorId, "--subject", dutySubject, "--body", plain, "--expected", duty.ComputedHash);
            Assert.Equal(2, deviceless.Code);
            Assert.Contains("admitted device", deviceless.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("expected hash", deviceless.Err, StringComparison.Ordinal);
            Assert.Equal(before, api.Session.Load().Records.Count);
            Assert.False(api.Session.Load().Findings.HasErrors, string.Join('\n', api.Session.Load().Findings));
        }
        finally { Directory.Delete(scratch, true); }
    }
}
