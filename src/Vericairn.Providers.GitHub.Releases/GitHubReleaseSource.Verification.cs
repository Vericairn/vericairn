// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Vericairn.Records.Updates;

namespace Vericairn.Providers.GitHub.Releases;

public sealed partial class GitHubReleaseSource
{
    // The same anonymous handler rules as the small update lookup, with time to stream a distributable.
    private static readonly HttpClient ArchiveClient = new(CreateHandler()) { Timeout = TimeSpan.FromMinutes(5) };

    /// <inheritdoc />
    public async Task<VerifiedRelease> VerifyAsync(ReleaseBuild build, string platform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        string tag = "v" + build.Version;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            return await PublishedReleaseVerifier.VerifyAsync(build, platform, async (name, token) =>
            {
                using HttpResponseMessage response = await OpenAsync(Address(name), name == "SHA256SUMS", token).ConfigureAwait(false);
                using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[8192];
                int length;
                while ((length = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + length > PublishedReleaseVerifier.MetadataLimit)
                        throw new InvalidOperationException("Published release metadata exceeds its size limit. Nothing changed. Obtain the release metadata and SHA256SUMS again.");
                    bytes.Write(buffer, 0, length);
                }
                return bytes.ToArray();
            }, async (name, token) =>
            {
                using HttpResponseMessage response = await OpenAsync(Address(name), true, token).ConfigureAwait(false);
                using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                return await PublishedReleaseVerifier.HashArchiveAsync(stream, token).ConfigureAwait(false);
            }, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The published release could not be read from GitHub. Nothing changed. Retry with network access or supply --from <directory> with the downloaded release responses, SHA256SUMS and platform archive.");
        }

        string Address(string name) => name switch
        {
            "release.json" => $"https://api.github.com/repos/{VerifiedRelease.Repository}/releases/tags/{Uri.EscapeDataString(tag)}",
            "tag.json" => $"https://api.github.com/repos/{VerifiedRelease.Repository}/git/ref/tags/{Uri.EscapeDataString(tag)}",
            _ when name.StartsWith("tag-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) =>
                $"https://api.github.com/repos/{VerifiedRelease.Repository}/git/tags/{name[4..^5]}",
            _ => VerifiedRelease.AssetUrl(tag, name),
        };
    }

    private async Task<HttpResponseMessage> OpenAsync(string address, bool asset, CancellationToken token)
    {
        Uri uri = new(address);
        for (int redirects = 0; redirects <= 3; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Vericairn");
            HttpResponseMessage response = await (ReferenceEquals(client, PublicClient) ? ArchiveClient : client)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            Uri? location = response.Headers.Location;
            HttpStatusCode status = response.StatusCode;
            response.Dispose();
            if (asset && status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                && location is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true, UserInfo.Length: 0, Fragment.Length: 0 }
                && location.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com")
            {
                uri = location;
                continue;
            }
            throw new HttpRequestException("The anonymous release request was refused.");
        }
        throw new HttpRequestException("The release asset redirected too many times.");
    }
}
