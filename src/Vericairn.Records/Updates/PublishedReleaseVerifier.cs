// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Updates;

/// <summary>Shared verification for anonymous GitHub reads and a downloaded offline directory. Writes nothing.</summary>
public static class PublishedReleaseVerifier
{
    /// <summary>Maximum metadata or checksum file size.</summary>
    public const int MetadataLimit = 1024 * 1024;
    /// <summary>Maximum distributable size, enforced while streaming.</summary>
    public const long ArchiveLimit = 512L * 1024 * 1024;

    /// <summary>
    /// Verifies release.json, tag.json (before and after the archive), optional tag-&lt;sha&gt;.json annotated tag objects,
    /// SHA256SUMS and the platform archive. Readers receive only these validated leaf names. No file is written.
    /// </summary>
    public static async Task<VerifiedRelease> VerifyAsync(ReleaseBuild build, string platform,
        Func<string, CancellationToken, Task<byte[]>> read, Func<string, CancellationToken, Task<string>> hashArchive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(hashArchive);
        if (PublishedRelease.FromTag(build.Version) is null || build.Version.Contains('+', StringComparison.Ordinal) || build.Version.StartsWith('v') || !ReleaseBuild.IsCommit(build.Commit)
            || build.Reads.Count == 0 || build.Reads.Any(f => f is < 1 or > 1000) || build.Writes is < 1 or > 1000 || !build.Reads.Contains(build.Writes))
            throw Refuse("The target build has no usable stable release identity or record formats.", "Run the target release's published command line.");
        if (platform == "any" || !ReleaseType.Platforms.Contains(platform))
            throw Refuse("No published archive is supported for this platform.", "Run on a supported release platform.");

        using JsonDocument release = Parse(await read("release.json", cancellationToken).ConfigureAwait(false));
        JsonElement metadata = release.RootElement;
        string tag = "v" + build.Version;
        if (String(metadata, "tag_name") != tag || !False(metadata, "draft") || !False(metadata, "prerelease")
            || !DateTimeOffset.TryParse(String(metadata, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw Refuse("The requested version is not a stable published release (it may be a draft or prerelease).", "Choose a published stable version and run its command line.");
        if (String(metadata, "html_url") != VerifiedRelease.PageUrl(tag))
            throw Refuse("The release page does not match the requested repository and tag.", "Download the release metadata again from the Vericairn repository.");
        string? target = String(metadata, "target_commitish");
        // GitHub can retain a branch name here. The immutable target is then the commit embedded in the target build,
        // checked against the peeled tag both before and after the archive. An explicit commit must agree too.
        if (string.IsNullOrWhiteSpace(target) || (ReleaseBuild.IsCommit(target) && target != build.Commit))
            throw Refuse("The release's commit differs from the target build.", "Obtain the command line and metadata for the same published release; investigate a moved tag.");

        string before = await TagAsync(build, tag, read, cancellationToken).ConfigureAwait(false);
        RequireAsset(metadata, tag, "SHA256SUMS");
        byte[] sums = await read("SHA256SUMS", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ReleaseArchive> archives = ParseChecksums(build.Version, sums);
        foreach (ReleaseArchive archive in archives) RequireAsset(metadata, tag, archive.Name);
        ReleaseArchive selected = archives.FirstOrDefault(a => a.Name == ReleaseArchive.FileName(build.Version, platform))
            ?? throw Refuse("SHA256SUMS has no archive for this platform.", "Download the complete published release for a supported platform.");
        string actual = await hashArchive(selected.Name, cancellationToken).ConfigureAwait(false);
        if (actual != selected.Hash)
            throw Refuse("The platform archive differs from its SHA256SUMS line.", "Download SHA256SUMS and the archive again from the same release; investigate if the mismatch persists.");
        string after = await TagAsync(build, tag, read, cancellationToken).ConfigureAwait(false);
        if (after != before)
            throw Refuse("The release tag moved during verification.", "Investigate the changed tag and obtain a consistent published release before retrying.");
        return new VerifiedRelease(build, tag, ContentHash.Sha256(sums), archives, platform);
    }

    /// <summary>Runs the same verification entirely from downloaded files; never consults the network.</summary>
    public static async Task<VerifiedRelease> FromDirectoryAsync(string directory, ReleaseBuild build, string platform, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            return await VerifyAsync(build, platform, async (name, token) =>
            {
                using FileStream file = File.OpenRead(Path.Combine(directory, name));
                if (file.Length > MetadataLimit) throw Refuse("Downloaded metadata exceeds its size limit.", "Download the release metadata and SHA256SUMS again.");
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[8192];
                int length;
                while ((length = await file.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + length > MetadataLimit) throw Refuse("Downloaded metadata exceeds its size limit.", "Download the release metadata and SHA256SUMS again.");
                    bytes.Write(buffer, 0, length);
                }
                return bytes.ToArray();
            }, async (name, token) =>
            {
                using FileStream file = File.OpenRead(Path.Combine(directory, name));
                return await HashArchiveAsync(file, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Refuse("The offline release directory is incomplete or unreadable.", "Supply release.json, tag.json, any annotated tag objects, SHA256SUMS and the platform archive from the same published release.");
        }
    }

    /// <summary>Hashes an archive stream with a bounded byte count and no scratch files.</summary>
    public static async Task<string> HashArchiveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long total = 0;
        int length;
        while ((length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += length;
            if (total > ArchiveLimit) throw Refuse("The release archive exceeds its size limit.", "Obtain the supported platform archive from the published release.");
            hash.AppendData(buffer, 0, length);
        }
        return ContentHash.Prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static ReleaseArchive[] ParseChecksums(string version, byte[] bytes)
    {
        if (bytes.Length > MetadataLimit) throw Refuse("SHA256SUMS exceeds its size limit.", "Download the published SHA256SUMS again.");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            string value = line.TrimEnd('\r');
            if (value.Length == 0) continue;
            if (value.Length < 67 || value[64] != ' ' || value[65] is not (' ' or '*') || !ContentHash.IsValid("sha256:" + value[..64])
                || value[66..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) || !entries.TryAdd(value[66..], "sha256:" + value[..64]))
                throw Refuse("SHA256SUMS contains a malformed or duplicate line.", "Download an unmodified SHA256SUMS from the published release.");
        }
        return ReleaseType.Platforms.Where(p => p != "any").SelectMany(p => new[] { ReleaseArchive.FileName(version, p), ReleaseArchive.WorkbenchFileName(version, p) }
                .Select(name => new ReleaseArchive(name, p, entries.GetValueOrDefault(name)!)))
            .Where(a => a.Hash is not null).ToArray();
    }

    private static async Task<string> TagAsync(ReleaseBuild build, string tag, Func<string, CancellationToken, Task<byte[]>> read, CancellationToken token)
    {
        using JsonDocument reference = Parse(await read("tag.json", token).ConfigureAwait(false));
        if (String(reference.RootElement, "ref") != "refs/tags/" + tag || !reference.RootElement.TryGetProperty("object", out JsonElement target))
            throw Refuse("The tag response does not name the requested release tag.", "Download the exact tag reference again.");
        string identity = String(target, "sha") ?? "";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; depth < 8; depth++)
        {
            string? sha = String(target, "sha");
            string? type = String(target, "type");
            if (!ReleaseBuild.IsCommit(sha) || !seen.Add(sha!)) break;
            if (type == "commit")
            {
                if (sha != build.Commit)
                    throw Refuse("The release tag does not point to the target release's commit, or moved since it was read.", "Investigate the tag and obtain the command line and metadata for the same published commit.");
                return identity;
            }
            if (type != "tag") break;
            using JsonDocument annotated = Parse(await read("tag-" + sha + ".json", token).ConfigureAwait(false));
            if (String(annotated.RootElement, "sha") != sha || !annotated.RootElement.TryGetProperty("object", out JsonElement next)) break;
            target = next.Clone();
        }
        throw Refuse("The release tag cannot be resolved to an exact commit.", "Obtain the tag reference and its annotated tag objects from GitHub again.");
    }

    private static void RequireAsset(JsonElement release, string tag, string name)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array
            || assets.EnumerateArray().Count(a => String(a, "name") == name) != 1
            || !assets.EnumerateArray().Any(a => String(a, "name") == name && String(a, "browser_download_url") == VerifiedRelease.AssetUrl(tag, name)))
            throw Refuse($"The published release has no unambiguous {name} asset at its expected address.", "Obtain the complete published release and its SHA256SUMS; do not create a release record by hand.");
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MetadataLimit) throw new JsonException();
            JsonDocument document = CanonicalJson.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (Exception exception) when (exception is JsonException or CanonicalJsonException) { }
        throw Refuse("The published release metadata is malformed or too large.", "Download the release and tag responses again from GitHub.");
    }

    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    private static bool False(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.False;
    private static InvalidOperationException Refuse(string reason, string next) => new(reason + " Nothing changed. " + next);
}
