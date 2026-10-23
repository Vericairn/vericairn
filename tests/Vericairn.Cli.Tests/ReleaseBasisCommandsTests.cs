// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ReleaseBasisCommandsTests
{
    public ReleaseBasisCommandsTests() => CliHarness.Ok("release", "basis", "--help");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownAndUnsupportedHashesNameTheRequestedHash(bool unsupported)
    {
        using var f = new ReleaseBasisFixture();
        string hash = unsupported ? f.Api.Session.Objects.Put("{\"format\":\"vericairn.release-basis/99\"}"u8) : ContentHash.Sha256("unknown"u8);
        foreach (bool json in new[] { false, true })
        {
            CliHarness.Outcome outcome = Read(f, hash, json);
            Assert.Equal(2, outcome.Code);
            Assert.Equal("error: No supported retained release basis has this hash: " + hash + ".\n", outcome.Err);
            Assert.Empty(outcome.Out);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCorruptSourcesHaveTheSameFindingsAsTheApi(bool corrupted)
    {
        using var f = new ReleaseBasisFixture();
        int before = f.Api.Session.Load().Records.Count;
        f.LoseObject(f.SourceHash, corrupted);
        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));

        CliHarness.Outcome human = Read(f, f.Hash, json: false);
        CliHarness.Outcome json = Read(f, f.Hash, json: true);

        Assert.Equal(2, human.Code);
        Assert.Equal(2, json.Code);
        Assert.Empty(human.Out);
        Assert.Equal(human.Err, json.Err);
        Assert.Contains(response.Problem!.Message, human.Err, StringComparison.Ordinal);
        FindingDto finding = Assert.Single(response.Problem.Findings);
        Assert.Contains($"{finding.Code} {f.SourceLocation}: {finding.Message}", human.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("No supported retained release basis", human.Err, StringComparison.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(response, ProtocolJson.Options) + "\n", json.Out);
        Assert.Equal(JsonValueKind.Null, json.Json.GetProperty("evidence").ValueKind);
        Assert.Equal(before, f.Api.Session.Load().Records.Count);
    }

    [Fact]
    public void AllFailuresArePrintedAndCompleteEvidenceStillReads()
    {
        using var f = new ReleaseBasisFixture();
        CliHarness.Outcome complete = Read(f, f.Hash, json: true);
        Assert.Equal(0, complete.Code);
        Assert.Empty(complete.Err);
        Assert.False(complete.Json.TryGetProperty("problem", out _));
        Assert.Equal("vericairn.release-basis/2", complete.Json.GetProperty("evidence").GetProperty("format").GetString());
        Assert.Contains("Retained release evidence " + f.Hash, Read(f, f.Hash, json: false).Out, StringComparison.Ordinal);
        f.LoseObject(f.SourceHash, corrupted: false);
        f.LoseObject(f.ProofHash, corrupted: true);

        CliHarness.Outcome outcome = Read(f, f.Hash, json: false);

        Assert.Equal(2, outcome.Code);
        Assert.Contains("release-basis.missing " + f.SourceLocation, outcome.Err, StringComparison.Ordinal);
        Assert.Contains("release-basis.hash-mismatch object:" + f.ProofHash, outcome.Err, StringComparison.Ordinal);
    }

    private static CliHarness.Outcome Read(ReleaseBasisFixture fixture, string hash, bool json)
    {
        List<string> arguments = ["--estate", fixture.Records.Estate.Root, "release", "basis", hash];
        if (json) arguments.Add("--json");
        return CliHarness.RunWithApi(fixture.Api, null, [.. arguments]);
    }
}
