// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;

namespace Vericairn.Daemon;

/// <summary>The machine-local token families. The file contains only credential hashes; it is replaced before a rotation is returned.</summary>
internal sealed class McpTokenStore(string directory, TimeProvider clock)
{
    internal const string FileName = "mcp-token-families.json";
    internal static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private const int FamilyLimit = 256;
    private const int RotationLimit = 16_384;
    private const string Format = "vericairn.mcp-token-families/1";
    private static readonly string AccessPrefix = string.Concat("vc", "at", "_");
    private static readonly string RefreshPrefix = string.Concat("vc", "rt", "_");
    private readonly object gate = new();
    private readonly string path = Path.Combine(directory, FileName);
    private Dictionary<string, McpTokenFamily>? families;

    internal McpTokenResponse? Create(McpTokenGrant grant)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Dictionary<string, McpTokenFamily> all = Load();
            Dictionary<string, McpTokenFamily> next = all.Where(p => p.Value.Grant.ExpiresAt > now && p.Value.Grant.Device != grant.Device)
                .ToDictionary(StringComparer.Ordinal);
            if (next.Count >= FamilyLimit || grant.ExpiresAt <= now)
            {
                return null;
            }

            string id = Ulid.NewUlid(now).Value;
            McpTokenResponse response = Issue(grant, now);
            next.Add(id, new McpTokenFamily(grant, Hash(response.RefreshToken), [], [Access(response, now)]));
            Save(next);
            return response;
        }
    }

    internal McpTokenAccess? Authenticate(string secret, string resource)
    {
        if (!HasShape(secret, AccessPrefix))
        {
            return null;
        }

        string hash = Hash(secret);
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            foreach ((string id, McpTokenFamily family) in Load())
            {
                if (family.Grant.Resource == resource && family.Grant.ExpiresAt > now
                    && family.Access.FirstOrDefault(a => a.ExpiresAt > now && a.Hash == hash) is { } access)
                {
                    return new McpTokenAccess(id, family.Grant with { Scope = access.Scope }, access.ExpiresAt);
                }
            }

            return null;
        }
    }

    internal bool Current(McpTokenAccess access)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            return access.ExpiresAt > now && Load().TryGetValue(access.Family, out McpTokenFamily? family) && family.Grant.ExpiresAt > now;
        }
    }

    internal McpTokenRefresh? RefreshGrant(string secret, string client, string resource)
    {
        if (!HasShape(secret, RefreshPrefix))
        {
            return null;
        }

        string hash = Hash(secret);
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            foreach ((string id, McpTokenFamily family) in Load())
            {
                if (family.Grant.ExpiresAt > now && family.Grant.Client == client && family.Grant.Resource == resource
                    && (family.Refresh == hash || family.Spent.Contains(hash, StringComparer.Ordinal)))
                {
                    return new McpTokenRefresh(id, hash, family.Grant);
                }
            }

            return null;
        }
    }

    internal (McpTokenResponse? Response, bool Reused) Rotate(McpTokenRefresh refresh, string? scope = null)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Dictionary<string, McpTokenFamily> all = Load();
            if (!all.TryGetValue(refresh.Family, out McpTokenFamily? family) || family.Grant.ExpiresAt <= now)
            {
                return (null, false);
            }

            var next = new Dictionary<string, McpTokenFamily>(all, StringComparer.Ordinal);
            if (family.Spent.Contains(refresh.Hash, StringComparer.Ordinal))
            {
                next.Remove(refresh.Family);
                Save(next);
                return (null, true);
            }

            if (family.Refresh != refresh.Hash || family.Spent.Length >= RotationLimit)
            {
                return (null, false);
            }

            McpTokenResponse response = Issue(family.Grant with { Scope = scope ?? family.Grant.Scope }, now);
            next[refresh.Family] = family with
            {
                Refresh = Hash(response.RefreshToken),
                Spent = [.. family.Spent, refresh.Hash],
                // Keep access already in use briefly when the client refreshes. Rapid rotations keep the newest 64 at most.
                Access = [.. family.Access.Where(a => a.ExpiresAt > now).TakeLast(63), Access(response, now)],
            };
            Save(next);
            return (response, false);
        }
    }

    internal void EndFamily(string id)
    {
        lock (gate)
        {
            Dictionary<string, McpTokenFamily> all = Load();
            if (!all.ContainsKey(id)) return;
            var next = new Dictionary<string, McpTokenFamily>(all, StringComparer.Ordinal);
            next.Remove(id);
            Save(next);
        }
    }

    // The local command reads an atomic snapshot and projects only public client facts.
    internal IReadOnlyDictionary<string, McpTokenGrant> ClientGrants()
    {
        lock (gate)
        {
            return Load().Values.ToDictionary(family => family.Grant.Device, family => family.Grant, StringComparer.Ordinal);
        }
    }

    private Dictionary<string, McpTokenFamily> Load()
    {
        if (families is not null) return families;
        if (!File.Exists(path)) return families = new Dictionary<string, McpTokenFamily>(StringComparer.Ordinal);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        McpTokenState? state = JsonSerializer.Deserialize<McpTokenState>(file, ProtocolJson.Options);
        if (state?.Format != Format || state.Families is null)
        {
            throw new InvalidDataException("The runtime cannot read its MCP token family state.");
        }

        return families = new Dictionary<string, McpTokenFamily>(state.Families, StringComparer.Ordinal);
    }

    private void Save(Dictionary<string, McpTokenFamily> next)
    {
        WriteState(path, new McpTokenState(Format, next));
        families = next;
    }

    internal static void WriteState<T>(string path, T state)
    {
        string temporary = path + "." + Ulid.NewUlid().Value + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(file, state, ProtocolJson.Options);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static McpTokenResponse Issue(McpTokenGrant grant, DateTimeOffset now)
    {
        int seconds = (int)Math.Min(AccessLifetime.TotalSeconds, Math.Floor((grant.ExpiresAt - now).TotalSeconds));
        return new McpTokenResponse(AccessPrefix + RandomValue(32), RefreshPrefix + RandomValue(32), seconds, grant.Scope);
    }

    private static McpAccessDigest Access(McpTokenResponse response, DateTimeOffset now) => new(Hash(response.AccessToken), now.AddSeconds(response.ExpiresIn), response.Scope);

    internal static string RandomValue(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    internal static string Hash(string value) => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static bool IsUrlValue(string value) => value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool HasShape(string value, string prefix) =>
        value.Length == prefix.Length + 43 && value.StartsWith(prefix, StringComparison.Ordinal) && IsUrlValue(value);
}

internal sealed record McpTokenGrant(string Device, string Principal, string PublicKey, string Admission, string By, string Client, string Resource, string Scope, DateTimeOffset ExpiresAt)
{
    public string? ClientName { get; init; }
}

internal sealed record McpTokenAccess(string Family, McpTokenGrant Grant, DateTimeOffset ExpiresAt);
internal sealed record McpTokenRefresh(string Family, string Hash, McpTokenGrant Grant);
internal sealed record McpAccessDigest(string Hash, DateTimeOffset ExpiresAt, string Scope);
internal sealed record McpTokenFamily(McpTokenGrant Grant, string Refresh, string[] Spent, McpAccessDigest[] Access);
internal sealed record McpTokenState(string Format, Dictionary<string, McpTokenFamily> Families);
internal sealed record McpTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string Scope);
