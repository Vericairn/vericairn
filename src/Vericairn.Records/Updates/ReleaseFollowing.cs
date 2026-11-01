// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Updates;

// One selection rule for upgrade plans, checks and clients. Stable and LTS have equal stability;
// the line, not a version's major number, determines its compatibility and support promise.
internal static partial class ReleaseFollowing
{
    internal static bool Allows(string line, string channel, string releaseLine, string releaseChannel, string? version) =>
        line == releaseLine && Rank(channel) >= 0 && Rank(releaseChannel) >= 0 && Rank(releaseChannel) <= Rank(channel)
        && version is not null && VersionPattern().IsMatch(version)
        && (Rank(channel) > 0 || !version.Split('+')[0].Contains('-', StringComparison.Ordinal));

    internal static bool Allows(string line, string channel, JsonElement release) =>
        Allows(line, channel, ReleaseLines.Line(release), ReleaseLines.Channel(release), PolicyRecords.Text(release, "version"));

    private static int Rank(string channel) => channel switch { "stable" or "lts" => 0, "prerelease" => 1, "nightly" => 2, _ => -1 };

    internal static bool IsNewer(string version, string? baseline) => baseline is not null
        && VersionPattern().IsMatch(version) && VersionPattern().IsMatch(baseline) && Compare(version, baseline) > 0;

    // Running build identities and cached tags can include metadata even though record versions omit it.
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z")]
    private static partial Regex VersionPattern();

    // SemVer precedence, including numeric prerelease identifiers and arbitrarily large core numbers.
    private static int Compare(string left, string right)
    {
        string[] a = left.Split('+')[0].Split('-', 2), b = right.Split('+')[0].Split('-', 2);
        string[] ac = a[0].Split('.'), bc = b[0].Split('.');
        for (int i = 0; i < 3; i++)
        {
            int order = Number(ac[i]).CompareTo(Number(bc[i]));
            if (order != 0) return order;
        }
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        string[] ap = a[1].Split('.'), bp = b[1].Split('.');
        for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        {
            bool an = ap[i].All(char.IsAsciiDigit), bn = bp[i].All(char.IsAsciiDigit);
            int order = an && bn ? Number(ap[i]).CompareTo(Number(bp[i]))
                : an != bn ? (an ? -1 : 1) : string.Compare(ap[i], bp[i], StringComparison.Ordinal);
            if (order != 0) return order;
        }
        return ap.Length.CompareTo(bp.Length);
    }

    private static BigInteger Number(string value) => BigInteger.Parse(value, CultureInfo.InvariantCulture);

    internal static IEnumerable<RecordEnvelope> Publications(EstateModel model) => model.Current("release")
        .Where(r => EstateModel.BodyString(r, "status") == "published")
        .Concat(model.Current("observation").Where(VericairnReleaseObservation.Is))
        .Where(r => string.Equals(PolicyRecords.Text(Data(r), "repository"), model.Manifest?.Repository, StringComparison.OrdinalIgnoreCase));

    internal static JsonElement Data(RecordEnvelope release)
    {
        if (!VericairnReleaseObservation.Is(release)) return release.Body;
        JsonElement facts = PolicyRecords.Property(release.Body, "facts");
        // Dependency verification establishes current/stable. Extra flat observation facts cannot
        // relabel that dependency; the upgrade planner's ReleaseBody projection has the same defaults.
        return JsonSerializer.SerializeToElement(new { repository = PolicyRecords.Text(facts, "repository"), version = PolicyRecords.Text(facts, "version") });
    }

    internal static string? Newest(EstateModel model, PublishedRelease? cached)
    {
        if (model.Manifest is not { } manifest) return null;
        RecordEnvelope[] publications = Publications(model).ToArray();
        JsonElement[] cachedRecords = cached is null ? [] : publications.Select(Data)
            .Where(data => PolicyRecords.Text(data, "version") == cached.Version).ToArray();
        string? newest = string.Equals(manifest.Repository, PublishedRelease.Repository, StringComparison.OrdinalIgnoreCase)
            && cached is not null && Allows(manifest.Line, manifest.Channel, "current", "stable", cached.Version)
            && (cachedRecords.Length == 0 || cachedRecords.Any(data => Allows(manifest.Line, manifest.Channel, data))) ? cached.Version : null;
        foreach (RecordEnvelope record in publications)
        {
            JsonElement data = Data(record);
            if (!Allows(manifest.Line, manifest.Channel, data)) continue;
            string version = PolicyRecords.Text(data, "version")!;
            if (newest is null || IsNewer(version, newest)) newest = version;
        }
        return newest;
    }

    internal static void WarnUnsupported(EstateModel model, FindingList findings, DateTimeOffset now)
    {
        if (model.Manifest is not { } manifest) return;
        RecordEnvelope[] releases = Publications(model).ToArray();
        var lines = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        // Newer recorded support information replaces older publications in that line.
        foreach (RecordEnvelope release in releases.OrderBy(r => r.AtTime).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            JsonElement data = Data(release);
            if (ReleaseLines.Channel(data) != "prerelease" || !lines.ContainsKey(ReleaseLines.Line(data)))
                lines[ReleaseLines.Line(data)] = data;
        }
        // A product's current declarations are its explicit support promise, including lines without a release yet.
        foreach (RecordEnvelope product in model.Current("product").Where(p => releases.Any(r => r.Type == "release"
            && PolicyRecords.ReleaseMatchesProduct(model, r, p.Id))).OrderBy(p => p.AtTime).ThenBy(p => p.Id, StringComparer.Ordinal))
            foreach (JsonElement declaration in PolicyRecords.Items(PolicyRecords.Property(product.Body, "lines")))
                if (PolicyRecords.Text(declaration, "name") is { } name) lines[name] = declaration;
        if (!lines.TryGetValue(manifest.Line, out JsonElement followed) || ReleaseLines.Channel(followed) != "lts"
            || DutyRecords.Time(PolicyRecords.Text(followed, "endOfSupport")) is not { } end || now <= end) return;
        string[] supported = lines.Where(l => ReleaseLines.Channel(l.Value) != "lts"
            || DutyRecords.Time(PolicyRecords.Text(l.Value, "endOfSupport")) is { } until && now <= until)
            .Select(l => l.Key).Order(StringComparer.Ordinal).ToArray();
        findings.Warning("manifest.line-unsupported", EstateLayout.ManifestPath,
            $"The followed LTS line {manifest.Line} passed its end of support ({PolicyRecords.Text(followed, "endOfSupport")}). "
            + (supported.Length == 0 ? "No supported lines are known in this estate. " : $"Supported lines: {string.Join(", ", supported)}. ")
            + "Review a supported line's published release, then explicitly change lines with vericairn estate upgrade --line <name> --to <version> --check; apply with --by <operator>. Nothing changes automatically.");
    }
}
