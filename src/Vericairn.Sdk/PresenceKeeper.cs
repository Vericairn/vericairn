// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

/// <summary>
/// From 0.9.4 (ID-11; design/events-and-presence.md § 8): a session's transport keeping its presence lease. It registers the session,
/// renews the lease every heartbeat interval (a third of the lease) while its process and connection live, registers again when the
/// runtime no longer keeps the lease, and releases it when disposed. It runs beside the session's model, never through it, so a long
/// model turn keeps the lease and a dead process loses it. A failed renewal is tried again at the next interval; the keeper never
/// stops the session it serves.
/// </summary>
public sealed class PresenceKeeper : IAsyncDisposable, IDisposable
{
    private readonly IEstateApi api;
    private readonly PresenceRegisterRequest request;
    private readonly TimeProvider time;
    private readonly Action<string>? log;
    private readonly CancellationTokenSource stop = new();
    private readonly object sync = new();
    private Task? loop;
    private PresenceLease? lease;
    private int disposed;

    /// <summary>Keeps one session's presence through a runtime's API.</summary>
    /// <param name="api">The runtime: the daemon, over its local socket or a paired connection.</param>
    /// <param name="request">The session's registration.</param>
    /// <param name="time">The clock the heartbeat interval is waited on; the system's when null.</param>
    /// <param name="log">Where the keeper says what it did, such as a refused registration; nowhere when null.</param>
    public PresenceKeeper(IEstateApi api, PresenceRegisterRequest request, TimeProvider? time = null, Action<string>? log = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.time = time ?? TimeProvider.System;
        this.log = log;
    }

    /// <summary>The lease held now; null before registration, after a refused one and after disposal.</summary>
    public PresenceLease? Lease
    {
        get { lock (sync) return lease; }
    }

    /// <summary>Told after each registration and renewal the runtime answered, with its answer; tests wait on it rather than on time.</summary>
    public event Action<PresenceLeaseResponse>? Answered;

    /// <summary>
    /// Registers the session and starts renewing its lease; returns the registration's answer. A refused registration (a person, an
    /// embedded runtime, which keeps no presence) starts nothing.
    /// </summary>
    public async Task<PresenceLeaseResponse> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        PresenceLeaseResponse registered = await api.PresenceRegisterAsync(request, cancellationToken).ConfigureAwait(false);
        Take(registered);
        if (registered.Lease is not null)
        {
            lock (sync)
            {
                loop ??= Task.Run(() => RenewAsync(stop.Token), CancellationToken.None);
            }
        }
        return registered;
    }

    private async Task RenewAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PresenceLease? held = Lease;
            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, held?.HeartbeatSeconds ?? PresenceVocabulary.DefaultLeaseSeconds / 3));
            try
            {
                await Task.Delay(interval, time, cancellationToken).ConfigureAwait(false);
                PresenceLeaseResponse answer = held is null
                    ? await api.PresenceRegisterAsync(request, cancellationToken).ConfigureAwait(false)
                    : await api.PresenceHeartbeatAsync(held.Lease, cancellationToken).ConfigureAwait(false);
                if (answer.Problem is { Code: "not-found" })
                {
                    // The runtime no longer keeps the lease (it lapsed and was dropped, or the runtime restarted): register again.
                    answer = await api.PresenceRegisterAsync(request, cancellationToken).ConfigureAwait(false);
                }

                Take(answer);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                // A runtime briefly unreachable: the lease is renewed at the next interval, or lapses if it stays unreachable.
                log?.Invoke($"presence could not be renewed: {exception.Message}");
            }
        }
    }

    private void Take(PresenceLeaseResponse answer)
    {
        if (answer.Lease is not null)
        {
            lock (sync)
            {
                lease = answer.Lease;
            }
        }

        Answered?.Invoke(answer);
    }

    /// <summary>Stops renewing and releases the lease, so its session's end is no lapse.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        Task? running;
        lock (sync)
        {
            running = loop;
        }

        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }

        PresenceLease? held;
        lock (sync)
        {
            held = lease;
            lease = null;
        }

        if (held is not null)
        {
            try
            {
                using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await api.PresenceReleaseAsync(held.Lease, bounded.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                // A release that cannot be sent leaves the lease to lapse, which is what a dead process does.
                log?.Invoke($"presence could not be released: {exception.Message}");
            }
        }

        stop.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
