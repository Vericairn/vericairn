// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// The routes only a running daemon can serve, from 0.6.0: pairing codes and the pairing exchange, the runtime identity's proof
/// and the certificate's rotation. They need the daemon's listeners and memory, so the embedded runtime has none of them.
/// </summary>
internal static class DaemonRoutes
{
    /// <summary>Maps the daemon's own routes.</summary>
    public static void Map(IEndpointRouteBuilder app, DaemonHost host)
    {
        string v = ApiVersion.Prefix;
        app.MapPost($"{v}/pairings", (PairingIssueRequest request) => Issue(host, request));
        app.MapPost($"{v}/pairing/hello", (HttpContext context, PairingHello hello) => Hello(host, context, hello));
        app.MapPost($"{v}/pairing/confirm", (HttpContext context, PairingConfirm confirm) => Confirm(host, context, confirm));
        app.MapGet(RuntimeIdentityScheme.Path, (HttpContext context, string? nonce) => Prove(host, context, nonce));
        app.MapPost($"{v}/runtime/certificate", (HttpContext context, CertificateRotateRequest request) => Rotate(host, context, request));
    }

    /// <summary>Issues a pairing code under an administrator's authority, for a principal and the profiles it will be permitted.</summary>
    private static IResult Issue(DaemonHost host, PairingIssueRequest request)
    {
        if (host.Identity is null || host.Endpoint.LocalNetwork is not LocalNetworkEndpoint served)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", "This runtime serves no local-network profile, so no device can pair with it: configure an address with 'vericairn runtime configure --local-network <address:port>' and start the runtime again.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Principal) || string.IsNullOrWhiteSpace(request.By))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A pairing code is issued for a principal (principal) by an administrator (by).");
        }

        IReadOnlyList<string> profiles = request.Profiles is { Count: > 0 } ? request.Profiles.Distinct(StringComparer.Ordinal).ToList() : [RequestAuthenticator.LocalNetworkProfile];
        if (host.Service.PairingRefusal(request.Principal, request.By, profiles) is string refusal)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", refusal);
        }

        (string? code, PairingRegistry.Pairing? pairing, string? problem) = host.Pairings.Issue(request.Principal, request.By, profiles);
        if (code is null || pairing is null)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", problem ?? "No pairing code was issued.");
        }

        string? name = host.Service.PairingPlan(request.Principal).PrincipalName;
        string address = new IPEndPoint(IPAddress.Parse(served.Host), served.Port).ToString();
        host.Log($"pairing {pairing.Id} issued by {request.By} for {request.Principal} ({string.Join(", ", profiles)}), until {RecordParser.FormatTimestamp(pairing.ExpiresAt)}");
        return Results.Json(new PairingIssued(PairingProtocol.Display(code), request.Principal, name, profiles, RecordParser.FormatTimestamp(pairing.ExpiresAt), PairingRegistry.Attempts, address, host.Service.Session.Manifest.Estate, host.Identity.Fingerprint), ProtocolJson.Options);
    }

    /// <summary>
    /// Answers a device's hello that proves a code it holds: who the runtime is, the certificate on this connection and what will be
    /// recorded, with the runtime's MAC. A hello that proves no code is refused alike and its connection closed.
    /// </summary>
    private static IResult Hello(DaemonHost host, HttpContext context, PairingHello hello)
    {
        if (!DaemonHost.OnLocalNetwork(context) || host.Identity is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "denied", "Pairing runs over the local-network profile, at the address the administrator gave with the code.");
        }

        if (hello is null || !PairingProtocol.IsNonce(hello.Nonce) || string.IsNullOrEmpty(hello.Mac))
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A hello carries a nonce of 32 random bytes and its MAC, both base64url.");
        }

        string certificate = host.CertificateOf(context);
        (PairingRegistry.Pairing? pairing, string? problem) = host.Pairings.Hello(hello, certificate, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (pairing is null)
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status401Unauthorized, "denied", problem!);
        }

        PairingPlan plan = host.Service.PairingPlan(pairing.Principal);
        var challenge = new PairingChallenge(hello.Nonce, PairingProtocol.NewNonce(), plan.Estate, plan.EstateName, host.Identity.PublicKey, certificate, plan.Principal, plan.PrincipalName, plan.PrincipalKind, pairing.Profiles, plan.Record, plan.At, plan.Format, RecordParser.FormatTimestamp(pairing.ExpiresAt), string.Empty);
        challenge = challenge with { Mac = PairingProtocol.ChallengeMac(pairing.RuntimeKey, hello, challenge) };
        host.Pairings.Open(pairing, hello, challenge, plan);
        return Results.Json(challenge, ProtocolJson.Options);
    }

    /// <summary>Records the device a confirmation presents, when its MAC matches the code and its key signed its record: the device record, then its admission.</summary>
    private static IResult Confirm(DaemonHost host, HttpContext context, PairingConfirm confirm)
    {
        if (!DaemonHost.OnLocalNetwork(context))
        {
            return Problem(StatusCodes.Status401Unauthorized, "denied", "Pairing runs over the local-network profile, at the address the administrator gave with the code.");
        }

        if (confirm is null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A confirmation carries the device, its signature of its record and its MAC.");
        }

        (PairingRegistry.Pairing? pairing, PairingRegistry.Exchange? exchange, string? problem) = host.Pairings.Confirm(confirm);
        if (pairing is null || exchange is null)
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status401Unauthorized, "denied", problem!);
        }

        string? device = null;
        try
        {
            if (DescriptiveRefusal(confirm.Device) is string descriptive)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "refused", descriptive);
            }

            string note = $"Paired over the local network with a pairing code {pairing.By} issued at {RecordParser.FormatTimestamp(pairing.IssuedAt)}; the device presented its key and signed its record. Its name, platform, custody and presence are its own statement, which the runtime cannot verify.";
            (WriteOutcome written, WriteOutcome? admitted) = host.Service.PairDevice(exchange.Plan, pairing.Profiles, confirm.Device, confirm.Signature, pairing.By, note);
            if (!written.Ok || admitted?.Written is null)
            {
                return Results.Json(written.Problem ?? admitted?.Problem, ProtocolJson.Options, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            device = written.Written!.Id;
            var completed = new PairingCompleted(device, admitted.Written.Id, written.Written.Hash, string.Empty);
            completed = completed with { Mac = PairingProtocol.CompletedMac(pairing.RuntimeKey, exchange.Hello, exchange.Challenge, confirm, completed) };
            host.Log($"device {device} ({confirm.Device.Platform}) paired for {pairing.Principal} under {pairing.By}, pairing {pairing.Id}");
            return Results.Json(completed, ProtocolJson.Options);
        }
        finally
        {
            host.Pairings.Finish(pairing, device);
        }
    }

    /// <summary>A device's descriptive name is text a person reads: no control or formatting characters, which could hide or reorder what is shown.</summary>
    private static string? DescriptiveRefusal(PairingDevice device) =>
        device.Name is null || device.Name.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            ? "A device's name is text without control or formatting characters; nothing was recorded."
            : null;

    /// <summary>The runtime identity's proof for a nonce, over the certificate this connection presented; written with its length, for a client that reads it before any other request.</summary>
    private static IResult Prove(DaemonHost host, HttpContext context, string? nonce)
    {
        if (host.Identity is null)
        {
            return Problem(StatusCodes.Status404NotFound, "not-found", "This runtime has no identity to prove: it serves no local-network profile.");
        }

        if (!PairingProtocol.IsNonce(nonce))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A proof answers a nonce of 32 random bytes in base64url.");
        }

        RuntimeIdentityProof proof = host.Identity.Prove(host.Service.Session.Manifest.Estate, host.CertificateOf(context), nonce!);
        return Results.Content(JsonSerializer.Serialize(proof, ProtocolJson.Options), "application/json", Encoding.UTF8, StatusCodes.Status200OK);
    }

    /// <summary>Replaces the runtime certificate now; for a compromise also closes every other open connection, and with identity replaces the runtime identity too.</summary>
    private static IResult Rotate(DaemonHost host, HttpContext context, CertificateRotateRequest request)
    {
        request ??= new CertificateRotateRequest();
        if (host.Certificates is null)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", "This daemon serves no TLS profile, so it presents no certificate to rotate; the certificate file is replaced at the next start.");
        }

        if (request.Identity && host.Identity is null)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", "This daemon serves no local-network profile, so it has no identity to replace.");
        }

        CertificateRotated rotated = host.RotateCertificate(request.Compromised || request.Identity, request.Reason, request.Identity, context.Connection.Id);
        if (rotated.Compromised && context.Connection.LocalIpAddress is not null)
        {
            context.Response.Headers.Connection = "close";
        }

        return Results.Json(rotated, ProtocolJson.Options);
    }

    /// <summary>A problem with its length stated.</summary>
    private static IResult Problem(int status, string code, string message) =>
        Results.Content(JsonSerializer.Serialize(new Problem(code, message, []), ProtocolJson.Options), "application/json", Encoding.UTF8, status);
}
