// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

public sealed class PolicyCommandsTests
{
    [Fact]
    public void ReadCommandsShowTheirBasisAndDoNotAuthorRecords()
    {
        CliHarness.Ok("policies", "--help");
        using var fixture = new PolicyFixture();
        fixture.Policy();
        int before = fixture.Estate.Load().Records.Count;
        string root = fixture.Estate.Root;
        CliHarness.Outcome list = CliHarness.Ok("--estate", root, "policies", "list", "--json");
        Assert.Equal("recorded", list.Json.GetProperty("basis").GetProperty("origin").GetString());
        CliHarness.Outcome preview = CliHarness.Ok("--estate", root, "policies", "check", "--as-of", "2026-10-16T13:00:00Z", "--json");
        Assert.Equal(1, preview.Json.GetProperty("holds").GetInt32());
        Assert.Contains("Basis sha256:", CliHarness.Ok("--estate", root, "policies", "show", "maintenance").Out, StringComparison.Ordinal);
        Assert.Contains("recurring-duty", CliHarness.Ok("--estate", root, "policies", "kinds").Out, StringComparison.Ordinal);
        Assert.Contains("Writing exceptions", CliHarness.Ok("--estate", root, "estate", "writing-exceptions", "list").Out, StringComparison.Ordinal);
        Assert.Equal(2, CliHarness.Run("--estate", root, "policies", "show", "absent").Code);
        Assert.Equal(2, CliHarness.Run("--estate", root, "estate", "writing-exceptions", "show", "absent").Code);
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
    }
}
