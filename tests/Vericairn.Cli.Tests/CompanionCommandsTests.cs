// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class CompanionCommandsTests
{
    [Fact]
    public void ReadersSelectExactRevisionsAndCompareOnlyNeverRepairs()
    {
        CliHarness.Ok("docs", "companions", "--help");
        using var fixture = new PolicyFixture();
        string root = fixture.Estate.Root;
        string snapshot = CliHarness.Ok("--estate", root, "docs", "companions", "--json").Json.GetProperty("snapshot").GetString()!;
        var item = CliHarness.Ok("--estate", root, "docs", "render", "vericairn.records/items", "--selection", "item", "--revision", fixture.Decision.Id, "--json");
        Assert.Equal(fixture.Decision.ComputedHash, item.Json.GetProperty("sources")[0].GetProperty("hash").GetString());
        CliHarness.Ok("--estate", root, "docs", "render", "vericairn.records/decisions", "--selection", "current", "--snapshot", snapshot);
        var sources = CliHarness.Ok("--estate", root, "docs", "render", "vericairn.records/items", "--selection", "item", "--revision", fixture.Decision.Id, "--sources", "--json");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, sources.Json.GetProperty("text").ValueKind);
        Assert.Equal(2, CliHarness.Run("--estate", root, "docs", "render", "vericairn.records/items", "--selection", "current").Code);
        Assert.Equal(2, CliHarness.Run("--estate", root, "docs", "regenerate", "unavailable", "--compare").Code);
        Assert.Empty(fixture.Estate.Load().OfType("document-revision"));
        Assert.False(Directory.Exists(Path.Combine(root, "docs")));
    }
}
