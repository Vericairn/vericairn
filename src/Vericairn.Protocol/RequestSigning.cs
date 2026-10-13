// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Protocol;

/// <summary>Signs request digests with a device key; the SDK supplies one for profiles that authenticate requests.</summary>
public interface IRequestSigner
{
    /// <summary>The device record id whose key signs.</summary>
    string DeviceId { get; }

    /// <summary>Signs a 32-byte digest and returns the 64-byte IEEE P1363 ECDSA P-256 signature.</summary>
    byte[] Sign(ReadOnlySpan<byte> digest);
}

/// <summary>
/// Device-signed requests, version 1: the profiles that cannot rely on operating-system identity (loopback now, the local network
/// later) carry three headers naming the device, the signing time and a signature over the method, the request target, the time
/// and the body hash. The daemon accepts a signature only from a device admitted now, within five minutes of its time, and once.
/// </summary>
public static class RequestSigning
{
    /// <summary>The scheme name bound into every digest.</summary>
    public const string Scheme = "vericairn-request/1";

    /// <summary>Header carrying the device record id.</summary>
    public const string DeviceHeader = "Vericairn-Device";

    /// <summary>Header carrying the signing time (RFC 3339, UTC, seconds).</summary>
    public const string SignedAtHeader = "Vericairn-Signed-At";

    /// <summary>Header carrying the base64url signature.</summary>
    public const string SignatureHeader = "Vericairn-Signature";

    /// <summary>How far a signing time may be from the daemon's clock.</summary>
    public static TimeSpan MaxSkew { get; } = TimeSpan.FromMinutes(5);

    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Formats a signing time.</summary>
    public static string FormatTime(DateTimeOffset at) => at.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses a signing time.</summary>
    public static bool TryParseTime(string? text, out DateTimeOffset at) =>
        DateTimeOffset.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);

    /// <summary>The digest a device signs: SHA-256 over the scheme, method, request target, time and body hash joined by line feeds.</summary>
    public static byte[] Digest(string method, string requestTarget, string signedAt, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(requestTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedAt);
        string canonical = string.Join('\n', Scheme, method.ToUpperInvariant(), requestTarget, signedAt, Convert.ToHexStringLower(SHA256.HashData(body)));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
