// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Updates;

namespace Vericairn.Providers.GitHub;

/// <summary>An anonymous stable-release lookup, separate from the estate API transport.</summary>
/// <param name="client">The HTTP transport; the shipped instance disables credentials, cookies, redirects, proxies and trace propagation.</param>
public sealed class GitHubReleaseSource(HttpClient client) : IReleaseSource
{
    internal const string Endpoint = "https://api.github.com/repos/Vericairn/vericairn/releases/latest";
    private static readonly HttpClient PublicClient = new(CreateHandler())
        { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 1024 * 1024 };

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        ActivityHeadersPropagator = null,
    };

    /// <summary>The shipped anonymous transport, with a two-second timeout and bounded response size.</summary>
    public static GitHubReleaseSource Default { get; } = new(PublicClient);

    /// <inheritdoc />
    public async Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        // GitHub requires a User-Agent. This constant says nothing about the installed version or the caller.
        request.Headers.UserAgent.ParseAdd("Vericairn");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        JsonElement body = json.RootElement;
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("draft", out JsonElement draft) || draft.ValueKind != JsonValueKind.False
            || !body.TryGetProperty("prerelease", out JsonElement prerelease) || prerelease.ValueKind != JsonValueKind.False
            || !body.TryGetProperty("published_at", out JsonElement published) || published.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(published.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || !body.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String) return null;
        return PublishedRelease.FromTag(tag.GetString());
    }
}
