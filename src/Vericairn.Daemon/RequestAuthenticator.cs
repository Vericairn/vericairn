// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>A device a request was authenticated as, on loopback or the local network: what the request may act for once it is bound.</summary>
/// <param name="Identity">The device's identity.</param>
/// <param name="Principal">The principal it acts for, and the only one its requests may name as acting.</param>
/// <param name="LocalNetwork">Whether the request came over the local network.</param>
internal sealed record AuthenticatedDevice(string Identity, string Principal, bool LocalNetwork);

/// <summary>
/// Verifies device-signed requests on profiles without operating-system identity: the device must be admitted now in the estate
/// the daemon serves, the signing time must be within the permitted skew, the body must be UTF-8 without a byte order mark, the
/// signature must verify over the method, the request target, the time and the body, and a signature is accepted once, in
/// whichever of its equivalent forms it comes. On the local network the device must also be permitted that profile and present a
/// TLS client certificate its own key issued, and the operations on the runtime's own machine are refused. The device it verified
/// is left on the request, so that once the request is bound <see cref="RequestScope"/> holds it to its own principal.
/// </summary>
internal sealed class RequestAuthenticator
{
    /// <summary>The key under which a verified request carries its <see cref="AuthenticatedDevice"/>.</summary>
    internal static readonly object DeviceItem = new();

    /// <summary>The profile name a device must be permitted to use the local network.</summary>
    internal const string LocalNetworkProfile = "local-network";

    /// <summary>The routes of the pairing exchange, which a device without a credential reaches; the code authenticates them.</summary>
    internal static readonly IReadOnlySet<string> PairingRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST /v1/pairing/hello", "POST /v1/pairing/confirm" };

    /// <summary>
    /// Operations whose effect is on the runtime's own machine: its process, its key custody, its files, its certificate, the release
    /// provider's credentials there (a release's submission and observation), its object store, and the coding agents it launches.
    /// </summary>
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
        "POST /v1/releases/submit",
        "POST /v1/releases/observe",
        "POST /v1/objects",
        "POST /v1/runs",
    };


    /// <summary>The OID of ecdsa-with-SHA256, the only signature a device-issued client certificate carries.</summary>
    private const string EcdsaWithSha256 = "1.2.840.10045.4.3.2";

    /// <summary>The order n of the P-256 group: an ECDSA signature (r, s) and its twin (r, n - s) verify alike.</summary>
    private static readonly BigInteger GroupOrder = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private readonly EstateService service;
    private readonly ReplayCache replays;

    public RequestAuthenticator(EstateService service, int replayCapacity)
    {
        this.service = service;
        replays = new ReplayCache(replayCapacity);
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

        (byte[] digest, byte[] body) = await DigestAsync(context, signedAt).ConfigureAwait(false);
        if (Encoded(request, body) is Problem encoding)
        {
            return encoding;
        }

        DeviceStanding? standing = service.Standing(device);
        if (standing is null || standing.Admission != "admitted")
        {
            return Denied($"Device {device} is unknown here or not admitted now; an administrator admits devices with 'vericairn devices admit'.");
        }

        if (Signed(standing, signedAt, digest, signature) is Problem problem)
        {
            return problem;
        }

        context.Items[DeviceItem] = new AuthenticatedDevice(standing.Identity, standing.Principal, LocalNetwork: false);
        return null;
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
        if (Encoded(request, body) is Problem encoding)
        {
            return encoding;
        }

        if (Signed(standing, signedAt, digest, signature) is Problem problem)
        {
            return problem;
        }

        if (MachineLocal.Contains(operation))
        {
            return Denied($"{operation} acts on the runtime's own machine and is refused over the local network; run it on that machine.");
        }

        context.Items[DeviceItem] = new AuthenticatedDevice(standing.Identity, standing.Principal, LocalNetwork: true);
        return null;
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
    /// A device-signed body is UTF-8 without a byte order mark: the bytes the signature covers are then exactly the text the runtime
    /// binds, so nothing a check reads can differ from what the operation receives. A declared charset other than UTF-8 is refused
    /// whatever the content, and a JSON body that starts with a byte order mark is refused.
    /// </summary>
    private static Problem? Encoded(HttpRequest request, byte[] body)
    {
        if (request.ContentType is not string type || !MediaTypeHeaderValue.TryParse(type, out MediaTypeHeaderValue? media))
        {
            return null;
        }

        if (media.Charset.HasValue && media.Charset.Length > 0)
        {
            string charset = HeaderUtilities.RemoveQuotes(media.Charset).ToString();
            bool utf8;
            try
            {
                utf8 = System.Text.Encoding.GetEncoding(charset).CodePage == System.Text.Encoding.UTF8.CodePage;
            }
            catch (ArgumentException)
            {
                utf8 = false;
            }

            if (!utf8)
            {
                return Denied($"A device-signed request carries its body in UTF-8; the runtime refuses the charset '{charset}'.");
            }
        }

        bool json = media.MediaType.Value?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        if (json && body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
        {
            return Denied("A device-signed request carries its JSON body in UTF-8 without a byte order mark; this one starts with one.");
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

    /// <summary>Verifies a request signature and accepts it once, remembered for its device until it is stale.</summary>
    private Problem? Signed(DeviceStanding standing, string signedAt, byte[] digest, string signature)
    {
        if (!RecordSigning.Verify(standing.PublicKey, digest, signature))
        {
            return Denied("The request signature does not verify against the device's key.");
        }

        // The time was found fresh before the body was read, so it parses; a signature is remembered until it is stale.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expires = (RequestSigning.TryParseTime(signedAt, out DateTimeOffset at) ? at : now) + RequestSigning.MaxSkew;
        return replays.Accept(standing.Identity, ReplayKey(signature), expires, now) switch
        {
            ReplayCache.Outcome.Repeated => Denied("The request signature was already used; sign each request afresh."),
            ReplayCache.Outcome.Full => Denied($"Device {standing.Identity} has signed more requests in the last five minutes than the runtime remembers for one device; it refuses more until older ones expire."),
            _ => null,
        };
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
