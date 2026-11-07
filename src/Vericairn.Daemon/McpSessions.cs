// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// One network MCP session (MCP-03): opened by one device's initialize, kept in memory, bound to that device, its principal and the
/// profile it arrived on, and ended by DELETE, an idle hour, its device's revocation or loss of the profile, or the daemon's restart.
/// A session is never authentication: every request on it is device-signed and authenticated afresh, and a request from any other
/// device, principal or profile finds no session.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The source is cancelled when the session ends and never disposed: streams and calls link its token and may outlive the session's removal, and it creates no wait handle.")]
internal sealed class McpSession
{
    private readonly CancellationTokenSource ended = new();
    private long lastUsed;
    private int streaming;

    internal McpSession(string id, AuthenticatedDevice device, string profile, string protocolVersion, DateTimeOffset now)
    {
        Id = id;
        Device = device.Identity;
        Principal = device.Principal;
        Profile = profile;
        ProtocolVersion = protocolVersion;
        lastUsed = now.UtcTicks;
    }

    /// <summary>The session id the client repeats in Mcp-Session-Id: 256 random bits, base64url.</summary>
    internal string Id { get; }

    /// <summary>The identity of the device that opened it.</summary>
    internal string Device { get; }

    /// <summary>That device's principal.</summary>
    internal string Principal { get; }

    /// <summary>The profile it was opened on: loopback or local-network.</summary>
    internal string Profile { get; }

    /// <summary>The protocol revision initialize negotiated, which later requests' MCP-Protocol-Version must name.</summary>
    internal string ProtocolVersion { get; }

    /// <summary>When it was last used.</summary>
    internal DateTimeOffset LastUsed => new(Interlocked.Read(ref lastUsed), TimeSpan.Zero);

    /// <summary>Cancelled when the session ends: its server stream closes and its calls in flight stop being observed.</summary>
    internal CancellationToken Ended => ended.Token;

    /// <summary>Whether it has ended.</summary>
    internal bool IsEnded => ended.IsCancellationRequested;

    /// <summary>The requests in flight on it, by JSON-RPC id, for the client's cancellation notifications.</summary>
    internal ConcurrentDictionary<string, CancellationTokenSource> Active { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The messages waiting for the session's server stream: the notifications MCP-05 sends, for this session's device alone. Bounded;
    /// when full the oldest gives way, since a notification is a hint and the client reads state afresh.
    /// </summary>
    internal Channel<JsonObject> Outbox { get; } = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    /// <summary>Whether a request's authenticated device, principal and profile are this session's own.</summary>
    internal bool BelongsTo(AuthenticatedDevice device, string profile) =>
        string.Equals(device.Identity, Device, StringComparison.Ordinal)
        && string.Equals(device.Principal, Principal, StringComparison.Ordinal)
        && string.Equals(profile, Profile, StringComparison.Ordinal);

    internal void Touch(DateTimeOffset now) => Interlocked.Exchange(ref lastUsed, now.UtcTicks);

    /// <summary>Takes the session's one server stream; false when it already has one open.</summary>
    internal bool TryOpenStream() => Interlocked.CompareExchange(ref streaming, 1, 0) == 0;

    internal void CloseStream() => Interlocked.Exchange(ref streaming, 0);

    internal void End()
    {
        if (ended.IsCancellationRequested)
        {
            return;
        }

        try
        {
            ended.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already ended.
        }

        Outbox.Writer.TryComplete();
        foreach (CancellationTokenSource call in Active.Values)
        {
            try
            {
                call.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The call finished as the session ended.
            }
        }
    }
}

/// <summary>
/// The network MCP sessions a daemon holds in memory, and how many of their requests run at once (MCP-03). A device holds at most
/// <see cref="PerDevice"/> sessions, its least recently used giving way to a new one, and the runtime <see cref="PerRuntime"/>; a
/// device runs at most <see cref="CallsPerDevice"/> requests at once and all devices <see cref="CallsPerRuntime"/>, so MCP never takes
/// the connections the API and the event streams need. Server streams take the event streams' slots.
/// </summary>
internal sealed class McpSessions(TimeProvider time)
{
    /// <summary>The most sessions one device holds; a new one ends its least recently used.</summary>
    internal const int PerDevice = 4;

    /// <summary>The most sessions the runtime holds; beyond it a new session is refused until one ends.</summary>
    internal const int PerRuntime = 64;

    /// <summary>The most requests one device runs on /mcp at once.</summary>
    internal const int CallsPerDevice = 4;

    /// <summary>The most requests all devices run on /mcp at once: a quarter of the daemon's connections.</summary>
    internal const int CallsPerRuntime = DaemonHost.ConcurrentConnections / 4;

    /// <summary>How long a session lasts unused.</summary>
    internal static readonly TimeSpan Idle = TimeSpan.FromHours(1);

