// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Runtime.Events;

/// <summary>
/// One agent session's presence lease as this runtime keeps it (ID-11; design/events-and-presence.md § 8): owner-only machine state in
/// the live store, never a record. The lease identity itself is never kept, only its SHA-256, so neither the store nor a listing can
/// renew or release a lease; the session's public identity is the first half of that digest.
/// </summary>
internal sealed class PresenceLeaseState
{
    /// <summary>The session's public identity.</summary>
    public required string Session { get; init; }

    /// <summary>The SHA-256 of the lease identity, in lowercase hexadecimal.</summary>
    public required string Hash { get; init; }

    /// <summary>The principal the session acts for, by the id of its first record.</summary>
    public required string Principal { get; init; }

    /// <summary>The device that registered it, whose requests alone renew and release it; null for the local socket.</summary>
    public string? Device { get; init; }

    /// <summary>Its label, without machine paths.</summary>
    public string? Label { get; init; }

    /// <summary>The roles it offers; empty for every role the roster names its principal for.</summary>
    public List<string> Roles { get; init; } = [];

    /// <summary>How many steps routing may give it at once.</summary>
    public int Capacity { get; init; } = 1;

    /// <summary>How long the lease lasts without a heartbeat, in seconds.</summary>
    public int LeaseSeconds { get; init; }

    /// <summary>When it was registered, in Unix seconds.</summary>
    public long RegisteredAt { get; init; }

    /// <summary>Its last heartbeat, or its registration, in Unix seconds.</summary>
    public long RenewedAt { get; set; }

    /// <summary>When it lapses without another heartbeat, in Unix seconds.</summary>
    public long ExpiresAt { get; set; }

    /// <summary>When the runtime observed it lapsed, in Unix seconds: its expiry; null while it is live.</summary>
    public long? LapsedAt { get; set; }

    /// <summary>The assignments it holds, by the id of each one's first record: routing gave them to it, or it recorded attempts or progress on them.</summary>
    public List<string> Holds { get; init; } = [];

    /// <summary>The ask recorded about its lapse for each assignment it holds, by the assignment's first record's id.</summary>
    public Dictionary<string, string> Asks { get; init; } = new(StringComparer.Ordinal);

    /// <summary>When the sweep next tries an ask it could not record, by the assignment's first record's id, in Unix seconds.</summary>
    public Dictionary<string, long> Retry { get; init; } = new(StringComparer.Ordinal);

    /// <summary>How many times the sweep has tried each ask it could not record, by the assignment's first record's id.</summary>
    public Dictionary<string, int> Tries { get; init; } = new(StringComparer.Ordinal);

    /// <summary>True when the lease has not lapsed.</summary>
    [JsonIgnore]
    public bool Live => LapsedAt is null;

    /// <summary>A fresh lease identity: 32 random bytes in URL-safe base64.</summary>
    public static string NewLease() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The digest a lease identity is kept and found by.</summary>
    public static string HashOf(string lease) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lease)));

    /// <summary>True when a lease identity is this lease's, compared in constant time.</summary>
    public bool Matches(string lease) =>
        lease.Length is > 0 and <= 128 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(lease)), Encoding.ASCII.GetBytes(Hash));

    /// <summary>Reads the kept leases; an unreadable value keeps none, so every session registers again.</summary>
    public static List<PresenceLeaseState> Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PresenceLeaseState>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The kept form of the leases.</summary>
    public static string Write(IReadOnlyList<PresenceLeaseState> leases) => JsonSerializer.Serialize(leases, Options);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
