// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Upgrade;

/// <summary>What a migration may do: stage repository-relative files; the upgrade writes them only once every step succeeded, so a failed migration leaves the repository untouched.</summary>
public sealed class MigrationContext
{
    private readonly Dictionary<string, byte[]> staged = new(StringComparer.Ordinal);

    /// <summary>Creates a context over an estate.</summary>
    public MigrationContext(string root, EstateModel model)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>Repository root.</summary>
    public string Root { get; }

    /// <summary>The estate as loaded before the migration.</summary>
    public EstateModel Model { get; }

    /// <summary>Files staged so far, by repository-relative path.</summary>
    public IReadOnlyDictionary<string, byte[]> Staged => staged;

    /// <summary>Stages a file to be written when the whole upgrade succeeds; records are never rewritten, so record paths are refused.</summary>
    public void Stage(string relativePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(bytes);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\', StringComparison.Ordinal) || relativePath.Contains(':', StringComparison.Ordinal) ||
            relativePath.Split('/').Any(part => part is "" or "." or "..") || relativePath.StartsWith("records/", StringComparison.OrdinalIgnoreCase) || relativePath.Equals("records", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(EstateLayout.ManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A migration stages repository-relative files outside records/ and never the manifest.", nameof(relativePath));
        }

        staged[relativePath] = bytes;
    }
}

/// <summary>A record-format migration: forward-only, idempotent, named, and applied through a context so that a failure changes nothing.</summary>
public interface IRecordFormatMigration
{
    /// <summary>Stable name recorded in the migration record.</summary>
    string Name { get; }

    /// <summary>Format it reads.</summary>
    long FromFormat { get; }

    /// <summary>Format it writes.</summary>
    long ToFormat { get; }

    /// <summary>What the step changes, in a sentence, for the migration record.</summary>
    string Summary => $"moves the estate from record format {FromFormat} to {ToFormat}";

    /// <summary>Applies the migration by staging files; throws to abort the upgrade with nothing changed.</summary>
    void Apply(MigrationContext context);
}

/// <summary>What an upgrade would do, computed before anything changes.</summary>
/// <param name="FromPin">Current pin description.</param>
/// <param name="ToPin">Target pin description.</param>
/// <param name="FromFormat">Current record format.</param>
/// <param name="ToFormat">Target record format.</param>
/// <param name="Migrations">Migrations that would run, in order.</param>
/// <param name="Release">The published release record upgraded to.</param>
/// <param name="Artifacts">Distributable hash per platform, carried into the manifest.</param>
/// <param name="Anchored">Moving to record format 2, the admissions recorded without a signature that are in force, by id and hash, which the signed migration anchors.</param>
public sealed record UpgradePlan(string FromPin, string ToPin, long FromFormat, long ToFormat, IReadOnlyList<string> Migrations, RecordEnvelope Release, IReadOnlyDictionary<string, string> Artifacts, IReadOnlyList<RecordReference> Anchored);

/// <summary>Upgrades an estate to a published release: one reviewable change to the manifest plus a migration record, forward-only.</summary>
public static class EstateUpgrade
{
    private static IReadOnlyList<IRecordFormatMigration> migrations = [new RecordFormat2Migration(), new RecordFormat3Migration()];

    /// <summary>Record-format migrations this release knows: format 1 to 2 to 3.</summary>
    public static IReadOnlyList<IRecordFormatMigration> Migrations => migrations;

    /// <summary>Substitutes the known migrations for a synthetic set, for tests of the upgrade path; restored on dispose.</summary>
    internal static IDisposable UseMigrations(IReadOnlyList<IRecordFormatMigration> synthetic)
    {
        IReadOnlyList<IRecordFormatMigration> previous = migrations;
        migrations = synthetic;
        return new Restore(() => migrations = previous);
    }

    /// <summary>The migration steps of a plan, in order.</summary>
    public static IReadOnlyList<IRecordFormatMigration> Steps(UpgradePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Migrations.Select(name => Migrations.First(m => m.Name == name)).ToList();
    }