    private readonly object gate = new();
    private readonly Dictionary<string, McpSession> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> calls = new(StringComparer.Ordinal);
    private int running;
    private DateTimeOffset lastSwept = DateTimeOffset.MinValue;

    /// <summary>How often a request to /mcp sweeps every session; each request's own device is judged on every request regardless.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>How many sessions are open now.</summary>
    internal int Count
    {
        get { lock (gate) return sessions.Count; }
    }

    /// <summary>The sessions a device holds now.</summary>
    internal int CountFor(string device)
    {
        lock (gate)
        {
            return sessions.Values.Count(s => s.Device == device);
        }
    }

    /// <summary>Opens a session for a device, or null when the runtime holds as many as it keeps.</summary>
    internal McpSession? Open(AuthenticatedDevice device, string profile, string protocolVersion)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            List<McpSession> own = [.. sessions.Values.Where(s => s.Device == device.Identity).OrderBy(s => s.LastUsed)];
            while (own.Count >= PerDevice)
            {
                EndLocked(own[0]);
                own.RemoveAt(0);
            }

            if (sessions.Count >= PerRuntime)
            {
                return null;
            }

            var session = new McpSession(NewId(), device, profile, protocolVersion, now);
            sessions[session.Id] = session;
            return session;
        }
    }

    /// <summary>
    /// The session an id names for this device, principal and profile, or null: an unknown, ended or idle id and another device's are
    /// answered alike, so nothing tells a client whose session it named.
    /// </summary>
    internal McpSession? Find(string id, AuthenticatedDevice device, string profile)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out McpSession? session) || session.IsEnded)
            {
                return null;
            }

            if (now - session.LastUsed > Idle)
            {
                EndLocked(session);
                return null;
            }

            if (!session.BelongsTo(device, profile))
            {
                return null;
            }

            session.Touch(now);
            return session;
        }
    }

    /// <summary>Ends a session: DELETE, or its device no longer standing.</summary>
    internal void End(McpSession session)
    {
        lock (gate)
        {
            EndLocked(session);
        }
    }

    /// <summary>
    /// Ends every session that has been idle an hour, and every session whose device is no longer admitted with its key and the
    /// profile the session was opened on, as the records say now.
    /// </summary>
    internal int Sweep(EstateService service, string? device = null)
    {
        DateTimeOffset now = time.GetUtcNow();
        List<McpSession> open;
        lock (gate)
        {
            if (device is null)
            {
                lastSwept = now;
            }

            open = [.. sessions.Values.Where(s => device is null || s.Device == device)];
        }

        var ended = new List<McpSession>();
        foreach (IGrouping<string, McpSession> held in open.GroupBy(s => s.Device, StringComparer.Ordinal))
        {
            DeviceStanding? standing;
            try
            {
                standing = service.Standing(held.Key);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
            {
                continue;
            }

            foreach (McpSession session in held)
            {
                if (now - session.LastUsed > Idle || standing is null || standing.Admission != "admitted"
                    || !string.Equals(standing.Principal, session.Principal, StringComparison.Ordinal)
                    || !standing.Profiles.Contains(session.Profile, StringComparer.Ordinal))
                {
                    ended.Add(session);
                }
            }
        }

        lock (gate)
        {
            foreach (McpSession session in ended)
            {
                EndLocked(session);
            }
        }

        return ended.Count;
    }

    /// <summary>Sweeps when the last sweep of every session is more than a few seconds old, so a busy runtime reads standings sparingly.</summary>
    internal int SweepIfDue(EstateService service)
    {
        lock (gate)
        {
            if (time.GetUtcNow() - lastSwept < SweepInterval)
            {
                return 0;
            }
        }

        return Sweep(service);
    }

    /// <summary>Ends every session, as the daemon stops.</summary>
    internal void EndAll()
    {
        lock (gate)
        {
            foreach (McpSession session in sessions.Values.ToArray())
            {
                EndLocked(session);
            }
        }
    }

    /// <summary>A slot for one request of a device, or null when that device or the runtime runs as many as it may.</summary>
    internal IDisposable? TryCall(string device)
    {
        lock (gate)
        {
            if (calls.GetValueOrDefault(device) >= CallsPerDevice || running >= CallsPerRuntime)
            {
                return null;
            }

            calls[device] = calls.GetValueOrDefault(device) + 1;
            running++;
            return new Call(this, device);
        }
    }

    private void Release(string device)
    {
        lock (gate)
        {
            running--;
            if (--calls[device] == 0)
            {
                calls.Remove(device);
            }
        }
    }

    private void EndLocked(McpSession session)
    {
        sessions.Remove(session.Id);
        session.End();
    }

    private static string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private sealed class Call(McpSessions sessions, string device) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                sessions.Release(device);
            }
        }
    }
}
