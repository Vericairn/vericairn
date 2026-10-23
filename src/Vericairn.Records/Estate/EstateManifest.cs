// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>The manifest at estate/manifest.json: which estate this is and which Vericairn reads it.</summary>
public sealed partial class EstateManifest
{
    /// <summary>Format identifier.</summary>
    public const string Format1 = "vericairn.estate-manifest/1";

    /// <summary>Estate record id.</summary>
    public required string Estate { get; init; }

    /// <summary>Record format version.</summary>
    public required long RecordFormat { get; init; }

    /// <summary>Storage profile: repository-synced (records in a git repository) or local-only (records in this machine's live store).</summary>
    public required string Profile { get; init; }

    /// <summary>Profiles this release implements.</summary>
    public static readonly ImmutableArray<string> Profiles = ["repository-synced", "local-only"];

    /// <summary>"source" while Vericairn is built from a pinned commit; "release" once a release is pinned.</summary>
    public required string Mode { get; init; }

    /// <summary>GitHub owner/name of the Vericairn source repository.</summary>
    public string? Repository { get; init; }

    /// <summary>Pinned commit (source mode).</summary>
    public string? Commit { get; init; }

    /// <summary>Pinned version (release mode).</summary>
    public string? Version { get; init; }

    /// <summary>Release mode: the hash of the distributable per platform, so the bootstrap verifies what it fetches against the estate, never against a tag name.</summary>
    public IReadOnlyDictionary<string, string>? Artifacts { get; init; }

    /// <summary>Optional exact published dependency observation. Older release manifests remain readable.</summary>
    public RecordReference? Release { get; init; }

    /// <summary>One-line description of the pin for status output.</summary>
    public string Describe() => Mode == "release" ? $"release {Version}" : $"source {Repository}@{(Commit is { Length: >= 12 } ? Commit[..12] : Commit)}";

    /// <summary>Loads and validates the manifest; problems become findings and the result is null.</summary>
    public static EstateManifest? Load(string root, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        string path = Path.Combine(root, EstateLayout.ManifestPath);
        if (!File.Exists(path))
        {
            findings.Error("manifest.missing", EstateLayout.ManifestPath, "The estate manifest is missing.");
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Parse(bytes, findings);
    }

    /// <summary>Validates an exact manifest snapshot without reading a repository or machine settings.</summary>
    public static EstateManifest? Parse(byte[] bytes, FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(findings);
        JsonDocument document;
        try
        {
            document = CanonicalJson.Parse(bytes);
        }
        catch (CanonicalJsonException exception)
        {
            findings.Error("manifest.json", EstateLayout.ManifestPath, exception.Message);
            return null;
        }

        using (document)
        {
            JsonElement element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object)
            {
                findings.Error("manifest.shape", EstateLayout.ManifestPath, "The manifest is a JSON object.");
                return null;
            }

            try
            {
                if (!CanonicalJson.SerializePrettyUtf8(element).AsSpan().SequenceEqual(bytes))
                {
                    findings.Error("manifest.formatting", EstateLayout.ManifestPath, "The manifest is not in canonical pretty form.");
                }
            }
            catch (CanonicalJsonException exception)
            {
                findings.Error("manifest.canonical", EstateLayout.ManifestPath, exception.Message);
                return null;
            }

            int before = findings.Errors;
            var reader = new BodyReader(element, EstateLayout.ManifestPath + "#", findings);
            string? format = reader.Text("format", true, 100);
            if (format is not null && format != Format1)
            {
                reader.Error("manifest.format", "format", $"Expected '{Format1}'.");
            }

            string? estate = reader.Ulid("estate", true);

            // An estate a later release moved to a newer format is refused whole, never misread by an older build.
            if (element.TryGetProperty("recordFormat", out JsonElement declaredFormat) && declaredFormat.ValueKind == JsonValueKind.Number && declaredFormat.TryGetInt64(out long declared) && declared > Model.RecordFormats.Latest)
            {
                findings.Error("manifest.record-format", EstateLayout.ManifestPath + "#.recordFormat", $"This estate is at record format {declared}, and this build reads formats {Model.RecordFormats.First} to {Model.RecordFormats.Latest}; open it with the release its manifest pins.");
                return null;
            }

            long? recordFormat = reader.Number("recordFormat", true, 1, Model.RecordFormats.Latest);
            string profile = reader.Has("profile") ? reader.Enum("profile", false, Profiles) ?? Profiles[0] : Profiles[0];
            BodyReader? vericairn = reader.Nested("vericairn", true);
            string? mode = null, repository = null, commit = null, version = null;
            Dictionary<string, string>? artifacts = null;
            RecordReference? release = null;
            if (vericairn is not null)
            {
                mode = vericairn.Enum("mode", true, "source", "release");
                repository = vericairn.Text("repository", true, 200, Patterns.GitHubRepository, "owner/name");
                commit = vericairn.Text("commit", mode == "source", 40, CommitPattern(), "a 40-character lower-case hex commit id");
                version = vericairn.Text("version", mode == "release", 100, Patterns.SemanticVersion, "a semantic version");
                BodyReader? artifactsReader = vericairn.Nested("artifacts", mode == "release");
                BodyReader? releaseReader = vericairn.Nested("release", false);
                if (releaseReader is not null)
                {
                    string? type = releaseReader.Enum("type", true, "observation");
                    string? id = releaseReader.Ulid("id", true);
                    string? hash = releaseReader.Hash("hash", true);
                    releaseReader.NoOtherKeys();
                    if (mode != "release") releaseReader.Error("manifest.release", "type", "Only a release-mode manifest pins a published dependency observation.");
                    if (type is not null && id is not null && hash is not null) release = new(type, id, hash);
                }
                if (artifactsReader is not null)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (JsonProperty property in artifactsReader.Element.EnumerateObject())
                    {
                        string? hash = artifactsReader.Hash(property.Name, true);
                        if (Types.ReleaseType.Platforms.IndexOf(property.Name) < 0 || property.Name == "any")
                        {
                            artifactsReader.Error("body.enum", property.Name, "Artifact platforms are runtime identifiers such as linux-x64, osx-arm64 or win-x64.");
                        }
                        else if (hash is not null)
                        {
                            map[property.Name] = hash;
                        }
                    }

                    artifactsReader.NoOtherKeys();
                    artifacts = map;
                }

                vericairn.NoOtherKeys();
            }

            reader.NoOtherKeys();
            if (findings.Errors > before || estate is null || recordFormat is null || mode is null)
            {
                return null;
            }

            return new EstateManifest { Estate = estate, RecordFormat = recordFormat.Value, Profile = profile, Mode = mode, Repository = repository, Commit = commit, Version = version, Artifacts = artifacts, Release = release };
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial System.Text.RegularExpressions.Regex CommitPattern();
}