    /// <summary>Plans an upgrade to a version; throws with the reason when no published release of that version is recorded.</summary>
    public static UpgradePlan Plan(EstateModel model, string version)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        EstateManifest manifest = model.Manifest ?? throw new InvalidOperationException("The estate has no usable manifest.");
        List<RecordEnvelope> candidates = model.Current("release").Where(r => EstateModel.BodyString(r, "version") == version).ToList();
        RecordEnvelope release = candidates.FirstOrDefault(r => EstateModel.BodyString(r, "status") == "published")
            ?? throw new InvalidOperationException(candidates.Count == 0
                ? $"No release record has version {version}; a release is recorded in the estate before the estate is upgraded to it."
                : $"Release {version} is recorded as {EstateModel.BodyString(candidates[0], "status")}, not published; the operator's acceptance and the observed publication come first.");
        long toFormat = release.Body.TryGetProperty("recordFormat", out JsonElement format) && format.TryGetProperty("writes", out JsonElement writes) && writes.TryGetInt64(out long w) ? w : manifest.RecordFormat;
        if (toFormat < manifest.RecordFormat)
        {
            throw new InvalidOperationException($"Release {version} writes record format {toFormat}, older than this estate's format {manifest.RecordFormat}; upgrades are forward-only.");
        }

        if (toFormat > RecordFormats.Latest)
        {
            throw new InvalidOperationException($"Release {version} writes record format {toFormat}, which this Vericairn does not read; upgrade with release {version}'s own command line.");
        }

