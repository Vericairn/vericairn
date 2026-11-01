// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

[Collection("upgrade")]
public class EstateReleaseLineTests
{
    [Theory]
    [InlineData("current", "stable", "2.0.0")]
    [InlineData("1.x", "lts", "1.10.0")]
    [InlineData("1.x", "prerelease", "1.11.0-rc.10")]
    [InlineData("daily", "nightly", "3.0.0-nightly.2")]
    public void NewerReleaseChecksStayWithinTheFollowedLineAndStability(string line, string channel, string expected)
    {
        using var f = new EstateLineFixture();
        f.Publish("2.0.0");
        f.Publish("2.1.0-rc.1", "current", "prerelease");
        f.Publish("1.9.0", "1.x", "lts");
        f.Publish("1.10.0", "1.x", "lts");
        f.Publish("1.11.0-rc.2", "1.x", "prerelease");
        f.Publish("1.11.0-rc.10", "1.x", "prerelease");
        f.Publish("3.0.0-nightly.2", "daily", "nightly");
        f.Follow(line, channel, "1.0.0");
        f.Releases.AssertValid();
        byte[] before = File.ReadAllBytes(f.ManifestPath);
        var findings = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), findings, PublishedRelease.FromTag("v99.0.0"), now: f.Estate.Clock);
        Finding warning = Assert.Single(findings);
        Assert.Equal("manifest.release-behind", warning.Code);
        Assert.Contains($"newer published release {expected} is known in line {line}", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"estate upgrade --to {expected} --check", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("--line", warning.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(f.ManifestPath));
    }

    [Theory]
    [InlineData("current", "stable", "1.0.0", "1.1.0-preview", null, null)]
    [InlineData("current", "stable", "1.0.0", "2.0.0-rc.1", "current", "prerelease")]
    [InlineData("1.x", "lts", "1.0.0", "2.0.0", "current", "stable")]
    [InlineData("1.x", "lts", "1.1.0", "1.0.0", "1.x", "lts")]
    [InlineData("1.x", "lts", "1.1.0", "1.1.0", "1.x", "lts")]
    [InlineData("current", "prerelease", "2.0.0-rc.10", "2.0.0-rc.2", "current", "prerelease")]
    public void NoWarningOffersAnIneligibleOrNonNewerRelease(string line, string channel, string pin, string target, string? targetLine, string? targetChannel)
    {
        using var f = new EstateLineFixture();
        f.Publish(target, targetLine, targetChannel);
        f.Follow(line, channel, pin);
        f.Releases.AssertValid();
        var findings = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), findings, null, now: f.Estate.Clock);
        Assert.Empty(findings);
    }

    [Fact]
    public void SharedCurrentCacheNeverOffersAnotherLineEvenForTheFirstPartyRepository()
    {
        using var f = new EstateLineFixture();
        f.Follow("1.x", "lts", "1.0.0", "Vericairn/vericairn");
        var findings = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), findings, PublishedRelease.FromTag("v99.0.0"));
        Assert.Empty(findings);
    }

    [Fact]
    public async Task ExtraObservationFactsCannotRelabelADependencyForChecksOrUpgradeTargets()
    {
        using var f = new EstateLineFixture();
        using var published = new PublishedReleaseFixture();
        VerifiedRelease verified = await published.VerifyAsync();
        JsonObject body = JsonNode.Parse(verified.ObservationBody().GetRawText())!.AsObject();
        body["facts"]!["line"] = "1.x";
        body["facts"]!["channel"] = "lts";
        f.Estate.Add("observation", f.Estate.OperatorId, body.ToJsonString(), f.Estate.Estate.Reference);
        f.Follow("1.x", "lts", "1.0.0", "Vericairn/vericairn");
        f.Releases.AssertValid();
        var lts = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), lts, null);
        Assert.Empty(lts);
        Assert.Throws<InvalidOperationException>(() => EstateUpgrade.Plan(f.Estate.Load(), PublishedReleaseFixture.Version));
        f.Follow(version: "1.0.0", repository: "Vericairn/vericairn");
        var current = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), current, null);
        Assert.Contains("newer published release 9.2.0 is known", Assert.Single(current).Message, StringComparison.Ordinal);
        Assert.Equal("current", EstateUpgrade.Plan(f.Estate.Load(), PublishedReleaseFixture.Version).Line);
    }

    [Fact]
    public void RecordedLineMetadataTakesPrecedenceOverAnUnqualifiedCachedTag()
    {
        using var f = new EstateLineFixture();
        JsonObject body = ReleaseLineFixture.Body("9.2.0", "previous", "stable");
        body["repository"] = "Vericairn/vericairn";
        f.Releases.Publish(body);
        f.Follow(version: "1.0.0", repository: "Vericairn/vericairn");
        f.Releases.AssertValid();
        var findings = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), findings, PublishedRelease.FromTag("v9.2.0"));
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("lts")]
    [InlineData("prerelease")]
    public void EndOfSupportWarnsOnlyAfterTheBoundaryAndNamesSupportedLines(string channel)
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.1.0", "2.x", "lts", "2030-01-01T00:00:00Z");
        f.Publish("3.0.0");
        f.Publish("0.9.1", "legacy", "lts", "2027-01-01T00:00:00Z");
        f.Follow("1.x", channel); // An unavailable source baseline must not hide expired support.
        f.Releases.AssertValid();
        var end = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (DateTimeOffset supported in new[] { end.AddTicks(-1), end })
        {
            var findings = new FindingList();
            ReleasePinWarning.Add(f.Estate.Load(), findings, null, now: supported);
            Assert.Empty(findings);
        }
        var expired = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), expired, null, now: end.AddTicks(1));
        Finding warning = Assert.Single(expired);
        Assert.Equal("manifest.line-unsupported", warning.Code);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal(EstateLayout.ManifestPath, warning.Location);
        Assert.Contains("1.x", warning.Message, StringComparison.Ordinal);
        Assert.Contains("2028-01-01T00:00:00Z", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Supported lines: 2.x, current.", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", warning.Message, StringComparison.Ordinal);
        Assert.Contains("estate upgrade --line <name> --to <version> --check", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCurrentProductDeclarationControlsSupportIncludingLinesNotYetPublished()
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(
            new JsonObject { ["name"] = "1.x", ["channel"] = "lts", ["endOfSupport"] = "2029-01-01T00:00:00Z" },
            new JsonObject { ["name"] = "next", ["channel"] = "stable" });
        f.Releases.ReviseProduct(product);
        f.Follow("1.x", "lts");
        f.Releases.AssertValid();
        var extended = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), extended, null, now: new DateTimeOffset(2028, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(extended);
        var expired = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), expired, null, now: new DateTimeOffset(2029, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("Supported lines: next.", Assert.Single(expired).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLatestLtsPublicationControlsSupportAndAPrereleaseCannotEraseItsPromise()
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("1.2.0", "1.x", "lts", "2030-01-01T00:00:00Z");
        f.Publish("1.3.0-rc.1", "1.x", "prerelease");
        JsonObject unrelated = ReleaseLineFixture.Body("9.0.0", "unrelated", "stable");
        unrelated["repository"] = "Example/another-product";
        f.Releases.Publish(unrelated);
        f.Follow("1.x", "prerelease");
        f.Releases.AssertValid();
        var supported = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), supported, null, now: new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(supported);
        var expired = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), expired, null, now: new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Finding warning = Assert.Single(expired);
        Assert.Equal("manifest.line-unsupported", warning.Code);
        Assert.Contains("No supported lines are known in this estate.", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCheckReportsExpiredSupportWithoutChangingCanonicalStatusOrTheManifest()
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.0.0");
        f.Follow("1.x", "lts", "1.0.0");
        using var api = EstateService.Open(f.Estate.Root);
        api.PolicyClock = new AfterSupport();
        byte[] before = File.ReadAllBytes(f.ManifestPath);
        FindingsResponse checkedEstate = api.Check();
        Assert.Equal(0, checkedEstate.Errors);
        var unsupported = Assert.Single(checkedEstate.Findings, finding => finding.Code == "manifest.line-unsupported");
        Assert.Equal("warning", unsupported.Severity);
        Assert.Contains("Supported lines: current.", unsupported.Message, StringComparison.Ordinal);
        var newer = Assert.Single(checkedEstate.Findings, finding => finding.Code == "manifest.release-behind");
        Assert.Contains("estate upgrade --to 1.1.0 --check", newer.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2.0.0", newer.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.line-unsupported", api.Status().Text, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(f.ManifestPath));
    }

    [Fact]
    public void RuntimeUpgradeRequiresAnExplicitLineChangeAndCheckNeverWrites()
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.0.0");
        f.Follow("1.x", "lts");
        using var api = EstateService.Open(f.Estate.Root);
        Dictionary<string, byte[]> before = Snapshot(f.Estate.Root);
        var refused = api.Upgrade(new("2.0.0", f.Estate.OperatorId, false));
        Assert.Null(refused.Plan);
        Assert.Contains("--line current --to 2.0.0", refused.Problem!.Message, StringComparison.Ordinal);
        AssertSnapshot(before, f.Estate.Root);
        var sameLine = api.Upgrade(new("1.1.0", null, true));
        Assert.Null(sameLine.Problem);
        Assert.Equal("1.x", sameLine.Plan!.Line);
        var lineOnly = api.Upgrade(new("2.0.0", null, true) { Line = "current" });
        Assert.Null(lineOnly.Problem);
        Assert.Equal("current", lineOnly.Plan!.Line);
        Assert.Equal("lts", lineOnly.Plan.Channel);
        var request = new UpgradeRequest("2.0.0", f.Estate.OperatorId, true) { Line = "current", Channel = "stable" };
        var preview = api.Upgrade(request);
        Assert.Null(preview.Problem);
        Assert.Equal("current", preview.Plan!.Line);
        Assert.Equal("stable", preview.Plan.Channel);
        AssertSnapshot(before, f.Estate.Root);
        var applied = api.Upgrade(request with { Check = false });
        Assert.Null(applied.Problem);
        Assert.True(applied.Plan!.Applied);
        EstateModel model = api.Session.Load();
        Assert.Equal("current", model.Manifest!.Line);
        Assert.Equal("stable", model.Manifest.Channel);
        Assert.Equal("2.0.0", model.Manifest.Version);
        Assert.Single(model.OfType("migration"));
        Assert.Contains("from 1.x/lts to current/stable", model.OfType("migration").Single().Body.GetRawText(), StringComparison.Ordinal);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    [Fact]
    public void OptingIntoPrereleasesIsExplicitAndMoreStableTargetsKeepThatChoice()
    {
        using var f = new EstateLineFixture();
        f.Publish("2.0.0-rc.1", "current", "prerelease");
        f.Publish("2.0.0");
        Assert.Throws<InvalidOperationException>(() => EstateUpgrade.Plan(f.Estate.Load(), "2.0.0-rc.1"));
        UpgradePlan optedIn = EstateUpgrade.Plan(f.Estate.Load(), "2.0.0-rc.1", null, "prerelease");
        File.WriteAllBytes(f.ManifestPath, EstateUpgrade.ManifestAfter(f.Estate.Load().Manifest!, optedIn));
        UpgradePlan stable = EstateUpgrade.Plan(f.Estate.Load(), "2.0.0");
        Assert.Equal("current", stable.Line);
        Assert.Equal("prerelease", stable.Channel);
        var findings = new FindingList();
        ReleasePinWarning.Add(f.Estate.Load(), findings, null);
        Assert.Contains("newer published release 2.0.0 is known", Assert.Single(findings).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("recorded")]
    [InlineData("manifest-temporary")]
    public void AnInterruptedChannelOnlyChangeResumesOnceAndRefusesADifferentSelection(string stage)
    {
        using var f = new EstateLineFixture();
        f.Publish("2.0.0");
        using var api = EstateService.Open(f.Estate.Root);
        Assert.Null(api.Upgrade(new("2.0.0", f.Estate.OperatorId, false)).Problem);
        var request = new UpgradeRequest("2.0.0", f.Estate.OperatorId, false) { Channel = "prerelease" };
        EstateService.UpgradeStage = reached => { if (reached == stage) throw new InterruptedUpgrade(); };
        try { Assert.Throws<InterruptedUpgrade>(() => api.Upgrade(request)); }
        finally { EstateService.UpgradeStage = null; }
        EstateModel pending = api.Session.Load();
        Assert.Equal("stable", pending.Manifest!.Channel);
        Assert.NotNull(UpgradeProgress.Pending(pending.Manifest, pending.Records));
        Assert.Contains(pending.Findings, f => f.Code == "upgrade.incomplete");
        var other = api.Upgrade(request with { Channel = "nightly" });
        Assert.Null(other.Plan);
        Assert.Contains("finish that upgrade", other.Problem!.Message, StringComparison.Ordinal);
        var resumed = api.Upgrade(request);
        Assert.Null(resumed.Problem);
        Assert.True(resumed.Plan!.Applied);
        EstateModel moved = api.Session.Load();
        Assert.Equal("prerelease", moved.Manifest!.Channel);
        Assert.Null(UpgradeProgress.Pending(moved.Manifest, moved.Records));
        Assert.Equal(2, moved.OfType("migration").Count());
        var repeated = api.Upgrade(request);
        Assert.Null(repeated.Problem);
        Assert.False(repeated.Plan!.Applied);
        Assert.Equal(2, api.Session.Load().OfType("migration").Count());
    }

    private sealed class InterruptedUpgrade : Exception;

    private sealed class AfterSupport : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2029, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertSnapshot(Dictionary<string, byte[]> expected, string root)
    {
        Dictionary<string, byte[]> actual = Snapshot(root);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in expected) Assert.Equal(bytes, actual[path]);
    }
}
