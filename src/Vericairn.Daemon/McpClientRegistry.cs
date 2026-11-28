// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>Dynamic registrations are local state; registration alone confers no authority.</summary>
internal sealed class McpClientRegistry(string directory, TimeProvider clock)
{
    private const int PendingLimit = 128;
    private const int PendingPeerLimit = 8;
    private const int RegisteredLimit = 256;
    private readonly object gate = new();
    private readonly string path = Path.Combine(directory, "mcp-registrations.json");
    private readonly Dictionary<string, PendingRegistration> pending = new(StringComparer.Ordinal);
    private Dictionary<string, ApprovedRegistration>? approved;

    internal bool Add(McpClientMetadata client, IPAddress peer)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Prune(now);
            if (pending.Count >= PendingLimit || pending.Count + Load().Count >= RegisteredLimit
                || pending.Values.Count(r => r.Peer.Equals(peer)) >= PendingPeerLimit)
            {
                return false;
            }

            return pending.TryAdd(client.Id, new PendingRegistration(client, peer, now.AddDays(1)));
        }
    }

    internal McpClientMetadata? Find(string id)
    {
        lock (gate)
        {
            Prune(clock.GetUtcNow());
            return Load().GetValueOrDefault(id)?.Client ?? pending.GetValueOrDefault(id)?.Client;
        }
    }

    internal void Use(string id, DateTimeOffset until)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Prune(now);
            Dictionary<string, ApprovedRegistration> all = Load();
            ApprovedRegistration? existing = all.GetValueOrDefault(id);
            McpClientMetadata? client = existing?.Client ?? pending.GetValueOrDefault(id)?.Client;
            if (client is null || until <= now || existing?.Until >= until) return;

            var next = new Dictionary<string, ApprovedRegistration>(all, StringComparer.Ordinal) { [id] = new(client, until) };
            McpTokenStore.WriteState(path, new RegistrationFile(next));
            approved = next;
            pending.Remove(id);
        }
    }

    private Dictionary<string, ApprovedRegistration> Load()
    {
        if (approved is not null) return approved;
        if (!File.Exists(path)) return approved = new Dictionary<string, ApprovedRegistration>(StringComparer.Ordinal);
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("registrations", out JsonElement registrations))
        {
            return approved = registrations.Deserialize<Dictionary<string, ApprovedRegistration>>(ProtocolJson.Options)
                ?? throw new InvalidDataException("The runtime cannot read its MCP client registrations.");
        }

        // Earlier runtimes stored bare metadata, with no expiry. Retain it for at most one maximum authorization lifetime
        // from the last approval in that file so restarting cannot keep old entries alive forever.
        DateTimeOffset legacyUntil = File.GetLastWriteTimeUtc(path).AddDays(90);
        Dictionary<string, McpClientMetadata> legacy = document.RootElement.Deserialize<Dictionary<string, McpClientMetadata>>(ProtocolJson.Options)
            ?? throw new InvalidDataException("The runtime cannot read its MCP client registrations.");
        return approved = legacy.ToDictionary(p => p.Key, p => new ApprovedRegistration(p.Value, legacyUntil), StringComparer.Ordinal);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach ((string id, PendingRegistration registration) in pending)
        {
            if (registration.Until <= now) pending.Remove(id);
        }

        Dictionary<string, ApprovedRegistration> all = Load();
        foreach ((string id, ApprovedRegistration registration) in all)
        {
            if (registration.Until <= now) all.Remove(id);
        }
    }

    private sealed record PendingRegistration(McpClientMetadata Client, IPAddress Peer, DateTimeOffset Until);
    private sealed record ApprovedRegistration(McpClientMetadata Client, DateTimeOffset Until);
    private sealed record RegistrationFile(Dictionary<string, ApprovedRegistration> Registrations);
}
