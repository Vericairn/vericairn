// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Security.Cryptography;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>One-time local approvals. A random selector isolates the five guesses for one 80-bit secret from the other approvals.</summary>
internal sealed class McpApprovalCodes(TimeProvider clock)
{
    private static readonly SearchValues<char> Hex = SearchValues.Create("0123456789abcdefABCDEF");
    private readonly object gate = new();
    private readonly Dictionary<string, Pending> codes = new(StringComparer.Ordinal);

    internal McpApprovalIssued? Issue(McpApproval approval, string endpoint)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Prune(now);
            if (codes.Count >= PairingRegistry.MaxPending) return null;

            string id = McpTokenStore.RandomValue(12);
            string secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(10));
            DateTimeOffset until = now + PairingRegistry.Lifetime;
            codes.Add(id, new Pending(Convert.FromHexString(secret), approval, until));
            return new McpApprovalIssued(id + "." + secret, endpoint, approval.Principal, approval.Scope,
                RecordParser.FormatTimestamp(until), PairingRegistry.Attempts);
        }
    }

    internal McpApproval? Prove(string text)
    {
        if (text.Length < 17 || text[16] != '.') return null;

        lock (gate)
        {
            Prune(clock.GetUtcNow());
            string id = text[..16];
            if (!codes.TryGetValue(id, out Pending? pending)) return null;
            if (text.Length == 37 && text.AsSpan(17).IndexOfAnyExcept(Hex) < 0
                && CryptographicOperations.FixedTimeEquals(pending.Secret, Convert.FromHexString(text.AsSpan(17))))
            {
                codes.Remove(id);
                return pending.Approval;
            }

            if (--pending.Attempts == 0) codes.Remove(id);
            return null;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach ((string id, Pending pending) in codes)
        {
            if (pending.Until <= now) codes.Remove(id);
        }
    }

    private sealed record Pending(byte[] Secret, McpApproval Approval, DateTimeOffset Until)
    {
        internal int Attempts { get; set; } = PairingRegistry.Attempts;
    }
}

internal sealed record McpApproval(string Principal, PairingAuthority Authority, string Scope, DateTimeOffset ExpiresAt, McpTokenDevice? Renew);
