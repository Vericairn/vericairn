// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Updates;

namespace Vericairn.Sdk;

internal interface IReleaseSource
{
    Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken);
}

// This client has no credentials, cookies, redirects or tracing propagation. It never uses the estate API transport.
internal sealed class GitHubReleaseSource(HttpClient client) : IReleaseSource
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

    internal static GitHubReleaseSource Default { get; } = new(PublicClient);

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

// Internal implementation shared by the two shipped clients; it adds no SDK or estate API contract.
internal sealed class ReleaseUpdates(ReleaseUpdateStore store, IReleaseSource source, TimeProvider clock)
{
    internal ReleaseUpdates() : this(new ReleaseUpdateStore(), GitHubReleaseSource.Default, TimeProvider.System) { }

    internal bool Enabled => store.Enabled;

    internal async Task<PublishedRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        PublishedRelease? cached = store.CachedRelease();
        if (!store.Enabled) return cached;
        try
        {
            using FileStream held = store.Acquire();
            if (!store.Enabled) return cached;
            ReleaseCheckState state = store.ReadState();
            cached = state.Release;
            DateTimeOffset now = clock.GetUtcNow();
            // A future timestamp (for example after a clock correction) also suppresses another request.
            if (state.LastAttemptAt is DateTimeOffset previous && now - previous < TimeSpan.FromDays(1)) return cached;
            // Persist the attempt before any request, even if the request fails or this client is killed.
            state = state with { LastAttemptAt = now };
            store.WriteState(state);
            PublishedRelease? latest = await source.LatestAsync(cancellationToken).ConfigureAwait(false);
            if (latest is not null)
            {
                store.WriteState(state with { LastSuccessAt = clock.GetUtcNow(), Tag = latest.Tag });
                return latest;
            }
        }
        catch (Exception exception) when (ReleaseUpdateStore.Unavailable(exception) || exception is HttpRequestException or OperationCanceledException)
        {
            // Offline, timeout, malformed response, contention or inaccessible local state: keep the last answer.
        }
        return cached;
    }

    internal static string Disclosure(bool enabled) => enabled
        ? "Release check: on. CLI version, doctor and status, and Workbench, share at most one unauthenticated GitHub release check per 24 hours; no identifiers or usage telemetry are sent. Disable: vericairn updates configure --enabled false."
        : "Release check: off; no request is made. Cached release information may still be shown. Enable: vericairn updates configure --enabled true.";

    internal static string Notice(PublishedRelease? release, string installed) => release is null || !release.IsNewerThan(installed) ? string.Empty
        : $"Newer Vericairn release: {release.Version} (installed {installed.Split('+')[0]}).\n"
            + $"Release notes: {release.NotesUrl}\n"
            + $"NuGet tool: dotnet tool update --global dotnet-vc --version {release.Version} (omit --global for a local tool).\n"
            + $"Archive download (CLI or Workbench): {release.NotesUrl} (choose your platform and verify its hashes).\n"
            + $"Estate: vericairn estate upgrade --to {release.Version} --check; then apply with --by <operator>, once its published release record is in the estate. Nothing installs or upgrades automatically.";
}
