// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// Verifies device-signed requests on profiles without operating-system identity: the device must be admitted now in the estate
/// the daemon serves, the signing time must be within the permitted skew, the signature must verify over the method, the request
/// target, the time and the body, and a signature is accepted once, in whichever of its equivalent forms it comes. On the local
/// network the device must also be permitted that profile, present a TLS client certificate its own key issued, and act for its
/// own principal, and the operations on the runtime's own machine are refused.
/// </summary>
internal sealed class RequestAuthenticator
{
    /// <summary>The profile name a device must be permitted to use the local network.</summary>
    internal const string LocalNetworkProfile = "local-network";

    /// <summary>The routes of the pairing exchange, which a device without a credential reaches; the code authenticates them.</summary>
    internal static readonly IReadOnlySet<string> PairingRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST /v1/pairing/hello", "POST /v1/pairing/confirm" };

    /// <summary>Operations whose effect is on the runtime's own machine: its process, its key custody, its files and its certificate.</summary>
    internal static readonly IReadOnlySet<string> MachineLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "POST /v1/shutdown",
        "POST /v1/devices",
        "POST /v1/devices/rename",
        "POST /v1/devices/rotate",
        "POST /v1/devices/recovery",
        "POST /v1/estate/export",
        "POST /v1/pairings",
        "POST /v1/runtime/certificate",
    };

    private const int Remembered = 4096;

    /// <summary>The OID of ecdsa-with-SHA256, the only signature a device-issued client certificate carries.</summary>
    private const string EcdsaWithSha256 = "1.2.840.10045.4.3.2";

    /// <summary>The order n of the P-256 group: an ECDSA signature (r, s) and its twin (r, n - s) verify alike.</summary>
    private static readonly BigInteger GroupOrder = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private readonly EstateService service;
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();
    private readonly object gate = new();

    public RequestAuthenticator(EstateService service)
    {
        this.service = service;
    }

    /// <summary>The operation a request reaches, as its method and route pattern, for example POST /v1/shutdown; the path itself when no route matched.</summary>
    internal static string Operation(HttpContext context)
    {
        string path = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? string.Empty;
        return context.Request.Method.ToUpperInvariant() + " /" + path.Trim('/');
    }

    /// <summary>Null when a loopback request is authenticated; otherwise the problem to answer with.</summary>
    public async Task<Problem?> VerifyAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        string? device = Header(request, RequestSigning.DeviceHeader);
        string? signedAt = Header(request, RequestSigning.SignedAtHeader);
        string? signature = Header(request, RequestSigning.SignatureHeader);
        if (device is null || signedAt is null || signature is null)
        {
            return Denied($"Loopback requests are signed by an admitted device: send {RequestSigning.DeviceHeader}, {RequestSigning.SignedAtHeader} and {RequestSigning.SignatureHeader}, or use the local socket.");
        }

        if (!Fresh(signedAt))
        {
            return Denied("The signing time is malformed or more than five minutes from the runtime's clock.");
        }

        (byte[] digest, _) = await DigestAsync(context, signedAt).ConfigureAwait(false);
        string? publicKey = service.AdmittedDevicePublicKey(device);
        if (publicKey is null)
        {
            return Denied($"Device {device} is unknown here or not admitted now; an administrator admits devices with 'vericairn devices admit'.");
        }

        return Signed(publicKey, digest, signature);
    }

    /// <summary>
    /// Null when a local-network request may proceed; otherwise the problem to answer with. The pairing exchange passes, since its
    /// code authenticates it; every other request comes from a device that is current, admitted now and permitted the local network,
    /// over a TLS connection whose client certificate the device's current key issued, and is signed by that key, fresh and once.
    /// </summary>
    public async Task<Problem?> VerifyLocalNetworkAsync(HttpContext context)
    {
        string operation = Operation(context);
        if (PairingRoutes.Contains(operation))
        {
            return null;
        }

        HttpRequest request = context.Request;
        string? device = Header(request, RequestSigning.DeviceHeader);
        string? signedAt = Header(request, RequestSigning.SignedAtHeader);
        string? signature = Header(request, RequestSigning.SignatureHeader);
        if (device is null || signedAt is null || signature is null)
        {
            return Denied($"Local-network requests come from a paired device: present the TLS client certificate its device key issued and sign every request ({RequestSigning.DeviceHeader}, {RequestSigning.SignedAtHeader}, {RequestSigning.SignatureHeader}). A new device pairs first, with a pairing code from an administrator.");
        }

        // The device as the records say it is now: revocation counts from the next request on, on an open connection too.
        DeviceStanding? standing = service.Standing(device);
        if (standing is null)
        {
            return Denied($"Device {device} is unknown here: a device reaches the runtime over the local network once it has paired, with a pairing code from an administrator.");
        }

        if (standing.Admission == "revoked")
        {
            return Denied($"Device {standing.Identity} is revoked, and a revoked device is refused at once on every profile.");
        }

        if (standing.Admission != "admitted")
        {
            return Denied($"Device {standing.Identity} is not admitted now; an administrator admits devices.");
        }

        if (!standing.Profiles.Contains(LocalNetworkProfile, StringComparer.Ordinal))
        {
            return Denied($"Device {standing.Identity} is not permitted the local-network profile; its permitted profiles are {(standing.Profiles.Count == 0 ? "none" : string.Join(", ", standing.Profiles))}.");
        }

        X509Certificate2? certificate = context.Connection.ClientCertificate;
        if (certificate is null || !IssuedBy(certificate, device, standing.PublicKey, DateTimeOffset.UtcNow))
        {
            return Denied($"The connection's TLS client certificate is not one the current key of device {standing.Identity} issued; a paired device presents a certificate its own key issued.");
        }

        if (!Fresh(signedAt))
        {
            return Denied("The signing time is malformed or more than five minutes from the runtime's clock.");
        }

        (byte[] digest, byte[] body) = await DigestAsync(context, signedAt).ConfigureAwait(false);
        if (Signed(standing.PublicKey, digest, signature) is Problem problem)
        {
            return problem;
        }

        if (MachineLocal.Contains(operation))
        {
            return Denied($"{operation} acts on the runtime's own machine and is refused over the local network; run it on that machine.");
        }

        return WithinScope(request, body, standing);
    }

    /// <summary>
    /// True when a TLS client certificate names the device, is within its validity, and carries an ECDSA P-256 signature with SHA-256
    /// by the device's key: a certificate the device's key issued for the TLS key the connection proved it holds.
    /// </summary>
    internal static bool IssuedBy(X509Certificate2 certificate, string device, string publicKey, DateTimeOffset now)
    {
        if (!string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false), device, StringComparison.Ordinal)
            || now < new DateTimeOffset(certificate.NotBefore.ToUniversalTime()) - RequestSigning.MaxSkew
            || now > new DateTimeOffset(certificate.NotAfter.ToUniversalTime()))
        {
            return false;
        }

        try
        {
            var reader = new AsnReader(certificate.RawData, AsnEncodingRules.DER);
            AsnReader whole = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            ReadOnlyMemory<byte> tbs = whole.ReadEncodedValue();
            AsnReader algorithm = whole.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcdsaWithSha256)
            {
                return false;
            }

            byte[] signature = whole.ReadBitString(out int unused);
            whole.ThrowIfNotEmpty();
            if (unused != 0)
            {
                return false;
            }

            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(publicKey), out _);
            return key.VerifyData(tbs.Span, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception exception) when (exception is AsnContentException or CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// A device on the local network acts for its own principal: a request whose body names an acting principal, as by or principal
    /// at its top level in any spelling, names the device's. Its principal's authority is then evaluated by the runtime as for any
    /// request. Nor does it reach the runtime's files: a recovery key is a file on the runtime's machine, used there only.
    /// </summary>
    private static Problem? WithinScope(HttpRequest request, byte[] body, DeviceStanding standing)
    {
        if (body.Length == 0 || request.ContentType is null || !request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                bool acting = property.Name.Equals("by", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("principal", StringComparison.OrdinalIgnoreCase);
                if (acting && property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() != standing.Principal))
                {
                    return Denied($"Device {standing.Identity} acts for its own principal {standing.Principal} over the local network; this request names another in '{property.Name}'.");
                }

                if (property.Name.Equals("recovery", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind != JsonValueKind.Null)
                {
                    return Denied("A recovery key is a file on the runtime's own machine and is used there only; it is refused over the local network.");
                }
            }
        }
        catch (JsonException)
        {
            // Malformed: the route refuses it.
        }

        return null;
    }

    private static bool Fresh(string signedAt) =>
        RequestSigning.TryParseTime(signedAt, out DateTimeOffset at) && (DateTimeOffset.UtcNow - at).Duration() <= RequestSigning.MaxSkew;

    private static async Task<(byte[] Digest, byte[] Body)> DigestAsync(HttpContext context, string signedAt)
    {
        HttpRequest request = context.Request;
        request.EnableBuffering();
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        request.Body.Position = 0;
        string target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToString() + request.QueryString;
        return (RequestSigning.Digest(request.Method, target, signedAt, body), body);
    }

    /// <summary>Verifies a request signature and accepts it once.</summary>
    private Problem? Signed(string publicKey, byte[] digest, string signature)
    {
        if (!RecordSigning.Verify(publicKey, digest, signature))
        {
            return Denied("The request signature does not verify against the device's key.");
        }

        string key = ReplayKey(signature);
        lock (gate)
        {
            if (!seen.Add(key))
            {
                return Denied("The request signature was already used; sign each request afresh.");
            }

            order.Enqueue(key);
            if (order.Count > Remembered)
            {
                seen.Remove(order.Dequeue());
            }
        }

        return null;
    }

    /// <summary>
    /// What an accepted signature is remembered by. Its text is no key, because base64url spells the same 64 bytes in more than one
    /// way (padded, for one); nor are its bytes, because an ECDSA signature is malleable: (r, n - s) verifies over the same request
    /// as well as (r, s). The bytes with s replaced by the smaller of s and n - s are the same for every form of one signature.
    /// </summary>
    private static string ReplayKey(string signature)
    {
        byte[] bytes = Base64Url.DecodeFromChars(signature);
        Span<byte> half = bytes.AsSpan(RecordSigning.SignatureBytes / 2);
        var s = new BigInteger(half, isUnsigned: true, isBigEndian: true);
        BigInteger twin = GroupOrder - s;
        if (twin < s)
        {
            half.Clear();
            twin.TryWriteBytes(half[(half.Length - twin.GetByteCount(isUnsigned: true))..], out _, isUnsigned: true, isBigEndian: true);
        }

        return Convert.ToHexStringLower(bytes);
    }

    private static string? Header(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out StringValues values) && !string.IsNullOrWhiteSpace(values.ToString()) ? values.ToString() : null;

    private static Problem Denied(string message) => new("denied", message, []);
}
