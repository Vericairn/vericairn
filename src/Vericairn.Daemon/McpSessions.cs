// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Mcp;
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

    internal McpSession(string id, AuthenticatedDevice device, string profile, string protocolVersion, DateTimeOffset now, EstateService? service = null)
    {
        Id = id;
        Device = device.Identity;
        PublicKey = device.PublicKey;
        Principal = device.Principal;
        Profile = profile;
        ProtocolVersion = protocolVersion;
        lastUsed = now.UtcTicks;
        if (service is not null)
        {
            Events = new McpSessionEvents(service, device.Identity, device.PublicKey, profile, device.Principal);
            // Notifications are spaced in real time, whatever clock judges the session's idleness.
            Watch = new McpResourceWatch(Events);
        }
    }

    /// <summary>From 0.9.2 (MCP-05): the session's events, read for its device, principal and profile; null without a runtime.</summary>
    internal McpSessionEvents? Events { get; }

    /// <summary>From 0.9.2 (MCP-05): the session's resource subscriptions and the notifications its server stream carries.</summary>
    internal McpResourceWatch? Watch { get; }

    /// <summary>The session id the client repeats in Mcp-Session-Id: 256 random bits, base64url.</summary>
    internal string Id { get; }

    /// <summary>The identity of the device that opened it.</summary>
    internal string Device { get; }

    /// <summary>The key that device was admitted with when it opened the session.</summary>
    internal string PublicKey { get; }

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
    /// when full the oldest gives way, since a notification is a hint and the client reads state afresh. Its stream checks the
    /// device's standing before it sends any of them.
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

    /// <summary>Whether its server stream is open now.</summary>
    internal bool Streaming => Volatile.Read(ref streaming) == 1;

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
internal sealed class McpSessions(TimeProvider time, EstateService? service = null)
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
    private readonly Dictionary<string, DateTimeOffset> deviceSwept = new(StringComparer.Ordinal);

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

    /// <summary>The open session an id names, whichever device holds it: for the runtime's own reports and tests, never a request.</summary>
    internal McpSession? Named(string id)
    {
        lock (gate)
        {
            return sessions.TryGetValue(id, out McpSession? session) && !session.IsEnded ? session : null;
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

            var session = new McpSession(NewId(), device, profile, protocolVersion, now, service);
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
    /// profile the session was opened on, as one reading of the records says now; with <paramref name="device"/>, that device's only.
    /// </summary>
    internal int Sweep(EstateService service, string? device = null)
    {
        List<string> devices;
        lock (gate)
        {
            if (device is null)
            {
                lastSwept = time.GetUtcNow();
            }

            devices = [.. sessions.Values.Where(s => device is null || s.Device == device).Select(s => s.Device).Distinct(StringComparer.Ordinal)];
        }

        if (devices.Count == 0)
        {
            return 0;
        }

        DeviceStandingsRead read;
        try
        {
            read = service.ReadStandings(devices);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return 0;
        }

        return Sweep(read);
    }

    /// <summary>
    /// Ends the sessions of the devices a reading of the records covers that are idle an hour or whose device no longer stands, and
    /// every session idle an hour; sessions of devices the reading does not cover are judged by idleness alone.
    /// </summary>
    internal int Sweep(DeviceStandingsRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            List<McpSession> ended = [.. sessions.Values.Where(session => now - session.LastUsed > Idle
                || read.Devices.TryGetValue(session.Device, out DeviceStanding? standing) && !Stands(standing, session))];
            foreach (McpSession session in ended)
            {
                EndLocked(session);
            }

            return ended.Count;
        }
    }

    /// <summary>
    /// The devices holding sessions, when a sweep of every session is due: more than <see cref="SweepInterval"/> since the last;
    /// otherwise none. Taking them marks the sweep done, so a busy runtime reads the records for it sparingly.
    /// </summary>
    internal IReadOnlyCollection<string> DueDevices()
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            if (now - lastSwept < SweepInterval)
            {
                return [];
            }

            lastSwept = now;
            return [.. sessions.Values.Select(s => s.Device).Distinct(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// Whether a refused request naming a device should sweep its sessions now: it holds some, and none of its sweeps ran in the last
    /// <see cref="SweepInterval"/>, so refused requests never make the runtime read the records more than that for one device.
    /// </summary>
    internal bool TakeDeviceSweep(string device)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            if (!sessions.Values.Any(s => s.Device == device)
                || deviceSwept.TryGetValue(device, out DateTimeOffset last) && now - last < SweepInterval)
            {
                return false;
            }

            deviceSwept[device] = now;
            return true;
        }
    }

    private static bool Stands(DeviceStanding? standing, McpSession session) =>
        standing is not null && standing.Admission == "admitted" && string.Equals(standing.Principal, session.Principal, StringComparison.Ordinal)
        && standing.Profiles.Contains(session.Profile, StringComparer.Ordinal);

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
        if (!sessions.Values.Any(s => s.Device == session.Device))
        {
            deviceSwept.Remove(session.Device);
        }
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
