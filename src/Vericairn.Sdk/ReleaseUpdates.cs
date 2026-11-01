// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Updates;

namespace Vericairn.Sdk;

// Internal implementation shared by the two shipped clients; it adds no SDK or estate API contract.
internal sealed class ReleaseUpdates(ReleaseUpdateStore store, IReleaseSource source, TimeProvider clock)
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);
    private bool enabled = true;

    internal ReleaseUpdates(IReleaseSource source) : this(new ReleaseUpdateStore(), source, TimeProvider.System) { }

    // Disclosure uses the setting observed by the check, without doing unbounded I/O on the caller's thread.
    internal bool Enabled => Volatile.Read(ref enabled);

    internal async Task<PublishedRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        PublishedRelease? cached = null;
        long started = Stopwatch.GetTimestamp();
        var cancellation = new CancellationTokenSource();
        CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
            static state => ObserveFailure(((CancellationTokenSource)state!).CancelAsync()), cancellation);
        CancellationToken token = cancellation.Token;
        // Include synchronous state I/O and source invocation in the caller's deadline. A transport timeout
        // alone cannot bound an implementation that blocks before returning a task or ignores cancellation.
        Task<PublishedRelease?> pending = Task.Run(CheckCoreAsync, CancellationToken.None);
        try
        {
            // Independent of cancellation delivery, which can itself be delayed by a source's callbacks.
            return await pending.WaitAsync(CheckTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // This optional check must never change the command's exit code, even for an unexpected source failure.
            return Volatile.Read(ref cached);
        }
        finally
        {
            ObserveFailure(pending);
            // Neither cancelling a source nor disposing a linked registration may wait on its callbacks here.
            _ = CancelAndDisposeAsync(cancellation, registration);
        }

        async Task<PublishedRelease?> CheckCoreAsync()
        {
            Volatile.Write(ref cached, store.CachedRelease());
            ThrowIfExpired();
            Volatile.Write(ref enabled, store.Enabled);
            if (!Enabled) return cached;
            ThrowIfExpired();
            ReleaseCheckState state;
            using (store.Acquire())
            {
                ThrowIfExpired();
                Volatile.Write(ref enabled, store.Enabled);
                if (!Enabled) return cached;
                state = store.ReadState();
                Volatile.Write(ref cached, state.Release);
                DateTimeOffset now = clock.GetUtcNow();
                // A future timestamp (for example after a clock correction) also suppresses another request.
                if (state.LastAttemptAt is DateTimeOffset previous && now - previous < TimeSpan.FromDays(1)) return cached;
                // Persist the attempt before any request, even if the request fails or this client is killed.
                state = state with { LastAttemptAt = now };
                ThrowIfExpired();
                store.WriteState(state);
            }
            // The saved attempt reserves this day's request; an uncooperative source need not hold the lock.
            ThrowIfExpired();
            Task<PublishedRelease?> request = source.LatestAsync(token);
            try
            {
                // Late answers and faults are observed only for cleanup; they cannot replace the cached release.
                PublishedRelease? latest = await request.WaitAsync(CheckTimeout, token).ConfigureAwait(false);
                ThrowIfExpired();
                if (latest is not null)
                {
                    using FileStream held = store.Acquire();
                    ThrowIfExpired();
                    // Another client may have disabled checking or reserved a newer attempt while we waited.
                    Volatile.Write(ref enabled, store.Enabled);
                    if (!Enabled || store.ReadState() != state) return cached;
                    ThrowIfExpired();
                    store.WriteState(state with { LastSuccessAt = clock.GetUtcNow(), Tag = latest.Tag });
                    return latest;
                }
            }
            finally { ObserveFailure(request); }
            return cached;
        }

        void ThrowIfExpired()
        {
            token.ThrowIfCancellationRequested();
            // Check elapsed time too: a completed task must not beat a delayed timer callback.
            if (Stopwatch.GetElapsedTime(started) >= CheckTimeout) throw new TimeoutException();
        }
    }

    private static void ObserveFailure(Task pending)
    {
        _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellation, CancellationTokenRegistration registration)
    {
        try
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception) { /* A source's cancellation callback must also fail soft. */ }
        finally { cancellation.Dispose(); }
    }

    internal static string Disclosure(bool enabled) => enabled
        ? "Release check: on. CLI version, doctor and status, and Workbench, share at most one unauthenticated GitHub release check per 24 hours; no identifiers or usage telemetry are sent. Disable: vericairn updates configure --enabled false."
        : "Release check: off; no request is made. Cached release information may still be shown. Enable: vericairn updates configure --enabled true.";

    internal static string Notice(PublishedRelease? release, string installed, string? repository = PublishedRelease.Repository,
        string line = "current", string channel = "stable", string? pinnedVersion = null) => release is null
        || !string.Equals(repository, PublishedRelease.Repository, StringComparison.OrdinalIgnoreCase)
        || !ReleaseFollowing.Allows(line, channel, "current", "stable", release.Version)
        || !release.IsNewerThan(pinnedVersion ?? installed) ? string.Empty
        : $"Newer Vericairn release: {release.Version} ({(pinnedVersion is null ? string.Empty : $"estate pins {pinnedVersion}; ")}installed {installed.Split('+')[0]}).\n"
            + $"Release notes: {release.NotesUrl}\n"
            + $"NuGet tool: dotnet tool update --global dotnet-vc --version {release.Version} (omit --global for a local tool).\n"
            + $"Archive download (CLI or Workbench): {release.NotesUrl} (choose your platform and verify its hashes).\n"
            + $"Estate: vericairn estate upgrade --to {release.Version} --check; then apply with --by <operator>, once its published release record is in the estate. Nothing installs or upgrades automatically.";

    // A connected client uses the runtime's manifest projection. Missing line/channel fields from an older
    // runtime retain their format defaults; an unavailable pin cannot justify an estate upgrade notice.
    internal static string EstateNotice(PublishedRelease? release, string installed, JsonElement pin)
    {
        if (pin.ValueKind != JsonValueKind.Object) return string.Empty;
        string? Field(string key) => pin.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Notice(release, installed, Field("repository"), Field("line") ?? "current", Field("channel") ?? "stable",
            Field("mode") == "release" ? Field("version") : null);
    }
}
