// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ReleaseReviewCommandsTests
{
    [Theory]
    [InlineData("focused")]
    [InlineData("full")]
    public void WrittenAndAssignedBriefsMatchTheRecordDerivedText(string scope)
    {
        CliHarness.Ok("release", "review-brief", "--help");
        using var f = new ReleaseReviewFixture(scope);
        ReleaseReviewBriefResponse expected = f.Api.ReleaseReviewBrief(ReleaseReviewFixture.Version);
        string output = Path.Combine(f.Records.Estate.Root, "review-brief.md");
        int before = f.Model.Records.Count;
        string[] args = ["--estate", f.Records.Estate.Root, "release", "review-brief", ReleaseReviewFixture.Version,
            "--product", f.Product.Id, "--decision", f.Decision.Id, "--out", output];
        CliHarness.Outcome written = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(0, written.Code);
        Assert.Equal(expected.Text, written.Out);
        Assert.Equal(Encoding.UTF8.GetBytes(expected.Text), File.ReadAllBytes(output));
        Assert.Equal(before, f.Model.Records.Count);
        CliHarness.Outcome assigned = CliHarness.RunWithApi(f.Api, null, [.. args, "--assign", "--by", f.Operator, "--expected", expected.Hash!, "--json"]);
        Assert.Equal(0, assigned.Code);
        ReleaseReviewBriefResponse response = JsonSerializer.Deserialize<ReleaseReviewBriefResponse>(assigned.Out, ProtocolJson.Options)!;
        Assert.True(response.Ok);
        Assert.Equal(expected.Text, response.Text);
        Assert.Equal(expected.Hash, response.Hash);
        var record = f.Model.ById[response.Assignment!.Id];
        Assert.Equal(f.Reviewer.Id, EstateModel.BodyString(record, "contributor"));
        Assert.Equal(File.ReadAllText(output), record.Body.GetProperty("brief").GetProperty("reviewBrief").GetString());
        Assert.Equal(before + 1, f.Model.Records.Count);
    }

    [Fact]
    public void StalePreparationsAndMissingAuthorsWriteNothing()
    {
        CliHarness.Ok("release", "review-brief", "--help");
        using var f = new ReleaseReviewFixture();
        int before = f.Model.Records.Count;
        string[] args = ["--estate", f.Records.Estate.Root, "--json", "release", "review-brief", ReleaseReviewFixture.Version, "--assign"];
        CliHarness.Outcome missing = CliHarness.RunWithApi(f.Api, null, args);
        Assert.Equal(2, missing.Code);
        Assert.Contains("--assign requires --by", missing.Err, StringComparison.Ordinal);
        CliHarness.Outcome stale = CliHarness.RunWithApi(f.Api, null, [.. args, "--by", f.Operator, "--expected", "stale"]);
        Assert.Equal(2, stale.Code);
        Assert.Equal("conflict", stale.Json.GetProperty("problem").GetProperty("code").GetString());
        Assert.Equal(before, f.Model.Records.Count);
    }
}
