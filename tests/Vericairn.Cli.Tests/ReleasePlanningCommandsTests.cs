// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ReleasePlanningCommandsTests
{
    [Fact]
    public void BothCommandsReturnRecordValidationFindingsWithoutWriting()
    {
        CliHarness.Ok("release", "--help");
        using var f = new ReleasePlanningFixture();
        JsonObject body = PolicyFixture.Body(f.Candidate); body["artifacts"] = new JsonArray();
        RecordEnvelope empty = f.Records.Add("release", body, subject: f.Candidate.Subject, supersedes: f.Candidate.Id);
        f.Records.Add("work-status", new() { ["status"] = "retired", ["note"] = "Retired destination." }, subject: f.Target.Reference);
        string file = Path.Combine(f.Records.Estate.Root, "prepared-work.json");
        File.WriteAllText(file, JsonSerializer.Serialize(f.Move.ExpectedWork, ProtocolJson.Options));
        int before = f.Model.Records.Count;
        CliHarness.Outcome withdrawn = CliHarness.RunWithApi(f.Api, null, "--estate", f.Records.Estate.Root, "--json", "release", "withdraw", empty.Id,
            "--expected", empty.ComputedHash, "--provider", "fixture-releases", "--reason", "Replace the candidate.", "--by", f.Operator);
        Assert.Equal(1, withdrawn.Code);
        Assert.Contains(withdrawn.Json.GetProperty("problem").GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("code").GetString() == "release.artifacts");
        CliHarness.Outcome moved = CliHarness.RunWithApi(f.Api, null, "--estate", f.Records.Estate.Root, "--json", "work", "move-plan", "PLAN-1",
            "--expected", f.Source.ComputedHash, "--expected-work", file, "--to", "PLAN-2", "--expected-target", f.Target.ComputedHash, "--by", f.Operator);
        Assert.Equal(1, moved.Code);
        Assert.Contains(moved.Json.GetProperty("problem").GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("code").GetString() == "policy.placement");
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovePlanCarriesTheCompleteExpectedSetAndReturnsConflicts(bool version)
    {
        CliHarness.Ok("work", "move-plan", "--help");
        using var f = new ReleasePlanningFixture();
        string file = Path.Combine(f.Records.Estate.Root, "prepared-work.json");
        File.WriteAllText(file, JsonSerializer.Serialize(f.Move.ExpectedWork, ProtocolJson.Options));
        string[] destination = version ? ["--version", "1.2.0"] : ["--to", "PLAN-2", "--expected-target", f.Target.ComputedHash];
        string[] args = ["--estate", f.Records.Estate.Root, "--json", "work", "move-plan", "PLAN-1", "--by", f.Operator,
            "--expected", f.Source.ComputedHash, "--expected-work", file, .. destination];
        CliHarness.Outcome result = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(0, result.Code);
        WorkMovePlanResponse response = JsonSerializer.Deserialize<WorkMovePlanResponse>(result.Out, ProtocolJson.Options)!;
        Assert.True(response.Ok);
        Assert.Equal(version ? 3 : 2, response.Written.Count);
        int before = f.Model.Records.Count;
        result = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(2, result.Code);
        response = JsonSerializer.Deserialize<WorkMovePlanResponse>(result.Out, ProtocolJson.Options)!;
        Assert.Equal("conflict", response.Problem!.Code);
        Assert.Empty(response.Written);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void WithdrawalReturnsBothReceiptsAndObservationLeavesDeletionToTheOperator()
    {
        CliHarness.Ok("release", "withdraw", "--help");
        using var f = new ReleasePlanningFixture();
        string[] args = ["--estate", f.Records.Estate.Root, "--json", "release", "withdraw", f.Candidate.Id, "--by", f.Operator,
            "--expected", f.Candidate.ComputedHash, "--provider", "fixture-releases", "--reason", "Replace this candidate."];
        CliHarness.Outcome result = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(0, result.Code);
        ReleaseResponse response = JsonSerializer.Deserialize<ReleaseResponse>(result.Out, ProtocolJson.Options)!;
        Assert.Equal("withdrawn", response.Status);
        Assert.NotNull(response.Release);
        Assert.NotNull(response.Operation);
        int before = f.Model.Records.Count;
        result = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(2, result.Code);
        Assert.Equal("conflict", result.Json.GetProperty("problem").GetProperty("code").GetString());
        result = CliHarness.RunWithApi(f.Api, null, "--estate", f.Records.Estate.Root, "--json", "release", "observe",
            "--version", "1.0.0", "--by", f.Operator, "--withdrawal", response.Operation.Id);
        Assert.Equal(2, result.Code);
        Assert.Contains("operator deletes", result.Err, StringComparison.Ordinal);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public void APartialPlacementConflictDoesNotMoveTheOtherMembers()
    {
        CliHarness.Ok("work", "--help");
        using var f = new ReleasePlanningFixture();
        string file = Path.Combine(f.Records.Estate.Root, "prepared-work.json");
        File.WriteAllText(file, JsonSerializer.Serialize(f.Move.ExpectedWork, ProtocolJson.Options));
        Assert.True(f.Api.WorkPlace("WORK-2", new(f.Operator, f.Second.ComputedHash, f.Target.ComputedHash, f.Target.Id)).Ok);
        int before = f.Model.Records.Count;
        CliHarness.Outcome result = CliHarness.RunWithApi(f.Api, null, "--estate", f.Records.Estate.Root, "--json", "work", "move-plan", "PLAN-1",
            "--by", f.Operator, "--expected", f.Source.ComputedHash, "--expected-work", file, "--to", "PLAN-2", "--expected-target", f.Target.ComputedHash);
        Assert.Equal(2, result.Code);
        Assert.Equal("conflict", result.Json.GetProperty("problem").GetProperty("code").GetString());
        Assert.Equal(before, f.Model.Records.Count);
        Assert.Equal(f.First.Id, f.Model.Latest(f.First).Id);
    }
}
