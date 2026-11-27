// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Vericairn.Protocol;

namespace Vericairn.Relay;

/// <summary>
/// How many runtimes one relay holds registered, in total and per client. Each registration keeps up to 16,384 revocations,
/// about 2 MB at most, so the total bounds the relay's memory. A host serving many estates from one address raises the
/// per-client limit.
/// </summary>
internal sealed record RelayLimits(int Registrations = 256, int RegistrationsPerClient = 32);

/// <summary>The relay's live registrations and joins. It never decodes the joined streams.</summary>
internal sealed class RelayServer(TimeProvider clock, RelayLimits? limits = null)
{
    private readonly RelayLimits limits = (limits ?? new RelayLimits()) is { Registrations: > 0, RegistrationsPerClient: > 0 } valid
        ? valid : throw new ArgumentOutOfRangeException(nameof(limits), "Relay registration limits must be positive.");
    private readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);
    private const int AddressLimit = 32;
    private const int TicketLimit = 4;
    private const int UnverifiedLimit = 256;
    private const int UnverifiedPerClient = 32;
    private const int WindowSeconds = 10;
    private const int SocketLimit = 4096;
    private const int PeerSocketLimit = 256;
    private readonly Dictionary<string, int> peerSockets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> peerRegistrations = new(StringComparer.Ordinal);
    private int sockets;

    /// <summary>
    /// The client a connection's limits are counted against: an IPv4 address, or an IPv6 address's /64, since one IPv6 client
    /// commonly holds a whole /64. Clients behind one NAT address share its budget.
    /// </summary>
    internal static string PeerOf(System.Net.IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// Serves one upgraded WebSocket from the client at <paramref name="peer"/>. Upgraded sockets fall outside Kestrel's connection
    /// limit, so they are bounded here, in total and per client address, as registrations are.
    /// </summary>
    internal async Task HandleAsync(WebSocket socket, string peer, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (sockets >= SocketLimit || peerSockets.GetValueOrDefault(peer) >= PeerSocketLimit) return;
            sockets++;
            peerSockets[peer] = peerSockets.GetValueOrDefault(peer) + 1;
        }

        try
        {
            await ServeAsync(socket, peer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                sockets--;
                if (--peerSockets[peer] == 0) peerSockets.Remove(peer);
            }
        }
    }

    private async Task ServeAsync(WebSocket socket, string peer, CancellationToken cancellationToken)
    {
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setup.CancelAfter(SetupTimeout);
        try
        {
            RelayMessage? message = await RelayProtocol.ReceiveAsync(socket, setup.Token).ConfigureAwait(false);
            switch (message?.Kind)
            {
                case "register":
                    await RegisterAsync(socket, message, peer, setup.Token, cancellationToken).ConfigureAwait(false);
                    break;
                case "join":
                    await JoinAsync(socket, message.Ticket, peer, setup.Token, cancellationToken).ConfigureAwait(false);
                    break;
                case "accept":
                    await AcceptAsync(socket, message.Address, message.Nonce, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or WebSocketException or JsonException)
        {
            // An interrupted or invalid peer affects only its own socket.
        }
    }

    private async Task RegisterAsync(WebSocket socket, RelayMessage request, string peer, CancellationToken setup, CancellationToken cancellationToken)
    {
        // An address is the hash of its registration key, so only the holder of that key can take it: nobody squats another's.
        if (request.Address is not string address || !RelayProtocol.IsId(address) || request.PublicKey is not string key || !RelayProtocol.IsKey(key)
            || address != RelayProtocol.AddressForKey(key))
        {
            return;
        }

        string nonce = RelayProtocol.RandomId();
        await RelayProtocol.SendAsync(socket, new("challenge", Nonce: nonce), setup).ConfigureAwait(false);
        if (await RelayProtocol.ReceiveAsync(socket, setup).ConfigureAwait(false) is not { Kind: "prove" } proof
            || !RelayProtocol.VerifyProof(address, nonce, key, proof.Signature))
        {
            return;
        }

        var registration = new Registration(key);
        // Load all still-relevant revocations before this address can be joined. A disconnected relay holds no registration.
        while (true)
        {
            RelayMessage? initial = await RelayProtocol.ReceiveAsync(socket, setup).ConfigureAwait(false);
            if (initial is null)
            {
                return;
            }

            if (initial.Kind == "ready")
            {
                break;
            }

            if (!registration.Revoke(initial, Now()))
            {
                return;
            }
        }

        setup.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (registrations.Count >= limits.Registrations || peerRegistrations.GetValueOrDefault(peer) >= limits.RegistrationsPerClient
                || !registrations.TryAdd(address, registration))
            {
                return;
            }

            peerRegistrations[peer] = peerRegistrations.GetValueOrDefault(peer) + 1;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sender = Task.CompletedTask;
        try
        {
            await RelayProtocol.SendAsync(socket, new("registered"), setup).ConfigureAwait(false);
            sender = SendRequestsAsync(socket, registration, stop.Token);
            Task receiver = ReceiveRevocationsAsync(socket, registration, stop.Token);
            await Task.WhenAny(sender, receiver).ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(sender, receiver).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                registrations.Remove(address);
                if (--peerRegistrations[peer] == 0) peerRegistrations.Remove(peer);
                foreach (Join pending in registration.Joins.Values)
                {
                    pending.Stop.Cancel();
                }
            }
        }
    }

    private static async Task SendRequestsAsync(WebSocket socket, Registration registration, CancellationToken cancellationToken)
    {
        await foreach (RelayMessage request in registration.Requests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await RelayProtocol.SendAsync(socket, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveRevocationsAsync(WebSocket socket, Registration registration, CancellationToken cancellationToken)
    {
        while (await RelayProtocol.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false) is { } message)
        {
            lock (gate)
            {
                if (!registration.Revoke(message, Now()))
                {
                    return;
                }
            }
        }
    }

    private async Task JoinAsync(WebSocket socket, RelayTicket? ticket, string peer, CancellationToken setup, CancellationToken cancellationToken)
    {
        if (ticket is null || !RelayProtocol.IsId(ticket.Address))
        {
            return;
        }

        long now = Now();
        Registration? registration;
        Join pending;
        string nonce;
        lock (gate)
        {
            if (!registrations.TryGetValue(ticket.Address, out registration) || registration.Saturated(peer, now) || !registration.CanJoin(ticket, peer, now))
            {
                return;
            }

            nonce = RelayProtocol.RandomId();
            pending = new Join(ticket);
            registration.Joins.Add(nonce, pending);
            if (!registration.Requests.Writer.TryWrite(new("stream", Nonce: nonce, Ticket: ticket)))
            {
                registration.Joins.Remove(nonce);
                pending.Dispose();
                return;
            }
        }

        using (pending)
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(pending.Stop.Token, cancellationToken))
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(setup, stop.Token))
        {
            try
            {
                WebSocket accepted = await pending.Accepted.Task.WaitAsync(waiting.Token).ConfigureAwait(false);
                await RelayProtocol.SendAsync(socket, new("joined"), waiting.Token).ConfigureAwait(false);

                // The relay does not instantiate a TLS server for this stream, inspect records or know either endpoint's key.
                using var device = new RelayStream(socket);
                using var runtime = new RelayStream(accepted);
                Task inbound = device.CopyToAsync(runtime, 16 * 1024, stop.Token);
                Task outbound = runtime.CopyToAsync(device, 16 * 1024, stop.Token);
                await Task.WhenAny(inbound, outbound).ConfigureAwait(false);
                await stop.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(inbound, outbound).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    registration.Joins.Remove(nonce);
                }

                pending.Ended.TrySetResult();
            }
        }
    }

    private async Task AcceptAsync(WebSocket socket, string? address, string? nonce, CancellationToken cancellationToken)
    {
        if (!RelayProtocol.IsId(address) || !RelayProtocol.IsId(nonce))
        {
            return;
        }

        Join? pending;
        lock (gate)
        {
            if (!registrations.TryGetValue(address!, out Registration? registration)
                || !registration.Joins.TryGetValue(nonce!, out pending) || !pending.Accepted.TrySetResult(socket))
            {
                return;
            }
        }

        await pending.Ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();

    /// <summary>This window's joins to <paramref name="address"/> whose ticket did not verify, counted so far.</summary>
    internal int Unverified(string address)
    {
        lock (gate)
        {
            return registrations.TryGetValue(address, out Registration? registration) ? registration.UnverifiedIn(Now()) : 0;
        }
    }

    private sealed class Registration(string key)
    {
        private readonly Dictionary<string, (long Counter, long Expires)> revoked = new(StringComparer.Ordinal);
        private readonly Dictionary<long, int> ticketUses = [];
        private long window = -1;
        private int calls;
        private int unverified;
        private readonly Dictionary<string, int> unverifiedFrom = new(StringComparer.Ordinal);


        internal Channel<RelayMessage> Requests { get; } = Channel.CreateBounded<RelayMessage>(AddressLimit);

        internal Dictionary<string, Join> Joins { get; } = new(StringComparer.Ordinal);

        // Starts a new window when the clock has moved past the current one, resetting every per-window budget.
        private void Roll(long now)
        {
            long current = now / WindowSeconds;
            if (current == window) return;
            window = current;
            calls = 0;
            unverified = 0;
            unverifiedFrom.Clear();
            ticketUses.Clear();
            foreach (string device in revoked.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
            {
                revoked.Remove(device);
            }
        }

        internal int UnverifiedIn(long now)
        {
            Roll(now);
            return unverified;
        }

        // Whether this window's joins without a valid ticket have reached their limit, from this client or in total; past it
        // joins are refused before verifying, at no signature's cost, until the window ends. One client's joins use only its own
        // share, so closing an address for a window takes at least UnverifiedLimit / UnverifiedPerClient clients.
        internal bool Saturated(string peer, long now)
        {
            Roll(now);
            return unverified >= UnverifiedLimit || unverifiedFrom.GetValueOrDefault(peer) >= UnverifiedPerClient;
        }

        internal bool CanJoin(RelayTicket ticket, string peer, long now)
        {
            Roll(now);

            // A join that presents no valid ticket draws on a budget of its own, so joins without the key cannot use up the budget
            // a valid ticket draws on.
            if (!RelayProtocol.Verify(ticket, ticket.Address, key, now)
                || revoked.TryGetValue(ticket.Device, out var floor) && ticket.Counter <= floor.Counter)
            {
                unverified++;
                unverifiedFrom[peer] = unverifiedFrom.GetValueOrDefault(peer) + 1;
                return false;
            }

            if (calls >= AddressLimit || Joins.Count >= AddressLimit)
            {
                return false;
            }

            calls++;

            int uses = ticketUses.GetValueOrDefault(ticket.Counter);
            if (uses >= TicketLimit || Joins.Values.Count(item => item.Ticket.Counter == ticket.Counter) >= TicketLimit)
            {
                return false;
            }

            ticketUses[ticket.Counter] = uses + 1;
            return true;
        }

        internal bool Revoke(RelayMessage message, long now)
        {
            if (message.Kind != "revoke" || !RelayProtocol.IsDevice(message.Device) || message.Counter <= 0)
            {
                return false;
            }

            foreach (string device in revoked.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
            {
                revoked.Remove(device);
            }

            if (!revoked.ContainsKey(message.Device!) && revoked.Count >= 16_384)
            {
                // The registration closes instead of losing old revocations while it is still accepting joins.
                return false;
            }

            revoked[message.Device!] = (Math.Max(message.Counter, revoked.GetValueOrDefault(message.Device!).Counter), now + RelayProtocol.Lifetime);
            foreach (Join pending in Joins.Values)
            {
                if (pending.Ticket.Device == message.Device && pending.Ticket.Counter <= message.Counter)
                {
                    pending.Stop.Cancel();
                }
            }

            return true;
        }
    }

    private sealed class Join(RelayTicket ticket) : IDisposable
    {
        internal RelayTicket Ticket { get; } = ticket;
        internal CancellationTokenSource Stop { get; } = new();
        internal TaskCompletionSource<WebSocket> Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Stop.Dispose();
    }
}
