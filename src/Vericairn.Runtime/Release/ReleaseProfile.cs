// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Release;

/// <summary>An artifact a product's release declares: a name with {version} and optionally {platform}, its role, and the platforms it is built for.</summary>
/// <param name="Name">The name pattern, for example widget-{version}-{platform}.zip.</param>
/// <param name="Role">distributable, sbom, notices, documentation or other.</param>
/// <param name="Platforms">The platforms the pattern is expanded for; empty when the name has no {platform}.</param>
public sealed record ReleaseArtifactSpec(string Name, string Role, IReadOnlyList<string> Platforms);

/// <summary>Where a product's releases go: the provider and its settings, as the product record declares them.</summary>
/// <param name="Provider">The provider id, for example github-releases.</param>
/// <param name="Settings">The provider's settings the product fixes, for example repository.</param>
public sealed record ReleaseDestination(string Provider, IReadOnlyDictionary<string, string> Settings);

/// <summary>An artifact as a release record will name it: its name, its role and its platform (any when it has none).</summary>
/// <param name="Name">The file name.</param>
/// <param name="Role">Its role.</param>
/// <param name="Platform">A platform identifier or any.</param>
public sealed record ExpectedArtifact(string Name, string Role, string Platform);

/// <summary>
/// What a product declares about its releases on its record: the destination (provider and settings), the checksum file and the
/// artifacts a complete release carries. A product that declares no artifacts is read by the generic rule instead: a platform
/// identifier in an asset's name gives its platform, a CycloneDX or SPDX file is the bill of materials, a notices file is the
/// notices, and a release is complete when every asset is named in its checksum file. Nothing in this class knows a product.
/// </summary>
public sealed class ReleaseProfile
{
    private ReleaseProfile(ReleaseDestination? destination, string? checksums, IReadOnlyList<ReleaseArtifactSpec> artifacts)
    {
        Destination = destination;
        Checksums = checksums;
        Artifacts = artifacts;
    }

    /// <summary>The profile of a product that declares nothing: the generic rule.</summary>
    public static ReleaseProfile None { get; } = new(null, null, []);

    /// <summary>The declared destination, or null.</summary>
    public ReleaseDestination? Destination { get; }

    /// <summary>The declared checksum file, or null (the provider's setting decides).</summary>
    public string? Checksums { get; }

    /// <summary>The declared artifacts; empty when the product declares none.</summary>
    public IReadOnlyList<ReleaseArtifactSpec> Artifacts { get; }

    /// <summary>Whether the product declares its artifacts.</summary>
    public bool DeclaresArtifacts => Artifacts.Count > 0;

    /// <summary>The profile a product record's body declares, or <see cref="None"/>.</summary>
    public static ReleaseProfile FromProductBody(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("release", out JsonElement release) || release.ValueKind != JsonValueKind.Object)
        {
            return None;
        }

        ReleaseDestination? destination = null;
        if (release.TryGetProperty("destination", out JsonElement d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("provider", out JsonElement provider) && provider.ValueKind == JsonValueKind.String)
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (d.TryGetProperty("settings", out JsonElement s) && s.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in s.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    settings[property.Name] = property.Value.GetString()!;
                }
            }

