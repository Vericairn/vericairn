// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;

namespace Vericairn.Daemon;

/// <summary>Counts the socket peer. An IPv6 subnet shares one budget so rotating its host addresses cannot bypass it.</summary>
internal static class McpPublicPeer
{
    internal static IPAddress Of(IPAddress? address)
    {
        if (address is null) return IPAddress.IPv6None;
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork) return address;

        byte[] bytes = address.GetAddressBytes();
        bytes.AsSpan(8).Clear();
        return new IPAddress(bytes, address.ScopeId);
    }
}

/// <summary>Bounds the public listener's share of connections independently of the other listeners.</summary>
internal sealed class McpPublicConnections
{
    internal const int TotalLimit = 12;
    internal const int PeerLimit = 4;
    internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<IPAddress, int> peers = [];
    private int total;

    internal bool TryEnter(IPAddress peer)
    {
        lock (peers)
        {
            int count = peers.GetValueOrDefault(peer);
            if (total >= TotalLimit || count >= PeerLimit) return false;

            peers[peer] = count + 1;
            ++total;
            return true;
        }
    }

    internal void Leave(IPAddress peer)
    {
        lock (peers)
        {
            int remaining = peers[peer] - 1;
            if (remaining == 0) peers.Remove(peer);
            else peers[peer] = remaining;
            --total;
        }
    }
}

/// <summary>Counts requests in separate budgets so traffic on one set of paths cannot consume another's allowance.</summary>
internal sealed class McpPublicThrottle(TimeProvider clock, int totalLimit, int peerLimit)
{
    private readonly Dictionary<IPAddress, int> peers = [];
    private long current;
    private int total;

    internal bool Admit(IPAddress peer)
    {
        lock (peers)
        {
            long minute = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
            if (minute != current)
            {
                current = minute;
                peers.Clear();
                total = 0;
            }

            // Only admitted requests create entries, so the total request budget also bounds the peer table.
            int count = peers.GetValueOrDefault(peer);
            if (total >= totalLimit || count >= peerLimit) return false;

            peers[peer] = count + 1;
            ++total;
            return true;
        }
    }
}
