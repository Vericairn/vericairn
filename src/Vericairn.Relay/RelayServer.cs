// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Vericairn.Protocol;

namespace Vericairn.Relay;

/// <summary>The relay's live registrations and joins. It never decodes the joined streams.</summary>
internal sealed class RelayServer(TimeProvider clock)
{
    private readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);
    private const int AddressLimit = 32;
    private const int TicketLimit = 4;
    private const int WindowSeconds = 10;

    internal async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setup.CancelAfter(SetupTimeout);
        try
        {
            RelayMessage? message = await RelayProtocol.ReceiveAsync(socket, setup.Token).ConfigureAwait(false);
            switch (message?.Kind)
            {
                case "register":
                    await RegisterAsync(socket, message, setup.Token, cancellationToken).ConfigureAwait(false);
                    break;
                case "join":
                    await JoinAsync(socket, message.Ticket, setup.Token, cancellationToken).ConfigureAwait(false);
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

    private async Task RegisterAsync(WebSocket socket, RelayMessage request, CancellationToken setup, CancellationToken cancellationToken)
    {
        if (request.Address is not string address || !RelayProtocol.IsId(address) || request.PublicKey is not string key || !RelayProtocol.IsKey(key))
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
            if (!registrations.TryAdd(address, registration))
            {
                return;
            }
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

    private async Task JoinAsync(WebSocket socket, RelayTicket? ticket, CancellationToken setup, CancellationToken cancellationToken)
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
            if (!registrations.TryGetValue(ticket.Address, out registration) || !registration.CanJoin(ticket, now))
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

    private sealed class Registration(string key)
    {
        private readonly Dictionary<string, (long Counter, long Expires)> revoked = new(StringComparer.Ordinal);
        private readonly Dictionary<long, int> ticketUses = [];
        private long window = -1;
        private int calls;

        internal Channel<RelayMessage> Requests { get; } = Channel.CreateBounded<RelayMessage>(AddressLimit);

        internal Dictionary<string, Join> Joins { get; } = new(StringComparer.Ordinal);

        internal bool CanJoin(RelayTicket ticket, long now)
        {
            long current = now / WindowSeconds;
            if (current != window)
            {
                window = current;
                calls = 0;
                ticketUses.Clear();
                foreach (string device in revoked.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
                {
                    revoked.Remove(device);
                }
            }

            if (calls >= AddressLimit || Joins.Count >= AddressLimit)
            {
                return false;
            }

            calls++;
            if (!RelayProtocol.Verify(ticket, ticket.Address, key, now)
                || revoked.TryGetValue(ticket.Device, out var floor) && ticket.Counter <= floor.Counter)
            {
                return false;
            }

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
