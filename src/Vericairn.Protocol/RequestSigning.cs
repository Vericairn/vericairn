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
/// Device-signed requests: the profiles that cannot rely on operating-system identity carry three headers naming the device, the
/// signing time and a signature over the method, the request target, the time and the body hash. Loopback signs version 1, as API v1
/// clients always have. The local network signs version 2, from 0.6.0, which also binds the estate and the runtime identity the device
/// paired with; only the request for the runtime identity's proof, sent before the runtime has proven which identity answers, is
/// signed version 1 there. The daemon accepts a signature only from a device admitted now, signed since this instance began accepting
/// requests, no later than its clock, no more than five minutes old, and once.
/// </summary>
public static class RequestSigning
{
    /// <summary>The scheme name bound into every loopback digest.</summary>
    public const string Scheme = "vericairn-request/1";

    /// <summary>The scheme name bound into every local-network digest, from 0.6.0.</summary>
    public const string LocalNetworkScheme = "vericairn-request/2";

    /// <summary>Header carrying the device record id.</summary>
    public const string DeviceHeader = "Vericairn-Device";

    /// <summary>Header carrying the signing time (RFC 3339, UTC, seconds).</summary>
    public const string SignedAtHeader = "Vericairn-Signed-At";

    /// <summary>Header carrying the base64url signature.</summary>
    public const string SignatureHeader = "Vericairn-Signature";

    /// <summary>The maximum age of a request's signing time; future times and times before the instance's startup are refused.</summary>
    public static TimeSpan MaxSkew { get; } = TimeSpan.FromMinutes(5);

    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Formats a signing time.</summary>
    public static string FormatTime(DateTimeOffset at) => at.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses a signing time.</summary>
    public static bool TryParseTime(string? text, out DateTimeOffset at) =>
        DateTimeOffset.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);

    /// <summary>The digest a device signs on loopback: SHA-256 over the scheme, method, request target, time and body hash joined by line feeds.</summary>
    public static byte[] Digest(string method, string requestTarget, string signedAt, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(requestTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedAt);
        string canonical = string.Join('\n', Scheme, method.ToUpperInvariant(), requestTarget, signedAt, Convert.ToHexStringLower(SHA256.HashData(body)));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>
    /// The digest a device signs over the local network, version 2: SHA-256 over the scheme, the estate's id, the fingerprint of the
    /// runtime identity the device paired with (<see cref="RuntimeIdentityScheme.Fingerprint"/> of the public key it pinned), the
    /// method, request target, time and body hash, joined by line feeds. A request signed for one runtime verifies at no other, not
    /// even a runtime of the same estate on another machine, which knows the same device.
    /// </summary>
    public static byte[] LocalNetworkDigest(string estate, string identity, string method, string requestTarget, string signedAt, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(requestTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedAt);
        string canonical = string.Join('\n', LocalNetworkScheme, estate, RuntimeIdentityScheme.Fingerprint(identity), method.ToUpperInvariant(), requestTarget, signedAt, Convert.ToHexStringLower(SHA256.HashData(body)));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
