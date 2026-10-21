// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class UpdateNoticeJourneyTests : IDisposable
{
    private readonly string home = CliHarness.TempDirectory("update-notice");
    private readonly string estate = CliHarness.TempDirectory("update-estate");

    [Fact]
    public void VersionDoctorAndStatusDiscloseAndShowEveryUpgradeRouteWithoutChangingStdout()
    {
        Init();
        var source = new ReleaseUpdatesTests.Source();
        var updates = new ReleaseUpdates(new(home), source, new ReleaseUpdatesTests.Clock());
        foreach (string command in new[] { "version", "doctor", "status" })
        {
            CliHarness.Outcome baseline = CliHarness.Ok("--estate", estate, command);
            CliHarness.Outcome actual = CliHarness.RunWithUpdates(updates, "--estate", estate, command);
            Assert.Equal(0, actual.Code);
            Assert.Equal(baseline.Out, actual.Out);
            Assert.Contains("Release check: on", actual.Err, StringComparison.Ordinal);
            Assert.Contains("no identifiers or usage telemetry", actual.Err, StringComparison.Ordinal);
            Assert.Contains("updates configure --enabled false", actual.Err, StringComparison.Ordinal);
            Assert.Contains("Newer Vericairn release: 9.2.0", actual.Err, StringComparison.Ordinal);
            Assert.Contains("https://github.com/Vericairn/vericairn/releases/tag/v9.2.0", actual.Err, StringComparison.Ordinal);
            Assert.Contains("dotnet tool update --global dotnet-vc --version 9.2.0", actual.Err, StringComparison.Ordinal);
            Assert.Contains("Archive download", actual.Err, StringComparison.Ordinal);
            Assert.Contains("vericairn estate upgrade --to 9.2.0 --check", actual.Err, StringComparison.Ordinal);
            Assert.Contains("Nothing installs or upgrades automatically", actual.Err, StringComparison.Ordinal);
        }
        string before = CliHarness.Ok("--estate", estate, "status", "--json").Out;
        CliHarness.Outcome json = CliHarness.RunWithUpdates(updates, "--estate", estate, "status", "--json");
        Assert.Equal(before, json.Out);
        Assert.Contains("Newer Vericairn release", json.Err, StringComparison.Ordinal);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void OfflineNeverChangesTheCommandsExitCodeOrAddsFailureText()
    {
        Init();
        var source = new ReleaseUpdatesTests.Source { Answer = _ => throw new HttpRequestException("offline stand-in") };
        var updates = new ReleaseUpdates(new(home), source, new ReleaseUpdatesTests.Clock());
        foreach (string command in new[] { "version", "doctor", "status" })
        {
            CliHarness.Outcome actual = CliHarness.RunWithUpdates(updates, "--estate", estate, command);
            Assert.Equal(0, actual.Code);
            Assert.Contains("Release check: on", actual.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("Newer Vericairn release", actual.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("offline stand-in", actual.Err, StringComparison.Ordinal);
        }
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void ConfigureIsMachineLocalAndDisabledCommandsNeverCallTheSource()
    {
        var store = new ReleaseUpdateStore();
        var source = new ReleaseUpdatesTests.Source();
        var updates = new ReleaseUpdates(store, source, new ReleaseUpdatesTests.Clock());
        try
        {
            Assert.False(CliHarness.Ok("updates", "configure", "--enabled", "false", "--json").Json.GetProperty("enabled").GetBoolean());
            Assert.False(CliHarness.Ok("updates", "configure", "--json").Json.GetProperty("enabled").GetBoolean());
            Assert.Contains("Release check: off", CliHarness.RunWithUpdates(updates, "version").Err, StringComparison.Ordinal);
            Assert.Equal(0, source.Calls);
            Assert.Equal(0, CliHarness.RunWithUpdates(updates, "updates", "configure", "--enabled", "true").Code);
            Assert.True(store.Enabled);
            Assert.Equal(0, source.Calls); // Configuring and showing settings never check.
            Assert.Empty(Directory.EnumerateFileSystemEntries(estate));
        }
        finally { store.Configure(false); }
    }

    [Fact]
    public void EstateCheckWarnsFromCacheWithoutNetworkAndStatusRemainsCanonical()
    {
        Init();
        string path = Path.Combine(estate, "estate", "manifest.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest["vericairn"] = new JsonObject
        {
            ["mode"] = "release", ["repository"] = "Vericairn/vericairn", ["version"] = "0.1.0",
            ["artifacts"] = new JsonObject { ["linux-x64"] = "sha256:" + new string('1', 64) },
        };
        File.WriteAllBytes(path, CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)));
        string before = CliHarness.Ok("--estate", estate, "status", "--json").Out;
        var store = new ReleaseUpdateStore();
        ReleaseCheckState prior = store.ReadState();
        try
        {
            var clock = new ClientClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
            using (store.Acquire()) store.WriteState(new(clock.GetUtcNow(), clock.GetUtcNow(), "v9.2.0"));
            var source = new ReleaseUpdatesTests.Source { Answer = _ => throw new InvalidOperationException("Check must never call a source.") };
            var updates = new ReleaseUpdates(store, source, clock);
            CliHarness.Outcome check = CliHarness.RunWithUpdates(updates, "--estate", estate, "estate", "check");
            Assert.Equal(0, check.Code);
            Assert.Contains("manifest.release-behind", check.Out, StringComparison.Ordinal);
            Assert.Contains("9.2.0", check.Out, StringComparison.Ordinal);
            Assert.Contains("vericairn estate upgrade --to 9.2.0 --check", check.Out, StringComparison.Ordinal);
            Assert.Equal(0, source.Calls);
            Assert.Equal(before, CliHarness.Ok("--estate", estate, "status", "--json").Out);
            Assert.Equal("0.1.0", JsonNode.Parse(File.ReadAllText(path))!["vericairn"]!["version"]!.GetValue<string>());
        }
        finally { using (store.Acquire()) store.WriteState(prior); }
    }

    private void Init() => CliHarness.Ok("--estate", estate, "estate", "init", "--name", "Update fixture", "--slug", "update-fixture", "--purpose", "Test release notices.", "--operator-name", "Operator", "--organization", "Example", "--commit", new string('a', 40));
    public void Dispose() => CliHarness.Delete(home, estate);
}
