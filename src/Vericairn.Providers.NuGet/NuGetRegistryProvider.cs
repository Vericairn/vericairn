// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.IO.Compression;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Providers.NuGet;

/// <summary>Reads nuget.org V3 registration and package-content indexes through an anonymous, bounded transport.</summary>
/// <param name="client">Transport for recorded-response tests; the shipped instance uses a credential-free handler.</param>
public sealed class NuGetRegistryProvider(HttpClient client) : IPackageRegistryProvider
{
    internal const string ServiceIndex = "https://api.nuget.org/v3/index.json";
    internal const int MaximumResponseBytes = 1024 * 1024;
    internal const int MaximumTotalBytes = 8 * MaximumResponseBytes;
    internal const int MaximumRequests = 32;
    private static readonly HttpClient PublicClient = new(CreateHandler()) { Timeout = TimeSpan.FromSeconds(20) };

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, Credentials = null,
        ActivityHeadersPropagator = null, AutomaticDecompression = DecompressionMethods.None,
    };

    /// <summary>The shipped anonymous provider, with a twenty-second deadline for the whole package observation.</summary>
    public static NuGetRegistryProvider Default { get; } = new(PublicClient);

    /// <inheritdoc />
    public string Provider => "nuget-org";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("nuget-v3-public", IPackageRegistryProvider.Contract, "implemented", ["observe"],
        ["Account holders perform unlisting and deprecation on nuget.org.",
         "Each package read is limited to twenty seconds, thirty-two requests, one MiB per response before and after decompression, and eight MiB total.",
         "Index observations establish registry state, not downloaded package bytes or release acceptance."]);

    /// <inheritdoc />
    public async Task<PackageRegistryState> ObserveAsync(string package, string version, CancellationToken cancellationToken = default)
    {
        if (!PackageIdentity.ValidId(package) || !PackageIdentity.ValidVersion(version))
            throw new ProviderRefusedException("Name a NuGet package id and a three-component release version.");
        package = package.ToLowerInvariant();
        version = version.ToLowerInvariant();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var read = new ReadSession(client, deadline.Token);
        try
        {
            JsonElement index = (await read.GetAsync(ServiceIndex).ConfigureAwait(false))!.Value;
            string registration = Resource(index, "RegistrationsBaseUrl/3.6.0");
            string content = Resource(index, "PackageBaseAddress/3.0.0");
            string packageRoot = registration + package + "/";
            JsonElement? versions = await read.GetAsync(content + package + "/index.json", allowAbsent: true).ConfigureAwait(false);
            bool published = false;
            if (versions is { } versionIndex)
            {
                JsonElement list = Array(versionIndex, "versions");
                foreach (JsonElement value in list.EnumerateArray())
                {
                    string v = String(value, 100);
                    if (string.Equals(v, version, StringComparison.OrdinalIgnoreCase)) published = true;
                }
            }
            JsonElement? registrationIndex = await read.GetAsync(packageRoot + "index.json", allowAbsent: true).ConfigureAwait(false);
            JsonElement? entry = null;
            if (registrationIndex is { } root)
            {
                JsonElement pages = Items(root);
                foreach (JsonElement page in pages.EnumerateArray())
                {
                    JsonElement expanded = page;
                    if (!page.TryGetProperty("items", out _))
                    {
                        string url = Text(page, "@id", 500);
                        if (!url.StartsWith(packageRoot + "page/", StringComparison.Ordinal)) throw Malformed();
                        expanded = (await read.GetAsync(url).ConfigureAwait(false))!.Value;
                        if (!page.TryGetProperty("count", out JsonElement expectedCount) || !expectedCount.TryGetInt32(out int n)
                            || n != Items(expanded).GetArrayLength()) throw Malformed();
                    }
                    foreach (JsonElement leaf in Items(expanded).EnumerateArray())
                    {
                        if (!leaf.TryGetProperty("catalogEntry", out JsonElement catalog) || catalog.ValueKind != JsonValueKind.Object) throw Malformed();
                        string id = Text(catalog, "id", 100);
                        string foundVersion = Text(catalog, "version", 100);
                        if (!string.Equals(id, package, StringComparison.OrdinalIgnoreCase)) throw Malformed();
                        if (!string.Equals(foundVersion, version, StringComparison.OrdinalIgnoreCase)) continue;
                        if (entry is not null) throw Malformed();
                        entry = leaf;
                    }
                }
            }
            if (published != (entry is not null))
                throw new ProviderRefusedException("NuGet registration and package-content indexes disagree; observe again after they converge.");
            string asOf = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
            if (entry is null)
                return new(package, version, "not-found", "unknown", "unknown", null, null, asOf, read.Evidence);

            JsonElement selected = entry.Value.GetProperty("catalogEntry");
            if (!selected.TryGetProperty("listed", out JsonElement listed) || listed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Malformed();
            string packageContent = Text(entry.Value, "packageContent", 500);
            if (packageContent != content + package + "/" + version + "/" + package + "." + version + ".nupkg") throw Malformed();
            PackageDeprecation? deprecation = Deprecation(selected);
            return new(package, version, "published", listed.GetBoolean() ? "listed" : "unlisted",
                deprecation is null ? "not-deprecated" : "deprecated", deprecation, packageContent, asOf, read.Evidence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderRefusedException("The NuGet observation deadline expired; no registry state was established.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            // Raw response and transport diagnostics may contain remote text. Keep failures portable and bounded.
            throw new ProviderRefusedException("NuGet returned an unavailable or malformed response; no registry state was established.");
        }
    }

    private static PackageDeprecation? Deprecation(JsonElement catalog)
    {
        if (!catalog.TryGetProperty("deprecation", out JsonElement value)) return null;
        if (value.ValueKind != JsonValueKind.Object) throw Malformed();
        string[] reasons = Array(value, "reasons").EnumerateArray().Select(v => String(v, 100)).ToArray();
        if (reasons.Length is < 1 or > 3 || reasons.Distinct(StringComparer.Ordinal).Count() != reasons.Length
            || reasons.Any(r => r is not ("Legacy" or "CriticalBugs" or "Other"))) throw Malformed();
        string? message = OptionalText(value, "message", 2000);
        string? alternate = null;
        string? range = null;
        if (value.TryGetProperty("alternatePackage", out JsonElement replacement))
        {
            alternate = Text(replacement, "id", 100);
            if (!PackageIdentity.ValidId(alternate)) throw Malformed();
            range = OptionalText(replacement, "range", 256);
        }
        return new(reasons, message, alternate, range);
    }

    private static string Resource(JsonElement index, string type)
    {
        string[] matches = Array(index, "resources").EnumerateArray()
            .Where(r => Text(r, "@type", 200) == type).Select(r => Text(r, "@id", 500)).Distinct(StringComparer.Ordinal).ToArray();
        if (matches.Length != 1 || !PackageIdentity.PublicUrl(matches[0]) || !matches[0].EndsWith('/')) throw Malformed();
        return matches[0];
    }

    private static JsonElement Items(JsonElement value)
    {
        JsonElement items = Array(value, "items");
        if (!value.TryGetProperty("count", out JsonElement count) || !count.TryGetInt32(out int n) || n != items.GetArrayLength()) throw Malformed();
        return items;
    }

    private static JsonElement Array(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out JsonElement array) && array.ValueKind == JsonValueKind.Array ? array : throw Malformed();

    private static string Text(JsonElement value, string key, int limit) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out JsonElement text) ? String(text, limit) : throw Malformed();

    private static string String(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && text.Length <= limit ? text : throw Malformed();

    private static string? OptionalText(JsonElement value, string key, int limit) => value.TryGetProperty(key, out JsonElement text) ? String(text, limit) : null;

    private static ProviderRefusedException Malformed() => new("NuGet returned incomplete or malformed registry metadata; no state was established.");

    private sealed class ReadSession(HttpClient transport, CancellationToken cancellation)
    {
        private readonly HashSet<string> visited = new(StringComparer.Ordinal);
        private long total;
        internal List<RegistryEvidence> Evidence { get; } = [];

        internal async Task<JsonElement?> GetAsync(string url, bool allowAbsent = false)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!PackageIdentity.PublicUrl(url) || visited.Count >= MaximumRequests || !visited.Add(url))
                throw new ProviderRefusedException("The NuGet response names an unsupported URL or exceeds the request limit.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK && !(allowAbsent && response.StatusCode == HttpStatusCode.NotFound))
                throw new ProviderRefusedException("NuGet did not return a successful response or a supported not-found response.");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw TooLarge();
            string[] encodings = response.Content.Headers.ContentEncoding.ToArray();
            string encoding = encodings.Length == 0 ? "identity" : encodings.Length == 1 ? encodings[0].ToLowerInvariant() : "unsupported";
            if (encoding is not ("identity" or "gzip")) throw Malformed();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            byte[] body = await ReadBoundedAsync(stream).ConfigureAwait(false);
            Evidence.Add(new(url, ContentHash.Sha256(body), (int)response.StatusCode, body.Length, encoding));
            // Preserve the digest of the response bytes before decoding the gzip registration hive.
            // Both the compressed body and its expansion count towards this session's budget.
            if (encoding == "gzip")
            {
                using var compressed = new MemoryStream(body);
                await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                body = await ReadBoundedAsync(gzip).ConfigureAwait(false);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicates(document.RootElement);
            return document.RootElement.Clone();
        }

        private async Task<byte[]> ReadBoundedAsync(Stream stream)
        {
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                total += count;
                if (bytes.Length + count > MaximumResponseBytes || total > MaximumTotalBytes) throw TooLarge();
                bytes.Write(buffer, 0, count);
            }
            return bytes.ToArray();
        }

        private static void CheckDuplicates(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw Malformed();
                    CheckDuplicates(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in value.EnumerateArray()) CheckDuplicates(item);
        }

        private static ProviderRefusedException TooLarge() => new("The NuGet response exceeds the observation byte limit.");
    }
}
