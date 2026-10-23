// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Updates;

/// <summary>A distributable named by the published SHA256SUMS.</summary>
/// <param name="Name">Archive file name.</param>
/// <param name="Platform">Runtime identifier.</param>
/// <param name="Hash">Published SHA-256 hash.</param>
public sealed record ReleaseArchive(string Name, string Platform, string Hash)
{
    /// <summary>The distributable file name for a version and platform.</summary>
    public static string FileName(string version, string platform) => $"vericairn-{version}-{platform}" + (platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz");

    /// <summary>The companion Workbench distributable name; its hash is retained but the command line verifies its own archive.</summary>
    public static string WorkbenchFileName(string version, string platform) => "vericairn-workbench-" + FileName(version, platform)[10..];
}

/// <summary>Release evidence returned only after the platform archive and both tag reads verified.</summary>
public sealed class VerifiedRelease
{
    private static readonly string[] Limitations = ["The builds are unsigned developer builds. Published hashes establish byte identity, not publisher signatures; only this machine's command-line archive was downloaded and verified."];
    internal VerifiedRelease(ReleaseBuild build, string tag, string checksumsHash, IReadOnlyList<ReleaseArchive> archives, string platform)
    {
        Build = build with { Reads = Array.AsReadOnly(build.Reads.ToArray()) };
        Tag = tag;
        ChecksumsHash = checksumsHash;
        Archives = Array.AsReadOnly(archives.ToArray());
        Platform = platform;
        ObservedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The release repository.</summary>
    public const string Repository = "Vericairn/vericairn";

    /// <summary>Identity and formats supplied by the running target build.</summary>
    public ReleaseBuild Build { get; }
    /// <summary>Verified release tag.</summary>
    public string Tag { get; }
    /// <summary>Hash of the exact published checksum file bytes.</summary>
    public string ChecksumsHash { get; }
    /// <summary>Distributables and their published hashes.</summary>
    public IReadOnlyList<ReleaseArchive> Archives { get; }
    /// <summary>The platform archive whose bytes were verified.</summary>
    public string Platform { get; }
    /// <summary>Time verification completed.</summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>The observation body, using the existing flat facts representation.</summary>
    public JsonElement ObservationBody() => JsonSerializer.SerializeToElement(new
    {
        kind = "vericairn-release",
        asOf = RecordParser.FormatTimestamp(ObservedAt),
        summary = $"Observed published Vericairn {Build.Version} and verified its {Platform} command-line archive.",
        facts = new
        {
            repository = Repository, version = Build.Version, tag = Tag, commit = Build.Commit,
            url = PageUrl(Tag), checksumsUrl = AssetUrl(Tag, "SHA256SUMS"), checksumsHash = ChecksumsHash,
            artifactNames = Archives.Select(a => a.Name).ToArray(), artifactPlatforms = Archives.Select(a => a.Platform).ToArray(),
            artifactHashes = Archives.Select(a => a.Hash).ToArray(),
            reads = Build.Reads.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray(), writes = Build.Writes,
            observer = "vericairn " + Build.Identity, verifiedPlatform = Platform, signing = "unsigned developer build",
        },
        limitations = Limitations,
    });

    /// <summary>The canonical GitHub page address for a validated tag.</summary>
    public static string PageUrl(string tag) => $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(tag)}";
    /// <summary>The canonical GitHub asset address for a validated tag and file name.</summary>
    public static string AssetUrl(string tag, string name) => $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";
}
