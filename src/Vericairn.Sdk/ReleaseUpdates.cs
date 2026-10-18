// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Updates;

namespace Vericairn.Sdk;

// Internal implementation shared by the two shipped clients; it adds no SDK or estate API contract.
internal sealed class ReleaseUpdates(ReleaseUpdateStore store, IReleaseSource source, TimeProvider clock)
{
    internal ReleaseUpdates(IReleaseSource source) : this(new ReleaseUpdateStore(), source, TimeProvider.System) { }

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
