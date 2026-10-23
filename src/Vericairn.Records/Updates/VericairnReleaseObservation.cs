// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Updates;

/// <summary>The published dependency release carried by the existing observation record type.</summary>
public static class VericairnReleaseObservation
{
    /// <summary>True only for a published Vericairn dependency observation.</summary>
    public static bool Is(RecordEnvelope record) => record is { Type: "observation" } && EstateModel.BodyString(record, "kind") == "vericairn-release";

    /// <summary>Projects validated flat observation facts into the same release fields the upgrade planner reads.</summary>
    public static JsonElement ReleaseBody(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!Is(record)) return record.Body;
        JsonElement facts = record.Body.GetProperty("facts");
        string[] names = Strings(facts, "artifactNames"), platforms = Strings(facts, "artifactPlatforms"), hashes = Strings(facts, "artifactHashes");
        return JsonSerializer.SerializeToElement(new
        {
            repository = facts.GetProperty("repository").GetString(), version = facts.GetProperty("version").GetString(),
            commit = facts.GetProperty("commit").GetString(), status = "published",
            artifacts = names.Select((name, i) => new { name, platform = platforms[i], hash = hashes[i] }).ToArray(),
            recordFormat = new { reads = Strings(facts, "reads").Select(r => long.Parse(r, CultureInfo.InvariantCulture)).ToArray(), writes = facts.GetProperty("writes").GetInt64() },
        });
    }

    internal static void Validate(ValidationContext context, BodyReader facts)
    {
        string? repository = facts.Text("repository", true, 200);
        string? version = facts.Text("version", true, 100, Patterns.SemanticVersion, "a stable semantic version");
        string? tag = facts.Text("tag", true, 101);
        string? commit = facts.Text("commit", true, 40, Patterns.Commit, "a full lower-case commit");
        string? url = facts.Text("url", true, 500), sumsUrl = facts.Text("checksumsUrl", true, 500);
        facts.Hash("checksumsHash", true);
        string? observer = facts.Text("observer", true, 200);
        string? platform = facts.Enum("verifiedPlatform", true, ReleaseType.Platforms.Where(p => p != "any").ToArray());
        string? signing = facts.Text("signing", true, 200);
        IReadOnlyList<string>? names = facts.StringArray("artifactNames", true, allowEmpty: false, maxItemLength: 300);
        IReadOnlyList<string>? platforms = facts.StringArray("artifactPlatforms", true, allowEmpty: false, maxItemLength: 50);
        IReadOnlyList<string>? hashes = facts.StringArray("artifactHashes", true, allowEmpty: false, maxItemLength: 71);
        IReadOnlyList<string>? reads = facts.StringArray("reads", true, allowEmpty: false, maxItemLength: 4);
        long? writes = facts.Number("writes", true, 1, 1000);
        if (repository != VerifiedRelease.Repository || PublishedRelease.FromTag(version) is null || version?.Contains('+', StringComparison.Ordinal) == true
            || tag != "v" + version || url != VerifiedRelease.PageUrl(tag ?? "") || sumsUrl != VerifiedRelease.AssetUrl(tag ?? "", "SHA256SUMS")
            || observer != "vericairn " + version + "+" + commit || signing != "unsigned developer build")
            facts.Error("observation.vericairn-release", "version", "A published dependency names the stable Vericairn release, exact target build, canonical release addresses and unsigned signing state.");
        if (names is not null && platforms is not null && hashes is not null &&
            (names.Count != platforms.Count || names.Count != hashes.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Count
             || !names.Contains(ReleaseArchive.FileName(version!, platform ?? ""), StringComparer.Ordinal) || names.Where((name, i) => i >= platforms.Count || i >= hashes.Count
                 || platforms[i] == "any" || !ReleaseType.Platforms.Contains(platforms[i])
                 || name != ReleaseArchive.FileName(version!, platforms[i]) && name != ReleaseArchive.WorkbenchFileName(version!, platforms[i]) || !ContentHash.IsValid(hashes[i])).Any()))
            facts.Error("observation.vericairn-release", "artifactNames", "Archive names, platforms and hashes are nonempty parallel arrays with unique canonical distributable names, including the verified command-line platform.");
        if (reads is not null && (reads.Distinct(StringComparer.Ordinal).Count() != reads.Count
            || reads.Any(r => !long.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n is < 1 or > 1000 || r != n.ToString(CultureInfo.InvariantCulture))
            || !reads.Contains(writes?.ToString(CultureInfo.InvariantCulture) ?? "", StringComparer.Ordinal)))
            facts.Error("observation.vericairn-release", "reads", "Target formats are distinct positive integer strings and include the format it writes.");
        if (context.Record.Subject?.Type != "estate")
            context.Error("observation.vericairn-release", "A published dependency observation pins the estate charter as its exact subject.");
    }

    private static string[] Strings(JsonElement facts, string name) => facts.GetProperty(name).EnumerateArray().Select(v => v.GetString()!).ToArray();

    internal static void ValidatePin(EstateModel model)
    {
        if (model.Manifest is not { Release: { } pin } manifest) return;
        if (!model.ById.TryGetValue(pin.Id, out RecordEnvelope? record) || !Is(record) || pin != record.Reference)
        {
            model.Findings.Error("manifest.release", EstateLayout.ManifestPath, "The release pin must resolve to the exact vericairn-release observation and hash. Restore the pinned record or repeat the verified upgrade.");
            return;
        }
        // Body validation already reports malformed observations; a manifest must not turn one into a reader exception.
        try
        {
            JsonElement data = ReleaseBody(record);
            var artifacts = data.GetProperty("artifacts").EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == ReleaseArchive.FileName(data.GetProperty("version").GetString()!, a.GetProperty("platform").GetString()!))
                .ToDictionary(a => a.GetProperty("platform").GetString()!, a => a.GetProperty("hash").GetString()!, StringComparer.Ordinal);
            JsonElement format = data.GetProperty("recordFormat");
            var reads = format.GetProperty("reads").EnumerateArray().Select(f => f.GetInt64()).ToHashSet();
            if (data.GetProperty("version").GetString() != manifest.Version || data.GetProperty("repository").GetString() != manifest.Repository
                || record.Subject?.Id is not { } estateId || !model.ById.TryGetValue(estateId, out RecordEnvelope? estate) || model.Chain(estate)[0] != manifest.Estate
                || format.GetProperty("writes").GetInt64() != manifest.RecordFormat
                || Enumerable.Range((int)RecordFormats.First, (int)(manifest.RecordFormat - RecordFormats.First + 1)).Any(f => !reads.Contains(f))
                || manifest.Artifacts is null || artifacts.Count != manifest.Artifacts.Count || artifacts.Any(a => manifest.Artifacts.GetValueOrDefault(a.Key) != a.Value))
                model.Findings.Error("manifest.release", EstateLayout.ManifestPath, "The release pin, artifacts and record formats must agree with the pinned observation. Restore the manifest or repeat the verified upgrade.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            model.Findings.Error("manifest.release", EstateLayout.ManifestPath, "The pinned release observation is malformed. Restore the exact verified observation before using this pin.");
        }
    }
}
