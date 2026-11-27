// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
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
        app.MapPost($"{v}/runtime/mcp/authorize", (HttpContext context, McpAuthorizeRequest request) => AuthorizeMcp(host, context, request));
        app.MapPost($"{v}/pairings", (PairingIssueRequest request) => Issue(host, request));
        app.MapPost($"{v}/pairing/hello", (HttpContext context, PairingHello hello) => Hello(host, context, hello));
        app.MapPost($"{v}/pairing/confirm", (HttpContext context, PairingConfirm confirm) => Confirm(host, context, confirm));
        app.MapGet(RuntimeIdentityScheme.Path, (HttpContext context, string? nonce) => Prove(host, context, nonce));
        app.MapPost($"{v}/runtime/certificate", (HttpContext context, CertificateRotateRequest request) => Rotate(host, context, request));
    }

    private static IResult AuthorizeMcp(DaemonHost host, HttpContext context, McpAuthorizeRequest request)
    {
        if (!DaemonHost.OverLocalSocket(context.Features.Get<Microsoft.AspNetCore.Connections.Features.IConnectionItemsFeature>()?.Items))
            return Problem(StatusCodes.Status401Unauthorized, "denied", "MCP approval is issued only over the runtime's local socket.");
        if (host.OAuth is not { } authorization)
            return Problem(StatusCodes.Status409Conflict, "refused", "Enable MCP and configure a public endpoint on this runtime before issuing an approval.");

        context.Response.Headers.CacheControl = "no-store";
        (McpApprovalIssued? issued, string? refused) = authorization.Issue(request.Principal, request.By, request.Scope, request.Days, request.Renew);
        return issued is null ? Problem(StatusCodes.Status422UnprocessableEntity, "refused", refused!) : Results.Json(issued, ProtocolJson.Options);
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

        IReadOnlyList<string> profiles = request.Profiles is { Count: > 0 } ? request.Profiles.Distinct(StringComparer.Ordinal).ToList() : host.Relay is null ? [RequestAuthenticator.LocalNetworkProfile] : [RequestAuthenticator.LocalNetworkProfile, RequestAuthenticator.RelayProfile];
        if (profiles.Contains("token", StringComparer.Ordinal))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "Token devices are made by an approved MCP authorization.");
        }

        (PairingAuthority? issuer, string? refusal) = host.Service.PairingIssuer(request.Principal, request.By, profiles);
        if (issuer is null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", refusal!);
        }

        (string? code, PairingRegistry.Pairing? pairing, string? problem) = host.Pairings.Issue(request.Principal, issuer, profiles);
        if (code is null || pairing is null)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", problem ?? "No pairing code was issued.");
        }

        string? name = host.Service.PairingPlan(request.Principal).PrincipalName;
        string address = new IPEndPoint(IPAddress.Parse(served.Host), served.Port).ToString();
        host.Log($"pairing {pairing.Id} issued by {request.By}, device {issuer.Device}, authority {issuer.Authority}, for {request.Principal} ({string.Join(", ", profiles)}), until {RecordParser.FormatTimestamp(pairing.ExpiresAt)}");
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

        // The MAC is exactly 32 bytes of base64url, checked before anything is decoded or any code tried.
        if (hello is null || !PairingProtocol.IsNonce(hello.Nonce) || !PairingProtocol.IsMac(hello.Mac))
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A hello carries a nonce of 32 random bytes and its MAC, both base64url.");
        }

        string certificate = host.CertificateOf(context);
        byte[] mac = Base64Url.DecodeFromChars(hello.Mac);
        (PairingRegistry.Pairing? pairing, string? problem) = host.Pairings.Hello(hello, mac, certificate, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (pairing is null)
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status401Unauthorized, "denied", problem!);
        }

        (PairingPlan plan, PairingChallenge challenge) = Challenge(host.Service, pairing, hello, host.Identity.PublicKey, certificate);
        if (host.Pairings.Open(pairing, hello, challenge, plan) is string closed)
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status401Unauthorized, "denied", closed);
        }

        return Results.Json(challenge, ProtocolJson.Options);
    }

    internal static (PairingPlan Plan, PairingChallenge Challenge) Challenge(EstateService service, PairingRegistry.Pairing pairing, PairingHello hello, string identity, string certificate)
    {
        // Older clients ask for presence but can only declare it under the old rules. Pair their request key alone.
        bool offerPresence = hello.Presence == true && hello.PresenceAttestation == true;
        PairingPlan plan = service.PairingPlan(pairing.Principal, offerPresence);
        var challenge = new PairingChallenge(hello.Nonce, plan.RuntimeNonce, plan.Estate, plan.EstateName, identity, certificate, plan.Principal, plan.PrincipalName, plan.PrincipalKind, pairing.Profiles, plan.Record, plan.At, plan.Format, RecordParser.FormatTimestamp(pairing.ExpiresAt), string.Empty)
        {
            PresenceRecord = plan.PresenceRecord,
            PresenceAttestation = plan.Format >= 2 && hello.PresenceAttestation == true ? true : null,
        };
        return (plan, challenge with { Mac = PairingProtocol.ChallengeMac(pairing.RuntimeKey, hello, challenge) });
    }

    /// <summary>Records the device a confirmation presents, when its MAC matches the code and its key signed its record: the device record, then its admission.</summary>
    private static IResult Confirm(DaemonHost host, HttpContext context, PairingConfirm confirm)
    {
        if (!DaemonHost.OnLocalNetwork(context))
        {
            return Problem(StatusCodes.Status401Unauthorized, "denied", "Pairing runs over the local-network profile, at the address the administrator gave with the code.");
        }

        if (confirm is null || !PairingProtocol.IsMac(confirm.Mac))
        {
            context.Response.Headers.Connection = "close";
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A confirmation carries the device, its signature of its record and a 32-byte MAC in base64url.");
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
            if ((DescriptiveRefusal(confirm.Device) ?? (confirm.PresenceDevice is null ? null : DescriptiveRefusal(confirm.PresenceDevice))) is string descriptive)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "refused", descriptive);
            }

            if ((confirm.PresenceDevice is not null || confirm.PresenceSignature is not null) && exchange.Challenge.PresenceRecord is null)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "This exchange offered no presence device; nothing was recorded.");
            }

            string note = $"{DeviceRecords.PairingAdmissionNote}{pairing.By} issued from device {pairing.Issuer.Device}, under authority {pairing.Issuer.Authority}, at {RecordParser.FormatTimestamp(pairing.IssuedAt)}; the device presented its key and signed its record. The device declared its name, platform and custody; its record identifies any verified presence.";
            (WriteOutcome written, WriteOutcome? admitted, WriteOutcome? presence, WriteOutcome? presenceAdmitted) =
                host.Service.PairDevice(exchange.Plan, pairing.Profiles, confirm.Device, confirm.Signature, pairing.Issuer, note, confirm.PresenceDevice, confirm.PresenceSignature);
            bool withPresence = confirm.PresenceDevice is not null;
            if (!written.Ok || admitted?.Written is null || withPresence && (presence?.Written is null || presenceAdmitted?.Written is null))
            {
                Problem? refused = written.Problem ?? admitted?.Problem ?? presence?.Problem ?? presenceAdmitted?.Problem;
                return Results.Json(refused, ProtocolJson.Options, statusCode: refused?.Code == "denied" ? StatusCodes.Status401Unauthorized : StatusCodes.Status422UnprocessableEntity);
            }

            device = written.Written!.Id;
            var completed = new PairingCompleted(device, admitted.Written.Id, written.Written.Hash, string.Empty)
            {
                PresenceDevice = withPresence ? presence!.Written!.Id : null,
                PresenceAdmission = withPresence ? presenceAdmitted!.Written!.Id : null,
                PresenceHash = withPresence ? presence!.Written!.Hash : null,
                Relay = exchange.Hello.Relay == true ? host.Relay?.Issue(device) : null,
            };
            completed = completed with { Mac = PairingProtocol.CompletedMac(pairing.RuntimeKey, exchange.Hello, exchange.Challenge, confirm, completed) };
            host.Log($"device {device} ({confirm.Device.Platform}) paired for {pairing.Principal} under {pairing.By}, pairing {pairing.Id}" + (withPresence ? $", with presence device {completed.PresenceDevice}" : ""));
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

    /// <summary>
    /// The runtime identity's proof for a nonce, over the certificate this connection presented and the client certificate it was
    /// presented: over the local network only, to a paired device whose request was authenticated, and only while the connection's
    /// certificate is the current one. Written with its length, for a client that reads it before any other request.
    /// </summary>
    private static IResult Prove(DaemonHost host, HttpContext context, string? nonce)
    {
        if (host.Identity is null || (!DaemonHost.OnLocalNetwork(context) && !DaemonHost.OnRelay(context)) || context.Connection.ClientCertificate is not { } client)
        {
            return Problem(StatusCodes.Status404NotFound, "not-found", "The runtime identity proves itself to paired devices over the local network only.");
        }

        if (!PairingProtocol.IsNonce(nonce))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "refused", "A proof answers a nonce of 32 random bytes in base64url.");
        }

        string certificate = host.CertificateOf(context);
        if (certificate != host.Certificates?.Fingerprint)
        {
            return Problem(StatusCodes.Status409Conflict, "refused", "This connection began under a runtime certificate that has since been replaced; connect again.");
        }

        RuntimeIdentityProof proof = host.Identity.Prove(host.Service.Session.Manifest.Estate, certificate, RuntimeIdentityScheme.CertificateFingerprint(client.RawData), nonce!);
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
