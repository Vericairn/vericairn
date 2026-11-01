// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class EstateLineCommandsTests
{
    [Theory]
    [InlineData("current", "stable")]
    [InlineData("1.x", "lts")]
    public void InitRecordsTheFollowedLineAndChannel(string line, string channel)
    {
        string root = CliHarness.TempDirectory("estate-line-init");
        try
        {
            CliHarness.Ok("--estate", root, "estate", "init", "--name", "Line fixture", "--slug", "line-fixture",
                "--purpose", "Exercise followed lines.", "--operator-name", "Operator", "--organization", "Example",
                "--commit", new string('a', 40), "--line", line, "--channel", channel);
            var pin = CliHarness.Ok("--estate", root, "--json", "status").Json.GetProperty("vericairn");
            Assert.Equal(line, pin.GetProperty("line").GetString());
            Assert.Equal(channel, pin.GetProperty("channel").GetString());
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void UpgradeRejectsImplicitLineChangesAndAcceptsAnExplicitPreviewAndApply()
    {
        CliHarness.Delete(CliHarness.TempDirectory("estate-line-home"));
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.0.0");
        f.Follow("1.x", "lts");
        using var api = EstateService.Open(f.Estate.Root);
        byte[] before = File.ReadAllBytes(f.ManifestPath);
        CliHarness.Outcome refused = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "estate", "upgrade", "--to", "2.0.0", "--check");
        Assert.NotEqual(0, refused.Code);
        Assert.Contains("--line current --to 2.0.0", refused.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(f.ManifestPath));
        CliHarness.Outcome preview = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "--json", "estate", "upgrade",
            "--line", "current", "--channel", "stable", "--to", "2.0.0", "--check");
        Assert.Equal(0, preview.Code);
        Assert.Equal("current", preview.Json.GetProperty("line").GetString());
        Assert.Equal("stable", preview.Json.GetProperty("channel").GetString());
        Assert.False(preview.Json.GetProperty("applied").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(f.ManifestPath));
        CliHarness.Outcome applied = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "--json", "estate", "upgrade",
            "--line", "current", "--channel", "stable", "--to", "2.0.0", "--by", f.Estate.OperatorId);
        Assert.Equal(0, applied.Code);
        Assert.True(applied.Json.GetProperty("applied").GetBoolean());
        Assert.Equal("current", api.Session.Load().Manifest!.Line);
        Assert.Single(api.Session.Load().OfType("migration"));
    }

    [Fact]
    public void CheckNamesSupportedLinesAfterLtsSupportExpires()
    {
        CliHarness.Delete(CliHarness.TempDirectory("estate-support-home"));
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.0.0");
        f.Follow("1.x", "lts");
        using var api = EstateService.Open(f.Estate.Root);
        api.PolicyClock = new AfterSupport();
        CliHarness.Outcome result = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "--json", "estate", "check");
        Assert.Equal(0, result.Code);
        var warning = Assert.Single(result.Json.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "manifest.line-unsupported");
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal(EstateLayout.ManifestPath, warning.GetProperty("location").GetString());
        Assert.Contains("Supported lines: current.", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private sealed class AfterSupport : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2029, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
