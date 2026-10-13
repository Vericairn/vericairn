// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

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

    /// <summary>"source" while Vericairn is built from a pinned commit; "release" once a release is pinned.</summary>
    public required string Mode { get; init; }

    /// <summary>GitHub owner/name of the Vericairn source repository.</summary>
    public string? Repository { get; init; }

    /// <summary>Pinned commit (source mode).</summary>
    public string? Commit { get; init; }

    /// <summary>Pinned version (release mode).</summary>
    public string? Version { get; init; }

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
            long? recordFormat = reader.Number("recordFormat", true, 1, 1);
            BodyReader? vericairn = reader.Nested("vericairn", true);
            string? mode = null, repository = null, commit = null, version = null;
            if (vericairn is not null)
            {
                mode = vericairn.Enum("mode", true, "source", "release");
                repository = vericairn.Text("repository", true, 200, Patterns.GitHubRepository, "owner/name");
                commit = vericairn.Text("commit", mode == "source", 40, CommitPattern(), "a 40-character lower-case hex commit id");
                version = vericairn.Text("version", mode == "release", 100, Patterns.SemanticVersion, "a semantic version");
                vericairn.NoOtherKeys();
            }

            reader.NoOtherKeys();
            if (findings.Errors > before || estate is null || recordFormat is null || mode is null)
            {
                return null;
            }

            return new EstateManifest { Estate = estate, RecordFormat = recordFormat.Value, Mode = mode, Repository = repository, Commit = commit, Version = version };
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial System.Text.RegularExpressions.Regex CommitPattern();
}