        // The target release must read every record already written: the estate's formats from the first up to its current one.
        if (format.ValueKind == JsonValueKind.Object && format.TryGetProperty("reads", out JsonElement reads) && reads.ValueKind == JsonValueKind.Array)
        {
            var read = reads.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out _)).Select(r => r.GetInt64()).ToHashSet();
            long unread = Enumerable.Range((int)RecordFormats.First, (int)(manifest.RecordFormat - RecordFormats.First + 1)).Select(f => (long)f).FirstOrDefault(f => !read.Contains(f));
            if (unread != 0)
            {
                throw new InvalidOperationException($"Release {version} does not read record format {unread}, which this estate's records use; it cannot be the estate's release.");
            }
        }

        var steps = new List<string>();
        long current = manifest.RecordFormat;
        while (current < toFormat)
        {
            IRecordFormatMigration step = Migrations.FirstOrDefault(m => m.FromFormat == current) ?? throw new InvalidOperationException($"No migration from record format {current} is known to this Vericairn.");
            if (step.ToFormat <= current || step.ToFormat > toFormat)
            {
                throw new InvalidOperationException($"Migration {step.Name} goes from record format {step.FromFormat} to {step.ToFormat}, which does not lead to format {toFormat}.");
            }

            steps.Add(step.Name);
            current = step.ToFormat;
        }

        var artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (release.Body.TryGetProperty("artifacts", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement artifact in list.EnumerateArray())
            {
                string? platform = artifact.TryGetProperty("platform", out JsonElement p) ? p.GetString() : null;
                string? name = artifact.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                string? hash = artifact.TryGetProperty("hash", out JsonElement h) ? h.GetString() : null;
                if (platform is not null && platform != "any" && hash is not null && name is not null && IsDistributable(name, version, platform))
                {
                    artifacts[platform] = hash;
                }
            }
        }

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException($"Release {version} names no distributable of the form vericairn-{version}-<platform>.tar.gz or .zip; the bootstrap could not verify what it fetches.");
        }

        string repository = EstateModel.BodyString(release, "repository") ?? manifest.Repository ?? "?";

        // Moving to record format 2, the admissions recorded without a signature that are in force are listed by id and hash, so that the
        // signed migration anchors them; from then on an unsigned admission the list does not name counts for nothing.
        IReadOnlyList<RecordReference> anchored = steps.Contains("record-format-2")
            ? [.. model.OfType("admission").Where(a => a.Signatures.Count == 0).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => new RecordReference("admission", a.Id, a.ComputedHash))]
            : [];
        return new UpgradePlan(manifest.Describe(), $"release {version}", manifest.RecordFormat, toFormat, steps, release, artifacts, anchored);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action action;

        public Restore(Action action)
        {
            this.action = action;
        }

        public void Dispose() => action();
    }

    /// <summary>
    /// Record format 1 to 2. Records are never rewritten: every record already written keeps its bytes, its hash, its signatures
    /// and the rules of format 1, and the records written after the move state format 2 and follow its rules. The step therefore
    /// stages nothing; the upgrade moves the manifest's format and the migration record names the step.
    /// </summary>
    private sealed class RecordFormat2Migration : IRecordFormatMigration
    {
        public string Name => "record-format-2";

        public long FromFormat => 1;

        public long ToFormat => 2;

        public string Summary => "records written from now on state record format 2 and follow its rules; every record already written keeps its bytes, hash and format";

        public void Apply(MigrationContext context) => ArgumentNullException.ThrowIfNull(context);
    }

    private sealed class RecordFormat3Migration : IRecordFormatMigration
    {
        public string Name => "record-format-3";

        public long FromFormat => 2;

        public long ToFormat => 3;

        public string Summary => "new records may carry signed policies and duty evidence; historical bytes stay unchanged, and no policy, duty, exception, placement or authority is inferred";

        public void Apply(MigrationContext context) => ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>The distributable name for a platform: a tar.gz on Linux and macOS, a zip on Windows.</summary>
    public static string DistributableName(string version, string platform) =>
        $"vericairn-{version}-{platform}" + (platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz");

    /// <summary>True when an artifact name is the distributable of a platform for a version (either archive form).</summary>
    public static bool IsDistributable(string name, string version, string platform) =>
        name == $"vericairn-{version}-{platform}.zip" || name == $"vericairn-{version}-{platform}.tar.gz";

    /// <summary>The manifest bytes after the upgrade: release mode, the version, and the artifact hashes.</summary>
    public static byte[] ManifestAfter(EstateManifest current, UpgradePlan plan)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(plan);
        var artifacts = new JsonObject();
        foreach ((string platform, string hash) in plan.Artifacts.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            artifacts[platform] = hash;
        }

        var manifest = new JsonObject
        {
            ["format"] = EstateManifest.Format1,
            ["estate"] = current.Estate,
            ["recordFormat"] = plan.ToFormat,
            ["profile"] = current.Profile,
            ["vericairn"] = new JsonObject
            {
                ["mode"] = "release",
                ["repository"] = EstateModel.BodyString(plan.Release, "repository") ?? current.Repository,
                ["version"] = EstateModel.BodyString(plan.Release, "version"),
                ["artifacts"] = artifacts,
            },
        };
        using JsonDocument document = JsonDocument.Parse(manifest.ToJsonString());
        return CanonicalJson.SerializePrettyUtf8(document.RootElement);
    }

    /// <summary>The body of the migration record that describes an applied plan.</summary>
    public static JsonObject MigrationBody(UpgradePlan plan, IReadOnlyList<string> changes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(changes);
        var body = new JsonObject
        {
            ["fromPin"] = plan.FromPin,
            ["toPin"] = plan.ToPin,
            ["fromFormat"] = plan.FromFormat,
            ["toFormat"] = plan.ToFormat,
            ["migrations"] = new JsonArray(plan.Migrations.Select(m => (JsonNode)m).ToArray()),
            ["changes"] = new JsonArray(changes.Select(c => (JsonNode)c).ToArray()),
            ["release"] = plan.Release.Id,
        };
        if (plan.Anchored.Count > 0)
        {
            body["anchored"] = new JsonArray(plan.Anchored.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["hash"] = a.Hash }).ToArray());
        }

        return body;
    }
}
