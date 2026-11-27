// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>Dynamic registrations are local state; registration alone confers no authority.</summary>
internal sealed class McpClientRegistry(string directory, TimeProvider clock)
{
    private const int PendingLimit = 128;
    private const int RegisteredLimit = 256;
    private readonly object gate = new();
    private readonly string path = Path.Combine(directory, "mcp-registrations.json");
    private readonly Dictionary<string, Registration> pending = new(StringComparer.Ordinal);
    private Dictionary<string, McpClientMetadata>? approved;

    internal bool Add(McpClientMetadata client)
    {
        lock (gate)
        {
            Prune();
            if (pending.Count >= PendingLimit || Load().Count >= RegisteredLimit) return false;
            pending.Add(client.Id, new Registration(client, clock.GetUtcNow().AddDays(1)));
            return true;
        }
    }

    internal McpClientMetadata? Find(string id)
    {
        lock (gate)
        {
            Prune();
            return Load().GetValueOrDefault(id) ?? pending.GetValueOrDefault(id)?.Client;
        }
    }

    internal void Use(string id)
    {
        lock (gate)
        {
            if (!pending.TryGetValue(id, out Registration? registration)) return;
            Dictionary<string, McpClientMetadata> all = Load();
            if (all.Count >= RegisteredLimit) return;
            var next = new Dictionary<string, McpClientMetadata>(all, StringComparer.Ordinal) { [id] = registration.Client };
            McpTokenStore.WriteState(path, next);
            approved = next;
            pending.Remove(id);
        }
    }

    private Dictionary<string, McpClientMetadata> Load()
    {
        if (approved is not null) return approved;
        if (!File.Exists(path)) return approved = new Dictionary<string, McpClientMetadata>(StringComparer.Ordinal);
        using FileStream stream = File.OpenRead(path);
        return approved = JsonSerializer.Deserialize<Dictionary<string, McpClientMetadata>>(stream, ProtocolJson.Options)
            ?? throw new InvalidDataException("The runtime cannot read its MCP client registrations.");
    }

    private void Prune()
    {
        DateTimeOffset now = clock.GetUtcNow();
        foreach ((string id, Registration registration) in pending)
        {
            if (registration.ExpiresAt <= now) pending.Remove(id);
        }
    }

    private sealed record Registration(McpClientMetadata Client, DateTimeOffset ExpiresAt);
}

/// <summary>Bounds the public listener's HTTP budget independently of signed listeners.</summary>
internal sealed class McpPublicThrottle(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, (int Mcp, int OAuth)> clients = new(StringComparer.Ordinal);
    private long current;
    private int mcp;
    private int oauth;

    internal bool Admit(string ip, bool isMcp)
    {
        lock (gate)
        {
            long minute = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
            if (minute != current)
            {
                current = minute;
                clients.Clear();
                mcp = 0;
                oauth = 0;
            }

            (int mcpCount, int oauthCount) = clients.GetValueOrDefault(ip);
            if (clients.Count >= 1024 && !clients.ContainsKey(ip)) return false;
            if (isMcp)
            {
                if (mcp >= 2_048 || mcpCount >= 180) return false;
                ++mcp;
                ++mcpCount;
            }
            else
            {
                if (oauth >= 512 || oauthCount >= 60) return false;
                ++oauth;
                ++oauthCount;
            }

            clients[ip] = (mcpCount, oauthCount);
            return true;
        }
    }
}
