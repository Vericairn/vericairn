// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Upgrade;

/// <summary>Whether the release an estate is pinned to before an upgrade can still read it after.</summary>
/// <param name="PreviousPin">The pin before, as the plan describes it.</param>
/// <param name="Readability">readable, one-way or unconfirmed.</param>
/// <param name="Reasons">What the previous release cannot read, or whose reading cannot be confirmed.</param>
public sealed record UpgradeReadability(string PreviousPin, string Readability, IReadOnlyList<string> Reasons)
{
    /// <summary>The previous release reads the manifest and every record after the upgrade.</summary>
    public const string Readable = "readable";

    /// <summary>The previous release cannot read the estate after the upgrade.</summary>
    public const string OneWay = "one-way";

    /// <summary>The previous pin is a source build whose release is unknown, and the upgrade adds something a release first reads.</summary>
    public const string Unconfirmed = "unconfirmed";
}

/// <summary>A restore point as an upgrade made it: where it is kept and what it holds.</summary>
public sealed record RestorePoint(string Id, string CreatedAt, string? Commit, bool Clean, string Directory, string StateDirectory, string StateHash, IReadOnlyList<string> Files);

/// <summary>
/// The way back from an upgrade (FIX-145, decision 01M7H0P65X2QXTPCQC6JEY7MNG). Before applying, an upgrade checks whether the previous release
/// can still read the estate after it: a release refuses a record format, a manifest field or a kind of record it does not know, so an
/// upgrade that moves the record format, or adds to the manifest or the records something the previous release predates, is one-way, and
/// applying it needs explicit confirmation. Before writing anything, an applied upgrade records a restore point: the estate repository's
/// commit and an export of this machine's local state for the clone, from which the stated steps return to the previous release.
/// </summary>
public static class UpgradeReturns
{
    /// <summary>The format of a restore point's description file.</summary>
    public const string RestorePointFormat = "vericairn.restore-point/1";

    /// <summary>The directory, beside a clone's live store, that holds its restore points.</summary>
    public const string RestorePointsDirectory = "restore-points";

    /// <summary>
    /// Of what an upgrade itself can add, each manifest field and each kind of record it writes, with the first published release that reads
    /// it: a release before that refuses the manifest or the record.
    /// </summary>
    internal static readonly (string Thing, string Release, string Description)[] Introduced =
    [
        ("manifest:vericairn.release", "0.8.3", "the manifest's pin of a verified dependency observation"),
        ("record:observation/vericairn-release", "0.8.3", "a vericairn-release observation of a published dependency"),
        ("manifest:vericairn.line", "0.9.0", "the manifest's followed release line"),
        ("manifest:vericairn.channel", "0.9.0", "the manifest's followed channel"),
        ("manifest:firstAuthority", "0.9.2", "the manifest's first authority"),
        ("record:history-checkpoint", "0.9.2", "a history checkpoint record"),
    ];

    /// <summary>
    /// Whether the release pinned before an upgrade reads the estate after it: the record format the manifest states after it, each manifest
    /// field and each kind of record the upgrade adds that the estate does not hold already. What the estate holds already, the previous
    /// release has been reading.
    /// </summary>
    public static UpgradeReadability Check(EstateModel model, UpgradePlan plan, byte[] manifestAfter, IReadOnlyList<string> writes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifestAfter);
        ArgumentNullException.ThrowIfNull(writes);
        EstateManifest before = model.Manifest ?? throw new InvalidOperationException("The estate has no usable manifest.");
        string? version = before.Mode == "release" ? before.Version : null;
        var reasons = new List<string>();
        long reads = PreviousReads(model, version) ?? before.RecordFormat;
        bool oneWay = plan.ToFormat > reads;
        if (oneWay)
            reasons.Add($"the estate moves from record format {plan.FromFormat} to {plan.ToFormat}, and {plan.FromPin} reads record formats up to {reads}");

        var added = new List<string>();
        string manifestPath = Path.Combine(model.Root, EstateLayout.ManifestPath);
        using (JsonDocument old = JsonDocument.Parse(File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : "{}"u8.ToArray()))
        using (JsonDocument next = JsonDocument.Parse(manifestAfter))
        {
            foreach ((string thing, _, _) in Introduced.Where(i => i.Thing.StartsWith("manifest:", StringComparison.Ordinal)))
            {
                string[] path = thing["manifest:".Length..].Split('.');
                if (Has(next.RootElement, path) && !Has(old.RootElement, path)) added.Add(thing);
            }
        }

        foreach (string written in writes.Distinct(StringComparer.Ordinal))
        {
            bool held = written switch
            {
                "record:observation/vericairn-release" => model.Current("observation").Any(VericairnReleaseObservation.Is),
                "record:history-checkpoint" => model.OfType("history-checkpoint").Any(),
                _ => true,
            };
            if (!held) added.Add(written);
        }

        bool unconfirmed = false;
        foreach (string thing in added)
        {
            (_, string release, string description) = Introduced.First(i => i.Thing == thing);
            if (version is null)
            {
                unconfirmed = true;
                reasons.Add($"{plan.FromPin} is a source build whose release is unknown, so whether it reads {description}, which {release} first reads, cannot be confirmed");
            }
            else if (Compare(version, release) < 0)
            {
                oneWay = true;
                reasons.Add($"{plan.FromPin} predates {description}, which {release} first reads");
            }
        }

