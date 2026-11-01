// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ReleaseCadenceCommandsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StatusProgressAndSubmissionCheckShowOnlyDueCadencesWithoutEffects(bool due, bool json)
    {
        CliHarness.Ok("release", "--help");
        using var f = new ReleaseCadenceFixture();
        f.At(due ? f.DueAt : f.DueAt.AddSeconds(-1));
        string[] before = f.Snapshot();
        string[] prefix = ["--estate", f.Records.Estate.Root, .. (json ? new[] { "--json" } : [])];
        foreach (string command in new[] { "status", "progress" })
        {
            CliHarness.Outcome response = CliHarness.RunWithApi(f.Api, null, [.. prefix, command]);
            Assert.Equal(0, response.Code);
            if (json)
            {
                bool present = response.Json.GetProperty("operator").TryGetProperty("releaseCadences", out var rows);
                Assert.Equal(due, present);
                if (due)
                {
                    var row = Assert.Single(rows.EnumerateArray());
                    Assert.Equal("current", row.GetProperty("line").GetString());
                    Assert.Equal("1.0.0", row.GetProperty("version").GetString());
                }
            }
            else
            {
                Assert.Equal(due, response.Out.Contains("release due on the cadence; line current; target version 1.0.0", StringComparison.Ordinal));
                Assert.Equal(due, response.Out.Contains("cadence context", StringComparison.OrdinalIgnoreCase));
            }
        }
        CliHarness.Outcome check = CliHarness.RunWithApi(f.Api, null, [.. prefix, "release", "submit", "--check", "--product", f.Product.Id,
            "--version", "1.0.0", "--commit", f.Request.Commit!, "--expected", f.Product.ComputedHash, "--by", f.Records.Estate.OperatorId]);
        Assert.Equal(0, check.Code);
        if (json)
        {
            Assert.Empty(check.Json.GetProperty("check").GetProperty("objections").EnumerateArray());
            Assert.Equal(due, check.Json.GetProperty("policies").TryGetProperty("releaseCadences", out _));
        }
        else
        {
            Assert.Equal(due, check.Out.Contains("release due on the cadence; line current; target version 1.0.0", StringComparison.Ordinal));
            Assert.Equal(due, check.Out.Contains("cadence context", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(0, f.Dispatches);
        Assert.Equal(0, f.ProviderCalls);
        Assert.Empty(f.Api.Records("operation"));
        Assert.Empty(f.Api.Records("release"));
        Assert.Empty(f.Api.Records("acceptance"));
        Assert.Equal(before, f.Snapshot());
    }
}