            destination = new ReleaseDestination(provider.GetString()!, settings);
        }

        string? checksums = release.TryGetProperty("checksums", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var artifacts = new List<ReleaseArtifactSpec>();
        if (release.TryGetProperty("artifacts", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement artifact in list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
            {
                string? name = artifact.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                string role = artifact.TryGetProperty("role", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "other";
                List<string> platforms = artifact.TryGetProperty("platforms", out JsonElement p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : [];
                if (name is not null)
                {
                    artifacts.Add(new ReleaseArtifactSpec(name, role, platforms));
                }
            }
        }

        return new ReleaseProfile(destination, checksums, artifacts);
    }

    /// <summary>The artifacts a complete release of a version carries, by the declaration; empty when nothing is declared.</summary>
    public IReadOnlyList<ExpectedArtifact> Expected(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var expected = new List<ExpectedArtifact>();
        foreach (ReleaseArtifactSpec spec in Artifacts)
        {
            string named = spec.Name.Replace("{version}", version, StringComparison.Ordinal);
            if (named.Contains("{platform}", StringComparison.Ordinal))
            {
                expected.AddRange(spec.Platforms.Select(platform => new ExpectedArtifact(named.Replace("{platform}", platform, StringComparison.Ordinal), spec.Role, platform)));
            }
            else
            {
                expected.Add(new ExpectedArtifact(named, spec.Role, "any"));
            }
        }

        return expected;
    }

    /// <summary>An asset's role and platform: by the declaration when it names the asset, otherwise by the generic rule.</summary>
    public ExpectedArtifact Classify(string assetName, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ExpectedArtifact? declared = Expected(version).FirstOrDefault(e => e.Name == assetName);
        return declared ?? Generic(assetName);
    }

    /// <summary>The generic rule: a platform identifier in the name gives the platform; a CycloneDX or SPDX file is the bill of materials; a notices file is the notices; a docs archive is documentation.</summary>
    public static ExpectedArtifact Generic(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        string platform = ReleaseFlow.Platforms.FirstOrDefault(p => ContainsPlatform(assetName, p)) ?? "any";
        string role = assetName.EndsWith(".cdx.json", StringComparison.OrdinalIgnoreCase) || assetName.EndsWith(".spdx.json", StringComparison.OrdinalIgnoreCase) ? "sbom"
            : assetName.Contains("NOTICE", StringComparison.OrdinalIgnoreCase) ? "notices"
            : assetName.Contains("-docs.", StringComparison.OrdinalIgnoreCase) ? "documentation"
            : platform != "any" ? "distributable"
            : "other";
        return new ExpectedArtifact(assetName, role, platform);
    }

    /// <summary>
    /// What a draft still lacks: by the declaration, every declared artifact without an asset that its checksum file names; by
    /// the generic rule, every asset its checksum file does not name, or the checksum file itself when the draft has no hashed asset.
    /// </summary>
    public IReadOnlyList<string> Missing(ReleaseObservation release, string version, string checksums)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksums);
        if (DeclaresArtifacts)
        {
            return Expected(version).Select(e => e.Name).Where(name => !release.Assets.Any(a => a.Name == name && a.Hash is not null)).ToList();
        }

        List<ReleaseAsset> assets = release.Assets.Where(a => a.Name != checksums).ToList();
        if (assets.Count == 0 || assets.All(a => a.Hash is null))
        {
            return [checksums];
        }

        return assets.Where(a => a.Hash is null).Select(a => a.Name).ToList();
    }

    /// <summary>The settings an operation runs with: the provider's defaults, then the product's declared settings for the same provider, then the request's own.</summary>
    public IReadOnlyDictionary<string, string> Settings(ProviderDescriptor descriptor, string provider, IReadOnlyDictionary<string, string>? requested)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ProviderSetting setting in descriptor.DeclaredSettings.Where(s => s.Default is not null))
        {
            settings[setting.Name] = setting.Default!;
        }

        if (Destination is not null && Destination.Provider == provider)
        {
            foreach ((string name, string value) in Destination.Settings)
            {
                settings[name] = value;
            }
        }

        if (Checksums is not null && descriptor.DeclaredSettings.Any(s => s.Name == "checksums"))
        {
            settings["checksums"] = Checksums;
        }

        foreach ((string name, string value) in requested ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                settings[name] = value.Trim();
            }
        }

        return settings;
    }

    private static bool ContainsPlatform(string name, string platform)
    {
        int index = name.IndexOf(platform, StringComparison.Ordinal);
        while (index >= 0)
        {
            bool before = index == 0 || name[index - 1] is '-' or '_' or '.';
            int after = index + platform.Length;
            bool next = after == name.Length || name[after] is '-' or '_' or '.';
            if (before && next)
            {
                return true;
            }

            index = name.IndexOf(platform, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
