// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// Verifies device-signed requests on profiles without operating-system identity: the device must be admitted now in the estate
/// the daemon serves, the signing time must be within the permitted skew, the signature must verify over the method, the request
/// target, the time and the body, and a signature is accepted once.
/// </summary>
internal sealed class RequestAuthenticator
{
    private const int Remembered = 4096;
    private readonly EstateService service;
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();
    private readonly object gate = new();

    public RequestAuthenticator(EstateService service)
    {
        this.service = service;
    }

    /// <summary>Null when the request is authenticated; otherwise the problem to answer with.</summary>
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

        if (!RequestSigning.TryParseTime(signedAt, out DateTimeOffset at) || (DateTimeOffset.UtcNow - at).Duration() > RequestSigning.MaxSkew)
        {
            return Denied("The signing time is malformed or more than five minutes from the runtime's clock.");
        }

        request.EnableBuffering();
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        request.Body.Position = 0;
        string target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToString() + request.QueryString;
        byte[] digest = RequestSigning.Digest(request.Method, target, signedAt, body);
        string? publicKey = service.AdmittedDevicePublicKey(device);
        if (publicKey is null)
        {
            return Denied($"Device {device} is unknown here or not admitted now; an administrator admits devices with 'vericairn devices admit'.");
        }

        if (!RecordSigning.Verify(publicKey, digest, signature))
        {
            return Denied("The request signature does not verify against the device's key.");
        }

        lock (gate)
        {
            if (!seen.Add(signature))
            {
                return Denied("The request signature was already used; sign each request afresh.");
            }

            order.Enqueue(signature);
            if (order.Count > Remembered)
            {
                seen.Remove(order.Dequeue());
            }
        }

        return null;
    }

    private static string? Header(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out StringValues values) && !string.IsNullOrWhiteSpace(values.ToString()) ? values.ToString() : null;

    private static Problem Denied(string message) => new("denied", message, []);
}
