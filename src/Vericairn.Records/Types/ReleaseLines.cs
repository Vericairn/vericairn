// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Release-line defaults and the additive format-3 line, channel, support and backport rules.</summary>
public static partial class ReleaseLines
{
    private static readonly string[] Channels = ["stable", "lts", "prerelease", "nightly"];

    /// <summary>The named line, or current for a record without the field; never changes the record.</summary>
    public static string Line(JsonElement body) => PolicyRecords.Text(body, "line") ?? "current";

    /// <summary>The publication channel, or stable for a record without the field; never changes the record.</summary>
    public static string Channel(JsonElement body) => PolicyRecords.Text(body, "channel") ?? "stable";

    internal static void ValidateRelease(ValidationContext context, string? publishedAt)
    {
        BodyReader body = context.Body;
        string? line = body.Since(3, "line") ? body.Text("line", false, 40, LinePattern()) : null;
        string? channel = body.Since(3, "channel") ? body.Enum("channel", false, Channels) : null;
        line ??= "current";
        channel ??= "stable";
        DateTimeOffset? end = body.Since(3, "endOfSupport") ? Support(body, channel) : null;
        if (end is { } ends && DutyRecords.Time(publishedAt) is { } published && ends < published)
            body.Error("release.end-of-support", "endOfSupport", "End of support cannot precede this release's publication.");
        if (body.Since(3, "backportOf")) Backport(context, body.Nested("backportOf", false), line);

        if (context.Record.Subject is not { } subject || context.Find(subject.Id) is not { Type: "product" } product) return;
        RecordEnvelope[] releases = context.Model.OfType("release")
            .Where(r => PolicyRecords.ReleaseMatchesProduct(context.Model, r, product.Id) && Line(r.Body) == line).ToArray();
        JsonElement declared = Declarations(product).FirstOrDefault(d => PolicyRecords.Text(d, "name") == line);
        if (product.Body.TryGetProperty("lines", out _) && declared.ValueKind == JsonValueKind.Undefined)
            body.Error("release.line", "line", "This line is not declared by the release's product revision.");
        string[] fixedChannels = releases.Select(r => Channel(r.Body)).Where(c => c != "prerelease")
            .Concat(ProductHistory(context, product).SelectMany(Declarations).Where(d => PolicyRecords.Text(d, "name") == line)
                .Select(d => PolicyRecords.Text(d, "channel")).OfType<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (channel == "nightly" && releases.Any(r => Channel(r.Body) == "prerelease")
            || fixedChannels.Length > 1 || fixedChannels.Length == 1 && channel != fixedChannels[0]
            && !(channel == "prerelease" && fixedChannels[0] is "stable" or "lts"))
            body.Error("release.line-channel", "channel", "A line keeps its channel; only stable and lts lines may also carry prereleases of their next version.");

        if (channel == "prerelease" && fixedChannels.Any(c => c is "stable" or "lts"))
        {
            string? version = EstateModel.BodyString(context.Record, "version");
            if (version is null || !version.Contains('-', StringComparison.Ordinal)
                || releases.Any(r => r.AtTime < context.Record.AtTime && EstateModel.BodyString(r, "status") == "published"
                    && Channel(r.Body) is "stable" or "lts" && CompareVersionCore(version, EstateModel.BodyString(r, "version")) <= 0))
                body.Error("release.prerelease-version", "version", "A prerelease in a stable or lts line names a prerelease of a version newer than that line's earlier publications.");
        }

        if (EstateModel.BodyString(context.Record, "status") == "published"
            && context.Model.OfType("release").Any(r => r.Id != context.Record.Id && PolicyRecords.ReleaseMatchesProduct(context.Model, r, product.Id)
                && EstateModel.BodyString(r, "status") == "published" && EstateModel.BodyString(r, "version") == EstateModel.BodyString(context.Record, "version") && Line(r.Body) != line))
            body.Error("release.version-line", "line", "A published version belongs to only one line of a product.");
    }

    internal static void ValidateProduct(ValidationContext context)
    {
        BodyReader body = context.Body;
        if (body.Since(3, "lines"))
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader declaration in body.NestedArray("lines", false, allowEmpty: false) ?? [])
            {
                string? name = declaration.Text("name", true, 40, LinePattern());
                string? channel = declaration.Enum("channel", true, Channels);
                Support(declaration, channel);
                declaration.NoOtherKeys();
                if (name is null) continue;
                if (!names.Add(name)) declaration.Error("product.line-duplicate", "name", "Declare each line once.");
                IEnumerable<string?> earlier = ProductHistory(context, context.Record).Where(p => p.Id != context.Record.Id)
                    .SelectMany(Declarations).Where(d => PolicyRecords.Text(d, "name") == name).Select(d => PolicyRecords.Text(d, "channel"));
                IEnumerable<string> released = context.Model.OfType("release")
                    .Where(r => r.AtTime <= context.Record.AtTime && PolicyRecords.ReleaseMatchesProduct(context.Model, r, context.Record.Id) && Line(r.Body) == name)
                    .Select(r => Channel(r.Body));
                if (earlier.Any(c => c != channel) || released.Any(c => c != channel && !(c == "prerelease" && channel is "stable" or "lts")))
                    declaration.Error("product.line-channel", "channel", "A declared or released line keeps its channel.");
            }
        }
        if (body.Since(3, "cadence") && body.Nested("cadence", false) is { } cadence)
        {
            RecordEnvelope? policy = PolicyRecords.Reference(context, cadence.Nested("policy", true), true, "estate-policy");
            string? rule = PolicyRecords.Key(cadence, "rule");
            if (policy is not null && !DutyRecords.IsDuty(DutyRecords.Rule(policy, rule)))
                cadence.Error("product.cadence", "rule", "Cadence must name a recurring-duty rule in the exact policy revision.");
            cadence.NoOtherKeys();
        }
    }

    private static DateTimeOffset? Support(BodyReader body, string? channel)
    {
        string? text = body.Text("endOfSupport", channel == "lts", 100);
        if (body.Has("endOfSupport") && channel != "lts")
            body.Error("release.end-of-support", "endOfSupport", "Only the lts channel carries an end-of-support date.");
        if (text is null) return null;
        if (TimestampPattern().IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset result)) return result;
        body.Error("body.timestamp", "endOfSupport", "End of support must be an RFC 3339 timestamp with a time and UTC offset.");
        return null;
    }

    private static void Backport(ValidationContext context, BodyReader? reference, string line)
    {
        if (reference is null) return;
        string? id = reference.Ulid("id", true);
        string? hash = reference.Hash("hash", true);
        reference.NoOtherKeys();
        RecordEnvelope? release = context.RequireReference("backportOf", id, "release");
        if (release is null) return;
        if (hash != release.ComputedHash) reference.Error("release.backport-hash", "hash", "The backport pins the exact release hash.");
        if (!context.Model.IsCurrent(release) || EstateModel.BodyString(release, "status") != "published")
            reference.Error("release.backport-published", "id", "A backport names a current published release, never a candidate or withdrawn release.");
        if (!PolicyRecords.ReleaseMatchesProduct(context.Model, release, context.Record.Subject?.Id))
            reference.Error("release.backport-product", "id", "A backport names a release of the same product.");
        if (Line(release.Body) == line) reference.Error("release.backport-line", "id", "A backport carries a release from another line.");
        if (release.Id == context.Record.Id || release.AtTime > context.Record.AtTime)
            reference.Error("release.backport-time", "id", "A backport names an already recorded publication.");
    }

    private static IEnumerable<JsonElement> Declarations(RecordEnvelope product) => PolicyRecords.Items(PolicyRecords.Property(product.Body, "lines"));

    private static IEnumerable<RecordEnvelope> ProductHistory(ValidationContext context, RecordEnvelope product) =>
        context.Model.OfType("product").Where(p => PolicyRecords.Identity(context.Model, p) == PolicyRecords.Identity(context.Model, product));

    private static int CompareVersionCore(string left, string? right)
    {
        string[] a = left.Split('-')[0].Split('.');
        string[] b = (right ?? "").Split('-')[0].Split('.');
        if (a.Length != 3 || b.Length != 3) return 0;
        for (int i = 0; i < 3; i++)
        {
            if (!BigInteger.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger x)
                || !BigInteger.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger y)) return 0;
            int order = x.CompareTo(y);
            if (order != 0) return order;
        }
        return 0;
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9.-]{0,39}\z")]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})\z")]
    private static partial Regex TimestampPattern();
}