        string readability = oneWay ? UpgradeReadability.OneWay : unconfirmed ? UpgradeReadability.Unconfirmed : UpgradeReadability.Readable;
        return new(plan.FromPin, readability, reasons);
    }

    // The record formats the previous release reads, from its release record or dependency observation when the estate holds one.
    private static long? PreviousReads(EstateModel model, string? version)
    {
        if (version is null) return null;
        RecordEnvelope? release = EstateUpgrade.RecordedTargets(model, version).FirstOrDefault()
            ?? model.Current("observation").Where(VericairnReleaseObservation.Is).FirstOrDefault(r => r.Body.GetProperty("facts").TryGetProperty("version", out JsonElement v) && v.GetString() == version);
        if (release is null) return null;
        JsonElement data = VericairnReleaseObservation.ReleaseBody(release);
        if (!data.TryGetProperty("recordFormat", out JsonElement format) || format.ValueKind != JsonValueKind.Object) return null;
        if (format.TryGetProperty("reads", out JsonElement reads) && reads.ValueKind == JsonValueKind.Array)
        {
            long[] formats = [.. reads.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out _)).Select(r => r.GetInt64())];
            if (formats.Length > 0) return formats.Max();
        }

        return format.TryGetProperty("writes", out JsonElement writes) && writes.TryGetInt64(out long written) ? written : null;
    }

    private static bool Has(JsonElement element, IReadOnlyList<string> path)
    {
        foreach (string name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return false;
        }

        return true;
    }

    // Major, minor and patch compared as numbers; a prerelease counts as before its release, so it is never assumed to read what it introduced.
    internal static int Compare(string version, string release)
    {
        static (long, long, long, bool) Parse(string text)
        {
            string core = text.Split('+')[0];
            bool prerelease = core.Contains('-', StringComparison.Ordinal);
            string[] parts = core.Split('-')[0].Split('.');
            long Part(int i) => i < parts.Length && long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : 0;
            return (Part(0), Part(1), Part(2), prerelease);
        }

        (long a1, long a2, long a3, bool ap) = Parse(version);
        (long b1, long b2, long b3, bool bp) = Parse(release);
        int order = (a1, a2, a3).CompareTo((b1, b2, b3));
        return order != 0 ? order : ap == bp ? 0 : ap ? -1 : 1;
    }

    /// <summary>
    /// Records a restore point before an upgrade writes anything: a consistent copy of the clone's live store and every other file of its
    /// local state beside it (the daemon's endpoint, locks and temporary files excepted), with an inventory of their hashes, in a new
    /// directory beside the live store under the user's Vericairn home. Nothing is written to the estate.
    /// </summary>
    internal static RestorePoint Record(EstateSession session, string fromPin, string toPin, string? commit, bool clean, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        string clone = Path.GetDirectoryName(session.Live.Path)!;
        string id = Ulid.NewUlid().Value;
        string directory = Path.Combine(clone, RestorePointsDirectory, id);
        string state = Path.Combine(directory, "state");
        OwnerOnlyState.CreateDirectory(Path.Combine(clone, RestorePointsDirectory));
        OwnerOnlyState.CreateDirectory(directory);
        OwnerOnlyState.CreateDirectory(state);
        try
        {
            string live = Path.GetFileName(session.Live.Path);
            session.Live.ExportTo(Path.Combine(state, live));
            foreach (string file in Directory.EnumerateFiles(clone).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(live, StringComparison.Ordinal) || name is "daemon.json" || name.EndsWith(".tmp", StringComparison.Ordinal) || name.EndsWith(".lock", StringComparison.Ordinal)) continue;
                if (new FileInfo(file).LinkTarget is not null) continue;
                File.Copy(file, Path.Combine(state, name));
            }

            var inventory = new JsonArray();
            var names = new List<string>();
            foreach (string file in Directory.EnumerateFiles(state).Order(StringComparer.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using FileStream stream = File.OpenRead(file);
                inventory.Add(new JsonObject { ["name"] = Path.GetFileName(file), ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(stream)), ["bytes"] = stream.Length });
                names.Add(Path.GetFileName(file));
            }

            using JsonDocument listed = JsonDocument.Parse(inventory.ToJsonString());
            string stateHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.SerializeUtf8(listed.RootElement)));
            string createdAt = RecordParser.FormatTimestamp(now);
            var description = new JsonObject
            {
                ["format"] = RestorePointFormat, ["id"] = id, ["createdAt"] = createdAt, ["estate"] = session.Manifest.Estate,
                ["fromPin"] = fromPin, ["toPin"] = toPin, ["commit"] = commit, ["clean"] = clean, ["state"] = inventory.DeepClone(), ["stateHash"] = stateHash,
            };
            using JsonDocument written = JsonDocument.Parse(description.ToJsonString());
            File.WriteAllBytes(Path.Combine(directory, "restore-point.json"), CanonicalJson.SerializePrettyUtf8(written.RootElement));
            return new(id, createdAt, commit, clean, directory, state, stateHash, names);
        }
        catch
        {
            Discard(directory);
            throw;
        }
    }

    /// <summary>Removes a restore point an upgrade that did not apply had made.</summary>
    internal static void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A restore point left behind holds only this machine's own state, beside its live store.
        }
    }
}
